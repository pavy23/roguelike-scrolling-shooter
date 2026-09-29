using Shmup.Core.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// Input System에서 입력을 **샘플링만** 한다. 이동량·발사 가능 여부 같은 판정은
    /// 전부 Shmup.Core가 하고, 여기서는 한 틱 분량의 InputCommand로 포장해 넘긴다.
    ///
    /// 샘플링은 Update(가변 프레임), 소비는 BattleDirector.FixedUpdate(고정 60Hz)에서 일어난다.
    /// 한 프레임 안에 FixedUpdate가 0번 도는 경우에도 눌림이 유실되지 않도록
    /// "이번 프레임에 눌렸다"를 래치해 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] InputActionAsset _actions;
        [SerializeField] string _actionMapName = "Player";
        [SerializeField] string _moveActionName = "Move";
        [SerializeField] string _activateActionName = "Activate";

        [Tooltip("아날로그 스틱을 디지털 8방향으로 바꿀 때의 임계값.")]
        [SerializeField, Range(0.05f, 0.95f)] float _deadZone = 0.4f;

        /// <summary>옵션 화면(리바인딩)용 읽기 접근자.</summary>
        public InputActionAsset Actions => _actions;
        public string ActivateActionName => _activateActionName;

        InputAction _moveAction;
        InputAction _activateAction;

        Vector2 _move;
        bool _activateHeld;
        bool _activatePressedThisFrame;
        bool _bombPressedThisFrame;

        void Awake()
        {
            if (_actions == null)
            {
                Debug.LogError($"[{nameof(PlayerInputReader)}] InputActionAsset이 비어 있다. " +
                               "인스펙터에서 Assets/Settings/InputSystem_Actions를 지정해라.");
                enabled = false;
                return;
            }

            var map = _actions.FindActionMap(_actionMapName, throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogError($"[{nameof(PlayerInputReader)}] 액션 맵 '{_actionMapName}'을 찾을 수 없다.");
                enabled = false;
                return;
            }

            _moveAction = map.FindAction(_moveActionName, throwIfNotFound: false);
            // 게이지 활성화 (REQ-019). 액션이 없는 구 에셋이면 직접 키 샘플링으로 폴백.
            _activateAction = map.FindAction(_activateActionName, throwIfNotFound: false);
            if (_moveAction == null)
            {
                Debug.LogError($"[{nameof(PlayerInputReader)}] '{_moveActionName}' 액션이 없다.");
                enabled = false;
            }
        }

        void OnEnable()
        {
            _moveAction?.Enable();
            _activateAction?.Enable();
        }

        void OnDisable()
        {
            _moveAction?.Disable();
            _activateAction?.Disable();
            _move = Vector2.zero;
        }

        void Update()
        {
            _move = _moveAction.ReadValue<Vector2>();

            // 모바일 터치 조작 (원격 플레이). 이동은 아날로그 델타 경로로 따로 넘어가므로
            // 여기서는 버튼만 합친다. 시뮬은 InputCommand만 보므로 입력원이 달라도
            // 결정론에 영향이 없다.
            var touch = TouchControls.Instance;
            if (touch != null && touch.Active)
            {
                if (touch.ConsumeActivate()) _activatePressedThisFrame = true;
            }

            // 게이지 활성화 (REQ-019): Activate 액션(기본 X / 패드 Y) — 리바인딩 가능.
            // 액션이 없는 구 에셋에서는 하드코딩 키로 폴백한다. 상승 에지 판정은 Core가 한다.
            if (_activateAction != null)
            {
                _activateHeld = _activateAction.IsPressed();
                if (_activateAction.WasPressedThisFrame()) _activatePressedThisFrame = true;
            }
            else
            {
                var keyboard = Keyboard.current;
                var gamepad = Gamepad.current;
                _activateHeld = (keyboard != null && keyboard.xKey.isPressed)
                             || (gamepad != null && gamepad.buttonNorth.isPressed);
                if ((keyboard != null && keyboard.xKey.wasPressedThisFrame)
                    || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame))
                    _activatePressedThisFrame = true;
            }

            // 전멸 폭탄 (REQ-046). X는 이미 게이지 활성화가 쓰므로 B(bomb) / 패드 East로
            // 둔다. InputActions 에셋에 액션이 없어 저수준으로 읽는다 — 리바인딩이
            // 필요해지면 액션으로 승격하는 편이 낫다.
            var bombKeyboard = Keyboard.current;
            var bombGamepad = Gamepad.current;
            if ((bombKeyboard != null && bombKeyboard.bKey.wasPressedThisFrame)
                || (bombGamepad != null && bombGamepad.buttonEast.wasPressedThisFrame))
                _bombPressedThisFrame = true;
        }

        /// <summary>데모 영상 녹화용 오토파일럿 (dev 전용 — 사인 이동 + 연사 + 주기 활성화).</summary>
        public static bool AutopilotEnabled;

        /// <summary>조작 회귀 추적용 진단 문자열. 원인 확정 후 제거한다.</summary>
        public static string DebugState = "(no command yet)";

        // Always-fire policy (user decision, 2026-07-31). Legacy rss.autofire saves
        // and Attack binding overrides cannot disable firing, even before Awake runs.
        public static bool AutoFire => true;

        /// <summary>한 틱 분량의 입력을 만들어 반환하고 눌림 래치를 소모한다.</summary>
        public InputCommand ConsumeCommand()
        {
            if (AutopilotEnabled)
            {
                float t = Time.time;
                int moveY = Mathf.Sin(t * 1.1f) > 0.25f ? 1 : (Mathf.Sin(t * 1.1f) < -0.25f ? -1 : 0);
                int moveX = Mathf.Sin(t * 0.4f) > 0.5f ? 1 : (Mathf.Sin(t * 0.4f) < -0.6f ? -1 : 0);
                bool activate = Mathf.Repeat(t, 9f) < 0.1f;   // ~9초마다 게이지 활성화
                return new InputCommand(moveX, moveY, true, activate);
            }
            if (!enabled) return InputCommand.None;

            bool fire = AutoFire;
            bool activateGauge = _activateHeld || _activatePressedThisFrame;

            // 손가락이 화면에 닿아 있는 동안만 아날로그 경로를 쓴다 (REQ-045). Core는
            // 아날로그가 0/0이어도 디지털보다 우선하므로, 드래그 중이 아닐 때 이 경로를
            // 타면 키보드·패드 입력이 통째로 무시된다 — 데스크톱 WebGL에서도 터치 UI가
            // 켜져 있으니 이 구분이 필요하다.
            var touch = TouchControls.Instance;
            // 전멸 폭탄: 터치 버튼 래치와 키보드/패드 래치를 OR. 여기서 직접
            // wasPressedThisFrame을 읽으면 이 프레임에 FixedUpdate가 돌지 않을 때
            // 눌림이 통째로 유실된다 — 발사·활성화가 래치를 쓰는 이유와 같다.
            bool activateBomb = _bombPressedThisFrame;
            if (BombButton.Instance != null && BombButton.Instance.ConsumePress())
                activateBomb = true;

            InputCommand command;
            if (touch != null && touch.Active && touch.IsDragging)
            {
                touch.ConsumeAnalogDelta(out int deltaX, out int deltaY);
                command = InputCommand.Analog(deltaX, deltaY, fire, activateGauge, activateBomb);
            }
            else
            {
                command = new InputCommand(
                    Digital(_move.x), Digital(_move.y), fire, activateGauge, activateBomb);
            }

            // 조작 회귀 추적용 진단 문자열 (원인 확정 후 제거). 실제로 시뮬에 넘어가는
            // 값을 그대로 찍어야 어디서 끊기는지 알 수 있다.
            DebugState =
                $"rdr={(enabled ? "on" : "OFF")} kb=({_move.x:0.0},{_move.y:0.0}) "
                + $"mode={(command.UseAnalogMovement ? "ANALOG" : "digital")} "
                + $"d=({command.AnalogDeltaXSubUnits},{command.AnalogDeltaYSubUnits}) "
                + $"mv=({command.MoveX},{command.MoveY})";

            _activatePressedThisFrame = false;
            _bombPressedThisFrame = false;
            return command;
        }

        int Digital(float axis)
        {
            if (axis <= -_deadZone) return -1;
            if (axis >= _deadZone) return 1;
            return 0;
        }
    }
}

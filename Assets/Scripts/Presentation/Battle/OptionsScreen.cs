using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 옵션 화면 (UGUI + 픽셀 폰트): 해상도·전체화면·리바인딩·접근성 토글.
    /// 일시정지 중 O/(Select)로 연다. 커서형 내비게이션(↑↓/(dpad) 이동, Enter/(A) 실행,
    /// 해상도는 ←→ 순환) + 기존 키보드 단축키 병행. 설정은 PlayerPrefs 저장.
    /// 리바인딩은 Input System 바인딩 오버라이드(JSON) — 시뮬에는 영향 없음.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OptionsScreen : MonoBehaviour
    {
        const string ResolutionPrefKey = "rss.resolution";
        const string FullscreenPrefKey = "rss.fullscreen";
        const string BindingsPrefKey = "rss.bindings";

        static readonly Vector2Int[] Resolutions =
        {
            new Vector2Int(1152, 672),
            new Vector2Int(1280, 720),
            new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1440)
        };

        enum Item
        {
            Resolution = 0, Fullscreen, RebindActivate,
            RebindUp, RebindDown, RebindLeft, RebindRight,
            ResetBindings, ScreenShake, ReduceFlash, Close
        }
        const int ItemCount = (int)Item.Close + 1;

        /// <summary>PauseScreen이 입력 충돌(볼륨 화살표 등)을 피하기 위한 상태 공유.</summary>
        public static bool IsOpen { get; private set; }

        [SerializeField] PlayerInputReader _input;
        [SerializeField] JuiceDirector _juice;
        [SerializeField] Font _font;
        [SerializeField] Font _fontBold;

        /// <summary>
        /// 터치 기기에서 의미가 있는 항목만. 해상도·전체화면은 웹/폰에서 캔버스 크기를 건드려
        /// 레이아웃을 깨뜨리고, 리바인딩은 물리 키가 없으면 쓸 수 없다.
        /// </summary>
        static readonly Item[] TouchItems =
        {
            Item.ScreenShake, Item.ReduceFlash, Item.Close
        };

        bool _open;
        int _cursor;
        int _resolutionIndex;
        InputActionRebindingExtensions.RebindingOperation _rebind;

        GameObject _root;
        Text _bodyText;
        string _panelText;
        Text[] _touchLabels;
        GameObject _openButtonRoot;

        void Start()
        {
            _resolutionIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(ResolutionPrefKey, 0), 0, Resolutions.Length - 1);
            bool fullscreen = PlayerPrefs.GetInt(FullscreenPrefKey, 0) == 1;
            // 에디터·웹·모바일에서는 창 크기를 여기서 건드리지 않는다 (CanSetResolution 참고)
            Apply(fullscreen);
            LoadBindings();

            bool touch = UiPlatform.TouchMode;
            var canvas = UiKit.CreateCanvas("OptionsCanvas", 85);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;
            UiKit.CreateDim(canvas.transform, new Color(0f, 0.01f, 0.05f, 0.7f));
            var panel = UiKit.CreatePanel(canvas.transform,
                touch ? new Vector2(300f, 200f) : new Vector2(360f, 238f));
            UiKit.CreateCornerText(panel, _fontBold, UiText.OptionsTitle, 16, UiKit.TextMain,
                new Vector2(0.5f, 1f), new Vector2(0f, -10f), TextAnchor.UpperCenter, "Title");
            _bodyText = UiKit.CreateTextStretch(panel, _font, "", 11,
                UiKit.TextMain, TextAnchor.UpperLeft, 14f, "Body");
            _bodyText.rectTransform.offsetMax = new Vector2(-14f, -42f);

            if (touch)
            {
                // 항목 하나가 텍스트 한 줄이던 것을 각각 눌리는 행으로 바꾼다.
                _bodyText.gameObject.SetActive(false);
                _touchLabels = new Text[TouchItems.Length];
                for (int i = 0; i < TouchItems.Length; i++)
                {
                    var item = TouchItems[i];
                    bool isClose = item == Item.Close;
                    var button = UiKit.CreateTouchButton(panel, _font, "", 11,
                        new Vector2(0.5f, 1f), new Vector2(0f, -40f - i * 40f),
                        new Vector2(260f, 36f), () => ActivateItem(item),
                        $"Option{item}", accent: isClose);
                    _touchLabels[i] = button.GetComponentInChildren<Text>();
                }

                // 일시정지 화면에서 옵션으로 들어갈 입구. 옵션 캔버스는 열렸을 때만 켜지므로
                // 입구는 별도 캔버스에 둔다 (일시정지 패널 위, 옵션 패널 아래).
                var openCanvas = UiKit.CreateCanvas("OptionsOpenCanvas", 84);
                openCanvas.transform.SetParent(transform, false);
                _openButtonRoot = openCanvas.gameObject;
                UiKit.CreateTouchButton(openCanvas.transform, _font, "OPTIONS", 11,
                    new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(120f, 34f),
                    () => _open = true, "OpenOptions");
                _openButtonRoot.SetActive(false);
            }

            _root.SetActive(false);
        }

        void RefreshTouchLabels()
        {
            if (_touchLabels == null) return;
            bool shakeOn = PlayerPrefs.GetInt(JuiceDirector.ShakePrefKey, 1) == 1;
            bool flashReduce = PlayerPrefs.GetInt(JuiceDirector.FlashReducePrefKey, 0) == 1;
            for (int i = 0; i < TouchItems.Length; i++)
            {
                var label = _touchLabels[i];
                if (label == null) continue;
                switch (TouchItems[i])
                {
                    case Item.ScreenShake:
                        label.text = $"SCREEN SHAKE   {(shakeOn ? "ON" : "OFF")}";
                        break;
                    case Item.ReduceFlash:
                        label.text = $"REDUCE FLASH   {(flashReduce ? "ON" : "OFF")}";
                        break;
                    case Item.Close:
                        label.text = "CLOSE";
                        break;
                }
            }
        }

        void OnDestroy()
        {
            IsOpen = false;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            // 폰에는 키보드도 패드도 없다 — 여기서 일찍 리턴하면 터치 입구가 아예 안 뜬다.
            var gamepad = Gamepad.current;

            // 리바인딩 대기 중에는 다른 입력 처리를 멈춘다
            if (_rebind != null)
            {
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) CancelRebind();
                SetVisible(true, UiText.RebindPrompt);
                return;
            }

            bool toggle = (keyboard != null && keyboard.oKey.wasPressedThisFrame)
                       || (gamepad != null && gamepad.selectButton.wasPressedThisFrame);
            if (toggle && Time.timeScale == 0f)
                _open = !_open;
            if (Time.timeScale != 0f) _open = false;   // 일시정지 해제 시 자동 닫힘
            IsOpen = _open;

            // 옵션 입구는 일시정지 중, 옵션이 닫혀 있을 때만 보인다.
            if (_openButtonRoot != null)
            {
                bool showOpenButton = Time.timeScale == 0f && !_open;
                if (_openButtonRoot.activeSelf != showOpenButton)
                    _openButtonRoot.SetActive(showOpenButton);
            }

            if (!_open)
            {
                SetVisible(false, null);
                return;
            }

            if (_touchLabels != null)
            {
                RefreshTouchLabels();
                SetVisible(true, null);
                return;   // 터치 모드는 행 버튼이 전부 처리한다
            }

            // 커서 이동
            int move = 0;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame) move = -1;
                if (keyboard.downArrowKey.wasPressedThisFrame) move = 1;
            }
            if (gamepad != null)
            {
                if (gamepad.dpad.up.wasPressedThisFrame) move = -1;
                if (gamepad.dpad.down.wasPressedThisFrame) move = 1;
            }
            if (move != 0)
            {
                _cursor = (_cursor + move + ItemCount) % ItemCount;
                _panelText = null;
            }

            // 좌우: 해상도 항목 순환
            int adjust = 0;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame) adjust = -1;
                if (keyboard.rightArrowKey.wasPressedThisFrame) adjust = 1;
            }
            if (gamepad != null)
            {
                if (gamepad.dpad.left.wasPressedThisFrame) adjust = -1;
                if (gamepad.dpad.right.wasPressedThisFrame) adjust = 1;
            }
            if (adjust != 0 && (Item)_cursor == Item.Resolution)
            {
                _resolutionIndex = (_resolutionIndex + adjust + Resolutions.Length) % Resolutions.Length;
                Apply(Screen.fullScreen);
                _panelText = null;
            }

            bool confirm = (keyboard != null && keyboard.enterKey.wasPressedThisFrame)
                        || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (confirm)
                ActivateItem((Item)_cursor);

            // 키보드 단축키 병행 (기존 사용자 습관 유지)
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) ActivateItem(Item.Resolution);
                if (keyboard.fKey.wasPressedThisFrame) ActivateItem(Item.Fullscreen);
                if (keyboard.nKey.wasPressedThisFrame) ActivateItem(Item.RebindActivate);
                if (keyboard.digit1Key.wasPressedThisFrame) ActivateItem(Item.RebindUp);
                if (keyboard.digit2Key.wasPressedThisFrame) ActivateItem(Item.RebindDown);
                if (keyboard.digit3Key.wasPressedThisFrame) ActivateItem(Item.RebindLeft);
                if (keyboard.digit4Key.wasPressedThisFrame) ActivateItem(Item.RebindRight);
                if (keyboard.xKey.wasPressedThisFrame) ActivateItem(Item.ResetBindings);
                if (keyboard.sKey.wasPressedThisFrame) ActivateItem(Item.ScreenShake);
                if (keyboard.gKey.wasPressedThisFrame) ActivateItem(Item.ReduceFlash);
            }

            RefreshPanelText();
            SetVisible(true, _panelText);
        }

        void ActivateItem(Item item)
        {
            switch (item)
            {
                case Item.Resolution:
                    _resolutionIndex = (_resolutionIndex + 1) % Resolutions.Length;
                    Apply(Screen.fullScreen);
                    break;
                case Item.Fullscreen:
                    Apply(!Screen.fullScreen);
                    break;
                case Item.RebindActivate: StartRebindActivate(); break;
                case Item.RebindUp: StartRebindMovePart("up"); break;
                case Item.RebindDown: StartRebindMovePart("down"); break;
                case Item.RebindLeft: StartRebindMovePart("left"); break;
                case Item.RebindRight: StartRebindMovePart("right"); break;
                case Item.ResetBindings: ResetBindings(); break;
                case Item.ScreenShake:
                    ToggleAccessibilityPref(JuiceDirector.ShakePrefKey, 1);
                    break;
                case Item.ReduceFlash:
                    ToggleAccessibilityPref(JuiceDirector.FlashReducePrefKey, 0);
                    break;
                case Item.Close:
                    _open = false;
                    break;
            }
            _panelText = null;
        }

        void SetVisible(bool visible, string body)
        {
            if (_root == null) return;
            if (_root.activeSelf != visible)
                _root.SetActive(visible);
            if (visible && _bodyText != null && body != null && _bodyText.text != body)
                _bodyText.text = body;
        }

        void RefreshPanelText()
        {
            if (_panelText != null) return;
            var resolution = Resolutions[_resolutionIndex];
            bool shakeOn = PlayerPrefs.GetInt(JuiceDirector.ShakePrefKey, 1) == 1;
            bool flashReduce = PlayerPrefs.GetInt(JuiceDirector.FlashReducePrefKey, 0) == 1;

            var sb = new System.Text.StringBuilder(512);
            AppendItem(sb, Item.Resolution, $"RESOLUTION   ◄ {resolution.x} x {resolution.y} ►");
            AppendItem(sb, Item.Fullscreen, $"FULLSCREEN   {(Screen.fullScreen ? "ON" : "OFF")}");
            var activate = FindActivateAction();
            string activateBinding = activate != null
                ? InputControlPath.ToHumanReadableString(
                    activate.bindings[0].effectivePath,
                    InputControlPath.HumanReadableStringOptions.OmitDevice)
                : "?";
            AppendItem(sb, Item.RebindActivate, $"REBIND ACTIVATE  (now: {activateBinding})");
            AppendItem(sb, Item.RebindUp, "REBIND MOVE UP");
            AppendItem(sb, Item.RebindDown, "REBIND MOVE DOWN");
            AppendItem(sb, Item.RebindLeft, "REBIND MOVE LEFT");
            AppendItem(sb, Item.RebindRight, "REBIND MOVE RIGHT");
            AppendItem(sb, Item.ResetBindings, "RESET BINDINGS");
            AppendItem(sb, Item.ScreenShake, $"SCREEN SHAKE   {(shakeOn ? "ON" : "OFF")}");
            AppendItem(sb, Item.ReduceFlash, $"REDUCE FLASH   {(flashReduce ? "ON" : "OFF")}");
            AppendItem(sb, Item.Close, "CLOSE  [O]/(Select)");
            sb.Append("\n\nWEAPONS FIRE AUTOMATICALLY");
            _panelText = sb.ToString();
        }

        void AppendItem(System.Text.StringBuilder sb, Item item, string label)
        {
            sb.Append((int)item == _cursor ? "▶ " : "   ");
            sb.Append(label);
            if (item != Item.Close) sb.Append('\n');
        }

        /// <summary>
        /// 창 해상도를 이쪽에서 정할 수 있는 플랫폼인지. WebGL에서 SetResolution은 곧
        /// 캔버스 크기 변경이라, 페이지가 잡아 놓은 렌더 타겟(640×360)을 덮어써서
        /// 종횡비가 어긋나고 HUD가 화면 밖으로 밀려난다. 모바일도 창 개념이 없다.
        /// </summary>
        static bool CanSetResolution =>
            !Application.isEditor
            && Application.platform != RuntimePlatform.WebGLPlayer
            && !Application.isMobilePlatform;

        void Apply(bool fullscreen)
        {
            var resolution = Resolutions[_resolutionIndex];
            if (CanSetResolution)
                Screen.SetResolution(resolution.x, resolution.y, fullscreen);
            PlayerPrefs.SetInt(ResolutionPrefKey, _resolutionIndex);
            PlayerPrefs.SetInt(FullscreenPrefKey, fullscreen ? 1 : 0);
            SaveFlush.Request();   // WebGL IDBFS 플러시 (그 외 no-op)
        }

        void ToggleAccessibilityPref(string key, int defaultValue)
        {
            int next = PlayerPrefs.GetInt(key, defaultValue) == 1 ? 0 : 1;
            PlayerPrefs.SetInt(key, next);
            SaveFlush.Request();
            if (_juice != null) _juice.ReloadPrefs();
            _panelText = null;
        }

        InputAction FindActivateAction()
        {
            if (_input == null || _input.Actions == null) return null;
            return _input.Actions.FindAction(_input.ActivateActionName, throwIfNotFound: false);
        }

        void StartRebindActivate()
        {
            var activate = FindActivateAction();
            if (activate == null) return;
            StartRebind(activate, -1);
        }

        /// <summary>Move 2D 컴포지트의 키보드 파트(up/down/left/right)를 리바인딩한다.</summary>
        void StartRebindMovePart(string partName)
        {
            if (_input == null || _input.Actions == null) return;
            var move = _input.Actions.FindAction("Move", throwIfNotFound: false);
            if (move == null) return;
            for (int i = 0; i < move.bindings.Count; i++)
            {
                var binding = move.bindings[i];
                if (binding.isPartOfComposite
                    && string.Equals(binding.name, partName, System.StringComparison.OrdinalIgnoreCase)
                    && binding.path.StartsWith("<Keyboard>", System.StringComparison.Ordinal))
                {
                    StartRebind(move, i);
                    return;
                }
            }
        }

        void StartRebind(InputAction action, int bindingIndex)
        {
            action.Disable();
            var operation = bindingIndex >= 0
                ? action.PerformInteractiveRebinding(bindingIndex)
                : action.PerformInteractiveRebinding();
            _rebind = operation
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(_ => FinishRebind(action))
                .OnCancel(_ => FinishRebind(action))
                .Start();
        }

        void FinishRebind(InputAction fire)
        {
            _rebind?.Dispose();
            _rebind = null;
            fire.Enable();
            if (_input != null && _input.Actions != null)
                PlayerPrefs.SetString(BindingsPrefKey, _input.Actions.SaveBindingOverridesAsJson());
            SaveFlush.Request();
            _panelText = null;   // 바인딩 표시 갱신
        }

        void CancelRebind()
        {
            _rebind?.Cancel();
        }

        void ResetBindings()
        {
            if (_input == null || _input.Actions == null) return;
            _input.Actions.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(BindingsPrefKey);
            SaveFlush.Request();
            _panelText = null;   // 바인딩 표시 갱신
        }

        void LoadBindings()
        {
            if (_input == null || _input.Actions == null) return;
            string json = PlayerPrefs.GetString(BindingsPrefKey, null);
            if (!string.IsNullOrEmpty(json))
                _input.Actions.LoadBindingOverridesFromJson(json);
        }
    }
}

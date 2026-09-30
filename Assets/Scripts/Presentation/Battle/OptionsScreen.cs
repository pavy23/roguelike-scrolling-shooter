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
            ResetBindings, ScreenShake, ReduceFlash, ReplayGuide, Close
        }
        const int ItemCount = (int)Item.Close + 1;

        /// <summary>PauseScreen이 입력 충돌(볼륨 화살표 등)을 피하기 위한 상태 공유.</summary>
        public static bool IsOpen { get; private set; }
        static int _pauseInputConsumedFrame = -1;
        public static bool BlocksPauseInput => IsOpen || _pauseInputConsumedFrame == Time.frameCount;

        [SerializeField] PlayerInputReader _input;
        [SerializeField] JuiceDirector _juice;
        [SerializeField] Font _font;
        [SerializeField] Font _fontBold;
        OnboardingHints _onboarding;

        /// <summary>
        /// 터치 기기에서 의미가 있는 항목만. 해상도·전체화면은 웹/폰에서 캔버스 크기를 건드려
        /// 레이아웃을 깨뜨리고, 리바인딩은 물리 키가 없으면 쓸 수 없다.
        /// </summary>
        static readonly Item[] TouchItems =
        {
            Item.ScreenShake, Item.ReduceFlash, Item.ReplayGuide, Item.Close
        };

        bool _open;
        int _cursor;
        int _resolutionIndex;
        InputActionRebindingExtensions.RebindingOperation _rebind;
        bool _rebindWasEnabled;
        string _rebindPrompt;
        int _rebindFinishedFrame = -1;

        GameObject _root;
        Text _bodyText;
        string _panelText;
        Text[] _touchLabels;
        GameObject _openButtonRoot;

        void Start()
        {
            _onboarding = GetComponent<OnboardingHints>();
            _resolutionIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(ResolutionPrefKey, 0), 0, Resolutions.Length - 1);
            bool fullscreen = PlayerPrefs.GetInt(FullscreenPrefKey, 0) == 1;
            // 에디터·웹·모바일에서는 창 크기를 여기서 건드리지 않는다 (CanSetResolution 참고)
            Apply(fullscreen);

            bool touch = UiPlatform.TouchMode;
            var canvas = UiKit.CreateCanvas("OptionsCanvas", 85);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;
            UiKit.CreateDim(canvas.transform, new Color(0f, 0.01f, 0.05f, 0.7f)).raycastTarget = true;
            var panel = UiKit.CreatePanel(canvas.transform,
                touch ? new Vector2(300f, 230f) : new Vector2(360f, 274f));
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
                    () => SetOpen(true), "OpenOptions");
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
                    case Item.ReplayGuide:
                        label.text = UiText.ReplayGuide;
                        break;
                }
            }
        }

        void OnDestroy()
        {
            CancelRebind();
            IsOpen = false;
        }

        void LateUpdate()
        {
            // Audio can open in another component's Update. Refresh even when its
            // input guard skipped our Update, so this entry never sits behind it.
            if (_openButtonRoot == null) return;
            bool show = Time.timeScale == 0f && !_open && !AudioSettingsPanel.BlocksInput;
            if (_openButtonRoot.activeSelf != show) _openButtonRoot.SetActive(show);
        }

        void SetOpen(bool open)
        {
            _pauseInputConsumedFrame = Time.frameCount;
            if (!open) CancelRebind();
            _open = open;
            IsOpen = open;
            _panelText = null;
            if (!open) SetVisible(false, null);
            UiAudio.Play(open ? UiCue.Confirm : UiCue.Back);
        }

        void Update()
        {
            if (AudioSettingsPanel.BlocksInput) return;
            var keyboard = Keyboard.current;
            // 폰에는 키보드도 패드도 없다 — 여기서 일찍 리턴하면 터치 입구가 아예 안 뜬다.
            var gamepad = Gamepad.current;

            // A key captured/cancelled by the rebind must not also invoke a menu shortcut.
            if (_rebindFinishedFrame == Time.frameCount) return;
            if (_rebind != null)
            {
                if (Time.timeScale != 0f) SetOpen(false);
                else
                {
                    if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) CancelRebind();
                    SetVisible(true, _rebind != null ? _rebindPrompt : "KEY CHANGE CANCELLED");
                }
                return;
            }

            bool toggle = (keyboard != null && keyboard.oKey.wasPressedThisFrame)
                       || (gamepad != null && gamepad.selectButton.wasPressedThisFrame);
            if (toggle && Time.timeScale == 0f)
                SetOpen(!_open);
            if (_open && ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame))) SetOpen(false);
            if (Time.timeScale != 0f && _open) SetOpen(false);   // 일시정지 해제 시 자동 닫힘
            IsOpen = _open;

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
                UiAudio.Play(UiCue.Navigate);
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
                UiAudio.Play(UiCue.Navigate);
            }

            bool confirm = (keyboard != null && keyboard.enterKey.wasPressedThisFrame)
                        || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (confirm) ActivateItem((Item)_cursor);
            if (!_open || _rebind != null) return;

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

            if (!_open || _rebind != null) return;
            RefreshPanelText();
            SetVisible(true, _panelText);
        }

        void ActivateItem(Item item)
        {
            UiAudio.Play(UiCue.Confirm);
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
                    SetOpen(false);
                    break;
                case Item.ReplayGuide:
                    if (_onboarding != null) _onboarding.RestartGuide();
                    SetOpen(false);
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
            string activateBinding = activate != null ? PlayerBindings.KeyboardLabel(activate) : "UNAVAILABLE";
            AppendItem(sb, Item.RebindActivate, $"ACTIVATE KEY   {activateBinding}");
            var move = _input != null ? _input.MoveAction : null;
            AppendItem(sb, Item.RebindUp, $"MOVE UP KEY    {PlayerBindings.KeyboardLabel(move, "up")}");
            AppendItem(sb, Item.RebindDown, $"MOVE DOWN KEY  {PlayerBindings.KeyboardLabel(move, "down")}");
            AppendItem(sb, Item.RebindLeft, $"MOVE LEFT KEY  {PlayerBindings.KeyboardLabel(move, "left")}");
            AppendItem(sb, Item.RebindRight, $"MOVE RIGHT KEY {PlayerBindings.KeyboardLabel(move, "right")}");
            AppendItem(sb, Item.ResetBindings, "RESET BINDINGS");
            AppendItem(sb, Item.ScreenShake, $"SCREEN SHAKE   {(shakeOn ? "ON" : "OFF")}");
            AppendItem(sb, Item.ReduceFlash, $"REDUCE FLASH   {(flashReduce ? "ON" : "OFF")}");
            AppendItem(sb, Item.ReplayGuide, UiText.ReplayGuide);
            AppendItem(sb, Item.Close, "BACK  ESC / B");
            sb.Append("\n\nKEYBOARD KEYS ONLY / ARROWS ALSO MOVE\nWEAPONS FIRE AUTOMATICALLY");
            _panelText = sb.ToString();
        }

        void AppendItem(System.Text.StringBuilder sb, Item item, string label)
        {
            bool selected = (int)item == _cursor;
            if (selected) sb.Append("<color=#FFB31C>▶ ");
            else sb.Append("   ");
            sb.Append(label);
            if (selected) sb.Append("</color>");
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

        InputAction FindActivateAction() => _input != null ? _input.ActivateAction : null;

        void StartRebindActivate()
        {
            var action = FindActivateAction();
            StartRebind(action, PlayerBindings.KeyboardIndex(action));
        }

        void StartRebindMovePart(string partName)
        {
            var action = _input != null ? _input.MoveAction : null;
            StartRebind(action, PlayerBindings.KeyboardIndex(action, partName));
        }

        void StartRebind(InputAction action, int bindingIndex)
        {
            if (_rebind != null || action == null || bindingIndex < 0) return;
            _rebindWasEnabled = action.enabled;
            action.Disable();
            _rebindPrompt = "PRESS A KEYBOARD KEY\nESC CANCEL / B RESERVED FOR BOMB";
            _rebind = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsHavingToMatchPath("<Keyboard>/*")
                .WithExpectedControlType("Button")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnPotentialMatch(operation =>
                {
                    string conflict = BindingConflict(action, bindingIndex, operation.selectedControl);
                    if (conflict != null)
                    {
                        _rebindPrompt = conflict + "\nCHOOSE ANOTHER KEY / ESC CANCEL";
                        UiAudio.Play(UiCue.Reject);
                        operation.RemoveCandidate(operation.selectedControl);
                    }
                    else operation.Complete();
                })
                .OnComplete(_ => FinishRebind(action, true))
                .OnCancel(_ => FinishRebind(action, false));
            _rebind.Start();
            SetVisible(true, _rebindPrompt);
        }

        string BindingConflict(InputAction target, int index, InputControl control)
        {
            if (control == null) return "CHOOSE A KEYBOARD KEY";
            if (InputControlPath.Matches("<Keyboard>/b", control)) return "B IS RESERVED FOR BOMB";
            if (_input == null) return null;
            foreach (var action in new[] { _input.MoveAction, _input.ActivateAction })
            {
                if (action == null) continue;
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    if (binding.isComposite || (action == target && (i == index
                        || (binding.isPartOfComposite && binding.name == target.bindings[index].name)))) continue;
                    if (!string.IsNullOrEmpty(binding.effectivePath)
                        && InputControlPath.Matches(binding.effectivePath, control))
                        return "KEY IN USE: " + action.name.ToUpperInvariant();
                }
            }
            return null;
        }

        void FinishRebind(InputAction action, bool save)
        {
            _rebind?.Dispose();
            _rebind = null;
            if (_rebindWasEnabled) action.Enable();
            _rebindFinishedFrame = Time.frameCount;
            _pauseInputConsumedFrame = Time.frameCount;
            if (save && _input != null) PlayerBindings.Save(_input.Actions);
            UiAudio.Play(save ? UiCue.Confirm : UiCue.Back);
            _panelText = null;
        }

        void CancelRebind() => _rebind?.Cancel();

        void ResetBindings()
        {
            if (_input != null) PlayerBindings.Reset(_input.Actions);
            _panelText = null;
        }
    }
}

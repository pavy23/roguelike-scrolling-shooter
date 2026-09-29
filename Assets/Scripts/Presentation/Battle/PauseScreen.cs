using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// ESC/패드 Start 일시정지 (UGUI + 픽셀 폰트). Time.timeScale=0으로 시뮬 틱을 멈출 뿐이라
    /// 결정론에 영향 없다. 볼륨은 AudioListener.volume 전역 값 + PlayerPrefs 저장.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseScreen : MonoBehaviour
    {
        const string VolumePrefKey = "rss.volume";

        [SerializeField] Font _font;
        [SerializeField] Font _fontBold;
        [SerializeField] BattleDirector _director;

        bool _paused;
        GameObject _root;
        Text _volumeText;
        int _lastVolumePercent = -1;

        void Start()
        {
            AudioListener.volume = PlayerPrefs.GetFloat(VolumePrefKey, 1f);

            var canvas = UiKit.CreateCanvas("PauseCanvas", 80);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;

            bool touch = UiPlatform.TouchMode;
            UiKit.CreateDim(canvas.transform, new Color(0f, 0.01f, 0.05f, 0.62f));
            // 터치 모드에서는 버튼 줄이 들어가므로 패널을 키운다.
            var panel = UiKit.CreatePanel(canvas.transform,
                touch ? new Vector2(300f, 150f) : new Vector2(300f, 110f));
            UiKit.CreateCornerText(panel, _fontBold, UiText.PauseTitle, 20, UiKit.TextMain,
                new Vector2(0.5f, 1f), new Vector2(0f, -12f), TextAnchor.UpperCenter, "Title");
            _volumeText = UiKit.CreateCornerText(panel, _font, "", 11, UiKit.TextMain,
                new Vector2(0.5f, 1f), new Vector2(0f, -46f), TextAnchor.UpperCenter, "Volume");

            if (touch)
            {
                // 픽셀 폰트에 없는 글리프(− 등)는 빈칸으로 렌더되므로 ASCII만 쓴다.
                UiKit.CreateTouchButton(panel, _fontBold, "-", 18,
                    new Vector2(0.5f, 1f), new Vector2(-118f, -38f), new Vector2(34f, 32f),
                    () => AdjustVolume(-0.1f), "VolDown");
                UiKit.CreateTouchButton(panel, _fontBold, "+", 18,
                    new Vector2(0.5f, 1f), new Vector2(118f, -38f), new Vector2(34f, 32f),
                    () => AdjustVolume(0.1f), "VolUp");
                UiKit.CreateTouchButton(panel, _font, "RESUME", 11,
                    new Vector2(0.5f, 0f), new Vector2(-62f, 14f), new Vector2(104f, 36f),
                    () => SetPaused(false), "ResumeButton", accent: true);
                UiKit.CreateTouchButton(panel, _font, "QUIT", 11,
                    new Vector2(0.5f, 0f), new Vector2(62f, 14f), new Vector2(104f, 36f),
                    QuitToTitle, "QuitButton");

                // 폰에는 ESC가 없다 — 일시정지를 열 수단이 UI에 없으면 런에서 나갈 방법이 없다.
                var toggleCanvas = UiKit.CreateCanvas("PauseToggleCanvas", 79);
                toggleCanvas.transform.SetParent(transform, false);
                // **좌측 상단**이다 (사람 지시 2026-08-04). 상단 중앙은 보스 HP 바
                // 자리라 비켜야 했고, 좌하단으로 한 번 갔다가 좌상단으로 확정됐다.
                //
                // 여기 있던 진행도·계약 표시는 오른쪽으로 밀었다 (ProgressHud) —
                // 버튼은 손가락이 닿는 자리가 정해져 있지만 글자는 어디로든 갈 수 있다.
                var toggleButton = UiKit.CreateTouchButton(toggleCanvas.transform, _font, "PAUSE", 10,
                    new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(62f, 30f),
                    TogglePause, "PauseToggle");
                // 이 영역을 기체 드래그에서 빼 둔다 — 안 그러면 서로를 오작동시킨다.
                if (TouchControls.Instance != null)
                    TouchControls.Instance.ReserveRect(toggleButton.GetComponent<RectTransform>());
            }
            else
            {
                UiKit.CreateCornerText(panel, _font,
                    UiText.PauseHints, 11, UiKit.TextDim,
                    new Vector2(0.5f, 0f), new Vector2(0f, 14f), TextAnchor.LowerCenter, "Hints");
            }

            _root.SetActive(false);
        }

        /// <summary>화면 상단 ❙❙ 버튼용. 옵션이 열려 있으면 그쪽이 먼저 닫혀야 한다.</summary>
        void TogglePause()
        {
            if (OptionsScreen.BlocksPauseInput) return;
            SetPaused(!_paused);
        }

        void QuitToTitle()
        {
            if (_director != null)
                _director.SaveRunToDisk();   // 타이틀로 나가도 이어하기 가능 (REQ-017)
            SetPaused(false);
            SceneManager.LoadScene("Title");
        }

        void Update()
        {
            if (OptionsScreen.BlocksPauseInput) return;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            bool toggle = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                       || (gamepad != null && gamepad.startButton.wasPressedThisFrame);
            if (toggle)
                SetPaused(!_paused);
            if (!_paused) return;
            if (OptionsScreen.IsOpen) return;   // 옵션 내비게이션과 입력 충돌 방지

            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                    AdjustVolume(0.1f);
                if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
                    AdjustVolume(-0.1f);
                if (keyboard.qKey.wasPressedThisFrame)
                    QuitToTitle();
            }
            if (gamepad != null)
            {
                if (gamepad.dpad.up.wasPressedThisFrame || gamepad.dpad.right.wasPressedThisFrame)
                    AdjustVolume(0.1f);
                if (gamepad.dpad.down.wasPressedThisFrame || gamepad.dpad.left.wasPressedThisFrame)
                    AdjustVolume(-0.1f);
            }

            int volumePercent = (int)(AudioListener.volume * 100f);
            if (volumePercent != _lastVolumePercent && _volumeText != null)
            {
                _lastVolumePercent = volumePercent;
                _volumeText.text = string.Format(
                    UiPlatform.TouchMode ? UiText.VolumeFormatTouch : UiText.VolumeFormat,
                    volumePercent);
            }
        }

        void SetPaused(bool paused)
        {
            _paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            if (_root != null) _root.SetActive(paused);
        }

        static void AdjustVolume(float delta)
        {
            AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);
            PlayerPrefs.SetFloat(VolumePrefKey, AudioListener.volume);
            SaveFlush.Request();   // 볼륨 조절은 연타로 들어온다 — 디바운스가 모아서 한 번만 내린다
        }

        void OnDestroy()
        {
            // 씬 전환 시 잔류 방지
            if (_paused)
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }
    }
}

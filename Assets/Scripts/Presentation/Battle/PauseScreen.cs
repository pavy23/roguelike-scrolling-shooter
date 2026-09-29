using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>Pauses combat audio and simulation; the UI voice remains available.</summary>
    [DisallowMultipleComponent]
    public sealed class PauseScreen : MonoBehaviour
    {
        [SerializeField] Font _font;
        [SerializeField] Font _fontBold;
        [SerializeField] BattleDirector _director;
        bool _paused;
        GameObject _root;
        AudioSettingsPanel _audioSettings;
        Button _resumeButton;

        void Start()
        {
            _audioSettings = AudioSettingsPanel.Create(transform, _font, _fontBold);
            var canvas = UiKit.CreateCanvas("PauseCanvas", 80);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;
            bool touch = UiPlatform.TouchMode;
            UiKit.CreateDim(canvas.transform, new Color(0f, 0.01f, 0.05f, 0.72f)).raycastTarget = true;
            var panel = UiKit.CreatePanel(canvas.transform, new Vector2(300f, 160f));
            UiKit.CreateCornerText(panel, _fontBold, UiText.PauseTitle, 20, UiKit.TextMain,
                new Vector2(0.5f, 1f), new Vector2(0f, -12f), TextAnchor.UpperCenter, "Title");
            UiKit.CreateTouchButton(panel, _font, touch ? "AUDIO SETTINGS" : "AUDIO SETTINGS [V]", 11,
                new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(260f, 40f),
                OpenAudio, "AudioSettingsButton");
            _resumeButton = UiKit.CreateTouchButton(panel, _font, "RESUME", 11,
                new Vector2(0.5f, 0f), new Vector2(-62f, 14f), new Vector2(104f, 40f),
                () => SetPaused(false), "ResumeButton", accent: true);
            UiKit.CreateTouchButton(panel, _font, "QUIT", 11,
                new Vector2(0.5f, 0f), new Vector2(62f, 14f), new Vector2(104f, 40f),
                QuitToTitle, "QuitButton");

            if (touch)
            {
                var toggleCanvas = UiKit.CreateCanvas("PauseToggleCanvas", 79);
                toggleCanvas.transform.SetParent(transform, false);
                // Preserve the user's chosen upper-left pause entry point.
                var toggleButton = UiKit.CreateTouchButton(toggleCanvas.transform, _font, "PAUSE", 10,
                    new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(62f, 30f),
                    TogglePause, "PauseToggle");
                if (TouchControls.Instance != null)
                    TouchControls.Instance.ReserveRect(toggleButton.GetComponent<RectTransform>());
            }
            else
            {
                UiKit.CreateCornerText(panel, _font, UiText.PauseHints, 10, UiKit.TextDim,
                    new Vector2(0.5f, 0f), new Vector2(0f, -24f), TextAnchor.MiddleCenter, "Hints");
            }
            _root.SetActive(false);
        }

        void OpenAudio()
        {
            if (_paused && !OptionsScreen.BlocksPauseInput) _audioSettings.Open();
        }

        void TogglePause()
        {
            if (OptionsScreen.BlocksPauseInput || AudioSettingsPanel.BlocksInput) return;
            SetPaused(!_paused);
        }

        void QuitToTitle()
        {
            if (_director != null) _director.SaveRunToDisk();
            SetPaused(false);
            SceneManager.LoadScene("Title");
        }

        void Update()
        {
            if (OptionsScreen.BlocksPauseInput || AudioSettingsPanel.BlocksInput) return;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            bool toggle = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                       || (gamepad != null && gamepad.startButton.wasPressedThisFrame);
            if (toggle) SetPaused(!_paused);
            if (!_paused) return;
            if ((keyboard != null && keyboard.vKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame)) { OpenAudio(); return; }
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame) QuitToTitle();
        }

        void SetPaused(bool paused)
        {
            if (_paused != paused && _director != null) _director.BlockChoiceInputThisFrame();
            bool changed = _paused != paused;
            _paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            if (_root != null) _root.SetActive(paused);
            if (changed && UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(
                    paused && _resumeButton != null ? _resumeButton.gameObject : null);
            if (changed) UiAudio.Play(paused ? UiCue.Confirm : UiCue.Back);
        }

        void OnDestroy()
        {
            if (_paused)
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }
    }
}

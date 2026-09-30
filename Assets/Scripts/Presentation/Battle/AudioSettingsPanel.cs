using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>Shared title/pause audio dialog. All controls work without scaled time.</summary>
    public sealed class AudioSettingsPanel : MonoBehaviour
    {
        static AudioSettingsPanel _openPanel;
        static int _closedFrame = -1;
        public static bool BlocksInput => _openPanel != null || _closedFrame == Time.frameCount;
        public bool IsOpen => _root != null && _root.activeSelf;
        static readonly string[] Names = { "MASTER", "MUSIC", "SFX", "UI SOUNDS" };
        readonly Text[] _labels = new Text[4], _values = new Text[4];
        readonly Slider[] _sliders = new Slider[4];
        readonly float[] _unmuted = { 1f, 1f, 1f, 1f };
        GameObject _root, _previousSelection;
        Text _resetLabel, _backLabel;
        int _cursor;
        int _openedFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _openPanel = null; _closedFrame = -1; }

        public static AudioSettingsPanel Create(Transform parent, Font font, Font bold)
        {
            var go = new GameObject("AudioSettings");
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<AudioSettingsPanel>();
            panel.Build(font, bold);
            return panel;
        }

        void OnEnable() => AudioPreferences.Changed += Refresh;
        void OnDisable()
        {
            AudioPreferences.Changed -= Refresh;
            if (_openPanel == this) { _openPanel = null; _closedFrame = Time.frameCount; }
        }

        void Build(Font font, Font bold)
        {
            UiKit.EnsureEventSystem();
            var canvas = UiKit.CreateCanvas("AudioSettingsCanvas", 90);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;
            UiKit.CreateDim(canvas.transform, new Color(0f, 0.01f, 0.05f, 0.85f)).raycastTarget = true;
            var panel = UiKit.CreatePanel(canvas.transform, new Vector2(460f, 300f), "AudioPanel");
            UiKit.CreateCornerText(panel, bold, "AUDIO SETTINGS", 16, UiKit.TextAccent,
                new Vector2(0.5f, 1f), new Vector2(0f, -12f), TextAnchor.UpperCenter, "Title");
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var row = new GameObject("Row" + Names[i], typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(panel, false);
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.anchoredPosition = new Vector2(0f, -48f - i * 44f);
                row.sizeDelta = new Vector2(436f, 40f);
                _labels[i] = UiKit.CreateCornerText(row, font, "", 10, UiKit.TextMain,
                    new Vector2(0f, 0.5f), new Vector2(4f, 0f), TextAnchor.MiddleLeft, "Channel");
                _labels[i].rectTransform.sizeDelta = new Vector2(92f, 40f);
                _values[i] = UiKit.CreateCornerText(row, font, "", 10, UiKit.TextMain,
                    new Vector2(1f, 0.5f), new Vector2(-4f, 0f), TextAnchor.MiddleRight, "Value");
                _values[i].rectTransform.sizeDelta = new Vector2(60f, 40f);
                Control(row, font, "-", new Vector2(-100f, 0f), new Vector2(40f, 40f),
                    () => Adjust(index, -0.1f), () => Select(index), "Decrease");
                Control(row, font, "+", new Vector2(120f, 0f), new Vector2(40f, 40f),
                    () => Adjust(index, 0.1f), () => Select(index), "Increase");
                var hit = UiKit.CreateImage(row, "VolumeSlider", new Color(0f, 0f, 0f, 0f));
                hit.raycastTarget = true;
                hit.rectTransform.anchorMin = hit.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                hit.rectTransform.anchoredPosition = new Vector2(10f, 0f);
                hit.rectTransform.sizeDelta = new Vector2(160f, 40f);
                var track = UiKit.CreateImage(hit.transform, "Track", UiKit.PanelBorder);
                track.rectTransform.sizeDelta = new Vector2(160f, 6f);
                var fill = UiKit.CreateImage(track.transform, "Fill", UiKit.TextAccent);
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = Vector2.one;
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
                handleArea.SetParent(hit.transform, false);
                handleArea.sizeDelta = new Vector2(152f, 28f);
                var handle = UiKit.CreateImage(handleArea, "Handle", UiKit.TextMain);
                handle.rectTransform.sizeDelta = new Vector2(8f, 0f);
                var slider = hit.gameObject.AddComponent<Slider>();
                slider.navigation = new Navigation { mode = Navigation.Mode.None };
                slider.transition = Selectable.Transition.None;
                slider.fillRect = fill.rectTransform;
                slider.handleRect = handle.rectTransform;
                slider.targetGraphic = handle;
                slider.minValue = 0f;
                slider.maxValue = 10f;
                slider.wholeNumbers = true;
                slider.onValueChanged.AddListener(value => { Select(index); SetVolume(index, value / 10f); });
                _sliders[i] = slider;
            }
            _resetLabel = Control(panel, font, "RESET AUDIO", new Vector2(-100f, -124f), new Vector2(170f, 40f),
                ResetAudio, () => Select(4), "ResetAudio").GetComponentInChildren<Text>();
            _backLabel = Control(panel, font, "BACK", new Vector2(100f, -124f), new Vector2(170f, 40f),
                Close, () => Select(5), "Back").GetComponentInChildren<Text>();
            var hint = UiKit.CreateCornerText(panel, font, UiPlatform.TouchMode
                    ? "DRAG A BAR OR USE - / +" : "UP/DOWN SELECT   LEFT/RIGHT VOLUME   ENTER/(A) MUTE", 9,
                UiKit.TextDim, new Vector2(0.5f, 0f), new Vector2(0f, 61f), TextAnchor.MiddleCenter, "Hints");
            hint.rectTransform.sizeDelta = new Vector2(440f, 16f);
            Refresh();
            _root.SetActive(false);
        }

        static ChoiceButton Control(Transform parent, Font font, string label, Vector2 position, Vector2 size,
            UnityEngine.Events.UnityAction click, UnityEngine.Events.UnityAction hover, string name)
        {
            var panel = UiKit.CreatePanel(parent, size, name);
            panel.anchoredPosition = position;
            UiKit.CreateTextStretch(panel, font, label, 11, UiKit.TextMain, TextAnchor.MiddleCenter, 2f, "Label");
            return ChoiceButton.Create(panel.GetComponent<Image>(), click, hover);
        }

        public void Open()
        {
            _previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            EventSystem.current?.SetSelectedGameObject(null);
            _openPanel = this;
            _openedFrame = Time.frameCount;
            _cursor = 0;
            _root.SetActive(true);
            Refresh();
            UiAudio.Play(UiCue.Confirm);
        }

        public void Close()
        {
            _root.SetActive(false);
            if (_openPanel == this) _openPanel = null;
            _closedFrame = Time.frameCount;
            AudioPreferences.Save();
            UiAudio.Play(UiCue.Back);
            EventSystem.current?.SetSelectedGameObject(_previousSelection != null && _previousSelection.activeInHierarchy
                ? _previousSelection : null);
        }

        void Select(int index)
        {
            if (_cursor == index) return;
            _cursor = index;
            Refresh();
            UiAudio.Play(UiCue.Navigate);
        }

        void SetVolume(int index, float value)
        {
            bool changed = AudioPreferences.Set((AudioChannel)index, value);
            Refresh();
            UiAudio.Play(changed ? UiCue.Navigate : UiCue.Reject);
        }

        void Adjust(int index, float delta) { Select(index); SetVolume(index, AudioPreferences.Get((AudioChannel)index) + delta); }
        void ResetAudio() { AudioPreferences.RestoreDefaults(); Refresh(); UiAudio.Play(UiCue.Confirm); }

        void Refresh()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_sliders[i] == null) continue;
                float value = AudioPreferences.Get((AudioChannel)i);
                if (value > 0f) _unmuted[i] = value;
                _sliders[i].SetValueWithoutNotify(value * 10f);
                _labels[i].text = (_cursor == i ? "> " : "  ") + Names[i];
                _labels[i].color = _cursor == i ? UiKit.TextAccent : UiKit.TextMain;
                _values[i].text = value <= 0f ? "MUTED" : Mathf.RoundToInt(value * 100f) + "%";
            }
            if (_resetLabel != null) _resetLabel.text = (_cursor == 4 ? "> " : "") + "RESET AUDIO";
            if (_backLabel != null) _backLabel.text = (_cursor == 5 ? "> " : "") + "BACK";
        }

        void Update()
        {
            if (!IsOpen || _openedFrame == Time.frameCount) return;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame))
            { Close(); return; }
            int move = 0, adjust = 0;
            if ((kb != null && kb.upArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame)) move = -1;
            if ((kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame)) move = 1;
            if (move != 0) Select((_cursor + move + 6) % 6);
            if ((kb != null && kb.leftArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame)) adjust = -1;
            if ((kb != null && kb.rightArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame)) adjust = 1;
            if (adjust != 0 && _cursor < 4) Adjust(_cursor, adjust * 0.1f);
            if ((kb != null && kb.enterKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame))
            {
                if (_cursor == 5) Close();
                else if (_cursor == 4) ResetAudio();
                else SetVolume(_cursor, AudioPreferences.Get((AudioChannel)_cursor) > 0f ? 0f : _unmuted[_cursor]);
            }
        }
    }
}

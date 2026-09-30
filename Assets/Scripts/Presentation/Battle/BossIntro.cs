using UnityEngine;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    public enum BossIntroKind { StageBoss, HiddenBoss, FormTransition, SecondForm }

    /// <summary>
    /// Boss encounter information above the central flight lane. Core events own
    /// the trigger; the banner never changes encounter timing or simulation state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossIntro : MonoBehaviour
    {
        const float Duration = 2.4f;
        [SerializeField] Font _fontBold;

        float _age = float.MaxValue;
        GameObject _banner;
        CanvasGroup _group;
        Image _border;
        Text _heading, _detail;
        BattleDirector _director;
        JuiceDirector _juice;
        BossIntroKind _kind;
        bool IsSecondForm => _kind == BossIntroKind.FormTransition || _kind == BossIntroKind.SecondForm;
        Color Accent => IsSecondForm ? UiKit.TextAccent : UiKit.TextDanger;

        public void Trigger() => Trigger(BossIntroKind.StageBoss);

        public void Trigger(BossIntroKind kind)
        {
            _kind = kind;
            _age = 0f;
            RefreshText();
        }

        void Start()
        {
            _director = GetComponent<BattleDirector>();
            _juice = GetComponent<JuiceDirector>();
            var canvas = UiKit.CreateCanvas("BossIntroCanvas", 60);
            canvas.transform.SetParent(transform, false);

            // Leave the stage/HP readouts above, touch controls at the sides and
            // the central flight lane below clear at the 640x360 reference size.
            var rect = UiKit.CreatePanel(canvas.transform, new Vector2(360f, 56f), "Band");
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -64f);
            _border = rect.GetComponent<Image>();
            _group = rect.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _heading = UiKit.CreateCornerText(rect, _fontBold, "", 20, UiKit.TextDanger,
                new Vector2(0.5f, 1f), new Vector2(0f, -6f), TextAnchor.UpperCenter, "Warning");
            _heading.rectTransform.sizeDelta = new Vector2(344f, 24f);
            _detail = UiKit.CreateCornerText(rect, _fontBold, "", 10, UiKit.TextMain,
                new Vector2(0.5f, 0f), new Vector2(0f, 8f), TextAnchor.LowerCenter, "Encounter");
            _detail.rectTransform.sizeDelta = new Vector2(344f, 14f);
            RefreshText();
            _banner = rect.gameObject;
            _banner.SetActive(false);
        }

        void RefreshText()
        {
            if (_heading == null) return;
            _heading.text = IsSecondForm ? "SECOND FORM" : UiText.BossWarning;
            _detail.text = _kind == BossIntroKind.FormTransition ? "TRANSFORMATION IN PROGRESS"
                : _kind == BossIntroKind.SecondForm ? "SECOND FORM ENGAGED"
                : _kind == BossIntroKind.HiddenBoss ? "COLOSSUS DETECTED" : "STAGE BOSS DETECTED";
            _heading.color = Accent;
        }

        bool MenuOpen => Time.timeScale <= 0f || OptionsScreen.IsOpen || AudioSettingsPanel.BlocksInput;

        void Update()
        {
            if (_director != null && !_director.IsPlaying)
            {
                // A defeated boss must not leave a stale warning on the next room.
                _age = float.MaxValue;
                return;
            }
            if (!MenuOpen && _age < Duration) _age = Mathf.Min(Duration, _age + Time.deltaTime);
        }

        void LateUpdate()
        {
            if (_banner == null) return;
            bool visible = _age < Duration && !MenuOpen && (_director == null || _director.IsPlaying);
            if (_banner.activeSelf != visible) _banner.SetActive(visible);
            if (!visible) return;
            _group.alpha = Mathf.Clamp01((Duration - _age) / 0.3f);
            // Information stays readable throughout. Only the narrow frame pulses;
            // Reduce Flash holds it steady, including toggles during the warning.
            float pulse = _juice != null && _juice.FlashReduced ? 0f
                : (Mathf.Sin(_age * Mathf.PI * 4f) + 1f) * 0.5f;
            _border.color = Color.Lerp(Accent, Color.white, pulse * 0.35f);
        }
    }
}

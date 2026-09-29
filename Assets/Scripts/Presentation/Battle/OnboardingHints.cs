using UnityEngine;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 첫 런 온보딩 힌트 (M4 UX): 조작 → 캡슐/게이지 → 활성화 순서로 하단 중앙에
    /// 짧게 안내한다. 한 번 다 보면 PlayerPrefs로 영구 비활성 (rss.onboarded).
    /// 순수 표현 — 시뮬 상태를 읽기만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnboardingHints : MonoBehaviour
    {
        const string OnboardedPrefKey = "rss.onboarded";
        const float HintSeconds = 6f;

        [SerializeField] BattleDirector _director;
        [SerializeField] Font _font;

        const int HintCount = 3;

        /// <summary>조작 안내는 기기에 맞는 문면이어야 한다 — 폰에 WASD를 알려줘도 소용없다.</summary>
        string HintAt(int index)
        {
            bool touch = UiPlatform.TouchMode;
            switch (index)
            {
                case 0: return touch ? UiText.Onboarding1Touch
                    : PlayerBindings.MoveHint(_director?.Input?.MoveAction)
                      + "\nFIRE AUTOMATIC   BOMB B / EAST   PAUSE ESC / START";
                case 1: return UiText.Onboarding2;
                default:
                    if (touch) return UiText.Onboarding3Touch;
                    var activate = _director?.Input?.ActivateAction;
                    string keys = activate != null
                        ? PlayerBindings.KeyboardLabel(activate) + " / " + PlayerBindings.GamepadLabel(activate)
                        : "X / Y"; // Same fallback as PlayerInputReader for legacy action assets.
                    return $"Press {keys} to spend the gauge.\nWHERE you spend it is your build.";
            }
        }

        Text _text;
        GameObject _root;
        int _hintIndex = -1;
        int _bindingRevision = -1;
        float _age;
        bool _done;

        void Start()
        {
            _done = PlayerPrefs.GetInt(OnboardedPrefKey, 0) == 1;
            if (_done) return;

            var canvas = UiKit.CreateCanvas("OnboardingCanvas", 45);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;
            _text = UiKit.CreateCornerText(canvas.transform, _font, "", 11,
                UiKit.TextAccent, new Vector2(0.5f, 0f), new Vector2(0f, 40f),
                TextAnchor.LowerCenter, "Hint");
            UiKit.AddShadow(_text);
            _text.rectTransform.sizeDelta = new Vector2(600f, 42f);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void Update()
        {
            if (_done || _root == null || _director == null) return;

            // 게임오버/보상/경로 화면 중에는 숨긴다
            bool visible = !_director.IsRunFinished && !_director.AwaitingReward
                           && !_director.AwaitingRoute && Time.timeScale > 0f;
            if (_root.activeSelf != visible) _root.SetActive(visible);
            if (!visible) return;

            _age += Time.deltaTime;
            int index = Mathf.Min((int)(_age / HintSeconds), HintCount - 1);
            if (index != _hintIndex || _bindingRevision != PlayerBindings.Revision)
            {
                _hintIndex = index;
                _bindingRevision = PlayerBindings.Revision;
                _text.text = HintAt(index);
            }
            if (_age >= HintSeconds * HintCount)
            {
                _done = true;
                PlayerPrefs.SetInt(OnboardedPrefKey, 1);
                SaveFlush.Request();
                _root.SetActive(false);
            }
        }
    }
}

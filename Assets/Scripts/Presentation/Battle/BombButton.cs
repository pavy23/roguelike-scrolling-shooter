using UnityEngine;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 전멸 폭탄 발동 버튼 + 재고 표시.
    ///
    /// Core는 REQ-046부터 폭탄을 완전히 지원했지만(<c>InputCommand.ActivateBomb</c>,
    /// 재고, 무적, 데미지) Presentation이 없어서 **발동할 방법이 아예 없었다.**
    /// 사람이 요청한 "전멸폭탄 스톡모았다가 쓰기"의 마지막 조각이다.
    ///
    /// 재고가 0일 때도 탭을 받는다 — 눌렀는데 아무 반응이 없으면 버튼이 고장난 것처럼
    /// 느껴지므로, Core가 <c>BombActivationRejectedEmpty</c>를 내고 여기서 붉게 알린다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BombButton : MonoBehaviour
    {
        [SerializeField] BattleDirector _director;
        [SerializeField] Font _font;
        [SerializeField] Sprite _icon;   // 폭탄 픽업 스프라이트 — 게임 내 아이템과 같은 그림

        public static BombButton Instance { get; private set; }

        Image _background;
        Image _iconImage;
        Text _label;
        bool _pressed;
        float _emptyFlash;
        int _shownStock = -1;

        // 계기판 단일 액센트: 쓸 수 있으면 앰버, 아니면 무채색으로 죽는다.
        static readonly Color Ready = new Color(1f, 0.70f, 0.11f, 0.92f);
        static readonly Color Empty = new Color(0.30f, 0.34f, 0.38f, 0.5f);
        static readonly Color EmptyFlash = new Color(1f, 0.28f, 0.24f, 0.95f);

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            UiKit.EnsureEventSystem();

            var canvas = UiKit.CreateCanvas("BombCanvas", 46);
            canvas.transform.SetParent(transform, false);

            // 우하단 — 가로 모드에서 오른손 엄지가 닿는 자리다. 기체 드래그는 화면
            // 어디서나 시작되므로 버튼 영역은 아래에서 드래그 대상에서 뺀다.
            // 텍스트 사각형 대신 아이콘 버튼 — 게임에서 줍는 폭탄과 같은 그림이라
            // 설명 없이 연결된다 ("이쁘게 누르기 편한 아이콘으로", 2026-07-31).
            var button = UiKit.CreateTouchButton(
                canvas.transform, _font, "", 9,
                // y를 46에서 12로 내렸다 (사람 지시 2026-08-04: "폭탄 버튼은
                // 아래로 빼고"). 46은 게이지 줄과 같은 높이라 둘이 붙어 보였다.
                new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(64f, 64f),
                OnTap, "BombButton", accent: true);

            _background = button.targetGraphic as Image;
            _label = button.GetComponentInChildren<Text>();
            // 라벨은 하단 띠로 내려 아이콘 자리를 비운다
            var labelRect = _label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = new Vector2(1f, 0.36f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            _label.alignment = TextAnchor.MiddleCenter;

            if (_icon != null)
            {
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(button.transform, false);
                var iconImage = iconGo.AddComponent<Image>();
                iconImage.sprite = _icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                var iconRect = iconImage.rectTransform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.62f);
                iconRect.sizeDelta = new Vector2(28f, 28f);
                _iconImage = iconImage;
            }

            // 폭탄 버튼을 누르려다 기체가 끌려가면 최악이다 — 이 사각형은 드래그에서 뺀다.
            var touch = TouchControls.Instance;
            if (touch != null) touch.ReserveRect(button.GetComponent<RectTransform>());
        }

        void OnTap() => _pressed = true;

        /// <summary>
        /// 이번 프레임의 탭을 소비한다. PlayerInputReader가 매 프레임 한 번만 호출해야
        /// 한다 — 두 번 호출하면 두 번째는 항상 false가 된다.
        /// </summary>
        public bool ConsumePress()
        {
            bool pressed = _pressed;
            _pressed = false;
            return pressed;
        }

        /// <summary>재고 없이 눌렀음을 알린다 (BattleDirector가 Core 이벤트로 호출).</summary>
        public void FlashEmpty() => _emptyFlash = 0.35f;

        void Update()
        {
            if (_director == null || _background == null) return;

            // 메뉴·보상 화면에서는 숨긴다. TouchControls와 같은 기준을 쓴다.
            var touch = TouchControls.Instance;
            bool visible = touch == null || touch.GameplayActive;
            if (_background.transform.parent.gameObject.activeSelf != visible)
                _background.transform.parent.gameObject.SetActive(visible);
            if (!visible) return;

            int stock = _director.BombStock;
            if (stock != _shownStock)
            {
                _shownStock = stock;
                if (_label != null)
                {
                    string stockLabel = stock > 0 ? $"BOMB x{stock}" : "EMPTY";
                    _label.text = UiPlatform.TouchMode ? stockLabel : stockLabel + "\nB / EAST";
                }
                if (_iconImage != null)
                {
                    var ic = _iconImage.color;
                    ic.a = stock > 0 ? 1f : 0.4f;   // 재고 없으면 아이콘도 죽인다
                    _iconImage.color = ic;
                }
            }

            if (_emptyFlash > 0f)
            {
                _emptyFlash -= Time.unscaledDeltaTime;
                _background.color = Color.Lerp(
                    Empty, EmptyFlash, Mathf.Clamp01(_emptyFlash * 3f));
                return;
            }

            // 재고가 있으면 자홍으로 켜 두고, 없으면 죽여서 "지금 쓸 수 있는가"가
            // 눈으로 바로 읽히게 한다.
            _background.color = stock > 0 ? Ready : Empty;
            if (_label != null)
                _label.color = stock > 0 ? Color.white : UiKit.TextDim;
        }
    }
}

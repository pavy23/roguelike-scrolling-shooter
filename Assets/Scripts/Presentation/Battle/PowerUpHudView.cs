using Shmup.Core;
using Shmup.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 그라디우스식 파워업 게이지 HUD (REQ-074 7슬롯 대응 재작성).
    ///
    /// 슬롯 수·이름·순서가 GameData 주도가 되면서 씬에 박아 둔 스프라이트 4칸으로는
    /// 감당할 수 없어, 게이지 관측 API(GaugeSlots)를 순회하며 **런타임에 직접 조립**한다.
    /// 데이터가 슬롯을 바꿔도 HUD는 따라온다.
    ///
    /// 알파벳 한 글자와 작은 핍만으로는 레벨 식별이 어렵다는 피드백(2026-07-31)에 따라
    /// 풀네임 + "LV n"/"MAX"를 쓰고, 실드 잔량 숫자를 함께 띄운다.
    /// 여전히 Core 상태를 읽기만 한다 — 게이지 조작은 어디서도 하지 않는다 (CLAUDE.md).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerUpHudView : MonoBehaviour
    {
        [SerializeField] BattleDirector _director;
        [SerializeField] Font _font;

        const float SlotWidth = 76f;
        const float SlotHeight = 34f;
        const float SlotGap = 4f;
        const int MaxPips = 6;

        // UiSkin.Button(헤어라인 셀) 틴트 = 테두리 색, 속은 자동으로 짙어진다.
        // 계기판 언어: 평시는 무채색, 커서만 앰버 — 색이 곧 "지금 여기"다.
        static readonly Color FrameNormal = new Color(0.30f, 0.34f, 0.38f, 0.9f);
        static readonly Color FrameCursor = new Color(1f, 0.70f, 0.11f, 1f);
        static readonly Color FrameMaxed = new Color(0.35f, 0.62f, 0.42f, 0.95f);
        static readonly Color FrameActiveMode = new Color(0.78f, 0.42f, 0.68f, 0.95f);
        static readonly Color ShieldEmpty = new Color(1f, 0.40f, 0.32f, 1f);
        static readonly Color32 PipFilled = new Color32(0x9C, 0xD4, 0xFF, 0xFF);
        static readonly Color32 PipEmpty = new Color32(0x22, 0x2C, 0x44, 0xFF);
        static readonly Color32 PipBanking = new Color32(0x4E, 0x7A, 0xB8, 0xFF);
        static readonly Color32 PipBankingNear = new Color32(0x86, 0xB6, 0xF0, 0xFF);

        Canvas _canvas;
        Image[] _frames;
        Text[] _labels;
        Image[][] _pips;

        /// <summary>게이지 슬롯 식별용 — 실드만 표기 규칙이 다르다(레벨 아님, 스톡).</summary>
        const string ShieldNameKey = "shield";
        int _builtCount = -1;

        // 계약 잠금 플래시 (REQ-094). SELECT를 눌렀는데 게이지가 꿈쩍도 않으면 고장으로
        // 읽힌다 — 계약이 막고 있다는 말을 게이지 바로 위에서 한 번 해 준다.
        Text _contractLockText;
        Text _selectionText;
        int _hintCursor = int.MinValue, _hintLevel, _hintProgress, _hintRequired, _hintRevision = -1;
        string _hintName;
        bool _hintCanActivate, _hintTouch;
        int _shownLockPulse;
        float _lockFlashAge = float.MaxValue;

        /// <summary>플래시 지속 시간(초). 읽히되 시야를 오래 막지 않는 길이.</summary>
        const float LockFlashDuration = 1f;

        /// <summary>마지막 구간은 서서히 사라진다 — 뚝 끊기면 눈이 잔상만 남긴다.</summary>
        const float LockFadeTail = 0.3f;

        // ── 기체가 게이지 뒤로 들어갔을 때 (build25~29 "기체 영구 소실"의 진짜 원인) ──
        //
        // 플레이필드는 화면 전체다. Core는 기체 중심을 화면 아래 끝(-10.75u)까지
        // 허용하는데, 이 게이지 줄(화면 하단 38px = 2.375u)이 **불투명 오버레이
        // 캔버스**라 그 아래로 내려간 기체를 통째로 덮는다. 스프라이트 정렬 순서로는
        // 절대 이길 수 없다 — ScreenSpaceOverlay는 항상 모든 스프라이트 위다.
        //
        // 그래서 테스터가 다섯 빌드 연속으로 "화면 하단으로 붙이면 기체가 영구히
        // 사라진다"고 보고했다. 기체는 죽지도 화면 밖으로 나가지도 않았고, 시뮬도
        // 정상이었다. 자기 기체가 안 보이니 다시 올라올 수도 없었을 뿐이다.
        // (전경 실루엣 order 55는 같은 증상의 **다른** 가해자였고 이미 3으로 내렸다.)
        //
        // 기체가 이 띠에 들어오는 동안 게이지를 흐린다. 숨기지는 않는다 — 게이지는
        // 다음 캡슐을 어디에 쓸지 정하는 화면이라 사라지면 그것대로 손해다.
        // 값은 표시가 아니라 **가림 해소**가 기준이다: 기체 실루엣이 읽히는 최저선.
        const float MinOcclusionAlpha = 0.2f;

        /// <summary>기체 스프라이트 반높이 (48×30px @PPU16). 히트박스가 아니라 그림 크기다.</summary>
        const float ShipHalfHeightWorld = 0.94f;

        /// <summary>페이드 속도(초당). 즉시 껐다 켜면 깜빡임으로 읽힌다.</summary>
        const float OcclusionFadePerSecond = 6f;

        CanvasGroup _group;
        Camera _camera;
        float _occlusionAlpha = 1f;
        readonly Vector3[] _corners = new Vector3[4];

        void Start()
        {
            _canvas = UiKit.CreateCanvas("GaugeCanvas", 42);
            _canvas.transform.SetParent(transform, false);

            // 알파만 쓴다. 이 캔버스에는 raycastTarget이 켜진 그래픽이 없지만,
            // 흐려진 게이지가 조작 터치를 먹는 일이 절대 없도록 못을 박아 둔다.
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            // 좌하단 실드 숫자는 뺐다 (사람 지시 2026-08-04: "왼쪽 실드 숫자는
            // 빼도 될듯 (실드 개수가 옵션에 있으니)"). 게이지 줄의 SHIELD 슬롯이
            // 같은 값을 이미 보여 준다 — 같은 숫자를 두 곳에 두면 화면만 좁아진다.

            // 게이지 슬롯 줄(높이 SlotHeight, y=4) 바로 위. 슬롯을 가리지 않으면서
            // 시선이 게이지에 있을 때 같은 시야에 들어오는 자리다.
            _contractLockText = UiKit.CreateCornerText(_canvas.transform, _font, "", 11,
                UiKit.TextAccent, new Vector2(0.5f, 0f), new Vector2(0f, SlotHeight + 10f),
                TextAnchor.LowerCenter, "ContractLock");
            UiKit.AddShadow(_contractLockText);
            _contractLockText.gameObject.SetActive(false);
            _selectionText = UiKit.CreateCornerText(_canvas.transform, _font, "", 10,
                UiKit.TextAccent, new Vector2(0.5f, 0f), new Vector2(-43f, SlotHeight + 10f),
                TextAnchor.LowerCenter, "SelectionHint");
            _selectionText.rectTransform.sizeDelta = new Vector2(548f, 18f);
            _selectionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddShadow(_selectionText, 1f);
        }

        void Build(int count)
        {
            if (_builtCount == count) return;
            _builtCount = count;

            // 재구성 (스테이지 전환 등으로 슬롯 구성이 바뀔 가능성에 대비해 전부 새로)
            for (int i = _canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = _canvas.transform.GetChild(i);
                if (child.name.StartsWith("GaugeSlot"))
                    Destroy(child.gameObject);
            }

            _frames = new Image[count];
            _labels = new Text[count];
            _pips = new Image[count][];

            float total = count * SlotWidth + (count - 1) * SlotGap;
            // 줄 전체를 왼쪽으로 민다. 가운데 정렬이면 오른쪽 끝 슬롯이 우하단
            // 폭탄 버튼 밑에 깔린다 (사람 지적 2026-08-04). 폭탄 버튼 폭(64) +
            // 여백(14+8)만큼 비켜 주면 둘이 만나지 않는다.
            const float BombButtonClearance = 86f;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"GaugeSlot{i}");
                go.transform.SetParent(_canvas.transform, false);
                var frame = go.AddComponent<Image>();
                frame.sprite = UiSkin.Button;
                frame.type = Image.Type.Sliced;
                frame.color = FrameNormal;
                frame.raycastTarget = false;
                var rect = frame.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(
                    -total / 2f + SlotWidth / 2f + i * (SlotWidth + SlotGap)
                        - BombButtonClearance / 2f,
                    4f);
                rect.sizeDelta = new Vector2(SlotWidth, SlotHeight);
                _frames[i] = frame;

                var label = UiKit.CreateText(rect, _font, "", 9,
                    UiKit.TextMain, TextAnchor.UpperCenter, "Label");
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0.35f);
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = new Vector2(0f, -2f);
                _labels[i] = label;

                _pips[i] = new Image[MaxPips];
                float pipTotal = MaxPips * 9f - 3f;
                for (int p = 0; p < MaxPips; p++)
                {
                    var pipGo = new GameObject($"Pip{p}");
                    pipGo.transform.SetParent(rect, false);
                    var pip = pipGo.AddComponent<Image>();
                    pip.raycastTarget = false;
                    var pipRect = pip.rectTransform;
                    pipRect.anchorMin = pipRect.anchorMax = new Vector2(0.5f, 0f);
                    pipRect.pivot = new Vector2(0.5f, 0f);
                    pipRect.anchoredPosition = new Vector2(
                        -pipTotal / 2f + 3f + p * 9f, 4f);
                    pipRect.sizeDelta = new Vector2(6f, 6f);
                    _pips[i][p] = pip;
                }
            }
        }

        /// <summary>데이터의 nameKey를 HUD 표기로. 모르는 키는 대문자로 그대로 쓴다.</summary>
        static string DisplayName(string nameKey)
        {
            if (nameKey != null && nameKey.StartsWith("powerUp.", System.StringComparison.OrdinalIgnoreCase))
                nameKey = nameKey.Substring("powerUp.".Length);
            // Core가 함선 게이지의 주무기 축에 붙이는 고정 키 (REQ-082 D).
            // 스위치의 소문자 케이스와 달리 이 키만 혼합 대소문자라 별도 처리한다 —
            // 그대로 두면 "POWERUP.MAINSHOT"이 화면에 노출된다 (로컬 검증에서 발견).
            if (!string.IsNullOrEmpty(nameKey)
                && nameKey.IndexOf("mainShot", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "SHOT";
            switch (nameKey?.ToLowerInvariant())
            {
                case "speed": return "SPEED";
                case "mainshot":
                case "main shot":
                case "shot": return "SHOT";
                case "missile": return "MISSILE";
                case "double": return "DOUBLE";
                case "laser": return "LASER";
                case "triple": return "TRIPLE";
                case "option": return "OPTION";
                case ShieldNameKey: return "SHIELD";
                default:
                    return string.IsNullOrEmpty(nameKey)
                        ? "?" : nameKey.ToUpperInvariant();
            }
        }

        /// <summary>
        /// 무기 진화 단계명 (REQ-086, 사람 승인 설계). 모르는 조합은 기본 이름으로.
        /// 실데이터 nameKey는 "Double Shot"/"Triple Shot"/"Laser"처럼 표시용 문자열이라
        /// (REQ-089에서 확인) 소문자 포함 검사로 매칭한다 — 정확 일치는 조용히 폴백돼
        /// 진화명이 아예 안 보였다. 표시명은 슬롯 폭(76px) 안에 들어가게 짧게.
        /// </summary>
        /// <summary>미사일 계열 이름. 짧게 — 슬롯 폭이 좁다.</summary>
        static string MissileFamilyName(MissileFamily family)
        {
            switch (family)
            {
                case MissileFamily.SpreadBomb: return "SPREAD";
                case MissileFamily.PiercingLance: return "LANCE";
                case MissileFamily.DownwardDrop: return "DROP";
                case MissileFamily.Homing: return "HOMING";
                default: return "STRAIGHT";
            }
        }

        /// <summary>옵션 편대 형태.</summary>
        static string OptionFormationName(OptionFormation formation)
        {
            switch (formation)
            {
                case OptionFormation.Fixed: return "FIXED";
                case OptionFormation.Orbit: return "ORBIT";
                default: return "TRAIL";
            }
        }

        static string EvolutionName(string nameKey, int level)
        {
            string key = (nameKey ?? "").ToLowerInvariant();
            if (key.Contains("double"))
                return level >= 3 ? "CROSS FIRE" : level == 2 ? "TAIL GUARD" : "DOUBLE";
            if (key.Contains("triple"))
                return level >= 3 ? "BURNER" : level == 2 ? "PULSE FAN" : "TRIPLE";
            if (key.Contains("laser"))
                return level >= 3 ? "PRISM BEAM" : level == 2 ? "LANCE" : "LASER";
            return DisplayName(nameKey);
        }

        void LateUpdate()
        {
            if (_canvas == null) return;
            // 게이지가 아직 없어도(런 준비 중) 플래시 타이머는 돌려야 한다 —
            // 켜 놓은 채로 멈추면 글자가 화면에 눌어붙는다.
            UpdateContractLock();

            var gauge = _director != null ? _director.Gauge : null;
            if (gauge == null) return;


            int count = gauge.GaugeSlotCount;
            if (count <= 0) return;
            Build(count);
            UpdateOcclusionFade();
            RefreshSelectionHint(gauge);

            for (int i = 0; i < count; i++)
            {
                var view = gauge.GetGaugeSlotView(i);
                bool isCursor = gauge.Cursor == i;
                bool maxed = view.Level >= view.MaxLevel;
                bool emptyShield = view.Slot == PowerUpSlot.Shield && _director.ShieldRemaining == 0;

                _frames[i].color =
                    isCursor ? FrameCursor :
                    emptyShield ? ShieldEmpty :
                    view.IsActiveWeaponMode ? FrameActiveMode :
                    maxed ? FrameMaxed : FrameNormal;

                string name = DisplayName(view.NameKey);
                // 무기 모드는 켜짐/진화 단계가 정체다 (REQ-086: maxLevel 3 진화).
                // 활성 상태에서는 현재 진화 단계의 이름을 그대로 보여 준다 —
                // "DOUBLE LV2"보다 "TAIL GUARD"가 무엇이 바뀌었는지 즉시 읽힌다.
                // 실드는 레벨이 아니라 **스톡**이다 (사람 지적 2026-08-03). Core에서 이
                // 슬롯 레벨이 오르는 순간 RecoverShieldStock(+1)이 돌아 재고가 한 장 느는
                // 것뿐이고, 방어력이 세지지는 않는다. "SHIELD LV3"은 강해진 것처럼 읽혀
                // 거짓말이 된다 — 지금 들고 있는 재고를 그대로 보여 준다.
                // Slot identity is stable even when content uses a localized/prefixed name key.
                bool shieldSlot = view.Slot == PowerUpSlot.Shield;
                bool missileSlot = view.Slot == PowerUpSlot.Missile;
                bool optionSlot = view.Slot == PowerUpSlot.Option;

                // 슬롯마다 **읽고 싶은 정보가 다르다** (사람 지시 2026-08-03).
                //   샷    = 위력      → 레벨이 곧 화력이라 LV 그대로가 맞다
                //   미사일 = 유형      → 기체·보상마다 계열이 달라 "LV2"로는 뭐가 달렸는지 모른다
                //   옵션   = 편대 형태 → 마찬가지로 숫자가 아니라 배치가 정체다
                //   실드   = 재고      → 애초에 레벨이 아니다
                string label = view.IsActiveWeaponMode
                    ? $"{EvolutionName(view.NameKey, view.Level)}\n{(view.Level >= view.MaxLevel ? "MAX" : $"MK{view.Level}")}"
                    : shieldSlot
                        ? $"{name}\n{(emptyShield ? "EMPTY" : "x" + _director.ShieldRemaining)}"
                    : missileSlot && view.Level > 0
                        ? $"{name}\n{MissileFamilyName(_director.CurrentMissileFamily)}"
                    : optionSlot && view.Level > 0
                        ? $"{name}\n{OptionFormationName(_director.CurrentOptionFormation)}"
                    : view.MaxLevel <= 1 ? name
                    : maxed ? $"{name}\nMAX"
                    : $"{name}\nLV{view.Level}";
                if (isCursor) label = "> " + label;
                if (_labels[i].text != label) _labels[i].text = label;
                _labels[i].color =
                    emptyShield ? ShieldEmpty :
                    isCursor ? new Color(1f, 0.88f, 0.55f, 1f) :
                    view.IsActiveWeaponMode ? new Color(1f, 0.6f, 0.9f, 1f) :
                    maxed ? new Color(0.48f, 0.88f, 0.61f, 1f) : UiKit.TextMain;

                float banked = view.RequiredCapsules > 0
                    ? view.Progress / (float)view.RequiredCapsules : 0f;
                if (view.MaxLevel <= 1)
                {
                    // 무기 모드: 핍 = 필요 캡슐 수, 채움 = 적립량
                    int need = Mathf.Clamp(view.RequiredCapsules, 0, MaxPips);
                    for (int p = 0; p < MaxPips; p++)
                    {
                        bool within = p < need && !view.IsActiveWeaponMode;
                        _pips[i][p].enabled = within;
                        if (!within) continue;
                        _pips[i][p].color = p < view.Progress ? PipBankingNear : PipEmpty;
                    }
                }
                else if (shieldSlot)
                {
                    // 실드는 레벨이 아니라 재고다 — 레벨 표시기를 아예 감춘다
                    // (사람 지시 2026-08-03: "실드는 레벨이 아니니까 그냥 개수만
                    // 표시하면 될듯, 밑에 레벨 표시기 제외"). 핍이 남아 있으면
                    // 옆 슬롯들과 같은 "성장하는 것"으로 읽혀 라벨과 어긋난다.
                    for (int p = 0; p < MaxPips; p++)
                        _pips[i][p].enabled = false;
                }
                else
                {
                    for (int p = 0; p < MaxPips; p++)
                    {
                        bool within = p < view.MaxLevel;
                        _pips[i][p].enabled = within;
                        if (!within) continue;
                        if (p < view.Level) _pips[i][p].color = PipFilled;
                        else if (p == view.Level && banked > 0.001f)
                            _pips[i][p].color = banked >= 0.5f ? PipBankingNear : PipBanking;
                        else _pips[i][p].color = PipEmpty;
                    }
                }
            }
        }

        void RefreshSelectionHint(PowerUpGauge gauge)
        {
            bool visible = _director.IsPlaying && !_director.ReplayMode && Time.timeScale > 0f
                && !_contractLockText.gameObject.activeSelf;
            _selectionText.gameObject.SetActive(visible);
            if (!visible) return;
            var selected = gauge.HasSelection ? gauge.GetGaugeSlotView(gauge.Cursor) : default;
            bool canActivate = gauge.CanActivate, touch = UiPlatform.TouchMode;
            if (_hintCursor == gauge.Cursor && _hintName == selected.NameKey && _hintLevel == selected.Level
                && _hintProgress == selected.Progress && _hintRequired == selected.RequiredCapsules
                && _hintCanActivate == canActivate && _hintTouch == touch
                && _hintRevision == PlayerBindings.Revision) return;
            _hintCursor = gauge.Cursor;
            _hintName = selected.NameKey;
            _hintLevel = selected.Level;
            _hintProgress = selected.Progress;
            _hintRequired = selected.RequiredCapsules;
            _hintCanActivate = canActivate;
            _hintTouch = touch;
            _hintRevision = PlayerBindings.Revision;
            string hint = SelectionHint(gauge);
            if (_selectionText.text != hint) _selectionText.text = hint;
            _selectionText.color = canActivate ? UiKit.TextAccent : UiKit.TextMain;
        }

        string SelectionHint(PowerUpGauge gauge)
        {
            if (!gauge.HasSelection) return UiText.GaugeCollect;
            var view = gauge.GetGaugeSlotView(gauge.Cursor);
            string name = DisplayName(view.NameKey);
            // Read the Core's eligibility; do not promise an upgrade that a contract blocks.
            if (!gauge.CanActivate)
                return string.Format(view.Level >= view.MaxLevel ? UiText.GaugeMax : UiText.GaugeLocked, name);
            string key = PlayerBindings.ActivateHint(_director?.Input?.ActivateAction);
            return view.Progress + 1 >= view.RequiredCapsules
                ? string.Format(UiText.GaugeUpgrade, name, key)
                : string.Format(UiText.GaugeInvest, name, view.Progress, view.RequiredCapsules, key);
        }

        /// <summary>
        /// 기체가 게이지 줄 뒤로 들어가는 만큼 게이지를 흐린다.
        ///
        /// 경계는 상수로 굳히지 않고 **슬롯 사각형의 실제 윗변**을 화면 좌표로 읽어
        /// 월드로 되돌린다. 창 비율·정수 배율에 따라 화면이 참조 해상도보다 세로로
        /// 넓게 잡히는 경우가 있어(1300×760에서 380px), 참조 해상도로 계산한 상수는
        /// 실기에서 어긋난다.
        /// </summary>
        void UpdateOcclusionFade()
        {
            if (_group == null || _director == null) return;
            if (_frames == null || _frames.Length == 0 || _frames[0] == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            // ScreenSpaceOverlay 캔버스의 월드 좌표 = 화면 픽셀 좌표다.
            _frames[0].rectTransform.GetWorldCorners(_corners);
            float hudTopWorldY =
                _camera.ScreenToWorldPoint(new Vector3(_corners[1].x, _corners[1].y, 0f)).y;

            // 기체 아랫변이 띠에 닿기 시작하면 0, 통째로 잠기면 1.
            float shipY = _director.PlayerWorldPosition.y;
            float t = Mathf.InverseLerp(
                hudTopWorldY + ShipHalfHeightWorld,
                hudTopWorldY - ShipHalfHeightWorld,
                shipY);
            float target = Mathf.Lerp(1f, MinOcclusionAlpha, t);

            // 보상·일시정지로 timeScale이 0이어도 굳지 않게 unscaled로 민다.
            _occlusionAlpha = Mathf.MoveTowards(
                _occlusionAlpha, target, OcclusionFadePerSecond * Time.unscaledDeltaTime);
            if (!Mathf.Approximately(_group.alpha, _occlusionAlpha))
                _group.alpha = _occlusionAlpha;
        }

        /// <summary>
        /// 계약이 발동을 거부한 순간을 받아 1초짜리 앰버 플래시를 띄운다.
        /// BattleDirector가 세는 펄스 값이 바뀌는 순간이 곧 "방금 거부됐다"는 신호다.
        /// </summary>
        void UpdateContractLock()
        {
            if (_contractLockText == null) return;

            if (_director != null && _director.ContractLockPulse != _shownLockPulse)
            {
                _shownLockPulse = _director.ContractLockPulse;
                _lockFlashAge = 0f;
                _contractLockText.text = LockMessage(_director.ContractLockResult);
                _contractLockText.gameObject.SetActive(true);
            }

            if (!_contractLockText.gameObject.activeSelf) return;

            // 계약 화면·일시정지로 timeScale이 0이어도 플래시는 흘러야 한다.
            _lockFlashAge += Time.unscaledDeltaTime;
            if (_lockFlashAge >= LockFlashDuration)
            {
                _contractLockText.gameObject.SetActive(false);
                return;
            }
            float remaining = LockFlashDuration - _lockFlashAge;
            float alpha = remaining >= LockFadeTail ? 1f : remaining / LockFadeTail;
            var color = UiKit.TextAccent;
            color.a = alpha;
            _contractLockText.color = color;
        }

        /// <summary>
        /// 무엇이 막혔는지까지 말해 준다 — "게이지 전체"와 "옵션만"은 대응이 다르다
        /// (전자는 캡슐을 모을 이유가 없고, 후자는 커서를 다른 슬롯으로 옮기면 된다).
        /// </summary>
        static string LockMessage(PowerUpActivationResult result)
        {
            switch (result)
            {
                case PowerUpActivationResult.ContractOptionActivationBanned:
                    return "CONTRACT LOCK - OPTION";
                case PowerUpActivationResult.ContractShieldActivationBanned:
                    return "CONTRACT LOCK - SHIELD";
                default:
                    return "CONTRACT LOCK";
            }
        }

    }
}

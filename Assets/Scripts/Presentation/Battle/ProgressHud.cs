using Shmup.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 스테이지 진행도 HUD + 스테이지 진입 배너 (REQ-032, REQ-054에서 구간 표시로 전환).
    /// 런에서 "지금 어디쯤인가"를 알려 주지 않으면 여정 감각이 생기지 않는다.
    /// 좌상단에 BIOME n/5 · ROOM m/6 을 점 표시로, 바이옴이 바뀌면 중앙에 테마 배너를 띄운다.
    /// 순수 표현 — director 상태를 읽기만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProgressHud : MonoBehaviour
    {
        const float BannerSeconds = 2.2f;

        [SerializeField] BattleDirector _director;
        [SerializeField] Font _font;
        [SerializeField] Font _fontBold;
        [SerializeField] string[] _themeIds;
        [SerializeField] string[] _themeNames;

        Text _progressText;
        Text _contractText;
        string _shownContractId;
        Text _bannerText;
        Text _bannerDailyText;
        RectTransform _bannerRect;
        GameObject _bannerRoot;
        int _shownBiome = -1, _shownRoom = -1;
        RunStageSection _shownSection = (RunStageSection)(-1);
        int _bannerBiome = -1;
        float _bannerAge = float.MaxValue;
        bool _bannerDailyShown;

        /// <summary>최종전 판돈 배너를 이미 띄웠는가 (런당 1회 — Core도 1회만 정산한다).</summary>
        bool _wagerBannerShown;

        /// <summary>
        /// 마지막으로 배너를 띄운 고스트 합류 번호 (REQ-109). director가 GhostSpawned를
        /// 받을 때마다 올리는 값이라, 여기서는 "내가 본 번호와 다른가"만 보면 된다 —
        /// 재출격으로 다시 합류해도 번호가 또 올라가므로 배너가 한 번 더 뜬다.
        /// </summary>
        int _shownGhostSpawn;

        /// <summary>데일리 표식 (좌상단, STAGE 바로 위). 데일리 런에서만 켜진다.</summary>
        Text _dailyBadge;

        /// <summary>배너 기본 높이와, 윗줄이 붙은 데일리 첫 배너의 높이.</summary>
        const float BannerHeight = 44f;
        const float BannerHeightDaily = 60f;

        void Start()
        {
            var canvas = UiKit.CreateCanvas("ProgressCanvas", 43);
            canvas.transform.SetParent(transform, false);
            canvas.gameObject.AddComponent<BattleHudVisibility>().Initialize(_director);

            // 데일리 뱃지 — 진행도 바로 위. "지금 어떤 모드인가"를 런 내내 알 수 있어야 한다
            // (사람 피드백 2026-08-01). 앰버 한 색 액센트라 계기판 언어를 깨지 않는다.
            _dailyBadge = UiKit.CreateCornerText(canvas.transform, _font, UiText.DailyBadge, 9,
                // 좌상단은 PAUSE 버튼 자리가 됐다 — 글자를 오른쪽으로 비킨다.
                UiKit.TextAccent, new Vector2(0f, 1f), new Vector2(84f, -12f),
                TextAnchor.UpperLeft, "DailyBadge");
            UiKit.AddShadow(_dailyBadge, 1f);
            _dailyBadge.gameObject.SetActive(false);

            // 스테이지 표기는 **보스 체력바 바로 아래**다 (사람 지시 2026-08-04).
            // 체력바는 월드 y 9.7에 있고 화면 위에서 약 1.9유닛(=30px) 내려온
            // 자리가 그 아래다. 둘을 붙여 두면 "어느 판의 누구를 상대하는가"가
            // 한 덩어리로 읽힌다 — 예전처럼 좌상단에 흩어져 있으면 따로 논다.
            _progressText = UiKit.CreateCornerText(canvas.transform, _font, "", 11,
                UiKit.TextDim, new Vector2(0.5f, 1f), new Vector2(0f, -34f),
                TextAnchor.UpperCenter, "Progress");
            UiKit.AddShadow(_progressText);

            // 활성 계약 (REQ-070). 계약이 스테이지 전체에 걸리는데 표시가 없으면
            // "왜 적이 많지?"가 버그로 읽힌다 — 내가 고른 조건임을 계속 보여 준다.
            _contractText = UiKit.CreateCornerText(canvas.transform, _font, "", 9,
                UiKit.TextDim, new Vector2(0f, 1f), new Vector2(84f, -26f),
                TextAnchor.UpperLeft, "Contract");
            UiKit.AddShadow(_contractText);

            // 바이옴 진입 배너 (중앙, 짧게)
            var band = new GameObject("BiomeBanner");
            band.transform.SetParent(canvas.transform, false);
            var image = band.AddComponent<Image>();
            image.color = new Color(0.03f, 0.05f, 0.12f, 0.72f);
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(0f, BannerHeight);
            // 띠 위아래 금색 룰 — 밋밋한 반투명 사각형이 아니라 연출된 배너로 읽힌다.
            // 룰은 상/하단 앵커라 배너가 높아져도 그대로 가장자리에 붙는다.
            UiKit.CreateRule(rect, new Vector2(0.5f, 1f), Vector2.zero, 460f,
                UiKit.TextAccent, "RuleTop");
            UiKit.CreateRule(rect, new Vector2(0.5f, 0f), Vector2.zero, 460f,
                UiKit.TextAccent, "RuleBottom");
            _bannerText = UiKit.CreateTextStretch(rect, _fontBold, "", 20,
                UiKit.TextAccent, TextAnchor.MiddleCenter, 0f, "BannerText");
            // 데일리 런의 **첫** 배너에만 붙는 윗줄. 매 바이옴마다 반복하면 소음이 된다.
            _bannerDailyText = UiKit.CreateCornerText(rect, _font, UiText.DailyBannerHeader, 10,
                UiKit.TextAccent, new Vector2(0.5f, 1f), new Vector2(0f, -6f),
                TextAnchor.UpperCenter, "BannerDaily");
            _bannerDailyText.gameObject.SetActive(false);
            _bannerRect = rect;
            _bannerRoot = band;
            _bannerRoot.SetActive(false);
        }

        void Update()
        {
            if (_director == null || _progressText == null) return;

            // 데일리 뱃지는 런 내내 고정이지만 director가 Awake에서 굳히므로 상태만 맞춘다.
            bool daily = _director.IsDailyRun;
            if (_dailyBadge != null && _dailyBadge.gameObject.activeSelf != daily)
                _dailyBadge.gameObject.SetActive(daily);

            int biome = _director.BiomeIndex;
            int room = _director.RoomIndex;
            var section = _director.StageSection;
            if (biome != _shownBiome || room != _shownRoom || section != _shownSection)
            {
                _shownBiome = biome;
                _shownRoom = room;
                _shownSection = section;
                _progressText.text = BuildProgress(biome, section);
            }

            var contract = _director.ActiveContract;
            string contractId = contract != null ? contract.Id : null;
            if (!string.Equals(contractId, _shownContractId, System.StringComparison.Ordinal))
            {
                _shownContractId = contractId;
                if (contract == null
                    || contract.RiskTier == Shmup.Core.Simulation.ContractRiskTier.Safe)
                {
                    // 표준 항로는 표시하지 않는다 — 무보정 상태가 기본값이다.
                    _contractText.text = "";
                }
                else
                {
                    string name = contract.Id.StartsWith("contract_")
                        ? contract.Id.Substring("contract_".Length)
                        : contract.Id;
                    _contractText.text =
                        $"CONTRACT: {name.Replace('_', ' ').ToUpperInvariant()}";
                    _contractText.color =
                        contract.RiskTier == Shmup.Core.Simulation.ContractRiskTier.Low
                            ? new UnityEngine.Color(0.35f, 0.65f, 1f, 1f)
                            : contract.RiskTier == Shmup.Core.Simulation.ContractRiskTier.Extreme
                                ? new UnityEngine.Color(1f, 0.32f, 0.28f, 1f)
                                : new UnityEngine.Color(1f, 0.62f, 0.25f, 1f);
                }
            }

            // 재출격하면 판돈은 정산 전으로 돌아간다 — 다음 최종전에서 다시 알려야 한다.
            if (_wagerBannerShown && !_director.FinalWagerCommitted)
                _wagerBannerShown = false;

            // 바이옴이 바뀐 순간 배너
            if (biome != _bannerBiome && biome > 0 && !_director.IsRunFinished)
            {
                _bannerBiome = biome;
                _bannerAge = 0f;
                _bannerText.text = $"BIOME {biome}  -  {ThemeName(_director.CurrentThemeId)}";
                // 데일리라는 사실은 첫 스테이지 배너에서 한 번만 선언한다.
                SetBannerDailyHeader(daily && biome == 1);
            }
            // 타임루프 고스트 합류 (REQ-109). 최종 바이옴 Closing에서 St1의 내 입력이
            // 재생되며 반투명 기체 하나가 붙는다. 바이옴은 그대로라 진입 배너가 뜨지
            // 않으므로, 정체 불명의 기체가 조용히 나타나지 않게 여기서 한 줄 선언한다.
            // 판돈 배너보다 앞에 둔다 — 합류(Closing)가 판돈(최종 보스 진입)보다 먼저다.
            else if (_shownGhostSpawn != _director.GhostSpawnSequence)
            {
                _shownGhostSpawn = _director.GhostSpawnSequence;
                _bannerAge = 0f;
                _bannerText.text = UiText.GhostJoinBanner;
                SetBannerDailyHeader(false);
            }
            // 최종전 판돈 (REQ-104). 최종 보스 진입 경계에서 Core가 남은 컨티뉴를
            // 전부 회수해 실드와 점수로 바꾼다. 바이옴은 그대로라 진입 배너가 뜨지
            // 않으므로, 재고가 조용히 사라진 것으로 보이지 않게 여기서 한 번 알린다.
            else if (!_wagerBannerShown && _director.FinalWagerCommitted)
            {
                _wagerBannerShown = true;
                int shields = _director.FinalWagerShieldGranted;
                int overflow = _director.FinalWagerOverflowConverted;
                if (shields > 0 || overflow > 0)
                {
                    _bannerAge = 0f;
                    _bannerText.text = shields > 0
                        ? string.Format(UiText.FinalWagerShieldFormat, shields)
                        : string.Format(
                            UiText.FinalWagerOverflowFormat,
                            overflow,
                            _director.FinalWagerScoreBonus.ToString("N0"));
                    SetBannerDailyHeader(false);
                }
            }

            if (_bannerAge < BannerSeconds)
            {
                _bannerAge += Time.deltaTime;
                if (!_bannerRoot.activeSelf) _bannerRoot.SetActive(true);
                // 끝에서 부드럽게 사라짐
                float fade = Mathf.Clamp01((BannerSeconds - _bannerAge) / 0.5f);
                var color = UiKit.TextAccent;
                color.a = fade;
                _bannerText.color = color;
                if (_bannerDailyShown && _bannerDailyText != null)
                    _bannerDailyText.color = color;
            }
            else if (_bannerRoot.activeSelf)
            {
                _bannerRoot.SetActive(false);
            }
        }

        /// <summary>
        /// 배너 윗줄(DAILY CHALLENGE) 켜기/끄기. 켜지면 띠를 그만큼 키우고 본문을 아래 칸으로
        /// 밀어 두 줄이 겹치지 않게 한다 — 배너 조립은 그대로 두고 배치만 바꾼다.
        /// </summary>
        void SetBannerDailyHeader(bool on)
        {
            if (_bannerDailyText == null || _bannerRect == null) return;
            if (_bannerDailyShown == on) return;
            _bannerDailyShown = on;
            _bannerDailyText.gameObject.SetActive(on);
            _bannerRect.sizeDelta = new Vector2(
                0f, on ? BannerHeightDaily : BannerHeight);
            var textRect = _bannerText.rectTransform;
            textRect.offsetMax = new Vector2(
                textRect.offsetMax.x, on ? -(BannerHeightDaily - BannerHeight) : 0f);
        }

        /// <summary>
        /// 룸 카운터 대신 **지금 어느 구간인지**를 보여 준다. 분기가 사라지고 스테이지
        /// 안이 전반 → 중간보스 → 후반 → 보스로 흐르게 됐으므로(REQ-054), 남은 룸 수보다
        /// "다음에 무엇이 오는지"가 플레이어에게 쓸모 있는 정보다.
        /// </summary>
        string BuildProgress(int biome, RunStageSection section)
        {
            int biomeCount = Mathf.Max(1, _director.BiomeCount);
            var sb = new System.Text.StringBuilder(48);
            sb.Append("STAGE ").Append(biome).Append('/').Append(biomeCount);
            sb.Append("   ").Append(SectionLabel(section));
            return sb.ToString();
        }

        static string SectionLabel(RunStageSection section)
        {
            switch (section)
            {
                case RunStageSection.Opening: return "ADVANCE  >  mid-boss";
                case RunStageSection.MidBoss: return "MID-BOSS";
                case RunStageSection.Closing: return "ADVANCE  >  boss";
                case RunStageSection.StageBoss: return "BOSS";
                case RunStageSection.HiddenOpening: return "!! UNCHARTED !!";
                case RunStageSection.HiddenBoss: return "!! COLOSSUS !!";
                default: return "";
            }
        }

        string ThemeName(string themeId)
        {
            if (_themeIds == null || _themeNames == null || themeId == null) return "";
            int count = Mathf.Min(_themeIds.Length, _themeNames.Length);
            for (int i = 0; i < count; i++)
                if (string.Equals(_themeIds[i], themeId, System.StringComparison.Ordinal))
                    return _themeNames[i];
            return themeId;
        }
    }
}

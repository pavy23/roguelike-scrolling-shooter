using Shmup.Core.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 타이틀 화면 (UGUI + 픽셀 폰트). 스타필드가 천천히 흐르고, Space/Enter/(A)로 출격한다.
    ///
    /// 시드는 방문할 때마다 새로 뽑는다. 이건 "이번 런을 무엇으로 할지"의 선택일 뿐이고
    /// (Presentation 소관), 같은 시드를 넣으면 같은 런이 나오는 것은 Core가 보장한다.
    ///
    /// 시드를 **보고 고치는** 수단(표시 줄·숫자 입력·리롤 버튼)은 개발용 재현 도구라
    /// <see cref="DevArgs.DevMode"/>에서만 나온다 — <see cref="_seedUi"/> 참고.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleScreen : MonoBehaviour
    {
        [SerializeField] Transform[] _layers;
        [SerializeField] float[] _factors;
        [SerializeField] float _tileWidth = 24f;
        [SerializeField] float _driftSpeed = 1.2f;
        [SerializeField] Font _font;
        [SerializeField] Font _fontBold;

        string _seedText;
        Text _promptText, _seedValueText;
        GameObject _devRoot;
        string _shownSeed;

        /// <summary>
        /// 개발 도구 안의 시드 표시·입력을 제공하는가 = <see cref="DevArgs.DevMode"/>.
        ///
        /// 같은 판을 다시 돌리는 개발/디버깅 도구다. 릴리스에서 플레이어에게
        /// 시드 칸을 보여 주면 (a) 무슨 숫자인지 설명할 자리가 없고 (b) 손으로 고정한 런은
        /// 어차피 스코어보드 제출이 막혀 있어 눌러 봐야 손해만 본다. 그래서 통째로 감춘다 —
        /// 시드 값 자체는 그대로 <see cref="NewRandomSeed"/>로 뽑으므로 런의 동작은 같다.
        /// Start에서 한 번 읽어 캐시한다: 화면이 사는 동안 값이 바뀔 일이 없고, 생성 시점과
        /// Update의 판정이 어긋나면 없는 텍스트를 만지게 된다.
        /// </summary>
        bool _seedUi;

        /// <summary>
        /// 지금 칸에 있는 시드를 사람이 직접 쳐 넣었는가 (스코어보드 공정성).
        /// 같은 시드를 손으로 넣으면 같은 판을 몇 번이고 연습할 수 있고, 그렇게 만든
        /// 점수가 랜덤 시드 기록과 같은 보드에 서면 보드가 무의미해진다.
        /// 리롤(랜덤)로 되돌리면 다시 꺼진다 — 낙인은 출격 시점의 값으로 굳는다.
        /// </summary>
        bool _seedManual;
        bool _shownSeedManual;
        Shmup.Core.Simulation.RunSuspendData _suspended;
        ReplayFileData _replay;
        int _dailyDateInt;
        /// <summary>오늘(UTC)의 MM-dd. 데일리 안내/버튼이 같은 문자열을 쓰도록 한 번만 만든다.</summary>
        string _dailyDateLabel = "";
        Text _difficultyButtonLabel;

        // 글로벌 랭킹 패널 (P1). 처음 눌렀을 때 한 번만 조립한다 — 타이틀에 온 사람 중
        // 보드를 여는 쪽이 소수라 로드 시점에 미리 만들 이유가 없다.
        GameObject _rankingRoot;
        Text _rankingBody;
        /// <summary>컬럼별 본문. 순서는 RankingColumns와 같다.</summary>
        Text[] _rankingCols;
        Text _rankingTitle;
        ScrollRect _rankingScroll;
        RectTransform _rankingContent;

        /// <summary>
        /// 랭킹 패널이 지금 보여 주는 보드. 예전에는 데일리로 **하드코딩**돼 있었는데,
        /// 데일리는 그날 같은 시드로 뛴 기록만 담겨 대개 비어 있다. 그래서 전체 보드에
        /// 기록이 8개 있는데도 화면에는 아무것도 안 보였다 (사람 보고 2026-08-03:
        /// "스코어보드에 기록이 표시되지 않아").
        ///
        /// 기본값은 **전체**다 — 비어 있을 일이 거의 없어 "고장인가?"를 만들지 않는다.
        /// 데일리는 버튼으로 전환해서 본다.
        /// </summary>
        bool _rankingDaily;

        /// <summary>
        /// 출격 모드. 데일리는 "다른 시드로 한 판"이 아니라 모두가 같은 시드로 겨루는
        /// 스코어링 챌린지라 별도 모드다. 예전에는 각자 다른 버튼에서 바로 출발해
        /// 출격 버튼이 두 개였다 — 이제 여기서 고르고 LAUNCH 하나로 나간다.
        /// </summary>
        bool _dailyMode;

        // ── 개발자 패널 (사람 지시 2026-08-04) ───────────────────────────────
        //
        // "각 스테이지 (히든 보스 포함) 선택 플레이 버튼이 별도로 있으면 좋겠어.
        //  스테이지 및 보스 패턴 디버깅 및 테스트 필요해."
        //
        // URL 인자(?stage=3&warp=boss)로도 같은 일을 할 수 있지만 주소창을 고칠 수
        // 있어야 한다. 폰이나 배포판에서 보스를 확인하려면 화면 안에 스위치가 필요하다.
        // 개발 모드에서만 나오고, 여기서 시작한 런은 **점수 제출이 막힌다.**
        int _devStage = 1;          // 1~5, 0 = 미지의 구역(히든 보스)
        int _devWarp;               // 0 없음 / 1 중간보스 / 2 보스
        int _devTheme;              // 0 자동, 그 외 DevThemes 인덱스
        int _devColossal;           // 0 자동 / 1 레비아탄 / 2 브루드마더
        bool _devGod = true;
        bool _devMaxPower = true;
        Text _devStageLabel, _devWarpLabel, _devGodLabel, _devPowerLabel, _devThemeLabel;

        /// <summary>테마 선택 목록. waves.json의 themes 순서와 같다.</summary>
        static readonly string[] DevThemes =
            { "scrapyard", "hive", "fortress", "nebula", "core" };
        static readonly string[] DevColossals = { "", "leviathan", "broodmother" };

        Text _modeButtonLabel;
        Text _launchButtonLabel;

        /// <summary>
        /// 보드 표시 줄 수 = 서버 보관 상한(워커 MAX_ENTRIES)과 같다. 예전에는 상위
        /// 10줄만 올렸는데, 스크롤 뷰포트가 생기면서 받은 만큼 다 올린다 (사람 지시
        /// 2026-08-07: "스크롤로 100명까지 볼수 있게"). 화면에 한 번에 보이는 것은
        /// 여전히 뷰포트 높이(RankingViewportHeight)가 정한다 — 약 10줄.
        /// </summary>
        const int RankingRows = 100;

        /// <summary>기록 영역(뷰포트) 높이. 10줄이 들어가는 168px — 패널 288 안에서
        /// 제목/헤더(64) + 하단 버튼(56)을 뺀 값이다.</summary>
        const float RankingViewportHeight = 168f;

        // ── 보드 컬럼 폭 ──────────────────────────────────────────────────────
        //
        // "1 PAVY 123,450 ST 3-2 NB" 한 줄로는 뭐가 뭔지 알 수 없다는 지적(2026-08-01).
        // 컬럼 폭을 상수 한 벌로 뽑아 **헤더와 본문이 같은 자릿수**를 쓰게 한다 —
        // 둘을 따로 적어 두면 언젠가 반드시 어긋난다.
        const int ColPilot = 10;   // ScoreboardClient.NameMaxLength와 같다

        /// <summary>피격 수 (REQ-105). 서버 상한이 999라 세 자리 + 여백 한 칸.</summary>

        /// <summary>
        /// 컨티뉴 마커 폭 (REQ-109). " C1" 세 글자.
        ///
        /// **컬럼이 아니라 마커다.** HIT까지 붙어 한 줄이 이미 47자라 여덟 번째 컬럼을
        /// 세우면 라벨 줄과 본문이 화면 밖으로 밀린다. 그래서 헤더 라벨을 주지 않고
        /// PILOT 칸을 세 칸 넓혀 **이름 바로 뒤에** 붙인다 — 파일럿에 딸린 주석으로
        /// 읽히지 자기 축을 가진 통계로 읽히지 않는다.
        ///
        /// 컨티뉴를 쓰지 않은 기록과 <c>cu</c> 키가 없던 구 기록은 똑같이 빈칸이다.
        /// 마커의 뜻이 "이어붙였다"이지 "안 이어붙였다"가 아니므로, 모르는 기록에
        /// 아무 표시도 하지 않는 쪽이 정직하다 (BOMB/HIT의 0 강조와 정반대 문법).
        /// </summary>

        /// <summary>
        /// 컬럼 라벨 줄. 계기판 라벨 관례대로 전부 대문자이고, 본문보다 어두운 색으로
        /// 그려 기록보다 먼저 읽히지 않게 한다 (색은 BuildRankingPanel이 준다).
        /// PILOT 라벨은 마커 폭까지 덮는다 — 마커에는 라벨을 주지 않는다.
        /// </summary>
        /// <summary>
        /// 보드 컬럼 명세: 라벨 · 왼쪽 x(0~480) · 폭 · 오른쪽 정렬 여부.
        /// 숫자(점수·봄·피격)는 오른쪽, 글자(파일럿·스테이지·기체)는 왼쪽 —
        /// 자릿수가 다른 숫자는 오른쪽 끝을 맞춰야 한 눈에 비교된다.
        /// </summary>
        static readonly (string Label, float Left, float Width, bool Right)[]
            RankingColumns =
        {
            ("#", 0f, 24f, true),
            ("PILOT", 32f, 122f, false),
            ("SCORE", 158f, 86f, true),
            ("STG", 252f, 44f, false),
            ("SHIP", 300f, 50f, false),
            ("BOMB", 352f, 46f, true),
            ("HIT", 402f, 42f, true),
        };

        void RefreshDifficultyText()
        {
            // 데일리는 NORMAL 고정이다 (사람 결정 2026-08-04) — 모두가 같은 조건으로
            // 겨루는 것이 데일리의 존재 이유다. 고른 난이도가 적용되지 않는데 화면이
            // 그대로면 "설정이 먹지 않는다"로 읽히므로, 여기서 그 사실을 말한다.
            string label = _dailyMode ? "NORMAL (DAILY)" : DifficultySelect.Label;
            if (_difficultyButtonLabel != null)
                _difficultyButtonLabel.text = (UiPlatform.TouchMode ? "DIFFICULTY" : "DIFFICULTY [T]/UP")
                    + "\n" + label;
        }

        void CycleDifficulty()
        {
            DifficultySelect.Index = (DifficultySelect.Index + 1) % 3;
            RefreshDifficultyText();
        }

        void ToggleMode()
        {
            _dailyMode = !_dailyMode;
            RefreshModeText();
            RefreshDifficultyText();   // 데일리는 난이도가 고정된다 - 화면도 따라가야 한다
        }

        void RefreshModeText()
        {
            if (_modeButtonLabel != null)
                _modeButtonLabel.text = _dailyMode
                    ? string.Format(UiText.ModeButtonDaily, _dailyDateLabel)
                    : UiText.ModeButtonNormal;
            if (_launchButtonLabel != null)
                _launchButtonLabel.text = _dailyMode ? "LAUNCH DAILY" : "LAUNCH";
            if (!UiPlatform.TouchMode && _modeButtonLabel != null)
                _modeButtonLabel.text = _modeButtonLabel.text.Replace("MODE", "MODE [D]/RB");
            RefreshLaunchHint();
        }

        void StartDailyRun()
        {
            DevArgs.RuntimeSeed = (long)Shmup.Core.DailySeed.FromDate(_dailyDateInt);
            // 스코어보드가 데일리 보드로 가르는 유일한 근거 — 시드와 같은 채널로 넘긴다.
            DevArgs.RuntimeDaily = true;
            // 데일리는 전원이 같은 시드로 겨루는 판이다 — 손으로 친 시드와 성격이 정반대다.
            DevArgs.RuntimeSeeded = false;
            SceneManager.LoadScene("Battle");
        }

        void ContinueRun()
        {
            if (_suspended == null) return;
            // 저장 파일은 삭제하지 않는다 — 복원이 성공한 뒤 BattleDirector가 지운다
            BattleDirector.PendingResume = _suspended;
            DevArgs.RuntimeSeed = (long)_suspended.runSeed;
            DevArgs.RuntimeDaily = false;
            // 이어하기의 시드는 그 런이 시작될 때 이미 정해진 값이다 — 지금 칸에 뭐가
            // 적혀 있든 상관없다 (BattleDirector도 이어하기 런은 낙인에서 뺀다).
            DevArgs.RuntimeSeeded = false;
            SceneManager.LoadScene("Battle");
        }

        void PlayReplay()
        {
            if (_replay == null) return;
            BattleDirector.PendingReplay = _replay;
            DevArgs.RuntimeSeed = _replay.seed;
            DevArgs.RuntimeDaily = false;
            DevArgs.RuntimeSeeded = false;   // 재생은 기록의 재현 — 제출 경로가 원래 닫혀 있다
            SceneManager.LoadScene("Battle");
        }

        /// <summary>
        /// 새 시드 생성. TickCount 단독은 부팅 후 시간이 길수록 상위 자릿수가 고정돼
        /// (예: 전부 4294xxxxxx) "맨날 같은 시드"로 체감된다 — 사람 지적 2026-08-01.
        /// 시계·GUID를 곱셈 해시로 섞어 자릿수 전체가 움직이게 한다. 시드 '선택'은
        /// Presentation 소관이라 여기서 섞어도 결정론(같은 시드 = 같은 런)과 무관하다.
        /// </summary>
        internal static uint NewRandomSeed()
        {
            unchecked
            {
                uint mixed = (uint)System.Environment.TickCount * 2654435761u;
                mixed ^= (uint)System.DateTime.Now.Ticks;
                mixed ^= (uint)System.Guid.NewGuid().GetHashCode() * 2246822519u;
                return mixed;
            }
        }

        void RerollSeed()
        {
            _seedText = NewRandomSeed().ToString();
            // 랜덤으로 다시 뽑았으면 손으로 친 흔적은 사라진다 — 제출 자격도 돌아온다.
            _seedManual = false;
        }

        // ── 글로벌 랭킹 (P1 스코어보드) ────────────────────────────────────────

        /// <summary>
        /// 랭킹 모달이 떠 있는가. 같은 GameObject의 격납고가 이 값을 보고 입력을 멈춘다 —
        /// 보드를 읽는 동안 뒤에서 함선이 바뀌거나 크레딧이 나가면 안 된다.
        /// </summary>
        public bool RankingOpen => _rankingRoot != null && _rankingRoot.activeSelf;
        public bool DevPanelOpen => _devRoot != null && _devRoot.activeSelf;
        public bool ModalOpen => RankingOpen || DevPanelOpen;

        void ToggleRanking()
        {
            if (_rankingRoot == null) BuildRankingPanel();
            if (_rankingRoot == null) return;
            bool open = !_rankingRoot.activeSelf;
            _rankingRoot.SetActive(open);
            // 열 때마다 새로 받는다 — 데일리 보드는 하루 종일 움직인다.
            if (open) RequestRanking();
        }

        void CloseRanking()
        {
            if (_rankingRoot != null) _rankingRoot.SetActive(false);
        }

        /// <summary>
        /// 계기판 언어 그대로: 딤 + 헤어라인 패널 + 앰버 룰 한 줄. 이 모달 안에서
        /// 유일한 동작이 CLOSE라 여기서는 그쪽이 주 동작(챔퍼 블록)이다.
        /// </summary>
        void BuildRankingPanel()
        {
            var canvas = UiKit.CreateCanvas("RankingCanvas", 60);
            canvas.transform.SetParent(transform, false);
            _rankingRoot = canvas.gameObject;

            UiKit.CreateDim(canvas.transform, new Color(0f, 0.01f, 0.05f, 0.72f)).raycastTarget = true;
            // 컬럼 7개(순위·파일럿·점수·스테이지·기체·봄·피격)를 고정폭으로 세우려면
            // 380px로는 모자란다. HIT 칸이 붙으면서 한 줄이 42자 → 47자가 됐으므로
            // 같은 비율로 폭만 다시 키운다. 헤어라인 패널 언어는 그대로다.
            // 컨티뉴 마커(REQ-109)가 PILOT 칸에 세 글자를 더해 50자가 됐다 — 새 컬럼이
            // 아니라 마커라 라벨은 늘지 않지만, 자릿수만큼 폭은 따라가야 한다.
            // 640 기준 폭이라 520이 상한선에 가깝다: 여기서 더 늘리면 좌우 여백이 사라진다.
            var panel = UiKit.CreatePanel(canvas.transform, new Vector2(520f, 288f));

            _rankingTitle = UiKit.CreateCornerText(panel, _fontBold, "ALL-TIME RANKING", 14,
                UiKit.TextMain, new Vector2(0.5f, 1f), new Vector2(0f, -12f),
                TextAnchor.UpperCenter, "RankTitle");
            UiKit.CreateRule(panel, new Vector2(0.5f, 1f), new Vector2(0f, -34f), 460f,
                UiKit.TextAccent, "RankRule");

            // **컬럼마다 Text를 따로 둔다.** 예전에는 한 문자열을 공백 패딩으로
            // 정렬했는데, UI 폰트(Galmuri9)가 고정폭이 아니라 공백과 글자 폭이
            // 달라서 폰에서 컬럼이 제각각 밀렸다 (사람 보고 2026-08-05, 스크린샷).
            // 왼쪽 정렬 시작점인 PILOT만 맞아 보였던 이유다. 컬럼이 각자 앵커를
            // 가지면 글리프 폭과 무관하게 항상 선다.
            Text MakeColumn(
                Transform parent, string name, float left, float width, bool right,
                float y, float height, Color color)
            {
                var text = UiKit.CreateCornerText(
                    parent, _font, "", 10, color,
                    new Vector2(0.5f, 1f),
                    new Vector2(left + width / 2f - 240f, y),
                    right ? TextAnchor.UpperRight : TextAnchor.UpperLeft,
                    name);
                text.rectTransform.sizeDelta = new Vector2(width, height);
                return text;
            }

            // 기록은 뷰포트(마스크) 속 콘텐츠에 그린다 — 서버가 최대 100줄을 주는데
            // 화면에는 10줄 높이만 있으므로, 넘치는 줄은 세로 스크롤로 본다
            // (사람 지시 2026-08-07). 드래그(터치)와 휠 둘 다 ScrollRect가 받는다.
            var viewportGo = new GameObject("RankViewport");
            viewportGo.transform.SetParent(panel, false);
            var viewportRect = viewportGo.AddComponent<RectTransform>();
            viewportRect.anchorMin = viewportRect.anchorMax = new Vector2(0.5f, 1f);
            viewportRect.pivot = new Vector2(0.5f, 1f);
            viewportRect.anchoredPosition = new Vector2(0f, -64f);
            viewportRect.sizeDelta = new Vector2(500f, RankingViewportHeight);
            // 투명해도 레이캐스트는 잡힌다 — 드래그를 받을 히트 영역.
            var viewportHit = viewportGo.AddComponent<Image>();
            viewportHit.color = Color.clear;
            viewportGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("RankContent");
            contentGo.transform.SetParent(viewportGo.transform, false);
            _rankingContent = contentGo.AddComponent<RectTransform>();
            _rankingContent.anchorMin = _rankingContent.anchorMax = new Vector2(0.5f, 1f);
            _rankingContent.pivot = new Vector2(0.5f, 1f);
            _rankingContent.anchoredPosition = Vector2.zero;
            _rankingContent.sizeDelta = new Vector2(480f, RankingViewportHeight);

            _rankingScroll = viewportGo.AddComponent<ScrollRect>();
            _rankingScroll.content = _rankingContent;
            _rankingScroll.viewport = viewportRect;
            _rankingScroll.horizontal = false;
            _rankingScroll.movementType = ScrollRect.MovementType.Clamped;
            _rankingScroll.scrollSensitivity = 17f; // 휠 한 칸 ≈ 한 줄

            _rankingCols = new Text[RankingColumns.Length];
            for (int i = 0; i < RankingColumns.Length; i++)
            {
                var col = RankingColumns[i];
                var label = MakeColumn(
                    panel, "RankHead_" + col.Label, col.Left, col.Width, col.Right,
                    -44f, 14f, UiKit.TextDim);
                label.text = col.Label;
                _rankingCols[i] = MakeColumn(
                    _rankingContent, "RankCol_" + col.Label, col.Left, col.Width,
                    col.Right, 0f, RankingViewportHeight, UiKit.TextMain);
            }

            // 얇은 스크롤바. 기록이 뷰포트를 넘칠 때만 나타난다 — 10줄 이하일 때는
            // 지금까지처럼 아무것도 없다.
            var barGo = new GameObject("RankScrollbar");
            barGo.transform.SetParent(panel, false);
            var barRect = barGo.AddComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(252f, -64f);
            barRect.sizeDelta = new Vector2(4f, RankingViewportHeight);
            var barBg = barGo.AddComponent<Image>();
            barBg.color = UiKit.PanelBorder;
            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(barGo.transform, false);
            var handleRect = handleGo.AddComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = UiKit.TextDim;
            var bar = barGo.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handleRect;
            bar.targetGraphic = handleImg;
            _rankingScroll.verticalScrollbar = bar;
            _rankingScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;
            // 라벨과 기록을 가르는 헤어라인. 앰버는 위 룰 하나로 족하다 —
            // 액센트는 화면당 하나라는 계기판 원칙을 여기서도 지킨다.
            UiKit.CreateRule(panel, new Vector2(0.5f, 1f), new Vector2(0f, -58f), 480f,
                UiKit.PanelBorder, "RankHeaderRule");

            // 상태 문구(LOADING/OFFLINE/비어 있음) 전용. 기록은 컬럼이 그린다.
            _rankingBody = UiKit.CreateCornerText(panel, _font, "", 10, UiKit.TextDim,
                new Vector2(0.5f, 1f), new Vector2(0f, -64f), TextAnchor.UpperCenter, "RankBody");
            _rankingBody.rectTransform.sizeDelta = new Vector2(480f, 168f);

            UiKit.CreateTouchButton(panel, _font, "DAILY / ALL", 11,
                new Vector2(0.5f, 0f), new Vector2(-90f, 12f), new Vector2(140f, 34f),
                ToggleRankingBoard, "RankToggle");
            UiKit.CreateTouchButton(panel, _font, "CLOSE", 11,
                new Vector2(0.5f, 0f), new Vector2(90f, 12f), new Vector2(140f, 34f),
                CloseRanking, "RankClose", accent: true);

            _rankingRoot.SetActive(false);
        }

        /// <summary>전체 ↔ 데일리 전환. 보드가 바뀌면 즉시 다시 조회한다.</summary>
        void ToggleRankingBoard()
        {
            _rankingDaily = !_rankingDaily;
            RequestRanking();
        }

        void RequestRanking()
        {
            if (_rankingBody == null) return;
            ShowRankingStatus("LOADING...");
            if (_rankingTitle != null)
                _rankingTitle.text = _rankingDaily ? "DAILY RANKING" : "ALL-TIME RANKING";
            ScoreboardClient.FetchBoard(_rankingDaily, OnRankingLoaded);
        }

        /// <summary>
        /// 서버가 없거나 회선이 끊겨도 타이틀은 멀쩡해야 한다 — 실패는 OFFLINE 한 단어로
        /// 끝내고 이유는 개발 빌드 로그(ScoreboardClient)에만 남긴다.
        /// </summary>
        void OnRankingLoaded(ScoreboardEntry[] entries, string error)
        {
            // 응답이 늦게 오면 이미 Battle 씬으로 넘어가 이 화면이 없을 수 있다.
            if (this == null || _rankingBody == null) return;

            if (error != null || entries == null)
            {
                ShowRankingStatus("OFFLINE");
                return;
            }
            if (entries.Length == 0)
            {
                // 데일리가 비어 있는 것은 정상이다(그날 아무도 안 뛰었을 뿐) —
                // 고장으로 읽히지 않게 어느 보드가 비었는지 말해 준다.
                ShowRankingStatus(_rankingDaily
                    ? "NO DAILY ENTRIES YET - TRY ALL-TIME"
                    : "NO ENTRIES YET");
                return;
            }

            _rankingBody.text = "";
            var cols = new System.Text.StringBuilder[_rankingCols.Length];
            for (int c = 0; c < cols.Length; c++)
                cols[c] = new System.Text.StringBuilder(128);
            int count = Mathf.Min(RankingRows, entries.Length);
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                for (int c = 0; c < cols.Length; c++)
                    if (cols[c].Length > 0) cols[c].Append('\n');
                AppendRowCells(cols, i + 1, entry);
            }
            for (int c = 0; c < cols.Length; c++)
                _rankingCols[c].text = cols[c].ToString();

            // 콘텐츠 높이 = 실제 채워진 텍스트 높이. 픽셀 폰트는 명목 크기와 실제
            // 줄 높이가 달라서 "줄 수 × 상수"로 어림하지 않고 preferredHeight로 잰다.
            // 새 보드를 받으면 스크롤은 항상 맨 위(1위)로 돌아간다.
            float rowsHeight = Mathf.Max(
                RankingViewportHeight, _rankingCols[0].preferredHeight + 4f);
            _rankingContent.sizeDelta = new Vector2(480f, rowsHeight);
            // 셀 rect도 같은 높이로 늘린다. 뷰포트 높이(168)로 두면 스크롤로 rect가
            // 마스크 밖으로 완전히 나가는 순간 RectMask2D가 그래픽 전체를 컬링해
            // 오버플로로 그리던 아랫줄까지 통째로 사라진다 (헤드리스 검증에서 확인).
            for (int c = 0; c < _rankingCols.Length; c++)
                _rankingCols[c].rectTransform.sizeDelta = new Vector2(
                    _rankingCols[c].rectTransform.sizeDelta.x, rowsHeight);
            _rankingScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>상태 한 줄을 가운데에 띄우고 기록 컬럼은 비운다.</summary>
        void ShowRankingStatus(string message)
        {
            _rankingBody.text = message;
            _rankingBody.color = UiKit.TextDim;
            if (_rankingContent != null)
            {
                // 이전 보드의 스크롤 높이가 남으면 빈 화면이 스크롤된다.
                _rankingContent.sizeDelta = new Vector2(480f, RankingViewportHeight);
                if (_rankingScroll != null)
                    _rankingScroll.verticalNormalizedPosition = 1f;
            }
            if (_rankingCols == null) return;
            for (int c = 0; c < _rankingCols.Length; c++)
                if (_rankingCols[c] != null) _rankingCols[c].text = "";
        }

        /// <summary>앰버 뱃지 색 = UiKit.TextAccent. 리치 텍스트라 문자열로 박아 둔다.</summary>
        const string BadgeOpen = "<color=#FFB31C>";
        const string BadgeClose = "</color>";

        /// <summary>
        /// 흐린 마커 색 = UiKit.TextDim. 본문(TextMain)보다 어두워 기록을 먼저 읽고
        /// 나서 눈에 들어온다 — 컨티뉴 마커처럼 "곁들이는 사실"에만 쓴다.
        /// </summary>
        const string DimOpen = "<color=#77818C>";
        const string DimClose = "</color>";

        /// <summary>
        /// 보드 한 줄: 순위 · 파일럿 · 점수 · 달성 스테이지 · 기체 · 봄 · 피격 수.
        /// 헤더(<see cref="RankingHeader"/>)와 같은 컬럼 상수를 쓰고, 값이 없는 칸은
        /// '-'로 채워 자릿수를 지킨다 — 칸을 비우면 다음 컬럼이 밀려 헤더와 어긋난다.
        /// </summary>
        static void AppendRowCells(
            System.Text.StringBuilder[] cols, int rank, ScoreboardEntry entry)
        {
            // P1.5 이전 기록에는 상세 통계가 아예 없다 (서버가 키를 뺀다 → 전부 0).
            // 스테이지 번호는 1부터라 st <= 0이 곧 구 항목이고, 그때 0을 그리면
            // "1스테이지에서 봄 0개로 죽었다"는 거짓말이 된다.
            bool detailed = entry.st > 0;

            cols[0].Append(rank);
            AppendPilotCell(cols[1], entry);
            cols[2].Append(entry.s.ToString("N0"));
            cols[3].Append(StageCell(entry, detailed));
            cols[4].Append(ShipCell(entry));
            // 난이도 마커는 기체 코드가 아니다 — 붙여 쓰면 "STH"가 기체 약칭으로
            // 읽힌다. 말할 것이 있을 때만 한 칸 띄우고 흐리게 적는다.
            string mark = DifficultyMark(entry).TrimEnd();
            if (mark.Length > 0)
                cols[4].Append(DimOpen).Append(' ').Append(mark).Append(DimClose);
            AppendBombCell(cols[5], entry, detailed);
            AppendHitCell(cols[6], entry);
        }

        /// <summary>
        /// 난이도 마커. 컨티뉴 마커와 같은 문법이다 — **컬럼이 아니라 주석**이라
        /// 헤더 라벨을 주지 않고, 말할 것이 있을 때만 글자를 낸다.
        ///
        /// NORMAL과 "난이도를 모르는 기록"은 둘 다 빈칸이다. 서버가 이 값을 돌려주기
        /// 전 기록에는 정보 자체가 없고, 거기에 N을 적으면 EASY로 낸 기록까지
        /// NORMAL로 보이게 된다. 난이도는 적 HP를 0.75~1.25배로 바꾸므로 그 거짓말의
        /// 대가가 작지 않다 (데일리를 NORMAL 고정으로 바꾼 것도 같은 이유다).
        /// </summary>
        static string DifficultyMark(ScoreboardEntry entry)
        {
            if (!entry.HasDifficulty) return " ";
            if (entry.d.StartsWith("E", System.StringComparison.OrdinalIgnoreCase))
                return "E";
            if (entry.d.StartsWith("H", System.StringComparison.OrdinalIgnoreCase))
                return "H";
            return " ";
        }

        /// <summary>
        /// 파일럿 이름 + 컨티뉴 마커 (REQ-109).
        ///
        /// 마커는 이름이 끝나는 자리에 바로 붙는다 — 칸 끝에 오른쪽 정렬하면 짧은
        /// 이름에서 마커가 허공에 떠 어느 줄 것인지 읽히지 않는다. 색은 TextDim이라
        /// 기록을 먼저 읽고 나서 눈에 들어온다: 컨티뉴는 실격이 아니라 각주다.
        ///
        /// 색 태그는 폭에 잡히지 않으므로 남은 패딩은 태그 **밖에서** 실제 글자 수로
        /// 계산해 채운다 (BOMB/HIT 칸이 태그 안쪽에 패딩을 넣는 것과 반대 방향이다 —
        /// 여기서는 강조 대상이 칸 전체가 아니라 뒤에 붙은 두 글자뿐이라 그렇다).
        /// </summary>
        static void AppendPilotCell(System.Text.StringBuilder sb, ScoreboardEntry entry)
        {
            sb.Append(Clip(entry.n, ColPilot));
            if (entry.HasContinues)
            {
                // 서버가 이미 9로 자르지만, 손상된 응답이 컬럼 폭을 밀지 않게 한 번 더 막는다.
                int continues = entry.cu > 9 ? 9 : entry.cu;
                sb.Append(DimOpen);
                sb.Append(" C").Append(continues);
                sb.Append(DimClose);
            }
        }

        /// <summary>
        /// 허용한 피격 수 (REQ-105). **적을수록 좋은** 유일한 칸이라 0을 앰버로 강조한다 —
        /// BOMB 0과 같은 문법이다(무피격 완주는 봄 없는 완주만큼 어렵다).
        ///
        /// 구 항목 판정은 다른 칸과 다르다: 0이 정상 값이라 <c>st &lt;= 0</c> 같은
        /// 자리 여유가 없어, 서버 응답에 <c>ht</c> 키가 있었는지를 그대로 쓴다
        /// (ScoreboardClient가 없는 기록에 -1을 심어 준다).
        /// </summary>
        static void AppendHitCell(System.Text.StringBuilder sb, ScoreboardEntry entry)
        {
            if (!entry.HasHits)
            {
                sb.Append('-');
                return;
            }
            string cell = entry.ht.ToString();
            if (entry.ht != 0)
            {
                sb.Append(cell);
                return;
            }
            sb.Append(BadgeOpen);
            sb.Append(cell);
            sb.Append(BadgeClose);
        }

        /// <summary>
        /// 달성 지점. 완주는 도달 좌표보다 등급이 정보다 — "5-4"보다 "CLR"이 크고,
        /// 무피격 완주(PFT)는 그보다 더 크다.
        /// </summary>
        static string StageCell(ScoreboardEntry entry, bool detailed)
        {
            if (!detailed) return "-";
            if (entry.g == "PERFECT") return "PFT";
            if (entry.g == "CLEAR") return "CLR";
            return entry.st.ToString() + "-" + (entry.rm > 0 ? entry.rm : 1).ToString();
        }

        /// <summary>기체 약칭 (ST/IC/BW). id가 비면 공백보다 '-'가 "없다"로 읽힌다.</summary>
        static string ShipCell(ScoreboardEntry entry)
        {
            string ship = ScoreboardClient.ShipAbbrev(entry.sh);
            return string.IsNullOrEmpty(ship) || ship.Trim().Length == 0 ? "-" : ship;
        }

        /// <summary>
        /// 봄 사용 횟수. 0은 "봄을 한 번도 안 쓴 주행"이라 같은 점수라도 다른 기록이다 —
        /// 예전 NB 뱃지를 대신해 숫자 0 자체를 앰버로 강조한다.
        /// 색 태그는 폭에 잡히지 않으므로 패딩을 **태그 안쪽**에 넣어야 정렬이 유지된다.
        /// </summary>
        static void AppendBombCell(
            System.Text.StringBuilder sb, ScoreboardEntry entry, bool detailed)
        {
            if (!detailed)
            {
                sb.Append('-');
                return;
            }
            string cell = entry.bb.ToString();
            if (entry.bb != 0)
            {
                sb.Append(cell);
                return;
            }
            sb.Append(BadgeOpen);
            sb.Append(cell);
            sb.Append(BadgeClose);
        }

        static string Clip(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "?";
            return value.Length <= max ? value : value.Substring(0, max);
        }

        /// <summary>
        /// 터치 전용 버튼 열. 폰에서는 키보드 단축키 안내가 아무 의미가 없으므로, 안내 텍스트는
        /// 감추고 같은 동작을 하는 버튼으로 바꿔 놓는다.
        /// </summary>
        void CycleDevStage()
        {
            // 1..5 → 0(미지의 구역) → 1
            _devStage = _devStage >= 5 ? 0 : _devStage + 1;
            RefreshDevPanel();
        }

        void CycleDevTheme()
        {
            // 미지의 구역에서는 같은 버튼이 거대 보스를 고른다 — 그쪽엔 테마가 없다.
            if (_devStage == 0)
                _devColossal = (_devColossal + 1) % DevColossals.Length;
            else
                _devTheme = (_devTheme + 1) % (DevThemes.Length + 1);
            RefreshDevPanel();
        }

        void CycleDevWarp()
        {
            _devWarp = (_devWarp + 1) % 3;
            RefreshDevPanel();
        }

        void ToggleDevGod()
        {
            _devGod = !_devGod;
            RefreshDevPanel();
        }

        void ToggleDevPower()
        {
            _devMaxPower = !_devMaxPower;
            RefreshDevPanel();
        }

        void RefreshDevPanel()
        {
            if (_devStageLabel != null)
                _devStageLabel.text = _devStage == 0
                    ? "STAGE\nUNCHARTED"
                    : $"STAGE\n{_devStage}";
            if (_devWarpLabel != null)
                _devWarpLabel.text = _devWarp == 2 ? "JUMP\nBOSS"
                    : _devWarp == 1 ? "JUMP\nMID-BOSS" : "JUMP\nOFF";
            if (_devGodLabel != null)
                _devGodLabel.text = _devGod ? "GOD\nON" : "GOD\nOFF";
            // 미지의 구역에는 테마가 없다 — 같은 버튼이 거대 보스를 고른다.
            if (_devThemeLabel != null)
                _devThemeLabel.text = _devStage == 0
                    ? (_devColossal == 0
                        ? "BOSS\nAUTO"
                        : "BOSS\n" + DevColossals[_devColossal].ToUpperInvariant())
                    : (_devTheme == 0
                        ? "THEME\nAUTO"
                        : "THEME\n" + DevThemes[_devTheme - 1].ToUpperInvariant());
            if (_devPowerLabel != null)
                _devPowerLabel.text = _devMaxPower ? "POWER\nMAX" : "POWER\nNORMAL";
        }

        /// <summary>개발자 패널로 출격. 이 런은 점수 제출이 막힌다.</summary>
        void StartDevRun()
        {
            _dailyMode = false;
            DevArgs.RuntimeDevRun = true;
            DevArgs.RuntimeGod = _devGod;
            DevArgs.RuntimeMaxPower = _devMaxPower;
            DevArgs.RuntimeUncharted = _devStage == 0;
            DevArgs.RuntimeStartStage = _devStage == 0 ? (int?)null : _devStage;
            DevArgs.RuntimeWarp = _devWarp == 2 ? RunStageSection.StageBoss
                : _devWarp == 1 ? RunStageSection.MidBoss
                : (RunStageSection?)null;
            // 미지의 구역은 구간 자체가 히든 보스라, 점프를 켜면 거대 보스 앞에서 선다.
            if (_devStage == 0 && _devWarp != 0)
                DevArgs.RuntimeWarp = RunStageSection.HiddenBoss;
            DevArgs.RuntimeTheme = _devStage == 0 || _devTheme == 0
                ? null
                : DevThemes[_devTheme - 1];
            DevArgs.RuntimeColossal = _devStage == 0 && _devColossal != 0
                ? DevColossals[_devColossal]
                : null;
            DevArgs.RuntimeDaily = false;
            DevArgs.RuntimeSeeded = false;
            StartRun();
        }

        // The developer tools are a modal, so their controls never cover the normal menu.
        void BuildDevPanel(Transform parent)
        {
            MenuButton(parent, UiPlatform.TouchMode ? "DEV TOOLS" : "DEV [F2]/SELECT",
                new Vector2(1f, 1f), new Vector2(-12f, -26f), new Vector2(110f, 40f),
                ToggleDevPanel, "DevToolsButton");
            var canvas = UiKit.CreateCanvas("DevCanvas", 60);
            canvas.transform.SetParent(transform, false);
            _devRoot = canvas.gameObject;
            UiKit.CreateDim(canvas.transform, new Color(0f, 0.01f, 0.05f, 0.85f)).raycastTarget = true;
            var panel = UiKit.CreatePanel(canvas.transform, new Vector2(352f, 296f), "DevPanel");
            UiKit.CreateCornerText(panel, _fontBold, "DEV TOOLS / NO SCORE SUBMIT", 12,
                UiKit.TextAccent, new Vector2(0.5f, 1f), new Vector2(0f, -12f),
                TextAnchor.UpperCenter, "DevHeader");
            _seedValueText = UiKit.CreateCornerText(panel, _font, "", 10,
                UiKit.TextMain, new Vector2(0.5f, 1f), new Vector2(0f, -36f),
                TextAnchor.UpperCenter, "Seed");
            _seedValueText.rectTransform.sizeDelta = new Vector2(328f, 40f);

            Text Add(string name, int column, int row, UnityEngine.Events.UnityAction action)
            {
                var button = UiKit.CreateTouchButton(panel, _font, "", 10,
                    new Vector2(0.5f, 1f), new Vector2(column == 0 ? -83f : 83f, -84f - row * 46f),
                    new Vector2(158f, 40f), action, name);
                return button.GetComponentInChildren<Text>();
            }
            _devStageLabel = Add("DevStage", 0, 0, CycleDevStage);
            _devWarpLabel = Add("DevWarp", 1, 0, CycleDevWarp);
            _devThemeLabel = Add("DevTheme", 0, 1, CycleDevTheme);
            _devGodLabel = Add("DevGod", 1, 1, ToggleDevGod);
            _devPowerLabel = Add("DevPower", 0, 2, ToggleDevPower);
            UiKit.CreateTouchButton(panel, _font, "CLOSE", 10,
                new Vector2(0.5f, 1f), new Vector2(83f, -176f), new Vector2(158f, 40f),
                ToggleDevPanel, "DevClose");
            UiKit.CreateTouchButton(panel, _fontBold, "DEV LAUNCH", 16,
                new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(324f, 44f),
                StartDevRun, "DevLaunch", accent: true);
            RefreshDevPanel();
            RefreshSeedText();
            _devRoot.SetActive(false);
        }

        void ToggleDevPanel()
        {
            if (_devRoot != null) _devRoot.SetActive(!_devRoot.activeSelf);
            var first = DevPanelOpen ? _devStageLabel?.GetComponentInParent<Button>() : null;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(
                first != null ? first.gameObject : null);
        }

        // Keyboard shortcuts own controller navigation on the base screen. Prevent an
        // EventSystem Submit from also clicking a previously pointer-selected menu button.
        Button MenuButton(Transform parent, string label, Vector2 anchor, Vector2 offset,
            Vector2 size, UnityEngine.Events.UnityAction action, string name, bool accent = false)
        {
            var button = UiKit.CreateTouchButton(parent, accent ? _fontBold : _font, label,
                accent ? 18 : 10, anchor, offset, size,
                () => { if (!ModalOpen) action(); }, name, accent);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        void BuildMenuButtons(Transform parent)
        {
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);
            var size = new Vector2(166f, 40f);
            UiKit.CreateCornerText(parent, _font, "RUN SETUP", 9, UiKit.TextAccent,
                topLeft, new Vector2(12f, -110f), TextAnchor.UpperLeft, "SetupHeader");
            _difficultyButtonLabel = MenuButton(parent, "", topLeft, new Vector2(12f, -130f),
                size, CycleDifficulty, "DifficultyButton").GetComponentInChildren<Text>();
            _modeButtonLabel = MenuButton(parent, "", topLeft, new Vector2(12f, -174f),
                size, ToggleMode, "ModeButton").GetComponentInChildren<Text>();
            MenuButton(parent, "RANKING", topLeft, new Vector2(12f, -218f),
                size, ToggleRanking, "RankingButton");
            float historyY = -130f;
            if (_suspended != null || _replay != null)
                UiKit.CreateCornerText(parent, _font, "FLIGHT RECORD", 9, UiKit.TextAccent,
                    topRight, new Vector2(-12f, -110f), TextAnchor.UpperRight, "HistoryHeader");
            if (_suspended != null)
            {
                string hint = UiPlatform.TouchMode ? "" : " [C]/X";
                MenuButton(parent, $"CONTINUE{hint}\nSTAGE {_suspended.stageIndex}", topRight,
                    new Vector2(-12f, historyY), size, ContinueRun, "ContinueButton");
                historyY -= 44f;
            }
            if (_replay != null)
            {
                string hint = UiPlatform.TouchMode ? "" : " [V]/LB";
                MenuButton(parent, $"REPLAY{hint}\n{_replay.finalScore:N0}", topRight,
                    new Vector2(-12f, historyY), size, PlayReplay, "ReplayButton");
            }
            _launchButtonLabel = MenuButton(parent, "LAUNCH", new Vector2(0.5f, 0f),
                new Vector2(0f, 10f), new Vector2(232f, 44f), StartRun,
                "LaunchButton", accent: true).GetComponentInChildren<Text>();
        }

        void Start()
        {
            _seedText = NewRandomSeed().ToString();
            _seedUi = DevArgs.DevMode;

            var canvas = UiKit.CreateCanvas("TitleCanvas", 50);
            canvas.transform.SetParent(transform, false);

            // CODEX 계기판 시그니처: 상단 상태 스트립 — 장식이 아니라 "시스템이 켜져
// 있다"는 세계관 소품이다 (SYSTEM // READY).
            var strip = new GameObject("StatusStrip");
            strip.transform.SetParent(canvas.transform, false);
            var stripImage = strip.AddComponent<Image>();
            stripImage.sprite = UiSkin.Button;
            stripImage.type = Image.Type.Sliced;
            stripImage.color = new Color(0.275f, 0.315f, 0.360f, 0.9f);
            stripImage.raycastTarget = false;
            var stripRect = stripImage.rectTransform;
            stripRect.anchorMin = new Vector2(0f, 1f);
            stripRect.anchorMax = new Vector2(1f, 1f);
            stripRect.pivot = new Vector2(0.5f, 1f);
            stripRect.sizeDelta = new Vector2(0f, 18f);
            var stripLeft = UiKit.CreateText(stripRect, _font, "SYSTEM // READY", 8,
                UiKit.TextAccent, TextAnchor.MiddleLeft, "StripLeft");
            var stripLeftRect = stripLeft.rectTransform;
            stripLeftRect.anchorMin = Vector2.zero;
            stripLeftRect.anchorMax = Vector2.one;
            stripLeftRect.offsetMin = new Vector2(10f, 0f);
            stripLeftRect.offsetMax = new Vector2(-10f, 0f);
            var stripRight = UiKit.CreateText(stripRect, _font, "RSS-01 // PILOT LINK", 8,
                UiKit.TextDim, TextAnchor.MiddleRight, "StripRight");
            var stripRightRect = stripRight.rectTransform;
            stripRightRect.anchorMin = Vector2.zero;
            stripRightRect.anchorMax = Vector2.one;
            stripRightRect.offsetMin = new Vector2(10f, 0f);
            stripRightRect.offsetMax = new Vector2(-10f, 0f);

            // 아이브로: 로고 위 작은 앰버 라벨 — 큰 타이포의 서열을 만들어 준다
            var eyebrow = UiKit.CreateCornerText(canvas.transform, _font, "- RUN PROTOCOL -", 9,
                UiKit.TextAccent, new Vector2(0.5f, 1f), new Vector2(0f, -24f),
                TextAnchor.UpperCenter, "Eyebrow");
            UiKit.AddShadow(eyebrow, 1f);

            var title1 = UiKit.CreateCornerText(canvas.transform, _fontBold, "ROGUELIKE", 30,
                UiKit.TextMain, new Vector2(0.5f, 1f), new Vector2(0f, -36f),
                TextAnchor.UpperCenter, "Title1");
            var title2 = UiKit.CreateCornerText(canvas.transform, _fontBold, "SCROLLING SHOOTER", 26,
                UiKit.TextMain, new Vector2(0.5f, 1f), new Vector2(0f, -70f),
                TextAnchor.UpperCenter, "Title2");
            UiKit.AddShadow(title1, 3f);
            UiKit.AddShadow(title2, 3f);
            // 로고 밑줄 — 양끝이 사그라드는 앰버 라인이 로고와 메뉴 영역을 나눈다
            UiKit.CreateRule(canvas.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -102f), 300f, UiKit.TextAccent, "TitleRule");
            _promptText = UiKit.CreateCornerText(canvas.transform, _font,
                UiPlatform.TouchMode ? "CHOOSE SHIP / TAP LAUNCH" : UiText.LaunchPrompt, 9,
                UiKit.TextAccent, new Vector2(0.5f, 0f), new Vector2(0f, 58f),
                TextAnchor.LowerCenter, "Prompt");
            UiKit.AddShadow(_promptText);

            _suspended = RunSave.TryLoad();
            _replay = ReplaySave.TryLoad();
            var todayUtc = System.DateTime.UtcNow;
            _dailyDateInt = todayUtc.Year * 10000 + todayUtc.Month * 100 + todayUtc.Day;
            _dailyDateLabel = todayUtc.ToString(
                "MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            BuildMenuButtons(canvas.transform);
            if (_seedUi) BuildDevPanel(canvas.transform);
            RefreshDifficultyText();
            RefreshModeText();
        }

        void Update()
        {
            if (_layers != null && _factors != null)
            {
                float scroll = Time.time * _driftSpeed;
                for (int i = 0; i < _layers.Length && i < _factors.Length; i++)
                {
                    if (_layers[i] == null) continue;
                    float offset = Mathf.Repeat(scroll * _factors[i], _tileWidth);
                    _layers[i].localPosition = new Vector3(-offset, 0f, 0f);
                }
            }

            // 랭킹이 열려 있는 동안에는 출격/시드 편집 입력을 받지 않는다 —
            // 모달 위에서 스페이스가 그대로 출격으로 새면 보드를 읽다가 런이 시작된다.
            if (_rankingRoot != null && _rankingRoot.activeSelf) return;

            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            bool toggleDev = _seedUi && ((keyboard != null && keyboard.f2Key.wasPressedThisFrame)
                || (gamepad != null && gamepad.selectButton.wasPressedThisFrame));
            if (toggleDev)
            {
                ToggleDevPanel();
                return;
            }
            if (DevPanelOpen)
            {
                if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                    || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame))
                    ToggleDevPanel();
                else if (keyboard != null) EditSeed(keyboard);
                RefreshSeedText();
                return;
            }

            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
                {
                    StartRun();
                    return;
                }
            }
            if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)
            {
                StartRun();
                return;
            }

            // 이어하기
            if (_suspended != null &&
                ((keyboard != null && keyboard.cKey.wasPressedThisFrame)
                 || (gamepad != null && gamepad.buttonWest.wasPressedThisFrame)))
            {
                ContinueRun();
                return;
            }

            // 난이도 순환
            if ((keyboard != null && keyboard.tKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.dpad.up.wasPressedThisFrame))
                CycleDifficulty();

            // 데일리 전환: 같은 날짜 → 전 세계 같은 시드 (Core DailySeed).
            // 예전에는 D가 곧바로 출격이었는데, 출격 경로가 둘로 갈리면 "무엇으로
// 시작했는지"가 화면에 안 남는다 — 이제 모드만 바꾸고 출격은 LAUNCH다.
            if ((keyboard != null && keyboard.dKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.rightShoulder.wasPressedThisFrame))
                ToggleMode();

            // 마지막 런 리플레이
            if (_replay != null &&
                ((keyboard != null && keyboard.vKey.wasPressedThisFrame)
                 || (gamepad != null && gamepad.leftShoulder.wasPressedThisFrame)))
            {
                PlayReplay();
                return;
            }

        }

        void RefreshSeedText()
        {
            if (_seedValueText != null
                && (!ReferenceEquals(_shownSeed, _seedText) || _shownSeedManual != _seedManual))
            {
                _shownSeed = _seedText;
                _shownSeedManual = _seedManual;
                string line = string.Format(
                    UiPlatform.TouchMode ? UiText.SeedFormatTouch : "SEED {0}\nTYPE DIGITS / BACKSPACE TO EDIT", _seedText);
                // 제출이 막힌 사실은 런이 끝난 뒤가 아니라 **출격 전에** 알려야 한다.
                _seedValueText.text = _seedManual ? line + "\nMANUAL SEED / NO SUBMIT" : line;
                _seedValueText.color = _seedManual ? UiKit.TextAccent : UiKit.TextMain;
                RefreshLaunchHint();
            }
        }

        void RefreshLaunchHint()
        {
            if (_promptText == null) return;
            _promptText.text = _seedManual && !_dailyMode
                ? "MANUAL SEED / NO SCORE SUBMIT"
                : (UiPlatform.TouchMode ? "CHOOSE SHIP / TAP LAUNCH" : UiText.LaunchPrompt);
        }

        void EditSeed(Keyboard keyboard)
        {
            if (keyboard.backspaceKey.wasPressedThisFrame && _seedText.Length > 0)
            {
                _seedText = _seedText.Substring(0, _seedText.Length - 1);
                _seedManual = true;
            }
            for (Key key = Key.Digit1; key <= Key.Digit0; key++)
            {
                if (!keyboard[key].wasPressedThisFrame || _seedText.Length >= 12) continue;
                int digit = key == Key.Digit0 ? 0 : key - Key.Digit1 + 1;
                _seedText += (char)('0' + digit);
                _seedManual = true;
            }
        }

        void StartRun()
        {
            // 모드 선택이 여기 모인다 — 버튼이든 스페이스든 한 곳으로 흐른다.
            if (_dailyMode)
            {
                StartDailyRun();
                return;
            }
            bool parsed = long.TryParse(_seedText, out long seed);
            DevArgs.RuntimeSeed = parsed ? seed : NewRandomSeed();
            DevArgs.RuntimeDaily = false;
            // 수동 시드 낙인은 **출격 시점**에 굳는다: 만졌다가 리롤로 되돌렸으면 랜덤이고,
            // 파싱이 깨진 문자열이면 어차피 새로 뽑은 랜덤이라 수동이 아니다.
            DevArgs.RuntimeSeeded = _seedManual && parsed;
            SceneManager.LoadScene("Battle");
        }
    }
}

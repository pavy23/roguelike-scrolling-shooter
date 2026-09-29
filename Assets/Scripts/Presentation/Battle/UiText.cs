namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// UI 표시 문자열 단일 출처. 출시 요건상 언어를 영어로 통일했고(퍼블리셔 심사 지적:
    /// 한/영 혼재), 로컬라이제이션은 이 클래스만 언어별 테이블로 바꾸면 된다.
    /// 게임플레이 로직은 이 문자열에 의존하지 않는다 — 표시 전용.
    /// </summary>
    public static class UiText
    {
        public const string OnboardingStep = "FLIGHT GUIDE  {0}/3";
        public const string OnboardingMoveTouch = "Drag to move your ship.\nWeapons fire automatically.";
        public const string OnboardingAutomaticFire = "WEAPONS FIRE AUTOMATICALLY";
        public const string OnboardingCollect = "Collect a capsule dropped by an enemy.\nEach capsule moves the highlight on the gauge below.";
        public const string OnboardingInvest = "Press {0} to invest in the highlighted slot.\nCollect more capsules to choose a different slot.";
        public const string OnboardingSelect = "Collect a capsule to highlight a slot.\nThen press {0} to invest in it.";
        public const string OnboardingContract = "Upgrades are locked by this sector's contract.\nContinue this guide when upgrades are available.";
        public const string OnboardingUnavailable = "This slot is locked or already at its limit.\nCollect a capsule to move to another slot.";
        public const string OnboardingCompleteTitle = "FLIGHT GUIDE COMPLETE";
        public const string OnboardingComplete = "Move, collect, invest - you are ready.\nReplay this guide anytime from Options.";
        public const string ReplayGuide = "REPLAY FLIGHT GUIDE";
        public const string GaugeCollect = "COLLECT A CAPSULE TO SELECT AN UPGRADE";
        public const string GaugeMax = "{0} MAX - COLLECT TO MOVE SELECTION";
        public const string GaugeLocked = "CONTRACT LOCK - {0}";
        public const string GaugeInvest = "{0}  {1}/{2}   |   {3} INVEST";
        public const string GaugeUpgrade = "{0}   |   {1} UPGRADE";

        // 온보딩 (첫 런 3단계)
        // 폭탄(B)을 빼놓고 있었다. 키는 처음부터 있었는데 안내에 없어서 사람이
        // "폭탄도 키보드로 누를수 있게 해줘(이미 되어있나?)"라고 물었다 —
        // 화면에 적히지 않은 조작은 없는 것과 같다 (2026-08-05).
        public const string Onboarding1 =
            "MOVE  WASD / LEFT STICK      FIRE  AUTOMATIC      "
            + "BOMB  B / (B)      PAUSE  ESC / (START)";
        public const string Onboarding2 =
            "Destroy enemies to drop capsules - each one advances the gauge below";
        public const string Onboarding3 =
            "Press X / (Y) to spend the gauge. WHERE you spend it is your build.";

        // 일시정지
        public const string PauseTitle = "PAUSED";
        public const string PauseHints =
            "ESC / (START) RESUME      O / (SELECT) OPTIONS      Q QUIT TO TITLE";
        public const string VolumeFormat = "VOLUME  {0}%   (LEFT / RIGHT)";

        // 게임오버 / 완주
        public const string GameOverTitle = "GAME OVER";
        public const string RunClearedTitle = "RUN COMPLETE";

        // 완주에도 두 종류가 있다 (사람 지시 2026-08-04: "일반 클리어 / 히든보스
        // 클리어에 따른 별도의 클리어 축하 그림과 메시지"). 같은 문면으로 끝내면
        // 미지의 구역을 잡은 런과 5바이옴만 돈 런이 구분되지 않는다 — 그러면
        // 히든 루트가 존재하는지조차 모르고 게임을 끝낸다.
        public const string RunClearedPerfectTitle = "PERFECT CLEAR";

        /// <summary>미지의 구역까지 잡은 완전 클리어.</summary>
        public const string RunClearedPerfectBody =
            "UNCHARTED ZONE BREACHED. "
            + "NOTHING THIS GAME HID REMAINS HIDDEN.";

        /// <summary>일반 완주 + 히든 조건을 이미 채웠던 경우.</summary>
        public const string RunClearedHiddenReadyBody =
            "FIVE SECTORS CLEARED. THE UNCHARTED ZONE WAS OPEN THIS RUN -\n"
            + "TAKE THE UNCHARTED ROUTE AT THE LAST FORK AND A COLOSSUS WAITS.";

        /// <summary>일반 완주 + 히든 조건 미달.</summary>
        public const string RunClearedHiddenLockedBody =
            "FIVE SECTORS CLEARED. MEET 2 OF THE 3 CONDITIONS "
            + "TO OPEN THE EXTREME ROUTE -\n"
            + "{0}";
        public const string GameOverHints =
            "[ENTER] / (A) REDEPLOY - KEEP POWER-UPS      [R] / (B) TITLE";
        public const string RunClearedHints =
            "[ENTER] / (A) NEW RUN      [R] / (B) TITLE";

        // 컨티뉴 (REQ-104). 격납고에서 크레딧으로 사 둔 재고를 죽은 자리에서 쓴다.
        /// <summary>{0} = 남은 재고. 몇 번 더 쓸 수 있는지가 버튼에서 바로 읽혀야 한다.</summary>
        public const string ContinueButtonFormat = "CONTINUE  ({0} LEFT)";

        /// <summary>
        /// 컨티뉴는 공짜가 아니다 — 이번 런에서 쌓은 점수를 전부 버린다. 누르기 전에
        /// 알려야 하는 조건이라 버튼 옆이 아니라 버튼 위 한 줄로 세운다.
        /// </summary>
        public const string ContinueWarning =
            "CONTINUE RESTARTS THIS SECTOR - SCORE RESETS TO 0";
        public const string GameOverHintsContinue =
            "[ENTER] / (A) REDEPLOY      [C] / (X) CONTINUE      [R] / (B) TITLE";

        /// <summary>{0} = 사용 횟수. 컨티뉴로 이어간 런임을 요약에 남긴다.</summary>
        public const string ContinuedFormat = "CONTINUED x{0}";

        /// <summary>{0} = 보너스 점수 (REQ-105 잔여 실드 환산).</summary>
        public const string ShieldBonusFormat = "SHIELD BONUS +{0}";

        // 격납고 컨티뉴 구매
        /// <summary>{0} = 보유, {1} = 상한.</summary>
        public const string HangarContinueStockFormat = "CONTINUE  {0}/{1}";

        /// <summary>{0} = 다음 한 개 가격.</summary>
        public const string HangarContinueBuyFormat = "BUY CONTINUE\n{0} cr";
        public const string HangarContinueFull = "CONTINUE STOCK FULL";
        public const string HangarContinueHint = "[B] BUY CONTINUE  {0} cr";

        /// <summary>
        /// 최종전 판돈 (REQ-104). 남은 컨티뉴는 최종 보스 진입에서 전부 회수돼
        /// 실드와 점수로 바뀐다 — 사라진 게 아니라 걸린 것임을 그 자리에서 알린다.
        /// {0} = 실드 증가분.
        /// </summary>
        public const string FinalWagerShieldFormat = "CONTINUES  →  SHIELD +{0}";

        /// <summary>{0} = 점수로 환산된 컨티뉴 수, {1} = 그 점수.</summary>
        public const string FinalWagerOverflowFormat = "+{0} OVER CAP  →  {1} PTS";

        // 보상 / 경로
        public const string RewardTitle = "STAGE CLEAR - CHOOSE REWARD";

        /// <summary>중간보스 직후의 짧은 2택 (REQ-054). 주 보상과 무게가 달라야 한다.</summary>
        public const string MidRewardTitle = "MID-BOSS DOWN - QUICK PICK";
        public const string RouteTitle = "CHOOSE YOUR ROUTE";

        /// <summary>섹터 계약 (REQ-070) — 다음 스테이지의 조건을 보고 고른다.</summary>
        public const string ContractTitle = "NEXT SECTOR - CHOOSE YOUR CONTRACT";
        public const string ChoiceHints =
            "[1]-[3] QUICK PICK      LEFT / RIGHT MOVE   (A) / [ENTER] CONFIRM";

        // 타이틀
        public const string LaunchPrompt = "PRESS SPACE / (A) TO LAUNCH";
        public const string SeedFormat = "SEED  {0}_   (type digits, backspace to edit)";

        /// <summary>
        /// 손으로 친 시드 표시. 같은 시드를 반복 연습해 만든 점수는 글로벌 보드에
        /// 올리지 않는다 — 그 사실을 출격 전에 알려야 한 판을 헛되이 돌리지 않는다.
        /// </summary>
        public const string SeedManualSuffix = "   [MANUAL SEED - NO SUBMIT]";
        public const string ContinueFormat = "[C]/(X) CONTINUE - stage {0}, score {1}";

        /// <summary>
        /// 데일리는 "그냥 다른 시드로 한 판"이 아니라 **모두가 같은 시드로 겨루는 스코어링
        /// 챌린지**다. 그 성격이 이름에서 읽히지 않으면 왜 눌러야 하는지 알 수 없다. {0} = MM-dd.
        /// </summary>
        // 키보드 안내도 "즉시 출격"에서 "모드 전환"으로 바뀌었다 - 출격은 스페이스 하나다.
        public const string DailyFormat = "[D]/(RB) MODE: {0}";
        public const string ModeHintNormal = "NORMAL RUN";
        public const string ModeHintDaily = "DAILY {0} · GLOBAL SEED";
        // 모드 선택 버튼. 데일리와 일반 런은 **둘 다 게임 모드**인데 예전에는 각자
        // 다른 버튼에서 바로 출발해, 화면에 출격 버튼이 두 개인 꼴이었다 (사람 지적
        // 2026-08-03: "둘 중 하나를 가운데 버튼에서 골라야하지 않을까"). 이제 여기서
        // 고르기만 하고 출격은 가운데 LAUNCH가 전담한다.
        public const string ModeButtonNormal = "MODE\nNORMAL RUN";
        public const string ModeButtonDaily = "MODE\nDAILY {0}";

        /// <summary>전투 HUD의 데일리 표식 — "지금 무슨 모드인가"가 런 내내 읽혀야 한다.</summary>
        public const string DailyBadge = "DAILY";

        /// <summary>데일리 런의 첫 바이옴 배너 윗줄 (첫 배너에만 — 매번이면 소음이다).</summary>
        public const string DailyBannerHeader = "DAILY CHALLENGE";
        public const string ReplayFormat = "[V]/(LB) REPLAY - {0}";
        public const string DifficultyFormat = "[T] DIFFICULTY  < {0} >";

        // 행거
        public const string HangarFormat = "HANGAR  < {0}/{1} >      CREDIT {2}";
        public const string ShipSelected = "[SELECTED]";
        public const string ShipOwned = "[OWNED]";
        public const string ShipLockedFormat = "[LOCKED - {0} cr, U/(Y) to unlock]";

        // 옵션
        public const string OptionsTitle = "OPTIONS";
        public const string RebindPrompt = "PRESS ANY KEY\n\n(ESC cancel)";
        public const string OptionClose = "CLOSE  [O]/(SELECT)";

        // 조우 타입 (EncounterType 순서와 정렬)
        public static readonly string[] EncounterNames =
        {
            "BATTLE",
            "ELITE  (modifier guaranteed)",
            "SUPPLY  (resupply)",
            "HAZARD  (score x1.5)",
            "RARE  (double reward)"
        };

        // 테마 표시명 (themeIds 순서와 정렬)
        public static readonly string[] ThemeNames =
        {
            "SCRAPYARD", "BIO HIVE", "FORTRESS", "NEBULA", "CORE"
        };

        // 보스 등장
        public const string BossWarning = "!! WARNING !!";

        /// <summary>
        /// St5 타임루프 고스트 합류 (REQ-109). 최종 구간에서 St1의 내 입력이 그대로
        /// 재생되며 반투명 기체 하나가 붙는다 — 설명 없이 뜨면 "적인가?"로 읽히므로
        /// 정체를 한 줄로 못 박는다. 계기판 언어대로 명사구 대문자, 감탄부호 없음
        /// (WARNING의 무게를 나눠 갖지 않는다 — 이건 경고가 아니라 통지다).
        /// </summary>
        public const string GhostJoinBanner = "PAST SELF JOINS";

        // 터치 기기 전용 문면. 폰에서는 키·패드 단축키 안내가 읽을 이유가 없어서,
        // 같은 자리에 터치 조작을 설명하거나 버튼이 대신하도록 비워 둔다.
        public const string Onboarding1Touch =
            "TOUCH AND DRAG - YOUR SHIP FOLLOWS YOUR FINGER      AUTO FIRE IS ON";
        public const string Onboarding3Touch =
            "Tap the X button to spend the gauge. WHERE you spend it is your build.";
        public const string SeedFormatTouch = "SEED  {0}";
        public const string ChoiceHintsTouch = "TAP A CARD TO CHOOSE";
        public const string VolumeFormatTouch = "VOLUME  {0}%";
    }
}

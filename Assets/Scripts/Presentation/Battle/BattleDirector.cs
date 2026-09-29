using System.Collections.Generic;
using Shmup.Core;
using Shmup.Core.Content;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;
using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// Core 시뮬레이션과 씬을 잇는 유일한 지점.
    ///
    /// 하는 일: (1) 고정 60Hz로 IBattleSim.Step 호출, (2) 시뮬 상태를 트랜스폼에 복사,
    /// (3) 탄 뷰를 풀에서 빌리고 돌려주기. 그 외에는 아무 결정도 하지 않는다 —
    /// 이동량, 발사 쿨다운, 탄 위치, 화면 밖 컬링은 전부 Shmup.Core 소관이다.
    ///
    /// 틱은 FixedUpdate에서만 돈다. Fixed Timestep은 SimSpace.TicksPerSecond(60)와
    /// 맞춰야 한다 (ProjectSettings/TimeManager.asset = 0.0166666667).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class BattleDirector : MonoBehaviour
    {
        [Header("Scene wiring")]
        [SerializeField] PlayerInputReader _input;
        public PlayerInputReader Input => _input;
        OnboardingHints _onboarding;
        [SerializeField] Transform _playerTransform;
        [SerializeField] GameObject _bulletPrefab;
        [SerializeField] Transform _bulletRoot;
        [SerializeField] GameObject _enemyPrefab;
        [SerializeField] Transform _enemyRoot;
        [SerializeField] GameObject _capsulePrefab;
        [SerializeField] Transform _capsuleRoot;
        [SerializeField] GameObject _bombPickupPrefab;
        [SerializeField] Transform _bombPickupRoot;
        [SerializeField] GameObject _explosionPrefab;
        [SerializeField] Transform _fxRoot;
        [SerializeField] SpriteRenderer _damageFlash;
        [SerializeField] Sprite _missileSprite;
        /// <summary>MissileFamily 순서(Straight/SpreadBomb/PiercingLance)와 정렬.</summary>
        [SerializeField] Sprite[] _missileFamilySprites;
        [SerializeField] GameObject _optionPrefab;
        [SerializeField] Transform _optionRoot;
        [SerializeField] SpriteRenderer _shieldView;
        [SerializeField] SfxPlayer _sfx;
        [Tooltip("폭발 애니메이션 프레임 (M2). 비어 있으면 단일 스프라이트 확대+페이드 폴백.")]
        [SerializeField] Sprite[] _explosionFrames;
        [SerializeField] Sprite _enemyShotSprite;
        [SerializeField] SpriteRenderer _bossRenderer;
        [SerializeField] GameObject _bossHpRoot;
        [SerializeField] Transform _bossHpFill;
        [Tooltip("definitionId 접두어 → 전용 스프라이트 (M2 적 비주얼 다양화). 매칭 실패 시 기본+틴트 폴백.")]
        [SerializeField] string[] _enemySpritePrefixes;
        [SerializeField] Sprite[] _enemySprites;
        [SerializeField] BossIntro _bossIntro;
        [Tooltip("스테이지 테마 배경 루트들. StagePlan.ThemeId 매칭 우선, 없으면 (StageIndex-1)%개수 로테이션 (M3).")]
        [SerializeField] GameObject[] _themeBackgrounds;
        [SerializeField] string[] _themeIds;
        [Tooltip("StagePlan.BossId 접두어 → 보스 스프라이트.")]
        [SerializeField] string[] _bossSpritePrefixes;
        [SerializeField] Sprite[] _bossSprites;

        // 보상 화면에 쓰는 **초상화**. 전투용 본체 스프라이트와 따로 두는 이유:
        // 하이브와 전함은 화면에서 여러 조각을 조립해 그리고, 등록된 본체 그림
        // (boss_hive.png 128x96 등)은 그 조립 이전의 **구버전**이다. 그걸 보상
        // 카드에 걸면 방금 싸운 보스와 다른 그림이 뜬다 (사람 지시 2026-08-04:
        // "스테이지와 보스 그림 더 잘보이게 추가하고 최신 버전으로 변경").
        [Tooltip("StagePlan.BossId 접두어 → 보상 화면 초상화 (없으면 본체 스프라이트).")]
        [SerializeField] string[] _bossPortraitPrefixes;
        [SerializeField] Sprite[] _bossPortraits;

        // 아이들 애니메이션 (M4): 접두어별 프레임 시퀀스를 평탄화해 직렬화 (빌더가 채움).
        [SerializeField] string[] _animPrefixes;
        [SerializeField] int[] _animFrameCounts;
        [SerializeField] Sprite[] _animFrames;
        [SerializeField] float _animFramesPerSecond = 8f;

        /// <summary>HP 바 폭 (월드유닛). px_white(2px) 스프라이트 기준 스케일 환산에 쓴다.</summary>
        const float BossHpBarWidthUnits = 16f;
        const float WhiteSpriteUnits = 2f / 16f;

        [Header("Run")]
        [Tooltip("로그라이크 시드. 같은 시드 + 같은 입력 = 같은 결과 (AGENTS.md §4).")]
        [SerializeField] long _seed = 1;


        RunManager _run;
        MetaState _meta;              // 함선 해금 메타 (저장 채널: MetaSave)
        int _lastCreditedRunNumber;   // 런당 1회만 점수 적립
        bool _wagerStockSaved;        // 최종전 판돈 정산 뒤 메타 재고 저장 1회
        IBattleSim _sim;   // 매 스텝 _run.Battle로 갱신 — 스테이지 전환/재시작 시 인스턴스가 교체된다
        SpritePool _bulletPool;
        SpritePool _enemyPool;
        SpritePool _capsulePool;

        /// <summary>캡슐 판정 반폭(서브유닛). 뷰 스케일을 여기서 산출한다 — 그림과 판정이 함께 움직인다.</summary>
        int _capsuleHalfWidthSubUnits;

        /// <summary>
        /// 장애물 뷰 배율 (사람 지시 2026-08-03: "고철 크기를 절반으로").
        ///
        /// 절반이 정확히 맞는 값인 이유: 장애물 스프라이트는 32×32px = **2×2유닛**인데
        /// Core 판정은 반크기 0.5 = **1×1유닛**이다. 그림이 판정의 두 배였고, 전함
        /// 함미에서 겪은 "보이는 것보다 판정이 작다"와 같은 거짓말이었다.
        /// 0.5를 곱하면 요청도 충족하고 그림과 판정이 정확히 겹친다.
        ///
        /// 판정(Core 상수)은 건드리지 않았다 — 장애물 크기는 전 테마 공용이라
        /// 줄이면 방금 넣은 포트리스 발판까지 작아진다.
        /// </summary>
        const float ObstacleViewScale = 0.5f;

        /// <summary>
        /// 이 장애물이 기본 크기의 몇 배인가. Core가 장애물마다 반폭을 실어 줄 수
        /// 있게 되면서(2026-08-05) 그림도 같은 배율을 따라가야 한다.
        /// 0이면 기본값이라는 뜻이므로 1배다.
        /// </summary>
        float ObstacleSizeRatio(in Shmup.Core.Simulation.ObstacleState obstacle)
        {
            if (obstacle.HalfWidth <= 0 || _obstacleBaseHalfWidth <= 0)
                return 1f;
            return obstacle.HalfWidth / (float)_obstacleBaseHalfWidth;
        }

        int _obstacleBaseHalfWidth;
        SpritePool _bombPickupPool;

        // Id → 뷰 인스턴스. Core가 주는 Id는 스폰~소멸까지 불변이라 매칭 키로 쓸 수 있다.
        readonly Dictionary<int, Transform> _bulletViews = new Dictionary<int, Transform>(64);
        readonly Dictionary<int, Transform> _enemyViews = new Dictionary<int, Transform>(32);
        readonly Dictionary<int, Transform> _capsuleViews = new Dictionary<int, Transform>(16);
        readonly Dictionary<int, Transform> _bombPickupViews = new Dictionary<int, Transform>(16);
        readonly Dictionary<int, float> _obstacleFadeAges = new Dictionary<int, float>(32);
        const float ObstacleFadeSeconds = 0.35f;

        // 피격 플래시 (REQ-082 C): "데미지를 주고 있다는 표시가 안 난다" — 비치명
        // ObstacleDamaged마다 앰버로 번쩍여 탄이 먹히고 있음을 보여 준다.
        readonly Dictionary<int, float> _obstacleHitFlashes = new Dictionary<int, float>(32);
        const float ObstacleHitFlashSeconds = 0.12f;
        static readonly Color ObstacleHitFlashColor = new Color(1f, 0.72f, 0.25f, 1f);

        // 재생벽 (REQ-101 C-B). regenDelayTicks 장애물은 파괴된 뒤 **같은 entity id로**
        // 다시 나타난다 — 그대로 두면 스폰 페이드와 구분이 안 되어 "장애물이 두 번 스폰됐다"로
        // 읽힌다. 파괴(ObstacleDestroyed)와 재생(ObstacleRegenerated)을 눈으로 가르는 것이
        // 이 연출의 목적이다: 작게 시작해 자라나고, 자라는 동안만 초록으로 물든다.
        // hive 세포벽 정체성 — 부서진 자리가 스스로 아무는 느낌.
        readonly Dictionary<int, float> _obstacleRegenAges = new Dictionary<int, float>(16);
        const float ObstacleRegenSeconds = 0.3f;
        const float ObstacleRegenStartScale = 0.3f;
        static readonly Color ObstacleRegenColor = new Color(0.42f, 1f, 0.55f, 1f);

        // 적탄 차단 스파크는 같은 틱에 여러 발이 먹혀도 1회만 찍는다 — 벽 하나가
        // 탄막을 통째로 소거하는 프레임이 흔해서, 발당 1회면 스파크가 벽을 뒤덮는다.
        bool _blockSparkThisTick;
        readonly Dictionary<int, Transform> _optionViews = new Dictionary<int, Transform>(4);
        readonly Dictionary<int, SpriteRenderer> _enemyRenderers = new Dictionary<int, SpriteRenderer>(32);
        readonly Dictionary<int, Color> _enemyDeathTints = new Dictionary<int, Color>(32);   // 테마별 폭발 틴트
        SpritePool _optionPool;
        Sprite _mainShotSprite;   // Awake에서 탄 프리팹 원본 스프라이트 캡처

        // ── 타임루프 고스트 (REQ-109) ──────────────────────────────────────────
        // 씬에 두지 않고 Awake에서 플레이어 렌더러를 원본으로 만든다 — 고스트는
        // 이번 런의 함선 실루엣을 그대로 물려받아야 "과거의 나"가 되기 때문이다.
        GhostView _ghostView;

        /// <summary>
        /// 고스트가 합류할 때마다 오르는 카운터. ProgressHud가 이 값의 변화만 보고
        /// 배너를 띄운다 — 이벤트를 직접 넘기면 배너가 없는 화면에서도 소비자를
        /// 배선해야 하고, bool 플래그는 소비자가 지워 줘야 해서 누가 리셋하는지가 흐려진다.
        /// </summary>
        public int GhostSpawnSequence { get; private set; }

        /// <summary>지금 고스트가 살아 있는가 (dev 오버레이 `ghost:live`).</summary>
        public bool GhostActive => _run != null && _run.Ghost.Active;

        /// <summary>St1 입력 기록을 들고 있는가 — 최종 구간에서 고스트가 뜰 자격 (dev `ghost:rec`).</summary>
        public bool HasGhostRecording => _run != null && _run.HasStageOneGhostRecording;
        readonly HashSet<int> _aliveIds = new HashSet<int>();
        readonly List<int> _retiredIds = new List<int>(16);

        // 시각 이펙트 (순수 표현 — 게임 상태에 영향 없음)
        SpritePool _fxPool;
        readonly List<Transform> _activeFx = new List<Transform>(16);
        readonly List<SpriteRenderer> _activeFxRenderers = new List<SpriteRenderer>(16);
        readonly List<float> _activeFxAges = new List<float>(16);
        const float ExplosionDuration = 0.28f;
        int _lastHp = -1;
        float _damageFlashAge = float.MaxValue;
        const float DamageFlashDuration = 0.3f;

        // 함선별 스프라이트 (밸런스/스피드/탱커) — 선택 함선에 맞는 기체 비주얼 적용
        [SerializeField] string[] _shipSpriteIds;
        [SerializeField] Sprite[] _shipSprites;
        // 장애물 (REQ-023): 테마×계열 스프라이트, _themeIds와 인덱스 정렬
        [SerializeField] Transform _obstacleRoot;
        [SerializeField] GameObject _obstaclePrefab;
        [SerializeField] Sprite[] _obstacleSolidSprites;
        [SerializeField] Sprite[] _obstacleBreakableSprites;
        SpritePool _obstaclePool;
        readonly Dictionary<int, Transform> _obstacleViews = new Dictionary<int, Transform>(32);

        // ── 레이저 포탑 (2026-08-02 사람 지적) ──────────────────────────────────
        // "고철이 레이저를 발사하는 건 좀 이상. 레이저를 발사하는 포대 같은 게 있는 게 맞을듯"
        //
        // ObstacleType.LaserEmitter는 파괴 불가(HP 0)인데도 파괴 가능 계열 스프라이트
        // (스크랩 잔해·결정)로 그려지고 있었다. 그래서 화면에서는 "떠다니는 고철이
        // 갑자기 빔을 쏜다"로 읽혔고, **어느 잔해가 위험한지 미리 알 방법이 없었다** —
        // 예고선이 뜨기 전까지 다른 장애물과 완전히 같아 보이기 때문이다.
        //
        // 세 가지로 구분한다:
        //   1. 전용 포탑 스프라이트 (BattleSceneBuilder가 생성, 포신은 -X를 향한다)
        //   2. 발사 방향으로 회전 — 포신 끝이 빔이 나갈 곳을 가리킨다
        //   3. 예고~발사 동안 가열 틴트 — 포탑 자체가 "지금 충전 중"을 말한다
        //
        // 차지 글로우는 LaserBeamView가 이미 발사 원점(= 장애물 중심)에 그리므로
        // 여기서 또 띄우지 않는다. 글로우는 포탑 몸통 위에 얹혀 "코어가 달아오른다"로
        // 읽히고, 포신 끝에서 빔이 뻗어 나간다.
        [SerializeField] Sprite _obstacleEmitterSprite;

        /// <summary>포탑 id → 마지막으로 관측한 발사 방향(도). 사이클 사이에도 유지한다.</summary>
        readonly Dictionary<int, float> _emitterAngles = new Dictionary<int, float>(8);

        /// <summary>포탑 id → 이번 프레임 가열도 0~1 (레이저가 없으면 항목 없음 = 0).</summary>
        readonly Dictionary<int, float> _emitterHeat = new Dictionary<int, float>(8);

        /// <summary>포탑 스프라이트의 기본 포신 방향(-X)을 월드 각도로 돌리는 보정.</summary>
        const float EmitterBarrelBaseAngle = 180f;

        /// <summary>차지 글로우와 같은 창(0.8초)에서 달아오른다 — 두 신호가 어긋나면 안 된다.</summary>
        const float EmitterChargeWindupSeconds = 0.8f;

        static readonly Color EmitterHeatColor = new Color(1f, 0.55f, 0.32f, 1f);

        /// <summary>
        /// 레이저탄만 앞으로 밀어 그리는 거리 (월드 단위, 12px @ PPU16).
        ///
        /// Core는 모든 주무기탄을 **기체 중심**(PlayerX/PlayerY)에서 낳는다. 벌컨탄(8px)은
        /// 그래도 금방 기수 밖으로 나오지만, 레이저탄 스프라이트는 20px짜리 줄기라 48px
        /// 기체 실루엣(탄은 sortingOrder 5, 기체는 10 — 탄이 뒤다) 안에 통째로 묻힌 채
        /// 태어나고 두어 프레임 뒤 기수 앞에 툭 나타난다 (사람 지적 2026-08-01).
        /// 스폰 위치는 Core 소관이라 건드리지 않고, **그림만** 기수 끝으로 밀어
        /// "기수에서 앞으로 나간다"로 읽히게 한다. 28u/s 기준 히트 지점보다 2프레임
        /// 못 되게 앞서므로 판정 어긋남은 눈에 잡히지 않는다.
        /// </summary>
        const float LaserBoltMuzzleLead = 0.75f;

        // 무기 계열별 주무기 탄 스프라이트 (REQ-022): laser/spread가 없으면 vulcan 폴백
        [SerializeField] Sprite _laserShotSprite;
        [SerializeField] Sprite _spreadShotSprite;
        Sprite _vulcanShotSprite;   // 프리팹 원본 — 전환 후 벌컨 복귀용
        Shmup.Core.WeaponType _shownWeaponType = (Shmup.Core.WeaponType)(-1);

        void ApplyWeaponBulletSprite(Shmup.Core.WeaponType weaponType)
        {
            _shownWeaponType = weaponType;
            if (weaponType == Shmup.Core.WeaponType.Laser && _laserShotSprite != null)
                _mainShotSprite = _laserShotSprite;
            else if (weaponType == Shmup.Core.WeaponType.Spread && _spreadShotSprite != null)
                _mainShotSprite = _spreadShotSprite;
            else if (_vulcanShotSprite != null)
                _mainShotSprite = _vulcanShotSprite;
        }

        /// <summary>
        /// 게이지로 무기를 갈아탄 순간을 쫓는다 (REQ-089 회귀 수정). 예전에는 시작
        /// 무기(전 기체 벌컨)로 한 번만 굳혀서, 더블/레이저 전환 후에도 탄이 벌컨
        /// 그대로 그려져 "특수탄이 안 나간다"로 읽혔다. Core는 계속 정상 발사 중이었다.
        /// </summary>
        void TrackWeaponTypeChange()
        {
            if (_sim == null) return;
            var current = _sim.PlayerWeaponType;
            if (current != _shownWeaponType)
                ApplyWeaponBulletSprite(current);
        }

        void ApplyShipSprite(string shipId)
        {
            if (_playerTransform == null || _shipSpriteIds == null || _shipSprites == null) return;
            var renderer = _playerTransform.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            int count = Mathf.Min(_shipSpriteIds.Length, _shipSprites.Length);
            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(_shipSpriteIds[i], shipId, System.StringComparison.Ordinal)) continue;
                if (_shipSprites[i] == null) return;
                renderer.sprite = _shipSprites[i];
                // 엔진 프레임 애니는 starter 전용 아트 — 다른 함선은 정지 스프라이트 유지
                var animator = _playerTransform.GetComponent<PlayerShipAnimator>();
                if (animator != null) animator.enabled = i == 0;
                return;
            }
        }

        // 주스 연출 (REQ 없음 — 순수 표현, 시뮬 비관여)
        [SerializeField] JuiceDirector _juice;
        [SerializeField] SpriteRenderer _muzzleFlash;
        readonly List<Transform> _punchTargets = new List<Transform>(16);
        readonly List<SpriteRenderer> _punchRenderers = new List<SpriteRenderer>(16);
        readonly List<Vector3> _punchBaseScales = new List<Vector3>(16);
        readonly List<Color> _punchBaseColors = new List<Color>(16);
        readonly List<float> _punchAges = new List<float>(16);
        const float PunchDuration = 0.09f;
        static readonly Color PunchTint = new Color(1f, 0.45f, 0.45f);
        readonly List<Vector3> _pendingBoomPositions = new List<Vector3>(8);
        readonly List<float> _pendingBoomDelays = new List<float>(8);
        /// <summary>
        /// 예약 폭발의 크기. 예전에는 전부 기본 크기였는데, 거대 보스 파츠가
        /// 무너질 때 작은 폭발만 흩어져 규모가 읽히지 않았다. 위치·지연과 항상
        /// 같은 길이로 유지한다.
        /// </summary>
        readonly List<float> _pendingBoomScales = new List<float>(8);
        float _muzzleAge = float.MaxValue;
        const float MuzzleDuration = 0.06f;

        /// <summary>
        /// 현재 콤보 배율 (HUD 표시용). **Core에게 매 프레임 물어본다.**
        ///
        /// 예전에는 MultiplierChanged 이벤트로만 따라갔고, 방이 바뀌어 새 BattleSim이
        /// 서면 표시를 1로 되돌렸다. 그런데 Core는 방을 넘어갈 때 배율을 그대로
        /// **이어받는다**(BattleContinuityState). 새 방에서는 값이 안 바뀌었으니
        /// 이벤트도 안 오고, 화면만 x1에 멈춰 있었다.
        ///
        /// 사람이 네 번 보고한 "중간보스 진입 순간 배율 리셋 / 이후 배수가 아예 동작
        /// 안 함"이 정확히 이것이다 — 규칙은 멀쩡했고 **화면이 거짓말을 했다.**
        /// 실제 데이터로 1스테이지를 돌려 확인했다(Req140MultiplierAcrossRoomsTests):
        /// 중간보스 진입 시 x16을 유지했고 전투 중 x32까지 올랐다.
        ///
        /// 그래서 파생값으로 바꾼다. 상태를 두 곳에 두면 언젠가 다시 어긋난다.
        /// </summary>
        public int ScoreMultiplier => _sim != null ? _sim.ScoreMultiplier : 1;

        /// <summary>이번 런에서 도달한 최고 배율 (게임오버 요약용).</summary>
        public int BestMultiplier { get; private set; } = 1;

        /// <summary>런 지속 모디파이어 (게임오버 요약용).</summary>
        public BattleModifier ActiveModifiers => _run != null ? _run.ActiveModifiers : BattleModifier.None;

        [SerializeField] ScorePopups _scorePopups;
        [SerializeField] SpawnTelegraph _spawnTelegraph;

        /// <summary>스테이지 기믹 시각화 (REQ-055) — 통로 벽 접촉 번쩍임에 필요하다.</summary>
        [SerializeField] StageGimmickView _gimmickView;
        [Tooltip("근접 공격 예고를 그 파츠 위에 띄우려면 필요하다.")]
        [SerializeField] BossPartsView _bossPartsView;
        int _lastBossHp = -1;
        float _bossFlashAge = float.MaxValue;

        // 페이즈 전환 발광 (REQ-054). 남은 시간과 최대 세기를 따로 둔다 —
        // 33% 광폭화는 66%보다 강하게 번쩍여야 한다.
        float _bossPhaseFlash;
        float _bossPhaseFlashPeak;
        float _bombFlashAge = float.MaxValue;
        const float BombFlashDuration = 0.5f;
        [SerializeField] BombButton _bombButton;
        const float BossFlashDuration = 0.09f;

        /// <summary>
        /// 무적 파츠 차단 스파크 색 (REQ-125). 파츠 무적 표시(청록 테두리·맥동)와 같은
        /// 계열이라 "저 색 = 지금 못 깎는다"가 하나의 어휘로 묶인다. 피격·폭발의
        /// 흰·주황과 확실히 갈라야 착시가 안 생긴다.
        /// </summary>
        static readonly Color BlockedHitTint = new Color(0.35f, 0.85f, 1f, 1f);

        /// <summary>
        /// 경로 선택은 폐지됐다 (REQ-054) — 새 런은 이 상태에 들어가지 않고
        /// `RunManager.ChooseRoute`는 `NotSupportedException`을 던진다. 다른 화면이
        /// "메뉴가 떠 있는지" 판단할 때 쓰던 자리라 상수 false로 남겨 둔다.
        /// </summary>
        public bool AwaitingRoute => false;


        readonly List<int> _recordedRoutes = new List<int>(8);
        int _replayRouteCursor;

        /// <summary>보스전 진행 중 여부 (BgmPlayer 보스 트랙 전환용).</summary>
        public bool BossActive => _sim != null && _sim.BossActive;

        /// <summary>
        /// 보스가 기체를 빨아들이는 중인가 (브루드마더 최종 패턴).
        ///
        /// Core는 처음부터 이 상태를 내보내고 있었는데 **Presentation에 그것을
        /// 읽는 코드가 한 줄도 없었다** — 판정만 있고 그림이 없어서, 기체가
        /// 끌려가는데 왜 끌려가는지 알 수 없었다 (사람 보고 2026-08-04:
        /// "브루드마더 빨아들이는건 아무런 이팩트가 없어서 모르겠어").
        /// </summary>
        public bool SuctionActive => _sim != null && _sim.SuctionActive;

        /// <summary>이번 방의 보스 id (waves.json). 보스별 전용 뷰가 자기 차례인지 가른다.</summary>
        public string BossStageId =>
            _run != null && _run.StagePlan != null ? _run.StagePlan.BossId : null;

        /// <summary>
        /// 지금 장착된 미사일 계열 / 옵션 편대. 게이지 HUD가 **레벨 숫자 대신 유형**을
        /// 보여 주는 데 쓴다 (사람 지시 2026-08-03: "missile은 유형, option은 타입
        /// 형태를 표기해주는게 알기 좋을듯"). 레벨만 보면 무엇이 달린 건지 알 수 없다.
        /// </summary>
        public MissileFamily CurrentMissileFamily =>
            _run != null ? _run.CurrentMissileFamily : MissileFamily.Straight;

        public OptionFormation CurrentOptionFormation =>
            _run != null ? _run.CurrentOptionFormation : OptionFormation.Trail;

        /// <summary>보스 중심 x (서브유닛). 좌우 파츠를 가르는 기준으로 쓴다.</summary>
        public int BossPositionSubUnitsX => _sim != null ? _sim.Boss.X : 0;

        /// <summary>
        /// 보스 판정 반높이 (월드 유닛). 보스 전용 뷰가 **그림을 판정에 맞추는** 데 쓴다.
        ///
        /// 이 값을 노출한 이유: 보이는 크기와 맞는 크기가 어긋나 "때렸는데 안 맞는다"는
        /// 보고가 세 번 올라왔다(c3df07c). 뷰가 배율을 스스로 박아 두면 데이터가 바뀔 때
        /// 조용히 어긋나므로, 판정 크기를 그대로 내보내 뷰가 계산하게 한다.
        /// </summary>
        public float BossHalfHeightWorld =>
            _run != null && _run.StagePlan != null
                ? _run.StagePlan.BossHalfHeight * SimView.WorldUnitsPerSubUnit
                : 0f;

        /// <summary>
        /// 원하는 테마(또는 거대 보스)가 나오는 시드를 찾는다. 개발자 패널 전용.
        ///
        /// 테마는 시드가 정한다. "이 테마로 만들어라"를 Core에 뚫으면 결정론 경로에
        /// 분기가 하나 늘고, 그 분기는 리플레이·이어하기·해시까지 따라다닌다. 반면
        /// **시드를 고르는 일은 원래 Presentation 소관**이므로, 결과가 맞을 때까지
        /// 후보 시드로 런을 만들어 보는 쪽이 훨씬 싸고 안전하다.
        ///
        /// 못 찾으면 원래 시드를 그대로 돌려준다 — 개발용 편의가 런을 못 띄우게
        /// 만드는 것이 최악이다.
        /// </summary>
        long FindSeedFor(
            GameDataSet data,
            BattleSimConfig config,
            ShipDefinition ship,
            int diffNum,
            int diffDen,
            RunConfig runConfig,
            string wantedTheme,
            string wantedColossal,
            long fallbackSeed)
        {
            const int Attempts = 48;
            for (int i = 0; i < Attempts; i++)
            {
                long candidate = fallbackSeed + i * 7919L;
                RunManager probe;
                try
                {
                    probe = new RunManager(
                        (ulong)candidate,
                        new SegmentStageGenerator(data.StageGeneration),
                        config,
                        data.BattleContent,
                        data.CreatePowerUpGauge(ship),
                        data.Rewards,
                        ship,
                        diffNum,
                        diffDen,
                        runConfig);
                }
                catch (System.Exception)
                {
                    continue;   // 이 시드로는 만들 수 없는 조합 — 다음 후보로
                }

                if (!string.IsNullOrEmpty(wantedColossal))
                {
                    if (probe.SelectedColossalBoss.ToString()
                            .IndexOf(wantedColossal, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return candidate;
                    continue;
                }
                string theme = probe.StagePlan != null ? probe.StagePlan.ThemeId : null;
                if (string.Equals(theme, wantedTheme, System.StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            Debug.LogWarning(
                $"[dev] '{wantedTheme}{wantedColossal}'가 나오는 시드를 {Attempts}번 안에 "
                + "못 찾았다 — 원래 시드로 진행한다.");
            return fallbackSeed;
        }

        /// <summary>
        /// 파츠가 **잘려 나가는** 순간의 연출 (하이브 다리 절단).
        /// 일반 폭발보다 조밀하게 터뜨려 "사라졌다"가 아니라 "끊겼다"로 읽히게 한다.
        /// </summary>
        public void SpawnSeverBurst(Vector3 at)
        {
            SpawnExplosion(at, 0.9f);
            for (int i = 0; i < 4; i++)
            {
                var offset = new Vector3(
                    Mathf.Cos(i * 1.9f) * 0.45f,
                    -0.35f * (i + 1),   // 아래로 흘러내린다 — 잘린 다리가 떨어지는 방향
                    0f);
                _pendingBoomPositions.Add(at + offset);
                _pendingBoomDelays.Add(0.07f * (i + 1));
                _pendingBoomScales.Add(0.9f);
            }
        }

        /// <summary>런 완주(최종 보스 격파) 여부 — 결과 화면이 승리/패배를 가른다 (REQ-031).</summary>
        public bool IsRunCleared => _run != null && _run.State == RunState.RunCleared;

        /// <summary>초대형 보스 파츠 상태 (REQ-035) — BossPartsView가 읽는다.</summary>
        public IReadOnlyList<BossPartState> BossParts =>
            _sim != null ? _sim.BossParts : null;

        /// <summary>
        /// 파츠 **히트박스 정의** (반폭/반높이, 서브유닛). <see cref="BossParts"/>는
        /// 좌표·HP·무적만 주고 크기를 주지 않는다 — 크기는 StagePlan의 정의에 있다.
        ///
        /// 뷰가 이걸 읽어야 하는 이유: 하드포인트 스프라이트를 **native 크기로** 얹으면
        /// 그림과 판정이 어긋난다. 예를 들어 fortress_warship의 함미(engine)는 판정이
        /// 반높이 2.0u인데 재사용 스프라이트 boss_fortress는 96px/PPU16 = 반높이 3.0u다 —
        /// 보이는 함미 아래쪽 1유닛은 **쏴도 안 맞는 그림**이었다. build25~30의 테스터
        /// 5명이 "함미가 데미지를 안 받는다"고 연속 오판한 원인이 이 어긋남이다
        /// (build30 보고서 §2 — 실제로는 판정 밖에서 쏘고 있었다).
        /// 그림이 판정보다 크면 안 된다. 뷰는 이 값으로 스프라이트를 판정에 맞춘다.
        /// </summary>
        public IReadOnlyList<BossPartDefinition> BossPartDefinitions =>
            _run != null && _run.StagePlan != null ? _run.StagePlan.BossParts : null;

        /// <summary>
        /// 이 방의 보스 시각 표현을 <see cref="WarshipView"/>가 소유하는가.
        /// WarshipView가 자기 차례를 판단하는 조건과 같은 데이터로 계산한다 —
        /// 참조를 새로 직렬화하지 않으려고 조건을 복제했다.
        /// </summary>
        bool WarshipOwnsBossVisual =>
            WarshipEncounter != null
            && _sim != null && _sim.BossActive
            && _sim.BossParts != null && _sim.BossParts.Count > 0;

        /// <summary>
        /// 하이브도 전용 뷰가 화면을 소유한다 (HiveBossView).
        ///
        /// 이게 없어서 **에일리언이 두 마리로 보였다** — 전용 뷰가 잘라 만든 파츠를
        /// 그리는 동안 본체 렌더러가 옛 boss_hive.png(128x96짜리 작은 그림)를 같은
        /// 자리에 한 장 더 얹고 있었다. 사람 보고 2026-08-04: "이전 작은 사이즈
        /// 에일리언 보스가 겹쳐있는 문제".
        ///
        /// 전함과 같은 이유·같은 조건이라 조건식을 복제한다 (뷰 참조를 새로
        /// 직렬화하지 않으려는 것도 같다). 조건은 HiveBossView.Update와 맞춘다.
        /// </summary>
        bool HiveOwnsBossVisual =>
            _sim != null && _sim.BossActive
            && _sim.BossParts != null && _sim.BossParts.Count > 0
            && BossStageId != null
            && BossStageId.StartsWith("boss_hive", System.StringComparison.Ordinal);

        /// <summary>전함전이라 본체 스프라이트를 숨긴 상태 (격파 연출은 계속 나와야 한다).</summary>
        bool _bossVisualSuppressed;

        /// <summary>
        /// St4 번개룡(세그먼트 체인 미니언) 절 상태 (REQ-115b) — SegmentChainView가 읽는다.
        /// Core는 이 체인을 <see cref="IBattleSim.Enemies"/>가 아니라 **별도 관측**으로
        /// 노출한다. 그래서 적 뷰 동기화(SyncEnemies)에 절대 걸리지 않는다 —
        /// 뷰가 따로 없으면 접촉 데미지만 주는 투명 미니언이 된다
        /// ("체인 미니언 스프라이트를 못 찾겠다", build26/27 테스터).
        /// 한 체인은 절 6~8개가 ChainId로 묶여 SegmentIndex 순서로 들어온다.
        /// </summary>
        public IReadOnlyList<SegmentChainState> SegmentChains =>
            _sim != null ? _sim.SegmentChains : null;

        /// <summary>
        /// 지금 Core가 굴리는 **적탄** 수. 체인의 seg/drawn과 같은 용도다 —
        /// Core가 만든 것과 화면에 그려진 것을 갈라 봐야 "안 보인다"의 책임이
        /// 어느 쪽인지 정해진다.
        ///
        /// 2026-08-05에 "포탑 탄막이 안 보인다"를 좇으면서 이게 없어 스크린샷을
        /// 색으로 훑었고, 피격 플래시를 탄으로 잘못 세어 한참을 돌아갔다.
        /// </summary>
        public int CoreEnemyBulletCount
        {
            get
            {
                if (_sim == null) return 0;
                var bullets = _sim.Bullets;
                int n = 0;
                for (int i = 0; i < bullets.Count; i++)
                    if (bullets[i].Faction == BulletFaction.Enemy) n++;
                return n;
            }
        }

        /// <summary>지금 살아 있는 탄 뷰 수 (아군탄 포함).</summary>
        public int DrawnBulletViewCount =>
            _bulletViews != null ? _bulletViews.Count : 0;

        /// <summary>
        /// 체인 절 히트박스 반폭(서브유닛). Core의 절 상태는 좌표와 HP만 주고 크기는
        /// 페이즈 정의에 있으므로, 이 방 보스의 페이즈(2형태 포함)에서 읽어 온다.
        /// 0이면 이 방에 체인이 없다 — 뷰는 그때 자기 기본값으로 그린다.
        /// </summary>
        public int SegmentChainHalfWidthSubUnits { get; private set; }

        /// <summary>
        /// 체인 소환/파괴 폭발 틴트. nebula 낙뢰 섬광(SectionTheme의 flashColor
        /// 0.92/0.95/1.00)과 같은 색군이라 폭풍 스테이지의 어휘로 읽힌다.
        /// </summary>
        static readonly Color ChainSparkTint = new Color(0.80f, 0.93f, 1f);

        void CacheSegmentChainExtent()
        {
            SegmentChainHalfWidthSubUnits = 0;
            var plan = _run != null ? _run.StagePlan : null;
            if (plan == null) return;
            AccumulateChainHalfWidth(plan.BossPhases);
            if (plan.Form2 != null) AccumulateChainHalfWidth(plan.Form2.Phases);
        }

        void AccumulateChainHalfWidth(IReadOnlyList<BossPhase> phases)
        {
            if (phases == null) return;
            for (int i = 0; i < phases.Count; i++)
            {
                var chain = phases[i].SegmentChain;
                if (chain != null && chain.HalfWidth > SegmentChainHalfWidthSubUnits)
                    SegmentChainHalfWidthSubUnits = chain.HalfWidth;
            }
        }

        /// <summary>
        /// St3 거대 전함 정의 (REQ-110/111). 이 방의 보스가 전함이면 파츠 3그룹
        /// (함미/함체/함수)의 순서와 역할이 들어 있고, 아니면 null이다 —
        /// WarshipView가 이 값 하나로 자기 차례인지 판단한다.
        /// 파츠의 위치·HP·무적은 <see cref="BossParts"/>가 준다. 여기 정의는
        /// 그룹 소속을 읽기 위한 **읽기 전용 계약**이고 뷰는 상태를 굴리지 않는다.
        /// </summary>
        public WarshipEncounterDefinition WarshipEncounter =>
            _run != null && _run.StagePlan != null ? _run.StagePlan.WarshipEncounter : null;

        /// <summary>플레이어 기체의 월드 좌표 (터치 드래그 조작이 목표 방향을 계산할 때 쓴다).</summary>
        public Vector2 PlayerWorldPosition =>
            _sim != null ? (Vector2)SimView.ToWorld(_sim.PlayerX, _sim.PlayerY) : Vector2.zero;

        /// <summary>보스 본체의 월드 좌표 (파츠 오버레이 기준점).</summary>
        public Vector3 BossWorldPosition =>
            _sim != null ? SimView.ToWorld(_sim.Boss.X, _sim.Boss.Y) : Vector3.zero;

        // ── 플레이어 뷰 진단 (build25~29 "기체가 사라진다" 수사) ──────────────────
        //
        // 다섯 빌드 연속으로 "화면 아래로 내리면 기체가 영구히 사라진다"가 보고됐고,
        // 그때마다 원인 후보가 Core 클램프 / 정렬 순서 / 입력 경로로 갈렸다. 화면 한
        // 프레임에서 판별할 수 있게 네 값을 dev 오버레이에 그대로 내보낸다:
        //   py  = Core가 정한 기체 Y (월드 단위 환산)
        //   ty  = 실제 뷰 트랜스폼의 월드 Y
        //   ren = 기체 렌더러가 켜져 있는가
        //   sc  = 기체 렌더러 정렬 순서
        // py는 정상인데 ty가 이탈하면 뷰 동기 경로, ren이 false면 렌더러를 끈 범인이
        // 있다는 뜻이고, 넷 다 정상이면 **기체는 거기 있고 무언가가 덮고 있는 것**이다
        // (실제 원인이 그것이었다 — 하단 게이지 HUD, PowerUpHudView 참조).

        SpriteRenderer _playerRendererCache;

        /// <summary>기체 스프라이트 렌더러 (지연 캐시 — 배선 검증 실패 런에서도 안전하다).</summary>
        SpriteRenderer PlayerRenderer
        {
            get
            {
                if (_playerRendererCache == null && _playerTransform != null)
                    _playerRendererCache = _playerTransform.GetComponent<SpriteRenderer>();
                return _playerRendererCache;
            }
        }

        /// <summary>Core 권위 기체 Y를 월드 단위로 (dev 오버레이 `py`).</summary>
        public float PlayerCoreY =>
            _sim != null ? _sim.PlayerY * SimView.WorldUnitsPerSubUnit : 0f;

        /// <summary>기체 뷰 트랜스폼의 월드 Y (dev 오버레이 `ty`).</summary>
        public float PlayerViewY =>
            _playerTransform != null ? _playerTransform.position.y : 0f;

        /// <summary>기체 렌더러가 켜져 있는가 (dev 오버레이 `ren`).</summary>
        public bool PlayerRendererEnabled
        {
            get
            {
                var renderer = PlayerRenderer;
                return renderer != null && renderer.enabled;
            }
        }

        /// <summary>기체 렌더러 정렬 순서 (dev 오버레이 `sc`).</summary>
        public int PlayerSortingOrder
        {
            get
            {
                var renderer = PlayerRenderer;
                return renderer != null ? renderer.sortingOrder : 0;
            }
        }

        // 바이옴/룸 진행도 (REQ-032) — 22분 런에서 현재 위치를 알려 준다
        public int BiomeIndex => _run?.BiomeIndex ?? 0;
        public int RoomIndex => _run?.RoomIndex ?? 0;
        public int BiomeCount => _run?.BiomeCount ?? 0;
        public int RoomsPerBiome => _run?.RoomsPerBiome ?? 0;

        /// <summary>런이 끝났는가 (사망 또는 완주).</summary>
        public bool IsRunFinished => _run != null && _run.IsFinished;
        public bool IsPlaying => _run != null && _run.State == RunState.Playing;

        /// <summary>타이틀 CONTINUE가 채우는 이어하기 데이터 — Awake에서 1회 소비.</summary>
        public static Shmup.Core.Simulation.RunSuspendData PendingResume;

        /// <summary>타이틀 REPLAY가 채우는 재생 데이터 — Awake에서 1회 소비.</summary>
        public static ReplayFileData PendingReplay;

        // 리플레이/녹화 (REQ-018/019). 기록은 Playing 틱의 명령만 담는다 — 보상 대기 길이가
        // 달라도 스트림이 어긋나지 않는다. 보상 선택은 별도 목록(rewardChoices)으로 재현.
        InputRecorder _recorder;
        bool _recordingActive;
        readonly List<int> _recordedChoices = new List<int>(8);
        readonly List<int> _recordedContractChoices = new List<int>(8);
        int _replayContractCursor;
        bool _replayMode;
        InputPlayback _playbackSource;
        InputPlayback.Enumerator _playback;
        int _replayChoiceCursor;
        int _replayContinueCursor;
        bool _replayStreamEnded;
        float _replayEndTimer = 3f;
        string _recordShipId;

        public bool ReplayMode => _replayMode;

        /// <summary>현재 런을 스테이지 경계 스냅샷으로 저장 (Playing 상태에서만 유효).</summary>
        public void SaveRunToDisk()
        {
            if (_run == null || _run.State != RunState.Playing) return;
            // 개발 플래그 런은 Core가 스냅샷 내보내기를 예외로 거절한다 (REQ-096) —
            // 일시정지에서 타이틀로 나가거나 게임을 끄는 평범한 경로라 여기서 걸러야
            // 한다. 어차피 제출도 막힌 런이라 이어할 것도 없다.
            if (_run.DevFlagsActive) return;
            RunSave.Save(_run.ExportSuspendData());
        }

        void OnApplicationQuit()
        {
            if (_run != null && !IsRunOver && !_replayMode)
                SaveRunToDisk();

            // 종료 시점은 디바운스를 기다릴 여유가 없다 — WebGL에서 즉시 IDB로 내린다
            // (다른 플랫폼 no-op). 펌프 쪽 OnApplicationQuit과 순서가 갈릴 수 있어
            // 저장 직후 여기서 한 번 더 확실히 부른다.
            SaveFlush.FlushNow();
        }

        // Step이 RunOver/AwaitingReward에서 no-op이면 EventsThisTick이 클리어되지 않는다 —
        // 같은 이벤트를 매 FixedUpdate 재소비하지 않도록 (배틀 인스턴스, 틱)으로 신선도 판정.
        IBattleSim _lastEventSim;
        int _lastEventTick = -1;

        // 적 종류 구분용 틴트 (플레이스홀더 아트 한정 — 실제 스프라이트가 생기면 제거)
        static readonly Color32[] EnemyTints =
        {
            new Color32(0xE8, 0x6A, 0x6A, 0xFF),   // zako_straight
            new Color32(0xB4, 0x7A, 0xE8, 0xFF),   // zako_sine
            new Color32(0x8C, 0x8C, 0x9C, 0xFF)    // turret_ground
        };

        public int Tick => _sim?.Tick ?? 0;
        public int ActiveBulletViews => _bulletViews.Count;

        /// <summary>현재 런의 시드. --seed=N 커맨드라인 → 타이틀 입력 → 인스펙터 순으로 결정되고, 재시작 시 갱신된다.</summary>
        public long Seed { get; private set; }

        /// <summary>
        /// 이번 런이 데일리 시드로 시작됐는가 (스코어보드 보드 분리용). 타이틀이 DevArgs로
        /// 알려 준 값을 Awake에서 한 번 굳힌다 — 재출격은 새 시드라 데일리가 아니다.
        /// </summary>
        public bool IsDailyRun { get; private set; }

        /// <summary>
        /// 이번 런이 **지정된 시드**로 시작됐는가 (스코어보드 공정성). 타이틀에서 손으로
        /// 친 시드와 커맨드라인 <c>--seed=N</c>은 성격이 같다 — 같은 판을 몇 번이고
        /// 연습한 뒤 최고 기록만 올릴 수 있는 런이다. 치트 런과 같은 경로로 제출을 닫는다.
        ///
        /// 이어하기·리플레이는 제외한다: 이어하기 시드는 그 런이 시작될 때 정해졌고,
        /// 리플레이는 새 기록이 아니라 기록의 재현이라 제출 경로가 원래 닫혀 있다.
        /// 재출격은 새 랜덤 시드라 낙인이 풀린다.
        /// </summary>
        public bool IsSeededRun { get; private set; }

        /// <summary>
        /// 이번 런에서 개발용 치트(F9/F10/F11)를 한 번이라도 썼는가.
        /// 개발 검증 주행이 글로벌 보드를 오염시키면 안 되므로 GameOverScreen이 이 값을 보고
        /// 제출을 막는다. 재출격/새 런에서 초기화된다 — 오염되는 것은 그 런 하나뿐이다.
        /// </summary>
        public bool CheatUsed { get; private set; }

        /// <summary>DevCheats가 치트를 실제로 실행한 순간 호출한다 (되돌릴 수 없다).</summary>
        public void MarkCheatUsed() => CheatUsed = true;

        /// <summary>이번 런에 탄 기체 id (스코어보드 표시용). 이어하기/리플레이도 기록 당시 기체를 따른다.</summary>
        public string ShipId => _run != null && _run.Ship != null ? _run.Ship.Id : null;

        /// <summary>완주 등급 (미완주 = None). 결과 요약과 스코어보드 제출이 읽는다.</summary>
        public RunCompletionGrade CompletionGrade =>
            _run != null ? _run.CompletionGrade : RunCompletionGrade.None;

        /// <summary>
        /// 방금 상대한 보스의 초상화. 등록이 없으면 전투용 본체 스프라이트로 되돌아간다.
        /// 보상 화면이 "무엇을 잡았는지"를 보여 주는 데 쓴다.
        /// </summary>
        public Sprite CurrentBossPortrait
        {
            get
            {
                string bossId = BossStageId;
                if (string.IsNullOrEmpty(bossId)) return null;
                Sprite best = LongestPrefixSprite(
                    bossId, _bossPortraitPrefixes, _bossPortraits);
                return best != null
                    ? best
                    : LongestPrefixSprite(bossId, _bossSpritePrefixes, _bossSprites);
            }
        }

        static Sprite LongestPrefixSprite(string id, string[] prefixes, Sprite[] sprites)
        {
            if (prefixes == null || sprites == null) return null;
            Sprite best = null;
            int bestLength = -1;
            int count = Mathf.Min(prefixes.Length, sprites.Length);
            for (int i = 0; i < count; i++)
            {
                string prefix = prefixes[i];
                if (string.IsNullOrEmpty(prefix) || sprites[i] == null) continue;
                if (id.StartsWith(prefix, System.StringComparison.Ordinal)
                    && prefix.Length > bestLength)
                {
                    best = sprites[i];
                    bestLength = prefix.Length;
                }
            }
            return best;
        }

        /// <summary>보상 화면 머리글 — "STAGE 3 · FORTRESS" 형태.</summary>
        public string StageContextLabel
        {
            get
            {
                string theme = CurrentThemeId;
                return string.IsNullOrEmpty(theme)
                    ? $"STAGE {StageIndex}"
                    : $"STAGE {StageIndex}  ·  {theme.ToUpperInvariant()}";
            }
        }

        /// <summary>
        /// 이번 런에서 히든 조건을 몇 개 채웠나 (0~3). 완주 화면이 "미지의 구역이
        /// 열려 있었다 / 조금만 더였다"를 말하는 데 쓴다 — 조건을 모른 채 끝나면
        /// 미지의 구역은 우연히 열리는 기능으로 남는다.
        /// </summary>
        public int HiddenConditionsMet =>
            _run != null
                ? RunManager.CountHiddenBiomeConditions(
                    _run.EliteRoomsCleared,
                    _run.NoHitBiomesCleared,
                    _run.RareEncountersCleared)
                : 0;

        /// <summary>조건별 진행 상황 — 무엇이 모자랐는지 한 줄로 보여 준다.</summary>
        public string HiddenConditionsDetail =>
            _run == null
                ? string.Empty
                : $"ELITE {Mathf.Min(_run.EliteRoomsCleared, 3)}/3   "
                  + $"NO-HIT {Mathf.Min(_run.NoHitBiomesCleared, 2)}/2   "
                  + $"RARE {Mathf.Min(_run.RareEncountersCleared, 1)}/1";

        /// <summary>
        /// 난이도 라벨. 타이틀의 <see cref="DifficultySelect"/> 값이 아니라 런에 굳어 있는
        /// 배율에서 되짚는다 — 이어하기/리플레이 도중 타이틀 선택이 바뀌어도 표시가 어긋나지 않는다.
        /// </summary>
        public string DifficultyLabel
        {
            get
            {
                if (_run == null) return DifficultySelect.Label;
                int numerator = _run.DifficultyMultiplierNumerator;
                int denominator = _run.DifficultyMultiplierDenominator;
                if (numerator == 3 && denominator == 4) return "EASY";
                if (numerator == 5 && denominator == 4) return "HARD";
                if (numerator == denominator) return "NORMAL";
                return $"{numerator}/{denominator}";
            }
        }

        /// <summary>파워업 게이지 (Core/RunManager 소유). HUD가 읽어서 그린다. 재시작 시 승계 적용된 새 인스턴스로 바뀐다.</summary>
        public PowerUpGauge Gauge => _run?.PowerUpGauge;

        // ── 계약 잠금 피드백 (REQ-094 관측 소비) ──────────────────────────────
        //
        // 계약이 발동을 막고 있으면 SELECT를 눌러도 게이지가 그대로다. 화면이 아무
        // 반응도 하지 않으면 플레이어는 그것을 "고장"으로 읽는다 — 거부된 그 틱을
        // 잡아 HUD가 한 번 번쩍이게 신호를 보낸다.
        //
        // 게이지의 LastActivationResult는 다음 발동까지 남아 있는 '마지막 결과'라
        // 값 변화만으로는 재시도를 구분할 수 없다. 그래서 sim과 같은 방식으로
        // Activate의 상승 에지를 여기서도 재고, 그 틱의 결과만 본다.
        bool _contractActivateHeld;

        /// <summary>계약으로 발동이 거부될 때마다 1 증가. HUD는 값이 바뀐 순간에 플래시한다.</summary>
        public int ContractLockPulse { get; private set; }

        /// <summary>마지막으로 거부된 이유 (게이지 전체 / 옵션 / 실드).</summary>
        public PowerUpActivationResult ContractLockResult { get; private set; }
            = PowerUpActivationResult.NoSelection;

        void DetectContractLock(in InputCommand command, bool playingBefore)
        {
            bool pressed = command.Activate && !_contractActivateHeld;
            _contractActivateHeld = command.Activate;
            // Playing이 아닌 틱의 Step은 no-op이라 게이지 결과가 갱신되지 않는다 —
            // 보상/계약 화면에서 누른 SELECT가 지난 결과를 다시 울리면 안 된다.
            if (!pressed || !playingBefore) return;

            var gauge = _run?.PowerUpGauge;
            if (gauge == null) return;
            var result = gauge.LastActivationResult;
            if (result != PowerUpActivationResult.ContractGaugeActivationBanned
                && result != PowerUpActivationResult.ContractOptionActivationBanned
                && result != PowerUpActivationResult.ContractShieldActivationBanned)
                return;

            ContractLockResult = result;
            ContractLockPulse++;
        }

        public long TotalScore => _run?.TotalScore ?? 0;
        /// <summary>런 전체 통계 (완료 스테이지 + 현재 전투 합산 — Core 권위 값).</summary>
        public RunStatistics RunStats => _run != null ? _run.Statistics : default;
        /// <summary>현재 스테이지 테마 (BgmPlayer 등 표현 계층 참조용). null 가능.</summary>
        public string CurrentThemeId => _run != null && _run.StagePlan != null ? _run.StagePlan.ThemeId : null;
        public int RunNumber => _run?.RunNumber ?? 0;
        public int StageIndex => _run?.StageIndex ?? 0;
        public int Difficulty => _run?.Difficulty ?? 0;
        public bool IsRunOver => _run != null && _run.State == RunState.RunOver;

        /// <summary>런 오버 상태에서만 유효. 새 시드로 재출격 — 파워업 레벨은 MetaProgression 승계를 따른다.</summary>
        /// <summary>재출격 시 요약 지표 초기화 (RestartRun 경유).</summary>
        void ResetRunSummary()
        {
            BestMultiplier = 1;
        }

        public void RestartRun()
        {
            // 완주(RunCleared) 후에도 새 런을 시작할 수 있어야 한다 (REQ-031)
            if (_run == null || !_run.IsFinished) return;
            // TickCount 단독은 자릿수가 굳어 보인다 — 타이틀과 같은 혼합 시드 사용
            ulong newSeed = TitleScreen.NewRandomSeed() ^ ((ulong)(uint)_run.RunNumber << 32);
            _run.Restart(newSeed);
            Seed = (long)newSeed;
            IsDailyRun = false;   // 재출격은 새 시드다 — 더 이상 데일리 런이 아니다
            IsSeededRun = false;  // 위에서 뽑은 랜덤 시드다 — 지정 시드 낙인도 풀린다
            CheatUsed = false;    // 새 런은 깨끗하다 — 치트 낙인은 그 런에만 남는다
            // 단, 개발 플래그는 RunManager에 굳어 있어 재출격해도 그대로다 (REQ-096).
            if (_run.DevFlagsActive) MarkCheatUsed();
            // 재출격부터는 다른 런의 입력이다 — 지난 런 스트림에 이어 붙이면 재생이
            // 어긋난다. 직전 런의 리플레이는 종료 시점에 이미 저장돼 있다.
            _recordingActive = false;
            _wagerStockSaved = false;   // 새 런의 판돈은 다시 정산된다
            // 재출격은 새 런이라 St1 기록도 비워진다 (REQ-109) — 지난 런의 잔상이
            // 페이드 아웃으로 남으면 새 St1에 유령이 걸쳐 보인다.
            if (_ghostView != null) _ghostView.Hide();
            ResetRunSummary();
            RefreshBattle();
            SyncViews();
        }

        // ── 컨티뉴 경제 (REQ-104) ────────────────────────────────────────────────
        // 재고·가격·거절 사유는 전부 Core 판정이다. 화면은 물어보고 그리기만 한다.

        /// <summary>이 런에 남은 컨티뉴 재고 (메타 재고와 연동된 값).</summary>
        public int ContinueStock => _run?.ContinueStock ?? 0;

        /// <summary>지금 컨티뉴를 쓸 수 있는가 + 못 쓴다면 그 이유 (Core 관측).</summary>
        public ContinueAvailability ContinueAvailability =>
            _run != null ? _run.ContinueAvailability : default;

        /// <summary>이 런에서 컨티뉴를 쓴 횟수 (요약의 CONTINUED xN).</summary>
        public int ContinuesUsed => _run != null ? _run.Statistics.ContinuesUsed : 0;

        /// <summary>최종전 판돈이 정산됐는가 (진입 경계에서 Core가 1회 처리).</summary>
        public bool FinalWagerCommitted => _run != null && _run.FinalWagerCommitted;

        /// <summary>판돈으로 실드가 된 컨티뉴 수.</summary>
        public int FinalWagerShieldGranted => _run?.FinalWagerShieldGranted ?? 0;

        /// <summary>실드 상한을 넘겨 점수로 환산된 컨티뉴 수.</summary>
        public int FinalWagerOverflowConverted => _run?.FinalWagerOverflowConverted ?? 0;

        /// <summary>초과 컨티뉴가 만든 점수.</summary>
        public long FinalWagerScoreBonus => _run?.FinalWagerScoreBonus ?? 0;

        /// <summary>런 클리어 시 잔여 실드가 만든 보너스 점수 (REQ-105).</summary>
        public long RunClearShieldBonus => _run?.RunClearShieldBonus ?? 0;

        /// <summary>
        /// 컨티뉴 사용: 죽은 자리의 구간을 처음부터 다시 시작한다. 점수는 0으로
        /// 리셋되고 파워업은 기본 상태로 돌아가되 바이옴/방/계약/난이도는 유지된다 —
        /// 판정과 상태 복구는 전부 Core(TryUseContinue) 소관이다.
        ///
        /// Presentation이 할 일은 셋뿐이다: 교체된 배틀 인스턴스로 뷰를 다시 잡고,
        /// 메타 재고 차감을 디스크에 남기고, 점수 적립 기록을 열어 두는 것.
        /// </summary>
        public bool UseContinue()
        {
            if (_run == null || _replayMode) return false;
            if (!_run.TryUseContinue()) return false;

            // Core가 이미 메타 재고를 차감했다 — 저장으로 굳혀야 게임을 껐다 켜도
            // 쓴 컨티뉴가 되살아나지 않는다.
            if (_meta != null) MetaSave.Save(_meta);

            // 사망 시점에 이번 런 점수를 이미 적립했다. 컨티뉴는 점수를 0으로 되돌리고
            // 런을 이어가므로, 다음 종료에서 새로 쌓은 점수도 적립돼야 한다 —
            // "런당 1회" 잠금을 여기서 푼다 (이중 적립이 아니라 이어붙이기다).
            _lastCreditedRunNumber = 0;

            ResetRunSummary();
            RefreshBattle();
            SyncViews();
            return true;
        }

        /// <summary>서브유닛 ScrollX의 월드 단위 값. 배경 패럴랙스가 읽는다.</summary>
        public float ScrollWorldX => _sim != null ? _sim.ScrollX * SimView.WorldUnitsPerSubUnit : 0f;

        public int PlayerHp => _sim?.PlayerHp ?? 0;

        void Awake()
        {
            _onboarding = GetComponent<OnboardingHints>();
            if (!ValidateWiring()) return;

            Seed = DevArgs.OverrideSeed ?? DevArgs.RuntimeSeed ?? _seed;

            // GameData JSON이 유일한 원본 (AGENTS.md §5). 씬 재생성 시 Resources로 복사되고,
            // 파싱·단위 변환은 전부 Core(GameDataParser) 소관이다.
            var data = GameDataParser.Parse(
                LoadGameDataText("enemies"),
                LoadGameDataText("weapons"),
                LoadGameDataText("waves"),
                TryLoadGameDataText("rewards"),    // 없으면 Core 내장 풀 폴백 (REQ-G001)
                TryLoadGameDataText("ships"),      // 없으면 기본 함선 1척
                TryLoadGameDataText("scoring"));   // 없으면 Core 기본 그레이즈/콤보 수치 (REQ-016)

            // 적 티어별 히트박스 크기를 캐시해 스프라이트 스케일에 쓴다.
            CacheEnemyExtents(data);

            // 격납고 선택 함선 (저장이 채널 — Title 씬 HangarScreen이 기록)
            _meta = MetaSave.Load(data);
            var selectedShip = data.FindShip(_meta.SelectedShipId) ?? data.DefaultShip;

            // 리플레이 모드 (REQ-018/019): 기록 당시의 함선·시드로 재현. 이어하기보다 우선.
            var pendingReplay = PendingReplay;
            PendingReplay = null;
            if (pendingReplay != null)
            {
                _replayMode = true;
                selectedShip = data.FindShip(pendingReplay.shipId) ?? selectedShip;
                _playbackSource = new InputPlayback(pendingReplay.recording);
                _playback = _playbackSource.GetEnumerator();
                _replayContinueCursor = 0;
                _recordedChoices.Clear();
                if (pendingReplay.rewardChoices != null)
                    _recordedChoices.AddRange(pendingReplay.rewardChoices);
                _recordedRoutes.Clear();
                if (pendingReplay.routeChoices != null)
                    _recordedRoutes.AddRange(pendingReplay.routeChoices);
                _recordedContractChoices.Clear();
                if (pendingReplay.contractChoices != null)
                    _recordedContractChoices.AddRange(pendingReplay.contractChoices);
                _replayContractCursor = 0;
            }

            var config = data.CreateBattleSimConfig();
            // 장애물 그림 배율의 기준. Core가 장애물마다 반폭을 실어 줄 수 있으므로
            // 기본값 대비 몇 배인지로 그림을 맞춘다.
            _obstacleBaseHalfWidth = config.ObstacleHalfWidth;
            // 스키마에 아직 없는 잠정값 (스키마 v3 후보 — GameData로 옮기면 이 블록 제거)
            // EnemyDespawnX는 REQ-005 이후 Core 기본값(-22u, SimSpace 상수 파생)을 그대로 쓴다.
            // 캡슐 히트박스. 한 번 2배로 키웠다가(2026-08-03 "먹는 아이템 크기가
            // 너무 작아") 같은 날 "이제 너무 크다, 3/4로"라고 다시 지시받아 0.75배를
            // 곱한 값이다. 캡슐과 폭탄 아이템 뷰가 모두 이 판정 크기에 맞춰 그려지므로
            // 여기만 바꾸면 그림과 판정이 함께 움직인다 — 그림만 줄이면 전함 함미에서
            // 겪은 "보이는 것보다 판정이 크다"가 재발한다.
            // (2 × 0.75 = 1.5배 → 15/16 × 3/4 = 45/64, 3/4 × 3/4 = 9/16. 둘 다
            //  256 서브유닛 격자에 정확히 떨어진다: 180과 144.)
            config.CapsuleHalfWidth = SimSpace.SubUnitsPerWorldUnit * 45 / 64;
            _capsuleHalfWidthSubUnits = config.CapsuleHalfWidth;
            config.CapsuleHalfHeight = SimSpace.SubUnitsPerWorldUnit * 9 / 16;
            config.PlayerMaxHp = 3;

            // 개발용 런 시작 조건 (REQ-096). 새 런에만 건다 — 리플레이는 기록 당시
            // 조건을 그대로 재현해야 하고, 이어하기 저장은 dev 런에서 Core가 이미
            // 금지하므로 복원되는 파일은 항상 평범한 런이다.
            // DevArgs가 릴리스에서 false/null만 돌려주므로 여기서 다시 막지 않는다.
            bool devRunFlags = !_replayMode && PendingResume == null;
            config.PlayerInvulnerable = devRunFlags && DevArgs.GodMode;

            // 런 수명은 Core(RunManager) 소관: 스테이지 전환, 난이도 곡선, 사망 감지,
            // 재시작 시 파워업 승계까지. Presentation은 Step을 돌리고 Battle을 그릴 뿐이다.
            // 이어하기(REQ-017): 타이틀이 PendingResume을 채우면 새 런 대신 리줌한다.
            var pending = PendingResume;
            PendingResume = null;
            if (_replayMode) pending = null;   // 리플레이는 항상 새 런 재현
            if (pending != null)
            {
                try
                {
                    var resumeShip = data.FindShip(pending.shipId) ?? selectedShip;
                    var stageGenerator = new SegmentStageGenerator(data.StageGeneration);
                    // 기체 인식 게이지 (REQ-078) — 인자 없는 구버전은 7칸을
                    // 만들어 5칸 기체 검증에서 예외가 난다 (실측 재현).
                    var gauge = data.CreatePowerUpGauge(resumeShip);

                    // REQ-107: 이어하기 런도 살아 있는 메타에 물려야 컨티뉴 사용/최종전
                    // 판돈이 런 재고와 메타 재고에서 함께 빠진다 (메타 없는 오버로드로
                    // 리줌하면 이어한 런의 컨티뉴가 메타에 되살아나는 복제 구멍이 났다).
                    // Core는 비-데일리 런에서 저장 재고와 메타 재고가 어긋나면
                    // ArgumentException으로 거부한다 — 아래 catch가 새 런으로 넘긴다.
                    _run = _meta != null
                        ? RunManager.ResumeFromSuspendData(
                            pending,
                            stageGenerator,
                            config,
                            data.BattleContent,
                            gauge,
                            data.Rewards,
                            resumeShip,
                            _meta)
                        : RunManager.ResumeFromSuspendData(
                            pending,
                            stageGenerator,
                            config,
                            data.BattleContent,
                            gauge,
                            data.Rewards,
                            resumeShip);
                }
                catch (System.Exception e)
                {
                    // 새 런으로 여는 것까지가 이 폴백의 일이다 — 회계 불일치(REQ-107)든
                    // 손상이든 게임이 안 열리는 상태로 남기지 않는다. 저장 파일은
                    // 남겨 둔다(다음 실행에서 재시도 가능하고, 이 새 런이 끝나는
                    // 시점에 RunSave.Delete가 어차피 정리한다).
                    Debug.LogWarning($"[BattleDirector] 이어하기 실패({e.GetType().Name}) — 새 런으로 시작. {e.Message}");
                    _run = null;
                }
                if (_run != null)
                    RunSave.Delete();   // 복원 성공 후에만 소비 (심사 지적 반영)
            }
            // 데일리 표식은 "타이틀의 DAILY RUN으로 시작한 신규 런"에만 붙인다.
            // --seed 강제, 이어하기, 리플레이는 시드가 데일리와 같아도 같은 조건의 런이 아니다.
            // Core가 컨티뉴 재고를 데일리에서 0으로 강제하려면 런을 만들기 **전에**
            // 알아야 하므로 판정을 생성 앞으로 끌어올렸다.
            bool dailyRun = DevArgs.RuntimeDaily
                && DevArgs.OverrideSeed == null
                && !_replayMode
                && pending == null;

            if (_run == null)
            {
                // 난이도 배율 (REQ-020): 새 런은 타이틀 선택, 리플레이는 기록 당시 값
                int diffNum, diffDen;
                if (_replayMode && pendingReplay != null)
                {
                    diffNum = Mathf.Max(1, pendingReplay.difficultyNumerator);
                    diffDen = Mathf.Max(1, pendingReplay.difficultyDenominator);
                }
                else if (DevArgs.RuntimeDaily)
                {
                    // 데일리는 **NORMAL 고정**이다 (사람 결정 2026-08-04).
                    //
                    // 데일리의 존재 이유는 "전 세계가 같은 조건으로 겨룬다"인데, 시드만
                    // 맞추고 난이도는 각자 쓰던 값을 그대로 들고 갔다. 난이도가 바꾸는
                    // 것은 적 HP뿐이라 EASY/HARD가 같은 보드에서 비교 불가능해진다 —
                    // 게다가 HP가 오르면 보스전이 길어져 스치기·보스 딜 기회가 늘어
                    // **HARD 쪽이 점수를 더 버는** 방향이라 왜곡이 작지도 않다.
                    diffNum = 1;
                    diffDen = 1;
                }
                else
                {
                    DifficultySelect.GetMultiplier(out diffNum, out diffDen);
                }
                // 시작 스테이지 점프 (REQ-096). Core는 범위를 벗어나면 예외를 던지므로
                // 오타가 런을 아예 못 띄우는 일이 없게 여기서 캠페인 길이로 자른다.
                RunConfig runConfig = null;
                // 미지의 구역 직행 (REQ-123). 5바이옴 완주 + 히든 조건 2/3을 요구하는
                // 최종 항로에서만 열리던 거대 보스 2종을 바로 띄운다. 조건 카운터도
                // 함께 채워 둔다 — 히든 바이옴 안에서 조건을 다시 보는 경로가 있어도
                // 자격 미달로 흐름이 끊기지 않게. Core가 DevFlagsActive를 세워 제출은 닫힌다.
                if (devRunFlags && DevArgs.StartInUncharted)
                {
                    runConfig = new RunConfig(
                        startInHiddenBiome: true,
                        initialEliteRoomsCleared: 3,
                        initialNoHitBiomesCleared: 2,
                        initialRareEncountersCleared: 1);
                }
                else if (devRunFlags && DevArgs.OverrideStartStage.HasValue)
                {
                    int lastStage = RunProgressionConfig.CreateDefault().BiomeCount;
                    runConfig = new RunConfig(
                        Mathf.Clamp(DevArgs.OverrideStartStage.Value, 1, lastStage));
                }
                // 리플레이는 기록 당시의 컨티뉴 조건(초기 재고·데일리·경제 수치)까지
                // 재현해야 한다 — 최종전 판돈이 실드/점수를 바꾸므로 초기 재고가 다르면
                // 같은 입력이어도 결과가 갈린다 (REQ-104).
                else if (_replayMode && _playbackSource != null)
                    runConfig = _playbackSource.CreateRunConfig();
                // 데일리는 모두가 같은 조건으로 겨루는 판이라 컨티뉴가 들어오면 안 된다.
                // Core에 데일리임을 선언하면 재고가 0으로 강제되고 거절 사유도
                // NoStock이 아니라 DailyRun으로 정확해진다.
                else if (dailyRun)
                    runConfig = new RunConfig(isDailyRun: true);

                // 평범한 신규 런에만 MetaState를 붙인다. 붙는 순간 Core가 격납고에서
                // 산 컨티뉴 재고를 런 재고로 읽고, 컨티뉴 사용/최종전 판돈에서 메타
                // 재고까지 같이 차감한다 (Presentation에는 재고를 줄일 API가 없다).
                // 개발 시작-스테이지 런과 리플레이·데일리는 붙이지 않는다 — 그 런의
                // 컨티뉴가 진짜 재고를 먹으면 안 된다.
                // 개발자 패널에서 테마/히든 보스를 지정했으면, 그 결과가 나오는 시드를
                // 찾아 쓴다. Core에 강제 입구를 뚫지 않는 이유는 그 경로가 결정론의
                // 심장이기 때문이다 — 시드를 고르는 것은 원래 Presentation의 일이다.
                // 개발자 패널(RuntimeColossal)과 URL 인자(?colossal=)를 같이 본다 —
                // 헤드리스 검증은 패널을 누를 수 없으므로 URL 경로가 없으면
                // 두 거대 보스 중 한 쪽이 영영 검사되지 않는다.
                string devColossal = DevArgs.ColossalChoice;
                string devTheme = DevArgs.ThemeChoice;
                if (devRunFlags && !dailyRun && !_replayMode
                    && (!string.IsNullOrEmpty(devTheme)
                        || !string.IsNullOrEmpty(devColossal)))
                {
                    Seed = FindSeedFor(
                        data, config, selectedShip, diffNum, diffDen, runConfig,
                        devTheme, devColossal, Seed);
                }

                bool attachMeta = runConfig == null && _meta != null;
                _run = attachMeta
                    ? new RunManager(
                        (ulong)Seed,
                        new SegmentStageGenerator(data.StageGeneration),
                        config,
                        data.BattleContent,
                        data.CreatePowerUpGauge(selectedShip),
                        data.Rewards,
                        selectedShip,
                        diffNum,
                        diffDen,
                        _meta)
                    : new RunManager(
                        (ulong)Seed,
                        new SegmentStageGenerator(data.StageGeneration),
                        config,
                        data.BattleContent,
                        data.CreatePowerUpGauge(selectedShip),
                        data.Rewards,
                        selectedShip,
                        diffNum,
                        diffDen,
                        runConfig);
            }
            _sim = _run.Battle;
            CacheSegmentChainExtent();   // 첫 방의 체인 절 크기 (REQ-115b)

            // 개발 플래그가 걸린 런은 기록이 아니다 (REQ-096). F9/F10 치트와 같은
            // 경로로 제출을 닫는다 — 무적으로 5스테이지에서 시작한 점수가 보드에
            // 올라갈 이유가 없다. 판정은 Core(DevFlagsActive)가 하고 여기선 따르기만 한다.
            if (_run.DevFlagsActive) MarkCheatUsed();

            // 개발자 패널로 시작한 런도 같은 취급이다. 스테이지 선택·무적·풀강화는
            // 전부 "판을 건너뛰는" 수단이라 보드에 오를 이유가 없다.
            if (devRunFlags && DevArgs.RuntimeDevRun) MarkCheatUsed();

            // 게이지 전 슬롯 최대 (개발자 패널). 보스 패턴을 확인하려면 화력이
            // 있어야 하는데, 매번 캡슐을 모으고 있을 수는 없다.
            if (devRunFlags && DevArgs.MaxPower && _run.PowerUpGauge != null)
            {
                MarkCheatUsed();
                var maxed = new int[PowerUpGauge.SlotCount];
                for (int i = 0; i < maxed.Length; i++) maxed[i] = int.MaxValue;
                _run.PowerUpGauge.ImportLevels(maxed);   // 각 슬롯 상한으로 잘린다
            }

            // 데일리 판정은 런 생성 앞에서 이미 내렸다 (Core에 선언해야 하므로).
            // 이어하기로 복원된 런은 Core가 저장에서 데일리 여부를 되살린다.
            IsDailyRun = dailyRun || _run.IsDailyRun;

            // 지정 시드 낙인 (스코어보드 공정성). 타이틀 손입력과 --seed=N을 같이 본다 —
            // 둘 다 "같은 판을 다시 돌릴 수 있는" 런이다. 이어하기/리플레이는 제외.
            IsSeededRun = (DevArgs.RuntimeSeeded || DevArgs.OverrideSeed != null)
                && !_replayMode
                && pending == null;

            ApplyShipSprite(selectedShip != null ? selectedShip.Id : null);
            if (_sfx != null && selectedShip != null)
                _sfx.WeaponFamily = selectedShip.WeaponType;   // 계열별 발사음

            // 라이브 신규 런만 녹화한다 (리플레이/이어하기 런은 제외 — 첫 목숨 기준)
            if (!_replayMode && pending == null)
            {
                // 런에 묶인 녹화기: 난이도·캠페인 길이·컨티뉴 조건(초기 재고, 데일리,
                // 경제 수치)과 컨티뉴 결정 틱을 Core에서 직접 읽는다. 인자 없는
                // 생성자는 그 값들을 전부 기본값으로 굳혀서, 컨티뉴를 들고 최종전에
                // 들어간 런의 리플레이가 판돈 실드 없이 재생돼 어긋난다 (REQ-104).
                _recorder = new InputRecorder(_run);
                _recordingActive = true;
                _recordedChoices.Clear();
                _recordedRoutes.Clear();
                _recordedContractChoices.Clear();
                _recordShipId = selectedShip != null ? selectedShip.Id : null;
            }

            // 풀 용량은 Core가 허용하는 최대 개수와 맞춘다 — 런타임에 풀이 부족해질 수 없다.
            // 시뮬 Bullets는 플레이어탄+적탄 합산 리스트 — 풀도 합산 용량이어야 한다 (GROK 스트레스 검증 후속)
            _bulletPool = new SpritePool(
                _bulletPrefab, _bulletRoot, config.MaxBullets + config.MaxEnemyBullets, "Bullet");
            _enemyPool = new SpritePool(_enemyPrefab, _enemyRoot, 32, "Enemy");
            _capsulePool = new SpritePool(_capsulePrefab, _capsuleRoot, 16, "Capsule");
            if (_bombPickupPrefab != null && _bombPickupRoot != null)
            {
                _bombPickupPool = new SpritePool(
                    _bombPickupPrefab, _bombPickupRoot, config.MaxBombPickups, "BombPickup");
            }
            _fxPool = new SpritePool(_explosionPrefab, _fxRoot, 16, "Explosion");
            _optionPool = new SpritePool(_optionPrefab, _optionRoot, 6, "Option");   // 옵션 6기 (REQ-084)
            if (_obstaclePrefab != null && _obstacleRoot != null)
                _obstaclePool = new SpritePool(_obstaclePrefab, _obstacleRoot, config.MaxObstacles, "Obstacle");

            var bulletPrefabRenderer = _bulletPrefab.GetComponent<SpriteRenderer>();
            _mainShotSprite = bulletPrefabRenderer != null ? bulletPrefabRenderer.sprite : null;
            _vulcanShotSprite = _mainShotSprite;
            if (selectedShip != null)
                ApplyWeaponBulletSprite(selectedShip.WeaponType);

            // 고스트 뷰 (REQ-109). 최종 바이옴 전에는 한 번도 켜지지 않지만, 렌더러
            // 네 개짜리라 미리 만들어 두고 알파 0으로 재워 둔다 — 합류 프레임에
            // GameObject를 만들면 그 프레임에 스파이크가 난다.
            _ghostView = GhostView.Create(_playerTransform);

            if (_damageFlash != null)
                _damageFlash.color = new Color(1f, 0.2f, 0.2f, 0f);
            if (_shieldView != null)
                _shieldView.enabled = false;

            ApplyStageTheme();
            ApplyBossSprite();
            SyncViews();

            // 구간 워프 (REQ-124). 뷰가 다 선 뒤에 돌려야 워프 종료 시점의 상태가
            // 그대로 그려진다. 리플레이·이어하기에는 걸지 않는다(devRunFlags와 같은 이유).
            //
            // 워프 자체를 치트로 친다: Core의 DevFlagsActive는 무적·시작스테이지처럼
            // **런 생성 조건**이 바뀔 때만 서므로, 워프만 쓴 런은 그대로면 제출이 열린다.
            // 구간을 건너뛴 점수가 보드에 오를 이유가 없어 F9/F10과 같은 경로로 닫는다.
            if (_run != null && devRunFlags && DevArgs.WarpSection.HasValue)
            {
                MarkCheatUsed();
                DevWarpToSection(DevArgs.WarpSection.Value);
            }
        }

        void Update()
        {
            AnimateExplosions();
            AnimateDamageFlash();
            AnimatePunches();
            AnimateMuzzleFlash();
            TickPendingBooms();
            TickBossDeathCinematic();

            // 리플레이 종료 → 잠시 후 타이틀 복귀
            if (_replayMode && (IsRunOver || _replayStreamEnded))
            {
                _replayEndTimer -= Time.unscaledDeltaTime;
                if (_replayEndTimer <= 0f)
                {
                    _replayMode = false;
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
                }
            }
        }

        /// <summary>
        /// 이 틱에 기록된 컨티뉴 결정이 있으면 재현한다. 결정 틱은 엄격한 오름차순이라
        /// 커서 하나로 충분하다. 재생 런에는 MetaState를 붙이지 않으므로 실제 재고를
        /// 먹지 않는다 — 기록 당시의 초기 재고(CreateRunConfig)만 소비한다.
        /// </summary>
        void ReplayContinueIfRecorded()
        {
            if (_playbackSource == null) return;
            var decisions = _playbackSource.ContinueDecisions;
            if (decisions == null || _replayContinueCursor >= decisions.Count) return;
            if (decisions[_replayContinueCursor].SimulationTick != _run.SimulationTicksElapsed)
                return;
            _replayContinueCursor++;
            if (!_run.TryUseContinue()) return;
            ResetRunSummary();
            RefreshBattle();
            SyncViews();
        }

        void FixedUpdate()
        {
            if (_run == null) return;

            bool playingBefore = _run.State == RunState.Playing;
            InputCommand command;
            if (_replayMode)
            {
                if (playingBefore)
                {
                    if (_playback.MoveNext()) command = _playback.Current;
                    else { command = InputCommand.None; _replayStreamEnded = true; }
                }
                else
                {
                    command = InputCommand.None;
                    // 보상 대기: 기록된 선택을 즉시 재현 (기록도 Playing 틱만 담아 정렬 유지)
                    if (_run.State == RunState.AwaitingReward)
                    {
                        int choice = _replayChoiceCursor < _recordedChoices.Count
                            ? _recordedChoices[_replayChoiceCursor++] : 0;
                        // -1 = 리롤 (REQ-072). 리롤은 대기 상태를 유지하므로 다음
                        // 틱에 이어지는 기록(다음 리롤 또는 실제 선택)을 소비한다.
                        if (choice == RerollChoiceSentinel)
                            _run.RerollRewardOptions();
                        else
                            _run.ChooseReward(choice);
                    }
                    // 계약 대기: 기록된 계약 선택을 재현. 기록이 모자라면 0(표준 항로) —
                    // Core가 표준 항로를 항상 0번에 두므로 안전한 기본값이다.
                    else if (_run.State == RunState.AwaitingContract)
                    {
                        int choice = _replayContractCursor < _recordedContractChoices.Count
                            ? _recordedContractChoices[_replayContractCursor++] : 0;
                        _run.ChooseContract(choice);
                    }
                    // 컨티뉴 결정 재현 (REQ-104). 컨티뉴는 일반 입력 틱이 아니라
                    // RunOver에서 내리는 별도 결정이라 누적 시뮬 틱으로 기록된다 —
                    // 그 틱에 도달했고 런이 실제로 끝나 있을 때만 적용한다.
                    else if (_run.State == RunState.RunOver)
                        ReplayContinueIfRecorded();
                    // 경로 선택 재현은 없어졌다 (REQ-054). 구버전 리플레이의 route
                    // payload는 열리기만 하고, 재생은 현 빌드 규칙을 따른다.
                }
            }
            else
            {
                command = _input.ConsumeCommand();
            }
            if (_recordingActive && playingBefore)
            {
                // 용량 방어 (스테이지 1 크래시, 2026-07-30 폰 스크린샷). 녹화는 같은
                // 입력이 이어지면 한 칸으로 압축하는데, 터치 아날로그 입력은 매 틱
                // 델타가 달라 압축이 전혀 안 되고 4096칸이 ~68초 만에 찬다. 가득 찬
                // 뒤의 Record는 예외를 던져 Update가 매 프레임 죽는다.
                //
                // 잘린 리플레이는 재생 시 어차피 어긋나므로, 차기 직전에 녹화를 접고
                // 리플레이 저장을 포기한다 — 게임은 계속된다. 근본(용량/아날로그 압축)은
                // Core 몫이라 REQ로 넘겼다.
                if (_recorder.RunCount >= _recorder.Capacity - 1)
                {
                    _recordingActive = false;
                    Debug.LogWarning(
                        "[replay] 입력 녹화 용량 도달 — 이번 런은 리플레이 저장 없이 진행한다.");
                }
                else
                {
                    _recorder.Record(in command);
                }
            }
            if (_onboarding != null) _onboarding.BeforeStep(_run.Battle, Gauge, in command);
            _run.Step(command);
            if (_onboarding != null) _onboarding.AfterStep(_run.Battle);
            DetectContractLock(in command, playingBefore);

            // 최종전 판돈 (REQ-104): Core가 최종 보스 진입에서 남은 컨티뉴를 전부
            // 회수해 메타 재고까지 비웠다. 런이 끝나기 전에 저장해 두지 않으면
            // 그 사이의 강제 종료가 이미 실드로 바꾼 컨티뉴를 되살린다.
            if (!_wagerStockSaved && _run.FinalWagerCommitted)
            {
                _wagerStockSaved = true;
                if (_meta != null && !_replayMode) MetaSave.Save(_meta);
            }

            // 런 종료(사망 또는 완주) 시 점수를 메타 재화로 1회 적립. 리플레이는 비적립.
            // 컨티뉴로 이어간 런은 UseContinue가 이 잠금을 다시 열어, 컨티뉴 뒤에 새로
            // 쌓은 점수도 종료 시점에 이어 적립된다 (컨티뉴는 점수를 0으로 되돌린다).
            if (_run.IsFinished
                && !_replayMode
                && _meta != null
                && _run.RunNumber != _lastCreditedRunNumber)
            {
                _lastCreditedRunNumber = _run.RunNumber;
                _meta.CreditScore(_run.TotalScore);
                MetaSave.Save(_meta);
                RunSave.Delete();   // 런 종료 — 이어하기 무효화

                // 마지막 런 리플레이 저장. 녹화는 여기서 접지 않는다 — 컨티뉴로 런이
                // 이어지면 같은 스트림이 계속 자라고, 다음 종료에서 컨티뉴 결정까지
                // 담긴 완전한 리플레이로 덮어쓴다. 녹화를 접는 곳은 재출격(RestartRun)
                // 하나뿐이다: 거기서부터는 다른 런의 입력이라 한 파일에 담을 수 없다.
                if (_recordingActive)
                {
                    ReplaySave.Save(new ReplayFileData
                    {
                        seed = Seed,
                        shipId = _recordShipId,
                        finalScore = _run.TotalScore,
                        difficultyNumerator = _run.DifficultyMultiplierNumerator,
                        difficultyDenominator = _run.DifficultyMultiplierDenominator,
                        rewardChoices = _recordedChoices.ToArray(),
                        contractChoices = _recordedContractChoices.ToArray(),
                        routeChoices = _recordedRoutes.ToArray(),
                        recording = _recorder.Export()
                    });
                }
            }

            // 이벤트는 스텝 직후 같은 호출 안에서 소비한다 — 다음 Step에서 클리어되기 때문.
            var battle = _run.Battle;
            bool freshEvents = !ReferenceEquals(battle, _lastEventSim) || battle.Tick != _lastEventTick;
            _lastEventSim = battle;
            _lastEventTick = battle.Tick;
            var events = freshEvents ? battle.EventsThisTick : System.ReadOnlySpan<SimEvent>.Empty;
            if (_sfx != null)
                _sfx.PlayEvents(events);
            _blockSparkThisTick = false;
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                switch (e.Type)
                {
                    case SimEventType.EnemyHit:
                        PunchEnemy(e.EntityId);
                        break;
                    case SimEventType.EnemyKilled:
                        SpawnExplosion(SimView.ToWorld(e.X, e.Y), 1f,
                            _enemyDeathTints.TryGetValue(e.EntityId, out var deathTint)
                                ? deathTint : Color.white);
                        if (_scorePopups != null)
                            _scorePopups.Spawn(SimView.ToWorld(e.X, e.Y), e.Arg);   // Arg = 부여 점수 (REQ-024)
                        break;
                    case SimEventType.PlayerKilled:
                        SpawnExplosion(SimView.ToWorld(e.X, e.Y));
                        if (_juice != null)
                        {
                            _juice.Shake(0.45f);
                            _juice.Slowmo(0.35f, 0.6f);
                        }
                        break;
                    case SimEventType.PlayerHit:
                        // 런 종료 후에도 피격 이벤트가 이어질 수 있다 — 영구 히트스톱 방지
                        if (_juice != null && !IsRunOver)
                        {
                            _juice.Shake(0.28f);
                            _juice.Hitstop(0.06f);
                        }
                        break;
                    case SimEventType.BossFormChanged:
                        // Core가 준 폼 id로 그림을 바꾼다. 등록이 없으면 접두사
                        // 매칭이 원래 보스 그림으로 되돌아가므로 안전하다.
                        _bossFormId = e.PartId;
                        ApplyBossSprite();
                        break;
                    case SimEventType.BossFormTransitionStarted:
                        // 함체가 무너지고 안에서 다음 폼이 나온다 (REQ-139).
                        // 한 점에서 터뜨리면 20유닛짜리 배가 점 하나로 죽는다 —
                        // 함체 길이를 따라 훑으며 무너뜨린다.
                        SpawnHullCollapse(SimView.ToWorld(e.X, e.Y));
                        // **두 번째 형태가 온다는 것을 말한다.** 사람이 "0까지
                        // 가도 안바뀜"이라고 세 번 보고했는데, Core는 모든 경로에서
                        // 전환이 일어난다(테스트로 확인). 그러면 화면이 그것을
                        // 말하지 않는 것이 문제다 — HP 바가 0이 되고 사라진 뒤
                        // 3초 동안 아무 신호가 없으면 "끝났는데 안 끝났다"로 읽힌다.
                        // 등장 배너를 다시 띄워 그 3초를 사건으로 만든다.
                        if (_bossIntro != null) _bossIntro.Trigger();
                        break;
                    case SimEventType.BossSpawned:
                        // WARNING 배너는 스테이지 최종 보스(와 숨은 보스)에게만 띄운다
                        // ("중간보스 나올때 Warning 뜨는것도 이상함", 2026-07-30).
                        // 중간보스는 스테이지마다 나오는 통과 의례라 매번 배너가 뜨면
                        // 경고의 무게가 사라진다 — 흔들림만 남긴다.
                        if (_bossIntro != null
                            && StageSection != RunStageSection.MidBoss)
                            _bossIntro.Trigger();
                        if (_juice != null) _juice.Shake(0.3f);
                        break;
                    case SimEventType.BossPhaseChanged:
                        // 페이즈가 넘어간 것이 명백해야 한다 — 예전에는 탄종만 바뀌어
                        // 플레이어가 눈치채지 못했다 (REQ-054). 33% 광폭화는 66%보다
                        // 확실히 크게 알린다. Arg는 0-based 페이즈 index(1 또는 2).
                        bool enraged = e.Arg >= 2;
                        if (_juice != null)
                        {
                            _juice.Shake(enraged ? 0.55f : 0.3f);
                            _juice.Hitstop(enraged ? 0.14f : 0.08f);
                        }
                        _bossPhaseFlash = enraged ? 0.75f : 0.45f;
                        _bossPhaseFlashPeak = enraged ? 1f : 0.6f;
                        // WARNING 배너를 여기서 다시 틀지 않는다 ("보스 HP가 내려갈때마다
                        // Warning이 또 뜨는건 이상해", 2026-07-30). 배너는 "보스가 왔다"는
                        // 등장 신호인데 페이즈마다 반복되면 의미가 섞인다 — 페이즈 전환은
                        // 흔들림·히트스톱·보스 글로우만으로 알린다.
                        break;
                    case SimEventType.CorridorContact:
                        // 벽에 닿았다 — 어디가 벽인지 번쩍여 알린다 (실제 피해는 PlayerHit).
                        if (_gimmickView != null) _gimmickView.FlashCorridorContact();
                        if (_juice != null) _juice.Shake(0.12f);
                        break;
                    case SimEventType.TimeLimitExpired:
                        // 제한 시간 초과는 방어막·무적을 무시하는 즉사다. 사망 연출과
                        // 구분되게 크게 알린다.
                        if (_juice != null)
                        {
                            _juice.Shake(0.7f);
                            _juice.Hitstop(0.2f);
                        }
                        break;
                    case SimEventType.BossAttackTelegraphed:
                        // 위험 패턴 예고 (REQ-059). 이벤트만 있고 그리지 않으면 예고가
                        // 없는 것과 같다 — 회피가 실력이 되려면 눈에 보여야 한다.
                        // 보스 본체를 잠깐 밝히는 기존 페이즈 플래시를 약하게 재사용한다.
                        _bossPhaseFlash = Mathf.Max(_bossPhaseFlash, 0.3f);
                        _bossPhaseFlashPeak = Mathf.Max(_bossPhaseFlashPeak, 0.5f);
                        break;
                    case SimEventType.BombActivated:
                        // 화면을 지우는 사건이므로 가장 크게 알린다. 무적 시간이 함께
                        // 붙으므로(45틱) 플레이어가 "지금 안전하다"를 읽을 수 있어야 한다.
                        _bombFlashAge = 0f;
                        if (_juice != null)
                        {
                            _juice.Shake(0.6f);
                            _juice.Hitstop(0.12f);
                        }
                        break;
                    case SimEventType.BombActivationRejectedEmpty:
                        // 눌렀는데 아무 일도 없으면 버튼이 고장난 것처럼 느껴진다.
                        // 재고가 없다는 것을 짧게 알린다.
                        if (_bombButton != null) _bombButton.FlashEmpty();
                        break;
                    case SimEventType.LaserCapacityExceeded:
                    case SimEventType.EnemyCapacityExceeded:
                    case SimEventType.ObstacleCapacityExceeded:
                        // 상한 초과를 조용히 넘기지 않는다 — 과거에 적 탄이 상한에 걸려
                        // 조용히 발사되지 않던 버그를 놓친 전례가 있다.
                        if (Debug.isDebugBuild || Application.isEditor)
                            Debug.LogWarning(
                                $"[capacity] {e.Type} cap={e.Arg} at ({e.X},{e.Y})");
                        break;
                    case SimEventType.StageCleared:
                        TriggerBossDeathSequence();
                        break;
                    case SimEventType.PlayerFired:
                        _muzzleAge = 0f;
                        break;
                    case SimEventType.BulletRicocheted:
                        SpawnExplosion(SimView.ToWorld(e.X, e.Y), 0.45f);   // 도탄 스파크 (소형)
                        break;
                    case SimEventType.GrazeScored:
                        SpawnExplosion(SimView.ToWorld(e.X, e.Y), 0.3f);    // 그레이즈 스파크
                        break;
                    // 무적 파츠에 막힌 탄 (REQ-125). 화면이 거짓말을 하던 자리다 —
                    // Core는 탄을 지우되 데미지를 0으로 두는데, 슈팅에서 탄이 닿아
                    // 사라지는 것은 "맞았다"의 가장 강한 신호라 플레이어(와 테스터 5명)가
                    // 헛치는 줄 모르고 계속 쐈다.
                    // 어휘를 확실히 가른다: 피격은 흰·주황 폭발, 차단은 **차가운 청록**이고
                    // 크기도 더 작다. "여기는 지금 못 깎는다"가 한 프레임에 읽혀야 한다.
                    case SimEventType.BossPartHitBlocked:
                        // 사람 보고 2026-08-03: "처음 몸체를 때려도 데미지가 안 들어가는데
                        // 이걸 알 수가 없네." 작은 스파크로는 탄 소멸과 구분이 안 됐다.
                        // 두 겹으로 말한다 — 큰 청록 링(막혔다) + 튕겨 나가는 작은 불꽃.
                        // 색은 파츠 잠금 브래킷과 같은 청록이라 "저 색 = 지금 못 깎는다"가
                        // 하나의 어휘로 묶인다.
                        SpawnExplosion(
                            SimView.ToWorld(e.X, e.Y), 0.85f, BlockedHitTint);
                        SpawnExplosion(
                            SimView.ToWorld(e.X - SimSpace.SubUnitsPerWorldUnit / 2, e.Y),
                            0.4f, BlockedHitTint);
                        break;
                    case SimEventType.MultiplierChanged:
                        if (e.Arg > BestMultiplier) BestMultiplier = e.Arg;
                        break;
                    case SimEventType.KillExplosionTriggered:
                        SpawnExplosion(SimView.ToWorld(e.X, e.Y), 1.4f);    // 광역 폭발 (대형)
                        if (_juice != null) _juice.Shake(0.1f);
                        break;
                    case SimEventType.ObstacleDestroyed:
                        SpawnExplosion(SimView.ToWorld(e.X, e.Y), 0.9f);
                        if (_scorePopups != null)
                            _scorePopups.Spawn(SimView.ToWorld(e.X, e.Y), e.Arg);
                        break;
                    case SimEventType.ObstacleDamaged:
                        _obstacleHitFlashes[e.EntityId] = ObstacleHitFlashSeconds;
                        break;
                    case SimEventType.EnemyBulletBlocked:
                        // 탄이 왜 사라졌는지만 알린다 (REQ-101 C-A). X/Y는 Core가 준
                        // 소거 지점 = 장애물 표면이라 뷰가 위치를 추정하지 않는다.
                        if (!_blockSparkThisTick && _gimmickView != null)
                        {
                            _blockSparkThisTick = true;
                            _gimmickView.FlashBulletBlock(SimView.ToWorld(e.X, e.Y));
                        }
                        break;
                    case SimEventType.ObstacleRegenerated:
                        // 재생은 스폰이 아니다 — SyncObstacles가 이 표시를 보고
                        // 기본 페이드 대신 "자라나는" 연출로 갈아탄다 (REQ-101 C-B).
                        _obstacleRegenAges[e.EntityId] = 0f;
                        break;
                    case SimEventType.GhostSpawned:
                        // 과거의 내가 합류했다 (REQ-109). 위치는 Core가 준 스폰 지점을
                        // 그대로 쓴다 — 뷰가 첫 프레임 위치를 추정하면 잔상이 화면
                        // 밖에서 날아오는 꼬리를 한 번 그린다. Arg는 고정 무기 레벨.
                        if (_ghostView != null) _ghostView.OnSpawned(e.X, e.Y);
                        GhostSpawnSequence++;
                        // 흔들림은 짧게. 보스 등장(0.3)보다 확실히 작아야 "위협이
                        // 하나 늘었다"가 아니라 "아군 신호"로 읽힌다.
                        if (_juice != null) _juice.Shake(0.15f);
                        break;
                    case SimEventType.GhostEnded:
                        // 기록이 끝났거나 최종 보스가 먼저 끝났다. 사라지는 이유는
                        // 화면에 설명하지 않는다 — 시간이 다 됐다는 페이드면 족하다.
                        if (_ghostView != null) _ghostView.OnEnded();
                        break;
                    case SimEventType.BossPartMeleeTelegraphed:
                        // 근접 공격 예고 (사람 지시 2026-08-05: "번쩍임 등으로
                        // 사전 예고 있음"). 본체가 통째로 밀고 들어오는 공격이라
                        // 예고가 없으면 화면 어디에 있든 피할 수 없다.
                        // Arg = 예고 길이(틱).
                        if (_bossPartsView != null)
                            _bossPartsView.OnMeleeTelegraph(
                                e.PartId, e.Arg / (float)SimSpace.TicksPerSecond);
                        break;
                    case SimEventType.BossPartDestroyed:
                        // 멀티파트 보스의 파츠가 떨어졌다. 지금까지 아무 연출도 없어서
                        // 큰 파츠가 조용히 사라졌다 — 거대 전함(REQ-110/111)에서는
                        // 함미·포탑 파괴가 전투의 마디라 반드시 사건으로 읽혀야 한다.
                        // 기존 폭발을 그대로 쓴다. **스코어 팝업은 띄우지 않는다** —
                        // Core는 파츠 파괴에 점수를 주지 않으므로(격파 시 EnemyKilled로
                        // 한 번에 준다) 여기서 숫자를 띄우면 합계와 어긋난다.
                        SpawnPartDestruction(e.PartId, SimView.ToWorld(e.X, e.Y));
                        break;
                    case SimEventType.SegmentChainSpawned:
                        // St4 번개룡 소환 (REQ-115b). 체인은 보스 뒤에서 미끄러져 나오므로
                        // 소환 프레임을 못 보면 "언제 붙었는지 모르는 접촉 데미지"가 된다.
                        // 머리 스폰점에 짧은 방전을 터뜨리고 화면을 살짝 흔든다.
                        SpawnExplosion(SimView.ToWorld(e.X, e.Y), 0.9f, ChainSparkTint);
                        if (_juice != null) _juice.Shake(0.18f);
                        break;
                    case SimEventType.SegmentChainDestroyed:
                        // 머리를 부수면 절 전체가 같은 틱에 사라진다 — 폭발 없이 지우면
                        // 화면에서 증발한 것처럼 보인다. Arg = 제거된 절 수라 규모를
                        // 그대로 크기에 쓴다. 점수 팝업은 없다 (Core가 점수를 주지 않는다).
                        SpawnExplosion(
                            SimView.ToWorld(e.X, e.Y),
                            1.2f + 0.06f * Mathf.Max(0, e.Arg),
                            ChainSparkTint);
                        if (_juice != null)
                        {
                            _juice.Shake(0.3f);
                            _juice.Hitstop(0.05f);
                        }
                        break;
                    case SimEventType.MidBossDefeated:
                        // 구간이 넘어가는 프레임을 정확히 집는다 (REQ-101 C-E).
                        // RunStageSection 폴링은 보상 화면을 지나 AdvanceRoom이 돌아야
                        // Closing이 되므로, 배경 전환이 격파보다 수 초 늦게 시작했다.
                        _midBossDefeatSignaled = true;
                        break;
                }
            }
            RefreshBattle();
            SyncViews();
        }

        /// <summary>
        /// Core는 스테이지 전환/재시작 때 IBattleSim 인스턴스를 교체한다. Id 공간이
        /// 새로 시작되므로 이전 배틀의 뷰를 전부 반납하고 새 인스턴스로 갈아탄다.
        /// </summary>
        void RefreshBattle()
        {
            var battle = _run.Battle;
            if (ReferenceEquals(battle, _sim)) return;

            ReleaseAll(_bulletViews, _bulletPool);
            ReleaseAll(_enemyViews, _enemyPool);
            ReleaseAll(_capsuleViews, _capsulePool);
            if (_bombPickupPool != null) ReleaseAll(_bombPickupViews, _bombPickupPool);
            ReleaseAll(_optionViews, _optionPool);
            if (_obstaclePool != null) ReleaseAll(_obstacleViews, _obstaclePool);
            _enemyRenderers.Clear();
            _enemyDeathTints.Clear();
            _obstacleRegenAges.Clear();      // Id 공간이 새로 시작한다 — 재생 표시 잔류 금지
            _emitterAngles.Clear();          // 같은 이유로 포탑 조준각도 버린다
            _emitterHeat.Clear();
            _midBossDefeatSignaled = false;  // 다음 스테이지의 중간보스를 위해 초기화
            _lastHp = -1;   // 배틀 교체 직후 HP 차이를 피격 플래시로 오인하지 않게
            // 고스트는 Closing→최종 보스 경계를 넘어 살아 있다 (REQ-109) — 여기서
            // 숨기면 보스전에서 다시 뜨지 않는다(재합류 이벤트가 없다). 방이 바뀌며
            // 좌표가 한 프레임에 크게 튀므로 잔상만 접는다.
            if (_ghostView != null) _ghostView.ResetTrail();

            _sim = battle;
            _bossFormId = null;    // 방이 바뀌면 폼도 처음부터다
            _lastBossHp = -1;      // 보스 피격 플래시 오인 방지
            ApplyStageTheme();
            ApplyBossSprite();
            CacheSegmentChainExtent();   // 방마다 체인 절 크기가 달라진다 (REQ-115b)
        }

        /// <summary>
        /// Core가 정한 StagePlan.ThemeId로 배경을 고른다 (테마-보스 바인딩과 일치 보장).
        /// ThemeId가 없거나 미등록이면 스테이지 번호 로테이션 폴백.
        /// </summary>
        void ApplyStageTheme()
        {
            if (_themeBackgrounds == null || _themeBackgrounds.Length == 0 || _run == null) return;

            int index = -1;
            string themeId = _run.StagePlan != null ? _run.StagePlan.ThemeId : null;
            if (!string.IsNullOrEmpty(themeId) && _themeIds != null)
            {
                int count = Mathf.Min(_themeIds.Length, _themeBackgrounds.Length);
                for (int i = 0; i < count; i++)
                    if (string.Equals(_themeIds[i], themeId, System.StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
            }
            if (index < 0)
            {
                index = (_run.StageIndex - 1) % _themeBackgrounds.Length;
                if (index < 0) index += _themeBackgrounds.Length;
            }

            for (int i = 0; i < _themeBackgrounds.Length; i++)
                if (_themeBackgrounds[i] != null && _themeBackgrounds[i].activeSelf != (i == index))
                    _themeBackgrounds[i].SetActive(i == index);
        }

        /// <summary>
        /// 현재 보스 **폼**의 id. 2단 폼으로 넘어가면 Core가 이벤트로 알려 준다.
        ///
        /// 스테이지의 BossId만 보면 폼이 바뀌어도 그림이 그대로다. 전함처럼
        /// "함체를 부수면 안에서 로봇이 나온다"(REQ-139)에서는 20유닛짜리 함체
        /// 그림이 5유닛짜리 로봇 자리에 그려진다 — 반드시 갈라야 한다.
        /// </summary>
        string _bossFormId;

        /// <summary>
        /// 지금 보스가 두 번째 형태인가. 히든 보스는 마지막 코어 페이즈에서만
        /// 일반 보스곡으로 갈아탄다 (사람 지시 2026-08-05: "히든보스 첫 페이즈1~3은
        /// 히든보스 노래, 마지막 코어가 나오는 페이즈만 일반 보스곡").
        /// </summary>
        public bool IsBossSecondForm => _bossFormId != null;

        /// <summary>
        /// 개발용: 지금 때릴 수 있는 보스 파츠(또는 본체)를 최대 HP의 10%만큼 깎는다.
        ///
        /// 왜 필요한가: 히든 보스는 잡는 데 100~150초가 걸린다. 두 번째 형태나
        /// 클리어 화면처럼 **싸움 끝에만 보이는 것**을 한 번 확인하려고 매번
        /// 그 시간을 다 쓸 수는 없다. 2026-08-05에 form2를 눈으로 확인하려다
        /// 하네스를 300초씩 돌리고도 못 봤다.
        ///
        /// 치트라 MarkCheatUsed로 기록된다 — 보드에 오르지 않는다.
        /// </summary>
        /// <summary>
        /// 개발용: 이 브라우저의 **메타 진행을 초기화**한다 — 크레딧·기체 해금·
        /// 컨티뉴 재고가 사라지고 기본 함선만 남는다. 난이도·접근성 설정
        /// (PlayerPrefs)은 건드리지 않는다.
        /// </summary>
        public void DevResetMeta()
        {
            MetaSave.DeleteSave();
            MarkCheatUsed();
            Debug.Log("[dev] meta save cleared - credits and unlocks reset");
        }

        public void DevDamageBoss()
        {
            if (!(_sim is BattleSim sim)) return;
            if (sim.DevDamageBoss(10)) MarkCheatUsed();
        }

        void ApplyBossSprite()
        {
            if (_bossRenderer == null || _run == null || _run.StagePlan == null) return;
            if (_bossSpritePrefixes == null || _bossSprites == null) return;
            string bossId = _bossFormId ?? _run.StagePlan.BossId;
            int count = Mathf.Min(_bossSpritePrefixes.Length, _bossSprites.Length);
            Sprite best = null;
            int bestLength = -1;
            for (int i = 0; i < count; i++)
            {
                string prefix = _bossSpritePrefixes[i];
                if (string.IsNullOrEmpty(prefix) || _bossSprites[i] == null) continue;
                if (bossId.StartsWith(prefix, System.StringComparison.Ordinal)
                    && prefix.Length > bestLength)
                {
                    best = _bossSprites[i];
                    bestLength = prefix.Length;
                }
            }
            if (best != null)
                _bossRenderer.sprite = best;
        }

        static void ReleaseAll(Dictionary<int, Transform> views, SpritePool pool)
        {
            foreach (var pair in views) pool.Release(pair.Value);
            views.Clear();
        }

        /// <summary>시뮬 상태를 트랜스폼에 그대로 복사한다. 보간 없음 — 픽셀 퍼펙트라 스냅이 맞다.</summary>
        void SyncViews()
        {
            _playerTransform.localPosition = SimView.ToWorld(_sim.PlayerX, _sim.PlayerY);
            SyncPlayerInvulnerabilityBlink();

            TrackWeaponTypeChange();
            SyncGhost();
            SyncBullets();
            SyncOptions();
            SyncEnemies();
            SyncCapsules();
            SyncBombPickups();
            SyncObstacles(Time.deltaTime);
            SyncShield();
            SyncBoss();
        }

        /// <summary>
        /// 무적 동안 기체를 깜빡인다 (사람 지시 2026-08-03: "피격 당했을 때
        /// 깜빡이면서 2~3초 정도 무적시간이 있어야 할듯").
        ///
        /// 무적은 Core가 정하고 여기서는 **그 상태를 보이게만** 한다. 깜빡임이
        /// 없으면 2.5초 동안 자기가 무적인 줄 모르고 계속 피하기만 한다 —
        /// 무적은 도망칠 시간을 주려고 있는 것이라, 보이지 않으면 없는 것과 같다.
        ///
        /// 렌더러를 껐다 켜는 대신 알파만 흔든다. 끄면 기체가 어디 있는지 놓친다.
        /// </summary>
        void SyncPlayerInvulnerabilityBlink()
        {
            var renderer = PlayerRenderer;
            if (renderer == null) return;
            int remaining = _sim.PlayerInvulnerabilityTicksRemaining;
            if (remaining <= 0)
            {
                if (_playerBlinking)
                {
                    _playerBlinking = false;
                    var restored = renderer.color;
                    renderer.color = new Color(restored.r, restored.g, restored.b, 1f);
                }
                return;
            }
            _playerBlinking = true;
            // 틱 기반이라 프레임률과 무관하게 같은 속도로 깜빡인다.
            bool bright = (remaining / 5) % 2 == 0;
            var color = renderer.color;
            renderer.color = new Color(color.r, color.g, color.b, bright ? 1f : 0.35f);
        }

        bool _playerBlinking;

        /// <summary>장애물 뷰 동기화 (REQ-023). 테마×계열로 스프라이트를 고른다.</summary>
        void SyncObstacles(float deltaTime)
        {
            if (_obstaclePool == null) return;
            var obstacles = _sim.Obstacles;
            TrackLaserEmitters();
            _aliveIds.Clear();
            for (int i = 0; i < obstacles.Count; i++)
            {
                var obstacle = obstacles[i];
                _aliveIds.Add(obstacle.Id);
                bool emitter = obstacle.Type == ObstacleType.LaserEmitter;
                // 재생(REQ-101 C-B)은 같은 id로 다시 등장한다. 뷰는 파괴 시점에 반납됐으므로
                // 여기서 새로 얻는데, 이때 스폰 페이드가 아니라 성장 연출을 걸어야 한다.
                bool regenerating = _obstacleRegenAges.TryGetValue(obstacle.Id, out float regenAge)
                    && regenAge < ObstacleRegenSeconds;
                float baseScale = ObstacleViewScale * ObstacleSizeRatio(in obstacle);

                if (!_obstacleViews.TryGetValue(obstacle.Id, out var view))
                {
                    view = _obstaclePool.Acquire();
                    if (view == null) continue;
                    _obstacleViews.Add(obstacle.Id, view);
                    // 풀은 스케일을 되돌리지 않는다 — 성장 연출이 남긴 값이 다음
                    // 장애물에 새지 않게 획득 시점에 확실히 원복한다.
                    // 회전도 같다: 포탑이 쓰던 뷰를 잔해가 물려받으면 잔해가 기울어진다.
                    // **그림은 판정 크기를 따라간다.** 장애물이 자기 크기를 들고
                    // 있으면(Core의 per-obstacle half) 기본값 대비 비율만큼 키운다 —
                    // 판정만 커지고 그림이 그대로면 "안 맞았는데 맞는" 판정이 되고,
                    // 반대면 "맞았는데 안 맞는" 판정이 된다.
                    view.localScale = Vector3.one * baseScale;
                    view.localRotation = Quaternion.identity;
                    var renderer = view.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        var sprite = SpriteForObstacle(obstacle.Type);
                        if (sprite != null) renderer.sprite = sprite;
                        // 장애물은 예약 틱에 자기 좌표(화면 안 포함)에서 즉시 생겨난다 —
                        // 그대로 그리면 끊기듯 나타난다 ("장애물이 끊기듯 등장", 2026-07-31).
                        // 시뮬 판정은 스폰 즉시 유효하지만, 페이드는 짧아(0.35s) 판정과
                        // 표시의 어긋남이 문제되기 전에 끝난다.
                        // 재생은 알파가 아니라 스케일로 알린다 — 처음부터 불투명하게 자란다.
                        renderer.color = regenerating ? Color.white : new Color(1f, 1f, 1f, 0f);
                    }
                    _obstacleFadeAges[obstacle.Id] = regenerating ? ObstacleFadeSeconds : 0f;
                }
                view.localPosition = SimView.ToWorld(obstacle.X, obstacle.Y);
                // 포신은 빔이 나갈 방향을 가리켜야 한다. 방향은 Core가 준 레이저 선분에서만
                // 읽는다 — 뷰가 각도를 추정하면 판정과 어긋난다.
                if (emitter && _emitterAngles.TryGetValue(obstacle.Id, out float aim))
                    view.localRotation = Quaternion.Euler(0f, 0f, aim - EmitterBarrelBaseAngle);

                bool fading = _obstacleFadeAges.TryGetValue(obstacle.Id, out float age)
                    && age < ObstacleFadeSeconds;
                bool flashing = _obstacleHitFlashes.TryGetValue(obstacle.Id, out float flash)
                    && flash > 0f;
                // 포탑은 가열도가 0으로 돌아가는 프레임까지 색을 써야 하므로 항상 통과시킨다.
                if (fading || flashing || regenerating || emitter)
                {
                    var stateRenderer = view.GetComponent<SpriteRenderer>();
                    if (fading)
                    {
                        age += deltaTime;
                        _obstacleFadeAges[obstacle.Id] = age;
                    }
                    if (flashing)
                    {
                        flash -= deltaTime;
                        // 0에 닿는 프레임에 흰색으로 복원되고 키가 빠진다 — 잔틴트 방지
                        if (flash <= 0f) _obstacleHitFlashes.Remove(obstacle.Id);
                        else _obstacleHitFlashes[obstacle.Id] = flash;
                    }

                    float regenT = 1f;
                    if (regenerating)
                    {
                        regenAge += deltaTime;
                        regenT = Mathf.Clamp01(regenAge / ObstacleRegenSeconds);
                        // 마지막 프레임에 정확히 원 스케일/원 색으로 복귀시키고 키를 뺀다.
                        if (regenAge >= ObstacleRegenSeconds)
                        {
                            _obstacleRegenAges.Remove(obstacle.Id);
                            view.localScale = Vector3.one * baseScale;
                        }
                        else
                        {
                            _obstacleRegenAges[obstacle.Id] = regenAge;
                            // 감속 곡선(sin ease-out) — 처음에 훅 부풀고 마지막에 멎는다.
                            // 선형이면 "커진다"가 아니라 "늘어난다"로 읽힌다 (0.3 → 1.0).
                            float eased = Mathf.Sin(regenT * Mathf.PI * 0.5f);
                            float scale = Mathf.Lerp(ObstacleRegenStartScale, 1f, eased)
                                          * baseScale;
                            view.localScale = new Vector3(scale, scale, 1f);
                        }
                    }

                    if (stateRenderer != null)
                    {
                        // 피격 플래시(앰버)와 스폰 페이드(알파)는 독립 축 — 동시여도 겹친다
                        float flashT = flashing
                            ? Mathf.Clamp01(flash / ObstacleHitFlashSeconds) : 0f;
                        var c = Color.Lerp(Color.white, ObstacleHitFlashColor, flashT);
                        // 재생 틴트는 초록에서 흰색으로 빠진다 — 세포벽이 아무는 신호.
                        // 접근성(플래시 감소)에서는 채도만 낮춘다. 정보 자체는 남겨야 한다.
                        if (regenerating)
                        {
                            float tint = (1f - regenT)
                                * (_juice != null && _juice.FlashReduced ? 0.5f : 1f);
                            c = Color.Lerp(c, ObstacleRegenColor, tint);
                        }
                        // 포탑 가열: 예고 동안 달아오르고 발사 중에는 하얗게 탄다.
                        // 예고선이 눈에 안 들어와도 "저 포대가 지금 쏜다"가 읽혀야 한다.
                        if (emitter && _emitterHeat.TryGetValue(obstacle.Id, out float heat))
                        {
                            if (_juice != null && _juice.FlashReduced) heat *= 0.55f;
                            c = Color.Lerp(c, EmitterHeatColor, heat);
                        }
                        c.a = fading ? Mathf.Clamp01(age / ObstacleFadeSeconds) : 1f;
                        stateRenderer.color = c;
                    }
                }
            }

            _retiredIds.Clear();
            foreach (var pair in _obstacleViews)
                if (!_aliveIds.Contains(pair.Key))
                    _retiredIds.Add(pair.Key);
            for (int i = 0; i < _retiredIds.Count; i++)
            {
                int id = _retiredIds[i];
                var retired = _obstacleViews[id];
                // 성장 도중 파괴될 수 있다 — 반납 전에 원 스케일로 되돌린다.
                if (retired != null)
                {
                    retired.localScale = Vector3.one * ObstacleViewScale;
                    retired.localRotation = Quaternion.identity;
                }
                _obstaclePool.Release(retired);
                _obstacleViews.Remove(id);
                _obstacleFadeAges.Remove(id);
                _obstacleHitFlashes.Remove(id);
                _obstacleRegenAges.Remove(id);
                _emitterAngles.Remove(id);
            }
        }

        /// <summary>
        /// 이번 프레임의 포탑 조준 방향과 가열도를 Core의 레이저 상태에서 읽어 둔다.
        ///
        /// 지형 레이저(LaserSourceKind.Terrain)의 SourceEntityId가 곧 포탑 장애물 id다.
        /// 사이클 사이에는 레이저가 없어 방향을 알 수 없으므로 **각도는 마지막 관측값을
        /// 유지**하고(포신이 제자리로 튀지 않는다), 가열도는 매 프레임 새로 만든다.
        /// </summary>
        void TrackLaserEmitters()
        {
            _emitterHeat.Clear();
            var lasers = _sim?.Lasers;
            if (lasers == null) return;

            for (int i = 0; i < lasers.Count; i++)
            {
                var laser = lasers[i];
                if (laser.SourceKind != LaserSourceKind.Terrain) continue;

                float dx = laser.EndX - laser.StartX;
                float dy = laser.EndY - laser.StartY;
                if (dx * dx + dy * dy > 0f)
                    _emitterAngles[laser.SourceEntityId] =
                        Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                float heat;
                switch (laser.Phase)
                {
                    case LaserPhase.Telegraph:
                    {
                        // 발사가 가까울수록 뜨거워진다. 옅은 맥동은 "가동 중"의 신호다.
                        float toFire = laser.PhaseTicksRemaining
                            / (float)SimSpace.TicksPerSecond;
                        float charge = 1f - Mathf.Clamp01(toFire / EmitterChargeWindupSeconds);
                        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 14f);
                        heat = Mathf.Lerp(0.15f, 0.75f, charge) * pulse;
                        break;
                    }
                    case LaserPhase.Firing:
                    case LaserPhase.Sustaining:
                        heat = 1f;
                        break;
                    default:
                        heat = 0.4f;   // 소산 — 아직 달아 있지만 식는 중
                        break;
                }
                _emitterHeat[laser.SourceEntityId] = heat;
            }
        }

        Sprite SpriteForObstacle(ObstacleType type)
        {
            // 레이저 포탑은 테마와 무관하게 한 가지 실루엣이어야 한다 — 어느 스테이지에서도
            // "저건 쏘는 것"이 같은 모양으로 읽혀야 학습이 이어진다.
            if (type == ObstacleType.LaserEmitter && _obstacleEmitterSprite != null)
                return _obstacleEmitterSprite;
            // 폴백은 파괴 가능 계열이 아니라 단단한 계열이다 — 포탑은 부술 수 없다.
            bool solid = type != ObstacleType.Breakable;
            string themeId = CurrentThemeId;
            if (!string.IsNullOrEmpty(themeId) && _themeIds != null)
            {
                int count = Mathf.Min(_themeIds.Length,
                    Mathf.Min(_obstacleSolidSprites?.Length ?? 0, _obstacleBreakableSprites?.Length ?? 0));
                for (int i = 0; i < count; i++)
                    if (string.Equals(_themeIds[i], themeId, System.StringComparison.Ordinal))
                        return solid ? _obstacleSolidSprites[i] : _obstacleBreakableSprites[i];
            }
            return solid
                ? (_obstacleSolidSprites != null && _obstacleSolidSprites.Length > 0 ? _obstacleSolidSprites[0] : null)
                : (_obstacleBreakableSprites != null && _obstacleBreakableSprites.Length > 0 ? _obstacleBreakableSprites[0] : null);
        }

        Sprite SpriteForBulletKind(BulletKind kind)
        {
            // 미사일은 계열별 스프라이트 (REQ-034): 직진 추진체 / 투하 폭탄 / 관통 창
            if (kind == BulletKind.Missile)
            {
                var family = _run != null ? _run.CurrentMissileFamily : MissileFamily.Straight;
                var typed = SpriteForMissileFamily(family);
                if (typed != null) return typed;
                if (_missileSprite != null) return _missileSprite;
            }
            if (kind == BulletKind.EnemyShot && _enemyShotSprite != null) return _enemyShotSprite;
            // 고스트탄은 **지금 내 무기와 무관한** 고정 레벨 직진탄이다 (REQ-109).
            // _mainShotSprite는 게이지로 갈아탄 현재 무기를 따라가므로, 레이저 런에서
            // 고스트탄만 레이저 줄기로 그려져 "관통한다"는 거짓말이 된다. 기체 원본
            // 벌컨탄으로 되돌려 스프라이트가 위력을 과장하지 않게 한다.
            if (kind == BulletKind.GhostMainShot && _vulcanShotSprite != null)
                return _vulcanShotSprite;
            return _mainShotSprite;
        }

        /// <summary>
        /// 신규 보스 탄종(REQ-087)의 색 — 전용 스프라이트가 나오기 전까지 색·크기로
        /// 구분한다. 탄종 구분은 게임플레이 정보라 반드시 한눈에 갈라져야 한다.
        /// </summary>
        static Color ColorForBulletKind(BulletKind kind)
        {
            switch (kind)
            {
                case BulletKind.Heavy: return new Color(1f, 0.55f, 0.25f, 1f);     // 묵직한 주황
                case BulletKind.Splitter: return new Color(1f, 0.5f, 0.8f, 1f);    // 분열 예고 핑크
                case BulletKind.Mine: return new Color(0.55f, 0.85f, 1f, 1f);      // 정지 기뢰 청백
                // 고스트탄 (REQ-109): 본체와 같은 시안이되 알파는 1이다 — 실제로
                // 적을 깎는 탄이라 반투명하면 "안 맞는 탄"으로 오독된다.
                case BulletKind.GhostMainShot: return GhostView.BulletTint;
                default: return Color.white;
            }
        }

        static float ScaleForBulletKind(BulletKind kind)
        {
            switch (kind)
            {
                case BulletKind.Heavy: return 2.2f;
                case BulletKind.Splitter: return 1.35f;
                case BulletKind.Mine: return 1.6f;
                default: return 1f;
            }
        }

        Sprite SpriteForMissileFamily(MissileFamily family)
        {
            if (_missileFamilySprites == null) return null;
            int index = (int)family;
            return index >= 0 && index < _missileFamilySprites.Length
                ? _missileFamilySprites[index] : null;
        }

        void SyncBoss()
        {
            if (_bossRenderer == null) return;
            bool active = _sim.BossActive;

            // 전함전에서는 본체 스프라이트가 비켜난다. WarshipView가 함체 실루엣 +
            // 하드포인트로 배 전체를 조립하는데, 그 위에 본체 렌더러가 boss_fortress를
            // 한 장 더 얹으면 **같은 요새가 두 번** 선다 — 함미 하드포인트도 같은
            // 스프라이트라 화면에는 요새 2개 + 회색 판때기가 겹쳐 나온다. 사람 플레이
            // 스크린샷(2026-08-03, St2 fortress)에서 "대형 보스전을 못 봤다"고 한 것이
            // 이것이다. 배가 배로 안 읽혔다.
            // BossPartsView가 같은 이유로 비켜나는 것(_warshipView.Active)과 한 쌍이다.
            _bossVisualSuppressed =
                active && (WarshipOwnsBossVisual || HiveOwnsBossVisual);
            bool visible = active && !_bossVisualSuppressed;
            if (_bossRenderer.enabled != visible)
                _bossRenderer.enabled = visible;
            if (_bossHpRoot != null && _bossHpRoot.activeSelf != active)
                _bossHpRoot.SetActive(active);
            if (!active) return;
            // 아래 위치·틴트 갱신은 숨겨진 동안에도 계속 돈다 — 격파 연출이 본체
            // 렌더러의 위치를 폭발 중심으로 쓰기 때문이다(TriggerBossDeathSequence).

            _bossRenderer.transform.localPosition =
                SimView.ToWorld(_sim.Boss.X, _sim.Boss.Y)
                + new Vector3(0f, BossIdleOffsetY, 0f);
            // **지금 형태의 id로 돌린다.** 예전에는 늘 StagePlan.BossId(= 첫 형태)를
            // 넘겼는데, 이 함수는 매 프레임 renderer.sprite를 애니 프레임으로
            // 덮어쓴다. 그래서 두 번째 형태로 넘어가며 ApplyBossSprite가 작은 코어
            // 그림으로 바꿔 놓아도, 바로 다음 프레임에 **첫 형태 애니메이션이 다시
            // 덮어써서** 원본 보스가 그대로 서 있었다 (사람 보고 2026-08-05:
            // "첫 파괴 이후 코어 보스가 나와야하는데 원본 보스 그대로야").
            //
            // 두 번째 형태에 애니 프레임이 없으면 GetAnimRange가 0을 돌려주고
            // 그냥 빠져나가므로, ApplyBossSprite가 걸어 둔 정지 그림이 살아남는다.
            if (_run != null && _run.StagePlan != null)
                ApplyIdleAnimation(
                    _bossRenderer, _bossFormId ?? _run.StagePlan.BossId, 0,
                    exact: true);
            ApplyIdleBreath(_bossRenderer);

            // 보스 피격 플래시 + 빈사 맥동 (HP 바 외에 시각 피드백이 없던 문제)
            int bossHp = _sim.Boss.Hp;
            if (_lastBossHp >= 0 && bossHp < _lastBossHp)
                _bossFlashAge = 0f;
            _lastBossHp = bossHp;

            var bossColor = Color.white;
            if (_bossFlashAge < BossFlashDuration)
            {
                _bossFlashAge += Time.deltaTime;
                float t = Mathf.Clamp01(_bossFlashAge / BossFlashDuration);
                bossColor = Color.Lerp(new Color(1f, 0.55f, 0.55f), Color.white, t);
            }
            else if (_sim.Boss.MaxHp > 0 && bossHp * 4 <= _sim.Boss.MaxHp)
            {
                // 빈사(25% 이하): 붉은 맥동 — 접근성 플래시 감소 시 완화
                float amplitude = _juice != null && _juice.FlashReduced ? 0.12f : 0.3f;
                float pulse = (Mathf.Sin(Time.time * 7f) + 1f) * 0.5f * amplitude;
                bossColor = new Color(1f, 1f - pulse, 1f - pulse);
            }
            // 페이즈 전환 발광은 피격 플래시보다 우선한다 — 전환은 드물고 중요한 사건이라
            // 피격에 묻히면 안 된다. 흰빛으로 크게 번쩍이며 감쇠한다.
            if (_bossPhaseFlash > 0f)
            {
                _bossPhaseFlash -= Time.deltaTime;
                float glow = Mathf.Clamp01(_bossPhaseFlash) * _bossPhaseFlashPeak;
                if (_juice != null && _juice.FlashReduced) glow *= 0.4f;
                bossColor = Color.Lerp(bossColor, Color.white, glow);
                // 발광 중에는 살짝 부풀려 존재감을 준다.
                float swell = 1f + glow * 0.12f;
                _bossRenderer.transform.localScale = new Vector3(swell, swell, 1f);
            }
            else if (_bossRenderer.transform.localScale != Vector3.one)
            {
                _bossRenderer.transform.localScale = Vector3.one;
            }

            if (_bossRenderer.color != bossColor)
                _bossRenderer.color = bossColor;

            if (_bossHpFill != null && _sim.Boss.MaxHp > 0)
            {
                float fraction = Mathf.Clamp01((float)_sim.Boss.Hp / _sim.Boss.MaxHp);
                var scale = _bossHpFill.localScale;
                scale.x = BossHpBarWidthUnits / WhiteSpriteUnits * fraction;
                _bossHpFill.localScale = scale;
                var position = _bossHpFill.localPosition;
                position.x = -BossHpBarWidthUnits * (1f - fraction) / 2f;
                _bossHpFill.localPosition = position;
            }
        }

        // ── 보상 선택 (RunManager AwaitingReward — RewardScreen이 소비) ─────────

        public bool AwaitingReward => _run != null && _run.State == RunState.AwaitingReward;

        // ── 섹터 계약 (REQ-070 — ContractScreen이 소비) ─────────────────────────

        public bool AwaitingContract =>
            _run != null && _run.State == RunState.AwaitingContract;

        // REQ-086부터 후보는 목적지 바이옴이 결합된 ContractOption이다.
        public System.Collections.Generic.IReadOnlyList<ContractOption> ContractOptions
            => _run?.ContractOptions;

        /// <summary>현재 스테이지에 적용 중인 계약. 스테이지 1과 런 종료 후에는 null.</summary>
        public ContractDefinition ActiveContract => _run?.ActiveContract;

        public void ChooseContract(int index)
        {
            if (!AwaitingContract) return;
            if (_replayMode) return;   // 리플레이 중 수동 선택 금지 (자동 재현)
            if (!_run.ChooseContract(index)) return;   // 잘못된 인덱스는 Core가 안전 거부
            // 기록은 성공 후에만 — 거부된 선택이 기록되면 리플레이가 어긋난다.
            if (_recordingActive) _recordedContractChoices.Add(index);
            RefreshBattle();
            SyncViews();
        }

        /// <summary>
        /// 지금 고르는 보상이 중간보스 직후의 짧은 2택인지, 스테이지 보스 후의 주 3택인지.
        /// 화면 제목과 배치를 나누는 데 쓴다 (REQ-054).
        /// </summary>
        public RewardSelectionKind RewardKind =>
            _run != null ? _run.RewardSelectionKind : RewardSelectionKind.None;

        /// <summary>스테이지 내부 진행 구간 — 전반/중간보스/후반/보스 연출을 가른다.</summary>
        public RunStageSection StageSection =>
            _run != null ? _run.StageSection : RunStageSection.Opening;

        bool _midBossDefeatSignaled;

        /// <summary>
        /// 중간보스를 격파했지만 Core의 구간 상태는 아직 MidBoss인 동안 true
        /// (격파 → 보상 선택 → AdvanceRoom 사이). SectionThemeDirector가 이 신호로
        /// Late 룩 전환을 **격파 프레임에** 시작한다 — 보상 화면을 기다리지 않는다.
        /// 구간이 실제로 넘어가면 저절로 false가 되므로 소비자가 리셋할 필요가 없다.
        /// </summary>
        public bool MidBossDefeatSignaled =>
            _midBossDefeatSignaled && StageSection == RunStageSection.MidBoss;

        /// <summary>지속 레이저 상태 (REQ-042) — LaserBeamView가 선분으로 그린다.</summary>
        public IReadOnlyList<LaserState> Lasers => _sim?.Lasers;

        // ── 스테이지 기믹 관측값 (REQ-055) — StageGimmickView가 그린다 ─────────
        public StageEnvironmentState Environment =>
            _sim != null ? _sim.Environment : default;

        /// <summary>네뷸라 시야 제한. 표현 전용 플래그로, 판정에는 영향이 없다.</summary>
        public bool VisionObscured => _sim != null && _sim.VisionObscured;

        /// <summary>코어 스테이지 제한 시간. 0이면 제한이 없다. 초과는 즉사다.</summary>
        public int RemainingTimeTicks => _sim != null ? _sim.RemainingTimeTicks : 0;
        public System.Collections.Generic.IReadOnlyList<RewardOption> RewardOptions
            => _run?.RewardOptions;

        public void ChooseReward(int index)
        {
            if (!AwaitingReward) return;
            if (_replayMode) return;   // 리플레이 중 수동 선택 금지 (자동 재현)
            if (_recordingActive) _recordedChoices.Add(index);
            _run.ChooseReward(index);
            RefreshBattle();
            SyncViews();
        }

        // ── 보상 리롤 (REQ-072 — 캡슐 화폐) ─────────────────────────────────────

        public int CapsuleBalance => _run?.CapsuleBalance ?? 0;
        public int RewardRerollCost => _run?.RewardRerollCost ?? 0;
        public bool CanRerollRewards => _run != null && _run.CanRerollRewardOptions;

        /// <summary>리롤 성공 여부. 리플레이 기록에는 선택 -1이 리롤을 뜻한다.</summary>
        public bool RerollRewards()
        {
            if (!AwaitingReward || _replayMode) return false;
            if (!_run.RerollRewardOptions()) return false;
            if (_recordingActive) _recordedChoices.Add(RerollChoiceSentinel);
            return true;
        }

        /// <summary>
        /// 리플레이 선택 기록에서 리롤을 뜻하는 센티널. 실제 카드 인덱스는 0 이상이므로
        /// 충돌하지 않고, 구버전 리플레이는 스키마 버전(v13)에서 이미 거부된다.
        /// </summary>
        public const int RerollChoiceSentinel = -1;

        /// <summary>
        /// 개발용 빨리감기 (DevCheats F11): 무입력 틱을 일괄 진행한다. 이벤트 FX/SFX는
        /// 스킵 구간에서 생략한다 — 순수 dev 편의, 게임플레이 판정은 전부 Core 그대로.
        /// </summary>
        public void DevFastForward(int ticks)
        {
            if (_run == null) return;
            var none = InputCommand.None;
            for (int i = 0; i < ticks && _run.State == RunState.Playing; i++)
                _run.Step(in none);
            RefreshBattle();
            SyncViews();
        }

        /// <summary>
        /// 워프가 한 번에 진행할 수 있는 최대 틱 (60Hz 기준 10분). 목표 구간에 영영
        /// 닿지 않는 조합(무입력이라 격파가 안 되는 구간 등)에서 에디터가 멈추지 않게
        /// 하는 상한이다. 도달 실패는 예외가 아니라 경고 — dev 편의 기능이 런을
        /// 깨뜨리면 안 된다.
        /// </summary>
        const int DevWarpMaxTicks = 36000;

        /// <summary>
        /// 개발용 구간 워프 (REQ-124, `?warp=boss`): 목표 <see cref="RunStageSection"/>에
        /// 닿을 때까지 무입력 틱을 돌린다. F11을 수십 번 누르던 것을 한 번에 끝낸다 —
        /// 보스룸 도달에만 매 검증 3~5분이 들던 것이 이 기능의 존재 이유다.
        ///
        /// **게임플레이 판정은 하나도 하지 않는다.** DevFastForward와 똑같이 Core를
        /// 무입력으로 돌릴 뿐이고, 언제 구간이 넘어가는지는 전적으로 Core가 정한다.
        /// (Presentation에서 판정하지 말라는 원칙 — CLAUDE.md — 을 지키는 형태다.)
        ///
        /// 입력은 **발사 홀드**만 준다. 무입력으로 돌리면 중간보스처럼 격파가 있어야
        /// 넘어가는 게이트를 영영 못 지나 상한에서 멈춘다(첫 구현이 실제로 그랬다).
        /// 이동은 주지 않는다 — 세로로 움직이는 순간 "어디서 쐈나"가 결과를 바꿔
        /// 워프가 재현되지 않는다. 발사만으로 안 열리는 게이트(정확한 위치가 필요한
        /// hive 촉수 등)는 여전히 상한에서 멈추고, 그 조합은 F11 수동 진행이 맞다.
        /// </summary>
        /// <returns>목표 구간에 도달했으면 true.</returns>
        public bool DevWarpToSection(RunStageSection target)
        {
            if (_run == null) return false;
            var fire = new InputCommand(0, 0, true);
            int ticks = 0;
            while (_run.StageSection != target && ticks < DevWarpMaxTicks)
            {
                // 중간보스를 잡으면 런이 보상 선택에서 멈춘다 — 워프가 거기서 끝나면
                // 목표가 보스룸일 때 영영 도착하지 못한다. 첫 카드를 집어 흐름을 잇는다
                // (무엇을 고르는지는 워프의 관심사가 아니다 — 도달이 목적이다).
                if (_run.State == RunState.AwaitingReward) { _run.ChooseReward(0); continue; }
                if (_run.State != RunState.Playing) break;   // 항로 선택·런 종료는 워프 밖
                _run.Step(in fire);
                ticks++;
            }
            RefreshBattle();
            SyncViews();
            bool reached = _run.StageSection == target;
            if (!reached)
                Debug.LogWarning(
                    $"[dev] warp={target} 미도달 ({ticks}틱 소진, 현재 {_run.StageSection}). "
                    + "무입력으로는 넘어가지 않는 구간이다 — F11로 직접 진행해라.");
            return reached;
        }

        /// <summary>DevCheats 오버레이용.</summary>
        public int ShieldRemaining => _sim?.ShieldRemaining ?? 0;

        /// <summary>보유한 전멸 폭탄 수 — HUD와 폭탄 버튼의 활성 여부를 정한다.</summary>
        public int BombStock => _sim?.BombStock ?? 0;

        /// <summary>선택적 GameData — 없으면 null (Core가 폴백 처리).</summary>
        static string TryLoadGameDataText(string name)
        {
            var asset = Resources.Load<TextAsset>("GameData/" + name);
            return asset != null ? asset.text : null;
        }

        static string LoadGameDataText(string name)
        {
            var asset = Resources.Load<TextAsset>("GameData/" + name);
            if (asset == null)
                throw new System.InvalidOperationException(
                    $"Resources/GameData/{name} 를 찾을 수 없다. Tools → Shmup → " +
                    "Sync GameData To Resources 로 사본을 다시 맞춰라.");
            return asset.text;
        }

        /// <summary>
        /// 고스트 본체 (REQ-109). 활성 여부·좌표 전부 Core 관측값이다 — 뷰는
        /// 페이드와 잔상 타이밍만 자기 몫으로 가진다. 런이 재시작되면 Ghost.Active가
        /// 저절로 false가 되므로 여기서 별도 정리를 하지 않는다.
        /// </summary>
        void SyncGhost()
        {
            if (_ghostView == null) return;
            if (_run == null) { _ghostView.Hide(); return; }
            var ghost = _run.Ghost;
            _ghostView.Sync(ghost.Active, ghost.X, ghost.Y);
        }

        void SyncBullets()
        {
            var bullets = _sim.Bullets;
            _aliveIds.Clear();

            for (int i = 0; i < bullets.Count; i++)
            {
                var bullet = bullets[i];
                _aliveIds.Add(bullet.Id);

                if (!_bulletViews.TryGetValue(bullet.Id, out var view))
                {
                    view = _bulletPool.Acquire();
                    if (view == null) continue;   // 풀 소진 — 경고는 풀이 이미 냈다
                    _bulletViews.Add(bullet.Id, view);

                    // Kind는 Id 수명 동안 불변 — 획득 시 한 번만 외형을 고른다.
                    // 풀 재사용 대비: 색·스케일도 매 획득마다 반드시 재설정한다.
                    var renderer = view.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        renderer.sprite = SpriteForBulletKind(bullet.Kind);
                        renderer.color = ColorForBulletKind(bullet.Kind);
                    }
                    view.localScale = Vector3.one * ScaleForBulletKind(bullet.Kind);
                }

                var world = SimView.ToWorld(bullet.X, bullet.Y);
                if (bullet.Kind == BulletKind.MainShot
                    && _shownWeaponType == Shmup.Core.WeaponType.Laser)
                    world.x += LaserBoltMuzzleLead;
                view.localPosition = world;
            }

            ReleaseDeadViews(_bulletViews, _bulletPool);
        }

        void SyncOptions()
        {
            var options = _sim.Options;
            _aliveIds.Clear();

            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                _aliveIds.Add(option.Index);

                if (!_optionViews.TryGetValue(option.Index, out var view))
                {
                    view = _optionPool.Acquire();
                    if (view == null) continue;
                    _optionViews.Add(option.Index, view);
                    // 옵션 8×8은 너무 작아 눈에 안 띈다 ("크기를 두배정도", 2026-07-31).
                    // 순수 표현 스케일 — 옵션은 피격 판정이 없어 크기가 커져도 안전하다.
                    view.localScale = Vector3.one * 2f;
                }

                view.localPosition = SimView.ToWorld(option.X, option.Y);
            }

            ReleaseDeadViews(_optionViews, _optionPool);
        }

        /// <summary>
        /// 스톡 단계별 실드 색. 쌓일수록 차가운 청색에서 뜨거운 쪽으로 옮겨가서,
        /// 숫자를 읽지 않아도 남은 양이 보인다.
        /// </summary>
        static readonly Color[] ShieldTierColors =
        {
            new Color(0.35f, 0.70f, 1.00f, 0.55f),   // 1: 청색
            new Color(0.35f, 1.00f, 0.85f, 0.62f),   // 2: 청록
            new Color(0.70f, 1.00f, 0.55f, 0.70f),   // 3: 연녹
            new Color(1.00f, 0.90f, 0.45f, 0.78f),   // 4: 금색
            new Color(1.00f, 0.55f, 0.95f, 0.86f)    // 5+: 자홍 (현재 상한)
        };

        void SyncShield()
        {
            if (_shieldView == null) return;
            int remaining = _sim.ShieldRemaining;
            _shieldView.enabled = remaining > 0;
            if (remaining <= 0) return;

            // 실드는 기체 모양이어야 와닿는다 — 원형 링은 가로로 긴 함선과 겹치지 않아
            // "내가 감싸여 있다"는 느낌을 주지 못했다. 기체 스프라이트를 그대로 빌려
            // 기체 뒤에 조금 크게 깔면 외곽이 테두리처럼 보인다. 스프라이트를 매 프레임
            // 따라가므로 함선 교체와 애니메이션 프레임에 자동으로 맞는다.
            var ship = _playerTransform != null
                ? _playerTransform.GetComponent<SpriteRenderer>() : null;
            if (ship != null)
            {
                if (!ReferenceEquals(_shieldView.sprite, ship.sprite))
                    _shieldView.sprite = ship.sprite;
                _shieldView.sortingOrder = ship.sortingOrder - 1;
                _shieldView.flipX = ship.flipX;
            }

            int tier = Mathf.Clamp(remaining, 1, ShieldTierColors.Length);
            // 스톡이 쌓이면 테두리가 두꺼워진다 (기체와의 크기 차이가 곧 두께다).
            // 상한(5)에서도 기체 실루엣을 알아볼 수 있는 범위로 계수를 잡았다.
            float thickness = 1f + 0.08f * tier;
            _shieldView.transform.localScale = new Vector3(thickness, thickness, 1f);
            _shieldView.color = ShieldTierColors[tier - 1];
        }

        void SyncEnemies()
        {
            var enemies = _sim.Enemies;
            _aliveIds.Clear();

            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                _aliveIds.Add(enemy.Id);

                if (!_enemyViews.TryGetValue(enemy.Id, out var view))
                {
                    view = _enemyPool.Acquire();
                    if (view == null) continue;
                    _enemyViews.Add(enemy.Id, view);

                    var renderer = view.GetComponent<SpriteRenderer>();
                    _enemyRenderers[enemy.Id] = renderer;
                    _enemyDeathTints[enemy.Id] = DeathTintFor(enemy.DefinitionId);
                    // 화면 밖에서 스폰된 적은 등장 예고 마커를 띄운다
                    if (_spawnTelegraph != null
                        && enemy.X > SimSpace.PlayfieldHalfWidthSubUnits)
                        _spawnTelegraph.Warn(SimView.ToWorld(enemy.X, enemy.Y).y);
                    if (renderer != null)
                    {
                        Sprite typed = SpriteForEnemy(enemy.DefinitionId);
                        if (typed != null)
                        {
                            renderer.sprite = typed;
                            renderer.color = Color.white;
                        }
                        else
                        {
                            renderer.color = TintFor(enemy.DefinitionId);
                        }
                        ApplyEnemyScale(view, renderer, enemy.DefinitionId);
                    }
                }

                view.localPosition = SimView.ToWorld(enemy.X, enemy.Y);
                if (_enemyRenderers.TryGetValue(enemy.Id, out var animRenderer))
                    ApplyIdleAnimation(animRenderer, enemy.DefinitionId, enemy.Id);
            }

            ReleaseDeadEnemies();
        }

        /// <summary>
        /// 적 뷰 반납. 폭발은 이제 Core의 EnemyKilled 이벤트가 정본이다 (REQ-005) —
        /// 화면 밖 despawn과 처치를 위치로 추측하던 휴리스틱은 제거했다.
        /// </summary>
        void ReleaseDeadEnemies()
        {
            _retiredIds.Clear();
            foreach (var pair in _enemyViews)
                if (!_aliveIds.Contains(pair.Key))
                    _retiredIds.Add(pair.Key);

            for (int i = 0; i < _retiredIds.Count; i++)
            {
                int id = _retiredIds[i];
                _enemyPool.Release(_enemyViews[id]);
                _enemyViews.Remove(id);
                _enemyRenderers.Remove(id);
                _enemyDeathTints.Remove(id);
            }
        }

        bool HasExplosionFrames => _explosionFrames != null && _explosionFrames.Length > 0;

        /// <summary>프레임 애니메이션 기준 30fps — 9프레임이면 0.3초.</summary>
        float ExplosionLifetime => HasExplosionFrames
            ? _explosionFrames.Length / 30f
            : ExplosionDuration;

        // 테마 계열별 폭발 틴트 — 기계는 흰색(원색), 유기체는 녹황, 스크랩은 주황, 에너지는 청보라
        static Color DeathTintFor(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId)) return Color.white;
            if (definitionId.StartsWith("spore", System.StringComparison.Ordinal)
                || definitionId.StartsWith("brood_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("sting_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("lancer", System.StringComparison.Ordinal)
                || definitionId.StartsWith("mini_horror", System.StringComparison.Ordinal))
                return new Color(0.75f, 1f, 0.6f);
            if (definitionId.StartsWith("scrap_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("rust_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("junk_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("pipe_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("zako_tank", System.StringComparison.Ordinal))
                return new Color(1f, 0.8f, 0.55f);
            if (definitionId.StartsWith("wisp", System.StringComparison.Ordinal)
                || definitionId.StartsWith("echo_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("void_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("shard_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("phase_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("rift_", System.StringComparison.Ordinal)
                || definitionId.StartsWith("mini_crystal", System.StringComparison.Ordinal))
                return new Color(0.72f, 0.85f, 1f);
            return Color.white;
        }

        void SpawnExplosion(Vector3 position, float scale = 1f)
        {
            SpawnExplosion(position, scale, Color.white);
        }

        void SpawnExplosion(Vector3 position, float scale, Color tint)
        {
            var fx = _fxPool.Acquire();
            if (fx == null) return;
            fx.localPosition = position;
            var renderer = fx.GetComponent<SpriteRenderer>();
            if (HasExplosionFrames)
            {
                fx.localScale = Vector3.one * scale;
                if (renderer != null)
                {
                    renderer.sprite = _explosionFrames[0];
                    renderer.color = tint;
                }
            }
            else
            {
                fx.localScale = Vector3.one * (0.6f * scale);
            }
            _activeFx.Add(fx);
            _activeFxRenderers.Add(renderer);
            _activeFxAges.Add(0f);
        }

        /// <summary>적 피격 펀치: 짧은 스케일 팝 + 붉은 틴트. 원 스케일은 시작 시점 값을 복원한다.</summary>
        void PunchEnemy(int enemyId)
        {
            if (!_enemyViews.TryGetValue(enemyId, out var view) || view == null) return;

            // 이미 펀치 중이면 age만 리셋 (베이스 스케일 중복 캡처 방지)
            for (int i = 0; i < _punchTargets.Count; i++)
                if (ReferenceEquals(_punchTargets[i], view))
                {
                    _punchAges[i] = 0f;
                    return;
                }

            _enemyRenderers.TryGetValue(enemyId, out var renderer);
            _punchTargets.Add(view);
            _punchRenderers.Add(renderer);
            _punchBaseScales.Add(view.localScale);
            _punchBaseColors.Add(renderer != null ? renderer.color : Color.white);
            _punchAges.Add(0f);
        }

        void AnimatePunches()
        {
            bool tint = _juice == null || !_juice.FlashReduced;
            for (int i = _punchTargets.Count - 1; i >= 0; i--)
            {
                var view = _punchTargets[i];
                float age = _punchAges[i] + Time.deltaTime;
                bool alive = view != null && view.gameObject.activeSelf;
                if (age >= PunchDuration || !alive)
                {
                    if (alive)
                    {
                        view.localScale = _punchBaseScales[i];
                        if (_punchRenderers[i] != null) _punchRenderers[i].color = _punchBaseColors[i];
                    }
                    _punchTargets.RemoveAt(i);
                    _punchRenderers.RemoveAt(i);
                    _punchBaseScales.RemoveAt(i);
                    _punchBaseColors.RemoveAt(i);
                    _punchAges.RemoveAt(i);
                    continue;
                }
                _punchAges[i] = age;
                float t = age / PunchDuration;
                float scale = 1f + 0.14f * Mathf.Sin(t * Mathf.PI);   // 팝 후 복원
                view.localScale = _punchBaseScales[i] * scale;
                if (tint && _punchRenderers[i] != null)
                    _punchRenderers[i].color = Color.Lerp(PunchTint, _punchBaseColors[i], t);
            }
        }

        void AnimateMuzzleFlash()
        {
            if (_muzzleFlash == null) return;
            if (_muzzleAge >= MuzzleDuration)
            {
                if (_muzzleFlash.enabled) _muzzleFlash.enabled = false;
                return;
            }
            _muzzleAge += Time.deltaTime;
            _muzzleFlash.enabled = true;
        }

        /// <summary>
        /// 보스 격파 시퀀스: 보스 위치 주변 다단 폭발 + 슬로모 + 흔들림.
        /// StageCleared는 보스 격파 직후 발생 — 보스 뷰가 켜져 있을 때만 연출한다.
        /// </summary>
        /// <summary>
        /// 격파 연출이 끝날 때까지 남은 시간(초). 0보다 크면 보상 카드를 띄우지 않는다.
        ///
        /// 사람 지적 2026-08-03: "폭파하자마자 카드가 떠서 클리어 감흥이 너무 없어."
        /// 예전 연출은 총 0.72초였고 그 위로 곧장 카드가 덮였다 — 이겼다는 사실을
        /// 화면이 축하해 줄 시간이 없었다.
        /// </summary>
        public float BossDeathCinematicRemaining { get; private set; }

        public bool BossDeathCinematicActive => BossDeathCinematicRemaining > 0f;

        void TriggerBossDeathSequence()
        {
            // 전함전은 본체 렌더러를 숨기지만(SyncBoss) 격파 연출은 나와야 한다 —
            // 숨김 상태와 "보스가 아예 없음"을 갈라서 본다.
            if (_bossRenderer == null) return;
            if (!_bossRenderer.enabled && !_bossVisualSuppressed) return;
            var center = _bossRenderer.transform.localPosition;

            // 격파의 무게를 등급으로 가른다 (사람 지시): 중간보스는 작게, 스테이지
            // 보스와 히든 왕보스는 크게. 같은 폭발을 쓰면 5스테이지 완주도 잡졸
            // 처치와 같은 크기로 끝나 버린다.
            var section = StageSection;
            bool midBoss = section == RunStageSection.MidBoss;
            bool colossal = section == RunStageSection.HiddenBoss;

            int boomCount = midBoss ? 8 : colossal ? 20 : 16;
            float interval = midBoss ? 0.10f : 0.13f;
            float spread = midBoss ? 0.9f : colossal ? 2.2f : 1.7f;
            float scale = midBoss ? 1.1f : colossal ? 2.0f : 1.6f;

            if (_juice != null)
            {
                _juice.Shake(midBoss ? 0.5f : colossal ? 1.0f : 0.8f);
                _juice.Slowmo(midBoss ? 0.35f : 0.5f, midBoss ? 0.7f : 0.55f);
            }

            SpawnExplosion(center, scale);
            for (int i = 0; i < boomCount; i++)
            {
                // 황금비 각도로 돌려 같은 자리에 겹치지 않게 흩는다 — 규칙적인
                // 원형 배치는 폭발이 아니라 도형으로 읽힌다.
                float angle = i * 2.39996f;
                float radius = spread * (0.35f + 0.65f * i / Mathf.Max(1, boomCount - 1));
                _pendingBoomPositions.Add(center + new Vector3(
                    Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.8f, 0f));
                _pendingBoomDelays.Add(interval * (i + 1));
                _pendingBoomScales.Add(1f);
            }

            // 마지막 폭발 뒤 여운까지 카드를 막는다.
            BossDeathCinematicRemaining = interval * boomCount + (midBoss ? 0.35f : 0.7f);
        }

        /// <summary>
        /// 함체 붕괴 (폼 전환). 보스 격파 연출과 달리 **가로로 길게** 훑는다 —
        /// 전함은 길이가 정체성이라, 원형으로 터지면 배로 안 읽힌다.
        /// </summary>
        /// <summary>
        /// 파츠 파괴 연출. **크기에 비례한다.**
        ///
        /// 예전에는 어떤 파츠든 1.5배 폭발 하나였다. 작은 포탑에는 맞았지만 화면
        /// 절반을 차지하는 거대 보스 파츠가 부서져도 가운데에 점 하나가 터질
        /// 뿐이라, 사람이 "부위가 파괴되는 연출이 없음"이라고 보고했다
        /// (2026-08-04). 실제로는 있었는데 파츠에 비해 너무 작았던 것이다.
        ///
        /// 파츠 판정 크기를 읽어 폭발 크기를 키우고, 넓은 파츠는 그 면적에
        /// 흩뿌려 연달아 터뜨린다 — 큰 것이 무너지는 데는 시간이 걸려야 한다.
        /// </summary>
        void SpawnPartDestruction(string partId, Vector3 center)
        {
            float halfW = 0.6f, halfH = 0.6f;
            var definitions = BossPartDefinitions;
            if (definitions != null && !string.IsNullOrEmpty(partId))
                for (int i = 0; i < definitions.Count; i++)
                    if (string.Equals(
                            definitions[i].PartId,
                            partId,
                            System.StringComparison.Ordinal))
                    {
                        halfW = definitions[i].HalfWidth * SimView.WorldUnitsPerSubUnit;
                        halfH = definitions[i].HalfHeight * SimView.WorldUnitsPerSubUnit;
                        break;
                    }

            float span = Mathf.Max(halfW, halfH);
            // 1.5는 예전 고정값 — 작은 파츠에서 지금과 같게 보이도록 바닥으로 둔다.
            float scale = Mathf.Clamp(span * 1.6f, 1.5f, 5.0f);
            SpawnExplosion(center, scale);

            // 넓은 파츠일수록 여러 번. 판정 안쪽에만 뿌려 파츠 밖에서 터지지 않게 한다.
            int extra = Mathf.Clamp(Mathf.RoundToInt(span * 2.2f) - 1, 0, 7);
            for (int i = 0; i < extra; i++)
            {
                // 황금각 — 적은 개수로도 뭉치지 않고 고르게 흩어진다.
                float angle = i * 2.39996f;
                float radius = Mathf.Sqrt((i + 1f) / (extra + 1f));
                _pendingBoomPositions.Add(center + new Vector3(
                    Mathf.Cos(angle) * halfW * 0.7f * radius,
                    Mathf.Sin(angle) * halfH * 0.7f * radius,
                    0f));
                _pendingBoomDelays.Add(0.07f * (i + 1));
                _pendingBoomScales.Add(scale * 0.6f);
            }

            if (_juice != null)
            {
                _juice.Shake(Mathf.Clamp(0.25f + span * 0.12f, 0.25f, 0.8f));
                _juice.Hitstop(Mathf.Clamp(0.05f + span * 0.02f, 0.05f, 0.16f));
            }
        }

        void SpawnHullCollapse(Vector3 center)
        {
            const int Booms = 22;
            const float Interval = 0.11f;
            float halfLength = _run != null && _run.StagePlan != null
                ? _run.StagePlan.BossHalfWidth * SimView.WorldUnitsPerSubUnit
                : 8f;

            if (_juice != null)
            {
                _juice.Shake(1.0f);
                _juice.Slowmo(0.5f, 0.5f);
            }

            SpawnExplosion(center, 1.8f);
            for (int i = 0; i < Booms; i++)
            {
                // 함미(오른쪽)에서 함수(왼쪽)로 무너져 간다.
                float t = i / (float)(Booms - 1);
                float x = Mathf.Lerp(halfLength, -halfLength, t);
                float y = Mathf.Sin(i * 2.39996f) * halfLength * 0.16f;
                _pendingBoomPositions.Add(center + new Vector3(x, y, 0f));
                _pendingBoomDelays.Add(Interval * (i + 1));
                _pendingBoomScales.Add(1.4f);
            }
            BossDeathCinematicRemaining = Interval * Booms + 0.5f;
        }

        void TickBossDeathCinematic()
        {
            if (BossDeathCinematicRemaining <= 0f) return;
            BossDeathCinematicRemaining =
                Mathf.Max(0f, BossDeathCinematicRemaining - Time.deltaTime);
        }

        void TickPendingBooms()
        {
            for (int i = _pendingBoomPositions.Count - 1; i >= 0; i--)
            {
                float delay = _pendingBoomDelays[i] - Time.deltaTime;
                if (delay <= 0f)
                {
                    SpawnExplosion(_pendingBoomPositions[i], _pendingBoomScales[i]);
                    _pendingBoomPositions.RemoveAt(i);
                    _pendingBoomDelays.RemoveAt(i);
                    _pendingBoomScales.RemoveAt(i);
                }
                else
                {
                    _pendingBoomDelays[i] = delay;
                }
            }
        }

        void AnimateExplosions()
        {
            float lifetime = ExplosionLifetime;
            for (int i = _activeFx.Count - 1; i >= 0; i--)
            {
                float age = _activeFxAges[i] + Time.deltaTime;
                if (age >= lifetime)
                {
                    _fxPool.Release(_activeFx[i]);
                    _activeFx.RemoveAt(i);
                    _activeFxRenderers.RemoveAt(i);
                    _activeFxAges.RemoveAt(i);
                    continue;
                }

                _activeFxAges[i] = age;
                float t = age / lifetime;
                var renderer = _activeFxRenderers[i];
                if (HasExplosionFrames)
                {
                    if (renderer != null)
                    {
                        int frame = Mathf.Min(
                            (int)(t * _explosionFrames.Length), _explosionFrames.Length - 1);
                        renderer.sprite = _explosionFrames[frame];
                    }
                    continue;
                }

                _activeFx[i].localScale = Vector3.one * Mathf.Lerp(0.6f, 1.8f, t);
                if (renderer != null)
                {
                    var c = renderer.color;
                    c.a = 1f - t;
                    renderer.color = c;
                }
            }
        }

        void AnimateDamageFlash()
        {
            int hp = PlayerHp;
            if (_lastHp >= 0 && hp < _lastHp)
                _damageFlashAge = 0f;
            _lastHp = hp;

            if (_damageFlash == null) return;

            bool flashReduced = _juice != null && _juice.FlashReduced;

            // 폭탄이 피격보다 우선한다 — 화면을 지우는 사건이 더 크고, 폭탄 직후 피격이
            // 겹칠 때(무적 만료 직전) 약한 쪽이 이기면 연출이 뒤바뀐다.
            if (_bombFlashAge < BombFlashDuration)
            {
                _bombFlashAge += Time.deltaTime;
                float t = Mathf.Clamp01(1f - _bombFlashAge / BombFlashDuration);
                // 폭탄 아이콘과 같은 자홍 계열 — 무엇이 터졌는지 색으로 연결된다.
                _damageFlash.color = new Color(1f, 0.55f, 1f, t * (flashReduced ? 0.3f : 0.7f));
                return;
            }

            if (_damageFlashAge >= DamageFlashDuration) return;

            _damageFlashAge += Time.deltaTime;
            float intensity = flashReduced ? 0.15f : 0.35f;
            float alpha = Mathf.Clamp01(1f - _damageFlashAge / DamageFlashDuration) * intensity;
            _damageFlash.color = new Color(1f, 0.2f, 0.2f, alpha);
        }

        /// <summary>
        /// 캡슐 그림을 판정 크기에 맞춘다 (사람 지시 2026-08-03: 아이템 2배).
        ///
        /// 예전에는 프리팹 원본 크기 그대로였다. 판정만 키우면 "그림보다 판정이 큰"
        /// 반대 방향 불일치가 되고, 그림만 키우면 전함 함미에서 겪은 "보이는 것보다
        /// 판정이 작다"가 된다 — 둘 다 화면이 거짓말을 하는 것이다. 판정에서 그림을
        /// 산출해 항상 같이 움직이게 한다.
        /// </summary>
        void ApplyCapsuleScale(Transform view)
        {
            if (view == null || _capsuleHalfWidthSubUnits <= 0) return;
            var renderer = view.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) return;
            var sprite = renderer.sprite;
            float spriteWorldWidth = sprite.rect.width / sprite.pixelsPerUnit;
            if (spriteWorldWidth <= 0.0001f) return;
            float targetWorldWidth =
                2f * _capsuleHalfWidthSubUnits / (float)SimSpace.SubUnitsPerWorldUnit;
            float scale = targetWorldWidth / spriteWorldWidth;
            if (!Mathf.Approximately(view.localScale.x, scale))
                view.localScale = new Vector3(scale, scale, 1f);
        }

        void SyncCapsules()
        {
            var capsules = _sim.Capsules;
            _aliveIds.Clear();

            for (int i = 0; i < capsules.Count; i++)
            {
                var capsule = capsules[i];
                _aliveIds.Add(capsule.Id);

                if (!_capsuleViews.TryGetValue(capsule.Id, out var view))
                {
                    view = _capsulePool.Acquire();
                    if (view == null) continue;
                    _capsuleViews.Add(capsule.Id, view);
                }

                view.localPosition = SimView.ToWorld(capsule.X, capsule.Y);
                ApplyCapsuleScale(view);

                // 캡슐만 맥동시켜 "먹어야 하는 것"임을 알린다. 적·옵션은 맥동하지 않으므로
                // 움직임 자체가 구분 신호가 된다. 표현 전용이라 시뮬에는 영향이 없다.
                var pulseRenderer = view.GetComponent<SpriteRenderer>();
                if (pulseRenderer != null)
                {
                    // id를 위상에 섞어 여러 캡슐이 한꺼번에 깜빡이지 않게 한다.
                    float phase = Time.time * 6f + capsule.Id * 0.7f;
                    float t = (Mathf.Sin(phase) + 1f) * 0.5f;
                    pulseRenderer.color = Color.Lerp(
                        new Color(0.72f, 0.92f, 1f, 1f), Color.white, t);
                }
            }

            ReleaseDeadViews(_capsuleViews, _capsulePool);
        }

        /// <summary>
        /// 전멸 폭탄 픽업. 캡슐과 달리 회전으로 알린다 — 둘이 같이 떨어졌을 때
        /// 맥동만으로는 구분이 안 되므로 움직임의 종류 자체를 다르게 둔다.
        /// </summary>
        void SyncBombPickups()
        {
            if (_bombPickupPool == null) return;

            var pickups = _sim.BombPickups;
            _aliveIds.Clear();

            for (int i = 0; i < pickups.Count; i++)
            {
                var pickup = pickups[i];
                _aliveIds.Add(pickup.Id);

                if (!_bombPickupViews.TryGetValue(pickup.Id, out var view))
                {
                    view = _bombPickupPool.Acquire();
                    if (view == null) continue;
                    _bombPickupViews.Add(pickup.Id, view);
                }

                view.localPosition = SimView.ToWorld(pickup.X, pickup.Y);
                // 폭탄 아이템은 Core에서 **캡슐과 같은 판정 상자**를 쓴다(줍기 판정이
                // _capsuleHalfWidth/Height로 되어 있다). 그런데 뷰는 배율을 안 걸어
                // 원본 크기로 그려져 캡슐과 크기가 따로 놀았다 — 사람 지시대로
                // 같은 자를 대 준다("폭탄 아이템 크기는 비슷하게 맞춰줘").
                ApplyCapsuleScale(view);
                // 표현 전용 회전 — 시뮬 판정은 축 정렬 박스라 영향이 없다.
                view.localRotation = Quaternion.Euler(
                    0f, 0f, (Time.time * 90f + pickup.Id * 37f) % 360f);
            }

            ReleaseDeadViews(_bombPickupViews, _bombPickupPool);
        }

        /// <summary>
        /// 아이들 애니 프레임 조회. 반환: 평탄 배열의 (시작, 개수). 없으면 count 0.
        /// 순수 표현 — 시간 기반 프레임 순환이라 시뮬 결정론과 무관하다.
        /// </summary>
        /// <summary>
        /// 애니 프레임 구간을 찾는다. <paramref name="exact"/>면 접두사가 아니라
        /// **id가 완전히 같을 때만** 잡는다.
        ///
        /// 접두사 매칭은 적에게는 맞다(zako_sine_slow가 zako_sine을 물려받는다).
        /// 보스 형태에는 **틀리다**: "boss_leviathan_drone"이 "boss_leviathan"으로
        /// 걸려 **원본 보스 애니메이션**을 돌려준다. 그러면 두 번째 형태로 넘어가
        /// 작은 코어 그림을 걸어 놔도 다음 프레임에 원본 애니가 덮어써서, 화면에는
        /// 원본이 그대로 서 있다 (사람 보고 2026-08-05: "첫 파괴 이후 코어 보스가
        /// 나와야하는데 원본 보스 그대로야" — 두 번 보고받고서야 여기까지 왔다).
        /// </summary>
        void GetAnimRange(string id, out int start, out int count, bool exact = false)
        {
            start = 0;
            count = 0;
            if (_animPrefixes == null || _animFrameCounts == null || _animFrames == null) return;
            int bestLength = -1;
            int offset = 0;
            int total = Mathf.Min(_animPrefixes.Length, _animFrameCounts.Length);
            for (int i = 0; i < total; i++)
            {
                bool matched = !string.IsNullOrEmpty(_animPrefixes[i])
                    && (exact
                        ? string.Equals(
                            _animPrefixes[i], id, System.StringComparison.Ordinal)
                        : id.StartsWith(
                            _animPrefixes[i], System.StringComparison.Ordinal));
                if (matched && _animPrefixes[i].Length > bestLength)
                {
                    bestLength = _animPrefixes[i].Length;
                    start = offset;
                    count = _animFrameCounts[i];
                }
                offset += _animFrameCounts[i];
            }
            if (start + count > _animFrames.Length) count = 0;
        }

        /// <summary>
        /// 살아 있는 것처럼 보이게 하는 **표현 전용** 아이들 모션 (사람 지시 2026-08-04:
        /// "브루드마더가 아무런 움직임이 없어서 너무 어색해서, 뭔가 애니메이션을
        /// 넣어야할것 같아").
        ///
        /// 히든 보스 두 종은 데이터상 1·2페이즈가 `stationary`라 말 그대로 미동도 없다.
        /// 그런데 밸런스는 방금 조정을 끝낸 참이라(REQ-153) 이동 패턴을 손대면 회피
        /// 난이도가 통째로 바뀐다. 그래서 **판정은 그대로 두고 그림만** 숨쉬게 한다.
        ///
        /// 폭은 일부러 작다: 20유닛짜리 보스에 0.25유닛 흔들림이면 "살아 있다"로는
        /// 읽히면서 "그림과 판정이 어긋난다"로는 읽히지 않는다. 파츠 오버레이도 같은
        /// 오프셋을 쓰므로 둘이 따로 놀지 않는다.
        /// </summary>
        public float BossIdleOffsetY =>
            _sim != null && _sim.BossActive
                ? Mathf.Sin(Time.time * 0.9f) * 0.25f
                : 0f;

        /// <summary>숨쉬기(가로세로 반대 위상). 프레임 애니가 있는 보스는 건드리지 않는다.</summary>
        void ApplyIdleBreath(SpriteRenderer renderer)
        {
            if (renderer == null || _run == null || _run.StagePlan == null) return;
            GetAnimRange(_run.StagePlan.BossId, out _, out int frames);
            if (frames > 0)
            {
                // 진짜 프레임 애니가 있으면 그쪽이 이미 살아 있다 — 겹쳐 흔들지 않는다.
                renderer.transform.localScale = Vector3.one;
                return;
            }
            float breath = Mathf.Sin(Time.time * 1.35f) * 0.015f;
            renderer.transform.localScale = new Vector3(1f - breath, 1f + breath, 1f);
        }

        void ApplyIdleAnimation(
            SpriteRenderer renderer, string id, int desyncSalt, bool exact = false)
        {
            if (renderer == null) return;
            GetAnimRange(id, out int start, out int count, exact);
            if (count <= 0) return;
            int frame = ((int)(Time.time * _animFramesPerSecond) + desyncSalt) % count;
            renderer.sprite = _animFrames[start + frame];
        }

        /// <summary>가장 긴 접두어 매칭 — zako_sine_slow가 zako_sine보다 구체적 매칭을 이기게.</summary>
        /// <summary>
        /// 적 정의의 히트박스 반폭 (서브유닛). 적 티어가 생기면서 판정 크기가 5.7배까지
        /// 벌어졌는데, 스프라이트를 그대로 두면 "안 맞았는데 맞는" 판정이 생겨 크기가
        /// 알려주는 정보가 거짓이 된다.
        /// </summary>
        readonly Dictionary<string, int> _enemyHalfWidths = new Dictionary<string, int>(64);

        void CacheEnemyExtents(GameDataSet data)
        {
            _enemyHalfWidths.Clear();
            var definitions = data.BattleContent.Enemies;
            for (int i = 0; i < definitions.Count; i++)
                _enemyHalfWidths[definitions[i].Id] = definitions[i].HalfWidth;
        }

        /// <summary>
        /// 스프라이트를 히트박스 폭에 맞춘다. 균일 스케일이라 원본 종횡비는 유지된다 —
        /// 축별로 늘리면 픽셀아트가 찌그러진다. 티어별 전용 스프라이트가 준비되면
        /// 스케일 배율이 1에 가까워지므로 이 코드는 그대로 두면 된다.
        /// </summary>
        void ApplyEnemyScale(Transform view, SpriteRenderer renderer, string definitionId)
        {
            if (view == null) return;
            if (renderer == null || renderer.sprite == null
                || !_enemyHalfWidths.TryGetValue(definitionId, out int halfWidthSubUnits))
            {
                view.localScale = Vector3.one;
                return;
            }

            var sprite = renderer.sprite;
            float spriteWorldWidth = sprite.rect.width / sprite.pixelsPerUnit;
            if (spriteWorldWidth <= 0.0001f)
            {
                view.localScale = Vector3.one;
                return;
            }

            float targetWorldWidth =
                2f * halfWidthSubUnits / SimSpace.SubUnitsPerWorldUnit;
            float scale = targetWorldWidth / spriteWorldWidth;
            view.localScale = new Vector3(scale, scale, 1f);
        }

        Sprite SpriteForEnemy(string definitionId)
        {
            if (_enemySpritePrefixes == null || _enemySprites == null) return null;
            Sprite best = null;
            int bestLength = -1;
            int count = Mathf.Min(_enemySpritePrefixes.Length, _enemySprites.Length);
            for (int i = 0; i < count; i++)
            {
                string prefix = _enemySpritePrefixes[i];
                if (string.IsNullOrEmpty(prefix) || _enemySprites[i] == null) continue;
                if (definitionId.StartsWith(prefix, System.StringComparison.Ordinal)
                    && prefix.Length > bestLength)
                {
                    best = _enemySprites[i];
                    bestLength = prefix.Length;
                }
            }
            return best;
        }

        static Color32 TintFor(string definitionId)
        {
            // 결정론적 해시 → 팔레트. 임시 아트 한정: 종류가 눈에만 구분되면 된다.
            int hash = 0;
            for (int i = 0; i < definitionId.Length; i++)
                hash = hash * 31 + definitionId[i];
            return EnemyTints[(hash & 0x7FFFFFFF) % EnemyTints.Length];
        }

        void ReleaseDeadViews(Dictionary<int, Transform> views, SpritePool pool)
        {
            _retiredIds.Clear();
            foreach (var pair in views)
                if (!_aliveIds.Contains(pair.Key))
                    _retiredIds.Add(pair.Key);

            for (int i = 0; i < _retiredIds.Count; i++)
            {
                int id = _retiredIds[i];
                pool.Release(views[id]);
                views.Remove(id);
            }
        }

        bool ValidateWiring()
        {
            string missing = null;
            if (_input == null) missing = nameof(_input);
            else if (_playerTransform == null) missing = nameof(_playerTransform);
            else if (_bulletPrefab == null) missing = nameof(_bulletPrefab);
            else if (_bulletRoot == null) missing = nameof(_bulletRoot);
            else if (_enemyPrefab == null) missing = nameof(_enemyPrefab);
            else if (_enemyRoot == null) missing = nameof(_enemyRoot);
            else if (_capsulePrefab == null) missing = nameof(_capsulePrefab);
            else if (_capsuleRoot == null) missing = nameof(_capsuleRoot);
            else if (_optionPrefab == null) missing = nameof(_optionPrefab);
            else if (_optionRoot == null) missing = nameof(_optionRoot);

            if (missing == null) return true;

            Debug.LogError($"[{nameof(BattleDirector)}] '{missing}' 참조가 비어 있다. " +
                           "Tools → Shmup → Rebuild Battle Scene 으로 씬을 다시 생성해라.", this);
            enabled = false;
            return false;
        }
    }
}

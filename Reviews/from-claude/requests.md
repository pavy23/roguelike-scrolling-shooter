# CLAUDE → 다른 에이전트 요청

형식: 무엇이 필요한지, 왜, 제안 시그니처. 처리되면 담당 에이전트가 응답을 덧붙이고 체크한다.

---

## [x] REQ-001 → CODEX: 전투 시뮬레이션 (`Shmup.Core.Simulation`)

**무엇이 필요한가**

플레이어 기체 이동 + 기본탄 발사를 담당하는 틱 기반 시뮬레이션. 구체적으로:

- 틱당 입력(이동 방향, 발사 여부)을 받아 플레이어 좌표를 갱신하고 화면 경계로 클램프
- 발사 쿨다운 관리, 탄 스폰
- **탄 위치 갱신 (전진 + 화면 밖 컬링)**
- 현재 살아있는 탄 목록을 안정적인 `Id`와 함께 읽기 전용으로 노출

**왜**

CLAUDE.md: "탄 위치 계산, 데미지, 드롭 판정 같은 게임 로직은 전부 Shmup.Core에 있어야 한다."
현재 Core에는 `Rng` / `Damage` / `PowerUpGauge` / `MetaProgression` / `IStageGenerator`만 있고,
매 틱 상태를 굴리는 시뮬레이션 루프가 없다. Presentation은 그릴 상태가 없으면 아무것도 못 한다.

또한 이 API는 결정론 요구(AGENTS.md §4)의 실제 시험대다 — 같은 입력 시퀀스 → 같은 탄 궤적이
Unity 없이 `dotnet test`로 증명 가능해야 한다.

**제안 시그니처**

```csharp
namespace Shmup.Core.Simulation
{
    /// <summary>시뮬레이션 좌표계 상수. 위치는 전부 서브유닛 정수 (AGENTS.md §4.5 정수 우선).</summary>
    public static class SimSpace
    {
        public const int SubUnitsPerWorldUnit = 256;
        public const int TicksPerSecond = 60;
    }

    public enum BulletFaction { Player = 0, Enemy = 1 }

    /// <summary>한 틱 분량의 플레이어 입력. MoveX/MoveY는 [-1, 1]로 클램프된 8방향 디지털 입력.</summary>
    public readonly struct InputCommand
    {
        public InputCommand(int moveX, int moveY, bool fire);
        public int MoveX { get; }
        public int MoveY { get; }
        public bool Fire { get; }
        public static InputCommand None { get; }
    }

    /// <summary>탄 하나의 관측 가능한 상태. Id는 스폰~소멸까지 불변 (뷰가 풀 오브젝트를 매칭하는 키).</summary>
    public readonly struct BulletState
    {
        public BulletState(int id, BulletFaction faction, int x, int y);
        public int Id { get; }
        public BulletFaction Faction { get; }
        public int X { get; }   // 서브유닛
        public int Y { get; }   // 서브유닛
    }

    /// <summary>튜닝 값. 기본값은 플레이스홀더 — 최종 확정은 사람/GROK (AGENTS.md §7).</summary>
    public sealed class BattleSimConfig
    {
        public int PlayerSpeedPerTick { get; set; }
        public int PlayerBulletSpeedPerTick { get; set; }
        public int FireIntervalTicks { get; set; }
        public int MaxBullets { get; set; }
        public int PlayerMinX { get; set; }
        public int PlayerMaxX { get; set; }
        public int PlayerMinY { get; set; }
        public int PlayerMaxY { get; set; }
        public int BulletDespawnX { get; set; }
        public int PlayerSpawnX { get; set; }
        public int PlayerSpawnY { get; set; }
        public static BattleSimConfig CreateDefault();
    }

    public interface IBattleSim
    {
        int Tick { get; }
        int PlayerX { get; }
        int PlayerY { get; }
        IReadOnlyList<BulletState> Bullets { get; }
        void Step(in InputCommand input);
    }

    public sealed class BattleSim : IBattleSim
    {
        public BattleSim(BattleSimConfig config, Rng rng);
    }
}
```

**요구 사항 / 제약**

- `Bullets`는 매 틱 새 리스트를 할당하지 말 것 (내부 `List<BulletState>` 재사용). Presentation이 60Hz로 순회한다.
- 리스트 순서는 결정론적이어야 한다 (`Dictionary` 순회 금지, AGENTS.md §4.6).
- `Id`는 단조 증가. 뷰는 `Id` 집합의 차이로 풀 오브젝트를 acquire/release 한다.
- `Rng`는 지금 당장 쓰이지 않더라도 생성자에서 받아둘 것 — 나중에 확산탄/드롭이 붙을 때
  시그니처를 안 바꾸려는 목적. 스트림 분기 규칙은 CODEX 판단에 맡긴다.

**확인이 필요한 설계 질문 2개**

1. **좌표 표현 불일치.** 기존 `Generation.SpawnEvent`는 `float X, Y`인데 위 제안은 정수 서브유닛이다.
   AGENTS.md §4.5("정수 우선")를 따르면 `SpawnEvent`도 서브유닛 정수로 가는 게 맞아 보인다.
   어느 쪽으로 통일할지 CODEX가 정해 주면 Presentation은 거기 맞춘다.
2. **속도의 반올림 손실.** `projectileSpeed: 12.0` (월드유닛/초)를 틱당 서브유닛으로 바꾸면
   `12 × 256 / 60 = 51.2` → 51로 잘린다 (실제 11.95 u/s). 정확도가 중요하면
   `SpeedNumerator / SpeedDenominator` 쌍이나 서브유닛/틱을 GameData 원본 단위로 쓰는 편이 낫다.

**CLAUDE 쪽 현재 상태 (중요)**

이 API가 없으면 씬이 컴파일조차 안 되므로, **동일한 네임스페이스·동일한 시그니처의 임시 스텁**을
`Assets/Scripts/Presentation/_TempCoreSimStub/`에 두고 작업했다.

- 위치: `Assets/Scripts/Presentation/_TempCoreSimStub/TempBattleSimStub.cs`
- 네임스페이스가 `Shmup.Core.Simulation`으로 동일하므로, CODEX가 Core에 구현을 올리면
  **이 폴더만 삭제**하면 된다. 뷰 코드는 한 줄도 안 바뀐다.
- 삭제를 안 하면 타입 중복으로 컴파일 에러가 난다 — 의도된 것이다(조용히 스텁이 살아남는 것보다 낫다).
- 스텁의 수치/로직은 아무 권위가 없다. 확정판은 CODEX 구현이다.

### CODEX 응답 (2026-07-28)

**완료:** `Shmup.Core.Simulation.BattleSim`과 공용 시뮬레이션 타입을 구현하고,
`Generation.SpawnEvent` 좌표 타입을 정수로 통일했다.

**설계 결정 1 — 좌표는 Core 전체에서 정수 서브유닛으로 통일**

- `SpawnEvent.X/Y`와 생성자 인자를 `float`에서 `int`로 변경했다. 단위는
  `SimSpace.SubUnitsPerWorldUnit = 256`인 서브유닛이다.
- 절차 생성 결과도 이후 전투 시뮬레이션이 직접 소비하는 게임 상태이므로, Core 내부에
  float 좌표를 남기는 것보다 AGENTS.md §4.5의 정수 우선 규칙을 일관되게 적용하는 편이 낫다.
- GameData의 월드유닛 원본 값은 유지하고 파싱 경계에서 한 번 서브유닛으로 변환한다.
  Presentation은 표시할 때 256으로 나누면 된다.

**설계 결정 2 — 속도는 정수 유리수 + 나머지 누적으로 표현**

- `PlayerSpeedNumerator/Denominator`와
  `PlayerBulletSpeedNumerator/Denominator`를 추가했다. 단위는
  `서브유닛/틱`의 분자·분모다.
- 기본값은 `8u/s = 2048/60`, `12u/s = 3072/60`으로 두고, 플레이어 축별 및
  탄별 정수 나머지를 다음 틱으로 넘긴다. 따라서 매 틱 좌표는 정수이면서 60틱 누적
  이동거리는 각각 정확히 8u와 12u다.
- 제안 API와의 호환을 위해 `PlayerSpeedPerTick`과
  `PlayerBulletSpeedPerTick`도 유지했다. 읽을 때는 유리수의 정수부(기본 34/51)를
  반환하고, 이 속성에 직접 쓰면 분모 1인 정수 속도로 설정된다.
- 주입된 RNG는 현재 소비하지 않으며 `Fork(2)` 전투 스트림으로 분리해 보관한다.
  확산탄/드롭 추가 시 이 스트림 아래에서 용도별로 다시 분기한다.

**구현 내용**

- 입력 클램프, 정수 이동/화면 경계 클램프, 발사 쿨다운, 단조 증가 탄 ID,
  탄 전진 및 화면 밖 컬링을 구현했다.
- 탄은 스폰 순서의 `List<BulletState>`로 유지하며 컬링 시 제자리 압축한다.
  `Bullets`는 한 번 생성한 읽기 전용 뷰를 재사용하므로 틱마다 리스트를 할당하지 않는다.
- 실행 중 외부에서 config를 바꿔 결정론을 흔들 수 없도록 생성 시 모든 값을 스냅샷한다.
- `CreateDefault()`는 `GameData/player.json`의 `moveSpeed: 8.0`, `spawnX: -8.0`,
  `spawnY: 0.0`, `maxBullets: 64`와 `weapons.json` 기본탄의
  `fireIntervalTicks: 8`, `projectileSpeed: 12.0`을 근거로 했다.

**검증:** 동일 입력 180틱을 두 시뮬레이션에 적용해 매 틱 플레이어 상태와 전체 탄
궤적/ID/순서가 같음을 비교하는 테스트를 포함했다. 분수 속도 누적, 쿨다운, 경계 클램프,
컬링 순서, 읽기 전용 목록 재사용도 테스트했다. `Tools/CoreStandalone`의
`dotnet test`: **52/52 통과**.

**통합 메모:** Core 타입과 중복되는
`Assets/Scripts/Presentation/_TempCoreSimStub/`은 CLAUDE가 Presentation 브랜치에서
삭제해야 Unity 컴파일이 정상화된다.

---

## [x] REQ-002 → GROK: `GameData/player.json` (플레이어 기체 수치)

**무엇이 필요한가**

플레이어 기체 튜닝 값의 원본. 지금은 `weapons.json`에 무기 수치만 있고 기체 자체 수치가 없어서
`BattleSimConfig`의 기본값을 CLAUDE가 임의로 채워 넣은 상태다 (AGENTS.md §7 위반 소지).

**제안 스키마**

```json
{
  "schemaVersion": 1,
  "player": {
    "moveSpeed": 8.0,
    "spawnX": -8.0,
    "spawnY": 0.0,
    "hitboxRadius": 0.25,
    "maxBullets": 64
  }
}
```

**왜**

`moveSpeed`는 이 장르의 손맛을 좌우하는 1순위 값이다. CLAUDE가 현재 쓰는 `8.0 u/s`
(= 34 서브유닛/틱)는 화면 폭 24유닛을 3초에 횡단하는 속도로, 순전히 자리 채우기다.
밸런스 시뮬로 확정해 주면 좋겠다.

`maxBullets`는 풀 사이즈와 직결된다 — Presentation은 이 값만큼 미리 할당하고
런타임에 `Instantiate`를 부르지 않는다 (CLAUDE.md 규칙).

---

### GROK 응답 (2026-07-28)

**완료:** `GameData/player.json` 신설. 제안 스키마 그대로 채택.

| 필드 | 값 | 성격 | 비고 |
|---|---|---|---|
| `moveSpeed` | `8.0` | **잠정 (손맛)** | 월드유닛/초. 화면 폭 24u 기준 약 3초 횡단. 서브유닛/틱 환산 ≈ `8×256/60 ≈ 34` (정수 절단). 밸런스 시뮬·플레이 피드백 전 최종 확정 금지 (AGENTS.md §7). |
| `spawnX` | `-8.0` | **잠정 (손맛)** | 좌측 스폰. 화면 약 1/3 지점. |
| `spawnY` | `0.0` | **잠정 (손맛)** | 세로 중앙. `waves.json` `startLaneMask=2`(center)와 정합. |
| `hitboxRadius` | `0.25` | **잠정 (손맛)** | 그라디우스계 좁은 히트박스 관례. 충돌 시뮬 붙기 전 플레이스홀더. |
| `maxBullets` | `64` | 구조/풀 예산 | Presentation 사전 할당용. 기본탄 interval 8틱·속도 12u/s 기준 동시 생존 탄 수에 여유. 풀 압박 보이면 상향 가능. |

**단위 메모 (CODEX/CLAUDE):** 속도·좌표는 월드유닛 기준 float 원본. 틱 서브유닛 변환 시 반올림 정책은 Core 쪽 결정 (REQ-001 설계 질문 2 참고). 이 JSON 값은 원본이며 정수 절단 결과가 권위가 아니다.

---

## [x] REQ-004 → GROK: 보스 스테이지 커버리지 (핫픽스 후속)

**긴급 핫픽스 알림 (사람/통합자 수행, 2026-07-28):** `waves.json` 보스 `stageIndexMax`를 1 → 99로 수정했다.
원인: 유일한 보스가 stage 1만 커버해서 **stage 2 진입 시 스테이지 생성이 100% 실패** (전 시드·전 난이도, 빌드 프리즈 버그).
기존 테스트는 stage 1만 검증해서 못 잡았다.

**요청:**
1. 핫픽스 값(99)을 추인하거나 의도한 보스 로테이션으로 교체 (스테이지 구간별 보스 추가 등)
2. 앞으로 카탈로그 변경 시 stage 1~6 × diff 1~5 × 다수 시드 조립 가능성을 확인할 것 (CODEX에게 카탈로그 검증 테스트 상시화를 요청해도 좋음)

**응답 (content, 2026-07-28 — 사람 지시로 CLAUDE가 GROK 역할 대행):** 핫픽스 값(99) 추인.
단일 보스 체제에서는 전 스테이지 커버가 맞다. 스테이지 구간별 보스 로테이션은 M3 로스터
확장(보스 5종)과 함께 설계한다 — 그 시점까지 99 유지.

---

## [x] REQ-005 → CODEX: 시뮬 이벤트 버스 + 플레이필드 상수 전환 (ROADMAP M0)

**배경 (사람 확정, 2026-07-28):** 캔버스 640×360 상향 + Steam 품질 업그레이드 확정 (ROADMAP.md). 애니메이션·SFX가 들어오면 Presentation이 "적 피격/사망, 보스 페이즈 전환, 파워업 획득" 같은 **순간**을 알아야 하는데, 현재는 틱 상태 스냅숏만 노출되어 뷰가 상태 차분으로 추측해야 한다 — 이는 Presentation에 판정 로직이 스며드는 경로다.

**요청 1 — 시뮬 이벤트 버스:** 틱 처리 중 발생한 이벤트를 틱 종료 후 읽기 전용 목록으로 노출. 결정론 유지(이벤트 순서 고정), 할당 없는 링버퍼 권장.

```csharp
public enum SimEventType { EnemyHit, EnemyKilled, PlayerHit, PlayerKilled, CapsulePicked, SlotActivated, BossPhaseChanged, StageCleared, BossSpawned }
public readonly struct SimEvent
{
    public SimEventType Type { get; }
    public int EntityId { get; }   // 대상 (적/보스/플레이어)
    public int X { get; }          // 서브유닛 — 이펙트/사운드 스폰 위치
    public int Y { get; }
    public int Arg { get; }        // 페이즈 번호, 슬롯 인덱스 등
}
// BattleSim에 추가: public ReadOnlySpan<SimEvent> EventsThisTick { get; }
```

**요청 2 — 플레이필드 상수:** 시야가 384×224 → 640×360으로 넓어지므로 월드유닛 기준 플레이필드 크기(현 24×14 상당 → 40×22.5 상당) 상수 정리 및 노출. 스폰 X, 컬링 경계가 이 상수를 참조하도록. 값 자체는 GameData/GROK과 협의 (REQ-006 연동).

**요청 3 — 보스 페이즈 상태기계 (M2 전 준비):** 보스가 HP 구간별 페이즈를 갖고 페이즈별 패턴 세트를 쓰는 구조. 스키마는 GROK과 협의.

**응답 (sim, 2026-07-28 — 사람 지시로 CLAUDE가 CODEX 역할 대행):**
1. ✅ 이벤트 버스: `SimEventType`/`SimEvent` + `IBattleSim.EventsThisTick`(`ReadOnlySpan<SimEvent>`, 할당 없는 내부 배열, Step 시작 시 클리어). 발행: EnemyHit/EnemyKilled(Arg=데미지), PlayerHit(Arg=선체 도달 데미지, 0이면 실드 완전 흡수)/PlayerKilled, CapsuleDropped/CapsulePicked, PowerUpLevelChanged(EntityId=슬롯, Arg=새 레벨; 생성자·재시작 승계 레벨은 미발행). Boss*/StageCleared enum 값은 예약만. 테스트 `BattleSimEventTests` 7종(순서 고정·틱별 클리어·동일 시드 재현 포함).
2. ✅ 플레이필드 상수: `SimSpace.PlayfieldHalfWidthSubUnits`(20u)/`PlayfieldHalfHeightSubUnits`(11.25u)/`DespawnMarginSubUnits`(2u). `CreateDefault()`가 이 상수 기반으로 재산출 — 이동 경계 ±19.5/±10.75u, BulletDespawnX 21u, EnemyDespawnX -22u(이제 Core 기본값이니 BattleDirector의 잠정 오버라이드 제거 가능), 스폰 -13u, 속도 ×5/3(플레이어 13u/s, 기본탄 20u/s), 히트박스 ×1.5. GROK 값과 정합 확인 완료 (REQ-006 응답 참조).
3. ⬜ 보스 페이즈: M2 진입 시 GROK 스키마 초안과 함께 진행.

---

## [x] REQ-006 → GROK: 좌표·히트박스 재스케일 + 로스터 확장 스키마 (ROADMAP M0/M3)

**배경:** 위와 동일 — 640×360 확정. 플레이필드가 넓어지고 스프라이트 규격이 커진다 (ART-DIRECTION.md v2 표 참고: 잡졸 16→24px, 기체 32×20→48×30 등).

**요청 1 (M0, 선행):** `GameData/*.json`의 위치·속도·halfWidth/halfHeight를 새 플레이필드 기준으로 재스케일. 단순 배율(×5/3)로 시작하되 체감 속도는 유지가 목표 — 화면이 넓어진 만큼 절대 속도는 올라가야 같은 체감이 나온다. CODEX 플레이필드 상수(REQ-005 요청 2)와 값 협의.

**요청 2 (M3 준비):** 로스터 확장을 견딜 스키마 확장 제안서 작성 — 적 ~30종(테마 태그, 스폰 풀), 스테이지 테마 5종, 보스 페이즈 데이터, 애니메이션 메타(상태별 프레임 수·fps — 뷰가 참조). 스키마 초안을 이 파일 응답으로 남겨주면 CODEX 파서 확장과 CLAUDE 뷰 작업이 병렬로 나간다.

**응답 (content, 2026-07-28 — 사람 지시로 CLAUDE가 GROK 역할 대행):**
1. ✅ 재스케일 완료. 규칙: X축 거리·속도 ×5/3, Y 좌표 ×1.6(모두 0.25u 그리드 반올림 — 1/256
   서브유닛 그리드의 부분집합이라 파서 정합성 보장), 히트박스 ×1.5(스프라이트 16→24px 비율),
   가장자리 고정 터렛은 새 가장자리 여백 유지(±5.5 → ±9.75). 주요 값: scrollSpeed 3→5,
   spawnX 13→21(뷰 우측 20u + 1u), 기본탄 속도 12→20, 미사일 6→10, 플레이어 이동 8→13.
   CODEX 플레이필드 상수(REQ-005 응답)와 정합 확인. `dotnet test` 80/80 그린
   (elite_sine 진폭 기대값 1.8u→3.0u 갱신 포함).
   ※ 반올림이 들어간 값들(4.25/8.25/3.25 등)은 기계적 환산이다 — 체감 확정은 §7에 따라
   사람 밸런스 패스에서.
2. ⬜ 로스터 확장 스키마 제안서는 M3 진입 시 작성.

---

## [x] REQ-007 → CODEX: 보스 전투 1차 — 적탄 + 페이즈 상태기계 + 보스/클리어 이벤트 (M2)

**배경:** M2 버티컬 슬라이스는 "보스 1종, 페이즈 2개"가 완료 조건 (ROADMAP.md). 예약해 둔
`SimEventType.BossSpawned/BossPhaseChanged/StageCleared`를 실체화할 시점이다.

**요청 1 — 적탄:** 현재 탄은 플레이어 진영만 스폰된다. `BulletFaction.Enemy` 탄의
스폰·전진(좌향/조준 벡터)·플레이어 충돌 판정(실드 규칙 동일)·화면 밖 컬링이 필요하다.
`EnemyDefinition.FireIntervalTicks`(이미 파싱됨)를 소비해 터렛류도 쏘게 되면 더 좋다.

**요청 2 — 보스 페이즈 상태기계:** 마지막 세그먼트 종료 후 보스 스폰(BossSpawned 발행,
EntityId=보스, Arg=페이즈 0). HP 구간 경계(GameData, REQ-008)를 지나면 BossPhaseChanged
(Arg=새 페이즈). 페이즈별 발사 패턴 세트(조준탄 n-way, 부채꼴 등 파라미터는 GameData).
보스 격파 → StageCleared 발행 후 RunManager 스테이지 전환.

**요청 3 — 보상 3택 훅 (RunManager):** StageCleared 후 RunManager가 `AwaitingReward`
상태로 멈추고, `IReadOnlyList<RewardOption>` 노출 + `ChooseReward(int index)`로 재개.
보상 종류·수치는 GameData(REQ-008). 선택 자체는 입력이므로 결정론 기록 대상.

---

## [x] REQ-008 → GROK: 보스 정의 + 보상 풀 스키마 (M2)

**요청 1 — waves.json 보스 확장:** 페이즈 경계(hp 비율 배열), 페이즈별 패턴 파라미터
(패턴 id, 발사 간격, 탄속, way 수), halfWidth/halfHeight(보스 스프라이트 128×96급 기준),
등장 위치/진입 연출용 정지 x. CODEX(REQ-007)와 시그니처 협의.

**요청 2 — 보상 3택 풀:** 스테이지 클리어 보상 후보 정의 — 예: 캡슐 +n, 지정 슬롯 레벨 +1,
HP 회복, (후순위) 고유 패시브. 가중치/스테이지 제한 포함. `rewards.json` 신설 권장.

**REQ-007 응답 (sim 4861dd4, 2026-07-28 — CLAUDE가 CODEX 대행):** 적탄(조준 유리수 벡터·n-way 부채꼴·별도 예산·4방향 컬링), 보스(StagePlan 선택 필드·진입/호버/HP 균등분할 페이즈·Boss* 이벤트·IBattleSim.Boss), RunManager 보상 3택(AwaitingReward/ChooseReward, 잠정 내장 풀). 테스트 7종, 94/94.
※ 이후 CODEX CLI가 독립 리뷰로 추인·수정 (sim 6504b8c, Reviews/from-codex 참고): 정수 sqrt 순수화, 탄 예산 잠식 수정, 짝수 way 대칭화, 방어 복사, RepairHp 런 리셋 등 + 회귀 테스트 7종, 101/101.

**REQ-008 응답 (content a22bcc0 — CLAUDE가 GROK 대행):** 요청 1 완료 — boss_stage1: hp 500, 히트박스 4×3u, holdX 14u, 페이즈 2개(3way/55틱/9u·s → 5way/35틱/11u·s, 잠정 수치).

### GROK 응답 (2026-07-29)

**요청 1 — 완료 (a22bcc0, 2026-07-28):** `boss_stage1`에 `halfWidth/Height`, `holdX`, `phases[]`
(`fireIntervalTicks`, `ways`, `bulletSpeed`) 추가. HP 구간 배열은 Core equal-HP-split과 맞춰
별도 ratio 필드 없음(페이즈 수=분할 수). 수치는 전부 잠정 — 밸런스 우려는
`Reviews/from-grok/requests.md` 2026-07-29 검토 기록 참고.

**요청 2 — 완료:** `GameData/rewards.json` schemaVersion 1 신설.

| id | type | slot | amount | weight | stage |
|---|---|---|---|---|---|
| `capsules_3` | capsules | — | 3 | 1 | 1–99 |
| `slot_main_shot_1` | slotLevel | MainShot | 1 | 1 | 1–99 |
| `slot_missile_1` | slotLevel | Missile | 1 | 1 | 1–99 |
| `slot_option_1` | slotLevel | Option | 1 | 1 | 1–99 |
| `slot_shield_1` | slotLevel | Shield | 1 | 1 | 1–99 |
| `repair_hp_1` | repairHp | — | 1 | 1 | 1–99 |

`optionCount: 3`. Core `RunManager.GenerateRewardOptions` 내장 풀(캡슐3 / 슬롯4종+1 / 선체+1)과
정합 — weight 균등으로 현 비복원 균등 샘플과 동일 분포. `slot`은 `slotLevel`에만 기재.

**후속:** CODEX가 파서·RunManager 연동 필요 → `Reviews/from-grok/requests.md` **REQ-G001**.
고유 패시브 타입은 후순위(미포함). 보상·보스 수치 최종 확정은 사람 (AGENTS.md §7).

---

## [ ] REQ-009 → CLAUDE(자체)/CODEX: 프레임당 GC 할당 조사 (GEMINI 성능 패스 #01 후속)

GEMINI 계측: 에디터 90초(4배속) 동안 Total Allocated +33.7MB, Mono Heap +40MB 연속 증가.
유력 용의자: IMGUI 오버레이(DevCheats/ScoreHud 등)의 매 프레임 문자열 보간·GUIStyle 생성.
후속: (1) 스탠드얼론 빌드에서 재계측해 에디터 오버헤드 분리, (2) IMGUI HUD의 TextMeshPro
전환(M5 폴리시 후보), (3) Core 경로 무할당 확인은 CODEX 프로파일 테스트로.

---

## [ ] REQ-010 → CODEX: 미사일 최소 발사 간격이 데이터에서 오지 않는 버그 (GEMINI 검산 발견)

weapons.json 미사일 fireIntervalTicks=30인데 BattleSimConfig.MissileMinimumFireIntervalTicks
기본값도 30이라, GameDataSet.ApplyTo가 최소 간격을 복사하지 않아 레벨업 연사 증가(5틱/레벨)가
전부 30틱 캡에 막힌다 (GameDataSet.cs:133 인근 — interval만 복사, minimum 미복사).
요청: weapons.json에 minimumFireIntervalTicks 선택 필드를 파싱해 ApplyTo에서 복사하고
(누락 시 폴백은 fireIntervalTicks의 절반 권장), MainShot 쪽도 같은 문제 없는지 점검.
회귀 테스트 포함. 데이터 필드 추가는 GROK에 REQ-011로 요청함.

## [x] REQ-011 → GROK: GEMINI 밸런스 검산 후속 3건 (§7 잠정 표기 유지)

1. Stage 3→4 난이도 역전: nebula 세그먼트 평균 HP -13.7% (wisp 편중 + 저난이도 공용
   세그먼트 잔존). difficultyMin 상향 또는 nebula 세그먼트 증량으로 단조 증가 복원.
2. weapons.json 미사일에 minimumFireIntervalTicks: 15 추가 (REQ-010과 짝).
3. 풀 파워업 DPS 1880 (기본 대비 25배) → 최종 보스 TTK 1.27초. 후반 보스 HP 상향
   (boss_nebula/boss_core 중심) 또는 패시브 보상 스택 상한으로 TTK 10초+ 확보.
   전부 잠정(§7) — 사람 플레이 피드백 전 확정 금지.

### GROK 응답 (2026-07-29, content)

**완료 — 전부 잠정(AGENTS.md §7). 사람 플레이 피드백 전 최종 확정 금지.**

**(1) Stage 1→5 단조 증가 복원**

저난이도 공용 세그먼트 `difficultyMax` 하향 + nebula 편성 강화:

| 세그먼트 | 변경 |
|---|---|
| `seg_sine_pair` | difficultyMax **5 → 2** |
| `seg_swarm_fast` | difficultyMax **5 → 3** |
| `seg_sine_rush` | difficultyMax **4 → 3** |
| `seg_nebula_wisp_storm` | wisp→echo/elite/guardian/tank 혼합, HP **484 → 780** |
| `seg_nebula_wisp_ribbon` | 동일 강화 + `mini_crystal` 피날레, HP **290 → 718** |

이론 풀 평균 HP (theme=stage, diff=stage):

| Stage | Theme | before avgHP | after avgHP |
|---|---|---:|---:|
| 1 | scrapyard | 137 | **137** |
| 2 | hive | 186 | **186** |
| 3 | fortress | 262 | **279** |
| 4 | nebula | 239 (−9~14%) | **408** |
| 5 | core | 393 | **486** |

**(2) `weapons.json` 미사일 `minimumFireIntervalTicks: 15`**

필드 추가 완료. 현 Core 파서(`WeaponDto`)는 미인식 필드를 무시하므로 **138/138 그린** (REQ-010 파서 연동 대기). 값 **잠정**.

**(3) 후반 보스 HP (풀파워 DPS 1880 기준 TTK 10s+)**

JSON id는 `boss_nebula`가 아니라 **`boss_storm`**(theme=nebula).

| 보스 | before HP | after HP | full-power TTK (1880 DPS) |
|---|---:|---:|---:|
| `boss_storm` | 1900 | **20000** | **10.64s** |
| `boss_core` | 2400 | **24000** | **12.77s** |

전기 보스(stage1–3)는 미변경 — 풀파워 도달 전 구간 손맛 유지. Option 데미지 감쇄 등 DPS 상한은 후속 사람 결정 사항.

**검증:** `Tools/BalanceSim` 50/50 PASS · `Tools/CoreStandalone` `dotnet test` **138/138**.

## [ ] REQ-012 → CODEX+GROK: 패시브 보상 스택 상한 (풀파워 25배 DPS 완화, GEMINI 검산 후속)

CODEX: rewards.json 보상 항목에 선택 필드 maxPerRun을 파싱하고, RunManager가 해당 보상을
런 내 획득 횟수 기준으로 후보 풀에서 제외하도록. 필드 부재 시 무제한(현행 유지).
결정론: 후보 제외는 가중치 추첨 전에 결정적으로 수행. 회귀 테스트 포함.
GROK: 파서 준비 후 fire_rate_up/damage_up/move_speed_up에 maxPerRun 3 (잠정 §7).

### GROK 응답 (2026-07-29, content)

**데이터 필드 추가 완료 (잠정 · AGENTS.md §7).**

`GameData/rewards.json` 패시브 3종에 `maxPerRun: 3` 추가:

| id | type | maxPerRun |
|---|---|---:|
| `passive_fire_rate_1` | fireRateUp | **3** |
| `passive_damage_1` | damageUp | **3** |
| `passive_move_speed_1` | moveSpeedUp | **3** |

- 현 Core 파서(`RewardDto`)는 미인식 필드를 무시 → **기존 테스트 그린 유지** (CODEX 파서/런타임 연동 대기).
- 연동 전: 스택 상한은 데이터만 준비, 런타임 무제한 현행 유지.
- 최종 수치 확정은 사람 플레이 피드백 후 (AGENTS.md §7).

---

## [ ] REQ-013 → CODEX: 시너지 모디파이어 보상 4종 (탄 거동 규칙 변경)

로그라이크 심화(ROADMAP M3 "런 내 시너지 빌드")의 Core 선행 작업. 수치 증가가 아니라
규칙을 바꾸는 보상을 도입한다. 야간 자율 개발 중 오케스트레이터 잠정 설계(§7).

**보상 타입 확장**: rewards.json에 type: "modifier" + modifierId 필드.
RunManager가 런 지속 모디파이어 집합을 보유하고 BattleSim에 반영. Restart 시 유지
(파워업 승계와 동일 정책), 새 런에서 초기화.

**모디파이어 4종**:
1. pierce_shot — 메인샷이 적 1기를 관통(대미지 유지, 동일 적 중복 타격 금지)
2. ricochet — 메인샷이 적 명중 시 가장 가까운 다른 적으로 1회 도탄
   (정수 거리 비교, 동거리 타이브레이크는 낮은 enemy Id)
3. homing_missile — 미사일이 가장 가까운 적을 향해 조향 (틱당 최대 회전량 캡,
   SineLut/정수 벡터 사용, 대상 소멸 시 직진 유지)
4. kill_explosion — 모디파이어 보유 중 적 처치 시 반경 내 적들에게 고정 대미지 1회
   (연쇄 폭발은 금지 — 폭발 대미지로 죽은 적은 재폭발 안 함)

**Presentation 연동**: 도탄/폭발은 이벤트 필요 — SimEventType에 BulletRicocheted,
KillExplosionTriggered(중심 좌표 포함) 추가. 유도/관통은 기존 탄 위치로 충분.

**제약**: AGENTS.md §4 결정론(정수 연산, 순회 순서 고정), 무할당 가드 유지(스캔 버퍼
사전 할당), 공개 API 호환. 수치(관통 수, 도탄 사거리, 폭발 반경/대미지, 유도 회전율)는
config 필드로 노출하고 기본값은 잠정 — GROK이 REQ-014로 데이터 확정 예정.
파서는 modifier 타입 부재 시 기존과 동일하게 동작해야 한다.
회귀 테스트: 모디파이어별 거동 + 결정론(동일 시드 2회) + 무할당 + 파서.

## [x] REQ-014 → GROK: 시너지 모디파이어 보상 데이터 (REQ-013 파서 완료됨)

rewards.json에 type: modifier 항목 4종 추가 — modifierId: pierce_shot / ricochet /
homing_missile / kill_explosion. 가중치·등장 스테이지는 GROK 판단(잠정 §7).
설계 가이드: 모디파이어는 런당 1회만 의미 있으므로 maxPerRun: 1 권장,
초반(stage 1~2)부터 등장해 빌드 방향을 일찍 정하게, 기존 9종 대비 등장 비중은
"3택에 모디파이어가 평균 1개꼴" 수준. BalanceSim에 모디파이어 조합 시뮬 추가해
관통+처치폭발 등 조합 DPS 폭주 여부 확인. dotnet test 그린. 완료 기준은 커밋까지다.

### GROK 응답 (2026-07-29, content)

**완료 — 전부 잠정(AGENTS.md §7). 사람 플레이 피드백 전 최종 확정 금지.**

`GameData/rewards.json`에 modifier 4종 추가 (카탈로그 9 → **13**):

| id | type | modifierId | weight | stage | maxPerRun |
|---|---|---|---:|---|---:|
| `mod_pierce_shot` | modifier | pierce_shot | **2** | 1–99 | **1** |
| `mod_ricochet` | modifier | ricochet | **2** | 1–99 | **1** |
| `mod_homing_missile` | modifier | homing_missile | **2** | 1–99 | **1** |
| `mod_kill_explosion` | modifier | kill_explosion | **2** | 1–99 | **1** |

**가중치 근거 (가이드: 3택 평균 모디파이어 ≈1)**

| 구간 | 총 weight | 모디파이어 weight | E[mods in 3-pick] (복원 근사) |
|---|---:|---:|---:|
| stage 1 | 20 (기존 12 + 8) | 8 | **≈1.20** |
| stage 2+ | 23 (기존 15 + 8) | 8 | **≈1.04** |

초반(stage 1)부터 등장해 빌드 방향을 조기에 고정. 동일 모디파이어 중복 무의미 → `maxPerRun: 1`.

**BalanceSim 조합 검증 (밀집 팩 12기 HP1, spacing 0.5u, Core 기본 튜닝)**

| 시나리오 | clearTicks | kills/s (proxy) | vs baseline |
|---|---:|---:|---|
| none | 107 | 6.7 | 1.00× |
| pierce | 59 | 12.2 | 1.81× |
| kill_explosion | 34 | 21.2 | 3.15× |
| pierce+explosion | 26 | 27.7 | **4.12×** |

- 콤보 vs 최강 단독(kill_explosion): **×1.31** — 초승산은 완만.
- baseline 대비 ≥4× soft WARN 발화. 주원인은 밀집 저HP 팩에서의 **처치폭발 단독 강함**
  (폭발 dmg 2 / radius 2u 기본값). 관통 자체보다 폭발 파라미터가 우선 튜닝 후보.
- 연쇄 폭발은 Core가 금지(폭발 킬 재폭발 없음) — 폭주는 관통이 추가 킬 시드만 여는 형태.
- Core config 수치(`KillExplosionDamage` 등)는 GameData 미이관. 조정 필요 시 CODEX/사람에게 요청.

**테스트 동기화:** `GameDataParserTests.RepositoryApprovedV2Files_ParseCompletely`
`Rewards.All.Count` **9 → 13** (카탈로그 확장 패턴).

**검증:** `Tools/CoreStandalone` `dotnet test` **155/155** · `Tools/BalanceSim` **PASS**.

**CLAUDE 후속:** `Assets/Resources/GameData/rewards.json` 동기화 + 보상 UI에 modifier 4종 표시명.

## [ ] REQ-015 → CODEX: 그레이즈 + 콤보 배율 스코어링 (아케이드 깊이)

결정론 코어를 활용한 스코어링 심화. 오케스트레이터 잠정 설계(§7) — 수치는 GROK이 후속 확정.

1. 그레이즈(graze): 적탄이 플레이어 히트박스에 맞지 않고 근접 반경(잠정: 히트박스 반경
   +128 서브유닛) 안을 지나가면 1회 가산. 같은 탄은 1회만 그레이즈 가능(탄별 플래그).
   피격 판정과 같은 틱이면 피격이 우선.
2. 콤보 배율: 처치마다 배율 게이지 증가, 잠정 단계 x1→x2→x4→x8 (단계당 필요 킬 수
   증가), 일정 틱(잠정 300틱) 동안 킬 없으면 1단계 하락, 피격 시 x1로 리셋.
   격파 점수에 배율 적용. 그레이즈는 소량 고정 점수 + 배율 게이지 소폭 충전.
3. 이벤트: GrazeScored(탄 좌표), MultiplierChanged(EntityId=새 단계) 추가 — HUD/이펙트용.
4. RunStatistics에 GrazeCount 추가. TotalScore 오버플로 주의(기존 Damage.Compute 교훈).
5. 결정론·무할당 가드 유지, config 필드 노출(잠정 표기), 회귀 테스트
   (그레이즈 1회 제한, 배율 상승/하락/리셋, 점수 적용, 결정론).

## [x] REQ-016 → CODEX+GROK: 스코어링 수치 데이터화 (GameData/scoring.json)

REQ-015의 그레이즈/콤보 수치가 Core config 기본값에만 있어 GROK이 튜닝할 수 없다
(미사일 최소 간격 REQ-010과 같은 구조 문제의 예방).
CODEX: GameData/scoring.json 신설 파싱 — grazeRadiusSubUnits, grazeScore,
grazeGaugeCharge, multiplierGaugeRequirements[], multiplierDecayTicks.
GameDataParser에 선택 인자(부재 시 현행 기본값), GameDataSet.ApplyTo에서 config 복사.
회귀 테스트 포함(부재 폴백/명시값/검증). Unity NUnit 호환 API만 사용(Assert.Multiple 금지).
GROK: 파서 완료 후 scoring.json 초기값 작성 + BalanceSim 그레이즈/콤보 점수 곡선 검증(잠정 §7).

### CODEX 응답 (sim, main 병합됨 — f23b565)

파서 선택 6번째 인자 `scoringJson`, `ScoringDefinition` → `GameDataSet.ApplyTo` 복사,
부재 시 Core 기본값 폴백, 회귀 테스트 포함.

### GROK 응답 (2026-07-29, content)

**완료 — 전부 잠정(AGENTS.md §7). 사람 플레이 피드백 전 최종 확정 금지.**

`GameData/scoring.json` schemaVersion 1 신설. Core 기본값을 출발점으로 채택:

| 필드 | 값 | 근거 |
|---|---:|---|
| `grazeRadiusSubUnits` | **128** (0.5u) | 히트박스 외곽 +0.5u 근접 그레이즈. 스킬 보상 반경. |
| `grazeScore` | **10** | 고정 점수(배율 미적용). min kill 60 대비 16.7%. |
| `grazeGaugeCharge` | **1** | 소폭 게이지. 그레이즈 단독 x8 도달 160회. |
| `multiplierGaugeRequirements` | **[30, 50, 80]** | 킬 게이지+10 기준 x2=3킬 / x4=8킬 / x8=16킬. |
| `multiplierDecayTicks` | **300** (5.0s) | 킬 없을 때 1단계 하락. 전투 유지 압박, AFK 불가. |

**BalanceSim 곡선 검증**

- kills-to-x8=**16** (band 8–40), decay=**300** (band 120–600) → x8 유지 난이도 적절.
- graze/minKill=**0.167** ≤0.25; x8 최저킬 상쇄에 그레이즈 **48회** 필요.
- 60s 스케치(1킬/2s + 3그레이즈/s): grazeShare=**13.8%** <40% — 파밍이 격파 점수 미압도.
- 그레이즈 등반이 킬 등반 대비 **10×** 느림. Core 규칙: 그레이즈는 감쇠 타이머를 리셋하지 않음.
- 스모크: scoring.json 값이 BattleSim 그레이즈 점수/게이지·킬 배율에 실적용.

**CLAUDE 후속:** `Assets/Resources/GameData/scoring.json` 동기화 + BattleDirector/Hangar 파서 6인자 전달.

**검증:** `Tools/CoreStandalone` `dotnet test` 그린 · `Tools/BalanceSim` **PASS**.

## [x] REQ-017 → CODEX: 런 중단 저장 (스테이지 경계 서스펜드/리줌)
## [x] GROK → CODEX/CLAUDE: 최대 탄밀도 스트레스 검증 (2026-07-29)

**임무:** stage 5(core) 최악 적탄 밀도 + 풀파워 플레이어 탄 vs Core `MaxEnemyBullets`/`MaxBullets`.
수치 **변경 없음** (한도는 CODEX 소유, 웨이브 편성 조정은 권고만).

### GROK 응답 (content, Tools/BalanceSim)

`CheckBulletDensityStress` 추가. 검증: `dotnet test` Tools/CoreStandalone **167/167** · BalanceSim **PASS** (overflow는 WARN).

#### Core 한도 (CreateDefault)

| Cap | 값 |
|---|---:|
| `MaxEnemyBullets` | **32** |
| `MaxBullets` | **64** |

#### (1) Stage 5 core 적탄

- 테마 ordinal stage5 = `core`, boss = `boss_core` phase2: interval **34t**, ways **9**, speed **12.5 u/s**, travel≈35u → life≈**168t**, steady volleys **5** → boss alone theo **45**.
- 최악 세그먼트 `seg_core_final_gauntlet`: peak enemies **17**, peak shooters **13** (no-kill 수명 모델).
- 이론 동시 적탄 (densest shooters + boss p2 동시 가정):
  - faithful (fodder 1-way, Core 실측): fodder **48** + boss **45** = **93**
  - stress (전 슈터 9-way 가상): **477**
- Headless (cap 512로 상향, 플레이어 히트박스 0):
  - 생성 시드 피크: **31** (headroom +3.1% vs 32)
  - densest core×3: **31** / phase2 window **23**
  - **boss-only lab phase2 hold: 41** (theo 45에 근접) → **cap 32 초과, headroom -28.1%**

**권고 (수치 미적용):**

| 우선 | 내용 |
|---|---|
| Primary | `MaxEnemyBullets` **>= 57** (peak~45 boss p2, +25% headroom) |
| Upper | **>= 117** if residual turrets co-fire with p2 (theo 93) |
| Extreme | **>= 597** only if Core adds multi-way to fodder |
| waves.json | `boss_core` p2 ways 9→7 or interval 34→45; thin `seg_core_final_gauntlet` shooters |

Silent drop at cap = 위협 누락 (크래시 아님). CLAUDE 풀은 Presentation 풀 크기와 동기화 필요할 수 있음.

#### (2) 플레이어 풀파워 (Main5 / Mis3 / Opt4 + pierce+ricochet)

- main interval **5t**, beams/volley **5**, life≈102t → main concurrent **105**; missile **11** → no-mod theo **116**.
- pierce×ricochet lifetime uplift (soft): theo **~235**.
- Headless elevated MaxBullets=512: peak **106** @tick 101.

**권고 (수치 미적용):**

| 우선 | 내용 |
|---|---|
| Primary | `MaxBullets` **>= 145** (peak~116, +25%) |
| Uplift | **>= 294** if pierce+ricochet lifetime packing is budgeted |
| Alternate | option max 하향 / fire interval 상향 / 기존 cap 근처 deterministic volley drop 유지 |

#### CODEX 후속

- `BattleSimConfig.CreateDefault` (및 RunManager/BattleDirector 주입값) `MaxEnemyBullets`/`MaxBullets` 상향 검토.
- 권고 1차: enemy **57+**, player **145+** (또는 웨이브 쪽 밀도 완화로 적탄 압력 흡수).

#### CLAUDE 후속

- 탄 풀 프리팹/풀 크기가 Core 한도를 반영하는지 확인 (한도 상향 시 Presentation 동기화).

**검증 명령:** `cd Tools\BalanceSim && dotnet run` · `cd Tools\CoreStandalone && dotnet test`.

---

## [ ] REQ-017 → CODEX: 런 중단 저장 (스테이지 경계 서스펜드/리줌)

상용 로그라이트 표준 기능. 결정론 덕에 전체 상태 직렬화 대신 스테이지 경계 스냅샷으로 충분.
- RunManager.ExportSuspendData(): 스테이지 시작 시점 기준 — 시드, 런 번호, 스테이지 인덱스,
  점수, 통계, 게이지/HP/실드, 패시브 보상 획득 이력(획득 카운트 포함), ActiveModifiers,
  함선 id. 직렬화 가능한 평범한 데이터 클래스(MetaStateData 패턴).
- RunManager 리줌 생성자/팩토리: 데이터로부터 해당 스테이지 시작 상태를 재구성.
  같은 시드+스테이지의 StagePlan이 재현되고 이후 진행이 결정론적이어야 한다.
- 중단 시점이 스테이지 중간이면 그 스테이지 처음부터 재개(체크포인트 관례) — 문서화.
- 회귀 테스트: export→resume 라운드트립, 리줌 후 N틱 진행 == 연속 플레이 N틱(동일 스테이지
  시작 기준), 데이터 손상 시 안전 거부. Unity NUnit 호환 API만(Assert.Multiple 금지).
파일 저장/로드와 타이틀 CONTINUE UI는 CLAUDE 몫 (MetaSave 패턴 재사용).

### CODEX 응답 (2026-07-29, sim)

- `RunSuspendData`/`RewardAcquisitionData` 직렬화 DTO와
  `RunManager.ExportSuspendData()`/`ResumeFromSuspendData(...)` 팩토리를 추가했다.
- 매 스테이지 tick 0의 시드·런/스테이지·누적 점수/통계·게이지 커서/레벨·HP/실드·
  보상 획득 카운트·ActiveModifiers·함선 id·패시브 전투 튜닝을 캡처한다.
  스테이지 중간 Export도 현재 틱이 아닌 해당 스테이지 시작 스냅샷을 반환한다.
- 리줌은 StagePlan 생성 전에 스키마, 수치 범위, 스테이지/통계 관계, 함선,
  게이지, 실드, 보상 id/순서/maxPerRun, modifier 비트를 검증하고 손상 데이터는 거부한다.
  `AwaitingReward`/`RunOver`는 완전한 경계 상태가 아니므로 Export를 거부한다.
- 경계 캡처 배열은 생성 시 한 번 할당해 재사용한다. 할당이 허용된 Export만 방어적
  배열/DTO 복사를 만든다.
- 회귀 테스트: export→resume 라운드트립, 같은 시드+스테이지 StagePlan,
  중간 Export→리줌 후 90틱 상태 해시와 연속 플레이 일치, 손상 데이터의 생성 전 거부,
  보상 카운트/패시브/수정자 복원.

검증: `Tools/CoreStandalone` `dotnet test --no-restore` **173/173 통과**.
샌드박스가 사용자 전역 NuGet.Config 읽기를 차단해 자동 restore 단계는 실행할 수 없었으며,
기존 복원 자산을 사용한 빌드·전체 테스트는 그린이다.

## [x] REQ-018 → CODEX: 데일리 시드 규칙 + 입력 녹화/재생 (결정론 리플레이)

결정론 자산의 기능화. 두 부분:
1. DailySeed: 날짜(UTC 기준 yyyy-MM-dd 정수화)를 시드로 바꾸는 순수 함수
   (예: FNV-1a 해시, 정수 연산만). 같은 날짜 → 전 세계 같은 시드. 날짜는 호출자가
   int(yyyymmdd)로 주입 — Core는 시계를 읽지 않는다(§4 환경 의존 금지).
2. InputRecorder/InputPlayback: RunManager.Step에 들어가는 InputCommand 시퀀스를
   압축 기록(변화 시점만 기록하는 런렝스 방식 권장 - 8방향+발사라 엔트로피 낮음)하고,
   직렬화 가능한 DTO(RunSuspendData 패턴)로 내보내기/재생. 재생은 기록된 틱 수만큼
   Step에 명령을 공급하는 열거자. 시드+입력 → 동일 런 재현이 목적.
   회귀 테스트: 기록→재생 전체 상태 해시 일치(DeterminismAuditHasher 활용),
   런렝스 왕복, 빈 기록/손상 거부. 무할당: 기록 버퍼는 증폭 재할당 허용(게임 루프 밖
   Export 시점만 할당), 틱당 기록은 무할당. Unity NUnit 호환 API만.
파일 저장·재생 UI·데일리 메뉴는 CLAUDE 몫.

## [ ] REQ-019 → CODEX: 게이지 활성화를 InputCommand로 편입 (리플레이 무결성)

발견: 파워업 게이지 활성화가 DevCheats F10(dev 치트)에서 Presentation이 Gauge.Activate()를
직접 호출하는 경로뿐이다. 정식 입력이 없고, 시뮬 입력 스트림 밖에서 상태를 바꾸므로
REQ-018 입력 리플레이가 활성화를 재현하지 못한다.
요청: (1) InputCommand에 activate(bool) 필드 추가 - 기존 3인자 생성자 호환 유지
(2) RunManager/BattleSim Step이 activate 상승 에지에서 게이지 활성화를 수행
(3) Presentation의 직접 Activate 호출 경로는 유지하되(dev 치트) 주석으로 리플레이
비기록임을 명시 (4) REQ-018 레코더가 activate를 포함해 기록하도록 갱신
(5) 회귀 테스트: activate 포함 기록→재생 해시 일치, 파워업 레벨 변화 재현.
### CODEX 응답 (2026-07-29)

완료:

- `DailySeed.FromDate(int yyyymmdd)`를 추가했다. Core는 시계를 읽지 않으며,
  유효한 그레고리력 날짜를 검증한 뒤 명시적 리틀엔디언 32-bit FNV-1a로 `ulong`
  런 시드를 반환한다.
- `InputRecorder`는 생성 시 예약한 값 타입 run 버퍼에 동일한 `InputCommand`를
  런렝스로 합친다. 성공하는 `Record` 경로는 할당이 없고, 용량 초과는 기록을
  변경하지 않고 거부한다. DTO 할당은 `Export()`에서만 발생한다.
- `[DataContract]` 기반 `InputRecordingData`/`InputRunData` DTO와
  `InputPlayback` 값 타입 열거자를 추가했다. Playback은 DTO를 검증·복사하며
  스키마, 빈 기록, null run, 디지털 범위 밖 입력, 0 이하 run 길이, 틱 합계
  불일치/오버플로, 인접 중복 run을 손상으로 거부한다.
- JSON 직렬화 왕복, 런렝스 왕복, DTO 스냅숏 독립성, 전체 RunManager 상태 궤적
  해시 일치, 빈 기록/손상 거부, 용량 초과 불변성, 레코더 재사용을 회귀 테스트로
  고정했다. 독립 할당 계측에서 변화 경계를 포함한 `Record`는 0바이트였다.

검증: `Tools/CoreStandalone`의 `dotnet test` **188/188 통과**.

## [x] REQ-020 → CODEX: 난이도 선택 배율 주입 경로 (이지/노멀/하드)

타이틀에서 난이도를 고르는 상용 표준 기능. 기존 MetaProgression(배율) 훅을 활용한다.
1. RunManager 7인자 ctor(rewards, ship 포함)에 난이도 배율을 받는 오버로드 추가
   (기존 호출 호환 유지). 배율은 유리수(분자/분모 정수)로 — §4 부동소수점 금지 확인
   (MetaProgression이 double이면 정수 유리수로 대체 검토, 기존 동작 보존).
2. RunSuspendData와 InputRecordingData(또는 리플레이 래퍼가 쓸 수 있게 RunManager
   Export)에 난이도가 보존되어 CONTINUE/REPLAY가 같은 난이도로 재현되게.
3. 적용 지점은 CODEX 판단(적 HP/대미지/등장 밀도 중 HP 중심 권장), 잠정 §7 표기.
4. 회귀 테스트: 배율별 결정론, 리줌/기록 재현에 난이도 반영. Unity NUnit 호환 API만.
GROK 후속: 프리셋 수치(easy/normal/hard, 잠정). UI는 CLAUDE.

### CODEX 응답 (2026-07-29, sim)

- 기존 rewards+ship 생성자를 유지하고, 정수 유리수 난이도 분자/분모를 받는
  `RunManager` 오버로드를 추가했다. 배율은 생성 시 축약되며 공개 속성으로 노출된다.
- 적용 범위는 잠정 밸런스(AGENTS.md §7)로 일반 적과 보스의 최대 HP만이다.
  HP 계산은 정수 ceil이며 오버플로 시 `int.MaxValue`로 포화한다. 기본 1/1은 기존 동작과 같다.
- `RunSuspendData` schema 2와 `InputRecordingData` schema 3에 축약 배율을 저장한다.
  이전 suspend schema 1 / recording schema 2는 1/1로 호환 로드한다.
  `InputRecorder(RunManager)`와 `InputPlayback`의 배율 속성으로 리플레이 생성 경로를 열었다.
  현재 `BattleDirector`가 재생 배율을 RunManager에 넘기도록 하는 Presentation 연결은
  소유 경계상 `Reviews/from-codex/requests.md`에 후속 요청으로 남겼다.
- `MetaProgression`의 실제 승계 계산을 `CarryNumerator/CarryDenominator` 기반 정수 연산으로
  교체했다. 기존 `double` 생성자는 호출 호환용 변환 경계로 유지되며 시뮬 계산에는
  부동소수점을 사용하지 않는다.
- 배율별 결정론·일반 적/보스 HP·기존 생성자 1/1 호환·서스펜드 리줌 궤적·입력 기록
  리플레이 해시·구 스키마 호환·손상 배율 거부 회귀 테스트를 추가했다.

검증: `Tools/CoreStandalone`의 `dotnet test --no-restore` **202/202 통과**.

---

## [x] REQ-021 → GROK(데이터): 적 이동 패턴 배정 (CODEX 파서 완료 전제)

**GROK 응답 (2026-07-29, content):** 완료 — `enemies.json` schemaVersion **3**, dive/dash/zigzag **12/30** 배정.
상세·검증은 `Reviews/from-grok/requests.md` 동명 항목. 전부 잠정(§7).

## [ ] REQ-021 → CODEX: 캡슐 스크롤 드리프트 + 적 이동 패턴 확장 (사람 플레이 피드백)

사람 데모 시청 피드백 2건 (2026-07-29):
1. **캡슐이 제자리에 떠 있어 언제든 먹을 수 있다** — 월드 스크롤과 함께 왼쪽으로
   흘러가야 한다. 스크롤 속도(ScrollSpeed) 기준 드리프트 + 가벼운 사인 보브(선택).
   화면 밖으로 나가면 소멸. 놓치는 긴장감이 설계 의도다.
2. **적 이동이 단조롭다** — 현재 straight/sine/static 위주. 신규 패턴 3종을 데이터
   주도로 추가하라(enemies.json movement 필드 확장, GROK이 배정 예정):
   - dive: 진입 후 플레이어 Y를 향해 한 번 급강하/급상승 후 직진 이탈
   - zigzag: 큰 진폭 사선 왕복(사인보다 각진 삼각파)
   - dash: 정지 → 짧은 돌진 → 정지 반복 (예측 가능한 텔레그래프)
   전부 정수 연산·SineLut/유리수 속도(§4), 기존 패턴 하위 호환, 파서 검증.
회귀 테스트: 패턴별 궤적 결정론, 캡슐 드리프트·소멸, 구 데이터 호환.
잠정 §7. Unity NUnit 호환 API만.

## [x] REQ-022 → GROK(데이터): ships.json weaponType/maxHp (CODEX 파서 완료 전제)

**GROK 응답 (2026-07-29, content):** 완료 — starter=vulcan/HP3, interceptor=laser/HP2, bulwark=spread/HP5.
BalanceSim 단타 DPS 비 1.47. 상세는 `Reviews/from-grok/requests.md`. 잠정(§7).

## [ ] REQ-022 → CODEX: 주무기 3계열 (vulcan / laser / spread)

사람 피드백: 무기 종류가 적다. 함선 차별화와 묶는다 — ships.json에 weaponType 필드,
함선마다 주무기 계열이 다르다(행거 선택의 실질 가치).
- vulcan: 현행 기본탄 (기준)
- laser: 가늘고 빠른 관통탄(관통 2, 연사 느림, 대미지 높음) — pierce 모디파이어와
  중첩 시 관통 수 합산
- spread: 3-way 부채꼴(way당 대미지 낮음, 커버 넓음) — n-way 로직 재사용
weapons.json에 계열별 정의(GROK 후속), 파워업 레벨 스케일은 계열별로 동작.
BattleSim 발사 로직 분기, 이벤트/통계 호환, 리플레이·서스펜드에 자연 포함(함선 id 경유).
회귀 테스트: 계열별 거동·결정론·데이터 폴백(weaponType 부재 시 vulcan).
잠정 §7. Unity NUnit 호환 API만.

### REQ-022 보강 (사람 지시 2026-07-29): 기체 3종 컨셉 확정 — 밸런스/스피드/탱커

- starter = 밸런스: vulcan, 표준 속도, 시작 HP 3
- interceptor = 스피드: laser, 빠른 이동(기존 배율), 시작 HP 2 (유리 대포)
- bulwark = 탱커: spread, 느린 이동, 시작 HP 5
ships.json에 weaponType과 maxHp 필드 추가 파싱(부재 시 vulcan/3 폴백).
BattleSimConfig.PlayerMaxHp가 함선별로 덮이도록. 수치 잠정 §7, GROK 확정 후속.

## [x] REQ-023 → GROK(데이터): waves.json obstacles 배치 (CODEX 시스템 완료 전제)

**GROK 응답 (2026-07-29, content):** 완료 — stage1-capable 세그먼트 비움, stage2+ 점진 2→7.
solid 통로 + breakable 파밍. BalanceSim corridor/stage1 empty PASS. 상세는 from-grok. 잠정(§7).

## [ ] REQ-023 → CODEX: 스테이지 장애물 시스템 (사람 지시 2026-07-29)

"스테이지마다 전용 기믹 — 1스테이지는 평범, 나머지는 장애물 조금씩."
1. Obstacle 엔티티: 월드 스크롤과 함께 왼쪽 이동, 사각 히트박스, 플레이어 접촉 시
   피해(적 충돌과 동일 규칙). 두 계열:
   - solid: 파괴 불가, 플레이어 탄을 막는다(탄 소멸)
   - breakable: HP 보유, 격파 가능(소량 점수), 적탄은 통과(플레이어만 유불리 비대칭 방지
     여부는 CODEX 판단 - 결정 기록)
2. waves.json 세그먼트에 obstacles 배열(type, x, y, 필요시 hp) — 데이터 주도, 부재 시
   없음(하위 호환). 스테이지 1은 GROK이 비워 둔다.
3. 이벤트: ObstacleDestroyed(좌표) — 표현용. 뷰 동기화용 읽기 전용 목록 노출
   (Bullets/Enemies 패턴).
4. 결정론·무할당·풀 상한(MaxObstacles config), 회귀 테스트. 잠정 §7.
   Unity NUnit 호환 API만. GROK: 테마별 배치(hive 포자기둥/fortress 장갑블록/
   nebula 크리스탈/core 혼합, 밀도 점진 증가). CLAUDE: 테마별 스프라이트·뷰 풀.

## [ ] REQ-024 → CODEX: EnemyKilled 이벤트 Arg를 부여 점수로 (점수 팝업용)

현재 EnemyKilled.Arg는 데미지인데 소비처가 없다. 배율 적용된 실제 부여 점수로 바꿔
Presentation이 +N 플로팅 팝업을 그릴 수 있게 하라. ObstacleDestroyed도 동일하게.
Arg 의미 변경은 주석/독스트링 갱신, 관련 테스트 조정. 소규모.

---

## [x] REQ-025 → CODEX: 테마 순서 시드 셔플 (로그라이크化 1단계)

오케스트레이터 진단(2026-07-29): 현재 테마는 `(stageIndex-1) % themes.Count`로 계산돼
어떤 시드든 scrapyard→hive→fortress→nebula→core 고정이다. 보스도 테마당 1개라
런의 뼈대가 100% 예측 가능 — "다음 스테이지 기대감"이 구조적으로 0.

요청:
1. SegmentStageGenerator.SelectTheme을 시드 기반 순열로 교체.
   - 스테이지 1은 themes[0] 고정 (온보딩 일관성)
   - 스테이지 2 이후는 나머지 테마를 런 시드로 결정론 셔플(Fisher-Yates, 정수 Rng)
   - 순열은 런당 1회 결정되고 모든 스테이지에서 일관돼야 한다(스테이지마다 재추첨 금지).
     StagePlan 생성이 stageIndex별 독립 호출이므로, 런 시드에서 순열을 유도하는
     순수 함수로 만들어 같은 시드+stageIndex면 같은 테마가 나오게 하라.
   - 5스테이지를 넘어가면(2회차 대비) 순환 규칙은 CODEX 판단, 결정론만 유지.
2. **안전 폴백 필수**: 셔플된 테마로 스테이지 조립이 불가능하면(해당 난이도에 그 테마
   세그먼트가 없거나 보스 미존재) 예외를 던지지 말고 조립 가능한 테마로 결정론적
   대체하라. GROK이 REQ-026으로 전 난이도 커버를 채우는 중이지만, 데이터가 불완전해도
   런이 깨지면 안 된다. 대체가 일어났음을 StagePlan이나 로그로 관측 가능하게.
3. 리줌/리플레이 재현: 런 시드 경유로 자동 재현되는지 테스트로 확인.
4. 회귀 테스트: 시드별 순열 분포(같은 시드=같은 순열, 다른 시드=다른 순열이 실제로
   발생), 스테이지1 고정, 폴백 동작, 결정론.
잠정 §7, Unity NUnit 호환 API만.

## [x] REQ-026 → GROK: 테마 전용 세그먼트 증량 + 전 난이도 커버 (로그라이크化 1단계)
### CODEX 응답 (2026-07-29, sim)

- `themes[0]`은 스테이지 1에 고정하고, 나머지는 런 시드 전용 `Rng` 스트림의
  Fisher-Yates 순열로 결정하도록 교체했다. `Generate(seed, stageIndex, difficulty)`가
  독립 호출되어도 같은 런 순서를 다시 유도하며, 5개 테마 이후에는 같은 전체 순열을
  순환한다.
- 최초 선택 테마가 현재 스테이지/난이도/경로/보스 조건으로 조립 불가능하면 런 순열의
  다음 테마부터 순환 탐색해 최초 조립 가능 테마로 결정론적 대체한다. 모두 불가능한
  카탈로그만 기존처럼 예외를 낸다.
- `StagePlan.RequestedThemeId`와 `ThemeFallbackApplied`를 추가했다. `ThemeId`는 실제
  조립에 사용된 테마라 Presentation 배경 선택 호환을 유지하며, 결정론 감사 해시에는
  요청 테마도 포함된다.
- 스테이지 1 고정, 시드별 순열 다양성/동일 시드 재현, 독립 stageIndex 호출 기반
  리줌·리플레이 재현, 폴백 관측/결정론 회귀 테스트를 추가했다.

검증: `Tools/CoreStandalone`의 `dotnet test --no-restore` **236/236 통과**.
자동 restore는 샌드박스 밖 사용자 `NuGet.Config` 읽기 권한 때문에 차단됐다.

## [ ] REQ-026 → GROK: 테마 전용 세그먼트 증량 + 전 난이도 커버 (로그라이크化 1단계)

진단 수치: 테마 전용 세그먼트가 테마당 2개뿐이고 나머지는 공용이라, 하이브에서도
스크랩야드 세그먼트가 그대로 나온다. 스테이지 1은 후보가 3개(전부 공용)라 시드를
바꿔도 거의 같은 판이 나온다.

요청:
1. 테마 전용 세그먼트를 **테마당 5~6개**로 증량(현재 2개 → 총 25~30개 목표).
   각 테마의 개성이 드러나게: scrapyard=파편/좁은 통로, hive=포자 무리/유기적 파상,
   fortress=포탑 격자/장갑 통로, nebula=고속 위습/시야 교란형 배치, core=혼합 정예.
2. **REQ-025(테마 셔플)와 짝**: 테마 순서가 섞이므로 각 테마 세그먼트가
   difficulty 2~5 전 범위를 커버해야 한다(예: hive가 스테이지 4에 올 수도 있음).
   테마별로 난이도 구간을 나눠 세그먼트를 배치하라. 스테이지 1(difficulty 1)은
   themes[0] 고정이므로 그 테마만 difficulty 1을 커버하면 된다 —
   대신 **스테이지 1 후보를 3개 → 6개 이상**으로 늘려라.
3. 보스도 테마 셔플 대상이므로 stageIndexMin/Max와 difficulty 범위가 어느 순서에서도
   유효한지 점검하라(현재 boss_core는 stage 5 이후만 가능할 수 있음 — 셔플 시 조립
   실패를 유발한다면 범위를 넓혀라. 보스 HP는 스테이지 난이도로 스케일되므로 잠정).
4. 검증: BalanceSim에 "모든 테마 × difficulty 1~5 조합에서 스테이지 조립 가능"
   전수 검사를 추가하라. 난이도 단조 증가 곡선은 스테이지 인덱스 기준으로 유지.
잠정 §7. dotnet test와 BalanceSim 그린. 완료 기준은 커밋까지다.

### GROK 응답 (2026-07-29, content)

**완료 — 전부 잠정(AGENTS.md §7). 사람 플레이 피드백 전 최종 확정 금지.**

`GameData/waves.json` 세그먼트 **16 → 38**, 테마 전용 **2 → 6/테마** (목표 5–6).

| 테마 | 전용 세그먼트 (신규 포함) | 개성 |
|---|---|---|
| scrapyard | debris_line / pipe_dash / skimmer_weave / junk_corridor / tumbler_pack / rust_gauntlet | 파편·파이프 대시·좁은 잔해 통로 |
| hive | spore_cloud / lancer_rush + brood_wave / hornet_dive / organic_pulse / nest_choke | 포자 무리·호넷 급강하·유기 파상 |
| fortress | sentry_grid / interceptor_assault + mortar_line / turret_cross / drone_lattice / armored_gate | 포탑 격자·박격 라인·장갑 게이트 |
| nebula | wisp_storm / wisp_ribbon + echo_ribbon / void_moth_swarm / crystal_drift / prism_haze | 고속 위습·보이드 모스·크리스탈 |
| core | guardian_wall / final_gauntlet + rift_blades / phase_discs / shard_battery / void_mix | 리프트 칼날·페이즈 디스크·혼합 정예 |

**난이도 커버 (REQ-025 셔플 대비)**

- 기존 테마 세그먼트 `difficultyMin` 하향: fortress/nebula **3→2**, core **4→2** (전 테마 diff 2–5).
- 테마별 신규 세그먼트를 2–4 / 2–5 / 3–5 대역으로 분산.
- **스테이지 1 후보 3 → 6**: 공용 3 + scrapyard d1 3종 (`debris_line`, `pipe_dash`, `skimmer_weave`, 장애물 없음).

**보스 범위**

| 보스 | before stage | after stage | diff |
|---|---|---|---|
| boss_stage1 | 1–99 | **1–99** | 1–5 |
| boss_hive | 2–99 | **1–99** | 1–5 |
| boss_fortress | 3–99 | **1–99** | 1–5 |
| boss_storm | 4–99 | **1–99** | 1–5 |
| boss_core | 5–99 | **1–99** | 1–5 |

HP 미변경 (스테이지 난이도 스케일 전제 유지, 잠정).

**스테이지 인덱스 avgHP 단조 (theme=ordinal, diff=stage)**

| Stage | Theme | avgHP |
|---:|---|---:|
| 1 | scrapyard | **141** |
| 2 | hive | **189** |
| 3 | fortress | **381** |
| 4 | nebula | **416** |
| 5 | core | **613** |

**BalanceSim**

- `CheckThemeDifficultyCoverage` 추가: 테마 전용 ≥5, stage1 후보 ≥6, 보스 전 stage×diff,
  **theme 강제 × diff 1–5 × 8 seeds = 200** 조립, 스테이지 avgHP 단조.
- 기존 stage 1–10 × diff 1–5 (50) 조립 유지.

**테스트 동기화:** `GameDataParserTests` Segments.Count **16 → 38**.

**검증:** `Tools/CoreStandalone` `dotnet test` **234/234** · `Tools/BalanceSim` **PASS**.

## [x] REQ-027 → CODEX: 스테이지 내 세그먼트 중복 방지 (진단 덤프에서 발견)

REQ-025 검증 중 발견: 세그먼트가 매 포지션 독립 균등 추첨이라 같은 세그먼트가
한 스테이지에 반복된다. 실측 예 — seed 42 스테이지1 = [skimmer_weave × 3],
seed 20260729 스테이지1 = [intro_line × 3]. 다양성 체감을 크게 해친다.

요청: 한 스테이지 조립 시 이미 쓴 세그먼트를 우선 제외하라.
- 1순위: 스테이지 내 유일(중복 없음)
- 후보가 부족해 조립 불가하면(look-ahead 실패 포함) 결정론적으로 중복을 허용하되,
  **직전 포지션과 같은 세그먼트는 최후까지 회피**(연속 반복이 가장 눈에 띈다)
- 완화가 일어났는지 관측 가능하게(카운터/플래그 등 CODEX 판단)
결정론 유지, 기존 clearability look-ahead와 함께 동작해야 한다.
회귀 테스트: 풀이 충분할 때 유일성 보장, 풀이 부족할 때 조립 성공 + 연속 중복 회피,
결정론. Unity NUnit 호환 API만. dotnet test 전체 그린.

### CODEX 응답 (2026-07-29, sim)

- 현재 위치의 미사용 후보가 남은 모든 위치와 보스까지 중복 없이 완주할 수 있는지
  clearability look-ahead로 먼저 검사한다. 완전 유일 경로가 없을 때만
  미사용 후보 → 사용했지만 직전과 다른 후보 → 직전과 같은 후보 순으로 결정론적으로
  완화한다.
- `StagePlan.SegmentReuseCount`와 `SegmentReuseApplied`를 추가해 완화 여부를
  관측 가능하게 했다.
- 충분한 풀의 전 구간 유일성, 2개 풀의 5구간 조립/인접 중복 회피, 단일 후보의
  최종 인접 중복 허용, 완화 경로 결정론을 회귀 테스트로 고정했다.
  실제 `waves.json`의 seed 42/20260729 stage 1도 중복 0을 확인한다.

검증: `Tools/CoreStandalone`의 `dotnet test --no-restore` **240/240 통과**.

---

## [ ] REQ-028 → CODEX: 경로 선택(맵 노드) + 조우 타입 (로그라이크化 2단계)

진단 후속: 테마가 시드로 섞이면서 "무엇이 나올지 모른다"는 생겼지만, 로그라이크의
핵심인 "내 선택으로 런이 갈린다"가 아직 없다. 보상 3택이 유일한 선택지다.

**설계 (오케스트레이터 확정, 수치는 잠정 §7)**

1. `EncounterType` 열거: Normal, Elite, Supply, Hazard.
2. `StagePlan`에 EncounterType 추가. 생성 시 타입별 변조:
   - Normal: 현행 (세그먼트 N개 + 보스)
   - Elite: 세그먼트 수 축소(잠정 1) + 미니보스급 강화 조우, 보스는 유지하되 CODEX가
     판단해 축약 가능. 클리어 보상은 **모디파이어 확정 등장**(RewardCatalog에 힌트 전달)
   - Supply: 전투 최소(세그먼트 1, 저난이도 편성) + 캡슐 다량 드롭. 보스 없음
   - Hazard: 세그먼트 수 유지 + 장애물 밀도 증가 + 격파 점수 보정(잠정 ×1.5)
   타입별 실제 적용 방식은 CODEX 재량 — 데이터로 뺄 수 있는 건 GROK 후속으로 넘겨라.
3. **경로 선택 흐름**: 스테이지 클리어 → 기존 AwaitingReward(보상 3택) → 새 상태
   `RunState.AwaitingRoute` → `RouteOptions`(2~3개, 각각 ThemeId + EncounterType) 노출
   → `ChooseRoute(int index)` → 다음 스테이지 생성.
   - 후보는 런 시드 + 스테이지 인덱스로 결정론 생성. 테마는 REQ-025 순열에서 뽑되
     후보끼리 서로 달라야 한다(가능한 범위에서).
   - 마지막 스테이지(또는 최종 보스 층)는 경로 선택 없이 진행 — 경계 규칙은 CODEX 판단.
4. **재현성**: RunSuspendData와 InputRecording에 경로 선택 이력을 포함해
   CONTINUE/REPLAY가 같은 경로를 재현해야 한다(보상 선택과 동일 패턴).
5. 결정론·무할당 가드 유지(후보 생성은 게임 루프 밖이라 할당 허용).
   회귀 테스트: 후보 결정론, 타입별 스테이지 변조, 리줌/리플레이 재현,
   경로 선택 없이 Step 호출 시 안전(진행 정지), 기존 데이터 하위 호환.
   Unity NUnit 호환 API만(Assert.Multiple 금지).

CLAUDE 후속: 경로 선택 UI. GROK 후속: 조우 타입별 데이터 튜닝.

---

## [ ] REQ-029 → CODEX: 세그먼트 가중치 + 희귀 조우 + 캡슐 자석

사람 플레이 후 완성도 마감 사이클. 세 건 (전부 잠정 §7).

1. **세그먼트 가중치**: waves.json 세그먼트에 선택 필드 weight(기본 10) 파싱.
   현재 균등 추첨이라 "가끔만 보는 특별한 편성"이 불가능하다. 가중 추첨으로 바꾸되
   REQ-027 유일성·clearability look-ahead와 함께 동작해야 한다. 결정론 유지.
2. **희귀 조우**: 경로 후보(REQ-028) 생성 시 낮은 확률로 특별 노드가 섞이게 하라.
   최소 구현으로 EncounterType에 Rare 하나 추가 — 세그먼트 가중치와 별개로,
   후보 슬롯 하나가 낮은 확률(잠정 12%)로 Rare가 되고, Rare는 고난도 편성 +
   보상 2개 동시 획득(또는 CODEX 판단의 강한 보상). 확률/보상은 config 노출.
3. **캡슐 자석**: 캡슐이 스크롤로 흘러가게 된 뒤 회수 난이도가 올랐다. 플레이어
   반경 내(잠정 3u) 캡슐이 플레이어 쪽으로 가속 이동하게 하라. 정수 연산·유리수 속도,
   무할당. config로 반경/속도 노출, 0이면 비활성(하위 호환).
회귀 테스트: 가중치 분포(고가중 세그먼트가 실제로 더 자주 뽑히는지), Rare 등장 확률,
자석 궤적 결정론. Unity NUnit 호환 API만. dotnet test 전체 그린.

---

## [x] REQ-031 → CODEX: 출시 차단 결함 3건 (퍼블리셔 심사 후속, 최우선)

세 심사관 합동 심사에서 NO-GO 판정. 오케스트레이터가 직접 검증한 차단 결함부터 해소한다.

### 1. 승리 조건 없음 (가장 심각)
RunState에 승리 상태가 없어 5스테이지를 클리어해도 6, 7, 8...로 무한히 이어진다.
런 완주라는 성취가 존재하지 않는다 — 로그라이크로서 게임이 미완성이다.
- RunState에 완주 상태 추가(예: RunCleared). 최종 스테이지 보스 격파 시 진입.
- 최종 스테이지 수는 config/데이터로(잠정 5). 넘어가면 완주.
- 완주 시 통계 확정, 이후 Step은 안전 정지. 메타 적립은 Presentation이 기존
  RunOver 경로와 동일하게 처리할 수 있도록 관측 가능하게.
- 2회차(루프)는 이번 범위 밖 — 단, 나중에 얹을 수 있게 구조만 열어 두어라.
- 리줌/리플레이가 완주 지점을 재현하는지 테스트.

### 2. 결정론 감사가 현재 HEAD에서 실패
`dotnet run --project Tools/DeterminismAudit -- --suite` 실행 결과:
`Scenario 'seed-0-first' completed only 0/4 stages`.
경로 선택(REQ-028) 도입 후 감사 도구가 ChooseRoute를 호출하지 않아 진행이 멈춘다.
회귀 안전망이 죽은 상태로 3개 사이클을 진행했다 — 즉시 복구하라.
- 감사 도구가 AwaitingReward와 AwaitingRoute를 모두 결정론적으로 소비하게 갱신
- 승리 상태(위 1번) 도달도 시나리오에 포함
- 감사 통과를 CI 대신 dotnet test에서도 강제할 수 있게 스모크 테스트 하나 추가 검토

### 3. 세이브 데이터 위험
- RunSuspendData/MetaStateData/InputRecordingData 스키마가 이미 3~4회 바뀌었는데
  **마이그레이션 경로가 없다.** 구 스키마를 읽어 현재 버전으로 승격하는 경로를 만들고,
  불가한 경우 명확히 거부(부분 손상 상태로 진행 금지).
- 데이터 무결성 검증용 체크섬 필드를 DTO에 추가(계산은 Core, 파일 IO는 Presentation).
- 파일 쓰기/교체 순서와 백업은 CLAUDE가 Presentation에서 처리한다.
회귀 테스트: 구 스키마 승격, 손상 거부, 체크섬 불일치 거부.
Unity NUnit 호환 API만. dotnet test 전체 그린 + 감사 suite 통과가 완료 조건이다.

### CODEX 응답 (2026-07-30, sim)

- `RunState.RunCleared`와 `RunProgressionConfig`를 추가했다. 기본 최종층은 5이며,
  최종 보스(및 보스 없는 호환 플랜의 최종층)를 클리어하면 보상/경로를 만들지 않고
  완주한다. `IsFinished`로 사망/완주 양쪽의 메타 정산 시점을 공통 관측할 수 있고,
  완주 후 `Step`은 상태·통계·Battle tick을 바꾸지 않는다. 최종층 설정은 서스펜드와
  입력 기록에도 보존되어 커스텀 캠페인 길이의 CONTINUE/REPLAY도 같은 완주점을 재현한다.
- 결정론 감사 suite가 `AwaitingReward`와 `AwaitingRoute`를 각각 결정론적으로 소비하고
  모든 시나리오에서 기본 5층 `RunCleared` 도달을 필수 검증하도록 복구했다. 동일 흐름을
  두 번 실행해 해시/선택 횟수/완주를 비교하는 NUnit 스모크 테스트도 추가했다.
- `SaveDataIntegrity`를 추가해 RunSuspend v1~v3, InputRecording v1~v4,
  버전 필드가 없던 MetaState v0을 현재 스키마로 깊은 복사 승격한다. 현재 DTO는
  정규 필드 순서의 64-bit FNV-1a 체크섬을 필수로 검증하며, 누락/불일치와 지원하지
  않는 버전은 상태 복원 전에 명확히 거부한다. 세 DTO의 Core export는 체크섬이
  채워진 현재 스키마만 생성한다.

검증: `Tools/CoreStandalone` `dotnet test --no-restore` **264/264 통과**.
`dotnet run --no-restore --project Tools/DeterminismAudit -- --suite`는
5개 시나리오 모두 **5/5, RunCleared**, cap-boundary 포함 `AUDIT PASS`.

---

## [ ] REQ-032 → CODEX: 바이옴/룸 계층 도입 (레벨 구조 재설계, 최대 규모)

레벨 디자인 실측: 런 총 3.6분(세그먼트 12.6초 × 3 = 37.7초 후 곧바로 보스).
보스가 38초마다 등장해 "구두점" 수준이고, 경로 선택이 런 전체에서 4회뿐이다.
스테이지 수를 늘리는 것만으로는 해결되지 않는다 — 계층이 없는 것이 원인이다.

### 목표 구조 (Hades 모델)
- Run = **5 바이옴** (테마 = 바이옴, 기존 셔플 유지)
- 바이옴 = **6 룸 + 바이옴 보스**
- 룸 = 기존 StagePlan 내용(세그먼트 3개) 그대로 재사용 — **보스 없는 룸**이 기본
- 보스는 **바이옴 마지막에만** (즉 룸 6개를 지나야 만난다)
- 룸 클리어마다 **경로 선택**(기존 RouteOptions/ChooseRoute 재사용, 룸 단위로 내림)
- **보상 3택은 바이옴 보스 격파 후 + 엘리트 룸 클리어 후에만** (룸마다 주면 인플레이션;
  엘리트를 고른 사람만 추가 보상을 받아 위험-보상 선택이 실제로 작동한다)
- 완주(RunCleared)는 **5번째 바이옴 보스 격파** 시
- 예상 런 길이: 30룸 × 38초 + 보스 5 × 40초 ≈ 22분

### 요구 사항
1. 인덱스 체계를 명확히: BiomeIndex(1~5)와 RoomIndex(1~6)를 분리 노출.
   기존 StageIndex를 어떻게 매핑할지는 CODEX가 정하되, 난이도 곡선은
   **바이옴 진행 기준**으로 유지하라(룸마다 난이도가 오르면 곡선이 망가진다).
2. 룸 수/바이옴 수는 config(RunProgressionConfig 확장)로. 잠정 6룸 × 5바이옴.
3. EncounterType은 **룸 타입**으로 재활용. 바이옴 보스 룸은 별도 취급.
4. RunSuspendData / InputRecordingData 스키마 갱신 + **기존 버전 마이그레이션 유지**
   (REQ-031에서 만든 경로를 확장하라). 이어하기는 룸 경계 체크포인트로.
5. 결정론 감사 갱신 — 5바이옴 × 6룸 완주까지 검증하고 AUDIT PASS를 유지하라.
6. 무할당 가드 유지. 회귀 테스트: 바이옴/룸 진행, 보스 등장 위치, 보상 지급 시점,
   리줌/리플레이 재현, 완주 판정.
규모가 크다 — 단계적으로 진행하고 중간에 테스트를 유지하라.
Unity NUnit 호환 API만. 잠정 §7.

## [ ] REQ-033 → GROK: 보스 전면 재설계 (TTK 40초, 3페이즈)
## [x] REQ-033 → GROK: 보스 전면 재설계 (TTK 40초, 3페이즈)

실측 결함: 초반 보스 3마리 TTK가 2~3초다(HP 1000/1300/1600, 중간 화력 500dps 기준).
등장 연출(WARNING 2.4초)보다 전투가 짧다. 후반만 4000/4500으로 올려 곡선이 깨졌다.
바이옴 구조(REQ-032)에서 보스는 룸 6개를 지나 만나는 이벤트가 되므로 무게가 필요하다.

1. 보스 5종 HP를 **목표 TTK 35~45초**로 재산정하라(바이옴 도달 시점 기대 화력 기준 —
   기존 analyze_stage_hp.py의 기대 화력 모델을 갱신해 쓰라). 곡선은 단조 증가.
2. **페이즈 2개 → 3개**로. 단순 수치 강화만으로는 40초가 지루하다:
   페이즈별로 탄 패턴 성격을 바꿔라(예: 조준 사격 → 확산 탄막 → 고속 소수탄).
   페이즈 전환 HP 임계는 데이터로 명시.
3. 페이즈별 이동 성격도 구분 가능하면 반영(현재 사인 호버 단일).
   Core가 지원하지 않는 항목은 권고로 남겨라.
4. BalanceSim으로 검산: 각 보스 TTK가 목표 구간에 들어오는지, 풀파워 기준으로도
   최소 12초 이상인지(즉발 격파 방지), 페이즈별 위협도가 단조 증가하는지.
잠정 §7. dotnet test와 BalanceSim 그린. 완료 기준은 커밋까지다.


### GROK 응답 (2026-07-30, content)

**완료 — 전부 잠정(AGENTS.md §7). 사람 플레이 피드백 전 최종 확정 금지.**

**(1) 보스 HP 곡선 (바이옴 6룸 도달 기대 화력 · 목표 TTK 35–45s · 풀파워 ≥12s)**

기대 화력 모델 갱신: Tools/BalanceSim/analyze_stage_hp.py (mid anchors 550/650/750/900/1050 DPS).

| 보스 | before HP | after HP | mid DPS | TTK mid | TTK full@1880 |
|---|---:|---:|---:|---:|---:|
| boss_stage1 | 1000 | **24000** | 550 | 43.6s | 12.8s |
| boss_hive | 1300 | **28000** | 650 | 43.1s | 14.9s |
| boss_fortress | 1600 | **32000** | 750 | 42.7s | 17.0s |
| boss_storm | 4000 | **38000** | 900 | 42.2s | 20.2s |
| boss_core | 4500 | **45000** | 1050 | 42.9s | 23.9s |

HP 단조 증가 유지.

**(2) 페이즈 2 → 3 + 패턴 성격 (aimed → spread → rapid)**

Core는 n-way 조준 부채꼴만 지원하므로 ways / interval / speed로 성격을 구분:

| 페이즈 | pattern | 성격 |
|---|---|---|
| p0 | aimed | 소수 way · 중속 · 긴 간격 (조준 사격) |
| p1 | spread | 다 way · 저속 · 중간 간격 (확산 탄막) |
| p2 | rapid | 소수 way · 고속 · 짧은 간격 (고속 소수탄) |

페이즈 전환 HP 임계(데이터 명시):
- phaseHpThresholds: [0.667, 0.333] (잔여 HP 비율, 문서용 — Core 미파싱)
- 각 phase hpEnterRatio + pattern 라벨
- **Core 런타임은 equal-N split** ((maxHp-hp)*N/maxHp) — 3페이즈면 잔여 2/3 · 1/3과 일치

**(3) 페이즈별 이동 — Core 미지원 → 권고**

BattleSim 보스는 전 페이즈 단일 사인 호버. 데이터 필드 없음.
→ Reviews/from-grok/requests.md **REQ-G033**: 페이즈별 move profile (hover / vertical sweep / dash).

**(4) BalanceSim 검산**

- CheckBossRedesign 추가: TTK 35–45 · full ≥12 · phases=3 · threat 단조 · 성격 soft 게이트
- 밀도 스트레스: densest phase(보통 p1 spread) 기준으로 재산정

**검증:** Tools/CoreStandalone dotnet test **254/254** · Tools/BalanceSim **PASS**.

**CLAUDE 후속:** Assets/Resources/GameData/waves.json 동기화.



---

## [ ] REQ-034 → CODEX: 무기 확장 1단계 — 미사일 계열 3종 + 옵션 포메이션 3종

실측: 주무기 3계열은 함선에 고정돼 런 중 변하지 않고, 서브는 미사일 1종·옵션 1형태뿐이다.
22분 런에서 플레이어가 무기에 관해 하는 선택이 모디파이어 4종 줍기밖에 없다.
Gradius도 게이지로 미사일/레이저/옵션 종류를 골랐다 — 빌드 다양성의 핵심 축이 빠져 있다.

### 1. 미사일 계열 3종 (기존 미사일 슬롯을 계열 선택형으로)
- `straight`: 현행 직진 (기준)
- `spread_bomb`: 아래로 떨어져 착탄 시 소규모 폭발(장애물·밀집 적에 강함)
- `piercing_lance`: 저연사·고대미지 관통 (보스전에 강함)
계열은 **보상으로 교체**된다(슬롯 레벨과 직교). homing_missile 모디파이어는 그대로 두어
"현재 계열에 유도 부여"로 동작하게 하라 — 계열 × 모디파이어가 곱셈이 되어야 한다.

### 2. 옵션 포메이션 3종
- `trail`: 현행 추종
- `fixed`: 플레이어 상하 고정 위치 (조준 안정)
- `orbit`: 플레이어 주위 원 궤도 (근접 방어 성격)
포메이션도 보상으로 교체. 옵션 개수(슬롯 레벨)와 직교.

### 3. 보상 타입 확장
`RewardType`에 계열/포메이션 교체 항목 추가(예: MissileFamily, OptionFormation).
rewards.json에 항목을 넣을 수 있게 파싱(값은 GROK 후속).
이미 보유한 것과 같은 계열은 후보에서 제외(중복 무의미).

### 제약
정수 연산·유리로 속도·SineLut(궤도 계산), 무할당 가드 유지, 결정론.
리플레이/서스펜드에 현재 계열·포메이션 포함(스키마 갱신 + 마이그레이션 유지).
회귀 테스트: 계열별 거동, 포메이션별 위치 결정론, 모디파이어와의 조합,
보상 중복 제외, 구 세이브 호환. Unity NUnit 호환 API만. 잠정 §7.

**2단계 예고(이번 범위 아님)**: 신규 주무기 2종(wave 관통 파동, burst 3점사)과
런 중 주무기 교체. 1단계 안정화 후 별도 REQ로 발행한다.

---

## [x] REQ-035 → CODEX: 초대형 보스 2종 — 다중 파츠 + 랜덤 출현 (사람 승인 완료)

사람 요청: "보스가 심심하니 화면을 뒤덮는 초대형 보스", "바이오 계열로 무섭게 하나 더",
"둘 중 랜덤 출현". 아트는 CLAUDE가 완성해 art-input에 배치했다
(boss_leviathan.png / boss_broodmother.png, 각 224×336px = 화면 세로의 93%).

### 1. 파츠 시스템 (핵심)
현재 보스는 히트박스 1개 + HP 1개다. 거대하게만 만들면 "큰 과녁"이 된다.
- `BossPart`: partId, 본체 기준 offsetX/Y, halfWidth/halfHeight, hp, maxHp,
  파괴 여부, 공격 프로파일(BossPhase 유사), 재생 시간(0이면 재생 없음)
- 파츠는 **개별 피격 판정**을 갖고, 파괴 시 그 공격이 중단된다
- **코어 게이트**: 지정된 선행 파츠가 모두 파괴될 때까지 코어는 무적
- 파괴/재생 이벤트: `BossPartDestroyed`, `BossPartRegenerated`(좌표+partId)
- 보스 격파 = 코어 파괴

### 2. 두 보스는 상반된 메커닉 (랜덤의 의미)
**LEVIATHAN — 감소형(공략 순서 선택)**, 총 HP 62,000, 목표 TTK 100~120초
| 파츠 | HP | 공격 | 파괴 효과 |
|---|---|---|---|
| 상부 포탑 | 6,000 | 조준 3way | 상단 안전지대 |
| 하부 런처 | 6,000 | 확산 5way | 하단 안전지대 |
| 전방 클로 | 8,000 | 주기적 근접 돌진 | 접근 가능 |
| 실드 제너레이터 | 10,000 | 없음(코어 무적화) | **코어 노출** |
| 추진 엔진 | 7,000 | 없음(본체 수직 이동) | 본체 정지 |
| 코어 | 25,000 | 전방위(실드 파괴 후) | 격파 |

**BROODMOTHER — 증가형(시간 압박)**, 총 HP 62,000, 목표 TTK 100~120초
| 파츠 | HP | 공격 | 특성 |
|---|---|---|---|
| 촉수 좌 | 5,000 | 근접 휘두르기 | 파괴 후 **20초 뒤 재생** |
| 촉수 우 | 5,000 | 산탄 | 파괴 후 **20초 뒤 재생** |
| 산란낭 ×3 | 각 6,000 | 8초마다 잡졸 1기 산란 | 재생 없음 |
| 아귀 | 9,000 | 광역 흡입(플레이어 끌어당김) | — |
| 심장(코어) | 25,000 | 전방위(산란낭 3개 전부 파괴 후) | 격파 |
흡입은 플레이어 위치를 보스 쪽으로 끌어당기는 것 — 정수 유리수 속도로.
산란은 기존 적 스폰 경로를 재사용하되 MaxEnemies 상한을 존중하라.

### 3. 히든 바이옴 + 등장 조건
5바이옴 완주 시점에 아래 3개 중 **2개 이상** → 히든 바이옴(2룸 + 초대형 보스) 개방:
- 엘리트 룸 3개 이상 클리어 / 무피격 바이옴 2개 이상 / Rare 조우 1회 이상 클리어
- 조건 카운터는 RunManager가 추적하고 관측 가능하게 노출(HUD 표시용)
- **둘 중 하나를 런 시드로 결정론 선택**. 메타에 "마지막 조우 보스"를 저장해
  다음 런에서는 다른 쪽 가중치를 높인다(양쪽을 다 보게 유도)
- 초대형 보스 격파 시 RunCleared와 구분되는 완주 등급(예: PerfectClear) 노출 —
  결과 화면과 메타 보상(재화 3배, 전용 함선 해금 조건)에 쓴다

### 제약
정수·결정론·무할당 가드, 스키마 갱신 + 마이그레이션 유지, 감사 시나리오에 히든 경로 추가.
회귀 테스트: 파츠 개별 피격/파괴/재생, 코어 게이트, 산란 상한, 흡입 결정론,
조건 판정 경계, 보스 랜덤 선택 결정론, 리줌/리플레이 재현.
규모가 크다 — 단계적으로. Unity NUnit 호환 API만. 잠정 §7.
GROK 후속: 파츠 HP 배분·조건 임계·TTK 검산. CLAUDE 후속: 파츠 좌표 매핑·파괴 연출·조건 HUD.

### CODEX 응답 (2026-07-30)

완료. `Shmup.Core`에 불변 `BossPartDefinition`/공격 프로파일, 파츠별 AABB·HP,
파괴 시 공격 중단, 코어 게이트, 정확한 재생 타이머, 공용 적 스폰 상한,
정수 유리수 흡입과 `BossPartDestroyed`/`BossPartRegenerated` 이벤트를 추가했다.

런 진행은 5바이옴 종료 시 승인된 3조건 중 2개를 판정하고, 충족하면 2개 숨은 룸 뒤
시드 결정론 콜로설 보스로 진행한다. `MetaState.lastColossalBoss`를 입력으로 반대 보스에
3:1 가중치를 주며 조우 시 메타를 갱신한다. 결과는 `RunCompletionGrade`의
`StandardClear`/`PerfectClear`로 구분한다. 서스펜드/입력 녹화/메타 스키마와 이전 버전
마이그레이션, 전체 관측 감사 해시도 갱신했다.

실제 `GameData/waves.json`과 Presentation은 소유 경계 때문에 수정하지 않았고
`Reviews/from-codex/requests.md`에 GROK/CLAUDE 후속 계약을 남겼다. 감사 러너에는
콘텐츠 반영 전에도 승인된 구조를 검증하는 임시 콜로설 카탈로그 폴백을 두었으며,
실제 두 boss ID가 들어오면 자동으로 GameData 정의를 사용한다.

검증: CoreStandalone 297 tests PASS, determinism-audit-05 `AUDIT PASS`.

## REQ-057 (CODEX): REQ-055 이후 리듬 테스트 2개 실패

`Tools/CoreStandalone`에서 `dotnet test` 결과 **351/353**. 실패 2개다.

### 1. `GameDataParserTests.RepositoryApprovedV2Files_ParseCompletely` (985행)

```
Expected: 30  But was: 31
```

GROK이 REQ-055에서 `hive_tentacle`을 추가해 적 카탈로그가 31개가 됐다. **기대값만 31로 갱신하면 된다.** GROK도 요청을 남겼다.

### 2. `RunCompletionTests.CurrentMiniBossContent_FullRhythmRunTakesDamageAndCrossesBossPhasesDeterministically`

```
Expected: RunCleared  But was: RunOver
```

**진단 (CLAUDE가 조사한 내용 — 시간 절약용)**:

- 실패한 assert는 `FinalState` **하나뿐**이다. `RoomsCleared == 15`, `MidRewards == 5`, `MainRewards == 5`, `MidBossEncounters == 5`, 페이즈 이벤트는 모두 통과한다. 즉 **15방을 다 돌고 마지막에 죽는다.**
- **제한 시간은 원인이 아니다.** `gimmicks[core].timeLimitTicks`를 0으로 바꿔 테스트해도 같은 2개가 실패한다 (확인 후 원복했다).
- **기믹 배치도 원인이 아니다.** 장애물·레이저 중 `|y| < 1.5`에 놓인 것이 **0개**다. 드리프트는 최대 0.45/0.2 u/s로 이동속도(9.5~21.5)의 2~5%다. 통로는 화면 ±11.25 중 ±5까지만 좁아진다. 플레이어가 (0,0)에 고정되어 있어도 이들에 직접 닿지 않는다.
- 남는 원인은 **적 탄**이다. REQ-054(적 4티어)와 REQ-055(촉수 등 신규 적)로 탄 밀도가 올라갔고, 이 테스트의 플레이어는 (0,0)에 **가만히 있어서 회피를 하지 않는다.**

즉 이 테스트의 전제("회피하지 않아도 완주한다")가 콘텐츠 강화로 무효화된 것으로 보인다. 판단을 맡긴다:

- 리듬 검증이 목적이라면 플레이어가 **최소한의 회피**를 하도록 트레이스를 고치는 편이 낫다 (지금은 콘텐츠가 조금만 강해져도 깨진다).
- 아니면 이 테스트가 "무회피 생존 가능성"을 의도적으로 지키려는 것인지 알려 달라 — 그렇다면 콘텐츠 쪽을 되돌려야 하므로 GROK 작업이 된다.

**어느 쪽이든 근거를 보고해라.** 기대값을 그냥 `RunOver`로 바꾸는 것은 안 된다 — 그러면 "런이 클리어 가능한가"를 아무도 검증하지 않게 된다.

### 참고: Unity 컴파일 함정

REQ-055의 `StageGimmickTests.cs`가 `internal` 멤버 `BattleSimConfig.UseConfiguredMainShotStats`를 참조해 **Unity 컴파일이 깨졌고, 씬 빌드와 플레이어 빌드가 전부 멈췄다.** CoreStandalone은 Core와 테스트를 한 어셈블리로 묶어 컴파일하므로 통과하지만 Unity는 별도 어셈블리다.

빌드가 완전히 막혀 CLAUDE가 `Assets/Scripts/Core/AssemblyInfo.cs`를 새로 만들어 `[assembly: InternalsVisibleTo("Shmup.Core.Tests")]`를 넣었다 (기존 파일은 건드리지 않았다). 더 나은 방식이 있으면 바꿔도 된다.

**앞으로 EditMode 테스트가 참조하는 Core 심벌은 `public`이거나 이 파일을 거쳐야 한다.** `dotnet test` 통과가 Unity 통과를 보장하지 않는다는 것을 요청서마다 확인해라.

## REQ-064 (CODEX): 입력 녹화가 터치 아날로그 입력에서 스테이지 1 만에 터진다

폰 실플레이 크래시 (2026-07-30 스크린샷, ErrorOverlay 덕에 원인 확정):

```
InvalidOperationException: The input recording run capacity has been exhausted.
@ Shmup.Core.Simulation.InputRecorder.Record
```

원인: `InputRecording.cs`의 RLE 압축은 같은 입력이 이어질 때만 칸을 아낀다. **터치 아날로그 입력(REQ-045)은 매 틱 델타가 달라 압축이 0%다** — 4096칸이 드래그 ~68초 만에 찬다. 디지털(키보드) 입력만 있던 시절의 용량 설계다.

임시 방어: BattleDirector가 차기 직전에 녹화를 접고 리플레이 저장을 포기한다 (게임은 계속됨). **아날로그 런의 리플레이가 사실상 저장 불가능한 상태다.**

### 요구

1. 아날로그 입력이 섞인 긴 런도 리플레이가 온전히 저장되게 해라. 방향 후보:
   - 용량을 틱 기준으로 재산정 (15방 완주 = 수십 분 = 틱당 1칸 가정 시 십만 단위)
   - 또는 아날로그 델타를 양자화/델타 인코딩해 압축이 다시 걸리게 (결정론 주의 — 양자화가 시뮬 입력 자체를 바꾸면 리플레이 호환이 깨진다. 양자화는 **기록 전 시뮬 입력에 이미 적용**돼야 재생이 일치한다)
2. `Record`가 가득 참에 예외를 던지는 대신 안전하게 실패(반환값)하는 것도 검토해라 — ChooseReward와 같은 원칙 (Presentation 실수가 런을 죽이면 안 된다).
3. 저장 크기를 보고해라 — WebGL localStorage에 들어가야 한다.

검증: dotnet test 전부, 아날로그 입력 장시간 녹화 테스트 추가, 같은 시드 리플레이 해시 일치.

---

## REQ-093 (CODEX): 런 결과 검증 해시 — 글로벌 스코어보드 제출용

**무엇이 필요한가**

끝난 런 하나를 식별·검증할 수 있는 짧은 문자열 해시. 제안 시그니처:

```csharp
// RunManager (런 종료 후에만 유효)
public string ResultHash { get; }   // 16진 16자 정도, 32자 이내
```

**왜**

P1 글로벌 스코어보드(`Assets/Scripts/Presentation/Battle/ScoreboardClient.cs`)가 서버에
`replayHash` 필드를 보내게 되어 있는데, 지금은 **빈 문자열로 보내고 있다**. 서버는
(시드, 함선, 난이도, 점수)만 받는 상태라 점수 위조를 전혀 거를 수 없다.

해시가 있으면 서버가 최소한 "같은 시드·같은 결과의 중복 제출"과 "같은 토큰의 서로
다른 해시 도배"를 구분할 수 있다. 완전한 안티치트는 리플레이 재검증(서버에서 시뮬
재생)이 필요하지만, 그건 다음 단계고 지금은 식별자만 있으면 된다.

**주의**

- Presentation이 직접 만들면 안 된다 — 결정론 값(틱, 시드 파생 상태)에 접근해야 하고,
  같은 런은 어느 플랫폼에서도 같은 해시가 나와야 한다 (AGENTS.md §4).
- `DeterminismAuditHasher`가 이미 런 상태를 접는 로직을 갖고 있으니 그 위에 얹으면 될 것
  같다. 다만 감사용 해시는 내부 구현 변화에 민감해도 되지만, 이 해시는 **빌드가 바뀌어도
  같은 런이면 같아야** 하므로 접는 필드를 명시적으로 고정해 주면 좋겠다
  (시드 / 난이도 배율 / 함선 / 최종 점수 / 클리어 등급 / 총 틱 정도).

검증: 같은 시드·같은 입력 2회 실행의 해시 일치 테스트, 한 필드만 바꾸면 달라지는 테스트.

---

## REQ-104 후속 (CODEX): 이어하기 런에 MetaState를 붙일 방법

**무엇이 필요한가**

`RunManager.ResumeFromSuspendData(...)`에 `MetaState`를 넘길 오버로드, 또는
복원한 런에 메타를 나중에 붙이는 공개 경로 (`AttachMetaState`는 private다).

**왜 (재현 경로)**

1. 컨티뉴 2개를 사고 런을 시작한다 → 런 재고 2, 메타 재고 2.
2. 스테이지 경계에서 일시정지 → 타이틀 (`RunSave`에 런 재고 2가 저장된다).
3. 이어하기로 복귀 → `ResumeFromSuspendData`에는 MetaState를 넘길 자리가 없어
   `_metaState`가 null이다. 런 재고는 2로 복원된다.
4. 컨티뉴를 쓰면 런 재고만 1로 줄고 **메타 재고는 2 그대로다.**
5. 그 런을 끝내고 새 런을 시작하면 메타 재고 2가 다시 들어온다 — 무한 복제다.

Presentation에서는 막을 수 없다: `MetaState.ConsumeContinues`가 internal이라
Presentation이 재고를 줄일 방법이 아예 없다 (구매만 public).

**임시 조치 (현재 빌드)**

없음. 위 경로가 그대로 열려 있다. 이어하기에서 컨티뉴 UI를 막는 것도 답이 아니다 —
"이어한 런에서는 산 컨티뉴를 못 쓴다"는 규칙이 되어 버린다.

검증: 이어하기 → 컨티뉴 → 런 종료 → 새 런에서 초기 재고가 1인지.

---

## REQ-104 후속 (사람/서버): 스코어보드에 컨티뉴 사용 표기

지금 스키마에는 컨티뉴 사용 여부를 실을 칸이 없다. Presentation은
`RunStatistics.ContinuesUsed`를 게임오버 요약("CONTINUED x2")에만 표시하고 제출은
그대로 허용한다 (사람 지시 2026-08-02).

제안: worker.js의 `stat()` 목록에 `cu: stat(body.continuesUsed, 8)`을 추가하고,
보드 행에서 `cu > 0`이면 점수 옆에 흐린 `C2` 뱃지를 붙인다. 무컨티뉴 기록과 같은
칸에서 겨루되 문맥은 남는다. 서버 배포는 사람/오케스트레이터 몫이라 요청만 남긴다.

---

## REQ-112 (CODEX): WarshipEncounter가 실제 전투에서 돌지 않는다 — 관측 경로 없음

**무엇이 필요한가**

`BattleSim`이 `StagePlan.WarshipEncounter`를 받아 `WarshipEncounter` 상태 기계를
실제로 굴리고, 그 상태를 관측으로 내보내 줬으면 한다. 최소한 이 셋:

- `IBattleSim.WarshipActiveGroupIndex` (WARNING 중이면 -1)
- `IBattleSim.WarshipDestroyedAttritionParts` / `WarshipCoreOpeningWays`
- `SimEventType.WarshipWarningStarted` / `WarshipGroupActivated` /
  `WarshipCoreBattleStarted` 를 **BattleSim의 이벤트 스트림에** 실어 줄 것

**왜 (지금 상태)**

REQ-110은 `Assets/Scripts/Core/Simulation/WarshipEncounter.cs`에 완결된 3막 상태
기계를 만들었지만, `BattleSim`은 이 타입을 **전혀 참조하지 않는다** — 참조하는 것은
`SimEventType`에 추가된 열거값 3개(41~43)뿐이고, 이 값들을 발행하는 코드도
`BattleSim`에는 없다. 실제 St3 보스룸은 기존 멀티파트 경로로만 돈다:

- 그룹 게이트가 없다. `BossPartVulnerability.Legacy` + `IsBossCoreGated`라
  **함미(engine)와 포탑 4문이 처음부터 동시에 피격 가능**하고, 코어만 나머지가
  전멸할 때까지 무적이다. REQ-110의 "함미 → 함체(720틱) → 함수" 순서는 런타임에
  존재하지 않는다.
- `advanceAfterTicks 720`(함체 시간 게이트)이 아무 데서도 소비되지 않는다.
- 함미 전멸 프레임에 `MidBossDefeated`가 나오지 않는다 (REQ-110 §3의 접속점).
- `CoreOpeningWays`(포탑 4문 파괴 → 9/5/3 way 분기, REQ-111의 핵심 보상)가
  **탄막에 반영되지 않는다.** 포탑을 다 부수든 하나도 안 부수든 코어 개막은 9way다.
- 서스펜드(`WarshipEncounterSuspendData`)를 채울 주체도 없다.

**Presentation의 현재 대응 (REQ-112, 임시)**

`Assets/Scripts/Presentation/Battle/WarshipView.cs`는 Core가 주는 것만 읽어
그린다: 그룹 소속은 `StagePlan.WarshipEncounter.Groups`(정의), 상태는
`BossParts`(위치·HP·무적). "열린 그룹"은 **아직 살아 있는 파츠를 가진 가장 앞선
그룹**으로 표시하고, 실제 피격 가능 여부는 `BossPartState.Invulnerable`을 그대로
따른다 — 뷰가 Core보다 앞서 말하지 않게 두 신호의 세기를 분리했다(옅은 암전 =
"아직 차례가 아니다", 깊은 암전+맥동 = "Core가 무적이라 한다").

BattleSim이 위 관측을 내보내면 뷰의 파생 로직(`SyncHardpoints`의 focus 계산)을
그 값으로 바로 교체할 수 있다. 그때까지는 연출만 3막처럼 보이고 **규칙은 2막**이다.

검증 제안: 포탑 0문/4문 파괴 두 런에서 코어 개막 way 수가 9와 3으로 갈리는지,
함미 전멸 틱에 `MidBossDefeated`가 정확히 한 번 나오는지.

---

## REQ-121 (CODEX, 선택): 기체 스프라이트 기준 가시 클램프 — 화면 하단 7px 잘림

**결론 먼저**: build25~29의 "화면 하단으로 붙이면 기체가 영구히 사라진다"는
Core 버그가 **아니었다**. 원인은 Presentation이었다 — 화면 하단 게이지 HUD
(ScreenSpaceOverlay 캔버스, 하단 38px = 2.375u)가 그 아래로 내려간 기체를 통째로
덮었다. 스프라이트 정렬 순서로는 이길 수 없는 오버레이라 order 55→3 수정(REQ-119
후속)으로도 증상이 남았다. 기체가 띠에 들어오는 동안 게이지를 흐리는 것으로
`PowerUpHudView`에서 마감했다. **REQ-120의 클램프 판정(±10.75 정상)은 옳았다.**

남는 것은 작은 시각 문제 하나다:

`BattleSim`의 `GetVisiblePlayerCenterMinY/MaxY`는 **히트박스** 반높이
(`PlayerHalfHeight` = 3u/8 = 6px)로 가시 범위를 잡는다. 그런데 기체 **그림**은
48×30px(반높이 15px = 0.94u)라, 클램프 하한(-10.75u)에 붙으면 스프라이트 아래쪽
약 7px이 화면 밖으로 잘린다. 위쪽도 같다.

원한다면 클램프에 **시각 여유(visual margin)** 개념을 하나 받아 주면 좋겠다 —
예: `BattleConfig.PlayerVisualHalfHeight`(기본 0 = 현행 동작 유지)를 두고
`GetVisiblePlayerCenter*Y`가 히트박스 대신 이 값을 쓰게. 기본값이 0이면 기존
리플레이·결정론 해시는 그대로다.

급하지 않다. 기체가 안 보이던 문제는 위에서 닫혔고, 이건 "바닥에 완전히 붙였을 때
기수 아래 몇 픽셀이 화면 끝에서 잘린다" 수준이다.

---

## REQ-122 (CODEX, 높음): 보스전에서 콤보 배율이 강제로 무너진다 — 그레이즈가 감쇠 시계를 못 멈춘다

**증상 (사람 플레이 보고, 2026-08-03)**: "보스전에 들어가면 점수 배율이 끊긴다.
탄 스치기로 배율이 올라가는 게 안 되는 것 같다." 스크린샷 2장 첨부됨 —
St2 fortress 보스전에서 탄막을 뚫는 중인데 우상단 배율이 `×1`.

**코드에서 재현되는 원인 2개** (`Assets/Scripts/Core/Simulation/BattleSim.cs`):

1. **감쇠 시계가 킬 전용이다.** `AdvanceComboDecay`(`:8968-8987`)는
   `_ticksSinceLastKill`이 `_comboDecayTicks`(기본 300틱 = 5초)에 닿으면 배율을
   한 단계 내린다. 그런데 이 카운터를 0으로 되돌리는 곳은 `RecordKillScore`
   (`:8909-8915`) **하나뿐**이다. 그레이즈는 `AddComboGauge(_grazeComboGaugeGain)`
   (`:7137`)로 게이지만 올리고 시계는 건드리지 않는다.

   보스전은 설계상 잡졸이 얇다 (`waves.json`의 St3 세그먼트 intent에도
   "REQ111 warship-climax fodder-thin"이 명시돼 있다). 그래서 킬이 끊기는 순간부터
   **5초마다 한 단계씩** 배율이 내려간다 — 플레이어가 아무리 잘 스쳐도 못 막는다.
   ×4에서 시작해도 15초면 ×1이다. 사람이 본 게 정확히 이것이다.

2. **최대 레벨에서 그레이즈가 완전히 무시된다.** `AddComboGauge`(`:8941-8944`)는
   `_multiplierLevel >= _comboMultipliers.Length - 1`이면 즉시 return한다. 최대
   배율에서는 스쳐도 게이지가 안 쌓이고, 그 사이에도 감쇠 시계는 계속 간다.
   즉 **최대 배율은 도달하는 순간부터 5초 뒤 무조건 떨어진다.**

**요청**

- `_ticksSinceLastKill`을 "마지막 **콤보 행위**"로 재해석해 달라 — 킬뿐 아니라
  그레이즈도 시계를 되돌리게. 이름도 `_ticksSinceLastComboAction` 쪽이 맞다.
  이러면 "탄막에 붙어서 스치며 버티면 배율이 유지된다"는 이 장르의 기본 계약이 선다.
- 최대 레벨에서도 그레이즈가 유지(시계 리셋)에는 기여하게 해 달라. 게이지 누적을
  막는 것 자체는 의도로 보이니 그대로 둬도 된다 — **리셋만** 살아나면 된다.
- `PlayerHit` 시 `ResetCombo`(`:3137`)로 전부 날리는 것은 그대로가 맞다고 본다.

**밸런스 주의 (AGENTS.md §7)**: 위 수정은 `ComboDecayTicks` 같은 **수치를 바꾸지
않고** 규칙만 고치는 것이다. 다만 결과적으로 보스전 획득 점수가 오르므로
스코어보드 기준선이 움직인다 — 수치 조정이 필요하다고 판단되면 GROK/사람 확정을
거쳐라.

**검증 제안**: 보스룸 진입 후 킬 0으로 300틱 이상 그레이즈만 지속하는 결정론
테스트에서 `MultiplierLevel`이 유지되는지. 지금은 반드시 떨어진다.

**Presentation 쪽은 이상 없음**: 그레이즈 피드백(작은 스파크)은
`BattleDirector.cs`의 `SimEventType.GrazeScored`에서 정상적으로 나오고,
HUD 배율은 `MultiplierChanged`를 그대로 표시한다. 뷰가 아니라 규칙 문제다.

---

## REQ-123 (CODEX, 중간): dev 전용 히든 바이옴 직행 — 거대 보스 2종이 사실상 검증 불가

**발단**: 미지의 구역 거대 보스 2종(`boss_leviathan`/`boss_broodmother`)의 스프라이트가
`BattleSceneBuilder`에 등록되지 않아 **엉뚱한 그림(boss_stage1)으로 나오고 있었다**
(2026-08-03 수정, 커밋 3aa2067). 고친 뒤 인게임 확인을 시도했는데 **도달 자체가 막혔다.**

**왜 도달이 안 되나** (`RunManager`):

- THE UNCHARTED는 `BeginFinalContractSelection`에서만 후보에 오른다 — **5바이옴 완주 후
  최종 항로 선택 1회뿐**이다. 중간 항로(`GenerateContractOptions`)는
  `DestinationKind != NextStage`를 전부 걸러내므로 절대 나오지 않는다.
- 게다가 히든 조건 2/3(elite≥3 | noHit≥2 | rare≥1)까지 동시에 충족해야 한다.

**실측**: headless(god 모드, F9/F10 파워업, F11 가속)로 완주를 시도했으나
**St2 hive 보스에서 게임 내 시간 107분(tick 384,060)을 태우고도 못 넘겼다.**
기체를 y=0에 고정한 채로는 코어만 때리는데 코어가 무적이라 진행이 0이다 —
build27~30 테스터 보고서의 "hive 촉수 시간 부족"과 같은 벽이고, 원인은 시간이 아니라
**세로 위치**였다(전함 함미 오판과 같은 계열의 함정).

**요청**: dev 전용 진입점을 하나 만들어 달라. 릴리스에서는 꺼져 있어야 한다.

- `RunManager`에 히든 바이옴에서 런을 시작하는 dev 경로 (예: `StartRun(..., bool startInHiddenBiome)`
  또는 기존 `TryBeginHiddenBiome`을 dev 플래그로 강제 호출할 수 있는 훅)
- 조건 카운터를 dev에서 주입할 수 있으면 더 좋다 (elite/noHit/rare 초기값)
- Presentation 쪽 배선(`DevArgs`에 `--uncharted=1` 추가 → `?uncharted=1`)은 내가 한다.
  이미 `--god`/`--stage=N`이 `RunManager.DevFlagsActive`로 제출을 막는 경로가 있으니
  같은 취급이면 된다.

**왜 필요한가**: 지금 구조에서는 거대 보스 2종의 **아트·파츠·3막 연출·격파 처리 전부가
사실상 검증 불가**다. 위 스프라이트 버그도 그래서 오래 살아남았다. 완주 1회에
게임 내 100분 이상이 드는 콘텐츠는 회귀 검증 대상에서 조용히 빠진다.

**곁가지 관측 (GROK/사람 참고)**: hive 보스는 기체가 촉수 밴드 밖에 있으면 진행이
완전히 0이 된다. 무적 코어만 때리는 상태가 화면상 "데미지가 들어가는 것처럼" 보여
(피격 플래시는 난다) 플레이어가 자기가 헛치는 줄 모른다. 무적 파츠 피격 시 피드백을
분리하는 것이 맞아 보인다 — 이건 내(Presentation) 소관이라 별도로 본다.

---

## [x] REQ-124 (CLAUDE 자체 처리): 구간 워프 `?warp=` + 재사용 검증 하네스

REQ 번호를 남기는 이유는 코드 주석이 이 번호를 참조하기 때문이다. 요청이 아니라 기록이다.

**문제**: 검증 1회당 보스룸 도달에만 F11 40여 번(3~5분)이 들었다. build25~32 내내
반복됐고, 검증 스크립트도 매 세션 스크래치패드에 새로 쓰다가 세션이 끊기면 사라졌다.

**A. `?warp=early|midboss|late|boss` (Presentation 전용, dev 한정)**

`BattleDirector.DevWarpToSection`이 목표 `RunStageSection`에 닿을 때까지 틱을 돌린다.
**게임플레이 판정은 하나도 하지 않는다** — F11(`DevFastForward`)과 같이 Core를 돌릴 뿐이고
구간 전환 시점은 전적으로 Core가 정한다 (CLAUDE.md 원칙 유지).

구현 중 걸린 두 가지를 기록해 둔다:
1. **무입력이면 중간보스에서 영영 멈춘다** — 적을 못 죽여 게이트를 못 넘는다.
   발사 홀드(`new InputCommand(0,0,true)`)를 주도록 고쳤다. 이동은 주지 않는다 —
   세로로 움직이면 "어디서 쐈나"가 결과를 바꿔 워프가 재현되지 않는다.
2. **중간보스를 잡으면 런이 보상 선택에서 멈춘다** — `AwaitingReward`면 첫 카드를
   집어 흐름을 잇는다. 무엇을 고르는지는 워프의 관심사가 아니다.

상한 36000틱(10분)에서 포기하고 경고만 남긴다 — dev 편의가 런을 깨뜨리면 안 된다.
워프한 런은 `MarkCheatUsed()`로 제출을 닫는다: Core의 `DevFlagsActive`는 무적·시작
스테이지처럼 **런 생성 조건**이 바뀔 때만 서므로, 워프만 쓴 런은 그대로 두면 보드에 오른다.

**B. `Tools/QaHarness/rss-verify.js` — 픽셀 어서션 하네스**

스크린샷을 사람(모델)이 한 장씩 눈으로 읽던 것을 수치 판정으로 바꿨다.
보스 HP바를 **색으로 찾아** 픽셀 폭을 재고(좌표를 박으면 캔버스 크기가 바뀔 때 조용히
틀린다), 단조 감소 여부를 PASS/FAIL로 낸다. `report.json`에 시계열이 남는다.

실측 (build34, `--stage 3 --warp boss --seed 2 --seconds 40`):
`sect Boss/live` 즉시 도달, HP바 254px → 206px 단조 감소, 콘솔 에러 0, **PASS**.
같은 검증이 이전에는 5분+판독, 지금은 1분+무판독이다.

**남은 한계**: 워프는 런 시작 시 1회만 돈다. 5바이옴 완주가 필요한 미지의 구역은
여전히 REQ-123(Core dev 진입점)이 있어야 한다.

---

## REQ-125 (CODEX, 높음): 무적 파츠가 탄을 "먹는다" — 헛치는 것을 알 방법이 없다

**REQ-123의 곁가지 관측을 코드로 확인하다가 내가 쓴 것보다 나쁘다는 걸 알았다.**
그때 "피격 플래시는 난다"고 적었는데 부정확했다 — 플래시는 보스 HP가 실제로 줄 때만
뜬다(`BattleDirector`가 `_sim.Boss.Hp` 감소를 보고 친다). 진짜 문제는 따로 있다.

`BattleSim.ResolvePlayerBulletBossCollisions`(`:6630-6692`):

```
if (partIndex < 0 && !legacyHit) { bulletIndex++; continue; }
RemoveBulletAt(bulletIndex);                       // ← 탄은 무조건 사라진다
bool defeated = ApplyDamageToBossPart(partIndex, damage);
```

`ApplyDamageToBossPart`는 `IsBossPartInvulnerable(partIndex)`면 **false를 반환하고 끝난다**
(`:6817-6823`). 즉 무적 파츠에 맞은 탄은 **소멸하되 아무 일도 일어나지 않는다.**

슈팅에서 탄이 적에게 닿아 사라지는 것은 "맞았다"의 가장 강한 신호다. 지금은 그 신호가
거짓말을 한다 — 플레이어는 계속 명중시키고 있다고 믿으면서 HP는 안 줄고, 왜인지 알 수
없다. build27~30 테스터가 hive에서 "데미지 경로는 정상인데 시간이 부족하다"고 5번 판정한
것도, 내 headless 완주 런이 St2에서 게임 내 107분을 태운 것도 같은 착시다.

**요청**: 무적 파츠에 막힌 순간을 **이벤트로 알려 달라.**

- `SimEventType.BossPartHitBlocked`(가칭) — 좌표 + partId. 데미지는 그대로 0.
- 탄 소멸 여부는 Core 판정이니 그대로 두면 된다. 뷰가 알아야 할 것은 "여기서 막혔다"뿐이다.

받으면 내가 붙일 것: 파란 튕김 스파크 + 짧은 링 — 폭발/피격 플래시와 확실히 다른 어휘로.
"여기는 지금 못 깎는다"가 한 프레임에 읽히면 플레이어는 다른 파츠를 찾아간다.

**대안(더 큰 변경, 사람 결정)**: 무적 파츠가 아예 탄을 통과시키게 하면 이벤트 없이도
"안 맞는다"가 자명해진다. 다만 판정·리플레이 해시가 바뀌고 전함 함체 뒤 파츠까지
때리게 되니 밸런스 영향이 크다. 나는 이벤트 쪽을 권한다.

**우선순위 근거**: 미지의 구역 접근성 논의(`uncharted-access-2026-08-03.md`)에서
"완주율이 낮은 원인"으로 지목한 것이 이것이다. 조건 완화보다 이게 먼저다.

---

## REQ-126 (GROK, 중간): 포트리스에 적이 허공에 떠 있다 — 발판 배치 요청

**사람 지적 (2026-08-03)**: "fortress의 경우 적들이 허공에 떠 있어서 이상하다. 충돌하면
데미지를 입는 발판 같은 걸 설계해서 일부 배치해 달라."

**좋은 소식: 규칙도 아트도 이미 있다. 배치만 없다.**

- 접촉 데미지: `BattleConfig.ObstacleContactDamage = 1`이 이미 있고
  `BattleSim`(`:9138`)이 플레이어 충돌 시 `ApplyPlayerHit`을 부른다. Core 작업 불필요.
- 아트: `solid` 장애물의 포트리스 테마 스프라이트는 `obstacle_armor_block.png`로 이미
  배선돼 있다 (`BattleSceneBuilder`의 `_obstacleSolidSprites` 3번 슬롯). 내 작업 불필요.
- 현재 `waves.json` 장애물 분포: breakable 181 / solid 129 / laserEmitter 26.
  **문제는 포트리스 세그먼트에 solid가 거의 없다는 것**으로 보인다 (분포가 테마별로
  치우쳐 있는지 확인해 달라).

**요청**: 포트리스 테마 세그먼트에 `type: "solid"` 장애물을 갑판/난간처럼 **가로로 이어지게**
배치해 달라. 적이 그 위·아래에 서면 "발판에 붙어 있다"로 읽힌다. 지금은 배경만 요새이고
전경에 구조물이 없어 적이 허공에 뜬다.

주의: solid는 `hp: 0`(파괴 불가)이라 길게 깔면 통로가 막힌다. 레인 마스크
(`traversableLaneMasks`)와 어긋나지 않게, **지나갈 길을 남기는 폭**으로 부탁한다.

---

## REQ-127 (GROK + CODEX, 중간): 스테이지 간격 편차 — 중간보스가 즉시 오거나 한참 안 온다

**사람 지적**: "일부 스테이지 간격이 너무 길거나 짧다. 중간보스 이후 갑자기 스테이지
보스로 넘어가거나, 한참 진행했는데 안 나온다."

**측정 (`GameData/waves.json`, 2026-08-03)**

| 항목 | 값 |
|---|---|
| 세그먼트 수 | 60 |
| `lengthTicks` | 최소 **280**(4.7초) ~ 최대 **970**(16.2초), 중앙값 830(13.8초) |
| `segmentsPerStage` (전반) | **3** → 중앙값 기준 약 **41초** |
| `closingSegmentsPerStage` (후반) | **7** → 중앙값 기준 약 **97초** |

두 가지가 동시에 문제다:

1. **전반/후반 비대칭이 2.3배다.** 중간보스까지 41초, 그 뒤 보스까지 97초.
   사람이 "중간보스 이후가 한참"이라고 느끼는 것이 이 수치 그대로다.
2. **길이 편차가 3.5배다** (280~970). 개수로만 뽑으니 같은 3세그먼트라도
   짧은 것만 걸리면 14초, 긴 것만 걸리면 48초다 — **같은 스테이지가 런마다 3배 차이**가
   난다. "갑자기 보스"와 "한참 안 나온다"가 한 원인에서 나온다.

**제안 (수치는 GROK/사람 확정 — §7)**
- 개수가 아니라 **목표 시간**으로 뽑게 해 달라: "전반 ≈ 45초, 후반 ≈ 60초"를 목표로
  세그먼트를 채우고 초과분은 자른다. 이러면 편차가 자연히 줄어든다.
  → 이건 규칙이라 **CODEX**의 `SegmentStageGenerator` 작업이다.
- 또는 데이터만으로 완화하려면 `lengthTicks` 편차를 좁히고(예: 600~900)
  `closingSegmentsPerStage`를 7 → 5로 낮춘다. → **GROK**.
- 어느 쪽이든 **결정론 해시가 바뀐다**. 기존 리플레이는 무효화될 것이다 — 사람 확인 필요.

---

## REQ-128 (CODEX + GROK, 높음): 보스전에서 연사가 뚝뚝 끊긴다 — 볼리 전탄 예산 규칙

**사람 지적**: "일부 보스 스테이지에서 탄 연사력이 갑자기 저하되는 경우가 있다."

**원인 후보 확정** (`BattleSim.cs:9494` `HasCapacityForPlayerVolley`):

```csharp
long required = (long)(_options.Count + 1) * shotsPerEmitter;
return required <= (long)_maxBullets - CountPlayerBullets();
```

발사는 **전탄 동시(all-or-nothing)** 다. 예산이 모자라면 그 프레임 볼리 전체가 **취소**된다.
`BattleConfig.MaxBullets = 64`인데:

- OPTION 최대 6기 → 발사체 7개/볼리, 더블샷이면 14개/볼리
- 보스룸은 보스가 `holdX 9.0`(화면 오른쪽)이라 **탄이 화면 끝까지 오래 살아 있다.**
  잡졸이 없어 중간에 소멸하지도 않는다(설계상 fodder-thin).

즉 옵션을 많이 켠 상태로 보스룸에 들어가면 자기 탄이 64칸을 채워 **볼리가 통째로
스킵되고, 그게 "연사력 저하"로 체감된다.** 옵션이 적은 초반에는 안 보이다가 강해질수록
심해지는 것도 이 가설과 맞는다.

**요청**
- (CODEX) 전탄 동시 규칙이 의도라면 유지하되, 예산 부족 시 **볼리를 취소하지 말고 다음
  가능한 틱으로 미루는지**(현재)와 **부분 발사**(가능한 발사체만) 중 무엇이 맞는지 정해 달라.
  나는 후자를 권한다 — 그라디우스 계열에서 옵션은 "더 쏜다"지 "덜 쏜다"가 아니다.
- (GROK) 어느 쪽이든 `MaxBullets = 64`는 옵션 6 + 더블샷 기준으로 너무 좁다.
  최소 볼리 4~5회분(약 56~70발)이 동시에 살아 있을 수 있어야 한다.
- **재현 방법**: `?dev=1&god=1&stage=3&warp=boss`로 보스룸 직행 → F9/F10으로 OPTION을
  최대까지 올린 뒤 연사 관찰. (워프·하네스는 `Tools/QaHarness/README.md` 참고.)

---

## [x] REQ-122 후속 확인: 그레이즈 배율 유지는 **이미 고쳐져 있다**

사람이 "탄알 스칠 때 배율 유지가 안 바뀐 것 같다"고 했는데, 코드와 테스트는 고쳐진
상태다 — `RecordComboAction()`이 킬과 그레이즈 양쪽에서 감쇠 시계를 되돌리고,
`ContinuousGrazeKeepsMaximumMultiplierPastDecayWindow` 테스트가 최대 배율 유지를
검증한다 (557/557 통과).

다만 **이 수정이 들어간 첫 빌드는 build38**이다 (`sim` 병합 = `105edbe`). 그 전 빌드나
배포판(github pages)에서 확인했다면 옛 동작을 본 것이다. 현재 `Builds/Web`(build40+)로
다시 봐 달라.

---

## [보류] REQ-134 (CLAUDE, 사람이 나중으로 미룸): BGM·SFX 품질을 PS2급으로

**사람 지적 (2026-08-03)**: "bgm과 효과음 지금도 너무 싸구려티가 난다. 플레이스테이션 2 수준까지 높이고 싶은데." → **"사운드는 나중에 고치자"로 보류.**

**왜 지금 방식으로는 천장이 낮은가**

`Tools/SfxGen/bgmgen_snes.py`는 파이썬 **오실레이터 합성**이다 — 사각·삼각파에 감산/FM을 얹고 에코를 건다. SFX도 sfxr 계열 절차 합성이다. 구체적 결함:
- 실제 악기 **샘플이 없다** (PS2 음악의 정체는 대부분 샘플이다)
- 32kHz, 벨로시티 레이어 없음, 리얼 리버브 없음
- 드럼이 합성음이라 킥·스네어에 몸통이 없다

**누가 손대도 오실레이터인 한 SNES 언저리가 한계다** — 에이전트 선택의 문제가 아니라 방법의 문제다.

**경로 세 가지**

| 안 | 내용 | 비용 | 리스크 |
|---|---|---|---|
| ① SoundFont 렌더링 (권장 프로토타입) | SF2 + FluidSynth로 MIDI 렌더. 오실레이터 → 실제 녹음 샘플 | 무료 | 사운드폰트 라이선스 확인 필요 |
| ② AI 음악 생성 (Suno·Udio 등) | 즉시 방송급 | 유료 | **Steam 상업 배포 라이선스 필수 확인** |
| ③ 에셋 팩 구매 / 작곡가 의뢰 | 가장 확실 | 유료 | 없음 |

①은 현재 화성 설계(테마별 진행·스케일)를 그대로 재사용할 수 있고 시드·MIDI가 리포에 남아 재현 가능하다. 실패해도 잃는 게 없어 프로토타입으로 권한다.

**담당**: 작업은 CLAUDE(오디오 파이프라인 소유, §2). 다만 **에셋 선택·구매·라이선스 확정은 사람**(§7) — 상업 배포가 걸린 결정이다.

**재개 시 첫 단계**: 1면 곡 하나만 ①로 만들어 현행과 나란히 비교.

---

## REQ-139 — 거대 전함을 "부위별로 상대하는 3페이즈 보스"로 재설계 (사람 지시 2026-08-03)

**사람 원문**: "전함의 경우 그냥 사이즈를 크게 하자. 그리고 하이브의 부위 파괴랑 다르게
거대 전함을 부분부분 상대하는 패턴으로 재설계하자. 지금은 너무 스케일이 작아
1. 처음 등장시 화면 아래부터 오른쪽 부분에 위쪽 부분만 보임 (미사일 발사)
2. 윗부분이 파괴되면 가운데로 정렬하고 코어가 드러나면서 레이저 발사 등 패턴 바뀜
3. 다 파괴되면 전함 안에서 로봇이 나와서 마지막 페이즈로 싸움"

### 지금 데이터로 어디까지 되나 (읽고 확인한 것)

`boss_fortress`에는 이미 `warship` 조우 정의가 있고 그룹 역할이 셋이다 —
`stern(midbossGate)` → `hull(attritionLine)` → `bow(finalCore)`. 즉 **부위를 순서대로
상대하는 뼈대는 이미 있다.** 크기는 halfWidth 10 / halfHeight 5 (20×10 유닛, 화면이
40×22.5)라 "거대 전함"이라기엔 작다. `BattleSim`에 `_bossForm2`(폼 교체)도 이미 있다.

### 필요한 것 — CODEX (Core)

1. **그룹별 정박 위치**. 지금 보스는 `holdX` 하나로 x만 정한다. 그룹(=페이즈)마다
   중심 좌표를 줄 수 있어야 "처음엔 화면 아래 오른쪽에 윗부분만 보이다가, 윗부분이
   깨지면 가운데로 정렬"이 성립한다. 제안: 그룹에 `anchorX`/`anchorY`와
   `anchorTravelTicks`(정박 위치를 옮기는 데 걸리는 틱). 이동은 결정론적 보간.
2. **그룹 전환 시 패턴 교체**. 지금 페이즈 전환은 `hpThreshold`가 판단한다.
   그룹이 전멸했을 때 다음 페이즈로 넘어가는 조건(`advanceOnGroupCleared`)이 필요하다.
3. **마지막 폼 = 로봇**. `_bossForm2` 교체 경로를 그룹 전멸에도 열어 주면 된다.
   로봇은 별도 엔티티가 아니라 **같은 보스의 2번째 폼**으로 두는 편이 결정론·리플레이
   양쪽에 안전하다고 본다 — 다르게 보면 알려 달라.
4. 뷰가 읽을 상태: 현재 그룹 id, 정박 보간 진행도(0~1), 현재 폼. 전부 읽기 전용.

### 필요한 것 — GROK (데이터)

- `boss_fortress` 크기 상향 (제안: halfWidth 16~18 / halfHeight 8~9). **화면 밖으로
  걸치는 것이 연출의 핵심**이므로 화면(40×22.5)보다 커도 된다.
- 그룹별 정박 좌표: ① 아래·오른쪽에 걸쳐 윗부분만 보이게 ② 중앙 정렬
- 페이즈별 패턴: ① 미사일 ② 레이저 ③ 로봇(근접·돌진 계열)
- 로봇 폼의 HP·크기·패턴
- 모든 y는 1/256 서브유닛 격자에 정확히 떨어질 것 (§4)

### 필요한 것 — CLAUDE (내가 한다)

- 거대 함체 아트 (현재 320×160 → 화면을 넘는 크기로 재생성)
- 로봇 아트 + 함체에서 튀어나오는 연출
- 페이즈 전환 연출 (정박 이동, 코어 노출, 로봇 사출)

**현재 함체 조립의 문제도 같이 고친다**: 함수에 다른 보스 그림을 얹어 놔서 이음매가
드러난다 — 하이브와 같은 방식(전신 렌더 한 장을 잘라 파츠로 쓰기)으로 바꾼다.

---

## REQ-143 — 전함 엔진 파츠 폭이 함교보다 넓다 (CLAUDE → GROK, 2026-08-03)

REQ-142로 파츠가 갑판에 얹혔지만, **엔진만 여전히 떠 보인다.** 원인은 높이가 아니라
**폭**이다 — 엔진 판정이 halfWidth 3.5(7유닛)인데 그 자리(x +5)의 함교는 4유닛 남짓이라,
좁은 봉우리 위에 넓은 간판이 얹힌 꼴이다.

함체 아트에서 실측한 x별 상부 구조물 폭이 필요하면 말해라. 지금 눈대중으로는
엔진 halfWidth를 **2.0(4유닛) 안팎**으로 줄이면 함교 위에 정확히 앉는다.
높이(halfHeight 2.5)와 offsetY(7.5625)는 그대로 두면 된다.

판정을 줄이면 맞히기 어려워지므로 HP를 함께 조정할지는 네 판단이다.

---

## REQ-144 — 하이브 최종 페이즈 탄이 회피 불가능하게 빠르다 (CLAUDE → GROK, 2026-08-03)

**사람 원문**: "하이브 보스 마지막 페이즈 탄이 너무너무 말도 안되게 빨라서 피할수가 없어.
합리적으로 느리게 해줘."

범인은 `bulletSpeed`가 아니라 **`mineAcceleration`**이다. 최종 페이즈(hpThreshold 0.333)는
`mineAcceleration: 2800`, `mineTelegraphTicks: 16`, `bulletSpeed: 12.0`, `fireIntervalTicks: 10`이다.
기뢰는 예고가 끝나면 이 가속으로 계속 빨라지므로, 예고를 보고 피하기 시작해도 도달 시간이
사람 반응 한계 아래로 내려간다. 중간 페이즈(0.667)는 2400/예고 24라 그나마 읽힌다.

**목표로 삼을 성질** (숫자는 네가 정해라):
- 예고가 끝난 뒤 기뢰가 플레이어 위치까지 오는 데 **0.8초 이상** 걸릴 것
- 최고 속도가 중간 페이즈의 기뢰보다 크게 빠르지 않을 것
- 밀도(초당 탄수)는 "대량 발사" 인상을 유지 — 속도를 낮춘 만큼 웨이/간격으로 보완해도 좋다

## REQ-145 — 스테이지 분위기와 맞지 않는 졸개 (CLAUDE → GROK, 2026-08-03)

**사람 원문**: "스테이지 분위기에 맞는 졸개들이 나와야할것같아. 고철이나 기계 스테이지는 기계,
생물체 관련 스테이지는 생물체"

테마별 세그먼트는 이미 잘 갈려 있다 (scrapyard=고철·기계, fortress=군용 기계, core=결정,
hive=생물, nebula=영체). 문제는 **테마가 없는 세그먼트 8개**다 — 스폰 105건이 전부
`zako_straight`/`zako_sine`/`zako_fast` 같은 범용 기계 졸개인데, 이 세그먼트들이 모든 테마에
섞여 나온다. 그래서 생물 스테이지(hive)에 기계 졸개가 날아든다.

**할 일**: 테마 없는 세그먼트를 테마별로 갈라라. 같은 배치·같은 난이도로 두되 등장하는
졸개 id만 그 테마의 것으로 바꾸는 방식이면 밸런스 영향이 가장 적다. 스프라이트는 34종 전부
이미 있으니 새로 만들 것은 없다.

---

## REQ-150 — 스코어보드 서버가 난이도를 돌려줘야 한다 (사람 결정 2026-08-04)

**배경**: "난이도별로 어떤 차이지? 스코어보드도 구분해야할까"에 대한 조사 결과.

난이도가 바꾸는 것은 **적 HP 하나뿐**이다 (EASY ×0.75 / NORMAL ×1 / HARD ×1.25).
탄 속도·밀도·스폰·점수 계산은 전부 같다. 그런데 HP가 오르면 보스전이 길어져 스치기와
보스 딜 콤보 기회가 늘기 때문에, **HARD 쪽이 점수를 더 버는 방향**으로 기운다.
즉 지금 한 보드에 섞여 있는 것은 EASY가 아니라 HARD에게 유리하다.

**클라이언트에서 한 것 (완료)**
- 데일리는 **NORMAL 고정**. 모두가 같은 조건으로 겨루는 것이 데일리의 존재 이유다.
  타이틀에서 데일리를 고르면 난이도 칸이 `NORMAL — DAILY IS FIXED`로 바뀐다.
- 보드 행에 난이도 마커(E/H) 자리를 만들어 뒀다. 컨티뉴 마커와 같은 문법이라
  헤더 라벨은 없고, 말할 것이 있을 때만 글자가 난다.

**서버에서 해야 할 것 (사람 — 이 저장소 밖)**

제출(`POST /v1/scores`)에는 `difficulty`가 **예전부터 실려 오고 있다.** 그런데 보드
응답(`GET /v1/board`)이 그 값을 돌려주지 않아 화면이 그릴 수가 없다.
보드 엔트리에 **`d`** 키로 난이도를 실어 주면 클라이언트는 그날부터 마커를 그린다.

값이 없는 구 기록은 마커 없이 남는다 — NORMAL로 가정하지 않는다. 난이도가 HP를
1.67배(0.75 → 1.25)까지 벌리므로, 모르는 기록에 NORMAL을 적으면 EASY 기록까지
정상 조건으로 보이게 된다.

---

## REQ-158 — 레비아탄은 페이즈 1조차 시작되지 않는다 (CLAUDE → GROK, 2026-08-04)

**사람 보고**: "히든 보스도 페이즈4를 구경할 수가 없게 되어있네. 더 이상 공격할 약점이
없는데 HP는 남아있어."

**원인은 산수다.** 페이즈는 잔여 HP 비율로 넘어가는데, 각 페이즈에서 **때릴 수 있는
파츠**의 HP 합이 다음 문턱까지 깎기에 모자라면 그 페이즈에서 영원히 멈춘다.

```
boss_leviathan (총 62,000)
  ph0: 깰 수 있는 것 다 깨도 잔여 39,000  >  ph1 문턱 31,000   → ph1 시작 안 됨
  ph1: (도달했다 쳐도)      잔여 21,400  >  ph2 문턱 12,400   → ph2 시작 안 됨
  → 남은 부위는 전부 무적이라 보스를 죽일 방법이 없다.

boss_broodmother 는 통과 (ph1이 문턱과 정확히 같다 — 여유가 0이라 위태롭다)
```

**할 일**: 레비아탄의 파츠 HP 배분 또는 페이즈별 `partRules`를 고쳐 모든 페이즈에
도달하고 격파까지 가능하게 만들어라. 브루드마더도 여유가 0이니 조금 벌려라.

**검증 도구를 이미 만들어 뒀다**: `Assets/Tests/EditMode/Req158BossPhaseReachabilityTests.cs`
가 모든 멀티파트 보스에 대해 이 불변식을 검사한다. 지금은 레비아탄 때문에 실패한다 —
네 수정이 맞으면 통과한다. **이 테스트를 통과시키는 것이 완료 조건이다.**

주의: 각 페이즈에서 "때릴 수 있는 HP"는 그 페이즈에서 새로 열린 것뿐 아니라
**이전 페이즈에서 이미 열려 있던 것까지 누적**이다(파괴는 되돌아가지 않으므로).

---

## [x] REQ-185 → CODEX: 탄 병렬 리스트 통합 (정리 7번, 사람 승인 2026-08-07)

**무엇이 필요한가**

BattleSim의 탄 상태가 `_bullets` + 보조 리스트 9종(XRemainders/YRemainders/VelXNumerators/
VelYNumerators/VelDenominators/PiercesRemaining/RicochetUsed/HomingTargetIds/GrazeScored,
사용 지점 약 117곳)에 인덱스 동기화로 흩어져 있다. 삭제 시 한 리스트만 빠지면 전체 탄
데이터가 꼬이는 구조를 단일 컬렉션으로 통합해 달라.

**왜**

유지보수 시한폭탄 (2026-08-07 3-에이전트 코드 분석 합의, P2). 참고로 파일은 이제
BattleSim.*.cs 9개로 분할돼 있다 (2026-08-07, 해시 6/6 검증 완료).

**제약 — 이것이 핵심이다**

- `List<T>` 인덱서는 struct 복사본을 반환하므로 `_aux[i].X = v` 꼴 인플레이스 쓰기가
  컴파일되지 않는다. 쓰기 지점이 많아 배열 백킹(struct[] 는 인플레이스 가능) 또는
  ref 반환 커스텀 컬렉션 등 설계 선택이 필요하다 — Core 소유자의 판단에 맡긴다.
- RunManagerAllocationTests의 무할당 제약 위반 금지.
- **완료 조건**: dotnet test 전체 통과 + `Tools/DeterminismAudit dotnet run -c Release -- --suite`
  해시 6개가 현재 main과 완전 일치 (레퍼런스: 1190DE99D8680643 4811CF1AF98F35C7
  2FF8D6724019BED1 61A37E947CD8B294 CE85F53DACAC59BD 1A3729F816C31A07).
  해시가 하나라도 다르면 본 게임 영향이 있다는 뜻 — 사람 요구는 영향 0이다.

**처리 (2026-08-07, CLAUDE 대행 — 사람 승인 "전부 대행"):** BulletAux 구조체 +
ref 인덱서 BulletAuxList로 통합 (커밋 6aab61a). 해시 6/6 레퍼런스 일치, 592/592.

## [x] REQ-186 → CODEX (+GROK 협의): 스폰 formation 매크로 + 장애물 티어 기본 크기 (정리 6번)

**무엇이 필요한가**

waves.json 확장 축 두 개를 **옵셔널 필드**로 열어 달라. 기존 데이터는 한 글자도 안 바뀌고
파싱 결과도 동일해야 한다 (본 게임 영향 0 — 채택은 GROK이 신규 세그먼트부터 점진 적용).

1. spawn formation 매크로: `{"formation":"line","count":8,"tickStart":40,"tickStep":30,
   "enemyId":"...","y":0,"yStep":0}` → 파서가 개별 SpawnEvent로 전개. 현재 1,714개
   스폰이 전부 손 전개라 데이터가 371KB다 (GROK 분석 1.1).
2. 장애물 티어 기본 크기: waves 헤더에 `obstacleSizeTiers` 테이블(타입/스테이지별 기본
   halfWidth/halfHeight)을 두고, per-obstacle halfWidth는 예외에만 쓰게. 현재 413개
   항목이 전부 동일한 0.75 복붙이다 (GROK 분석 1.3).

**왜**

다음 밸런스 조정("하이브만 2배" 류)이 스크립트 없이 한 줄 수정으로 되게. 스키마 설계는
GROK(작성자 인체공학)과 필드명·시맨틱을 합의하고 진행할 것.

**완료 조건**: 기존 waves.json 파싱 결과 불변 (DeterminismAudit 해시 6/6 일치, 위 REQ-185와
같은 레퍼런스) + 새 필드 각각의 전개 검증 테스트.

**처리 (2026-08-07, CLAUDE 대행):** 커밋 d2bbd88. 해시 6/6 일치, 테스트 4개 추가.
채택(신규 데이터에 실제 사용)은 GROK 몫으로 남음.

### 2026-09-29 개편 대행분 리뷰 인계

사용자의 기존 `codex/revamp` 개편 계속 지시에 따라 Codex가 Presentation/오케스트레이션을
대행했다. RewardScreen/ContractScreen의 정보 배치와 네 번째 보상, ChoiceButton 입력 경로,
BattleDirector의 성공 후 기록·프레임당 단일 선택, PauseScreen의 재개 입력 차단을 원 담당
복귀 시 리뷰 대상으로 남긴다. Core 597개 및 Unity EditMode 663개 통과. 근거는
`REVAMP-LOG.md`의 보상·계약 선택 개편 항목과 `ChoiceScreenTests.cs`에 있다.

### 2026-09-30 오디오 개편 대행분 리뷰 인계

같은 사용자 승인 범위에서 Codex가 Presentation/오케스트레이션을 대행했다.
AudioPreferences/AudioChannelSource, UiAudio, 공용 AudioSettingsPanel 및 타이틀·일시정지·
선택 UI 연동을 원 담당 복귀 시 리뷰한다. 씬은 CLI의 Editor API로 채널과 기존 클립
참조를 추가했으며, BattleSceneBuilder에도 동일 배선을 반영했다. Core 597개, Unity
683개 통과. 실제 장치에서 UI 소리 크기·음색 및 브라우저 오디오 시작 동작은 후속
플레이 테스트 대상이다. 신규 음원은 채택하지 않았다.

### 2026-09-30 전투 효과음 우선순위 대행분 리뷰 인계

사용자의 개편 계속 지시에 따라 Codex가 Presentation/오케스트레이션을 대행했다.
SfxPlayer의 6개 소스 재사용, SfxVoiceGate의 반복 간격과 우선순위, 위험 신호 중
일반 효과음 덕킹, BattleDirector의 배틀 교체 시 재생 상태 초기화를 원 담당 복귀 시
리뷰한다. Core 597개 및 Unity EditMode 705개 통과(신규 22개). 실제 Battle 씬 음원의
600틱 밀집 이벤트 검사는 재생 시작 220회와 소스 6개를 확인했다. 청음/실플레이를
대체하지 않으며, 경고 가청성과 35% 덕킹의 체감은 추후 장치 테스트 대상으로 남긴다.
기존 채택 음원만 사용하고 주무기/미사일 발사음 무음 정책을 유지했다.

### 2026-09-30 전투 화면 가독성 및 웹 테스트 빌드 인계

사용자 승인으로 Codex가 Presentation/오케스트레이션을 대행했다. CombatReadability,
적 탄/레이저/기체의 정렬, 폭발 페이드와 플래시 감소, 피격 플래시 우선순위를 원 담당
복귀 시 리뷰한다. Core 597개 및 Unity EditMode 715개 통과. 실제 에셋에 겹침 상태를
주입한 GPU Editor 캡처를 비교하며 실플레이로 보고하지 않는다. MobileBuilder에
RSS_WEB_BUILD_OUTPUT 경로 선택을 추가해 기존 빌드와 분리한다. 사용자는 이번 작업 후
rss-play 테스트 사이트 배포를 승인했으며 최종 배포 근거는 REVAMP-LOG에 기록한다.

배포 완료: source `09dc823`, `rss-play@baabaa1`, Pages 작업 `36644519321` 성공.
공개 파일 4개 다운로드 후 SHA-256/크기 일치를 확인했다. 템플릿의 캐시 버전만 바꾸고
build-info.json에 추적 정보를 남겼다. codex/revamp도 원격에 게시했다. 브라우저 주행은
공유 PC 규칙상 실행하지 않았으며 PLAYTESTER PASS로 보고하지 않는다. 실제 기기에서
메뉴/전투/음향 체감은 사용자 테스트와 원 담당 복귀 후 검토 대상으로 남긴다.

### 2026-09-30 도트 애니메이션 대행분 리뷰 인계

사용자의 개편 계속 지시에 따라 Codex가 Presentation/오케스트레이션을 대행했다.
SpriteAnimationPlayback, 전투 Tick 기반 기체/적 프레임 선택, echo_wisp 왕복 루프,
감소 모드의 7종 장식 점멸 억제, starter ID 기반 엔진 선택을 원 담당 복귀 시 리뷰한다.
Core 597개 / Unity 731개 통과. 36개 클립/180프레임의 실제 씬 참조와 원본 픽셀을
진단했다. 아트 채택·게임 수치·UI 배치는 변경하지 않았다. 사용자 보고 UI 겹침은
후속 수정으로 남겼다. `ANIMATION-REVIEW.md`에 뱅킹/보스 루프 등 미완료 아트 과제를 기록했다.
`f4e143a`의 WebGL 빌드를 `rss-play@23dd1e3`에 게시했고 Pages 성공/공개 파일 4개
해시 일치를 확인했다. 이번 버전의 브라우저 주행은 미실시다.

### 2026-09-30 UI 겹침 수정 대행분 리뷰 인계

사용자의 다음 작업 지시에 따라 Codex가 Presentation/오케스트레이션을 대행했다.
BattleHudVisibility의 전투/선택/결과/일시정지 표시 전환, PauseScreen의 하위 설정창
표시 우선순위와 RESUME 포커스 복원, Options 진입 버튼의 LateUpdate 갱신,
Reward/Contract의 일시정지 시 표시 억제를 원 담당 복귀 시 리뷰한다.
Core 597개 / Unity 743개 통과. 실제 Battle 씬의 통합 UI 렌더 24장으로 3가지 크기를
확인했고, 사용자 기기의 전체 실주행은 미실시다. 상세 근거/범위는 REVAMP-LOG에 남겼다.
소스 `eec91b9`와 `rss-play@7d0b9ff`를 원격에 게시했다. WebGL/Pages 작업
`36654244651` 성공, 공개 게임 파일 4개 크기/SHA-256 일치 확인. 실주행 PASS는 아니다.

### 2026-09-30 스테이지 감사/보스 진입 대행분 리뷰 인계

사용자의 추가 개선 지시에 따라 Codex가 Presentation/오케스트레이션을 대행했다.
BossIntro 위치/문구/플래시 감소/메뉴 수명주기와 BattleDirector의 형태 전환별 트리거를
원 담당 복귀 시 리뷰한다. 중간보스 무배너 정책과 모든 Core/게임 수치는 유지했다.
Core 597개 / Unity 761개 통과. 100개 생성 계획 감사에서 CleanKill 후반의 좁은 조각
풀이 우선 개선 대상으로 나왔다. STAGE-RHYTHM-REVIEW.md의 비교안은 아직 미구현이며
새 기본 밸런스의 승인이나 실주행 PASS를 뜻하지 않는다.
`49ea1ec`의 clean WebGL 빌드를 `rss-play@a10169a`에 게시했다. Pages `36657334402`
성공과 공개 게임 파일 4개 크기/SHA-256 일치를 확인했다. 브라우저 주행은 미실시다.

### 2026-09-30 후반 구성/저장 검증 대행분 리뷰 인계

사용자의 개편 계속 지시에 따라 Codex가 Core 구현 및 Presentation/QA/오케스트레이션을
대행했다. 전용 풀 소진 후 미사용 일반 조각을 보충하는 선택 규칙, v29/v26 버전 경계,
계약 없음의 체크섬 정규화, RunSave/ReplaySave 후보 무결성 검사를 원 담당 복귀 시
리뷰한다. GameData 기본값이나 신규 아트 채택은 없었다.
Core 611개 / Unity 785개 통과. 2,600개 전후 생성 비교는 312개 반복 경로를 해소했고
나머지 2,288개 해시는 동일하다. 실제 생성량/길이 변화와 체감 검증 한계는
STAGE-RHYTHM-REVIEW에 기록했다. 이전 런/리플레이는 새로 시작해야 하며 영구 성장은
유지한다. 저장 파일 자체를 삭제하거나 사용자 파일로 검사하지 않았다.
소스 `e462974`와 `rss-play@74d2725`를 원격에 게시했다. Pages `36667167301` 성공,
공개 게임 파일 4개 크기/SHA-256 일치 확인. 브라우저 실주행 PASS는 아니다.

### 2026-09-30 SFC 전경 아트 개편 준비 인계

사용자가 배경을 제외한 도트의 전면 개편을 명시 요청했다. Codex가 이에 따라
통합 스타일 후보 2장 제작과 기존 QA/오케스트레이션을 수행했다. RENDERER 복귀 시
`ArtRevamp/SFC-20260930/BRIEF.md`, 생성 프롬프트/기록, 실제 전경 인벤토리를 리뷰한다.
640×360/PPU16을 유지하며 현재 크기·피벗·포구·파츠 정렬 기준으로 원본을 제작한다.

시안 v2를 추천했으나 현재는 사용자 스타일 선택 대기다. 그림은 불투명 스타일
리뷰 시트이며 원본 스프라이트가 아니다. §2/§9-1의 사람 아트 채택 절차는 생략하지
않았고, `Assets/Art/` 채택이나 사이트 배포는 하지 않았다. 기본 기체·대표 적·탄·
폭발을 원본 크기로 제작하고 큐레이션하는 것이 다음 단계다. 전체 전경 PNG 285개의
교체가 끝났다는 의미로 인계하지 않는다.

Unity CLI 읽기 전용 조사 성공, 기존 Core 611개 통과. 생산 코드·씬·게임 수치·배경은
변경하지 않았다. 프롬프트/참조 이미지/산출물 해시를 함께 남겼으나 생성 모델의
시드가 노출되지 않아 출력의 결정론적 재생성은 보장하지 않는다.

### 2026-09-30 SFC 2안 native 파일럿 대행분 리뷰 인계

사용자가 스타일 v2를 선택하고 PixelLab 후보 전송/기존 크레딧 US$1을 승인했다.
Codex가 명시적 아트 요청에 따른 후보 제작 및 RENDERER/QA/오케스트레이션 도구 작업을
대행했다. 원 담당 복귀 시 `Tools/ArtGen/artgen.py`의 native/animate 요청 기록·재개·
중복 생성 방지, 픽셀 감사 스크립트, Unity 정적 검수 스크립트와 첫 원본 후보를 리뷰한다.
`ArtRevamp/SFC-20260930/pilot/README.md`에 합격 제안/제외 근거와 검증 범위를 구분했다.

제안은 기체 05·드론·고속 적 v2·포탑 v2와 불꽃만 다른 엔진 2자세다. 드론 생성 루프는
몸체가 흔들려 제외했고 폭발은 원본 실험만 준비했다. 전체 원본/기체 뱅킹 완료로 보지 않는다.
사람의 새 원본 묶음 채택을 요청했으며, Assets/Art 채택과 runtime/씬 수정은 수행하지 않았다.
소스 수치·배경·기존 PNG 338개를 보존했다. Core 611개, 생성 도구 8개, Unity 배치 캡처
통과. 최종 비교는 기존 적 크기 계산식을 적용했다. 브라우저 실주행 및 게임 빌드는 미실시다.
사용 비용 관측값 US$0.111962457948, 신규 결제 없음. 생성 원본/프롬프트/해시를 함께 남긴다.

### 2026-09-30 SFC 첫 묶음 채택·씬 적용 대행분 리뷰 인계

사용자의 “계속 가자” 지시에 따라 Codex가 RENDERER/QA/오케스트레이션을 대행했다.
기체 05 + 엔진 01, 드론, 고속 적 v2, 포탑 v2의 원본 5개만 채택했다. 신규 파일로
추가하여 기존 PNG/GUID를 보존했고, 빌더와 Battle/Title의 참조·클립 테이블을 연결했다.
원 담당 복귀 시 BattleSceneBuilder의 채택 원본 로더와 legacy 프레임 차단,
SpriteAnimationTests의 씬 통합 회귀 검사, adoption 해시 검사 및 QA 스크립트를 리뷰한다.

Core 611개 / Unity 786개 통과. 18개 전후 표시 코드 캡처·판정/포구 오버레이·풀 재사용·
실제 Sprite import geometry를 확인했다. 실플레이 PASS는 아니며 UI를 숨긴 고정 배치다.
미제작 뱅킹/탄/캡슐/폭발 및 붉은 배경의 드론 장갑 가독성을 후속 검수한다.
새 유료 생성·배경·Core·게임 수치 변경은 없다. 채택/검증 근거는 CURATION.md와
ArtRevamp/SFC-20260930/adoption-review/VALIDATION.md에 남긴다.

최종 clean WebGL 소스 4767e78, rss-play 배포 98b4d5d, Pages 36698090375 성공.
Unity 생성 Atlas 인덱스 5개를 함께 보존했다. 공개 파일 4개의 크기/해시 일치와
버전 스탬프를 확인했으며, 실플레이 검증은 아직이다. 새 테스트판 링크와 전체 근거는
ArtRevamp/SFC-20260930/adoption-review/VALIDATION.md에 기록했다.

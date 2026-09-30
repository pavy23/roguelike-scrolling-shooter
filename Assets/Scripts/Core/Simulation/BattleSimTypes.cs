using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Shmup.Core.Generation;

namespace Shmup.Core.Simulation
{
    public static class SimSpace
    {
        public const int SubUnitsPerWorldUnit = 256;
        public const int TicksPerSecond = 60;

        // 640×360 뷰(월드 40×22.5u) 기준 플레이필드 (ROADMAP M0, REQ-005 요청 2).
        // 스폰/컬링 경계는 이 상수를 기준으로 파생시킨다.
        public const int PlayfieldHalfWidthSubUnits = 20 * SubUnitsPerWorldUnit;
        public const int PlayfieldHalfHeightSubUnits = 45 * SubUnitsPerWorldUnit / 4;
        public const int DespawnMarginSubUnits = 2 * SubUnitsPerWorldUnit;

        /// <summary>
        /// Lowest player-center Y that keeps the complete simulation hitbox in
        /// the 640x360 playfield. Presentation may draw a larger sprite, but it
        /// must never receive a Core center outside this visible range.
        /// </summary>
        public static int GetVisiblePlayerCenterMinY(int playerHalfHeight)
        {
            ValidatePlayerHalfHeight(playerHalfHeight);
            return -PlayfieldHalfHeightSubUnits + playerHalfHeight;
        }

        /// <summary>
        /// Highest player-center Y that keeps the complete simulation hitbox in
        /// the 640x360 playfield.
        /// </summary>
        public static int GetVisiblePlayerCenterMaxY(int playerHalfHeight)
        {
            ValidatePlayerHalfHeight(playerHalfHeight);
            return PlayfieldHalfHeightSubUnits - playerHalfHeight;
        }

        static void ValidatePlayerHalfHeight(int playerHalfHeight)
        {
            if (playerHalfHeight < 0
                || playerHalfHeight > PlayfieldHalfHeightSubUnits)
                throw new ArgumentOutOfRangeException(nameof(playerHalfHeight));
        }
    }

    /// <summary>
    /// 틱 처리 중 발생한 이산 사건 (REQ-005 요청 1). Presentation이 애니메이션/SFX로
    /// 번역한다 — 상태 차분으로 추측하지 말 것. 보스 계열 값은 보스 시스템용 예약.
    /// </summary>
    public enum SimEventType
    {
        EnemyHit = 0,
        /// <summary>
        /// EntityId = killed enemy id, X/Y = death point,
        /// Arg = multiplier-applied score actually awarded, saturated to int.MaxValue.
        /// </summary>
        EnemyKilled = 1,
        PlayerHit = 2,
        PlayerKilled = 3,
        CapsuleDropped = 4,
        CapsulePicked = 5,
        PowerUpLevelChanged = 6,
        BossSpawned = 7,
        BossPhaseChanged = 8,
        StageCleared = 9,
        /// <summary>Arg = (int)BulletKind. 발사음 SFX용 — 볼리당 1회.</summary>
        PlayerFired = 10,
        /// <summary>EntityId = bullet id, Arg = target enemy id.</summary>
        BulletRicocheted = 11,
        /// <summary>EntityId = killed enemy id, X/Y = explosion center, Arg = damage.</summary>
        KillExplosionTriggered = 12,
        /// <summary>EntityId = enemy bullet id, X/Y = bullet position, Arg = fixed graze score.</summary>
        GrazeScored = 13,
        /// <summary>EntityId = zero-based multiplier level, Arg = score multiplier.</summary>
        MultiplierChanged = 14,
        /// <summary>
        /// EntityId = obstacle id, X/Y = destruction point,
        /// Arg = multiplier-applied score actually awarded, saturated to int.MaxValue.
        /// </summary>
        ObstacleDestroyed = 15,
        /// <summary>EntityId = missile id, X/Y = impact point, Arg = explosion damage.</summary>
        MissileExploded = 16,
        /// <summary>EntityId = boss id, PartId = destroyed part id.</summary>
        BossPartDestroyed = 17,
        /// <summary>EntityId = boss id, PartId = regenerated part id.</summary>
        BossPartRegenerated = 18,
        /// <summary>EntityId = pickup id, Arg = stock after acquisition.</summary>
        BombAcquired = 19,
        /// <summary>EntityId = 0, Arg = stock after the change.</summary>
        BombStockChanged = 20,
        /// <summary>X/Y = activation center, Arg = visual effect radius.</summary>
        BombActivated = 21,
        /// <summary>X/Y = attempted activation position.</summary>
        BombActivationRejectedEmpty = 22,
        /// <summary>EntityId = laser id, Arg = (int)LaserSourceKind.</summary>
        LaserTelegraphStarted = 23,
        /// <summary>EntityId = laser id, Arg = full beam half-width.</summary>
        LaserFired = 24,
        /// <summary>EntityId = laser id.</summary>
        LaserEnded = 25,
        /// <summary>
        /// EntityId = rejected source entity id, Arg = configured laser cap.
        /// </summary>
        LaserCapacityExceeded = 26,
        /// <summary>X/Y = rejected spawn point, Arg = configured enemy cap.</summary>
        EnemyCapacityExceeded = 27,
        /// <summary>X/Y = rejected spawn point, Arg = configured obstacle cap.</summary>
        ObstacleCapacityExceeded = 28,
        /// <summary>EntityId = segment index, X/Y = clamped player point, Arg = damage.</summary>
        CorridorContact = 29,
        /// <summary>Arg = configured hard deadline tick.</summary>
        TimeLimitExpired = 30,
        /// <summary>
        /// EntityId = boss id, Arg = zero-based action phase. The phase's first
        /// volley is delayed by its configured telegraphTicks.
        /// </summary>
        BossAttackTelegraphed = 31,
        /// <summary>
        /// EntityId = boss id, Arg = configured enemy-bullet capacity.
        /// One event is emitted for a boss volley truncated by the hard cap.
        /// </summary>
        EnemyBulletCapacityExceeded = 32,
        /// <summary>
        /// EntityId = obstacle id, X/Y = impact point, Arg = remaining HP.
        /// Destructive hits emit ObstacleDestroyed instead.
        /// </summary>
        ObstacleDamaged = 33,
        /// <summary>
        /// EntityId = boss id, X/Y = hold point, Arg = movement warning ticks.
        /// Emitted at the start of every lungeReturn cycle.
        /// </summary>
        BossMovementTelegraphed = 34,
        /// <summary>EntityId = defeated mid-boss id, Arg = combat ticks.</summary>
        MidBossDefeated = 35,
        /// <summary>EntityId = regenerated obstacle id, X/Y = restored point, Arg = HP.</summary>
        ObstacleRegenerated = 36,
        /// <summary>EntityId = erased enemy-bullet id, Arg = blocking obstacle id.</summary>
        EnemyBulletBlocked = 37,
        /// <summary>
        /// EntityId = remaining shield stock, Arg = total run-clear shield bonus
        /// actually awarded, saturated to int.MaxValue.
        /// </summary>
        ShieldBonusAwarded = 38,
        /// <summary>EntityId = ghost id, X/Y = spawn point, Arg = fixed weapon level.</summary>
        GhostSpawned = 39,
        /// <summary>EntityId = ghost id, X/Y = final point, Arg = replayed tick count.</summary>
        GhostEnded = 40,
        /// <summary>EntityId = encounter id, Arg = warning duration ticks.</summary>
        WarshipWarningStarted = 41,
        /// <summary>EntityId = encounter id, PartId = group id, Arg = group index.</summary>
        WarshipGroupActivated = 42,
        /// <summary>EntityId = encounter id, PartId = core group id, Arg = opening ways.</summary>
        WarshipCoreBattleStarted = 43,
        /// <summary>EntityId = defeated form id, Arg = invulnerable transition ticks.</summary>
        BossFormTransitionStarted = 44,
        /// <summary>EntityId = new form id, Arg = zero-based form index, PartId = content form id.</summary>
        BossFormChanged = 45,
        /// <summary>EntityId = boss id, X/Y = suction source, PartId = source part id.</summary>
        SuctionStarted = 46,
        /// <summary>EntityId = boss id, X/Y = final suction source, PartId = source part id.</summary>
        SuctionEnded = 47,
        /// <summary>EntityId = chain id, X/Y = head spawn point, Arg = segment count.</summary>
        SegmentChainSpawned = 48,
        /// <summary>EntityId = chain id, X/Y = destroyed head point, Arg = segment count.</summary>
        SegmentChainDestroyed = 49,
        /// <summary>
        /// EntityId = boss id, X/Y = blocked bullet impact point,
        /// Arg = 0 damage, PartId = invulnerable part id.
        /// </summary>
        BossPartHitBlocked = 50,
        /// <summary>
        /// 근접 공격 예고. EntityId = 보스 id, X/Y = 그 파츠 위치,
        /// Arg = 예고 길이(틱), PartId = 예고 중인 파츠 id.
        ///
        /// 사람 지시 2026-08-05: 레비아탄 낫팔 휘두르기 / 브루드마더 촉수 찌르기에
        /// "번쩍임 등으로 사전 예고"가 있어야 한다. 근접 공격은 본체가 통째로
        /// 앞으로 밀고 들어오는 것이라, 예고가 없으면 피할 방법이 없다.
        /// </summary>
        BossPartMeleeTelegraphed = 51
    }

    /// <summary>One event that happened during the last Step. Coordinates are subunits.</summary>
    public readonly struct SimEvent
    {
        public SimEvent(SimEventType type, int entityId, int x, int y, int arg)
            : this(type, entityId, x, y, arg, null)
        {
        }

        public SimEvent(
            SimEventType type,
            int entityId,
            int x,
            int y,
            int arg,
            string partId)
            : this(
                type,
                entityId,
                x,
                y,
                arg,
                partId,
                BulletKind.MainShot,
                BossSignaturePattern.None,
                BossTelegraphKind.None)
        {
        }

        public SimEvent(
            SimEventType type,
            int entityId,
            int x,
            int y,
            int arg,
            string partId,
            BulletKind bulletKind,
            BossSignaturePattern signaturePattern,
            BossTelegraphKind telegraphKind)
        {
            Type = type;
            EntityId = entityId;
            X = x;
            Y = y;
            Arg = arg;
            PartId = partId;
            BulletKind = bulletKind;
            SignaturePattern = signaturePattern;
            TelegraphKind = telegraphKind;
        }

        public SimEventType Type { get; }
        public int EntityId { get; }
        public int X { get; }
        public int Y { get; }
        /// <summary>
        /// Event-specific value. EnemyHit/PlayerHit use damage;
        /// EnemyKilled/ObstacleDestroyed use the multiplier-applied score actually awarded
        /// (saturated to int.MaxValue); other meanings are documented on SimEventType.
        /// </summary>
        public int Arg { get; }
        /// <summary>
        /// Stable boss-part id for part events, or boss-form content id for
        /// BossFormTransitionStarted/BossFormChanged; null for legacy events.
        /// </summary>
        public string PartId { get; }
        /// <summary>Projectile vocabulary for boss telegraphs; legacy events use MainShot.</summary>
        public BulletKind BulletKind { get; }
        public BossSignaturePattern SignaturePattern { get; }
        public BossTelegraphKind TelegraphKind { get; }
    }

    /// <summary>
    /// Read-only counters observed from one BattleSim instance.
    /// Accuracy is intentionally left to consumers as ShotsHit / ShotsFired.
    /// </summary>
    public readonly struct BattleStatistics
    {
        internal BattleStatistics(
            long shotsFired,
            long shotsHit,
            long kills,
            long capsulesCollected,
            long grazeCount,
            long bombsUsed,
            long hitsTaken)
        {
            ShotsFired = shotsFired;
            ShotsHit = shotsHit;
            Kills = kills;
            CapsulesCollected = capsulesCollected;
            GrazeCount = grazeCount;
            BombsUsed = bombsUsed;
            HitsTaken = hitsTaken;
        }

        public long ShotsFired { get; }
        public long ShotsHit { get; }
        public long Kills { get; }
        public long CapsulesCollected { get; }
        public long GrazeCount { get; }
        public long BombsUsed { get; }
        /// <summary>
        /// Accepted player hits, including shield absorption and the lethal hit.
        /// Invulnerability-rejected contacts are not counted.
        /// </summary>
        public long HitsTaken { get; }
    }

    public enum BulletFaction { Player = 0, Enemy = 1 }
    public enum BulletKind
    {
        MainShot = 0,
        Missile = 1,
        EnemyShot = 2,
        Heavy = 3,
        Splitter = 4,
        Mine = 5,
        BossLaser = 6,
        /// <summary>Fixed low-level, straight primary shot fired by the St1 ghost.</summary>
        GhostMainShot = 7
    }

    public enum BossTelegraphKind
    {
        None = 0,
        Barrage = 1,
        Laser = 2
    }

    [Flags]
    public enum BattleModifier
    {
        None = 0,
        PierceShot = 1 << 0,
        Ricochet = 1 << 1,
        HomingMissile = 1 << 2,
        KillExplosion = 1 << 3
    }

    internal static class BattleModifierRules
    {
        internal const BattleModifier All =
            BattleModifier.PierceShot
            | BattleModifier.Ricochet
            | BattleModifier.HomingMissile
            | BattleModifier.KillExplosion;

        internal static readonly BattleModifier[] Ordered =
        {
            BattleModifier.PierceShot,
            BattleModifier.Ricochet,
            BattleModifier.HomingMissile,
            BattleModifier.KillExplosion
        };

        internal static bool IsSingleKnown(BattleModifier modifier)
        {
            int value = (int)modifier;
            return modifier != BattleModifier.None
                && (modifier & ~All) == 0
                && (value & (value - 1)) == 0;
        }
    }

    /// <summary>Observable boss state (REQ-007). Valid only while IBattleSim.BossActive.</summary>
    public readonly struct BossState
    {
        public BossState(int id, int x, int y, int hp, int maxHp, int phase)
            : this(
                id,
                x,
                y,
                hp,
                maxHp,
                phase,
                BossMovementPattern.LegacyHover,
                BossPartVulnerability.Legacy)
        {
        }

        public BossState(
            int id,
            int x,
            int y,
            int hp,
            int maxHp,
            int phase,
            BossMovementPattern movementPattern,
            BossPartVulnerability partVulnerability)
            : this(
                id,
                x,
                y,
                hp,
                maxHp,
                phase,
                movementPattern,
                partVulnerability,
                0)
        {
        }

        public BossState(
            int id,
            int x,
            int y,
            int hp,
            int maxHp,
            int phase,
            BossMovementPattern movementPattern,
            BossPartVulnerability partVulnerability,
            int formIndex)
        {
            Id = id;
            X = x;
            Y = y;
            Hp = hp;
            MaxHp = maxHp;
            Phase = phase;
            MovementPattern = movementPattern;
            PartVulnerability = partVulnerability;
            FormIndex = formIndex;
        }

        public int Id { get; }
        public int X { get; }
        public int Y { get; }
        public int Hp { get; }
        public int MaxHp { get; }
        public int Phase { get; }
        public BossMovementPattern MovementPattern { get; }
        public BossPartVulnerability PartVulnerability { get; }
        public int FormIndex { get; }
    }

    /// <summary>
    /// One tick of player input. The legacy digital axes are clamped to
    /// -1, 0, or 1. Commands created with the analog overload or
    /// <see cref="Analog"/> use integer SimSpace subunit deltas instead;
    /// analog movement wins over digital movement even when both deltas are zero.
    /// </summary>
    public readonly struct InputCommand
    {
        public InputCommand(int moveX, int moveY, bool fire)
            : this(moveX, moveY, fire, false)
        {
        }

        public InputCommand(
            int moveX,
            int moveY,
            bool fire,
            bool activate)
            : this(moveX, moveY, fire, activate, false)
        {
        }

        public InputCommand(
            int moveX,
            int moveY,
            bool fire,
            bool activate,
            bool activateBomb)
            : this(
                moveX,
                moveY,
                fire,
                activate,
                activateBomb,
                0,
                0,
                false)
        {
        }

        /// <summary>
        /// Creates a command carrying both legacy digital axes and an analog
        /// movement delta. The analog delta is expressed in SimSpace subunits
        /// for this simulation tick and takes precedence over the digital axes.
        /// </summary>
        public InputCommand(
            int moveX,
            int moveY,
            bool fire,
            bool activate,
            bool activateBomb,
            int analogDeltaXSubUnits,
            int analogDeltaYSubUnits)
            : this(
                moveX,
                moveY,
                fire,
                activate,
                activateBomb,
                analogDeltaXSubUnits,
                analogDeltaYSubUnits,
                true)
        {
        }

        InputCommand(
            int moveX,
            int moveY,
            bool fire,
            bool activate,
            bool activateBomb,
            int analogDeltaXSubUnits,
            int analogDeltaYSubUnits,
            bool useAnalogMovement)
        {
            MoveX = Clamp(moveX);
            MoveY = Clamp(moveY);
            Fire = fire;
            Activate = activate;
            ActivateBomb = activateBomb;
            AnalogDeltaXSubUnits = analogDeltaXSubUnits;
            AnalogDeltaYSubUnits = analogDeltaYSubUnits;
            UseAnalogMovement = useAnalogMovement;
        }

        public int MoveX { get; }
        public int MoveY { get; }
        public bool Fire { get; }
        public bool Activate { get; }
        public bool ActivateBomb { get; }
        public int AnalogDeltaXSubUnits { get; }
        public int AnalogDeltaYSubUnits { get; }
        public bool UseAnalogMovement { get; }
        public static InputCommand None => default;

        /// <summary>
        /// Creates an analog-only movement command. Deltas use
        /// SimSpace.SubUnitsPerWorldUnit and represent movement during one tick.
        /// </summary>
        public static InputCommand Analog(
            int deltaXSubUnits,
            int deltaYSubUnits,
            bool fire,
            bool activate = false,
            bool activateBomb = false)
        {
            return new InputCommand(
                0,
                0,
                fire,
                activate,
                activateBomb,
                deltaXSubUnits,
                deltaYSubUnits,
                true);
        }

        /// <summary>
        /// Returns this command with only the activate bit replaced.
        /// All movement modes and payload fields are preserved.
        /// </summary>
        public InputCommand WithActivate(bool activate)
        {
            return new InputCommand(
                MoveX,
                MoveY,
                Fire,
                activate,
                ActivateBomb,
                AnalogDeltaXSubUnits,
                AnalogDeltaYSubUnits,
                UseAnalogMovement);
        }

        static int Clamp(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;
    }

    public readonly struct BossPartState
    {
        internal BossPartState(
            string partId,
            int x,
            int y,
            int hp,
            int maxHp,
            bool destroyed,
            bool isCore,
            bool coreGated)
            : this(
                partId,
                x,
                y,
                hp,
                maxHp,
                destroyed,
                true,
                isCore,
                coreGated)
        {
        }

        internal BossPartState(
            string partId,
            int x,
            int y,
            int hp,
            int maxHp,
            bool destroyed,
            bool active,
            bool isCore,
            bool coreGated)
        {
            PartId = partId;
            X = x;
            Y = y;
            Hp = hp;
            MaxHp = maxHp;
            Destroyed = destroyed;
            Active = active;
            IsCore = isCore;
            Invulnerable = coreGated;
        }

        public string PartId { get; }
        public int X { get; }
        public int Y { get; }
        public int Hp { get; }
        public int MaxHp { get; }
        public bool Destroyed { get; }
        /// <summary>False while the current phase keeps this part hidden.</summary>
        public bool Active { get; }
        public bool IsCore { get; }
        /// <summary>
        /// True while the current boss phase prevents damage to this part.
        /// </summary>
        public bool Invulnerable { get; }
        /// <summary>
        /// Legacy compatibility alias. Phase rules may now cause invulnerability
        /// even when no predecessor core gate exists.
        /// </summary>
        public bool CoreGated => Invulnerable;
    }

    /// <summary>Observable bullet state in integer simulation subunits.</summary>
    public readonly struct BulletState
    {
        public BulletState(int id, BulletFaction faction, int x, int y)
            : this(id, faction, BulletKind.MainShot, x, y, 0)
        {
        }

        public BulletState(int id, BulletFaction faction, BulletKind kind, int x, int y)
            : this(id, faction, kind, x, y, 0)
        {
        }

        public BulletState(
            int id,
            BulletFaction faction,
            BulletKind kind,
            int x,
            int y,
            int ageTicks)
            : this(id, faction, kind, x, y, ageTicks, 100)
        {
        }

        public BulletState(
            int id,
            BulletFaction faction,
            BulletKind kind,
            int x,
            int y,
            int ageTicks,
            int damagePercent)
            : this(
                id,
                faction,
                kind,
                x,
                y,
                ageTicks,
                damagePercent,
                100,
                BossSignaturePattern.None)
        {
        }

        public BulletState(
            int id,
            BulletFaction faction,
            BulletKind kind,
            int x,
            int y,
            int ageTicks,
            int damagePercent,
            int collisionScalePercent,
            BossSignaturePattern signaturePattern)
            : this(
                id,
                faction,
                kind,
                x,
                y,
                ageTicks,
                damagePercent,
                collisionScalePercent,
                signaturePattern,
                0)
        {
        }

        public BulletState(
            int id,
            BulletFaction faction,
            BulletKind kind,
            int x,
            int y,
            int ageTicks,
            int damagePercent,
            int collisionScalePercent,
            BossSignaturePattern signaturePattern,
            int fixedDamage)
        {
            if (ageTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(ageTicks));
            if (damagePercent < 0)
                throw new ArgumentOutOfRangeException(nameof(damagePercent));
            if (collisionScalePercent < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(collisionScalePercent));
            if (fixedDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(fixedDamage));
            Id = id;
            Faction = faction;
            Kind = kind;
            X = x;
            Y = y;
            AgeTicks = ageTicks;
            DamagePercent = damagePercent;
            CollisionScalePercent = collisionScalePercent;
            SignaturePattern = signaturePattern;
            FixedDamage = fixedDamage;
        }

        public int Id { get; }
        public BulletFaction Faction { get; }
        public BulletKind Kind { get; }
        public int X { get; }
        public int Y { get; }
        public int AgeTicks { get; }
        /// <summary>Percent of configured damage dealt by this projectile.</summary>
        public int DamagePercent { get; }
        public int CollisionScalePercent { get; }
        public BossSignaturePattern SignaturePattern { get; }
        /// <summary>
        /// Exact damage for projectiles whose power must not inherit the current
        /// player loadout. Zero means normal weapon damage rules.
        /// </summary>
        public int FixedDamage { get; }
    }

    /// <summary>Observable option position in integer simulation subunits.</summary>
    public readonly struct OptionState
    {
        public OptionState(int index, int x, int y)
        {
            Index = index;
            X = x;
            Y = y;
        }

        /// <summary>Stable one-based index matching the option's gauge level.</summary>
        public int Index { get; }
        public int X { get; }
        public int Y { get; }
    }

    /// <summary>Observable enemy state in integer simulation subunits.</summary>
    public readonly struct EnemyState
    {
        public EnemyState(int id, string definitionId, int x, int y, int hp)
        {
            Id = id;
            DefinitionId = definitionId;
            X = x;
            Y = y;
            Hp = hp;
        }

        public int Id { get; }
        public string DefinitionId { get; }
        public int X { get; }
        public int Y { get; }
        public int Hp { get; }
    }

    /// <summary>
    /// One observable segment of a boss-owned chain minion. Segment zero is
    /// the only damageable segment; its destruction removes all sibling states.
    /// </summary>
    public readonly struct SegmentChainState
    {
        internal SegmentChainState(
            int chainId,
            int segmentIndex,
            int x,
            int y,
            int headHp,
            int headMaxHp)
        {
            ChainId = chainId;
            SegmentIndex = segmentIndex;
            X = x;
            Y = y;
            HeadHp = headHp;
            HeadMaxHp = headMaxHp;
        }

        public int ChainId { get; }
        public int SegmentIndex { get; }
        public int X { get; }
        public int Y { get; }
        public bool IsHead => SegmentIndex == 0;
        public bool Damageable => IsHead;
        public int HeadHp { get; }
        public int HeadMaxHp { get; }
    }

    internal sealed class SegmentChainRuntime
    {
        internal SegmentChainRuntime(
            int id,
            SegmentChainDefinition definition,
            int headMaxHp,
            int x,
            int y)
        {
            Id = id;
            Definition = definition;
            HeadHp = headMaxHp;
            HeadMaxHp = headMaxHp;
            HeadX = x;
            HeadY = y;
            DirectionX = -SineDirectionScale;
            DirectionY = 0;
            int capacity = checked(
                (definition.SegmentCount - 1)
                    * definition.FollowDelayTicks
                + 1);
            HistoryX = new int[capacity];
            HistoryY = new int[capacity];
            for (int i = 0; i < capacity; i++)
            {
                HistoryX[i] = x;
                HistoryY[i] = y;
            }
        }

        internal const int SineDirectionScale = 1024;
        internal int Id;
        internal SegmentChainDefinition Definition;
        internal int HeadHp;
        internal int HeadMaxHp;
        internal int HeadX;
        internal int HeadY;
        internal int DirectionX;
        internal int DirectionY;
        internal long MoveRemainderX;
        internal long MoveRemainderY;
        internal int[] HistoryX;
        internal int[] HistoryY;
        internal int HistoryHead;
    }

    /// <summary>Observable stage obstacle state in integer simulation subunits.</summary>
    public readonly struct ObstacleState
    {
        public ObstacleState(int id, ObstacleType type, int x, int y, int hp)
            : this(id, type, x, y, hp, 0, 0)
        {
        }

        /// <summary>
        /// 반폭·반높이가 0이면 설정 기본값을 쓴다. 장애물마다 크기를 실을 수
        /// 있어야 "스테이지 2부터만 크게"가 성립한다 — 전역 상수 하나로는
        /// 입문 구간까지 함께 커진다.
        /// </summary>
        public ObstacleState(
            int id, ObstacleType type, int x, int y, int hp,
            int halfWidth, int halfHeight)
        {
            Id = id;
            Type = type;
            X = x;
            Y = y;
            Hp = hp;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        public int Id { get; }
        public ObstacleType Type { get; }
        public int X { get; }
        public int Y { get; }
        /// <summary>Remaining HP for breakable obstacles; zero for solid obstacles.</summary>
        public int Hp { get; }
        /// <summary>0이면 BattleSimConfig의 기본 크기를 쓴다.</summary>
        public int HalfWidth { get; }
        public int HalfHeight { get; }
    }

    /// <summary>
    /// Destroyed breakable obstacle that still owns its capacity slot until its
    /// deterministic respawn tick. It is non-colliding while in this list.
    /// </summary>
    public readonly struct ObstacleRegenerationState
    {
        public ObstacleRegenerationState(
            int id,
            ObstacleType type,
            int x,
            int y,
            int maxHp,
            bool blocksEnemyBullets,
            int regenDelayTicks,
            int respawnAtTick,
            int halfWidth = 0,
            int halfHeight = 0)
        {
            Id = id;
            Type = type;
            X = x;
            Y = y;
            MaxHp = maxHp;
            BlocksEnemyBullets = blocksEnemyBullets;
            RegenDelayTicks = regenDelayTicks;
            RespawnAtTick = respawnAtTick;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        public int Id { get; }
        public ObstacleType Type { get; }
        public int X { get; }
        public int Y { get; }
        public int MaxHp { get; }
        /// <summary>되살아날 때 쓸 크기. 0이면 설정 기본값.</summary>
        public int HalfWidth { get; }
        public int HalfHeight { get; }
        public bool BlocksEnemyBullets { get; }
        public int RegenDelayTicks { get; }
        public int RespawnAtTick { get; }

        public ObstacleRegenerationState WithRespawnAtTick(int tick)
        {
            return new ObstacleRegenerationState(
                Id,
                Type,
                X,
                Y,
                MaxHp,
                BlocksEnemyBullets,
                RegenDelayTicks,
                tick);
        }
    }

    /// <summary>
    /// Observable segment environment in integer simulation subunits.
    /// SegmentIndex is -1 outside a stage segment (for example, in a boss arena).
    /// </summary>
    public readonly struct StageEnvironmentState
    {
        public StageEnvironmentState(
            int segmentIndex,
            string segmentId,
            bool hasCorridor,
            int corridorMinY,
            int corridorMaxY,
            int corridorContactDamage,
            int driftXNumerator,
            int driftXDenominator,
            int driftYNumerator,
            int driftYDenominator)
        {
            SegmentIndex = segmentIndex;
            SegmentId = segmentId;
            HasCorridor = hasCorridor;
            CorridorMinY = corridorMinY;
            CorridorMaxY = corridorMaxY;
            CorridorContactDamage = corridorContactDamage;
            DriftXNumerator = driftXNumerator;
            DriftXDenominator = driftXDenominator;
            DriftYNumerator = driftYNumerator;
            DriftYDenominator = driftYDenominator;
        }

        public int SegmentIndex { get; }
        public string SegmentId { get; }
        public bool HasCorridor { get; }
        public int CorridorMinY { get; }
        public int CorridorMaxY { get; }
        public int CorridorContactDamage { get; }
        public int DriftXNumerator { get; }
        public int DriftXDenominator { get; }
        public int DriftYNumerator { get; }
        public int DriftYDenominator { get; }
        public bool HasDrift =>
            DriftXNumerator != 0 || DriftYNumerator != 0;
    }

    public enum LaserSourceKind
    {
        Enemy = 0,
        Terrain = 1,
        Player = 2,
        Boss = 3,
        /// <summary>SourceEntityId is the zero-based current boss-part index.</summary>
        BossPart = 4
    }

    public enum LaserPhase
    {
        Telegraph = 0,
        Firing = 1,
        Sustaining = 2,
        Dissipating = 3
    }

    public enum LaserThicknessStage
    {
        Telegraph = 0,
        Thin = 1,
        Full = 2
    }

    /// <summary>
    /// Observable hostile laser segment. Firing and Sustaining phases damage;
    /// Telegraph and Dissipating phases are presentation-only warnings/fades.
    /// </summary>
    public readonly struct LaserState
    {
        public LaserState(
            int id,
            LaserSourceKind sourceKind,
            int sourceEntityId,
            int startX,
            int startY,
            int endX,
            int endY,
            LaserPhase phase,
            LaserThicknessStage thicknessStage,
            int halfWidth,
            int phaseTicksRemaining,
            int damage,
            int fullHalfWidth = 0)
        {
            Id = id;
            SourceKind = sourceKind;
            SourceEntityId = sourceEntityId;
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
            Phase = phase;
            ThicknessStage = thicknessStage;
            HalfWidth = halfWidth;
            FullHalfWidth = Math.Max(halfWidth, fullHalfWidth);
            PhaseTicksRemaining = phaseTicksRemaining;
            Damage = damage;
        }

        public int Id { get; }
        public LaserSourceKind SourceKind { get; }
        public int SourceEntityId { get; }
        public int StartX { get; }
        public int StartY { get; }
        public int EndX { get; }
        public int EndY { get; }
        public LaserPhase Phase { get; }
        public LaserThicknessStage ThicknessStage { get; }
        public int HalfWidth { get; }
        /// <summary>
        /// Width to warn about before the hostile beam reaches Sustaining.
        /// Derived from its attack definition; HalfWidth remains the current
        /// collision width. Player beams default to their current growing width.
        /// </summary>
        public int FullHalfWidth { get; }
        public int PhaseTicksRemaining { get; }
        public int Damage { get; }
        public bool IsDamaging =>
            Phase == LaserPhase.Firing
            || Phase == LaserPhase.Sustaining;
    }

    public readonly struct BombPickupState
    {
        public BombPickupState(int id, int x, int y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        public int Id { get; }
        public int X { get; }
        public int Y { get; }
    }

    public static class LaserGeometry
    {
        /// <summary>
        /// Division-free segment-versus-circle test. Very large inputs are
        /// deterministically scaled by powers of two before squared products;
        /// the radius rounds outward so overflow protection never shrinks the
        /// hazardous beam.
        /// </summary>
        public static bool IntersectsSegmentCircle(
            int startX,
            int startY,
            int endX,
            int endY,
            int circleX,
            int circleY,
            int radius)
        {
            if (radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            long vx = (long)endX - startX;
            long vy = (long)endY - startY;
            long wx = (long)circleX - startX;
            long wy = (long)circleY - startY;
            long scaledRadius = radius;
            while (MaxAbs(vx, vy, wx, wy, scaledRadius) > 16_384)
            {
                vx /= 2;
                vy /= 2;
                wx /= 2;
                wy /= 2;
                scaledRadius = (scaledRadius + 1) / 2;
            }

            long radiusSquared =
                scaledRadius * scaledRadius;
            long segmentLengthSquared =
                vx * vx + vy * vy;
            if (segmentLengthSquared == 0)
                return wx * wx + wy * wy <= radiusSquared;

            long projection = wx * vx + wy * vy;
            if (projection <= 0)
                return wx * wx + wy * wy <= radiusSquared;
            if (projection >= segmentLengthSquared)
            {
                long ex = wx - vx;
                long ey = wy - vy;
                return ex * ex + ey * ey <= radiusSquared;
            }

            long cross = vx * wy - vy * wx;
            return cross * cross
                <= radiusSquared * segmentLengthSquared;
        }

        static long MaxAbs(
            long a,
            long b,
            long c,
            long d,
            long e)
        {
            long max = Abs(a);
            max = Math.Max(max, Abs(b));
            max = Math.Max(max, Abs(c));
            max = Math.Max(max, Abs(d));
            return Math.Max(max, e);
        }

        static long Abs(long value)
        {
            return value < 0 ? -value : value;
        }
    }

    /// <summary>Observable capsule state in integer simulation subunits.</summary>
    public readonly struct CapsuleState
    {
        public CapsuleState(int id, int x, int y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        public int Id { get; }
        public int X { get; }
        public int Y { get; }
    }
}

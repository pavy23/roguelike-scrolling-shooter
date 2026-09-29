using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Shmup.Core.Generation;

namespace Shmup.Core.Simulation
{
    public sealed partial class BattleSim
    {
        void CompleteWarshipTick()
        {
            if (_warshipEncounter == null)
                return;
            _warshipEncounter.CompleteTick();
            SyncWarshipPositionAndVulnerability();
            ForwardWarshipEvents();
        }

        void SyncWarshipPositionAndVulnerability()
        {
            _bossX = _warshipEncounter.WorldX;
            // The hull stages vertically between acts (REQ-139), so the body
            // origin has to follow the anchor - otherwise the hit box stays put
            // while the art moves, which is the exact mismatch that produced
            // five "no damage" reports on the stern (see commit c3df07c).
            _bossY = _warshipDefinition.OriginY
                + _warshipEncounter.AnchorOffsetY;
            RefreshBossPartPositions();
        }

        void ForwardWarshipEvents()
        {
            if (_warshipEncounter == null)
                return;
            ArraySegment<SimEvent> events =
                _warshipEncounter.EventsThisTick;
            while (_warshipEventCursor < events.Count)
            {
                SimEvent simEvent = events.Array[
                    events.Offset + _warshipEventCursor++];
                switch (simEvent.Type)
                {
                    case SimEventType.WarshipWarningStarted:
                    case SimEventType.WarshipGroupActivated:
                    case SimEventType.WarshipCoreBattleStarted:
                    case SimEventType.MidBossDefeated:
                        AppendEvent(in simEvent);
                        break;
                }
            }
        }

        void RestoreBattlePartsFromWarship()
        {
            IReadOnlyList<WarshipPartState> parts =
                _warshipEncounter.Parts;
            int aggregateHp = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                WarshipPartState restored = parts[i];
                BossPartDefinition definition =
                    _bossPartDefinitions[i];
                _bossPartStates[i] = new BossPartState(
                    restored.PartId,
                    restored.X,
                    restored.Y,
                    restored.Hp,
                    restored.MaxHp,
                    restored.Destroyed,
                    restored.Active,
                    definition.IsCore,
                    restored.Invulnerable);
                aggregateHp = SaturatingAddDamage(
                    aggregateHp,
                    restored.Hp);
                _bossPartsEverDestroyed[i] = restored.Destroyed;
                _bossPartRegenerationRemaining[i] = 0;
                _bossPartContactHitThisCycle[i] = false;
                int interval = definition.Attack.IntervalTicks;
                if (interval == 0
                    || restored.Destroyed
                    || !restored.Active)
                    _bossPartFireCooldowns[i] = interval;
                else
                {
                    int elapsed =
                        _warshipEncounter.ActiveGroupElapsedTicks;
                    int remainder = elapsed % interval;
                    _bossPartFireCooldowns[i] = remainder == 0
                        ? interval
                        : interval - remainder;
                }
                Simulation.LaserAttackDefinition secondary =
                    definition.Attack.SecondaryLaser;
                if (secondary == null
                    || restored.Destroyed
                    || !restored.Active)
                {
                    _bossPartSecondaryLaserCooldowns[i] =
                        secondary == null
                            ? 0
                            : secondary.CycleIntervalTicks;
                }
                else
                {
                    int elapsed =
                        _warshipEncounter.ActiveGroupElapsedTicks;
                    int secondaryInterval = secondary.CycleIntervalTicks;
                    int remainder = elapsed % secondaryInterval;
                    _bossPartSecondaryLaserCooldowns[i] = remainder == 0
                        ? secondaryInterval
                        : secondaryInterval - remainder;
                }
                BossPartBurstDefinition burst =
                    definition.Attack.SecondaryBurst;
                if (burst == null
                    || restored.Destroyed
                    || !restored.Active)
                {
                    _bossPartSecondaryBurstCooldowns[i] =
                        burst == null ? 0 : burst.CycleIntervalTicks;
                }
                else
                {
                    int elapsed =
                        _warshipEncounter.ActiveGroupElapsedTicks;
                    int burstInterval = burst.CycleIntervalTicks;
                    int remainder = elapsed % burstInterval;
                    _bossPartSecondaryBurstCooldowns[i] = remainder == 0
                        ? burstInterval
                        : burstInterval - remainder;
                }
            }
            _bossHp = aggregateHp;
            if (!_warshipEncounter.Completed)
            {
                SyncWarshipPositionAndVulnerability();
                return;
            }

            _bossHp = 0;
            SyncWarshipPositionAndVulnerability();
            // 함체를 다 부수면 안에서 로봇이 나온다 (REQ-139 3막, 사람 지시).
            // 새 개념을 만들지 않고 이미 있는 2단 폼 경로를 그대로 탄다 -
            // 로봇은 별도 엔티티가 아니라 같은 보스의 두 번째 폼이다. 리플레이·
            // 저장 경로가 하나로 유지되고, 데이터도 기존 form2 스키마로 쓴다.
            if (!BeginWarshipFormTransition())
                _bossDefeated = true;
        }

        /// <summary>
        /// 함체 격파 후 두 번째 폼(로봇)으로 넘어간다. 폼이 없으면 false를 돌려
        /// 호출부가 평소대로 보스 격파 처리를 하게 한다.
        /// </summary>
        bool BeginWarshipFormTransition()
        {
            if (_bossFormIndex != 0 || _bossForm2 == null)
                return false;
            int defeatedFormId = _bossId;
            int x = _bossX;
            int y = _bossY;
            // 함체는 여기서 끝난다. 참조를 끊지 않으면 다음 틱에도 죽은 조우가
            // 계속 돌아 파츠를 되살린다.
            _warshipEncounter = null;
            _warshipEventCursor = 0;
            _bossSpawned = false;
            _bossTransitionTicksRemaining = _bossForm2.TransitionTicks;
            EmitBossFormEvent(
                SimEventType.BossFormTransitionStarted,
                defeatedFormId,
                x,
                y,
                _bossTransitionTicksRemaining,
                _bossForm2.FormId);
            return true;
        }

        static int AdvancePositiveFraction(
            int ticks,
            int numerator,
            int denominator)
        {
            long value = (long)ticks * numerator / denominator;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        void RegenerateBossParts()
        {
            for (int i = 0; i < _bossPartDefinitions.Count; i++)
            {
                if (!_bossPartStates[i].Destroyed
                    || _bossPartRegenerationRemaining[i] <= 0)
                    continue;
                _bossPartRegenerationRemaining[i]--;
                if (_bossPartRegenerationRemaining[i] != 0)
                    continue;

                BossPartState previous = _bossPartStates[i];
                _bossPartStates[i] = new BossPartState(
                    previous.PartId,
                    previous.X,
                    previous.Y,
                    previous.MaxHp,
                    previous.MaxHp,
                    false,
                    IsBossPartActive(i),
                    previous.IsCore,
                    false);
                _bossHp = SaturatingAddDamage(
                    _bossHp,
                    previous.MaxHp);
                BossPartAttackProfile attack = GetBossPartAttack(i);
                _bossPartFireCooldowns[i] = attack.IntervalTicks;
                _bossPartSecondaryLaserCooldowns[i] =
                    attack.SecondaryLaser == null
                        ? 0
                        : attack.SecondaryLaser.CycleIntervalTicks;
                _bossPartSecondaryBurstCooldowns[i] =
                    attack.SecondaryBurst == null
                        ? 0
                        : attack.SecondaryBurst.CycleIntervalTicks;
                _bossPartContactHitThisCycle[i] = false;
                EmitBossPartEvent(
                    SimEventType.BossPartRegenerated,
                    previous.X,
                    previous.Y,
                    i);
            }
        }

        void RefreshBossPartPositions()
        {
            for (int i = 0; i < _bossPartDefinitions.Count; i++)
            {
                BossPartDefinition definition =
                    _bossPartDefinitions[i];
                BossPartState state = _bossPartStates[i];
                bool active = _warshipEncounter != null
                    ? _warshipEncounter.IsPartActive(definition.PartId)
                    : IsBossPartActive(i);
                _bossPartStates[i] = new BossPartState(
                    state.PartId,
                    SaturateToInt((long)_bossX + definition.OffsetX),
                    SaturateToInt((long)_bossY + definition.OffsetY),
                    state.Hp,
                    state.MaxHp,
                    state.Destroyed,
                    active,
                    state.IsCore,
                    IsBossPartInvulnerable(i));
            }
        }

        bool IsBossPartInvulnerable(int partIndex)
        {
            if (_warshipEncounter != null)
                return !_warshipEncounter.IsPartActive(
                    _bossPartDefinitions[partIndex].PartId);
            if (BossEntering)
                return true;
            BossPhasePartRule rule = FindBossPhasePartRule(partIndex);
            if (rule != null)
                return !rule.Active || rule.Invulnerable;
            BossPartVulnerability vulnerability =
                _bossPhases[_bossPhase].PartVulnerability;
            switch (vulnerability)
            {
                case BossPartVulnerability.Legacy:
                    return IsBossCoreGated(partIndex);
                case BossPartVulnerability.CoreOnly:
                    return !_bossPartDefinitions[partIndex].IsCore
                        || IsBossCoreGated(partIndex);
                case BossPartVulnerability.All:
                    return false;
                default:
                    throw new InvalidOperationException(
                        $"Unknown boss part vulnerability "
                        + $"{vulnerability}.");
            }
        }

        bool IsBossPartActive(int partIndex)
        {
            BossPhasePartRule rule = FindBossPhasePartRule(partIndex);
            return rule == null || rule.Active;
        }

        BossPartAttackProfile GetBossPartAttack(int partIndex)
        {
            BossPhasePartRule rule = FindBossPhasePartRule(partIndex);
            return rule != null && rule.Attack != null
                ? rule.Attack
                : _bossPartDefinitions[partIndex].Attack;
        }

        BossPhasePartRule FindBossPhasePartRule(int partIndex)
        {
            if (_bossPhases.Count == 0)
                return null;
            string partId = _bossPartDefinitions[partIndex].PartId;
            IReadOnlyList<BossPhasePartRule> rules =
                _bossPhases[_bossPhase].PartRules;
            for (int i = 0; i < rules.Count; i++)
                if (string.Equals(
                        rules[i].PartId,
                        partId,
                        StringComparison.Ordinal))
                    return rules[i];
            return null;
        }

        bool IsBossCoreGated(int partIndex)
        {
            BossPartDefinition definition =
                _bossPartDefinitions[partIndex];
            if (!definition.IsCore)
                return false;
            for (int gate = 0;
                gate < definition.CoreGatePartIds.Count;
                gate++)
            {
                string gateId =
                    definition.CoreGatePartIds[gate];
                for (int i = 0;
                    i < _bossPartDefinitions.Count;
                    i++)
                {
                    if (string.Equals(
                            _bossPartDefinitions[i].PartId,
                            gateId,
                            StringComparison.Ordinal)
                        && !_bossPartStates[i].Destroyed)
                        return true;
                }
            }
            return false;
        }

        void FireBossPartAttack(
            int partIndex,
            BossPartAttackProfile attack)
        {
            BossPartState part = _bossPartStates[partIndex];
            if (attack.Type == BossPartAttackType.Laser)
            {
                TryStartLaser(
                    LaserSourceKind.BossPart,
                    partIndex,
                    attack.LaserAttack,
                    part.X,
                    part.Y);
                return;
            }
            if (attack.Type == BossPartAttackType.SpawnEnemy)
            {
                SpawnBossEnemy(
                    _battleContent.FindEnemy(attack.SpawnEnemyId),
                    part.X,
                    part.Y);
                return;
            }

            int ways = attack.Ways;
            if (_warshipEncounter != null
                && part.IsCore
                && (attack.Type == BossPartAttackType.AimedSpread
                    || attack.Type == BossPartAttackType.RadialSpread))
            {
                int openingWays =
                    _warshipEncounter.ConsumeCoreOpeningWays();
                if (openingWays > 0)
                    ways = openingWays;
            }
            int available = Math.Max(
                0,
                _maxEnemyBullets - CountEnemyBullets());
            int shots = Math.Min(ways, available);
            for (int i = 0; i < shots; i++)
            {
                int targetX = PlayerX;
                int targetY = PlayerY;
                int rotation;
                if (attack.Type == BossPartAttackType.RadialSpread)
                {
                    rotation = (int)(
                        (long)i * SineLut.Length
                        / ways);
                    int sin = SineLut[rotation];
                    int cos = SineLut[
                        (rotation + SineLut.Length / 4)
                        % SineLut.Length];
                    targetX = SaturateToInt((long)part.X + cos);
                    targetY = SaturateToInt((long)part.Y + sin);
                    rotation = 0;
                }
                else
                {
                    long centeredIndex =
                        2L * i - (ways - 1L);
                    rotation = (int)(
                        (centeredIndex * SpreadStepLutSlots / 2)
                        % SineLut.Length);
                }
                SpawnEnemyAimedBullet(
                    part.X,
                    part.Y,
                    targetX,
                    targetY,
                    attack.BulletSpeedNumerator,
                    attack.BulletSpeedDenominator,
                    rotation);
            }
        }

        void RefreshSuctionLifecycle()
        {
            int activePart = FindActiveSuctionPart();
            if (_bossSuctionActive && activePart != _bossSuctionPartIndex)
            {
                EmitSuctionEvent(
                    SimEventType.SuctionEnded,
                    _bossSuctionSourceX,
                    _bossSuctionSourceY,
                    _bossSuctionPartId);
                ResetSuctionForce();
            }
            if (!_bossSuctionActive && activePart >= 0)
            {
                _bossSuctionActive = true;
                _bossSuctionPartIndex = activePart;
                _bossSuctionPartId =
                    _bossPartDefinitions[activePart].PartId;
                BossPartState started = _bossPartStates[activePart];
                BossPartAttackProfile startedAttack =
                    GetBossPartAttack(activePart);
                _bossSuctionSourceX =
                    GetSuctionSourceX(started, startedAttack);
                _bossSuctionSourceY =
                    GetSuctionSourceY(started, startedAttack);
                EmitSuctionEvent(
                    SimEventType.SuctionStarted,
                    _bossSuctionSourceX,
                    _bossSuctionSourceY,
                    _bossSuctionPartId);
            }
            else if (_bossSuctionActive && activePart >= 0)
            {
                BossPartState active = _bossPartStates[activePart];
                BossPartAttackProfile activeAttack =
                    GetBossPartAttack(activePart);
                _bossSuctionSourceX =
                    GetSuctionSourceX(active, activeAttack);
                _bossSuctionSourceY =
                    GetSuctionSourceY(active, activeAttack);
            }
        }

        static int GetSuctionSourceX(
            BossPartState part,
            BossPartAttackProfile attack)
        {
            return SaturateToInt((long)part.X + attack.EffectOffsetX);
        }

        static int GetSuctionSourceY(
            BossPartState part,
            BossPartAttackProfile attack)
        {
            return SaturateToInt((long)part.Y + attack.EffectOffsetY);
        }

        int FindActiveSuctionPart()
        {
            if (!BossActive || BossEntering || BossTransitioning)
                return -1;
            for (int i = 0; i < _bossPartDefinitions.Count; i++)
            {
                BossPartState state = _bossPartStates[i];
                if (state.Destroyed
                    || !state.Active
                    || IsBossPartInvulnerable(i))
                    continue;
                if (GetBossPartAttack(i).Type
                    == BossPartAttackType.Suction)
                    return i;
            }
            return -1;
        }

        void EmitSuctionEvent(
            SimEventType type,
            int x,
            int y,
            string partId)
        {
            if (_eventCount == _events.Length)
                throw new InvalidOperationException(
                    "The preallocated simulation event buffer is exhausted.");
            _events[_eventCount++] = new SimEvent(
                type,
                _bossId,
                x,
                y,
                0,
                partId);
        }

        void ResetSuctionForce()
        {
            _bossSuctionActive = false;
            _bossSuctionPartIndex = -1;
            _bossSuctionPartId = null;
            _bossSuctionSourceX = 0;
            _bossSuctionSourceY = 0;
            _bossSuctionDeltaX = 0;
            _bossSuctionDeltaY = 0;
            _bossSuctionAccelerationXRemainder = 0;
            _bossSuctionAccelerationYRemainder = 0;
        }

        /// <summary>
        /// 근접 공격의 **돌진 구간 안에서 몇 틱째인가.** 0 미만이면 아직 예고 중,
        /// chargeTicks 이상이면 돌진이 끝났다.
        ///
        /// 이동과 접촉 판정이 **반드시 같은 창**을 봐야 한다. 예고를 넣을 때 이동
        /// 쪽만 고치고 접촉을 그대로 뒀더니 창이 정확히 반대가 됐다 — 멈춰서
        /// 경고하는 동안 맞고 정작 밀고 들어올 때는 안 맞았다. 두 곳이 각자 식을
        /// 쓰는 한 언제든 다시 어긋나므로, 식을 하나만 둔다.
        /// </summary>
        static int MeleeChargeCycle(
            BossPartAttackProfile attack, int bossAge, out int chargeTicks)
        {
            chargeTicks = Math.Max(1, attack.IntervalTicks / 4);
            return (bossAge % attack.IntervalTicks) - attack.MeleeTelegraphTicks;
        }

        void ApplyBossMeleeContact(
            int partIndex,
            BossPartAttackProfile attack)
        {
            int cycle = _bossAge % attack.IntervalTicks;
            if (cycle == 0)
                _bossPartContactHitThisCycle[partIndex] = false;
            int chargeCycle = MeleeChargeCycle(attack, _bossAge, out int chargeTicks);
            if (chargeCycle < 0
                || chargeCycle >= chargeTicks
                || _bossPartContactHitThisCycle[partIndex]
                || attack.ContactDamage == 0)
                return;

            BossPartState part = _bossPartStates[partIndex];
            BossPartDefinition definition =
                _bossPartDefinitions[partIndex];
            if (!Intersects(
                    PlayerX,
                    PlayerY,
                    _playerHalfWidth,
                    _playerHalfHeight,
                    part.X,
                    part.Y,
                    definition.HalfWidth,
                    definition.HalfHeight))
                return;

            _bossPartContactHitThisCycle[partIndex] = true;
            ApplyPlayerHit(attack.ContactDamage);
        }

        void ResolvePlayerBulletBossCollisions()
        {
            if (!BossActive || BossEntering) return;

            int bulletIndex = 0;
            while (bulletIndex < _bullets.Count)
            {
                BulletState bullet = _bullets[bulletIndex];
                if (bullet.Faction != BulletFaction.Player)
                {
                    bulletIndex++;
                    continue;
                }

                int bulletHalfWidth = bullet.Kind == BulletKind.Missile
                    ? _missileHalfWidth : _playerBulletHalfWidth;
                int bulletHalfHeight = bullet.Kind == BulletKind.Missile
                    ? _missileHalfHeight : _playerBulletHalfHeight;
                int partIndex = _bossPartDefinitions.Count == 0
                    ? -1
                    : FindBossPartHit(
                        bullet.X,
                        bullet.Y,
                        bulletHalfWidth,
                        bulletHalfHeight);
                bool legacyHit = _bossPartDefinitions.Count == 0
                    && Intersects(
                        bullet.X,
                        bullet.Y,
                        bulletHalfWidth,
                        bulletHalfHeight,
                        _bossX,
                        _bossY,
                        _bossHalfWidth,
                        _bossHalfHeight);
                if (partIndex < 0 && !legacyHit)
                {
                    bulletIndex++;
                    continue;
                }

                RemoveBulletAt(bulletIndex);
                if (partIndex >= 0
                    && IsBossPartInvulnerable(partIndex))
                {
                    AppendEvent(new SimEvent(
                        SimEventType.BossPartHitBlocked,
                        _bossId,
                        bullet.X,
                        bullet.Y,
                        0,
                        _bossPartDefinitions[partIndex].PartId));
                }
                int damage = bullet.Kind == BulletKind.Missile
                    ? ComputeMissileDamage(
                        _missileBaseDamage,
                        bullet.DamagePercent)
                    : ComputeMainShotDamage(in bullet);
                bool defeated = partIndex >= 0
                    ? ApplyDamageToBossPart(partIndex, damage)
                    : ApplyDamageToBoss(damage);
                if (!defeated
                    && bullet.Kind == BulletKind.Missile
                    && _missileFamily == MissileFamily.SpreadBomb)
                {
                    ApplyMissileExplosion(
                        bullet.Id,
                        bullet.X,
                        bullet.Y,
                        bullet.DamagePercent);
                    defeated = _bossDefeated;
                }
                if (defeated)
                    return;
            }
        }

        void ResolvePlayerBulletSegmentChainCollisions()
        {
            int bulletIndex = 0;
            while (bulletIndex < _bullets.Count)
            {
                BulletState bullet = _bullets[bulletIndex];
                if (bullet.Faction != BulletFaction.Player)
                {
                    bulletIndex++;
                    continue;
                }
                int chainIndex = FindSegmentChainHeadHit(
                    bullet.X,
                    bullet.Y,
                    bullet.Kind == BulletKind.Missile
                        ? _missileHalfWidth
                        : _playerBulletHalfWidth,
                    bullet.Kind == BulletKind.Missile
                        ? _missileHalfHeight
                        : _playerBulletHalfHeight);
                if (chainIndex < 0)
                {
                    bulletIndex++;
                    continue;
                }

                int damage = bullet.Kind == BulletKind.Missile
                    ? ComputeMissileDamage(
                        _missileBaseDamage,
                        bullet.DamagePercent)
                    : ComputeMainShotDamage(in bullet);
                RemoveBulletAt(bulletIndex);
                ApplyDamageToSegmentChain(chainIndex, damage);
            }
        }

        int FindSegmentChainHeadHit(
            int x,
            int y,
            int halfWidth,
            int halfHeight)
        {
            for (int i = 0; i < _segmentChainRuntimes.Count; i++)
            {
                SegmentChainRuntime chain = _segmentChainRuntimes[i];
                if (Intersects(
                        x,
                        y,
                        halfWidth,
                        halfHeight,
                        chain.HeadX,
                        chain.HeadY,
                        chain.Definition.HalfWidth,
                        chain.Definition.HalfHeight))
                    return i;
            }
            return -1;
        }

        bool ApplyDamageToSegmentChain(int chainIndex, int damage)
        {
            if (chainIndex < 0
                || chainIndex >= _segmentChainRuntimes.Count
                || damage <= 0)
                return false;
            SegmentChainRuntime chain =
                _segmentChainRuntimes[chainIndex];
            int hp = Damage.ApplyToHp(chain.HeadHp, damage);
            int applied = chain.HeadHp - hp;
            chain.HeadHp = hp;
            EmitEvent(
                SimEventType.EnemyHit,
                chain.Id,
                chain.HeadX,
                chain.HeadY,
                applied);
            if (hp > 0)
            {
                RebuildSegmentChainStates();
                return false;
            }

            EmitEvent(
                SimEventType.SegmentChainDestroyed,
                chain.Id,
                chain.HeadX,
                chain.HeadY,
                chain.Definition.SegmentCount);
            _segmentChainRuntimes.RemoveAt(chainIndex);
            RebuildSegmentChainStates();
            return true;
        }

        int FindBossPartHit(
            int x,
            int y,
            int halfWidth,
            int halfHeight)
        {
            for (int i = 0; i < _bossPartDefinitions.Count; i++)
            {
                BossPartState part = _bossPartStates[i];
                if (part.Destroyed || !part.Active)
                    continue;
                BossPartDefinition definition =
                    _bossPartDefinitions[i];
                if (Intersects(
                        x,
                        y,
                        halfWidth,
                        halfHeight,
                        part.X,
                        part.Y,
                        definition.HalfWidth,
                        definition.HalfHeight))
                    return i;
            }
            return -1;
        }

        bool ApplyDamageToBossPart(int partIndex, int damage)
        {
            if (!BossActive || BossEntering || damage <= 0
                || partIndex < 0
                || partIndex >= _bossPartStates.Length)
                return false;
            BossPartState part = _bossPartStates[partIndex];
            if (part.Destroyed || IsBossPartInvulnerable(partIndex))
                return false;

            int hp = Damage.ApplyToHp(part.Hp, damage);
            int appliedDamage = part.Hp - hp;
            RecordBossDamage(appliedDamage);   // REQ-133
            _bossHp = Damage.ApplyToHp(
                _bossHp,
                appliedDamage);
            _bossPartStates[partIndex] = new BossPartState(
                part.PartId,
                part.X,
                part.Y,
                hp,
                part.MaxHp,
                hp == 0,
                part.Active,
                part.IsCore,
                false);
            if (_warshipEncounter != null)
            {
                var warshipDamage = new WarshipDamageCommand(
                    part.PartId,
                    appliedDamage);
                _warshipEncounter.ApplyDamage(in warshipDamage);
            }
            if (_bossHp > 0)
                UpdateBossPhaseFromHp();
            if (hp > 0)
            {
                EmitEvent(
                    SimEventType.EnemyHit,
                    _bossId,
                    part.X,
                    part.Y,
                    appliedDamage);
                return false;
            }

            BossPartDefinition definition =
                _bossPartDefinitions[partIndex];
            EmitEvent(
                SimEventType.EnemyHit,
                _bossId,
                part.X,
                part.Y,
                appliedDamage);
            _bossPartRegenerationRemaining[partIndex] =
                definition.RegenerationTicks;
            _bossPartsEverDestroyed[partIndex] = true;
            _bossPartFireCooldowns[partIndex] =
                definition.Attack.IntervalTicks;
            _bossPartSecondaryLaserCooldowns[partIndex] =
                definition.Attack.SecondaryLaser == null
                    ? 0
                    : definition.Attack.SecondaryLaser.CycleIntervalTicks;
            _bossPartSecondaryBurstCooldowns[partIndex] =
                definition.Attack.SecondaryBurst == null
                    ? 0
                    : definition.Attack.SecondaryBurst.CycleIntervalTicks;
            _bossPartContactHitThisCycle[partIndex] = false;
            EmitBossPartEvent(
                SimEventType.BossPartDestroyed,
                part.X,
                part.Y,
                partIndex);
            RefreshBossPartPositions();
            if (_warshipEncounter != null)
            {
                SyncWarshipPositionAndVulnerability();
                ForwardWarshipEvents();
            }
            if (definition.IsCore)
                return DefeatBoss(part.X, part.Y);
            return false;
        }

        bool ApplyDamageToBoss(int damage)
        {
            if (!BossActive || BossEntering || damage <= 0)
                return false;
            int previousBossHp = _bossHp;
            _bossHp = Damage.ApplyToHp(_bossHp, damage);
            RecordBossDamage(previousBossHp - _bossHp);   // REQ-133
            if (_bossHp > 0)
            {
                EmitEvent(
                    SimEventType.EnemyHit,
                    _bossId,
                    _bossX,
                    _bossY,
                    damage);
                UpdateBossPhaseFromHp();
                return false;
            }

            return DefeatBoss(_bossX, _bossY);
        }

        /// <summary>
        /// **때릴 수 있는 파츠가 하나도 없으면 다음 페이즈를 연다.**
        ///
        /// 페이즈 전환은 원래 데미지가 들어간 순간에만 다시 계산된다
        /// (<see cref="UpdateBossPhaseFromHp"/>는 피격 경로에서만 불린다). 그래서
        /// "지금 페이즈에서 깎을 수 있는 것을 다 깎았는데 다음 문턱에는 못 미치는"
        /// 상태에 빠지면 **영원히 그대로다.** 더 때릴 것이 없으니 데미지가 없고,
        /// 데미지가 없으니 페이즈도 안 넘어간다.
        ///
        /// 2026-08-05에 브루드마더가 정확히 이 상태로 멈췄다 (사람 보고: "HP가 0이
        /// 되지 않음"). ph0에서 주머니를 다 부수기 전에 HP가 50% 아래로 떨어지면
        /// ph1이 열리고, ph1 규칙이 주머니를 **다시 무적으로** 만들어 남은 주머니
        /// HP가 잠긴다. 남은 것은 sac_left 2,494 + 무적 코어 7,895 = 10,389인데
        /// ph2 문턱은 10,000이라 389 차이로 갇힌다.
        ///
        /// 데이터로도 고치지만(페이즈 재설계), 이 부류는 데이터를 만질 때마다
        /// 다시 생길 수 있어서 시뮬레이션이 스스로 막는다. 유예를 두는 이유는
        /// 등장·형태 전환처럼 잠깐 모두가 무적인 정상 구간이 있기 때문이다.
        /// </summary>
        void AdvanceBossPhaseIfNothingIsDamageable()
        {
            if (_bossPartDefinitions.Count == 0
                || _bossHp <= 0
                || _bossPhase + 1 >= _bossPhases.Count)
            {
                _bossNothingDamageableTicks = 0;
                return;
            }
            for (int i = 0; i < _bossPartDefinitions.Count; i++)
            {
                if (_bossPartStates[i].Destroyed)
                    continue;
                if (!IsBossPartInvulnerable(i))
                {
                    _bossNothingDamageableTicks = 0;
                    return;
                }
            }
            _bossNothingDamageableTicks++;
            if (_bossNothingDamageableTicks < NothingDamageableGraceTicks)
                return;
            _bossNothingDamageableTicks = 0;
            EnterBossPhase(_bossPhase + 1, true);
        }

        /// <summary>
        /// 아무것도 못 때리는 상태를 몇 틱 견딘 뒤에 페이즈를 넘길 것인가.
        /// 1초다 — 등장 연출이나 형태 전환처럼 정상적으로 잠깐 전부 무적인
        /// 구간을 페이즈 폭주로 오해하지 않을 만큼 길고, 사람이 "멈췄다"고
        /// 느끼기 전에 풀릴 만큼 짧다.
        /// </summary>
        const int NothingDamageableGraceTicks = SimSpace.TicksPerSecond;

        void UpdateBossPhaseFromHp()
        {
            if (_bossUsesTimedPattern)
                return;
            int phaseCount = _bossPhases.Count;
            int nextPhase;
            if (phaseCount > 1 && _bossPhases[1].HasHpThreshold)
            {
                nextPhase = _bossPhase;
                for (int i = _bossPhase + 1; i < phaseCount; i++)
                {
                    Generation.BossPhase candidate = _bossPhases[i];
                    if ((long)_bossHp * candidate.HpThresholdDenominator
                        > (long)_bossRuntimeMaxHp
                            * candidate.HpThresholdNumerator)
                        break;
                    nextPhase = i;
                }
            }
            else
            {
                nextPhase = Math.Min(
                    phaseCount - 1,
                    (int)((long)(_bossRuntimeMaxHp - _bossHp)
                        * phaseCount / _bossRuntimeMaxHp));
            }
            if (nextPhase <= _bossPhase)
                return;
            EnterBossPhase(nextPhase, true);
        }

        void SpawnSecondBossForm()
        {
            ConfigureSecondBossForm();
            if (_nextEnemyId == int.MaxValue)
                throw new InvalidOperationException(
                    "The enemy id counter is exhausted.");
            _bossFormIndex = 1;
            _bossSpawned = true;
            _bossId = _nextEnemyId++;
            _bossX = _bossHoldX;
            _bossY = 0;
            _bossHp = _bossMaxHp;
            _bossPhase = 0;
            _bossAge = 0;
            _bossPhaseAge = 0;
            _bossMovementAnchorY = _bossY;
            _bossMovementPhaseOffsetTicks = 0;
            _bossMovementTransitionOffsetX = 0;
            _bossMovementTransitionOffsetY = 0;
            _bossVelocityX = 0;
            _bossVelocityY = 0;
            ResetSuctionForce();
            Generation.BossPhase initialPhase = _bossPhases[0];
            _bossFireCooldown = initialPhase.TelegraphTicks > 0
                ? initialPhase.TelegraphTicks
                : initialPhase.FireIntervalTicks;
            _bossPhaseTelegraphPending = initialPhase.TelegraphTicks > 0;
            _bossBurstAwaitingVolley =
                initialPhase.FirePattern == BossFirePattern.Burst
                && initialPhase.TelegraphTicks > 0;
            _bossPatternVolleyIndex = 0;
            InitializeBossParts();
            ConfigureSegmentChainSchedule(initialPhase);
            EmitBossFormEvent(
                SimEventType.BossFormChanged,
                _bossId,
                _bossX,
                _bossY,
                _bossFormIndex,
                _bossForm2.FormId);
            EmitEvent(
                SimEventType.BossSpawned,
                _bossId,
                _bossX,
                _bossY,
                0);
        }

        void ConfigureSecondBossForm()
        {
            const int u = SimSpace.SubUnitsPerWorldUnit;
            _bossMaxHp = ScaleEnemyHp(_bossForm2.MaxHp);
            _bossHalfWidth = _bossForm2.HalfWidth;
            _bossHalfHeight = _bossForm2.HalfHeight;
            _bossHoldX = _bossForm2.HoldX != 0
                ? _bossForm2.HoldX
                : 14 * u;
            _bossPhases = _bossForm2.Phases;
            _bossPartDefinitions = _bossForm2.Parts;
            _bossUsesTimedPattern = ResolveTimedBossPattern(_bossPhases);
            ValidateBossPhaseRuntimeData();
            _bossPartStates =
                new BossPartState[_bossPartDefinitions.Count];
            _readOnlyBossParts = Array.AsReadOnly(_bossPartStates);
            _bossPartFireCooldowns =
                new int[_bossPartDefinitions.Count];
            _bossPartSecondaryLaserCooldowns =
                new int[_bossPartDefinitions.Count];
            _bossPartSecondaryBurstCooldowns =
                new int[_bossPartDefinitions.Count];
            _bossPartRegenerationRemaining =
                new int[_bossPartDefinitions.Count];
            _bossPartsEverDestroyed =
                new bool[_bossPartDefinitions.Count];
            _bossPartContactHitThisCycle =
                new bool[_bossPartDefinitions.Count];
            _bossPartSpawnDefinitions =
                new EnemyDefinition[_bossPartDefinitions.Count];
            ResolveBossPartRuntimeData();
            _bossRuntimeMaxHp = _bossPartStates.Length == 0
                ? _bossMaxHp
                : SumBossPartMaxHp();
            _bossSpawnX = Math.Max(
                _bossHoldX,
                SaturateToInt(
                    (long)SimSpace.PlayfieldHalfWidthSubUnits
                    + GetBossLeftExtent()
                    + 1));
        }

        void EmitBossFormEvent(
            SimEventType type,
            int entityId,
            int x,
            int y,
            int arg,
            string formId)
        {
            if (_eventCount == _events.Length)
                throw new InvalidOperationException(
                    "The preallocated simulation event buffer is exhausted.");
            _events[_eventCount++] = new SimEvent(
                type,
                entityId,
                x,
                y,
                arg,
                formId);
        }

        bool DefeatBoss(int x, int y)
        {
            _bossHp = 0;
            DestroyAllSegmentChains();
            int awardedScore =
                RecordKillScore((long)_bossRuntimeMaxHp * 2);
            EmitEvent(
                SimEventType.EnemyKilled,
                _bossId,
                x,
                y,
                awardedScore);
            AdvanceKillCombo();
            if (_bossFormIndex == 0 && _bossForm2 != null)
            {
                int defeatedFormId = _bossId;
                _bossSpawned = false;
                _bossTransitionTicksRemaining =
                    _bossForm2.TransitionTicks;
                EmitBossFormEvent(
                    SimEventType.BossFormTransitionStarted,
                    defeatedFormId,
                    x,
                    y,
                    _bossTransitionTicksRemaining,
                    _bossForm2.FormId);
                return false;
            }

            _bossDefeated = true;
            BossDefeatElapsedTicks = _bossAge;
            if (_isMidBossBattle)
                EmitEvent(
                    SimEventType.MidBossDefeated,
                    _bossId,
                    x,
                    y,
                    BossDefeatElapsedTicks);
            EmitEvent(
                SimEventType.StageCleared,
                _bossId,
                x,
                y,
                0);
            return true;
        }

        void ResolveEnemyBulletPlayerCollisions()
        {
            int index = 0;
            while (index < _bullets.Count)
            {
                BulletState bullet = _bullets[index];
                if (bullet.Faction != BulletFaction.Enemy)
                {
                    index++;
                    continue;
                }

                // A hit always wins over graze on the same tick.
                int bulletHalfWidth = ScaleProjectileHitbox(
                    _enemyBulletHalfWidth,
                    bullet.CollisionScalePercent);
                int bulletHalfHeight = ScaleProjectileHitbox(
                    _enemyBulletHalfHeight,
                    bullet.CollisionScalePercent);
                if (Intersects(
                        PlayerX, PlayerY, _playerHalfWidth, _playerHalfHeight,
                        bullet.X, bullet.Y, bulletHalfWidth, bulletHalfHeight))
                {
                    RemoveBulletAt(index);
                    ApplyPlayerHit(_enemyBulletDamage);
                    if (!_playerAlive)
                    {
                        return;
                    }
                    continue;
                }

                if (_bulletAux[index].GrazeScored == 0 && IsWithinGrazeRadius(in bullet))
                {
                    _bulletAux[index].GrazeScored = 1;
                    AddScoreSaturated(_grazeScore);
                    EmitEvent(
                        SimEventType.GrazeScored,
                        bullet.Id,
                        bullet.X,
                        bullet.Y,
                        _grazeScore);
                    RecordComboAction();
                    AdvanceMultiplierFromGraze();
                }
                index++;
            }
        }

        static bool ShouldAdvanceEnemyX(EnemyDefinition definition, int age)
        {
            if (definition.MovePattern == EnemyMovePattern.Static)
                return false;
            if (definition.MovePattern != EnemyMovePattern.Dash)
                return true;

            long cycleTicks =
                (long)definition.MovementPauseTicks + definition.MovementDurationTicks;
            long phase = (age - 1L) % cycleTicks;
            return phase >= definition.MovementPauseTicks;
        }

        static int ComputeTriangleLutValue(int age, int periodTicks)
        {
            const int cycleScale = 4 * SineScale;
            int phase = (int)(((long)age * cycleScale / periodTicks) % cycleScale);
            if (phase < SineScale)
                return phase;
            if (phase < 3 * SineScale)
                return 2 * SineScale - phase;
            return phase - cycleScale;
        }

        int AdvanceDiveY(int index, EnemyDefinition definition, int age)
        {
            int spawnY = _enemySpawnYs[index];
            if (age <= definition.MovementDelayTicks)
                return spawnY;

            if ((_enemyMovementFlags[index]
                    & EnemyMovementDiveTargetLocked) == 0)
            {
                _enemyDiveTargetYs[index] = PlayerY;
                _enemyMovementFlags[index] |=
                    EnemyMovementDiveTargetLocked;
            }

            int elapsed = age - definition.MovementDelayTicks;
            if (elapsed > definition.MovementDurationTicks)
                elapsed = definition.MovementDurationTicks;
            long delta = (long)_enemyDiveTargetYs[index] - spawnY;
            return SaturateToInt(
                spawnY + delta * elapsed / definition.MovementDurationTicks);
        }

        void AdvanceCapsules()
        {
            long scrollDelta = GetScrollXAtTick(Tick) - GetScrollXAtTick(Tick - 1);
            int index = 0;
            while (index < _capsules.Count)
            {
                CapsuleState capsule = _capsules[index];
                long cleanupRetreat = IsRoomBoundaryCleanupActive
                    ? BossRetreatSpeedPerTick
                    : 0L;
                int nextX = SaturateToInt(
                    capsule.X - scrollDelta - cleanupRetreat);
                int nextY = capsule.Y;
                if (_capsuleMagnetRadiusSubUnits > 0
                    && _capsuleMagnetSpeedNumerator > 0
                    && SquaredDistanceSaturated(
                        nextX,
                        nextY,
                        PlayerX,
                        PlayerY)
                        <= SquaredRadiusSaturated(
                            _capsuleMagnetRadiusSubUnits))
                {
                    long dx = (long)PlayerX - nextX;
                    long dy = (long)PlayerY - nextY;
                    long length = IntegerSqrt(dx * dx + dy * dy);
                    if (length > 0)
                    {
                        long directionX =
                            dx * CapsuleMagnetDirectionScale / length;
                        long directionY =
                            dy * CapsuleMagnetDirectionScale / length;
                        long denominator =
                            (long)_capsuleMagnetSpeedDenominator
                            * CapsuleMagnetDirectionScale;
                        long xRemainder =
                            _capsuleMagnetXRemainders[index];
                        long yRemainder =
                            _capsuleMagnetYRemainders[index];
                        nextX = AdvanceCapsuleMagnetAxis(
                            nextX,
                            PlayerX,
                            (long)_capsuleMagnetSpeedNumerator
                                * directionX,
                            denominator,
                            ref xRemainder);
                        nextY = AdvanceCapsuleMagnetAxis(
                            nextY,
                            PlayerY,
                            (long)_capsuleMagnetSpeedNumerator
                                * directionY,
                            denominator,
                            ref yRemainder);
                        _capsuleMagnetXRemainders[index] = xRemainder;
                        _capsuleMagnetYRemainders[index] = yRemainder;
                    }
                }
                else
                {
                    _capsuleMagnetXRemainders[index] = 0;
                    _capsuleMagnetYRemainders[index] = 0;
                }

                if (nextX < _enemyDespawnX)
                {
                    RemoveCapsuleAt(index);
                    continue;
                }

                _capsules[index] = new CapsuleState(
                    capsule.Id,
                    nextX,
                    nextY);
                index++;
            }
        }

        void AdvanceBombPickups()
        {
            long scrollDelta =
                GetScrollXAtTick(Tick)
                - GetScrollXAtTick(Tick - 1);
            int index = 0;
            while (index < _bombPickups.Count)
            {
                BombPickupState pickup = _bombPickups[index];
                long cleanupRetreat = IsRoomBoundaryCleanupActive
                    ? BossRetreatSpeedPerTick
                    : 0L;
                int nextX = SaturateToInt(
                    pickup.X - scrollDelta - cleanupRetreat);
                int nextY = pickup.Y;
                if (_capsuleMagnetRadiusSubUnits > 0
                    && _capsuleMagnetSpeedNumerator > 0
                    && SquaredDistanceSaturated(
                        nextX,
                        nextY,
                        PlayerX,
                        PlayerY)
                        <= SquaredRadiusSaturated(
                            _capsuleMagnetRadiusSubUnits))
                {
                    long dx = (long)PlayerX - nextX;
                    long dy = (long)PlayerY - nextY;
                    long length = IntegerSqrt(dx * dx + dy * dy);
                    if (length > 0)
                    {
                        long directionX =
                            dx * CapsuleMagnetDirectionScale / length;
                        long directionY =
                            dy * CapsuleMagnetDirectionScale / length;
                        long denominator =
                            (long)_capsuleMagnetSpeedDenominator
                            * CapsuleMagnetDirectionScale;
                        long xRemainder =
                            _bombPickupMagnetXRemainders[index];
                        long yRemainder =
                            _bombPickupMagnetYRemainders[index];
                        nextX = AdvanceCapsuleMagnetAxis(
                            nextX,
                            PlayerX,
                            (long)_capsuleMagnetSpeedNumerator
                                * directionX,
                            denominator,
                            ref xRemainder);
                        nextY = AdvanceCapsuleMagnetAxis(
                            nextY,
                            PlayerY,
                            (long)_capsuleMagnetSpeedNumerator
                                * directionY,
                            denominator,
                            ref yRemainder);
                        _bombPickupMagnetXRemainders[index] =
                            xRemainder;
                        _bombPickupMagnetYRemainders[index] =
                            yRemainder;
                    }
                }
                else
                {
                    _bombPickupMagnetXRemainders[index] = 0;
                    _bombPickupMagnetYRemainders[index] = 0;
                }

                if (nextX < _enemyDespawnX)
                {
                    RemoveBombPickupAt(index);
                    continue;
                }
                _bombPickups[index] = new BombPickupState(
                    pickup.Id,
                    nextX,
                    nextY);
                index++;
            }
        }

        static int AdvanceCapsuleMagnetAxis(
            int position,
            int target,
            long velocityNumerator,
            long velocityDenominator,
            ref long remainder)
        {
            long accumulated = remainder + velocityNumerator;
            long delta = accumulated / velocityDenominator;
            long next = (long)position + delta;
            if ((target >= position && next >= target)
                || (target <= position && next <= target))
            {
                remainder = 0;
                return target;
            }
            remainder = accumulated % velocityDenominator;
            return SaturateToInt(next);
        }

        void AdvanceObstacles()
        {
            long scrollDelta =
                GetScrollXAtTick(Tick) - GetScrollXAtTick(Tick - 1);
            int index = 0;
            while (index < _obstacles.Count)
            {
                ObstacleState obstacle = _obstacles[index];
                long velocityDenominator =
                    _obstacleVelocityDenominators[index];
                long gravityDenominator =
                    _obstacleGravityDenominators[index];
                long gcd = GreatestCommonDivisor(
                    velocityDenominator,
                    gravityDenominator);
                long commonDenominator = checked(
                    velocityDenominator / gcd * gravityDenominator);
                long velocityScale = commonDenominator / velocityDenominator;
                long gravityScale = commonDenominator / gravityDenominator;
                long velocityX = checked(
                    _obstacleVelocityXNumerators[index] * velocityScale);
                long velocityY = checked(
                    _obstacleVelocityYNumerators[index] * velocityScale
                    - _obstacleGravityNumerators[index] * gravityScale);
                long accumulatedX = checked(
                    _obstacleMotionXRemainders[index] * velocityScale
                    + velocityX);
                long accumulatedY = checked(
                    _obstacleMotionYRemainders[index] * velocityScale
                    + velocityY);
                long nextX = obstacle.X
                    - scrollDelta
                    + accumulatedX / commonDenominator;
                if (IsRoomBoundaryCleanupActive)
                    nextX -= BossRetreatSpeedPerTick;
                long nextY = obstacle.Y
                    + accumulatedY / commonDenominator;
                if (nextX < _enemyDespawnX
                    || nextY < -SimSpace.PlayfieldHalfHeightSubUnits
                        - SimSpace.DespawnMarginSubUnits)
                {
                    RemoveObstacleAt(index);
                    continue;
                }

                int age = _obstacleAges[index] + 1;
                _obstacleAges[index] = age;
                _obstacleMotionXRemainders[index] =
                    accumulatedX % commonDenominator;
                _obstacleMotionYRemainders[index] =
                    accumulatedY % commonDenominator;
                _obstacleVelocityXNumerators[index] = velocityX;
                _obstacleVelocityYNumerators[index] = velocityY;
                _obstacleVelocityDenominators[index] = commonDenominator;
                _obstacles[index] = new ObstacleState(
                    obstacle.Id,
                    obstacle.Type,
                    SaturateToInt(nextX),
                    SaturateToInt(nextY),
                    obstacle.Hp);
                LaserAttackDefinition laser =
                    _obstacleLaserAttacks[index];
                if (laser != null
                    && age % laser.CycleIntervalTicks == 0)
                {
                    TryStartLaser(
                        LaserSourceKind.Terrain,
                        obstacle.Id,
                        laser,
                        SaturateToInt(nextX),
                        obstacle.Y);
                }
                index++;
            }
        }

        void AdvanceObstacleRegeneration()
        {
            int index = 0;
            while (index < _pendingObstacleRegens.Count)
            {
                ObstacleRegenerationState pending =
                    _pendingObstacleRegens[index];
                if (Tick < pending.RespawnAtTick)
                {
                    index++;
                    continue;
                }
                if (IsRoomBoundaryCleanupActive)
                {
                    _pendingObstacleRegens.RemoveAt(index);
                    continue;
                }
                // 되살아날 장애물의 **자기 크기**로 자리를 본다 — 기본값으로
                // 재면 큰 장애물이 남의 위에 겹쳐 되살아난다.
                if (IsObstacleRespawnOccupied(
                        pending.X,
                        pending.Y,
                        pending.HalfWidth > 0
                            ? pending.HalfWidth : _obstacleHalfWidth,
                        pending.HalfHeight > 0
                            ? pending.HalfHeight : _obstacleHalfHeight))
                {
                    if (pending.RespawnAtTick < int.MaxValue)
                        _pendingObstacleRegens[index] =
                            pending.WithRespawnAtTick(
                                pending.RespawnAtTick + 1);
                    index++;
                    continue;
                }

                AddActiveObstacle(
                    pending.Id,
                    pending.Type,
                    pending.X,
                    pending.Y,
                    pending.MaxHp,
                    pending.MaxHp,
                    null,
                    pending.BlocksEnemyBullets,
                    pending.RegenDelayTicks);
                _pendingObstacleRegens.RemoveAt(index);
                EmitEvent(
                    SimEventType.ObstacleRegenerated,
                    pending.Id,
                    pending.X,
                    pending.Y,
                    pending.MaxHp);
            }
        }

        bool IsObstacleRespawnOccupied(
            int x, int y, int halfWidth, int halfHeight)
        {
            if (_playerAlive
                && Intersects(
                    PlayerX,
                    PlayerY,
                    _playerHalfWidth,
                    _playerHalfHeight,
                    x,
                    y,
                    halfWidth,
                    halfHeight))
                return true;
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyState enemy = _enemies[i];
                EnemyDefinition definition = _enemyDefinitions[i];
                if (Intersects(
                    enemy.X,
                    enemy.Y,
                    definition.HalfWidth,
                    definition.HalfHeight,
                    x,
                    y,
                    halfWidth,
                    halfHeight))
                    return true;
            }
            if (BossActive
                && Intersects(
                    _bossX,
                    _bossY,
                    _bossHalfWidth,
                    _bossHalfHeight,
                    x,
                    y,
                    halfWidth,
                    halfHeight))
                return true;
            for (int i = 0; i < _bossPartStates.Length; i++)
            {
                BossPartState part = _bossPartStates[i];
                if (part.Destroyed || !part.Active)
                    continue;
                BossPartDefinition definition =
                    _bossPartDefinitions[i];
                if (Intersects(
                    part.X,
                    part.Y,
                    definition.HalfWidth,
                    definition.HalfHeight,
                    x,
                    y,
                    halfWidth,
                    halfHeight))
                    return true;
            }
            return false;
        }

        static long GreatestCommonDivisor(long left, long right)
        {
            left = Math.Abs(left);
            right = Math.Abs(right);
            while (right != 0)
            {
                long remainder = left % right;
                left = right;
                right = remainder;
            }
            return left == 0 ? 1 : left;
        }

        void RemoveObstacleAt(int index)
        {
            _obstacles.RemoveAt(index);
            _obstacleAges.RemoveAt(index);
            _obstacleLaserAttacks.RemoveAt(index);
            _obstacleBlocksEnemyBullets.RemoveAt(index);
            _obstacleRegenDelayTicks.RemoveAt(index);
            _obstacleMaxHps.RemoveAt(index);
            _obstacleMotionXRemainders.RemoveAt(index);
            _obstacleMotionYRemainders.RemoveAt(index);
            _obstacleVelocityXNumerators.RemoveAt(index);
            _obstacleVelocityYNumerators.RemoveAt(index);
            _obstacleVelocityDenominators.RemoveAt(index);
            _obstacleGravityNumerators.RemoveAt(index);
            _obstacleGravityDenominators.RemoveAt(index);
        }

        void AddDefaultObstacleMotion()
        {
            _obstacleMotionXRemainders.Add(0);
            _obstacleMotionYRemainders.Add(0);
            _obstacleVelocityXNumerators.Add(0);
            _obstacleVelocityYNumerators.Add(0);
            _obstacleVelocityDenominators.Add(1);
            _obstacleGravityNumerators.Add(0);
            _obstacleGravityDenominators.Add(1);
        }

        void TryStartLaser(
            LaserSourceKind sourceKind,
            int sourceEntityId,
            LaserAttackDefinition definition,
            int sourceX,
            int sourceY)
        {
            if (_lasers.Count >= _maxLasers)
            {
                EmitEvent(
                    SimEventType.LaserCapacityExceeded,
                    sourceEntityId,
                    sourceX,
                    sourceY,
                    _maxLasers);
                return;
            }
            if (_nextLaserId == int.MaxValue)
                throw new InvalidOperationException(
                    "The laser id counter is exhausted.");
            int id = _nextLaserId++;
            // 조준 레이저는 **이 순간 한 번** 플레이어 쪽으로 방향을 정하고, 그
            // 방향을 이 발사 내내 유지한다. 정의를 복제해 넣으므로 이후 틱에서
            // 다시 조준되지 않는다 — 계속 따라오면 피할 수 없다.
            if (definition.AimsAtPlayer)
                definition = AimLaserAtPlayer(definition, sourceX, sourceY);
            _laserDefinitions.Add(definition);
            _laserAges.Add(0);
            _lasers.Add(CreateLaserState(
                id,
                sourceKind,
                sourceEntityId,
                sourceX,
                sourceY,
                definition,
                0));
            EmitEvent(
                SimEventType.LaserTelegraphStarted,
                id,
                sourceX,
                sourceY,
                (int)sourceKind);
        }

        void AdvanceLasers()
        {
            int index = 0;
            while (index < _lasers.Count)
            {
                LaserAttackDefinition definition =
                    _laserDefinitions[index];
                if (_lasers[index].SourceKind
                    == LaserSourceKind.Player)
                {
                    _playerBeamAge = _playerBeamAge == int.MaxValue
                        ? int.MaxValue
                        : _playerBeamAge + 1;
                    _laserAges[index] = _playerBeamAge;
                    _lasers[index] = CreatePlayerBeamState(
                        _lasers[index].Id,
                        _playerBeamAge);
                    index++;
                    continue;
                }
                LaserPhase previousPhase =
                    _lasers[index].Phase;
                int age = _laserAges[index] + 1;
                if (age >= definition.LifetimeTicks)
                {
                    int id = _lasers[index].Id;
                    int x = _lasers[index].StartX;
                    int y = _lasers[index].StartY;
                    RemoveLaserAt(index);
                    EmitEvent(
                        SimEventType.LaserEnded,
                        id,
                        x,
                        y,
                        0);
                    continue;
                }
                _laserAges[index] = age;
                LaserState current = _lasers[index];
                _lasers[index] = CreateLaserState(
                    current.Id,
                    current.SourceKind,
                    current.SourceEntityId,
                    current.StartX
                        - definition.StartOffsetX,
                    current.StartY
                        - definition.StartOffsetY,
                    definition,
                    age);
                if (previousPhase == LaserPhase.Telegraph
                    && _lasers[index].Phase == LaserPhase.Firing)
                {
                    EmitEvent(
                        SimEventType.LaserFired,
                        current.Id,
                        _lasers[index].StartX,
                        _lasers[index].StartY,
                        definition.FullHalfWidth);
                }
                index++;
            }
        }

        void RefreshLaserSegments()
        {
            int index = 0;
            while (index < _lasers.Count)
            {
                LaserState laser = _lasers[index];
                int sourceX;
                int sourceY;
                if (!TryGetLaserSourcePosition(
                        laser.SourceKind,
                        laser.SourceEntityId,
                        out sourceX,
                        out sourceY))
                {
                    RemoveLaserAt(index);
                    EmitEvent(
                        SimEventType.LaserEnded,
                        laser.Id,
                        laser.StartX,
                        laser.StartY,
                        0);
                    continue;
                }
                if (laser.SourceKind == LaserSourceKind.Player)
                {
                    _lasers[index] = CreatePlayerBeamState(
                        laser.Id,
                        _playerBeamAge);
                    index++;
                    continue;
                }
                _lasers[index] = CreateLaserState(
                    laser.Id,
                    laser.SourceKind,
                    laser.SourceEntityId,
                    sourceX,
                    sourceY,
                    _laserDefinitions[index],
                    _laserAges[index]);
                index++;
            }
        }

        bool TryGetLaserSourcePosition(
            LaserSourceKind kind,
            int sourceEntityId,
            out int x,
            out int y)
        {
            if (kind == LaserSourceKind.Player)
            {
                x = PlayerX;
                y = PlayerY;
                return _playerAlive;
            }
            if (kind == LaserSourceKind.Enemy)
            {
                int enemyIndex =
                    FindEnemyIndexById(sourceEntityId);
                if (enemyIndex >= 0)
                {
                    x = _enemies[enemyIndex].X;
                    y = _enemies[enemyIndex].Y;
                    return true;
                }
            }
            else if (kind == LaserSourceKind.Boss)
            {
                x = _bossX;
                y = _bossY;
                return _bossSpawned && !_bossDefeated
                    && sourceEntityId == _bossId;
            }
            else if (kind == LaserSourceKind.BossPart)
            {
                if (sourceEntityId >= 0
                    && sourceEntityId < _bossPartStates.Length)
                {
                    BossPartState part = _bossPartStates[sourceEntityId];
                    x = part.X;
                    y = part.Y;
                    return BossActive && part.Active && !part.Destroyed;
                }
            }
            else
            {
                for (int i = 0; i < _obstacles.Count; i++)
                    if (_obstacles[i].Id == sourceEntityId)
                    {
                        x = _obstacles[i].X;
                        y = _obstacles[i].Y;
                        return true;
                    }
            }
            x = 0;
            y = 0;
            return false;
        }

        static LaserState CreateLaserState(
            int id,
            LaserSourceKind sourceKind,
            int sourceEntityId,
            int sourceX,
            int sourceY,
            LaserAttackDefinition definition,
            int age)
        {
            int telegraphEnd = definition.TelegraphTicks;
            int firingEnd =
                telegraphEnd + definition.FiringTicks;
            int sustainEnd =
                firingEnd + definition.SustainTicks;
            LaserPhase phase;
            LaserThicknessStage thickness;
            int phaseEnd;
            int halfWidth;
            if (age < telegraphEnd)
            {
                phase = LaserPhase.Telegraph;
                thickness = LaserThicknessStage.Telegraph;
                phaseEnd = telegraphEnd;
                halfWidth = definition.ThinHalfWidth;
            }
            else if (age < firingEnd)
            {
                phase = LaserPhase.Firing;
                thickness = LaserThicknessStage.Thin;
                phaseEnd = firingEnd;
                halfWidth = definition.ThinHalfWidth;
            }
            else if (age < sustainEnd)
            {
                phase = LaserPhase.Sustaining;
                thickness = LaserThicknessStage.Full;
                phaseEnd = sustainEnd;
                halfWidth = definition.FullHalfWidth;
            }
            else
            {
                phase = LaserPhase.Dissipating;
                thickness = LaserThicknessStage.Thin;
                phaseEnd = definition.LifetimeTicks;
                halfWidth = definition.ThinHalfWidth;
            }
            int startX = SaturateToInt(
                (long)sourceX + definition.StartOffsetX);
            int startY = SaturateToInt(
                (long)sourceY + definition.StartOffsetY);
            ExtendLaserToPlayfieldEdge(
                startX,
                startY,
                SaturateToInt((long)sourceX + definition.EndOffsetX),
                SaturateToInt((long)sourceY + definition.EndOffsetY),
                out int endX,
                out int endY);

            return new LaserState(
                id,
                sourceKind,
                sourceEntityId,
                startX,
                startY,
                endX,
                endY,
                phase,
                thickness,
                halfWidth,
                phaseEnd - age,
                definition.Damage,
                definition.FullHalfWidth);
        }

        /// <summary>
        /// 이동 중인 전함에서 이 파츠가 사격을 참는가.
        ///
        /// 사람 지시 2026-08-05: "움직일땐 앞의 3개 레이저, 멈추면 6개 전부 레이저
        /// 쏘자." 이동 구간이 무음이면 썰렁하고, 여섯이 다 쏘면 움직이는 배를
        /// 피하면서 여섯 줄기를 피해야 해서 잠긴다. 그 사이를 만든다.
        ///
        /// "앞"은 **플레이어 쪽(왼쪽)**이다 — 함체 중심보다 왼쪽에 달린 포탑이
        /// 앞이고, 그 절반만 이동 중에 쏜다. 개수를 세지 않고 위치로 정하므로
        /// 문이 6문이든 8문이든 규칙이 그대로 산다.
        ///
        /// 지금 열려 있는 막의 파츠에만 적용한다 — 다른 막의 파츠는 어차피
        /// 무적이라 여기까지 오지 않는다.
        /// </summary>
        /// <summary>
        /// 이동 중인 전함의 **앞쪽** 포탑이 이동 창 안에서 실제로 한 발씩 쏘도록
        /// 첫 발 쿨다운을 당긴다.
        ///
        /// 왜 필요한가: 2막 포탑 주기는 560~1060틱(9~18초)인데 함체 이동은
        /// 240틱(4초)이다. 막이 시작될 때 쿨다운을 통째로 깔면 이동하는 4초
        /// 동안 아무도 발사 시점에 도달하지 못한다. 그래서 "이동 중엔 앞의 3개가
        /// 쏜다"는 규칙을 넣어도 화면에는 아무 변화가 없었다 — 억제할 발사 자체가
        /// 없었기 때문이다.
        ///
        /// 배치 방식: 앞쪽 포탑 n개를 이동 구간에 고르게 끼워 넣는다. k번째는
        /// travel*k/(n+1) 틱에 쏜다. 3개·240틱이면 60/120/180틱 — 넷으로 나눈
        /// 자리라 시작·도착 순간과 겹치지 않는다. 쿨다운을 **줄이기만** 하므로
        /// 매 틱 호출해도 결과가 같고(멱등), 데이터의 주기값을 늘리거나 하드코딩된
        /// 숫자를 새로 만들지 않는다.
        /// </summary>
        void PrimeWarshipMovingFireCooldowns()
        {
            if (_warshipEncounter == null || !_warshipEncounter.AnchorMoving)
                return;
            int travel = _warshipEncounter.ActiveAnchorTravelTicks;
            if (travel <= 0)
                return;
            int elapsed = _warshipEncounter.ActiveAnchorElapsedTicks;
            int frontCount = 0;
            for (int i = 0; i < _bossPartDefinitions.Count; i++)
            {
                if (IsWarshipMovingFireEligible(i))
                    frontCount++;
            }
            if (frontCount == 0)
                return;
            int slot = 0;
            for (int i = 0; i < _bossPartDefinitions.Count; i++)
            {
                if (!IsWarshipMovingFireEligible(i))
                    continue;
                slot++;
                int targetTick = (int)((long)travel * slot / (frontCount + 1));
                // **목표 시점을 지나면 손을 뗀다.** 계속 눌러 두면 발사 직후
                // 되돌아온 쿨다운까지 매 틱 0으로 깎여 한 문이 연사하고, 레이저
                // 슬롯을 다 먹어 나머지 포탑이 아예 못 쏜다 — 실제로 그렇게 돼서
                // 앞쪽 세 문 중 하나만 나갔다.
                if (elapsed >= targetTick)
                    continue;
                int remaining = targetTick - elapsed;
                if (_bossPartFireCooldowns[i] > remaining)
                    _bossPartFireCooldowns[i] = remaining;
            }
        }

        /// <summary>
        /// 이동 중에 쏠 자격이 있는 파츠인가 — 살아 있고, 뒤쪽이라 쉬는 대상이
        /// 아니고, 실제로 발사 쿨다운을 쓰는 공격을 가진 파츠.
        /// </summary>
        bool IsWarshipMovingFireEligible(int partIndex)
        {
            BossPartState state = _bossPartStates[partIndex];
            if (state.Destroyed
                || !state.Active
                || IsBossPartInvulnerable(partIndex)
                || IsWarshipPartHoldingFireWhileMoving(partIndex))
                return false;
            BossPartAttackType type = GetBossPartAttack(partIndex).Type;
            return type != BossPartAttackType.None
                && type != BossPartAttackType.VerticalMovement
                && type != BossPartAttackType.MeleeCharge
                && type != BossPartAttackType.Suction;
        }

        bool IsWarshipPartHoldingFireWhileMoving(int partIndex)
        {
            if (_warshipEncounter == null
                || !_warshipEncounter.AnchorMoving)
                return false;
            if (partIndex < 0 || partIndex >= _bossPartDefinitions.Count)
                return false;
            // 함체 중심보다 오른쪽(뒤)이면 이동 중에는 쉰다.
            return _bossPartDefinitions[partIndex].OffsetX > 0;
        }

        /// <summary>
        /// 레이저를 **화면 끝까지** 늘린다 (사람 지시 2026-08-04: "레이저는 중간에
        /// 끊김없이 항상 화면 끝까지 뻗어나가게 해줘").
        ///
        /// 데이터의 endOffset은 발사원 기준 상대 좌표라, 함체가 어디에 서 있느냐에
        /// 따라 빔이 허공에서 끝났다 — 전함 갑판 포탑은 x=-12에서 멈춰 왼쪽 8유닛이
        /// 비었다. 데이터마다 숫자를 늘리는 대신 규칙으로 세운다: **방향은 데이터가,
        /// 길이는 화면이 정한다.**
        ///
        /// 뷰가 아니라 여기서 늘리는 이유는 판정이 곧 그림이어야 하기 때문이다.
        /// 뷰에서만 늘리면 화면 끝까지 빛나는데 맞지는 않는 거짓말이 된다.
        ///
        /// 정수만 쓴다 — 두 축 중 **먼저 경계에 닿는 쪽**을 교차 곱으로 고르고,
        /// 그 축을 기준으로 나머지를 비례 배분한다. 나눗셈은 절삭이지만
        /// 결정론적이다(AGENTS.md §4).
        /// </summary>
        static void ExtendLaserToPlayfieldEdge(
            int startX,
            int startY,
            int rawEndX,
            int rawEndY,
            out int endX,
            out int endY)
        {
            long dx = (long)rawEndX - startX;
            long dy = (long)rawEndY - startY;
            if (dx == 0 && dy == 0)
            {
                endX = rawEndX;
                endY = rawEndY;
                return;
            }

            // 경계보다 살짝 밖까지 뻗어야 화면 가장자리에서 끊긴 것처럼 보이지 않는다.
            long limitX = SimSpace.PlayfieldHalfWidthSubUnits
                + SimSpace.DespawnMarginSubUnits;
            long limitY = SimSpace.PlayfieldHalfHeightSubUnits
                + SimSpace.DespawnMarginSubUnits;

            // 각 축에서 경계까지 남은 거리 (진행 방향으로).
            long spanX = dx > 0 ? limitX - startX
                : dx < 0 ? startX + limitX : 0;
            long spanY = dy > 0 ? limitY - startY
                : dy < 0 ? startY + limitY : 0;
            if (spanX < 0) spanX = 0;
            if (spanY < 0) spanY = 0;

            long absDx = Math.Abs(dx);
            long absDy = Math.Abs(dy);

            // spanX/absDx 와 spanY/absDy 중 작은 쪽이 먼저 닿는다 — 교차 곱으로 비교.
            bool xBinds = absDy == 0
                || (absDx != 0 && spanX * absDy <= spanY * absDx);

            if (xBinds && absDx != 0)
            {
                endX = SaturateToInt(startX + (dx > 0 ? spanX : -spanX));
                endY = SaturateToInt(startY + dy * spanX / absDx);
            }
            else if (absDy != 0)
            {
                endY = SaturateToInt(startY + (dy > 0 ? spanY : -spanY));
                endX = SaturateToInt(startX + dx * spanY / absDy);
            }
            else
            {
                endX = rawEndX;
                endY = rawEndY;
            }
        }

        void RemoveLaserAt(int index)
        {
            _lasers.RemoveAt(index);
            _laserDefinitions.RemoveAt(index);
            _laserAges.RemoveAt(index);
        }

        void ResolveLaserPlayerCollisions()
        {
            int playerRadius =
                Math.Max(_playerHalfWidth, _playerHalfHeight);
            for (int i = 0; i < _lasers.Count; i++)
            {
                LaserState laser = _lasers[i];
                if (!laser.IsDamaging
                    || laser.SourceKind == LaserSourceKind.Player)
                    continue;
                int radius = SaturatingAddDamage(
                    playerRadius,
                    laser.HalfWidth);
                if (LaserGeometry.IntersectsSegmentCircle(
                        laser.StartX,
                        laser.StartY,
                        laser.EndX,
                        laser.EndY,
                        PlayerX,
                        PlayerY,
                        radius))
                {
                    ApplyPlayerHit(laser.Damage);
                    if (!_playerAlive)
                        return;
                }
            }
        }

        void UpdatePlayerBeam(bool firing)
        {
            int index = FindPlayerBeamIndex();
            if (!firing || _beamDamagePerTick == 0 || !_playerAlive)
            {
                if (index >= 0)
                    RemoveLaserAt(index);
                _playerBeamAge = 0;
                return;
            }
            if (index >= 0)
            {
                _lasers[index] = CreatePlayerBeamState(
                    _lasers[index].Id,
                    _playerBeamAge);
                return;
            }
            if (_lasers.Count >= _maxLasers)
            {
                EmitEvent(
                    SimEventType.LaserCapacityExceeded,
                    0,
                    PlayerX,
                    PlayerY,
                    _maxLasers);
                return;
            }
            if (_nextLaserId == int.MaxValue)
                throw new InvalidOperationException(
                    "The laser id counter is exhausted.");
            int id = _nextLaserId++;
            _playerBeamAge = 0;
            _laserDefinitions.Add(null);
            _laserAges.Add(0);
            _lasers.Add(CreatePlayerBeamState(id, 0));
            IncrementSaturated(ref _shotsFired);
            EmitEvent(
                SimEventType.PlayerFired,
                0,
                PlayerX,
                PlayerY,
                (int)BulletKind.MainShot);
            EmitEvent(
                SimEventType.LaserFired,
                id,
                PlayerX,
                PlayerY,
                _beamStartHalfWidth);
        }

        LaserState CreatePlayerBeamState(int id, int age)
        {
            int halfWidth = SaturateToInt(
                Math.Min(
                    _beamMaxHalfWidth,
                    (long)_beamStartHalfWidth
                        + (long)_beamGrowthPerTick * age));
            return new LaserState(
                id,
                LaserSourceKind.Player,
                0,
                PlayerX,
                PlayerY,
                SaturateToInt((long)PlayerX + _beamLength),
                PlayerY,
                LaserPhase.Sustaining,
                halfWidth >= _beamMaxHalfWidth
                    ? LaserThicknessStage.Full
                    : LaserThicknessStage.Thin,
                halfWidth,
                0,
                _beamDamagePerTick);
        }

        int FindPlayerBeamIndex()
        {
            for (int i = 0; i < _lasers.Count; i++)
                if (_lasers[i].SourceKind
                    == LaserSourceKind.Player)
                    return i;
            return -1;
        }

        void RemovePlayerBeam()
        {
            int index = FindPlayerBeamIndex();
            if (index >= 0)
                RemoveLaserAt(index);
            _playerBeamAge = 0;
        }
    }
}

using System;
using System.Collections.Generic;
using Shmup.Core.Generation;

namespace Shmup.Core.Simulation
{
    /// <summary>
    /// Stable FNV-1a fold for determinism audits. The run overload includes every
    /// publicly observable mutable field, ordered collection item, reward option,
    /// generated stage value, and event. Values are folded byte-by-byte in an
    /// explicit little-endian order.
    /// </summary>
    public sealed class DeterminismAuditHasher
    {
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        ulong _hash = OffsetBasis;

        public ulong Hash => _hash;
        public string HexHash => _hash.ToString("X16");

        public void FoldRunState(RunManager run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            FoldInt32(run.RunNumber);
            FoldInt32(run.StageIndex);
            FoldInt32(run.FinalStageIndex);
            FoldInt32(run.BiomeIndex);
            FoldInt32(run.RoomIndex);
            FoldBool(run.IsBiomeBoss);
            FoldBool(run.IsHiddenBiome);
            FoldInt32((int)run.StageSection);
            FoldInt32(run.BiomeCount);
            FoldInt32(run.RoomsPerBiome);
            FoldInt32((int)run.State);
            FoldInt32((int)run.RewardSelectionKind);
            FoldInt32((int)run.CompletionGrade);
            FoldInt32((int)run.SelectedColossalBoss);
            FoldInt32((int)run.LastColossalBossAtRunStart);
            FoldInt32(run.EliteRoomsCleared);
            FoldInt32(run.NoHitBiomesCleared);
            FoldInt32(run.RareEncountersCleared);
            FoldInt32(run.HiddenConditionCount);
            FoldInt32(run.ThemeStageIndex);
            FoldInt32(run.StageThemeOrder.Count);
            for (int i = 0; i < run.StageThemeOrder.Count; i++)
                FoldInt32(run.StageThemeOrder[i]);
            FoldUInt64(run.RunSeed);
            FoldInt32(run.Difficulty);
            FoldInt32(run.DifficultyMultiplierNumerator);
            FoldInt32(run.DifficultyMultiplierDenominator);
            FoldInt64(run.TotalScore);
            FoldBool(run.IsDailyRun);
            FoldInt32(run.ContinueStock);
            FoldBool(run.FinalWagerCommitted);
            FoldInt32(run.FinalWagerShieldGranted);
            FoldInt32(run.FinalWagerOverflowConverted);
            FoldInt64(run.FinalWagerScoreBonus);
            FoldInt64(run.RunClearShieldBonus);
            FoldInt32(run.SimulationTicksElapsed);
            GhostReplayConfig ghostConfig = run.GhostReplayConfig;
            FoldInt32(ghostConfig.FixedWeaponLevel);
            FoldInt32(ghostConfig.FireIntervalTicks);
            FoldInt32(ghostConfig.MaximumInputRuns);
            FoldBool(run.StageOneGhostRecordingStarted);
            FoldBool(run.StageOneGhostRecordingFinalized);
            FoldInt32(run.StageOneGhostRecordedTicks);
            FoldInt32(run.StageOneGhostRecordedRunCount);
            FoldUInt64(run.StageOneGhostRecordingHash);
            GhostRecordingStartState ghostStart =
                run.StageOneGhostRecordingStart;
            FoldInt32(ghostStart.X);
            FoldInt32(ghostStart.Y);
            FoldInt32(ghostStart.SpeedNumerator);
            FoldInt32(ghostStart.SpeedDenominator);
            FoldInt32(ghostStart.MinimumX);
            FoldInt32(ghostStart.MaximumX);
            FoldInt32(ghostStart.MinimumY);
            FoldInt32(ghostStart.MaximumY);
            GhostState ghost = run.Ghost;
            FoldBool(ghost.Active);
            FoldInt32(ghost.EntityId);
            FoldInt32(ghost.X);
            FoldInt32(ghost.Y);
            FoldBool(ghost.IsFiring);
            FoldInt32(ghost.PlaybackTick);
            FoldInt32(ghost.TicksUntilNextShot);
            FoldInt64(ghost.MovementRemainderX);
            FoldInt64(ghost.MovementRemainderY);
            // Preserve every production hash byte-for-byte while still making
            // non-production runs distinct when the QA gate is active.
            if (run.DevFlagsActive)
                FoldBool(true);

            RunStatistics statistics = run.Statistics;
            FoldInt64(statistics.ShotsFired);
            FoldInt64(statistics.ShotsHit);
            FoldInt64(statistics.Kills);
            FoldInt64(statistics.CapsulesCollected);
            FoldInt64(statistics.GrazeCount);
            FoldInt64(statistics.BombsUsed);
            FoldInt64(statistics.HitsTaken);
            FoldInt32(statistics.StagesCleared);
            FoldInt32(statistics.RoomsCleared);
            FoldInt32(statistics.ContinuesUsed);
            FoldInt32(run.ContinueDecisionHistory.Count);
            for (int i = 0;
                i < run.ContinueDecisionHistory.Count;
                i++)
            {
                FoldInt32(
                    run.ContinueDecisionHistory[i].SimulationTick);
            }

            FoldShip(run.Ship);
            FoldPowerUpGauge(run.PowerUpGauge);
            FoldInt32((int)run.ActiveModifiers);
            FoldModifierStacks(run.ModifierStacks);
            FoldInt32((int)run.CurrentPrimaryWeaponFamily);
            FoldInt32(run.MaxShieldStock);
            FoldInt32(run.CapsuleBalance);
            FoldInt32(run.RewardRerollCost);
            FoldInt32((int)run.CurrentMissileFamily);
            FoldInt32((int)run.CurrentOptionFormation);
            FoldInt32((int)run.LastMidbossOutcome);
            FoldStagePlan(run.StagePlan);
            FoldRewards(run.RewardOptions);
            FoldContracts(run.ContractOptions);
            FoldContract(run.ActiveContract);
            FoldContractHistory(run.ContractChoiceHistory);
            FoldRewardDecisionHistory(
                run.RewardDecisionHistory);
            FoldRoutes(run.RouteOptions);
            FoldRouteHistory(run.RouteChoiceHistory);
            FoldBattle(run.Battle);
        }

        void FoldModifierStacks(BattleModifierStackSet stacks)
        {
            FoldInt32(stacks.CombinationLimit);
            FoldInt32(stacks.CombinationUsed);
            foreach (BattleModifier effect in BattleModifierRules.Ordered)
            {
                FoldInt32(stacks.GetStackCount(effect));
                FoldInt32(stacks.GetStrength(effect));
            }
        }

        public void FoldBattleState(IBattleSim battle)
        {
            if (battle == null)
                throw new ArgumentNullException(nameof(battle));
            FoldBattle(battle);
        }

        public void FoldRewardChoice(
            int stageIndex,
            int optionIndex,
            in RewardOption option)
        {
            FoldInt32(stageIndex);
            FoldInt32(optionIndex);
            FoldString(option.Id);
            FoldInt32((int)option.Type);
            FoldInt32((int)option.Slot);
            FoldInt32(option.Amount);
            FoldInt32((int)option.PrimaryWeaponFamily);
            FoldInt32((int)option.MissileFamily);
            FoldInt32((int)option.OptionFormation);
        }

        public void FoldRouteChoice(
            int stageIndex,
            int optionIndex,
            in RouteOption option)
        {
            FoldRouteChoice(stageIndex, 1, optionIndex, in option);
        }

        public void FoldRouteChoice(
            int biomeIndex,
            int roomIndex,
            int optionIndex,
            in RouteOption option)
        {
            FoldInt32(biomeIndex);
            FoldInt32(roomIndex);
            FoldInt32(optionIndex);
            FoldString(option.ThemeId);
            FoldInt32((int)option.EncounterType);
        }

        public void FoldTick(
            int runNumber,
            int stageIndex,
            RunState runState,
            IBattleSim steppedBattle,
            long totalScore)
        {
            if (steppedBattle == null)
                throw new ArgumentNullException(nameof(steppedBattle));
            FoldTick(
                runNumber,
                stageIndex,
                (int)runState,
                steppedBattle.Tick,
                steppedBattle.PlayerX,
                steppedBattle.PlayerY,
                steppedBattle.Bullets.Count,
                steppedBattle.Enemies.Count,
                totalScore,
                steppedBattle.EventsThisTick.Length);
            FoldInt32(steppedBattle.Obstacles.Count);
        }

        public void FoldTick(
            int runNumber,
            int stageIndex,
            int runState,
            int battleTick,
            int playerX,
            int playerY,
            int bulletCount,
            int enemyCount,
            long totalScore,
            int eventCount)
        {
            FoldInt32(runNumber);
            FoldInt32(stageIndex);
            FoldInt32(runState);
            FoldInt32(battleTick);
            FoldInt32(playerX);
            FoldInt32(playerY);
            FoldInt32(bulletCount);
            FoldInt32(enemyCount);
            FoldInt64(totalScore);
            FoldInt32(eventCount);
        }

        void FoldPowerUpGauge(PowerUpGauge gauge)
        {
            FoldInt32(gauge.Cursor);
            FoldInt32((int)gauge.LastActivationResult);
            FoldBool(gauge.GaugeActivationBanned);
            FoldBool(gauge.OptionActivationBanned);
            FoldBool(gauge.ShieldActivationBanned);
            for (int i = 0; i < PowerUpGauge.SlotCount; i++)
            {
                var slot = (PowerUpSlot)i;
                FoldInt32(gauge.GetLevel(slot));
                FoldInt32(gauge.GetMaxLevel(slot));
                FoldInt32(gauge.GetProgress(slot));
                FoldInt32(gauge.GetRequiredCapsules(slot));
            }
        }

        void FoldShip(ShipDefinition ship)
        {
            FoldString(ship.Id);
            FoldString(ship.DisplayName);
            FoldInt32(ship.MoveSpeedMultiplierNumerator);
            FoldInt32(ship.MoveSpeedMultiplierDenominator);
            FoldInt64(ship.UnlockCost);
            FoldInt32((int)ship.WeaponType);
            FoldBool(ship.MaxHp.HasValue);
            if (ship.MaxHp.HasValue)
                FoldInt32(ship.MaxHp.Value);
            FoldInt32(ship.StartingPowerUpLevels.Count);
            for (int i = 0; i < ship.StartingPowerUpLevels.Count; i++)
                FoldInt32(ship.StartingPowerUpLevels[i]);
        }

        void FoldStagePlan(StagePlan plan)
        {
            FoldString(plan.BossId);
            FoldString(plan.ThemeId);
            FoldString(plan.RequestedThemeId);
            FoldInt32((int)plan.EncounterType);
            FoldInt32(plan.EncounterEnemyHpMultiplierNumerator);
            FoldInt32(plan.EncounterEnemyHpMultiplierDenominator);
            FoldInt32(plan.CapsuleDropMultiplierNumerator);
            FoldInt32(plan.CapsuleDropMultiplierDenominator);
            FoldInt32(plan.EncounterScoreMultiplierNumerator);
            FoldInt32(plan.EncounterScoreMultiplierDenominator);
            FoldInt32(plan.LaneCount);
            FoldInt32(plan.StartLaneMask);
            FoldInt32(plan.BossEntryLaneMask);
            FoldInt32(plan.BossMaxHp);
            FoldInt32(plan.BossHalfWidth);
            FoldInt32(plan.BossHalfHeight);
            FoldInt32(plan.BossHoldX);
            FoldString(plan.Gimmick.ThemeId);
            FoldBool(plan.Gimmick.VisionObscured);
            FoldInt32(plan.Gimmick.TimeLimitTicks);
            FoldWarshipDefinition(plan.WarshipEncounter);

            FoldInt32(plan.BossPhases.Count);
            for (int i = 0; i < plan.BossPhases.Count; i++)
                FoldBossPhase(plan.BossPhases[i]);

            FoldInt32(plan.BossParts.Count);
            for (int i = 0; i < plan.BossParts.Count; i++)
                FoldBossPart(plan.BossParts[i]);

            FoldBool(plan.Form2 != null);
            if (plan.Form2 != null)
            {
                FoldString(plan.Form2.FormId);
                FoldInt32(plan.Form2.TransitionTicks);
                FoldInt32(plan.Form2.MaxHp);
                FoldInt32(plan.Form2.HalfWidth);
                FoldInt32(plan.Form2.HalfHeight);
                FoldInt32(plan.Form2.HoldX);
                FoldInt32(plan.Form2.Phases.Count);
                for (int i = 0; i < plan.Form2.Phases.Count; i++)
                    FoldBossPhase(plan.Form2.Phases[i]);
                FoldInt32(plan.Form2.Parts.Count);
                for (int i = 0; i < plan.Form2.Parts.Count; i++)
                    FoldBossPart(plan.Form2.Parts[i]);
            }

            FoldInt32(plan.Segments.Count);
            for (int i = 0; i < plan.Segments.Count; i++)
            {
                StageSegment segment = plan.Segments[i];
                FoldString(segment.SegmentId);
                FoldInt32(segment.LengthTicks);
                FoldInt32(segment.ScrollSpeedMultiplierNumerator);
                FoldInt32(segment.ScrollSpeedMultiplierDenominator);
                FoldInt32(segment.EntryLaneMask);
                FoldInt32(segment.ExitLaneMask);
                SegmentEnvironmentDefinition environment =
                    segment.Environment;
                FoldBool(environment.HasCorridor);
                FoldInt32(environment.StartMinY);
                FoldInt32(environment.StartMaxY);
                FoldInt32(environment.EndMinY);
                FoldInt32(environment.EndMaxY);
                FoldInt32(environment.CorridorContactDamage);
                FoldInt32(environment.DriftXNumerator);
                FoldInt32(environment.DriftXDenominator);
                FoldInt32(environment.DriftYNumerator);
                FoldInt32(environment.DriftYDenominator);
                FoldInt32(segment.TraversableLaneMasks.Count);
                for (int j = 0; j < segment.TraversableLaneMasks.Count; j++)
                    FoldInt32(segment.TraversableLaneMasks[j]);

                FoldInt32(segment.Spawns.Count);
                for (int j = 0; j < segment.Spawns.Count; j++)
                {
                    SpawnEvent spawn = segment.Spawns[j];
                    FoldInt32(spawn.Tick);
                    FoldString(spawn.EnemyId);
                    FoldInt32(spawn.X);
                    FoldInt32(spawn.Y);
                }

                FoldInt32(segment.Obstacles.Count);
                for (int j = 0; j < segment.Obstacles.Count; j++)
                {
                    ObstacleSpawn obstacle = segment.Obstacles[j];
                    FoldInt32((int)obstacle.Type);
                    FoldInt32(obstacle.X);
                    FoldInt32(obstacle.Y);
                    FoldInt32(obstacle.Hp);
                    FoldBool(obstacle.BlocksEnemyBullets);
                    FoldInt32(obstacle.RegenDelayTicks);
                    LaserAttackDefinition laser =
                        obstacle.LaserAttack;
                    FoldLaserDefinition(laser);
                }
            }
        }

        void FoldBossPhase(BossPhase phase)
        {
            FoldInt32(phase.FireIntervalTicks);
            FoldInt32(phase.Ways);
            FoldInt32(phase.BulletSpeedNumerator);
            FoldInt32(phase.BulletSpeedDenominator);
            FoldInt32((int)phase.MovementPattern);
            FoldInt32(phase.MovementAmplitudeNumerator);
            FoldInt32(phase.MovementAmplitudeDenominator);
            FoldInt32(phase.MovementPeriodTicks);
            FoldInt32(phase.MovementTelegraphTicks);
            FoldInt32((int)phase.PartVulnerability);
            FoldInt32(phase.DurationTicks);
            FoldInt32(phase.TelegraphTicks);
            FoldInt32((int)phase.FirePattern);
            FoldInt32((int)phase.ProjectileKind);
            FoldInt32(phase.SplitAfterTicks);
            FoldInt32(phase.MineTravelTicks);
            FoldInt32(phase.MineTelegraphTicks);
            FoldInt32(phase.MineAccelerationNumerator);
            FoldInt32(phase.MineAccelerationDenominator);
            FoldInt32((int)phase.SignaturePattern);
            FoldString(phase.SignatureSpawnEnemyId);
            FoldInt32(phase.SignatureObstacleHp);
            FoldInt32(phase.SignatureGravityNumerator);
            FoldInt32(phase.SignatureGravityDenominator);
            FoldInt32(phase.SignatureHomingTurnLutSlotsPerTick);
            FoldLaserDefinition(phase.LaserAttack);
            FoldInt32(phase.HpThresholdNumerator);
            FoldInt32(phase.HpThresholdDenominator);
            FoldInt32(phase.PartRules.Count);
            for (int i = 0; i < phase.PartRules.Count; i++)
            {
                BossPhasePartRule rule = phase.PartRules[i];
                FoldString(rule.PartId);
                FoldBool(rule.Active);
                FoldBool(rule.Invulnerable);
                FoldBossPartAttack(rule.Attack);
            }
            FoldSegmentChainDefinition(phase.SegmentChain);
        }

        void FoldSegmentChainDefinition(
            SegmentChainDefinition definition)
        {
            FoldBool(definition != null);
            if (definition == null)
                return;
            FoldInt32(definition.SegmentCount);
            FoldInt32(definition.SummonCount);
            FoldInt32(definition.SummonIntervalTicks);
            FoldInt32(definition.HeadMaxHp);
            FoldInt32(definition.HalfWidth);
            FoldInt32(definition.HalfHeight);
            FoldInt32(definition.MoveSpeedNumerator);
            FoldInt32(definition.MoveSpeedDenominator);
            FoldInt32(definition.TurnLutSlotsPerTick);
            FoldInt32(definition.FollowDelayTicks);
            FoldInt32(definition.ContactDamage);
            FoldInt32(definition.SpawnOffsetX);
            FoldInt32(definition.SpawnOffsetY);
            FoldInt32((int)definition.DamageRule);
        }

        void FoldBossPart(BossPartDefinition part)
        {
            FoldString(part.PartId);
            FoldInt32(part.OffsetX);
            FoldInt32(part.OffsetY);
            FoldInt32(part.HalfWidth);
            FoldInt32(part.HalfHeight);
            FoldInt32(part.MaxHp);
            FoldBool(part.IsCore);
            FoldInt32(part.RegenerationTicks);
            FoldInt32(part.CoreGatePartIds.Count);
            for (int gate = 0;
                gate < part.CoreGatePartIds.Count;
                gate++)
                FoldString(part.CoreGatePartIds[gate]);
            FoldBossPartAttack(part.Attack);
        }

        void FoldBossPartAttack(BossPartAttackProfile attack)
        {
            FoldBool(attack != null);
            if (attack == null)
                return;
            FoldInt32((int)attack.Type);
            FoldInt32(attack.IntervalTicks);
            FoldInt32(attack.Ways);
            FoldInt32(attack.BulletSpeedNumerator);
            FoldInt32(attack.BulletSpeedDenominator);
            FoldInt32(attack.EffectSpeedNumerator);
            FoldInt32(attack.EffectSpeedDenominator);
            FoldInt32(attack.EffectMaxSpeedNumerator);
            FoldInt32(attack.EffectMaxSpeedDenominator);
            FoldInt32(attack.EffectOffsetX);
            FoldInt32(attack.EffectOffsetY);
            FoldString(attack.SpawnEnemyId);
            FoldInt32(attack.ContactDamage);
            FoldLaserDefinition(attack.LaserAttack);
            FoldLaserDefinition(attack.SecondaryLaser);
            FoldBurstDefinition(attack.SecondaryBurst);
            FoldInt32(attack.MeleeTelegraphTicks);
        }

        void FoldBurstDefinition(BossPartBurstDefinition burst)
        {
            FoldBool(burst != null);
            if (burst == null)
                return;
            FoldInt32(burst.CycleIntervalTicks);
            FoldInt32(burst.Ways);
            FoldInt32(burst.BulletSpeedNumerator);
            FoldInt32(burst.BulletSpeedDenominator);
            FoldBool(burst.Aimed);
        }

        void FoldWarshipDefinition(WarshipEncounterDefinition definition)
        {
            FoldBool(definition != null);
            if (definition == null)
                return;
            FoldString(definition.EncounterId);
            FoldInt32(definition.EventEntityId);
            FoldInt32(definition.WarningTicks);
            FoldInt32(definition.OriginX);
            FoldInt32(definition.OriginY);
            FoldInt32(definition.HoldX);
            FoldInt32(definition.ScrollSpeedNumerator);
            FoldInt32(definition.ScrollSpeedDenominator);
            FoldInt32(definition.BaseCoreOpeningWays);
            FoldInt32(definition.WaysReductionPerTurret);
            FoldInt32(definition.MinimumCoreOpeningWays);
            FoldInt32(definition.Groups.Count);
            for (int i = 0; i < definition.Groups.Count; i++)
            {
                WarshipPartGroupDefinition group = definition.Groups[i];
                FoldString(group.GroupId);
                FoldInt32((int)group.Role);
                FoldInt32(group.AdvanceAfterTicks);
                FoldInt32(group.AnchorOffsetY);
                FoldInt32(group.AnchorTravelTicks);
                FoldInt32(group.PartIds.Count);
                for (int part = 0; part < group.PartIds.Count; part++)
                    FoldString(group.PartIds[part]);
            }
        }

        public void FoldWarshipEncounterState(WarshipEncounter encounter)
        {
            if (encounter == null)
                throw new ArgumentNullException(nameof(encounter));
            FoldWarshipDefinition(encounter.Definition);
            FoldInt32(encounter.Tick);
            FoldInt64(encounter.ScrollOffset);
            FoldInt64(encounter.ScrollRemainder);
            FoldInt32(encounter.ActiveGroupIndex);
            FoldInt32(encounter.ActiveGroupElapsedTicks);
            FoldInt32(encounter.AnchorOffsetY);
            FoldBool(encounter.WarningActive);
            FoldBool(encounter.MidbossDefeated);
            FoldBool(encounter.CoreBattleActive);
            FoldBool(encounter.CoreOpeningPending);
            FoldBool(encounter.Completed);
            FoldInt32(encounter.DestroyedAttritionParts);
            FoldInt32(encounter.TotalAttritionParts);
            FoldInt32(encounter.CoreOpeningWays);
            FoldInt32(encounter.Parts.Count);
            for (int i = 0; i < encounter.Parts.Count; i++)
            {
                WarshipPartState part = encounter.Parts[i];
                FoldString(part.PartId);
                FoldString(part.GroupId);
                FoldInt32((int)part.GroupRole);
                FoldInt32(part.X);
                FoldInt32(part.Y);
                FoldInt32(part.Hp);
                FoldInt32(part.MaxHp);
                FoldBool(part.Active);
            }
            ArraySegment<SimEvent> events = encounter.EventsThisTick;
            FoldInt32(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent simEvent = events.Array[events.Offset + i];
                FoldInt32((int)simEvent.Type);
                FoldInt32(simEvent.EntityId);
                FoldInt32(simEvent.X);
                FoldInt32(simEvent.Y);
                FoldInt32(simEvent.Arg);
                FoldString(simEvent.PartId);
            }
        }

        void FoldLaserDefinition(LaserAttackDefinition laser)
        {
            FoldBool(laser != null);
            if (laser == null)
                return;
            FoldInt32(laser.CycleIntervalTicks);
            FoldInt32(laser.TelegraphTicks);
            FoldInt32(laser.FiringTicks);
            FoldInt32(laser.SustainTicks);
            FoldInt32(laser.DissipateTicks);
            FoldInt32(laser.StartOffsetX);
            FoldInt32(laser.StartOffsetY);
            FoldInt32(laser.EndOffsetX);
            FoldInt32(laser.EndOffsetY);
            FoldInt32(laser.ThinHalfWidth);
            FoldInt32(laser.FullHalfWidth);
            FoldInt32(laser.Damage);
            FoldBool(laser.AimsAtPlayer);
        }

        void FoldRewards(IReadOnlyList<RewardOption> rewards)
        {
            FoldInt32(rewards.Count);
            for (int i = 0; i < rewards.Count; i++)
            {
                RewardOption reward = rewards[i];
                FoldString(reward.Id);
                FoldInt32((int)reward.Type);
                FoldInt32((int)reward.Slot);
                FoldInt32(reward.Amount);
                FoldInt32((int)reward.ModifierId);
                FoldString(reward.ModifierKey);
                FoldInt32((int)reward.PrimaryWeaponFamily);
                FoldInt32((int)reward.MissileFamily);
                FoldInt32((int)reward.OptionFormation);
                FoldInt32(reward.Gains.Count);
                for (int effect = 0;
                    effect < reward.Gains.Count;
                    effect++)
                {
                    FoldInt32((int)reward.Gains[effect].Type);
                    FoldInt32(reward.Gains[effect].Amount);
                }
                FoldInt32(reward.Costs.Count);
                for (int effect = 0;
                    effect < reward.Costs.Count;
                    effect++)
                {
                    FoldInt32((int)reward.Costs[effect].Type);
                    FoldInt32(reward.Costs[effect].Amount);
                }
            }
        }

        void FoldContracts(
            IReadOnlyList<ContractOption> contracts)
        {
            FoldInt32(contracts.Count);
            for (int i = 0; i < contracts.Count; i++)
            {
                FoldContract(contracts[i].Definition);
                FoldString(contracts[i].DestinationThemeId);
                FoldInt32(
                    contracts[i].DestinationThemeStageIndex);
            }
        }

        void FoldContract(ContractDefinition contract)
        {
            FoldBool(contract != null);
            if (contract == null)
                return;
            FoldString(contract.Id);
            FoldInt32((int)contract.RiskTier);
            FoldInt32((int)contract.DestinationKind);
            FoldInt32((int)contract.Eligibility);
            FoldInt32(contract.Effects.Count);
            for (int i = 0; i < contract.Effects.Count; i++)
            {
                FoldInt32((int)contract.Effects[i].Type);
                FoldInt32(contract.Effects[i].Numerator);
                FoldInt32(contract.Effects[i].Denominator);
            }
        }

        void FoldContractHistory(
            IReadOnlyList<ContractChoice> history)
        {
            FoldInt32(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                FoldInt32(history[i].TargetBiomeIndex);
                FoldInt32(history[i].OptionIndex);
                FoldString(history[i].ContractId);
                FoldInt32(
                    (int)history[i].DestinationKind);
                FoldString(history[i].DestinationThemeId);
                FoldInt32(
                    history[i].DestinationThemeStageIndex);
            }
        }

        void FoldRewardDecisionHistory(
            IReadOnlyList<RewardDecision> history)
        {
            FoldInt32(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                FoldInt32(history[i].RewardSequence);
                FoldInt32(
                    (int)history[i].SelectionKind);
                FoldInt32(
                    (int)history[i].DecisionKind);
                FoldInt32(history[i].OptionIndex);
            }
        }

        void FoldRoutes(IReadOnlyList<RouteOption> routes)
        {
            FoldInt32(routes.Count);
            for (int i = 0; i < routes.Count; i++)
            {
                RouteOption route = routes[i];
                FoldString(route.ThemeId);
                FoldInt32((int)route.EncounterType);
            }
        }

        void FoldRouteHistory(IReadOnlyList<RouteChoice> history)
        {
            FoldInt32(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                RouteChoice choice = history[i];
                FoldInt32(choice.BiomeIndex);
                FoldInt32(choice.RoomIndex);
                FoldInt32(choice.OptionIndex);
                FoldString(choice.ThemeId);
                FoldInt32((int)choice.EncounterType);
            }
        }

        void FoldBattle(IBattleSim battle)
        {
            FoldInt32(battle.Tick);
            FoldInt64(battle.Score);
            BattleStatistics statistics = battle.Statistics;
            FoldInt64(statistics.ShotsFired);
            FoldInt64(statistics.ShotsHit);
            FoldInt64(statistics.Kills);
            FoldInt64(statistics.CapsulesCollected);
            FoldInt64(statistics.GrazeCount);
            FoldInt64(statistics.BombsUsed);
            FoldInt64(statistics.HitsTaken);
            FoldInt32(battle.MultiplierLevel);
            FoldInt32(battle.ScoreMultiplier);
            FoldInt32(battle.ComboGauge);
            FoldInt32(battle.TicksSinceLastKill);
            FoldInt64(battle.ScrollX);
            FoldInt32(battle.PlayerX);
            FoldInt32(battle.PlayerY);
            FoldInt32(battle.PlayerHp);
            FoldInt32(battle.ShieldStock);
            FoldInt32(battle.BombStock);
            FoldInt32(
                battle.PlayerInvulnerabilityTicksRemaining);
            FoldInt32((int)battle.PlayerWeaponType);
            if (battle is BattleSim concreteBattle)
            {
                FoldInt32(
                    concreteBattle.PrimaryWeaponEvolutionLevel);
                FoldInt32(concreteBattle.BurstShotsRemaining);
                FoldInt32(
                    concreteBattle.BurstCooldownTicksRemaining);
                FoldInt32(concreteBattle.WarshipEncounterTick);
                FoldInt32(
                    concreteBattle.WarshipActiveGroupElapsedTicks);
                FoldInt64(concreteBattle.WarshipScrollRemainder);
                FoldBool(concreteBattle.WarshipCoreOpeningPending);
            }
            StageEnvironmentState environment = battle.Environment;
            FoldInt32(environment.SegmentIndex);
            FoldString(environment.SegmentId);
            FoldBool(environment.HasCorridor);
            FoldInt32(environment.CorridorMinY);
            FoldInt32(environment.CorridorMaxY);
            FoldInt32(environment.CorridorContactDamage);
            FoldInt32(environment.DriftXNumerator);
            FoldInt32(environment.DriftXDenominator);
            FoldInt32(environment.DriftYNumerator);
            FoldInt32(environment.DriftYDenominator);
            FoldBool(battle.VisionObscured);
            FoldInt32(battle.TimeLimitTicks);
            FoldInt32(battle.RemainingTimeTicks);
            FoldBool(battle.TimeLimitExpired);
            FoldInt32(battle.WarshipActiveGroupIndex);
            FoldInt32(battle.WarshipDestroyedAttritionParts);
            FoldInt32(battle.WarshipCoreOpeningWays);
            FoldBool(battle.BossTransitioning);
            FoldInt32(battle.BossTransitionTicksRemaining);
            FoldInt32(battle.BossFormIndex);
            FoldBool(battle.SuctionActive);

            FoldInt32(battle.Bullets.Count);
            for (int i = 0; i < battle.Bullets.Count; i++)
            {
                BulletState bullet = battle.Bullets[i];
                FoldInt32(bullet.Id);
                FoldInt32((int)bullet.Faction);
                FoldInt32((int)bullet.Kind);
                FoldInt32(bullet.X);
                FoldInt32(bullet.Y);
                FoldInt32(bullet.AgeTicks);
                FoldInt32(bullet.DamagePercent);
                FoldInt32(bullet.FixedDamage);
                FoldInt32(bullet.CollisionScalePercent);
                FoldInt32((int)bullet.SignaturePattern);
            }

            FoldInt32(battle.Options.Count);
            for (int i = 0; i < battle.Options.Count; i++)
            {
                OptionState option = battle.Options[i];
                FoldInt32(option.Index);
                FoldInt32(option.X);
                FoldInt32(option.Y);
            }

            FoldInt32(battle.Enemies.Count);
            for (int i = 0; i < battle.Enemies.Count; i++)
            {
                EnemyState enemy = battle.Enemies[i];
                FoldInt32(enemy.Id);
                FoldString(enemy.DefinitionId);
                FoldInt32(enemy.X);
                FoldInt32(enemy.Y);
                FoldInt32(enemy.Hp);
            }

            FoldInt32(battle.SegmentChains.Count);
            for (int i = 0; i < battle.SegmentChains.Count; i++)
            {
                SegmentChainState segment = battle.SegmentChains[i];
                FoldInt32(segment.ChainId);
                FoldInt32(segment.SegmentIndex);
                FoldInt32(segment.X);
                FoldInt32(segment.Y);
                FoldInt32(segment.HeadHp);
                FoldInt32(segment.HeadMaxHp);
                FoldBool(segment.Damageable);
            }

            FoldInt32(battle.Obstacles.Count);
            for (int i = 0; i < battle.Obstacles.Count; i++)
            {
                ObstacleState obstacle = battle.Obstacles[i];
                FoldInt32(obstacle.Id);
                FoldInt32((int)obstacle.Type);
                FoldInt32(obstacle.X);
                FoldInt32(obstacle.Y);
                FoldInt32(obstacle.Hp);
            }
            if (battle is BattleSim obstacleBattle)
            {
                FoldInt32(
                    obstacleBattle.PendingObstacleRegenerations.Count);
                for (int i = 0;
                    i < obstacleBattle.PendingObstacleRegenerations.Count;
                    i++)
                {
                    ObstacleRegenerationState pending =
                        obstacleBattle.PendingObstacleRegenerations[i];
                    FoldInt32(pending.Id);
                    FoldInt32((int)pending.Type);
                    FoldInt32(pending.X);
                    FoldInt32(pending.Y);
                    FoldInt32(pending.MaxHp);
                    FoldBool(pending.BlocksEnemyBullets);
                    FoldInt32(pending.RegenDelayTicks);
                    FoldInt32(pending.RespawnAtTick);
                }
            }
            else
            {
                FoldInt32(0);
            }

            FoldInt32(battle.Capsules.Count);
            for (int i = 0; i < battle.Capsules.Count; i++)
            {
                CapsuleState capsule = battle.Capsules[i];
                FoldInt32(capsule.Id);
                FoldInt32(capsule.X);
                FoldInt32(capsule.Y);
            }

            FoldInt32(battle.BombPickups.Count);
            for (int i = 0; i < battle.BombPickups.Count; i++)
            {
                BombPickupState pickup = battle.BombPickups[i];
                FoldInt32(pickup.Id);
                FoldInt32(pickup.X);
                FoldInt32(pickup.Y);
            }

            FoldInt32(battle.Lasers.Count);
            for (int i = 0; i < battle.Lasers.Count; i++)
            {
                LaserState laser = battle.Lasers[i];
                FoldInt32(laser.Id);
                FoldInt32((int)laser.SourceKind);
                FoldInt32(laser.SourceEntityId);
                FoldInt32(laser.StartX);
                FoldInt32(laser.StartY);
                FoldInt32(laser.EndX);
                FoldInt32(laser.EndY);
                FoldInt32((int)laser.Phase);
                FoldInt32((int)laser.ThicknessStage);
                FoldInt32(laser.HalfWidth);
                // FullHalfWidth is derived presentation metadata: hostile attack
                // definitions already fold it, and player beams use HalfWidth.
                // Keep gameplay trace hashes comparable across renderer fixes.
                FoldInt32(laser.PhaseTicksRemaining);
                FoldInt32(laser.Damage);
            }

            ReadOnlySpan<SimEvent> events = battle.EventsThisTick;
            FoldInt32(events.Length);
            for (int i = 0; i < events.Length; i++)
            {
                SimEvent simEvent = events[i];
                FoldInt32((int)simEvent.Type);
                FoldInt32(simEvent.EntityId);
                FoldInt32(simEvent.X);
                FoldInt32(simEvent.Y);
                FoldInt32(simEvent.Arg);
                FoldString(simEvent.PartId);
                FoldInt32((int)simEvent.BulletKind);
                FoldInt32((int)simEvent.SignaturePattern);
                FoldInt32((int)simEvent.TelegraphKind);
            }

            FoldBool(battle.BossActive);
            FoldBool(battle.BossEntering);
            FoldInt32(
                battle is BattleSim bossBattle
                    ? bossBattle.BossDefeatElapsedTicks
                    : 0);
            BossState boss = battle.Boss;
            FoldInt32(boss.Id);
            FoldInt32(boss.X);
            FoldInt32(boss.Y);
            FoldInt32(boss.Hp);
            FoldInt32(boss.MaxHp);
            FoldInt32(boss.Phase);
            FoldInt32((int)boss.MovementPattern);
            FoldInt32((int)boss.PartVulnerability);
            FoldInt32(boss.FormIndex);
            FoldInt32(battle.BossParts.Count);
            for (int i = 0; i < battle.BossParts.Count; i++)
            {
                BossPartState part = battle.BossParts[i];
                FoldString(part.PartId);
                FoldInt32(part.X);
                FoldInt32(part.Y);
                FoldInt32(part.Hp);
                FoldInt32(part.MaxHp);
            FoldBool(part.Destroyed);
            FoldBool(part.Active);
            FoldBool(part.IsCore);
                FoldBool(part.Invulnerable);
            }
        }

        void FoldBool(bool value)
        {
            FoldByte(value ? (byte)1 : (byte)0);
        }

        void FoldString(string value)
        {
            if (value == null)
            {
                FoldInt32(-1);
                return;
            }

            FoldInt32(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                FoldByte((byte)character);
                FoldByte((byte)(character >> 8));
            }
        }

        void FoldInt32(int value)
        {
            unchecked
            {
                uint bits = (uint)value;
                for (int shift = 0; shift < 32; shift += 8)
                    FoldByte((byte)(bits >> shift));
            }
        }

        void FoldInt64(long value)
        {
            unchecked
            {
                ulong bits = (ulong)value;
                for (int shift = 0; shift < 64; shift += 8)
                    FoldByte((byte)(bits >> shift));
            }
        }

        void FoldUInt64(ulong value)
        {
            unchecked
            {
                for (int shift = 0; shift < 64; shift += 8)
                    FoldByte((byte)(value >> shift));
            }
        }

        void FoldByte(byte value)
        {
            unchecked
            {
                _hash ^= value;
                _hash *= Prime;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;

namespace Shmup.Core.Tests
{
    [TestFixture]
    public sealed class Req109GhostReplayTests
    {
        const int GuardTicks = 2_000;

        [Test]
        public void SameStageOneRecordingProducesSameGhostTrajectoryAndHash()
        {
            RunManager left = CreateRun(0x109AUL);
            RunManager right = CreateRun(0x109AUL);

            ReachFinalClosing(left);
            ReachFinalClosing(right);

            Assert.IsTrue(left.HasStageOneGhostRecording);
            Assert.AreEqual(
                left.StageOneGhostRecordedTicks,
                right.StageOneGhostRecordedTicks);
            Assert.IsTrue(left.Ghost.Active);
            Assert.IsTrue(right.Ghost.Active);
            Assert.IsTrue(HasEvent(left.Battle, SimEventType.GhostSpawned));
            Assert.IsTrue(HasEvent(right.Battle, SimEventType.GhostSpawned));

            int observedTicks = 0;
            while (left.Ghost.Active && observedTicks < GuardTicks)
            {
                InputCommand current = new InputCommand(-1, 0, false);
                left.Step(in current);
                right.Step(in current);

                GhostState leftGhost = left.Ghost;
                GhostState rightGhost = right.Ghost;
                Assert.AreEqual(leftGhost.Active, rightGhost.Active);
                Assert.AreEqual(leftGhost.X, rightGhost.X);
                Assert.AreEqual(leftGhost.Y, rightGhost.Y);
                Assert.AreEqual(leftGhost.IsFiring, rightGhost.IsFiring);
                Assert.AreEqual(
                    leftGhost.PlaybackTick,
                    rightGhost.PlaybackTick);
                Assert.AreEqual(Hash(left), Hash(right));
                observedTicks++;
            }

            Assert.Greater(observedTicks, 0);
            Assert.IsFalse(left.Ghost.Active);
            Assert.IsFalse(right.Ghost.Active);
            Assert.IsTrue(HasEvent(left.Battle, SimEventType.GhostEnded));
            Assert.IsTrue(HasEvent(right.Battle, SimEventType.GhostEnded));
            TestContext.WriteLine(
                $"REQ-109 AUDIT PASS seed=0x109A "
                + $"ghostTicks={observedTicks} hash={Hash(left)}");
        }

        [Test]
        public void GhostFiresFixedLowLevelStraightProjectileWithRealDamage()
        {
            RunManager run = CreateRun(
                0x109BUL,
                new GhostReplayConfig(
                    fixedWeaponLevel: 1,
                    fireIntervalTicks: 2,
                    maximumInputRuns: 128));
            ReachFinalClosing(run);

            int before = run.Battle.Bullets.Count;
            InputCommand none = InputCommand.None;
            run.Step(in none);

            BulletState ghostShot = default;
            bool found = false;
            for (int i = before; i < run.Battle.Bullets.Count; i++)
            {
                BulletState candidate = run.Battle.Bullets[i];
                if (candidate.Kind != BulletKind.GhostMainShot)
                    continue;
                ghostShot = candidate;
                found = true;
                break;
            }

            Assert.IsTrue(found);
            Assert.AreEqual(BulletFaction.Player, ghostShot.Faction);
            Assert.AreEqual(10, ghostShot.FixedDamage);
        }

        [Test]
        public void GhostProjectileAppliesItsExactDamageToEnemy()
        {
            var enemy = new EnemyDefinition(
                "ghost_target",
                20,
                0,
                EnemyMovePattern.Static,
                0,
                1,
                2,
                2,
                0,
                0,
                1);
            BattleContent baseContent = CreateContent();
            WeaponDefinition weapon = baseContent.PlayerWeapon;
            var content = new BattleContent(
                new[] { enemy },
                new[] { weapon },
                weapon.Id);
            var plan = new StagePlan(
                new[]
                {
                    new StageSegment(
                        "ghost_damage_segment",
                        30,
                        new[]
                        {
                            new SpawnEvent(0, enemy.Id, 32, 0)
                        },
                        1,
                        1,
                        new[] { 1 })
                },
                "none",
                1,
                1,
                1);
            var battle = new BattleSim(
                CreateConfig(),
                new Rng(0x109BUL),
                plan,
                content,
                PowerUpGauge.CreateDefault());

            Assert.IsTrue(battle.TrySpawnGhostMainShot(0, 0, 7));
            InputCommand none = InputCommand.None;
            battle.Step(in none);

            Assert.AreEqual(1, battle.Enemies.Count);
            Assert.AreEqual(13, battle.Enemies[0].Hp);
            Assert.IsTrue(HasEvent(battle, SimEventType.EnemyHit));
        }

        [Test]
        public void StageThreeDevRunHasNoRecordingAndSpawnsNoGhost()
        {
            RunManager run = CreateRun(
                0x109CUL,
                GhostReplayConfig.CreateDefault(),
                startStageIndex: 3);

            ReachFinalClosing(run);

            Assert.IsFalse(run.StageOneGhostRecordingStarted);
            Assert.IsFalse(run.HasStageOneGhostRecording);
            Assert.IsFalse(run.Ghost.Active);
            Assert.IsFalse(HasEvent(
                run.Battle,
                SimEventType.GhostSpawned));
        }

        [Test]
        public void StageOneContinueKeepsFirstAttemptRecording()
        {
            RunManager run = CreateLethalContinueRun();
            InputCommand firstAttempt = new InputCommand(1, 0, true);
            run.Step(in firstAttempt);

            Assert.AreEqual(RunState.RunOver, run.State);
            Assert.IsTrue(run.StageOneGhostRecordingFinalized);
            Assert.AreEqual(1, run.StageOneGhostRecordedTicks);
            RecordedInputRun firstRun =
                run.GetStageOneGhostRecordedRun(0);
            Assert.AreEqual(1, firstRun.Command.MoveX);

            Assert.IsTrue(run.TryUseContinue());
            InputCommand retry = new InputCommand(-1, 0, false);
            run.Step(in retry);

            Assert.AreEqual(1, run.StageOneGhostRecordedTicks);
            Assert.AreEqual(1, run.StageOneGhostRecordedRunCount);
            RecordedInputRun retained =
                run.GetStageOneGhostRecordedRun(0);
            Assert.AreEqual(1, retained.Command.MoveX);
            Assert.IsTrue(retained.Command.Fire);
        }

        [Test]
        public void SuspendRoundTripRetainsRecordingAndRejectsSchema26()
        {
            RunManager source = CreateRun(0x109DUL);
            ReachBiomeOpening(source, 2);
            RunSuspendData data = source.ExportSuspendData();

            Assert.AreEqual(29, data.schemaVersion);
            Assert.IsNotNull(data.ghostRecording);
            Assert.IsTrue(data.ghostRecording.finalized);
            Assert.Greater(data.ghostRecording.totalTicks, 0);
            Assert.IsTrue(Shmup.Core.SaveDataIntegrity.HasValidChecksum(data));

            RunManager resumed = RunManager.ResumeFromSuspendData(
                data,
                new FixedStageGenerator(CreatePlan()),
                CreateConfig(),
                CreateContent(),
                PowerUpGauge.CreateDefault());
            Assert.AreEqual(
                source.StageOneGhostRecordedTicks,
                resumed.StageOneGhostRecordedTicks);
            Assert.AreEqual(
                source.StageOneGhostRecordedRunCount,
                resumed.StageOneGhostRecordedRunCount);
            Assert.IsTrue(resumed.HasStageOneGhostRecording);

            data.ghostRecording.fireIntervalTicks++;
            Assert.IsFalse(
                Shmup.Core.SaveDataIntegrity.HasValidChecksum(data));
            data.ghostRecording.fireIntervalTicks--;
            Assert.IsTrue(
                Shmup.Core.SaveDataIntegrity.HasValidChecksum(data));

            data.schemaVersion = 26;
            data.checksum = null;
            Assert.Throws<ArgumentException>(
                () => Shmup.Core.SaveDataIntegrity.MigrateAndValidate(data));
        }

        [Test]
        public void SuspendAtFinalBossBoundaryRestoresActiveGhostExactly()
        {
            RunManager source = CreateRun(0x109FUL);
            ReachFinalClosing(source);
            int guard = 0;
            while (!source.IsBiomeBoss && guard < GuardTicks)
            {
                AdvanceRun(source);
                guard++;
            }
            Assert.Less(guard, GuardTicks);
            Assert.IsTrue(source.Ghost.Active);

            RunSuspendData data = source.ExportSuspendData();
            Assert.IsTrue(data.ghostRecording.playbackActive);
            RunManager resumed = RunManager.ResumeFromSuspendData(
                data,
                new FixedStageGenerator(CreatePlan()),
                CreateConfig(),
                CreateContent(),
                PowerUpGauge.CreateDefault());

            Assert.IsTrue(resumed.Ghost.Active);
            Assert.AreEqual(source.Ghost.X, resumed.Ghost.X);
            Assert.AreEqual(source.Ghost.Y, resumed.Ghost.Y);
            Assert.AreEqual(
                source.Ghost.PlaybackTick,
                resumed.Ghost.PlaybackTick);
            Assert.AreEqual(Hash(source), Hash(resumed));

            InputCommand input = InputCommand.None;
            source.Step(in input);
            resumed.Step(in input);
            Assert.AreEqual(Hash(source), Hash(resumed));
        }

        static void ReachFinalClosing(RunManager run)
        {
            int guard = 0;
            while (guard < GuardTicks
                && !(run.BiomeIndex == run.FinalStageIndex
                    && run.StageSection == RunStageSection.Closing
                    && run.State == RunState.Playing))
            {
                AdvanceRun(run);
                guard++;
            }
            Assert.Less(guard, GuardTicks);
        }

        static void ReachBiomeOpening(RunManager run, int biomeIndex)
        {
            int guard = 0;
            while (guard < GuardTicks
                && !(run.BiomeIndex == biomeIndex
                    && run.RoomIndex == 1
                    && run.State == RunState.Playing))
            {
                AdvanceRun(run);
                guard++;
            }
            Assert.Less(guard, GuardTicks);
        }

        static void AdvanceRun(RunManager run)
        {
            if (run.State == RunState.AwaitingReward)
            {
                Assert.IsTrue(run.ChooseReward(0));
                return;
            }
            if (run.State == RunState.AwaitingContract)
            {
                Assert.IsTrue(run.ChooseContract(0));
                return;
            }
            Assert.AreEqual(RunState.Playing, run.State);
            int tick = run.SimulationTicksElapsed;
            InputCommand input = tick % 4 == 0
                ? new InputCommand(1, 1, true)
                : tick % 4 == 1
                    ? new InputCommand(0, -1, true)
                    : tick % 4 == 2
                        ? InputCommand.Analog(2, -1, true)
                        : new InputCommand(-1, 0, false);
            run.Step(in input);
        }

        static bool HasEvent(IBattleSim battle, SimEventType type)
        {
            ReadOnlySpan<SimEvent> events = battle.EventsThisTick;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].Type == type)
                    return true;
            }
            return false;
        }

        static string Hash(RunManager run)
        {
            var hasher = new DeterminismAuditHasher();
            hasher.FoldRunState(run);
            return hasher.HexHash;
        }

        static RunManager CreateRun(
            ulong seed,
            GhostReplayConfig ghostReplay = null,
            int startStageIndex = 1)
        {
            return new RunManager(
                seed,
                new FixedStageGenerator(CreatePlan()),
                CreateConfig(),
                CreateContent(),
                PowerUpGauge.CreateDefault(),
                new MetaProgression(1, 1),
                StageDifficultyCurve.CreateDefault(),
                null,
                null,
                1,
                1,
                new RunProgressionConfig(5, 3),
                new RunConfig(
                    startStageIndex,
                    ghostReplay: ghostReplay
                        ?? new GhostReplayConfig(
                            fixedWeaponLevel: 1,
                            fireIntervalTicks: 2,
                            maximumInputRuns: 128)));
        }

        static RunManager CreateLethalContinueRun()
        {
            var enemy = new EnemyDefinition(
                "ghost_contact",
                10,
                1,
                EnemyMovePattern.Static,
                0,
                1,
                2,
                2,
                0,
                0,
                1);
            BattleContent baseContent = CreateContent();
            WeaponDefinition weapon = baseContent.PlayerWeapon;
            var content = new BattleContent(
                new[] { enemy },
                new[] { weapon },
                weapon.Id);
            var plan = new StagePlan(
                new[]
                {
                    new StageSegment(
                        "ghost_lethal_segment",
                        30,
                        new[]
                        {
                            new SpawnEvent(
                                0,
                                enemy.Id,
                                4,
                                0)
                        },
                        1,
                        1,
                        new[] { 1 })
                },
                "none",
                1,
                1,
                1);
            BattleSimConfig config = CreateConfig();
            config.StartingShieldStock = 0;
            return new RunManager(
                0x109EUL,
                new FixedStageGenerator(plan),
                config,
                content,
                PowerUpGauge.CreateDefault(),
                new MetaProgression(1, 1),
                StageDifficultyCurve.CreateDefault(),
                null,
                null,
                1,
                1,
                new RunProgressionConfig(5, 3),
                new RunConfig(
                    initialContinueStock: 1,
                    ghostReplay: new GhostReplayConfig(
                        maximumInputRuns: 16)));
        }

        static BattleContent CreateContent()
        {
            var weapon = new WeaponDefinition(
                "ghost_test_shot",
                10,
                2,
                32,
                1,
                1,
                1);
            return new BattleContent(
                Array.Empty<EnemyDefinition>(),
                new[] { weapon },
                weapon.Id);
        }

        static BattleSimConfig CreateConfig()
        {
            return new BattleSimConfig
            {
                PlayerSpeedNumerator = 4,
                PlayerSpeedDenominator = 1,
                PlayerBulletSpeedNumerator = 32,
                PlayerBulletSpeedDenominator = 1,
                FireIntervalTicks = 2,
                MainShotBaseDamage = 10,
                MainShotHalfWidth = 1,
                MainShotHalfHeight = 1,
                MaxBullets = 64,
                PlayerMinX = -100,
                PlayerMaxX = 100,
                PlayerMinY = -100,
                PlayerMaxY = 100,
                PlayerSpawnX = 0,
                PlayerSpawnY = 0,
                BulletDespawnX = 1_000,
                EnemyDespawnX = -1_000,
                StartingShieldStock = 1,
                MaxShieldStock = 3,
                PlayerHalfWidth = 1,
                PlayerHalfHeight = 1,
                CapsuleHalfWidth = 0,
                CapsuleHalfHeight = 0,
                CapsuleNoDropWeight = 1,
                ScrollSpeedNumerator = 0,
                ScrollSpeedDenominator = 1,
                EnemyBulletSpeedNumerator = 1,
                EnemyBulletSpeedDenominator = 1,
                EnemyBulletHalfWidth = 1,
                EnemyBulletHalfHeight = 1,
                EnemyBulletDamage = 0,
                MaxEnemyBullets = 8
            };
        }

        static StagePlan CreatePlan()
        {
            return new StagePlan(
                new[]
                {
                    new StageSegment(
                        "ghost_test_segment",
                        3,
                        Array.Empty<SpawnEvent>(),
                        1,
                        1,
                        new[] { 1 })
                },
                "none",
                1,
                1,
                1);
        }

        sealed class FixedStageGenerator : IStageGenerator
        {
            readonly StagePlan _plan;

            public FixedStageGenerator(StagePlan plan)
            {
                _plan = plan;
            }

            public StagePlan Generate(
                ulong seed,
                int stageIndex,
                int difficulty)
            {
                return _plan;
            }
        }
    }
}

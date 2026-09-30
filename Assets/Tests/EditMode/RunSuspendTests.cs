using System;
using NUnit.Framework;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;

namespace Shmup.Core.Tests
{
    public sealed class RunSuspendTests
    {
        [Test]
        public void NoContractChecksumSurvivesNullToEmptyStringSerialization()
        {
            RunManager source = CreateRun(new BoundaryStageGenerator());
            RunSuspendData data = source.ExportSuspendData();
            Assert.IsNull(data.activeContractId);
            data.activeContractId = string.Empty;

            Assert.IsTrue(SaveDataIntegrity.HasValidChecksum(data));
            RunManager resumed = Resume(data, new BoundaryStageGenerator());
            AssertPlansEqual(source.StagePlan, resumed.StagePlan);

            data.activeContractId = "different_contract";
            Assert.IsFalse(SaveDataIntegrity.HasValidChecksum(data));
        }

        [Test]
        public void ExportDuringStage_ReturnsIndependentStageStartSnapshot()
        {
            RunManager run = CreateRun(new BoundaryStageGenerator());
            var fire = new InputCommand(1, 0, true);
            Step(run, 3, in fire);
            RunSuspendData atStageStart = run.ExportSuspendData();

            Step(run, 17, in fire);
            RunSuspendData duringStage = run.ExportSuspendData();

            Assert.AreEqual(1, duringStage.biomeIndex);
            Assert.AreEqual(1, duringStage.roomIndex);
            Assert.AreEqual(atStageStart.score, duringStage.score);
            Assert.AreEqual(
                atStageStart.shotsFired,
                duringStage.shotsFired);
            Assert.AreEqual(0, duringStage.powerUpCursor);
            CollectionAssert.AreEqual(
                atStageStart.powerUpLevels,
                duringStage.powerUpLevels);

            duringStage.powerUpLevels[0] = 99;
            RunSuspendData independentCopy = run.ExportSuspendData();
            Assert.AreEqual(1, independentCopy.powerUpLevels[0]);
        }

        [Test]
        public void ExportResumeRoundTrip_RebuildsSameStagePlanAndBoundary()
        {
            var sourceGenerator = new BoundaryStageGenerator();
            RunManager source = CreateRun(sourceGenerator);
            var fire = new InputCommand(1, -1, true);
            Step(source, 3, in fire);
            StagePlan expectedPlan = source.StagePlan;
            Step(source, 23, in fire);

            RunSuspendData exported = source.ExportSuspendData();
            var resumeGenerator = new BoundaryStageGenerator();
            RunManager resumed = Resume(exported, resumeGenerator);
            RunSuspendData roundTrip = resumed.ExportSuspendData();

            Assert.AreEqual(0, resumed.Battle.Tick);
            Assert.AreEqual(RunState.Playing, resumed.State);
            Assert.AreEqual(1, resumeGenerator.Calls);
            AssertPlansEqual(expectedPlan, resumed.StagePlan);
            AssertSuspendDataEqual(exported, roundTrip);
        }

        [Test]
        public void LargeCapsAndPartialProgress_RoundTripThroughV11Checksum()
        {
            var curve = new PowerUpCostCurve(2, 0, 0);
            var sourceGauge = new PowerUpGauge(
                new[] { 64, 48, 32, 24 },
                curve);
            sourceGauge.ImportLevels(new[] { 63, 47, 31, 23 });
            var source = new RunManager(
                0x53UL,
                new BoundaryStageGenerator(),
                CreateConfig(),
                CreateContent(),
                sourceGauge);

            RunSuspendData data = source.ExportSuspendData();
            data.powerUpProgress[0] = 1;
            SaveDataIntegrity.Seal(data);
            var resumeGauge = new PowerUpGauge(
                new[] { 64, 48, 32, 24 },
                curve);
            RunManager resumed = RunManager.ResumeFromSuspendData(
                data,
                new BoundaryStageGenerator(),
                CreateConfig(),
                CreateContent(),
                resumeGauge);

            AssertAll(() =>
            {
                Assert.AreEqual(
                    RunSuspendData.CurrentSchemaVersion,
                    data.schemaVersion);
                Assert.IsTrue(SaveDataIntegrity.HasValidChecksum(data));
                CollectionAssert.AreEqual(
                    new[] { 63, 47, 31, 23, 0, 0, 0, 0 },
                    resumed.PowerUpGauge.ExportLevels());
                CollectionAssert.AreEqual(
                    new[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                    resumed.PowerUpGauge.ExportProgress());
            });
        }

        [Test]
        public void ResumeThenNTicks_MatchesContinuousPlayFromStageStart()
        {
            RunManager source = CreateRun(new BoundaryStageGenerator());
            var firstStageInput = new InputCommand(0, 1, true);
            Step(source, 3, in firstStageInput);
            RunSuspendData stageStart = source.ExportSuspendData();

            RunManager continuous = Resume(
                stageStart,
                new BoundaryStageGenerator());
            RunManager interrupted = Resume(
                stageStart,
                new BoundaryStageGenerator());
            for (int tick = 0; tick < 37; tick++)
            {
                InputCommand discarded = InputForTick(tick + 900);
                interrupted.Step(in discarded);
            }

            RunSuspendData midStageExport =
                interrupted.ExportSuspendData();
            RunManager resumed = Resume(
                midStageExport,
                new BoundaryStageGenerator());

            for (int tick = 0; tick < 90; tick++)
            {
                InputCommand input = InputForTick(tick);
                continuous.Step(in input);
                resumed.Step(in input);

                var expectedHash = new DeterminismAuditHasher();
                var actualHash = new DeterminismAuditHasher();
                expectedHash.FoldRunState(continuous);
                actualHash.FoldRunState(resumed);
                Assert.AreEqual(
                    expectedHash.Hash,
                    actualHash.Hash,
                    $"determinism diverged at resumed tick {tick + 1}");
            }
        }

        [Test]
        public void Resume_RestoresRewardCountsModifiersAndPassiveBoundaryValues()
        {
            RunManager source = CreateRun(new BoundaryStageGenerator());
            InputCommand none = InputCommand.None;
            Step(source, 3, in none);
            RunSuspendData data = source.ExportSuspendData();
            data.roomIndex = 2;
            data.roomsCleared = 1;
            data.playerHp = 1;
            data.shieldStock = 4;
            data.shieldRemaining = 4;
            data.maxShieldStock = 5;
            data.bombStock = 2;
            data.maxBombStock = 3;
            data.rewardAcquisitions = new[]
            {
                new RewardAcquisitionData
                {
                    rewardId = "repair",
                    count = 1
                }
            };
            data.activeModifiers = (int)BattleModifier.PierceShot;
            SaveDataIntegrity.Seal(data);

            RewardCatalog rewards = CreateRewardCatalog();
            ShipDefinition ship = ShipDefinition.CreateDefault();
            RunManager resumed = RunManager.ResumeFromSuspendData(
                data,
                new BoundaryStageGenerator(),
                CreateConfig(),
                CreateContent(),
                PowerUpGauge.CreateDefault(),
                rewards,
                ship);
            RunSuspendData restored = resumed.ExportSuspendData();

            Assert.AreEqual(1, resumed.Battle.PlayerHp);
            Assert.AreEqual(4, resumed.Battle.ShieldStock);
            Assert.AreEqual(2, resumed.Battle.BombStock);
            Assert.AreEqual(
                BattleModifier.PierceShot,
                resumed.ActiveModifiers);
            Assert.AreEqual(1, restored.rewardAcquisitions.Length);
            Assert.AreEqual(
                "repair",
                restored.rewardAcquisitions[0].rewardId);
            Assert.AreEqual(1, restored.rewardAcquisitions[0].count);
            Assert.AreEqual(1, restored.playerHp);
            Assert.AreEqual(4, restored.shieldStock);
            Assert.AreEqual(2, restored.bombStock);
        }

        [Test]
        public void CorruptedSuspendData_IsRejectedBeforeStageGeneration()
        {
            RunManager source = CreateRun(new BoundaryStageGenerator());
            InputCommand none = InputCommand.None;
            Step(source, 3, in none);

            RunSuspendData badSchema = source.ExportSuspendData();
            badSchema.schemaVersion++;
            AssertRejectedBeforeGeneration(badSchema);

            RunSuspendData badShip = source.ExportSuspendData();
            badShip.shipId = "unknown_ship";
            SaveDataIntegrity.Seal(badShip);
            AssertRejectedBeforeGeneration(badShip);

            RunSuspendData badShield = source.ExportSuspendData();
            badShield.shieldRemaining++;
            SaveDataIntegrity.Seal(badShield);
            AssertRejectedBeforeGeneration(badShield);

            RunSuspendData badReward = source.ExportSuspendData();
            badReward.rewardAcquisitions = new[]
            {
                new RewardAcquisitionData
                {
                    rewardId = "missing_reward",
                    count = 1
                }
            };
            SaveDataIntegrity.Seal(badReward);
            AssertRejectedBeforeGeneration(badReward);

            RunSuspendData badModifiers = source.ExportSuspendData();
            badModifiers.activeModifiers = 1 << 20;
            SaveDataIntegrity.Seal(badModifiers);
            AssertRejectedBeforeGeneration(badModifiers);

            RunSuspendData badDifficulty = source.ExportSuspendData();
            badDifficulty.difficultyMultiplierNumerator = 0;
            SaveDataIntegrity.Seal(badDifficulty);
            AssertRejectedBeforeGeneration(badDifficulty);
        }

        [Test]
        public void SchemaOneSuspend_DefaultsToNormalDifficulty()
        {
            RunManager source = CreateRun(new BoundaryStageGenerator());
            RunSuspendData legacy = source.ExportSuspendData();
            legacy.schemaVersion = 1;
            legacy.checksum = null;
            legacy.difficultyMultiplierNumerator = 0;
            legacy.difficultyMultiplierDenominator = 0;

            RunManager resumed = Resume(
                legacy,
                new BoundaryStageGenerator());

            Assert.AreEqual(1, resumed.DifficultyMultiplierNumerator);
            Assert.AreEqual(1, resumed.DifficultyMultiplierDenominator);
        }

        [Test]
        public void SchemaFourteenSuspendIsRejectedAfterGaugeExpansion()
        {
            RunSuspendData legacy =
                CreateRun(new BoundaryStageGenerator())
                    .ExportSuspendData();
            legacy.schemaVersion = 14;
            legacy.checksum = null;

            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(legacy));
        }

        [Test]
        public void SchemaFifteenSuspendIsRejectedAfterShipGaugeChange()
        {
            RunSuspendData legacy =
                CreateRun(new BoundaryStageGenerator())
                    .ExportSuspendData();
            legacy.schemaVersion = 15;
            legacy.checksum = null;

            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(legacy));
        }

        [Test]
        public void LegacySuspendSchemas_MigrateToChecksummedCurrentPayload()
        {
            RunManager source = CreateRun(new BoundaryStageGenerator());
            for (int version = 1; version <= 3; version++)
            {
                RunSuspendData legacy = source.ExportSuspendData();
                legacy.schemaVersion = version;
                legacy.checksum = null;
                if (version == 1)
                {
                    legacy.difficultyMultiplierNumerator = 0;
                    legacy.difficultyMultiplierDenominator = 0;
                }
                if (version < 3)
                    legacy.routeChoices = null;

                RunSuspendData migrated =
                    SaveDataIntegrity.MigrateAndValidate(legacy);

                Assert.AreEqual(
                    RunSuspendData.CurrentSchemaVersion,
                    migrated.schemaVersion);
                Assert.AreEqual(
                    RunProgressionConfig.DefaultFinalStageIndex,
                    migrated.finalStageIndex);
                Assert.IsNotNull(migrated.routeChoices);
                Assert.IsTrue(
                    SaveDataIntegrity.HasValidChecksum(migrated));
                Assert.AreEqual(version, legacy.schemaVersion);
            }
        }

        [Test]
        public void SchemaSevenSuspend_MigratesHpAndShieldToSingleStock()
        {
            RunSuspendData legacy =
                CreateRun(new BoundaryStageGenerator())
                    .ExportSuspendData();
            legacy.schemaVersion = 7;
            legacy.checksum = null;
            legacy.playerHp = 3;
            legacy.shieldRemaining = 2;
            legacy.shieldStock = 0;
            legacy.maxShieldStock = 0;

            RunSuspendData migrated =
                SaveDataIntegrity.MigrateAndValidate(legacy);

            Assert.AreEqual(
                RunSuspendData.CurrentSchemaVersion,
                migrated.schemaVersion);
            Assert.AreEqual(1, migrated.playerHp);
            Assert.AreEqual(3, migrated.shieldStock);
            Assert.AreEqual(3, migrated.shieldRemaining);
            Assert.AreEqual(
                BattleSimConfig.ProvisionalMaxShieldStock,
                migrated.maxShieldStock);
            Assert.IsTrue(
                SaveDataIntegrity.HasValidChecksum(migrated));
        }

        [Test]
        public void SchemaEightSuspend_MigratesToEmptyBombStock()
        {
            RunSuspendData legacy =
                CreateRun(new BoundaryStageGenerator())
                    .ExportSuspendData();
            legacy.schemaVersion = 8;
            legacy.checksum = null;
            legacy.bombStock = 2;
            legacy.maxBombStock = 9;

            RunSuspendData migrated =
                SaveDataIntegrity.MigrateAndValidate(legacy);

            Assert.AreEqual(0, migrated.bombStock);
            Assert.AreEqual(
                BattleSimConfig.ProvisionalMaxBombStock,
                migrated.maxBombStock);
            Assert.IsTrue(
                SaveDataIntegrity.HasValidChecksum(migrated));
        }

        [TestCase(21)]
        [TestCase(22)]
        public void PreJitterSuspendSchemasAreRejected(int schemaVersion)
        {
            RunSuspendData legacy =
                CreateRun(new BoundaryStageGenerator())
                    .ExportSuspendData();
            legacy.schemaVersion = schemaVersion;
            legacy.checksum = null;

            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(legacy));
        }

        [Test]
        public void CurrentSuspendChecksumMismatch_IsClearlyRejected()
        {
            RunSuspendData corrupted =
                CreateRun(new BoundaryStageGenerator())
                    .ExportSuspendData();
            corrupted.score++;

            ArgumentException error =
                Assert.Throws<ArgumentException>(
                    () => SaveDataIntegrity.MigrateAndValidate(corrupted));

            StringAssert.Contains("checksum", error.Message);
        }

        [Test]
        public void ExportOutsidePlayingState_IsRejected()
        {
            var generator = new LethalStageGenerator();
            BattleSimConfig config = CreateConfig();
            config.StartingShieldStock = 0;
            RunManager run = new RunManager(
                42UL,
                generator,
                config,
                CreateContent(),
                PowerUpGauge.CreateDefault());
            InputCommand none = InputCommand.None;
            run.Step(in none);

            Assert.AreEqual(RunState.RunOver, run.State);
            Assert.Throws<InvalidOperationException>(
                () => run.ExportSuspendData());
        }

        static RunManager CreateRun(IStageGenerator generator)
        {
            var gauge = PowerUpGauge.CreateDefault();
            gauge.ImportLevels(new[] { 1, 0, 2, 2 });
            gauge.Collect();
            return new RunManager(
                0x5EED1234UL,
                generator,
                CreateConfig(),
                CreateContent(),
                gauge);
        }

        static RunManager Resume(
            RunSuspendData data,
            IStageGenerator generator)
        {
            return RunManager.ResumeFromSuspendData(
                data,
                generator,
                CreateConfig(),
                CreateContent(),
                PowerUpGauge.CreateDefault());
        }

        static BattleSimConfig CreateConfig()
        {
            return new BattleSimConfig
            {
                PlayerSpeedNumerator = 3,
                PlayerSpeedDenominator = 2,
                PlayerBulletSpeedPerTick = 2,
                FireIntervalTicks = 2,
                MaxBullets = 128,
                PlayerMinX = -1000,
                PlayerMaxX = 1000,
                PlayerMinY = -1000,
                PlayerMaxY = 1000,
                BulletDespawnX = 1000,
                EnemyDespawnX = -1000,
                PlayerSpawnX = 0,
                PlayerSpawnY = 0,
                PlayerMaxHp = 5,
                PlayerHalfWidth = 0,
                PlayerHalfHeight = 0,
                CapsuleHalfWidth = 0,
                CapsuleHalfHeight = 0,
                CapsuleNoDropWeight = 0,
                ScrollSpeedNumerator = 0,
                ScrollSpeedDenominator = 1
            };
        }

        static BattleContent CreateContent()
        {
            var harmless = new EnemyDefinition(
                "harmless",
                20,
                0,
                EnemyMovePattern.Static,
                0,
                1,
                0,
                0,
                0,
                0,
                1);
            var lethal = new EnemyDefinition(
                "lethal",
                20,
                10,
                EnemyMovePattern.Static,
                0,
                1,
                0,
                0,
                0,
                0,
                1);
            var weapon = new WeaponDefinition(
                "shot",
                1,
                2,
                2,
                1,
                0,
                0);
            return new BattleContent(
                new[] { harmless, lethal },
                new[] { weapon },
                weapon.Id);
        }

        static RewardCatalog CreateRewardCatalog()
        {
            return new RewardCatalog(
                RunManager.RewardOptionCount,
                new[]
                {
                    new RewardDefinition(
                        "repair",
                        RewardType.RepairHp,
                        PowerUpSlot.MainShot,
                        2,
                        1,
                        1,
                        int.MaxValue,
                        2),
                    new RewardDefinition(
                        "modifier",
                        RewardType.Modifier,
                        PowerUpSlot.MainShot,
                        1,
                        1,
                        1,
                        int.MaxValue,
                        2,
                        BattleModifier.PierceShot),
                    new RewardDefinition(
                        "capsules",
                        RewardType.Capsules,
                        PowerUpSlot.MainShot,
                        1,
                        1,
                        1,
                        int.MaxValue)
                });
        }

        static InputCommand InputForTick(int tick)
        {
            return new InputCommand(
                tick % 5 < 3 ? 1 : -1,
                tick % 7 < 2 ? 1 : tick % 7 > 4 ? -1 : 0,
                tick % 3 != 1);
        }

        static void Step(
            RunManager run,
            int count,
            in InputCommand input)
        {
            for (int i = 0; i < count; i++)
                run.Step(in input);
        }

        static void AssertRejectedBeforeGeneration(
            RunSuspendData data)
        {
            var generator = new BoundaryStageGenerator();
            Assert.Throws<ArgumentException>(
                () => Resume(data, generator));
            Assert.AreEqual(0, generator.Calls);
        }

        static void AssertSuspendDataEqual(
            RunSuspendData expected,
            RunSuspendData actual)
        {
            Assert.AreEqual(expected.schemaVersion, actual.schemaVersion);
            Assert.AreEqual(expected.runSeed, actual.runSeed);
            Assert.AreEqual(expected.runNumber, actual.runNumber);
            Assert.AreEqual(expected.stageIndex, actual.stageIndex);
            Assert.AreEqual(expected.score, actual.score);
            Assert.AreEqual(expected.shotsFired, actual.shotsFired);
            Assert.AreEqual(expected.shotsHit, actual.shotsHit);
            Assert.AreEqual(expected.kills, actual.kills);
            Assert.AreEqual(
                expected.capsulesCollected,
                actual.capsulesCollected);
            Assert.AreEqual(expected.grazeCount, actual.grazeCount);
            Assert.AreEqual(expected.bombsUsed, actual.bombsUsed);
            Assert.AreEqual(
                expected.stagesCleared,
                actual.stagesCleared);
            CollectionAssert.AreEqual(
                expected.powerUpLevels,
                actual.powerUpLevels);
            Assert.AreEqual(
                expected.powerUpCursor,
                actual.powerUpCursor);
            Assert.AreEqual(expected.playerHp, actual.playerHp);
            Assert.AreEqual(
                expected.shieldRemaining,
                actual.shieldRemaining);
            Assert.AreEqual(
                expected.shieldStock,
                actual.shieldStock);
            Assert.AreEqual(
                expected.maxShieldStock,
                actual.maxShieldStock);
            Assert.AreEqual(
                expected.bombStock,
                actual.bombStock);
            Assert.AreEqual(
                expected.maxBombStock,
                actual.maxBombStock);
            Assert.AreEqual(
                expected.activeModifiers,
                actual.activeModifiers);
            Assert.AreEqual(expected.shipId, actual.shipId);
            Assert.AreEqual(
                expected.fireIntervalTicks,
                actual.fireIntervalTicks);
            Assert.AreEqual(
                expected.mainShotBaseDamage,
                actual.mainShotBaseDamage);
            Assert.AreEqual(
                expected.playerSpeedNumerator,
                actual.playerSpeedNumerator);
            Assert.AreEqual(
                expected.playerSpeedDenominator,
                actual.playerSpeedDenominator);
            Assert.AreEqual(
                expected.difficultyMultiplierNumerator,
                actual.difficultyMultiplierNumerator);
            Assert.AreEqual(
                expected.difficultyMultiplierDenominator,
                actual.difficultyMultiplierDenominator);
            Assert.AreEqual(
                expected.finalStageIndex,
                actual.finalStageIndex);
            Assert.AreEqual(expected.checksum, actual.checksum);
            Assert.AreEqual(
                expected.rewardAcquisitions.Length,
                actual.rewardAcquisitions.Length);
            for (int i = 0;
                i < expected.rewardAcquisitions.Length;
                i++)
            {
                Assert.AreEqual(
                    expected.rewardAcquisitions[i].rewardId,
                    actual.rewardAcquisitions[i].rewardId);
                Assert.AreEqual(
                    expected.rewardAcquisitions[i].count,
                    actual.rewardAcquisitions[i].count);
            }
        }

        static void AssertPlansEqual(
            StagePlan expected,
            StagePlan actual)
        {
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.ThemeId, actual.ThemeId);
            Assert.AreEqual(
                expected.RequestedThemeId,
                actual.RequestedThemeId);
            Assert.AreEqual(
                expected.ThemeFallbackApplied,
                actual.ThemeFallbackApplied);
            Assert.AreEqual(expected.LaneCount, actual.LaneCount);
            Assert.AreEqual(
                expected.StartLaneMask,
                actual.StartLaneMask);
            Assert.AreEqual(
                expected.BossEntryLaneMask,
                actual.BossEntryLaneMask);
            Assert.AreEqual(
                expected.Segments.Count,
                actual.Segments.Count);
            for (int i = 0; i < expected.Segments.Count; i++)
            {
                Assert.AreEqual(
                    expected.Segments[i].SegmentId,
                    actual.Segments[i].SegmentId);
                Assert.AreEqual(
                    expected.Segments[i].LengthTicks,
                    actual.Segments[i].LengthTicks);
                Assert.AreEqual(
                    expected.Segments[i].Spawns.Count,
                    actual.Segments[i].Spawns.Count);
                for (int j = 0;
                    j < expected.Segments[i].Spawns.Count;
                    j++)
                {
                    Assert.AreEqual(
                        expected.Segments[i].Spawns[j].Tick,
                        actual.Segments[i].Spawns[j].Tick);
                    Assert.AreEqual(
                        expected.Segments[i].Spawns[j].EnemyId,
                        actual.Segments[i].Spawns[j].EnemyId);
                    Assert.AreEqual(
                        expected.Segments[i].Spawns[j].X,
                        actual.Segments[i].Spawns[j].X);
                    Assert.AreEqual(
                        expected.Segments[i].Spawns[j].Y,
                        actual.Segments[i].Spawns[j].Y);
                }
            }
        }

        static void AssertAll(Action assert) => assert();

        sealed class BoundaryStageGenerator : IStageGenerator
        {
            public int Calls { get; private set; }

            public StagePlan Generate(
                ulong seed,
                int stageIndex,
                int difficulty)
            {
                Calls++;
                Rng rng = new Rng(seed)
                    .Fork(stageIndex)
                    .Fork(difficulty);
                int length = 200;
                SpawnEvent[] spawns = stageIndex == 1
                    ? Array.Empty<SpawnEvent>()
                    : new[]
                    {
                        new SpawnEvent(
                            8,
                            "harmless",
                            300 + rng.NextInt(0, 40),
                            500),
                        new SpawnEvent(
                            51,
                            "harmless",
                            400 + rng.NextInt(0, 40),
                            -500)
                    };
                var segment = new StageSegment(
                    $"stage_{stageIndex}_{rng.NextInt(0, 100000)}",
                    length,
                    spawns,
                    1,
                    1,
                    new[] { 1 });
                return new StagePlan(
                    new[] { segment },
                    "boss",
                    1,
                    1,
                    1);
            }
        }

        sealed class LethalStageGenerator : IStageGenerator
        {
            public StagePlan Generate(
                ulong seed,
                int stageIndex,
                int difficulty)
            {
                var segment = new StageSegment(
                    "lethal",
                    10,
                    new[]
                    {
                        new SpawnEvent(0, "lethal", 0, 0)
                    },
                    1,
                    1,
                    new[] { 1 });
                return new StagePlan(
                    new[] { segment },
                    "boss",
                    1,
                    1,
                    1);
            }
        }
    }
}

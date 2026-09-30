using System;
using System.IO;
using NUnit.Framework;
using Shmup.Core.Content;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;

namespace Shmup.Core.Tests
{
    [TestFixture]
    public sealed class WeaponExpansionTests
    {
        [Test]
        public void MissileFamiliesUseDistinctIntegerTrajectories()
        {
            BulletState straight = FireMissile(
                MissileFamily.Straight,
                100,
                0,
                BattleModifier.None);
            BulletState bomb = FireMissile(
                MissileFamily.SpreadBomb,
                60,
                90,
                BattleModifier.None);
            BulletState lance = FireMissile(
                MissileFamily.PiercingLance,
                160,
                0,
                BattleModifier.None);

            Assert.AreEqual(100, straight.X);
            Assert.AreEqual(0, straight.Y);
            Assert.AreEqual(60, bomb.X);
            Assert.AreEqual(-90, bomb.Y);
            Assert.AreEqual(160, lance.X);
            Assert.AreEqual(0, lance.Y);
        }

        [Test]
        public void HomingModifierSteersTheCurrentlySelectedBombFamily()
        {
            BattleSimConfig plainConfig = Config();
            ConfigureMissile(
                plainConfig,
                MissileFamily.SpreadBomb,
                100,
                100);
            BattleSim plain = CreateSim(
                plainConfig,
                Gauge(missileLevel: 1),
                BattleModifier.None,
                new[] { Enemy("target", 100) },
                new[] { Spawn("target", 600, 600) });
            BattleSim homing = CreateSim(
                plainConfig,
                Gauge(missileLevel: 1),
                BattleModifier.HomingMissile,
                new[] { Enemy("target", 100) },
                new[] { Spawn("target", 600, 600) });

            FireAndAdvance(plain, 1);
            FireAndAdvance(homing, 1);

            BulletState plainMissile =
                FindBullet(plain, BulletKind.Missile);
            BulletState homingMissile =
                FindBullet(homing, BulletKind.Missile);
            Assert.AreEqual(MissileFamily.SpreadBomb, plainConfig.MissileFamily);
            Assert.Greater(
                homingMissile.Y,
                plainMissile.Y,
                "Homing must layer steering on the bomb profile instead of replacing it.");
        }

        [Test]
        public void PiercingLanceHitsThreeAlignedEnemiesOnlyOnceEach()
        {
            BattleSimConfig config = Config();
            ConfigureMissile(
                config,
                MissileFamily.PiercingLance,
                100,
                0);
            config.MissilePierceEnemyCount = 2;
            config.MissileBaseDamage = 1;
            BattleSim sim = CreateSim(
                config,
                Gauge(missileLevel: 1),
                BattleModifier.None,
                new[]
                {
                    Enemy("a", 1),
                    Enemy("b", 1),
                    Enemy("c", 1)
                },
                new[]
                {
                    Spawn("a", 100, 0),
                    Spawn("b", 200, 0),
                    Spawn("c", 300, 0)
                });

            FireAndAdvance(sim, 4);

            Assert.AreEqual(0, sim.Enemies.Count);
            Assert.AreEqual(3L, sim.Statistics.Kills);
            Assert.AreEqual(0, sim.Bullets.Count);
        }

        [Test]
        public void SpreadBombExplosionKillsNeverSeedKillExplosion()
        {
            BattleSimConfig config = Config();
            ConfigureMissile(
                config,
                MissileFamily.SpreadBomb,
                100,
                0);
            config.MissileBaseDamage = 0;
            config.MissileExplosionDamage = 1;
            config.MissileExplosionRadiusSubUnits = 100;
            config.MissileExplosionMaxTargets = 5;
            config.KillExplosionDamage = 99;
            config.KillExplosionRadiusSubUnits = 1000;
            BattleSim sim = CreateSim(
                config,
                Gauge(missileLevel: 1),
                BattleModifier.KillExplosion,
                new[]
                {
                    Enemy("impact", 100),
                    Enemy("splash_kill", 1),
                    Enemy("chain_canary", 50)
                },
                new[]
                {
                    Spawn("impact", 100, 0),
                    Spawn("splash_kill", 110, 0),
                    Spawn("chain_canary", 150, 0)
                });

            var fire = new InputCommand(0, 0, true);
            sim.Step(in fire);
            InputCommand none = InputCommand.None;
            sim.Step(in none);

            Assert.AreEqual(
                1,
                CountEvents(sim, SimEventType.MissileExploded));
            Assert.AreEqual(
                0,
                CountEvents(
                    sim,
                    SimEventType.KillExplosionTriggered),
                "AoE final hits must not seed kill_explosion.");
            Assert.AreEqual(2, sim.Enemies.Count);
            Assert.AreEqual("impact", sim.Enemies[0].DefinitionId);
            Assert.Greater(sim.Enemies[0].Hp, 0);
            Assert.AreEqual("chain_canary", sim.Enemies[1].DefinitionId);
            Assert.Greater(sim.Enemies[1].Hp, 0);
        }

        [Test]
        public void OptionFormationsProduceDeterministicPositions()
        {
            BattleSimConfig fixedConfig = Config();
            fixedConfig.OptionFormation = OptionFormation.Fixed;
            fixedConfig.OptionFixedOffsetXs =
                new[] { 192, 192, 192, 192, 192, 192 };
            fixedConfig.OptionFixedOffsetYs =
                new[] { 384, -384, 704, -704, 1024, -1024 };
            BattleSim fixedSim = CreateSim(
                fixedConfig,
                Gauge(optionLevel: PowerUpGauge.MaximumOptionCount),
                BattleModifier.None,
                Array.Empty<EnemyDefinition>(),
                Array.Empty<SpawnEvent>());
            Assert.AreEqual(192, fixedSim.Options[0].X);
            Assert.AreEqual(384, fixedSim.Options[0].Y);
            Assert.AreEqual(192, fixedSim.Options[1].X);
            Assert.AreEqual(-384, fixedSim.Options[1].Y);
            Assert.AreEqual(192, fixedSim.Options[4].X);
            Assert.AreEqual(1024, fixedSim.Options[4].Y);
            Assert.AreEqual(192, fixedSim.Options[5].X);
            Assert.AreEqual(-1024, fixedSim.Options[5].Y);

            BattleSimConfig orbitConfig = Config();
            orbitConfig.OptionFormation = OptionFormation.Orbit;
            orbitConfig.OptionOrbitRadiusSubUnits = 448;
            orbitConfig.OptionOrbitAngularLutSlotsNumerator = 1;
            orbitConfig.OptionOrbitAngularLutSlotsDenominator = 2;
            BattleSim first = CreateSim(
                orbitConfig,
                Gauge(optionLevel: PowerUpGauge.MaximumOptionCount),
                BattleModifier.None,
                Array.Empty<EnemyDefinition>(),
                Array.Empty<SpawnEvent>());
            BattleSim second = CreateSim(
                orbitConfig,
                Gauge(optionLevel: PowerUpGauge.MaximumOptionCount),
                BattleModifier.None,
                Array.Empty<EnemyDefinition>(),
                Array.Empty<SpawnEvent>());
            Assert.AreEqual(448, first.Options[0].X);
            Assert.AreEqual(-448, first.Options[3].X);
            InputCommand none = InputCommand.None;
            for (int tick = 0; tick < 130; tick++)
            {
                first.Step(in none);
                second.Step(in none);
                AssertOptionPositionsEqual(first, second);
            }
        }

        [Test]
        public void OrbitFormationStepAllocatesNoManagedMemory()
        {
            BattleSimConfig config = Config();
            config.OptionFormation = OptionFormation.Orbit;
            BattleSim warmup = CreateSim(
                config,
                Gauge(optionLevel: 4),
                BattleModifier.None,
                Array.Empty<EnemyDefinition>(),
                Array.Empty<SpawnEvent>());
            InputCommand none = InputCommand.None;
            warmup.Step(in none);

            BattleSim measured = CreateSim(
                config,
                Gauge(optionLevel: 4),
                BattleModifier.None,
                Array.Empty<EnemyDefinition>(),
                Array.Empty<SpawnEvent>());
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 100; tick++)
                measured.Step(in none);
            long allocated =
                GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void SpreadBombExplosionScanAllocatesNoManagedMemory()
        {
            BattleSim warmup = CreateExplosionAllocationSim();
            FireAndAdvance(warmup, 1);

            BattleSim measured = CreateExplosionAllocationSim();
            var fire = new InputCommand(0, 0, true);
            InputCommand none = InputCommand.None;
            long before = GC.GetAllocatedBytesForCurrentThread();
            measured.Step(in fire);
            measured.Step(in none);
            long allocated =
                GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void RewardCandidatesExcludeEquippedFamilyAndFormation()
        {
            RunManager run = CreateRewardRun();
            AdvanceToFirstBossReward(run);

            Assert.AreEqual(3, run.RewardOptions.Count);
            for (int i = 0; i < run.RewardOptions.Count; i++)
            {
                RewardOption option = run.RewardOptions[i];
                Assert.IsFalse(
                    option.Type == RewardType.MissileFamily
                    && option.MissileFamily
                        == MissileFamily.Straight);
                Assert.IsFalse(
                    option.Type == RewardType.OptionFormation
                    && option.OptionFormation
                        == OptionFormation.Trail);
            }
        }

        [Test]
        public void SuspendAndRecordingPreserveLoadoutAndLegacyDefaults()
        {
            RunManager source = CreateRewardRun();
            RunSuspendData selected = source.ExportSuspendData();
            selected.missileFamily =
                (int)MissileFamily.SpreadBomb;
            selected.optionFormation =
                (int)OptionFormation.Orbit;
            Shmup.Core.SaveDataIntegrity.Seal(selected);
            RunManager resumed = ResumeRewardRun(selected);

            Assert.AreEqual(
                MissileFamily.SpreadBomb,
                resumed.CurrentMissileFamily);
            Assert.AreEqual(
                OptionFormation.Orbit,
                resumed.CurrentOptionFormation);

            var recorder = new InputRecorder(resumed);
            InputCommand none = InputCommand.None;
            recorder.Record(in none);
            InputRecordingData recording = recorder.Export();
            var playback = new InputPlayback(recording);
            Assert.AreEqual(
                MissileFamily.SpreadBomb,
                playback.MissileFamily);
            Assert.AreEqual(
                OptionFormation.Orbit,
                playback.OptionFormation);

            RunSuspendData legacySuspend =
                source.ExportSuspendData();
            legacySuspend.schemaVersion = 5;
            legacySuspend.checksum = null;
            RunSuspendData migratedSuspend =
                Shmup.Core.SaveDataIntegrity.MigrateAndValidate(
                    legacySuspend);
            Assert.AreEqual(
                (int)MissileFamily.Straight,
                migratedSuspend.missileFamily);
            Assert.AreEqual(
                (int)OptionFormation.Trail,
                migratedSuspend.optionFormation);

            recording.schemaVersion = 6;
            recording.checksum = null;
            InputRecordingData migratedRecording =
                Shmup.Core.SaveDataIntegrity.MigrateAndValidate(
                    recording);
            Assert.AreEqual(
                (int)MissileFamily.Straight,
                migratedRecording.missileFamily);
            Assert.AreEqual(
                (int)OptionFormation.Trail,
                migratedRecording.optionFormation);
        }

        [Test]
        public void ShipOptionFormationSeedsRunSuspendAndReplay()
        {
            var ship = new ShipDefinition(
                "orbit_ship",
                "Orbit Ship",
                1,
                1,
                new int[PowerUpGauge.SlotCount],
                0,
                WeaponType.Vulcan,
                null,
                null,
                null,
                MissileFamily.Straight,
                OptionFormation.Orbit);
            RunManager run = CreateRewardRun(ship);

            Assert.AreEqual(
                OptionFormation.Orbit,
                run.CurrentOptionFormation);
            RunSuspendData suspend = run.ExportSuspendData();
            Assert.AreEqual(
                (int)OptionFormation.Orbit,
                suspend.optionFormation);
            RunManager resumed = RunManager.ResumeFromSuspendData(
                suspend,
                new RewardStageGenerator(),
                Config(),
                ExpandedContent(),
                Gauge(),
                SwitchRewards(),
                ship);
            Assert.AreEqual(
                OptionFormation.Orbit,
                resumed.CurrentOptionFormation);

            var recorder = new InputRecorder(resumed);
            InputCommand none = InputCommand.None;
            recorder.Record(in none);
            var playback = new InputPlayback(recorder.Export());
            Assert.AreEqual(
                OptionFormation.Orbit,
                playback.OptionFormation);
        }

        [Test]
        public void WeaponsV3AndSwitchRewardsParseIntoRuntimeCatalogs()
        {
            string root = TestKit.FindRepositoryRoot();
            string gameData = Path.Combine(root, "GameData");
            GameDataSet data = GameDataParser.Parse(
                File.ReadAllText(
                    Path.Combine(gameData, "enemies.json")),
                WeaponsV3Json,
                File.ReadAllText(
                    Path.Combine(gameData, "waves.json")),
                RewardsWithSwitchesJson);

            Assert.AreEqual(
                3,
                data.BattleContent.MissileFamilies.Count);
            Assert.AreEqual(
                3,
                data.BattleContent.OptionFormations.Count);
            MissileFamilyDefinition bomb =
                data.BattleContent.FindMissileFamily(
                    MissileFamily.SpreadBomb);
            Assert.AreEqual(12, bomb.BaseDamage);
            Assert.AreEqual(16, bomb.ExplosionDamage);
            Assert.AreEqual(448, bomb.ExplosionRadiusSubUnits);
            Assert.AreEqual(
                RewardType.MissileFamily,
                data.Rewards.All[0].Type);
            Assert.AreEqual(
                MissileFamily.SpreadBomb,
                data.Rewards.All[0].MissileFamily);
            Assert.AreEqual(
                OptionFormation.Orbit,
                data.Rewards.All[2].OptionFormation);
        }

        [Test]
        public void DoubleFiresForwardAndUpwardInsteadOfSymmetricPair()
        {
            var definition = new PrimaryWeaponFamilyDefinition(
                PrimaryWeaponFamily.Double,
                "Double",
                "Forward plus upward.",
                WeaponType.Spread,
                6,
                10,
                6,
                3,
                1,
                100,
                1,
                0,
                0,
                0,
                2,
                16,
                new[] { 0, 8 });
            BattleSimConfig config = Config();
            config.PlayerWeaponType = WeaponType.Spread;
            config.PlayerWeaponFamily =
                PrimaryWeaponFamily.Double;
            config.SpreadWays = definition.SpreadWays;
            config.SpreadStepLutSlots =
                definition.SpreadStepLutSlots;
            config.MainShotAngleLutSlots =
                new[]
                {
                    definition.ShotAngleLutSlots[0],
                    definition.ShotAngleLutSlots[1]
                };
            BattleSim sim = CreateSim(
                config,
                Gauge(),
                BattleModifier.None,
                Array.Empty<EnemyDefinition>(),
                Array.Empty<SpawnEvent>());

            FireAndAdvance(sim, 1);

            Assert.AreEqual(2, sim.Bullets.Count);
            Assert.AreEqual(0, sim.Bullets[0].Y);
            Assert.Greater(sim.Bullets[0].X, 0);
            Assert.Greater(sim.Bullets[1].Y, 0);
            Assert.Greater(sim.Bullets[1].X, 0);
        }

        [Test]
        public void DownwardDropTravelsForwardBeforeFalling()
        {
            BattleSimConfig config = Config();
            ConfigureMissile(
                config,
                MissileFamily.DownwardDrop,
                100,
                40);
            config.MissileDropDelayTicks = 2;
            BattleSim sim = CreateSim(
                config,
                Gauge(missileLevel: 1),
                BattleModifier.None,
                Array.Empty<EnemyDefinition>(),
                Array.Empty<SpawnEvent>());

            FireAndAdvance(sim, 1);
            BulletState first = FindBullet(
                sim,
                BulletKind.Missile);
            Assert.AreEqual(100, first.X);
            Assert.AreEqual(0, first.Y);
            Assert.AreEqual(1, first.AgeTicks);

            InputCommand none = InputCommand.None;
            sim.Step(in none);
            BulletState second = FindBullet(
                sim,
                BulletKind.Missile);
            Assert.AreEqual(200, second.X);
            Assert.AreEqual(0, second.Y);
            Assert.AreEqual(2, second.AgeTicks);

            sim.Step(in none);
            BulletState falling = FindBullet(
                sim,
                BulletKind.Missile);
            Assert.AreEqual(300, falling.X);
            Assert.AreEqual(-40, falling.Y);
        }

        [Test]
        public void HomingFamilySteersWithoutHomingModifier()
        {
            BattleSimConfig config = Config();
            ConfigureMissile(
                config,
                MissileFamily.Homing,
                100,
                0);
            config.HomingMissileTurnLutSlotsPerTick = 4;
            BattleSim sim = CreateSim(
                config,
                Gauge(missileLevel: 1),
                BattleModifier.None,
                new[] { Enemy("target", 100) },
                new[] { Spawn("target", 600, 600) });
            BattleSim redundantModifier = CreateSim(
                config,
                Gauge(missileLevel: 1),
                BattleModifier.HomingMissile,
                new[] { Enemy("target", 100) },
                new[] { Spawn("target", 600, 600) });

            FireAndAdvance(sim, 1);
            FireAndAdvance(redundantModifier, 1);

            BulletState missile = FindBullet(
                sim,
                BulletKind.Missile);
            BulletState redundant = FindBullet(
                redundantModifier,
                BulletKind.Missile);
            Assert.Greater(missile.Y, 0);
            Assert.Greater(missile.X, 0);
            Assert.AreEqual(missile.X, redundant.X);
            Assert.AreEqual(missile.Y, redundant.Y);
        }

        [Test]
        public void MissileDamageGrowthPercentScalesLevelDamage()
        {
            BattleSim levelOne = DamageScalingSim(1);
            BattleSim levelThree = DamageScalingSim(3);

            FireAndAdvance(levelOne, 1);
            FireAndAdvance(levelThree, 1);

            Assert.AreEqual(90, levelOne.Enemies[0].Hp);
            Assert.AreEqual(85, levelThree.Enemies[0].Hp);
        }

        [Test]
        public void HomingTrajectoryProducesSameSeedHash()
        {
            BattleSim first = HomingAuditSim();
            BattleSim second = HomingAuditSim();
            var firstHasher = new DeterminismAuditHasher();
            var secondHasher = new DeterminismAuditHasher();
            var fire = new InputCommand(0, 0, true);
            first.Step(in fire);
            second.Step(in fire);
            InputCommand none = InputCommand.None;
            for (int i = 0; i < 8; i++)
            {
                first.Step(in none);
                second.Step(in none);
            }
            firstHasher.FoldBattleState(first);
            secondHasher.FoldBattleState(second);

            Assert.AreEqual(
                firstHasher.HexHash,
                secondHasher.HexHash);
        }

        [Test]
        public void IncompatibleReplayAndSuspendVersionsAreRejected()
        {
            Assert.AreEqual(
                26,
                InputRecordingData.CurrentSchemaVersion);
            Assert.AreEqual(
                29,
                RunSuspendData.CurrentSchemaVersion);
            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(
                    new InputRecordingData
                    {
                        schemaVersion = 24
                    }));
            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(
                    new RunSuspendData
                    {
                        schemaVersion = 27
                    }));
            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(
                    new InputRecordingData
                    {
                        schemaVersion = 19
                    }));
            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(
                    new RunSuspendData
                    {
                        schemaVersion = 20
                    }));
            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(
                    new InputRecordingData
                    {
                        schemaVersion = 21
                    }));
            Assert.Throws<ArgumentException>(
                () => SaveDataIntegrity.MigrateAndValidate(
                    new RunSuspendData
                    {
                        schemaVersion = 23
                    }));
        }

        [Test]
        public void WeaponsV7AndShipsV3ParseReq080Profiles()
        {
            string root = TestKit.FindRepositoryRoot();
            string gameData = Path.Combine(root, "GameData");
            GameDataSet data = GameDataParser.Parse(
                File.ReadAllText(
                    Path.Combine(gameData, "enemies.json")),
                WeaponsV7Json(),
                File.ReadAllText(
                    Path.Combine(gameData, "waves.json")),
                null,
                ShipsV3Json);

            PrimaryWeaponFamilyDefinition doubleShot =
                data.BattleContent.FindPrimaryWeaponFamily(
                    PrimaryWeaponFamily.Double);
            Assert.AreEqual(0, doubleShot.ShotAngleLutSlots[0]);
            Assert.AreEqual(8, doubleShot.ShotAngleLutSlots[1]);
            Assert.AreEqual(
                5,
                data.BattleContent.MissileFamilies.Count);
            MissileFamilyDefinition downward =
                data.BattleContent.FindMissileFamily(
                    MissileFamily.DownwardDrop);
            Assert.AreEqual(3, downward.DropDelayTicks);
            Assert.AreEqual(
                25,
                downward.DamageGrowthPercentPerLevel);
            MissileFamilyDefinition homing =
                data.BattleContent.FindMissileFamily(
                    MissileFamily.Homing);
            Assert.AreEqual(2, homing.HomingTurnLutSlotsPerTick);
            Assert.AreEqual(
                MissileFamily.DownwardDrop,
                data.FindShip("starter").StartingMissileFamily);
            Assert.AreEqual(
                MissileFamily.Straight,
                data.FindShip("interceptor").StartingMissileFamily);
            Assert.AreEqual(
                MissileFamily.Homing,
                data.FindShip("bulwark").StartingMissileFamily);
            Assert.AreEqual(
                100,
                data.CreateBattleSimConfig().OptionMissileDamagePercent);
        }

        [Test]
        public void OptionalOptionMissileDamagePercentFlowsIntoBattleConfig()
        {
            string root = TestKit.FindRepositoryRoot();
            string gameData = Path.Combine(root, "GameData");
            string weapons = WeaponsV7Json().Replace(
                @"""defaultOptionFormation"": ""trail""",
                @"""defaultOptionFormation"": ""trail"",
  ""optionMissileDamagePercent"": 42");
            GameDataSet data = GameDataParser.Parse(
                File.ReadAllText(
                    Path.Combine(gameData, "enemies.json")),
                weapons,
                File.ReadAllText(
                    Path.Combine(gameData, "waves.json")),
                null,
                ShipsV3Json);

            Assert.AreEqual(
                42,
                data.CreateBattleSimConfig().OptionMissileDamagePercent);
        }

        [Test]
        public void HomingShipExcludesRedundantHomingRewards()
        {
            RunManager run = CreateHomingShipRewardRun();
            Assert.AreEqual(
                MissileFamily.Homing,
                run.CurrentMissileFamily);

            AdvanceToFirstBossReward(run);

            Assert.AreEqual(
                RunManager.RewardOptionCount,
                run.RewardOptions.Count);
            for (int i = 0; i < run.RewardOptions.Count; i++)
            {
                RewardOption option = run.RewardOptions[i];
                Assert.IsFalse(
                    option.Type == RewardType.MissileFamily
                    && option.MissileFamily
                        == MissileFamily.Homing);
                Assert.IsFalse(
                    option.Type == RewardType.Modifier
                    && option.ModifierId
                        == BattleModifier.HomingMissile);
            }
        }

        [Test]
        public void OptionFormationRewardRepositionsAndKeepsWeaponsAcrossRoomTransition()
        {
            RunManager run = CreateFormationRewardRun(3);
            AdvanceToReward(run);

            Assert.AreEqual(2, run.RoomIndex);
            Assert.IsFalse(run.IsBiomeBoss);
            Assert.AreEqual(RewardSelectionKind.MidStage, run.RewardSelectionKind);
            Assert.AreEqual(OptionFormation.Fixed, run.RewardOptions[0].OptionFormation);
            Assert.IsTrue(run.ChooseReward(0));

            Assert.AreEqual(1, run.BiomeIndex);
            Assert.AreEqual(3, run.RoomIndex);
            AssertFormationWeapons(run);
        }

        [Test]
        public void OptionFormationRewardPersistsAndKeepsWeaponsAcrossBiomeTransition()
        {
            RunManager run = CreateFormationRewardRun(1);
            AdvanceToReward(run);

            Assert.AreEqual(1, run.BiomeIndex);
            Assert.IsTrue(run.IsBiomeBoss);
            Assert.AreEqual(RewardSelectionKind.Main, run.RewardSelectionKind);
            Assert.AreEqual(OptionFormation.Fixed, run.RewardOptions[0].OptionFormation);
            Assert.IsTrue(run.ChooseReward(0));
            Assert.AreEqual(RunState.AwaitingContract, run.State);
            Assert.IsTrue(run.ChooseContract(0));

            Assert.AreEqual(2, run.BiomeIndex);
            Assert.AreEqual(1, run.RoomIndex);
            AssertFormationWeapons(run);
        }

        [Test]
        public void WeaponsV4PrimaryMetadataAndRewardV2ParseEndToEnd()
        {
            string root = TestKit.FindRepositoryRoot();
            string gameData = Path.Combine(root, "GameData");
            string weapons = WeaponsV3Json.Replace(
                @"""schemaVersion"": 3",
                @"""schemaVersion"": 4");
            int closingBrace = weapons.LastIndexOf('}');
            weapons = weapons.Insert(
                closingBrace,
                @",
  ""primaryWeaponFamilies"": [
    { ""id"": ""double"", ""displayName"": ""Double"",
      ""description"": ""Two-way coverage shot."", ""weaponType"": ""spread"",
      ""baseDamage"": 6, ""fireIntervalTicks"": 10,
      ""minimumFireIntervalTicks"": 6, ""rapidFireStartLevel"": 3,
      ""fireIntervalReductionPerLevel"": 1, ""projectileSpeed"": 20,
      ""projectileHalfWidth"": 0.375, ""projectileHalfHeight"": 0.140625,
      ""pierceEnemyCount"": 0, ""spreadWays"": 2,
      ""spreadStepLutSlots"": 2 },
    { ""id"": ""laser"", ""displayName"": ""Laser"",
      ""description"": ""Pierces three aligned targets."", ""weaponType"": ""laser"",
      ""baseDamage"": 15, ""fireIntervalTicks"": 16,
      ""minimumFireIntervalTicks"": 8, ""rapidFireStartLevel"": 2,
      ""fireIntervalReductionPerLevel"": 2, ""projectileSpeed"": 28,
      ""projectileHalfWidth"": 0.1875, ""projectileHalfHeight"": 0.0703125,
      ""pierceEnemyCount"": 2, ""spreadWays"": 1,
      ""spreadStepLutSlots"": 0 }
  ]
");
            GameDataSet data = GameDataParser.Parse(
                File.ReadAllText(
                    Path.Combine(gameData, "enemies.json")),
                weapons,
                File.ReadAllText(
                    Path.Combine(gameData, "waves.json")),
                PrimaryRewardsV2Json);

            PrimaryWeaponFamilyDefinition laser =
                data.BattleContent.FindPrimaryWeaponFamily(
                    PrimaryWeaponFamily.Laser);
            Assert.AreEqual("Laser", laser.DisplayName);
            Assert.AreEqual(
                "Pierces three aligned targets.",
                laser.Description);
            Assert.AreEqual(2, laser.PierceEnemyCount);
            Assert.AreEqual(
                RewardType.PrimaryWeaponFamily,
                data.Rewards.All[0].Type);
            Assert.AreEqual(
                PrimaryWeaponFamily.Double,
                data.Rewards.All[0].PrimaryWeaponFamily);
            Assert.IsNotNull(
                data.BattleContent.FindPrimaryWeaponFamily(
                    PrimaryWeaponFamily.Vulcan),
                "A minimal v4 Double/Laser table must retain the legacy "
                + "Vulcan profile for existing starting ships.");
            Assert.IsNotNull(
                data.BattleContent.FindPrimaryWeaponFamily(
                    PrimaryWeaponFamily.Spread),
                "A minimal v4 Double/Laser table must retain the legacy "
                + "Spread profile for existing starting ships.");
        }

        [Test]
        public void WeaponsV6PowerUpGaugeOrderCostsAndNamesAreDataDriven()
        {
            string root = TestKit.FindRepositoryRoot();
            string gameData = Path.Combine(root, "GameData");
            GameDataSet data = GameDataParser.Parse(
                File.ReadAllText(
                    Path.Combine(gameData, "enemies.json")),
                WeaponsV6Json(),
                File.ReadAllText(
                    Path.Combine(gameData, "waves.json")));
            PowerUpGauge gauge = data.CreatePowerUpGauge();

            Assert.AreEqual(7, gauge.GaugeSlotCount);
            PowerUpGaugeSlotView speed = gauge.GetGaugeSlotView(0);
            Assert.AreEqual(PowerUpSlot.Speed, speed.Slot);
            Assert.AreEqual("powerUp.speed.full", speed.NameKey);
            Assert.AreEqual(6, speed.MaxLevel);
            Assert.AreEqual(2, speed.RequiredCapsules);
            Assert.AreEqual(
                SimSpace.SubUnitsPerWorldUnit * 5 / 4,
                gauge.GaugeSlots[0].SpeedBonusNumerator);
            Assert.AreEqual(
                SimSpace.TicksPerSecond,
                gauge.GaugeSlots[0].SpeedBonusDenominator);
            Assert.AreEqual(
                PowerUpSlot.Missile,
                gauge.GetGaugeSlotView(1).Slot);
            Assert.AreEqual(
                PowerUpSlot.Double,
                gauge.GetGaugeSlotView(2).Slot);
            Assert.AreEqual(
                PowerUpSlot.Laser,
                gauge.GetGaugeSlotView(3).Slot);
            Assert.AreEqual(
                PowerUpSlot.Triple,
                gauge.GetGaugeSlotView(4).Slot);
            Assert.AreEqual(
                PowerUpSlot.Option,
                gauge.GetGaugeSlotView(5).Slot);
            Assert.AreEqual(
                PowerUpSlot.Shield,
                gauge.GetGaugeSlotView(6).Slot);
        }

        [Test]
        public void WeaponsDataAcceptsSixOptionsAndSixFixedOffsets()
        {
            string root = TestKit.FindRepositoryRoot();
            string gameData = Path.Combine(root, "GameData");
            string weapons = WeaponsV6Json()
                .Replace(
                    @"""maxLevel"": 4, ""effectSoftCapLevel"": 4",
                    @"""maxLevel"": 6, ""effectSoftCapLevel"": 6")
                .Replace(
                    @"""maxLevel"": 4, ""costCurve"":",
                    @"""maxLevel"": 6, ""costCurve"":")
                .Replace(
                    @"{ ""x"": 0.75, ""y"": -2.75 }",
                    @"{ ""x"": 0.75, ""y"": -2.75 },
      { ""x"": 0.75, ""y"": 4.0 },
      { ""x"": 0.75, ""y"": -4.0 }");

            GameDataSet data = GameDataParser.Parse(
                File.ReadAllText(Path.Combine(gameData, "enemies.json")),
                weapons,
                File.ReadAllText(Path.Combine(gameData, "waves.json")));
            OptionFormationDefinition fixedFormation =
                data.BattleContent.FindOptionFormation(OptionFormation.Fixed);

            Assert.AreEqual(
                PowerUpGauge.MaximumOptionCount,
                data.CreatePowerUpGauge().GetMaxLevel(PowerUpSlot.Option));
            Assert.AreEqual(
                PowerUpGauge.MaximumOptionCount,
                fixedFormation.OffsetXs.Count);
            Assert.AreEqual(1024, fixedFormation.OffsetYs[4]);
            Assert.AreEqual(-1024, fixedFormation.OffsetYs[5]);
        }

        static BulletState FireMissile(
            MissileFamily family,
            int speedX,
            int fallSpeedY,
            BattleModifier modifier)
        {
            BattleSimConfig config = Config();
            ConfigureMissile(
                config,
                family,
                speedX,
                fallSpeedY);
            BattleSim sim = CreateSim(
                config,
                Gauge(missileLevel: 1),
                modifier,
                Array.Empty<EnemyDefinition>(),
                Array.Empty<SpawnEvent>());
            FireAndAdvance(sim, 1);
            return FindBullet(sim, BulletKind.Missile);
        }

        static BattleSim DamageScalingSim(int missileLevel)
        {
            BattleSimConfig config = Config();
            ConfigureMissile(
                config,
                MissileFamily.Straight,
                100,
                0);
            config.MainShotBaseDamage = 0;
            config.MissileBaseDamage = 10;
            config.MissileDamageGrowthPercentPerLevel = 25;
            return CreateSim(
                config,
                Gauge(missileLevel: missileLevel),
                BattleModifier.None,
                new[] { Enemy("damage_target", 100) },
                new[] { Spawn("damage_target", 100, 0) });
        }

        static BattleSim HomingAuditSim()
        {
            BattleSimConfig config = Config();
            ConfigureMissile(
                config,
                MissileFamily.Homing,
                100,
                0);
            config.HomingMissileTurnLutSlotsPerTick = 2;
            return CreateSim(
                config,
                Gauge(missileLevel: 1),
                BattleModifier.None,
                new[] { Enemy("audit_target", 100) },
                new[] { Spawn("audit_target", 900, 500) });
        }

        static void ConfigureMissile(
            BattleSimConfig config,
            MissileFamily family,
            int speedX,
            int fallSpeedY)
        {
            config.MissileFamily = family;
            config.MissileSpeedXNumerator = speedX;
            config.MissileSpeedXDenominator = 1;
            config.MissileFallSpeedYNumerator = fallSpeedY;
            config.MissileFallSpeedYDenominator = 1;
            config.MissileFireIntervalTicks = 100;
            config.MissileMinimumFireIntervalTicks = 50;
        }

        static BattleSim CreateSim(
            BattleSimConfig config,
            PowerUpGauge gauge,
            BattleModifier modifiers,
            EnemyDefinition[] enemies,
            SpawnEvent[] spawns)
        {
            var weapon = new WeaponDefinition(
                "shot",
                0,
                100,
                100,
                1,
                0,
                0);
            var content = new BattleContent(
                enemies,
                new[] { weapon },
                weapon.Id);
            var segment = new StageSegment(
                "weapon_expansion",
                1000,
                spawns,
                1,
                1,
                new[] { 1 });
            var plan = new StagePlan(
                new[] { segment },
                "legacy",
                1,
                1,
                1);
            return new BattleSim(
                config,
                new Rng(123UL),
                plan,
                content,
                gauge,
                modifiers);
        }

        static BattleSimConfig Config()
        {
            return new BattleSimConfig
            {
                PlayerSpeedNumerator = 0,
                PlayerSpeedDenominator = 1,
                PlayerBulletSpeedNumerator = 100,
                PlayerBulletSpeedDenominator = 1,
                FireIntervalTicks = 100,
                MaxBullets = 64,
                PlayerMinX = -10000,
                PlayerMaxX = 10000,
                PlayerMinY = -10000,
                PlayerMaxY = 10000,
                BulletDespawnX = 10000,
                EnemyDespawnX = -10000,
                PlayerSpawnX = 0,
                PlayerSpawnY = 0,
                PlayerMaxHp = 100,
                PlayerHalfWidth = 0,
                PlayerHalfHeight = 0,
                CapsuleHalfWidth = 0,
                CapsuleHalfHeight = 0,
                CapsuleNoDropWeight = 1,
                ScrollSpeedNumerator = 0,
                ScrollSpeedDenominator = 1,
                MissileBaseDamage = 1,
                MissileFireIntervalTicks = 100,
                MissileMinimumFireIntervalTicks = 50,
                MissileSpeedXNumerator = 100,
                MissileSpeedXDenominator = 1,
                MissileFallSpeedYNumerator = 0,
                MissileFallSpeedYDenominator = 1,
                MissileHalfWidth = 0,
                MissileHalfHeight = 0,
                OptionFixedOffsetXs =
                    new[] { 192, 192, 192, 192, 192, 192 },
                OptionFixedOffsetYs =
                    new[] { 384, -384, 704, -704, 1024, -1024 },
                EnemyBulletDamage = 0,
                MaxEnemyBullets = 0
            };
        }

        static PowerUpGauge Gauge(
            int missileLevel = 0,
            int optionLevel = 0)
        {
            PowerUpGauge gauge = PowerUpGauge.CreateDefault();
            gauge.ImportLevels(
                new[] { 0, missileLevel, optionLevel, 0 });
            return gauge;
        }

        static string WeaponsV6Json()
        {
            string weapons = WeaponsV3Json
                .Replace(
                    @"""schemaVersion"": 3",
                    @"""schemaVersion"": 6")
                .Replace(
                    @"""maxLevel"": 5 }",
                    @"""maxLevel"": 5, ""effectSoftCapLevel"": 5 }")
                .Replace(
                    @"""maxLevel"": 4 }",
                    @"""maxLevel"": 4, ""effectSoftCapLevel"": 4 }")
                .Replace(
                    @"""maxLevel"": 3 }",
                    @"""maxLevel"": 3, ""effectSoftCapLevel"": 3 }");
            int closingBrace = weapons.LastIndexOf('}');
            return weapons.Insert(
                closingBrace,
                @",
  ""primaryWeaponFamilies"": [
    { ""id"": ""double"", ""displayName"": ""Double"",
      ""description"": ""Two-way coverage shot."", ""weaponType"": ""spread"",
      ""baseDamage"": 6, ""fireIntervalTicks"": 10,
      ""minimumFireIntervalTicks"": 6, ""rapidFireStartLevel"": 3,
      ""fireIntervalReductionPerLevel"": 1, ""projectileSpeed"": 20,
      ""projectileHalfWidth"": 0.375, ""projectileHalfHeight"": 0.140625,
      ""pierceEnemyCount"": 0, ""spreadWays"": 2,
      ""spreadStepLutSlots"": 2 },
    { ""id"": ""laser"", ""displayName"": ""Laser"",
      ""description"": ""Pierces three aligned targets."", ""weaponType"": ""laser"",
      ""baseDamage"": 15, ""fireIntervalTicks"": 16,
      ""minimumFireIntervalTicks"": 8, ""rapidFireStartLevel"": 2,
      ""fireIntervalReductionPerLevel"": 2, ""projectileSpeed"": 28,
      ""projectileHalfWidth"": 0.1875, ""projectileHalfHeight"": 0.0703125,
      ""pierceEnemyCount"": 2, ""spreadWays"": 1,
      ""spreadStepLutSlots"": 0 }
  ],
  ""powerUpCostCurve"": {
    ""baseCost"": 1, ""linearGrowth"": 1, ""quadraticGrowth"": 0
  },
  ""powerUpGauge"": {
    ""slots"": [
      { ""slot"": ""Speed"", ""nameKey"": ""powerUp.speed.full"",
        ""maxLevel"": 6, ""speedBonusPerLevel"": 1.25,
        ""costCurve"": {
          ""baseCost"": 2, ""linearGrowth"": 1, ""quadraticGrowth"": 0
        } },
      { ""slot"": ""Missile"", ""nameKey"": ""powerUp.missile.full"",
        ""maxLevel"": 3, ""costCurve"": {
          ""baseCost"": 1, ""linearGrowth"": 1, ""quadraticGrowth"": 0
        } },
      { ""slot"": ""Double"", ""nameKey"": ""powerUp.double.full"",
        ""maxLevel"": 1, ""costCurve"": {
          ""baseCost"": 1, ""linearGrowth"": 0, ""quadraticGrowth"": 0
        } },
      { ""slot"": ""Laser"", ""nameKey"": ""powerUp.laser.full"",
        ""maxLevel"": 1, ""costCurve"": {
          ""baseCost"": 1, ""linearGrowth"": 0, ""quadraticGrowth"": 0
        } },
      { ""slot"": ""Triple"", ""nameKey"": ""powerUp.triple.full"",
        ""maxLevel"": 1, ""costCurve"": {
          ""baseCost"": 1, ""linearGrowth"": 0, ""quadraticGrowth"": 0
        } },
      { ""slot"": ""Option"", ""nameKey"": ""powerUp.option.full"",
        ""maxLevel"": 4, ""costCurve"": {
          ""baseCost"": 1, ""linearGrowth"": 1, ""quadraticGrowth"": 0
        } },
      { ""slot"": ""Shield"", ""nameKey"": ""powerUp.shield.full"",
        ""maxLevel"": 3, ""costCurve"": {
          ""baseCost"": 1, ""linearGrowth"": 1, ""quadraticGrowth"": 0
        } }
    ]
  }
                ");
        }

        static string WeaponsV7Json()
        {
            string weapons = WeaponsV6Json()
                .Replace(
                    @"""schemaVersion"": 6",
                    @"""schemaVersion"": 7")
                .Replace(
                    @"""explosionMaxTargets"": 0 }",
                    @"""explosionMaxTargets"": 0,
      ""damageGrowthPercentPerLevel"": 25, ""dropDelayTicks"": 0,
      ""homingTurnLutSlotsPerTick"": 1 }")
                .Replace(
                    @"""explosionMaxTargets"": 5 }",
                    @"""explosionMaxTargets"": 5,
      ""damageGrowthPercentPerLevel"": 30, ""dropDelayTicks"": 0,
      ""homingTurnLutSlotsPerTick"": 1 }")
                .Replace(
                    @"""spreadStepLutSlots"": 2 }",
                    @"""spreadStepLutSlots"": 16,
      ""shotAngleLutSlots"": [0, 8] }")
                .Replace(
                    @"""spreadStepLutSlots"": 0 }",
                    @"""spreadStepLutSlots"": 0,
      ""shotAngleLutSlots"": [0] }");
            string marker = "  ],"
                + Environment.NewLine
                + "  \"defaultMissileFamily\"";
            int arrayEnd = weapons.IndexOf(
                marker,
                StringComparison.Ordinal);
            Assert.GreaterOrEqual(arrayEnd, 0);
            int lastObjectEnd = weapons.LastIndexOf(
                '}',
                arrayEnd);
            Assert.GreaterOrEqual(lastObjectEnd, 0);
            return weapons.Insert(
                lastObjectEnd + 1,
                @",
    { ""id"": ""downward_drop"", ""baseDamage"": 18,
      ""fireIntervalTicks"": 36, ""minimumFireIntervalTicks"": 20,
      ""fireIntervalReductionPerLevel"": 4, ""projectileSpeed"": 10,
      ""fallSpeedY"": 8, ""pierceEnemyCount"": 0,
      ""explosionDamage"": 0, ""explosionRadius"": 0,
      ""explosionMaxTargets"": 0,
      ""damageGrowthPercentPerLevel"": 25, ""dropDelayTicks"": 3,
      ""homingTurnLutSlotsPerTick"": 1 },
    { ""id"": ""homing"", ""baseDamage"": 16,
      ""fireIntervalTicks"": 42, ""minimumFireIntervalTicks"": 24,
      ""fireIntervalReductionPerLevel"": 4, ""projectileSpeed"": 9,
      ""fallSpeedY"": 0, ""pierceEnemyCount"": 0,
      ""explosionDamage"": 0, ""explosionRadius"": 0,
      ""explosionMaxTargets"": 0,
      ""damageGrowthPercentPerLevel"": 35, ""dropDelayTicks"": 0,
      ""homingTurnLutSlotsPerTick"": 2 }");
        }

        static EnemyDefinition Enemy(string id, int hp)
        {
            return new EnemyDefinition(
                id,
                id,
                hp,
                0,
                1,
                EnemyMovePattern.Static,
                0,
                1,
                0,
                0,
                0,
                0,
                0,
                1,
                64);
        }

        static SpawnEvent Spawn(string id, int x, int y)
        {
            return new SpawnEvent(0, id, x, y);
        }

        static void FireAndAdvance(
            BattleSim sim,
            int followingSteps)
        {
            var fire = new InputCommand(0, 0, true);
            sim.Step(in fire);
            InputCommand none = InputCommand.None;
            for (int i = 0; i < followingSteps; i++)
                sim.Step(in none);
        }

        static BulletState FindBullet(
            BattleSim sim,
            BulletKind kind)
        {
            for (int i = 0; i < sim.Bullets.Count; i++)
                if (sim.Bullets[i].Kind == kind)
                    return sim.Bullets[i];
            Assert.Fail("Expected bullet was not found.");
            return default;
        }

        static int CountEvents(
            BattleSim sim,
            SimEventType type)
        {
            int count = 0;
            ReadOnlySpan<SimEvent> events = sim.EventsThisTick;
            for (int i = 0; i < events.Length; i++)
                if (events[i].Type == type)
                    count++;
            return count;
        }

        static void AssertOptionPositionsEqual(
            BattleSim first,
            BattleSim second)
        {
            Assert.AreEqual(first.Options.Count, second.Options.Count);
            for (int i = 0; i < first.Options.Count; i++)
            {
                Assert.AreEqual(
                    first.Options[i].X,
                    second.Options[i].X);
                Assert.AreEqual(
                    first.Options[i].Y,
                    second.Options[i].Y);
            }
        }

        static RunManager CreateRewardRun()
        {
            return CreateRewardRun(ShipDefinition.CreateDefault());
        }

        static RunManager CreateRewardRun(ShipDefinition ship)
        {
            BattleContent content = ExpandedContent();
            return new RunManager(
                123UL,
                new RewardStageGenerator(),
                Config(),
                content,
                Gauge(),
                new MetaProgression(1, 1),
                StageDifficultyCurve.CreateDefault(),
                SwitchRewards(),
                ship,
                1,
                1,
                new RunProgressionConfig(2, 1));
        }

        static RunManager CreateFormationRewardRun(int roomsPerBiome)
        {
            return new RunManager(
                0x100UL,
                new FormationRewardStageGenerator(),
                Config(),
                ExpandedContent(),
                Gauge(
                    missileLevel: 1,
                    optionLevel: PowerUpGauge.MaximumOptionCount),
                new MetaProgression(1, 1),
                StageDifficultyCurve.CreateDefault(),
                FormationRewards(),
                ShipDefinition.CreateDefault(),
                1,
                1,
                new RunProgressionConfig(2, roomsPerBiome));
        }

        static RewardCatalog FormationRewards()
        {
            return new RewardCatalog(
                RunManager.RewardOptionCount,
                new[]
                {
                    FormationReward("mid_fixed_a", RewardPool.Mid),
                    FormationReward("mid_fixed_b", RewardPool.Mid),
                    FormationReward("main_fixed_a", RewardPool.Main),
                    FormationReward("main_fixed_b", RewardPool.Main),
                    FormationReward("main_fixed_c", RewardPool.Main)
                });
        }

        static RewardDefinition FormationReward(string id, RewardPool pool)
        {
            return new RewardDefinition(
                id,
                RewardType.OptionFormation,
                PowerUpSlot.Option,
                1,
                1,
                1,
                99,
                optionFormation: OptionFormation.Fixed,
                pool: pool);
        }

        static void AdvanceToReward(RunManager run)
        {
            var fire = new InputCommand(0, 0, true);
            for (int tick = 0;
                tick < 5000 && run.State != RunState.AwaitingReward;
                tick++)
                run.Step(in fire);
            Assert.AreEqual(RunState.AwaitingReward, run.State);
        }

        static void AssertFormationWeapons(RunManager run)
        {
            Assert.AreEqual(OptionFormation.Fixed, run.CurrentOptionFormation);
            Assert.AreEqual(RunState.Playing, run.State);
            var battle = (BattleSim)run.Battle;
            Assert.AreEqual(PowerUpGauge.MaximumOptionCount, battle.Options.Count);
            int[] expectedXs = { 192, 192, 192, 192, 192, 192 };
            int[] expectedYs = { 384, -384, 704, -704, 1024, -1024 };
            for (int i = 0; i < battle.Options.Count; i++)
            {
                Assert.AreEqual(battle.PlayerX + expectedXs[i], battle.Options[i].X);
                Assert.AreEqual(battle.PlayerY + expectedYs[i], battle.Options[i].Y);
            }

            var fire = new InputCommand(0, 0, true);
            run.Step(in fire);

            Assert.AreSame(battle, run.Battle);
            Assert.AreEqual(
                PowerUpGauge.MaximumOptionCount + 1,
                CountBullets(battle, BulletKind.MainShot));
            Assert.AreEqual(
                PowerUpGauge.MaximumOptionCount + 1,
                CountBullets(battle, BulletKind.Missile));
        }

        static int CountBullets(BattleSim sim, BulletKind kind)
        {
            int count = 0;
            for (int i = 0; i < sim.Bullets.Count; i++)
                if (sim.Bullets[i].Kind == kind)
                    count++;
            return count;
        }

        static RunManager CreateHomingShipRewardRun()
        {
            var ship = new ShipDefinition(
                "homing_ship",
                "Homing Ship",
                1,
                1,
                new int[PowerUpGauge.SlotCount],
                0,
                WeaponType.Vulcan,
                null,
                null,
                null,
                MissileFamily.Homing);
            return new RunManager(
                456UL,
                new RewardStageGenerator(),
                Config(),
                ExpandedContent(),
                Gauge(),
                new MetaProgression(1, 1),
                StageDifficultyCurve.CreateDefault(),
                HomingShipRewards(),
                ship,
                1,
                1,
                new RunProgressionConfig(2, 1));
        }

        static RewardCatalog HomingShipRewards()
        {
            return new RewardCatalog(
                RunManager.RewardOptionCount,
                new[]
                {
                    SwitchMissile(
                        "homing",
                        MissileFamily.Homing),
                    SwitchMissile(
                        "straight",
                        MissileFamily.Straight),
                    new RewardDefinition(
                        "homing_modifier",
                        RewardType.Modifier,
                        PowerUpSlot.Missile,
                        1,
                        1,
                        1,
                        99,
                        1,
                        BattleModifier.HomingMissile),
                    new RewardDefinition(
                        "capsules",
                        RewardType.Capsules,
                        PowerUpSlot.MainShot,
                        1,
                        1,
                        1,
                        99),
                    new RewardDefinition(
                        "bomb",
                        RewardType.BombStock,
                        PowerUpSlot.MainShot,
                        1,
                        1,
                        1,
                        99)
                });
        }

        static BattleSim CreateExplosionAllocationSim()
        {
            const int count = 16;
            var enemies = new EnemyDefinition[count];
            var spawns = new SpawnEvent[count];
            for (int i = 0; i < count; i++)
            {
                string id = "bomb_target_" + i;
                enemies[i] = Enemy(id, 2);
                spawns[i] = Spawn(id, 100, i);
            }
            BattleSimConfig config = Config();
            ConfigureMissile(
                config,
                MissileFamily.SpreadBomb,
                100,
                0);
            config.MissileBaseDamage = 0;
            config.MissileExplosionDamage = 1;
            config.MissileExplosionRadiusSubUnits = 100;
            config.MissileExplosionMaxTargets = count;
            return CreateSim(
                config,
                Gauge(missileLevel: 1),
                BattleModifier.KillExplosion,
                enemies,
                spawns);
        }

        static void AdvanceToFirstBossReward(RunManager run)
        {
            InputCommand none = InputCommand.None;
            for (int i = 0;
                i < 5000
                    && run.State != RunState.AwaitingReward;
                i++)
            {
                var fire = new InputCommand(0, 0, true);
                InputCommand input = run.IsBiomeBoss
                    ? fire
                    : none;
                run.Step(in input);
            }
            Assert.AreEqual(RunState.AwaitingReward, run.State);
        }

        static RunManager ResumeRewardRun(RunSuspendData data)
        {
            return RunManager.ResumeFromSuspendData(
                data,
                new RewardStageGenerator(),
                Config(),
                ExpandedContent(),
                Gauge(),
                SwitchRewards(),
                ShipDefinition.CreateDefault());
        }

        static RewardCatalog SwitchRewards()
        {
            return new RewardCatalog(
                RunManager.RewardOptionCount,
                new[]
                {
                    SwitchMissile(
                        "straight",
                        MissileFamily.Straight),
                    SwitchMissile(
                        "bomb",
                        MissileFamily.SpreadBomb),
                    SwitchMissile(
                        "lance",
                        MissileFamily.PiercingLance),
                    SwitchOption(
                        "trail",
                        OptionFormation.Trail),
                    SwitchOption(
                        "fixed",
                        OptionFormation.Fixed)
                });
        }

        static RewardDefinition SwitchMissile(
            string id,
            MissileFamily family)
        {
            return new RewardDefinition(
                id,
                RewardType.MissileFamily,
                PowerUpSlot.Missile,
                1,
                1,
                1,
                99,
                null,
                BattleModifier.None,
                family);
        }

        static RewardDefinition SwitchOption(
            string id,
            OptionFormation formation)
        {
            return new RewardDefinition(
                id,
                RewardType.OptionFormation,
                PowerUpSlot.Option,
                1,
                1,
                1,
                99,
                null,
                BattleModifier.None,
                MissileFamily.Straight,
                formation);
        }

        static BattleContent ExpandedContent()
        {
            int u = SimSpace.SubUnitsPerWorldUnit;
            var main = new WeaponDefinition(
                "shot",
                1,
                1,
                100,
                1,
                0,
                0);
            var missileFamilies = new[]
            {
                Missile(
                    MissileFamily.Straight,
                    20,
                    30,
                    10 * u,
                    0,
                    0,
                    0,
                    0),
                Missile(
                    MissileFamily.SpreadBomb,
                    12,
                    42,
                    6 * u,
                    9 * u,
                    0,
                    16,
                    448),
                Missile(
                    MissileFamily.PiercingLance,
                    40,
                    54,
                    16 * u,
                    0,
                    2,
                    0,
                    0),
                Missile(
                    MissileFamily.DownwardDrop,
                    18,
                    36,
                    10 * u,
                    8 * u,
                    0,
                    0,
                    0),
                Missile(
                    MissileFamily.Homing,
                    16,
                    42,
                    9 * u,
                    0,
                    0,
                    0,
                    0)
            };
            var formations = new[]
            {
                new OptionFormationDefinition(
                    OptionFormation.Trail,
                    12,
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    0,
                    0,
                    1),
                new OptionFormationDefinition(
                    OptionFormation.Fixed,
                    0,
                    new[] { 192, 192, 192, 192, 192, 192 },
                    new[] { 384, -384, 704, -704, 1024, -1024 },
                    0,
                    0,
                    1),
                new OptionFormationDefinition(
                    OptionFormation.Orbit,
                    0,
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    448,
                    1,
                    2)
            };
            return new BattleContent(
                Array.Empty<EnemyDefinition>(),
                new[] { main },
                main.Id,
                missileFamilies,
                MissileFamily.Straight,
                formations,
                OptionFormation.Trail);
        }

        static MissileFamilyDefinition Missile(
            MissileFamily family,
            int damage,
            int interval,
            int speedX,
            int fallY,
            int pierce,
            int explosionDamage,
            int explosionRadius)
        {
            return new MissileFamilyDefinition(
                family,
                damage,
                interval,
                Math.Max(1, interval / 2),
                family == MissileFamily.PiercingLance ? 6 : 5,
                speedX,
                SimSpace.TicksPerSecond,
                fallY,
                SimSpace.TicksPerSecond,
                pierce,
                explosionDamage,
                explosionRadius,
                explosionDamage == 0 ? 0 : 5,
                50,
                family == MissileFamily.DownwardDrop ? 2 : 0,
                family == MissileFamily.Homing ? 4 : 1);
        }

        sealed class RewardStageGenerator : IStageGenerator
        {
            static readonly BossPhase[] Phases =
            {
                new BossPhase(999, 1, 1, 1)
            };

            public StagePlan Generate(
                ulong seed,
                int stageIndex,
                int difficulty)
            {
                var segment = new StageSegment(
                    "reward",
                    1,
                    Array.Empty<SpawnEvent>(),
                    1,
                    1,
                    new[] { 1 });
                return new StagePlan(
                    new[] { segment },
                    "boss",
                    1,
                    1,
                    1,
                    1,
                    0,
                    0,
                    100,
                    Phases);
            }
        }

        sealed class FormationRewardStageGenerator : IStageGenerator
        {
            static readonly BossPhase[] Phases =
            {
                new BossPhase(999, 1, 1, 1)
            };

            public StagePlan Generate(
                ulong seed,
                int stageIndex,
                int difficulty)
            {
                var segment = new StageSegment(
                    "formation_reward",
                    200,
                    Array.Empty<SpawnEvent>(),
                    1,
                    1,
                    new[] { 1 });
                return new StagePlan(
                    new[] { segment },
                    "boss",
                    1,
                    1,
                    1,
                    1,
                    0,
                    0,
                    100,
                    Phases);
            }
        }

        const string RewardsWithSwitchesJson = @"{
  ""schemaVersion"": 1,
  ""optionCount"": 3,
  ""rewards"": [
    { ""id"": ""bomb"", ""type"": ""missileFamily"",
      ""familyId"": ""spread_bomb"", ""weight"": 2,
      ""stageIndexMin"": 1, ""stageIndexMax"": 99 },
    { ""id"": ""lance"", ""type"": ""missileFamily"",
      ""familyId"": ""piercing_lance"", ""weight"": 2,
      ""stageIndexMin"": 1, ""stageIndexMax"": 99 },
    { ""id"": ""orbit"", ""type"": ""optionFormation"",
      ""formationId"": ""orbit"", ""weight"": 2,
      ""stageIndexMin"": 1, ""stageIndexMax"": 99 }
  ]
}";

        const string PrimaryRewardsV2Json = @"{
  ""schemaVersion"": 2,
  ""optionCount"": 3,
  ""rewards"": [
    { ""id"": ""double"", ""type"": ""primaryWeaponFamily"",
      ""primaryFamilyId"": ""double"", ""weight"": 1,
      ""stageIndexMin"": 1, ""stageIndexMax"": 99 },
    { ""id"": ""laser"", ""type"": ""primaryWeaponFamily"",
      ""primaryFamilyId"": ""laser"", ""weight"": 1,
      ""stageIndexMin"": 1, ""stageIndexMax"": 99 },
    { ""id"": ""capsules"", ""type"": ""capsules"", ""amount"": 1,
      ""weight"": 1, ""stageIndexMin"": 1, ""stageIndexMax"": 99 }
  ]
}";

        const string ShipsV3Json = @"{
  ""schemaVersion"": 3,
  ""ships"": [
    {
      ""id"": ""starter"",
      ""displayName"": ""Starter"",
      ""moveSpeedMultiplierNumerator"": 1,
      ""moveSpeedMultiplierDenominator"": 1,
      ""startingPowerUpLevels"": [0, 0, 0, 0],
      ""unlockCost"": 0,
      ""weaponType"": ""vulcan"",
      ""missileFamily"": ""downward_drop""
    },
    {
      ""id"": ""interceptor"",
      ""displayName"": ""Interceptor"",
      ""moveSpeedMultiplierNumerator"": 1,
      ""moveSpeedMultiplierDenominator"": 1,
      ""startingPowerUpLevels"": [0, 0, 0, 0],
      ""unlockCost"": 1,
      ""weaponType"": ""spread"",
      ""missileFamily"": ""straight""
    },
    {
      ""id"": ""bulwark"",
      ""displayName"": ""Bulwark"",
      ""moveSpeedMultiplierNumerator"": 1,
      ""moveSpeedMultiplierDenominator"": 1,
      ""startingPowerUpLevels"": [0, 0, 0, 0],
      ""unlockCost"": 2,
      ""weaponType"": ""laser"",
      ""missileFamily"": ""homing""
    }
  ]
}";

        const string WeaponsV3Json = @"{
  ""schemaVersion"": 3,
  ""weapons"": [
    { ""id"": ""main_shot"", ""slot"": ""MainShot"", ""baseDamage"": 10,
      ""fireIntervalTicks"": 8, ""projectileSpeed"": 20,
      ""projectileHalfWidth"": 0.375, ""projectileHalfHeight"": 0.140625,
      ""maxLevel"": 5 },
    { ""id"": ""missile"", ""slot"": ""Missile"", ""baseDamage"": 20,
      ""fireIntervalTicks"": 30, ""minimumFireIntervalTicks"": 15,
      ""projectileSpeed"": 10, ""projectileHalfWidth"": 0.46875,
      ""projectileHalfHeight"": 0.28125, ""maxLevel"": 3 },
    { ""id"": ""option"", ""slot"": ""Option"", ""baseDamage"": 0,
      ""fireIntervalTicks"": 0, ""projectileSpeed"": 0,
      ""projectileHalfWidth"": 0, ""projectileHalfHeight"": 0,
      ""maxLevel"": 4 },
    { ""id"": ""shield"", ""slot"": ""Shield"", ""baseDamage"": 0,
      ""fireIntervalTicks"": 0, ""projectileSpeed"": 0,
      ""projectileHalfWidth"": 0, ""projectileHalfHeight"": 0,
      ""maxLevel"": 3 }
  ],
  ""missileFamilies"": [
    { ""id"": ""straight"", ""baseDamage"": 20,
      ""fireIntervalTicks"": 30, ""minimumFireIntervalTicks"": 15,
      ""fireIntervalReductionPerLevel"": 5, ""projectileSpeed"": 10,
      ""fallSpeedY"": 0, ""pierceEnemyCount"": 0,
      ""explosionDamage"": 0, ""explosionRadius"": 0,
      ""explosionMaxTargets"": 0 },
    { ""id"": ""spread_bomb"", ""baseDamage"": 12,
      ""fireIntervalTicks"": 42, ""minimumFireIntervalTicks"": 28,
      ""fireIntervalReductionPerLevel"": 5, ""projectileSpeed"": 6,
      ""fallSpeedY"": 9, ""pierceEnemyCount"": 0,
      ""explosionDamage"": 16, ""explosionRadius"": 1.75,
      ""explosionMaxTargets"": 5 },
    { ""id"": ""piercing_lance"", ""baseDamage"": 40,
      ""fireIntervalTicks"": 54, ""minimumFireIntervalTicks"": 36,
      ""fireIntervalReductionPerLevel"": 6, ""projectileSpeed"": 16,
      ""fallSpeedY"": 0, ""pierceEnemyCount"": 2,
      ""explosionDamage"": 0, ""explosionRadius"": 0,
      ""explosionMaxTargets"": 0 }
  ],
  ""defaultMissileFamily"": ""straight"",
  ""optionFormations"": [
    { ""id"": ""trail"", ""followDelayTicks"": 12 },
    { ""id"": ""fixed"", ""offsets"": [
      { ""x"": 0.75, ""y"": 1.5 },
      { ""x"": 0.75, ""y"": -1.5 },
      { ""x"": 0.75, ""y"": 2.75 },
      { ""x"": 0.75, ""y"": -2.75 }
    ] },
    { ""id"": ""orbit"", ""radius"": 1.75,
      ""angularLutSlotsNumerator"": 1,
      ""angularLutSlotsDenominator"": 2 }
  ],
  ""defaultOptionFormation"": ""trail""
}";
    }
}

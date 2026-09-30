using System;
using System.Globalization;
using Shmup.Core.Simulation;

namespace Shmup.Core
{
    /// <summary>
    /// Serializer-independent migration and integrity boundary for Core save DTOs.
    /// Presentation owns file IO; Core owns canonical field ordering and checksums.
    /// </summary>
    public static class SaveDataIntegrity
    {
        public static RunSuspendData MigrateAndValidate(
            RunSuspendData source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.schemaVersion < 1
                || source.schemaVersion > RunSuspendData.CurrentSchemaVersion)
            {
                throw Unsupported(
                    "run suspend",
                    source.schemaVersion);
            }
            if (source.schemaVersion == 14
                || source.schemaVersion == 15
                || source.schemaVersion == 16
                || source.schemaVersion == 17
                || source.schemaVersion == 18
                || source.schemaVersion == 19
                || source.schemaVersion == 20
                || source.schemaVersion == 21
                || source.schemaVersion == 22
                || source.schemaVersion == 23
                || source.schemaVersion == 24
                || source.schemaVersion == 25
                || source.schemaVersion == 26
                || source.schemaVersion == 27
                || source.schemaVersion == 28)
                throw Unsupported(
                    "run suspend",
                    source.schemaVersion);
            if (source.schemaVersion
                    == RunSuspendData.CurrentSchemaVersion
                && !HasValidChecksum(source))
            {
                throw Corrupted("Run suspend checksum is missing or invalid.");
            }
            if (source.schemaVersion >= 21)
                ValidateCurrentContractDestinations(
                    source.contractChoices,
                    "Run suspend");
            if (source.schemaVersion
                    < RunSuspendData.CurrentSchemaVersion
                && !string.IsNullOrEmpty(source.checksum)
                && !(source.schemaVersion == 13
                    && HasValidRunSuspendV13Checksum(source))
                && !(source.schemaVersion == 21
                    && HasValidRunSuspendV21Checksum(source))
                && !(source.schemaVersion == 11
                    && HasValidRunSuspendV11Checksum(source))
                && !(source.schemaVersion == 12
                    && HasValidRunSuspendV12Checksum(source))
                && !(source.schemaVersion == 9
                    && HasValidRunSuspendV9Checksum(source))
                && !(source.schemaVersion == 10
                    && HasValidRunSuspendV10Checksum(source))
                && !(source.schemaVersion == 8
                    && HasValidRunSuspendV8Checksum(source))
                && !(source.schemaVersion == 7
                    && HasValidRunSuspendV7Checksum(source))
                && !(source.schemaVersion == 6
                    && HasValidRunSuspendV6Checksum(source))
                && !(source.schemaVersion == 5
                    && HasValidRunSuspendV5Checksum(source))
                && !(source.schemaVersion == 4
                    && HasValidRunSuspendV4Checksum(source)))
            {
                throw Corrupted(
                    "Legacy run suspend contains an unexpected checksum.");
            }

            int migratedMaxShieldStock =
                source.schemaVersion >= 8
                    ? source.maxShieldStock
                    : BattleSimConfig.ProvisionalMaxShieldStock;
            int migratedShieldStock =
                source.schemaVersion >= 8
                    ? source.shieldStock
                    : MigrateLegacyDurabilityToShieldStock(
                        source.playerHp,
                        source.shieldRemaining,
                        migratedMaxShieldStock);

            var migrated = new RunSuspendData
            {
                schemaVersion = RunSuspendData.CurrentSchemaVersion,
                runSeed = source.runSeed,
                runNumber = source.runNumber,
                stageIndex = source.stageIndex,
                score = source.score,
                shotsFired = source.shotsFired,
                shotsHit = source.shotsHit,
                kills = source.kills,
                capsulesCollected = source.capsulesCollected,
                grazeCount = source.grazeCount,
                stagesCleared = source.stagesCleared,
                powerUpLevels = Clone(source.powerUpLevels),
                powerUpCursor = source.powerUpCursor,
                playerHp = source.schemaVersion >= 8
                    ? source.playerHp
                    : 1,
                shieldRemaining = source.schemaVersion >= 8
                    ? source.shieldRemaining
                    : migratedShieldStock,
                rewardAcquisitions =
                    Clone(source.rewardAcquisitions),
                activeModifiers = source.activeModifiers,
                shipId = source.shipId,
                fireIntervalTicks = source.fireIntervalTicks,
                mainShotBaseDamage = source.mainShotBaseDamage,
                playerSpeedNumerator = source.playerSpeedNumerator,
                playerSpeedDenominator = source.playerSpeedDenominator,
                difficultyMultiplierNumerator =
                    source.schemaVersion >= 2
                        ? source.difficultyMultiplierNumerator
                        : 1,
                difficultyMultiplierDenominator =
                    source.schemaVersion >= 2
                        ? source.difficultyMultiplierDenominator
                        : 1,
                routeChoices =
                    source.schemaVersion >= 3
                        ? Clone(
                            source.routeChoices,
                            source.schemaVersion >= 5)
                        : Array.Empty<RouteChoiceData>(),
                finalStageIndex =
                    source.schemaVersion >= 4
                        ? source.finalStageIndex
                        : RunProgressionConfig.DefaultFinalStageIndex,
                biomeIndex =
                    source.schemaVersion >= 5
                        ? source.biomeIndex
                        : source.stageIndex,
                roomIndex =
                    source.schemaVersion >= 5
                        ? source.roomIndex
                        : 1,
                isBiomeBoss =
                    source.schemaVersion >= 5
                        && source.isBiomeBoss,
                biomeCount =
                    source.schemaVersion >= 5
                        ? source.biomeCount
                        : source.schemaVersion >= 4
                            ? source.finalStageIndex
                            : RunProgressionConfig.DefaultBiomeCount,
                roomsPerBiome =
                    source.schemaVersion >= 5
                        ? source.roomsPerBiome
                        : 1,
                roomsCleared =
                    source.schemaVersion >= 5
                        ? source.roomsCleared
                        : Math.Max(0, source.stageIndex - 1),
                missileFamily =
                    source.schemaVersion >= 6
                        ? source.missileFamily
                        : (int)MissileFamily.Straight,
                optionFormation =
                    source.schemaVersion >= 6
                        ? source.optionFormation
                        : (int)OptionFormation.Trail,
                isHiddenBiome =
                    source.schemaVersion >= 7
                        && source.isHiddenBiome,
                eliteRoomsCleared =
                    source.schemaVersion >= 7
                        ? source.eliteRoomsCleared
                        : 0,
                noHitBiomesCleared =
                    source.schemaVersion >= 7
                        ? source.noHitBiomesCleared
                        : 0,
                rareEncountersCleared =
                    source.schemaVersion >= 7
                        ? source.rareEncountersCleared
                        : 0,
                currentBiomeHit =
                    source.schemaVersion >= 7
                        && source.currentBiomeHit,
                selectedColossalBoss =
                    source.schemaVersion >= 7
                        ? source.selectedColossalBoss
                        : 0,
                lastColossalBossAtRunStart =
                    source.schemaVersion >= 7
                        ? source.lastColossalBossAtRunStart
                        : 0,
                shieldStock = migratedShieldStock,
                maxShieldStock = migratedMaxShieldStock,
                bombStock = source.schemaVersion >= 9
                    ? source.bombStock
                    : 0,
                maxBombStock = source.schemaVersion >= 9
                    ? source.maxBombStock
                    : BattleSimConfig.ProvisionalMaxBombStock,
                primaryWeaponFamily =
                    source.schemaVersion >= 10
                        ? source.primaryWeaponFamily
                        : -1,
                powerUpProgress =
                    source.schemaVersion >= 11
                        ? Clone(source.powerUpProgress)
                        : new int[PowerUpGauge.SlotCount],
                hasStageStartContinuity =
                    source.schemaVersion >= 12
                    && source.hasStageStartContinuity,
                stageStartPlayerX =
                    source.stageStartPlayerX,
                stageStartPlayerY =
                    source.stageStartPlayerY,
                stageStartMultiplierLevel =
                    source.stageStartMultiplierLevel,
                stageStartComboGauge =
                    source.stageStartComboGauge,
                stageStartTicksSinceLastKill =
                    source.stageStartTicksSinceLastKill,
                activeContractId =
                    source.schemaVersion >= 13
                        ? source.activeContractId
                        : null,
                contractChoices =
                    source.schemaVersion >= 13
                        ? Clone(source.contractChoices)
                        : Array.Empty<ContractChoiceData>(),
                capsuleDropWeightReduction =
                    source.schemaVersion >= 13
                        ? source.capsuleDropWeightReduction
                        : 0,
                capsuleBalance =
                    source.schemaVersion >= 14
                        ? source.capsuleBalance
                        : 0,
                rewardDecisions =
                    source.schemaVersion >= 14
                        ? Clone(source.rewardDecisions)
                        : Array.Empty<RewardDecisionData>(),
                stageStartScrollX =
                    source.schemaVersion >= 21
                        ? source.stageStartScrollX
                        : 0L,
                bombsUsed =
                    source.schemaVersion >= 22
                        ? source.bombsUsed
                        : 0L,
                lastMidbossOutcome =
                    source.schemaVersion >= 24
                        ? source.lastMidbossOutcome
                        : 0,
                continueStock = source.schemaVersion >= 25
                    ? source.continueStock
                    : 0,
                initialContinueStock = source.schemaVersion >= 25
                    ? source.initialContinueStock
                    : 0,
                continuesUsed = source.schemaVersion >= 25
                    ? source.continuesUsed
                    : 0,
                isDailyRun = source.schemaVersion >= 25
                    && source.isDailyRun,
                finalWagerCommitted = source.schemaVersion >= 25
                    && source.finalWagerCommitted,
                finalWagerShieldGranted = source.schemaVersion >= 25
                    ? source.finalWagerShieldGranted
                    : 0,
                finalWagerOverflowConverted = source.schemaVersion >= 25
                    ? source.finalWagerOverflowConverted
                    : 0,
                finalWagerScoreBonus = source.schemaVersion >= 25
                    ? source.finalWagerScoreBonus
                    : 0,
                simulationTicksElapsed = source.schemaVersion >= 25
                    ? source.simulationTicksElapsed
                    : 0,
                continueDecisions = source.schemaVersion >= 25
                    ? Clone(source.continueDecisions)
                    : Array.Empty<ContinueDecisionData>(),
                continueMaximumStock = source.schemaVersion >= 25
                    ? source.continueMaximumStock
                    : ContinueEconomyConfig.DefaultMaximumStock,
                continueFirstPurchasePrice = source.schemaVersion >= 25
                    ? source.continueFirstPurchasePrice
                    : ContinueEconomyConfig.DefaultFirstPurchasePrice,
                continuePurchasePriceIncrease = source.schemaVersion >= 25
                    ? source.continuePurchasePriceIncrease
                    : ContinueEconomyConfig.DefaultPurchasePriceIncrease,
                finalWagerShieldCap = source.schemaVersion >= 25
                    ? source.finalWagerShieldCap
                    : ContinueEconomyConfig.DefaultFinalWagerShieldCap,
                continueOverflowScoreBonus = source.schemaVersion >= 25
                    ? source.continueOverflowScoreBonus
                    : ContinueEconomyConfig.DefaultOverflowScoreBonus,
                hitsTaken = source.schemaVersion >= 26
                    ? source.hitsTaken
                    : 0L,
                ghostRecording = source.schemaVersion >= 27
                    ? Clone(source.ghostRecording)
                    : CreateEmptyGhostRecording()
            };
            Seal(migrated);
            return migrated;
        }

        public static InputRecordingData MigrateAndValidate(
            InputRecordingData source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.schemaVersion < 1
                || source.schemaVersion
                    > InputRecordingData.CurrentSchemaVersion)
            {
                throw Unsupported(
                    "input recording",
                    source.schemaVersion);
            }
            if (source.schemaVersion == 13
                || source.schemaVersion == 14
                || source.schemaVersion == 15
                || source.schemaVersion == 16
                || source.schemaVersion == 17
                || source.schemaVersion == 18
                || source.schemaVersion == 19
                || source.schemaVersion == 20
                || source.schemaVersion == 21
                || source.schemaVersion == 22
                || source.schemaVersion == 23
                || source.schemaVersion == 24
                || source.schemaVersion == 25)
                throw Unsupported(
                    "input recording",
                    source.schemaVersion);
            if (source.schemaVersion
                    == InputRecordingData.CurrentSchemaVersion
                && !HasValidChecksum(source))
            {
                throw Corrupted(
                    "Input recording checksum is missing or invalid.");
            }
            if (source.schemaVersion
                    < InputRecordingData.CurrentSchemaVersion
                && !string.IsNullOrEmpty(source.checksum)
                && !(source.schemaVersion == 12
                    && HasValidInputRecordingV12Checksum(source))
                && !(source.schemaVersion == 8
                    && HasValidInputRecordingV8Checksum(source))
                && !(source.schemaVersion == 9
                    && HasValidInputRecordingV9Checksum(source))
                && !(source.schemaVersion == 7
                    && HasValidInputRecordingV7Checksum(source))
                && !(source.schemaVersion == 6
                    && HasValidInputRecordingV6Checksum(source))
                && !(source.schemaVersion == 5
                    && HasValidInputRecordingV5Checksum(source)))
            {
                throw Corrupted(
                    "Legacy input recording contains an unexpected checksum.");
            }

            var migrated = new InputRecordingData
            {
                schemaVersion = InputRecordingData.CurrentSchemaVersion,
                totalTicks = source.totalTicks,
                runs = Clone(
                    source.runs,
                    source.schemaVersion >= 2,
                    source.schemaVersion >= 9,
                    source.schemaVersion >= 10),
                difficultyMultiplierNumerator =
                    source.schemaVersion >= 3
                        ? source.difficultyMultiplierNumerator
                        : 1,
                difficultyMultiplierDenominator =
                    source.schemaVersion >= 3
                        ? source.difficultyMultiplierDenominator
                        : 1,
                routeChoices =
                    source.schemaVersion >= 4
                        ? Clone(
                            source.routeChoices,
                            source.schemaVersion >= 6)
                        : Array.Empty<RouteChoiceData>(),
                finalStageIndex =
                    source.schemaVersion >= 5
                        ? source.finalStageIndex
                        : RunProgressionConfig.DefaultFinalStageIndex,
                biomeCount =
                    source.schemaVersion >= 6
                        ? source.biomeCount
                        : source.schemaVersion >= 5
                            ? source.finalStageIndex
                            : RunProgressionConfig.DefaultBiomeCount,
                roomsPerBiome =
                    source.schemaVersion >= 6
                        ? source.roomsPerBiome
                        : 1,
                missileFamily =
                    source.schemaVersion >= 7
                        ? source.missileFamily
                        : (int)MissileFamily.Straight,
                optionFormation =
                    source.schemaVersion >= 7
                        ? source.optionFormation
                        : (int)OptionFormation.Trail,
                lastColossalBossAtRunStart =
                    source.schemaVersion >= 8
                        ? source.lastColossalBossAtRunStart
                        : 0,
                contractChoices =
                    source.schemaVersion >= 12
                        ? Clone(source.contractChoices)
                        : Array.Empty<ContractChoiceData>(),
                rewardDecisions =
                    source.schemaVersion >= 13
                        ? Clone(source.rewardDecisions)
                        : Array.Empty<RewardDecisionData>(),
                initialContinueStock = source.schemaVersion >= 23
                    ? source.initialContinueStock
                    : 0,
                isDailyRun = source.schemaVersion >= 23
                    && source.isDailyRun,
                continueDecisions = source.schemaVersion >= 23
                    ? Clone(source.continueDecisions)
                    : Array.Empty<ContinueDecisionData>(),
                continueMaximumStock = source.schemaVersion >= 23
                    ? source.continueMaximumStock
                    : ContinueEconomyConfig.DefaultMaximumStock,
                continueFirstPurchasePrice = source.schemaVersion >= 23
                    ? source.continueFirstPurchasePrice
                    : ContinueEconomyConfig.DefaultFirstPurchasePrice,
                continuePurchasePriceIncrease = source.schemaVersion >= 23
                    ? source.continuePurchasePriceIncrease
                    : ContinueEconomyConfig.DefaultPurchasePriceIncrease,
                finalWagerShieldCap = source.schemaVersion >= 23
                    ? source.finalWagerShieldCap
                    : ContinueEconomyConfig.DefaultFinalWagerShieldCap,
                continueOverflowScoreBonus = source.schemaVersion >= 23
                    ? source.continueOverflowScoreBonus
                    : ContinueEconomyConfig.DefaultOverflowScoreBonus
            };
            Seal(migrated);
            return migrated;
        }

        public static MetaStateData MigrateAndValidate(
            MetaStateData source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.schemaVersion != 0
                && source.schemaVersion != 1
                && source.schemaVersion != 2
                && source.schemaVersion
                    != MetaStateData.CurrentSchemaVersion)
            {
                throw Unsupported("meta state", source.schemaVersion);
            }
            if (source.schemaVersion
                    == MetaStateData.CurrentSchemaVersion
                && !HasValidChecksum(source))
            {
                throw Corrupted("Meta state checksum is missing or invalid.");
            }
            if (source.schemaVersion == 1
                && !HasValidLegacyMetaChecksum(source))
            {
                throw Corrupted(
                    "Legacy meta state checksum is missing or invalid.");
            }
            if (source.schemaVersion == 2
                && !HasValidMetaV2Checksum(source))
            {
                throw Corrupted(
                    "Legacy meta state checksum is missing or invalid.");
            }
            if (source.schemaVersion == 0
                && !string.IsNullOrEmpty(source.checksum))
            {
                throw Corrupted(
                    "Legacy meta state contains an unexpected checksum.");
            }

            var migrated = new MetaStateData
            {
                schemaVersion = MetaStateData.CurrentSchemaVersion,
                totalCurrency = source.totalCurrency,
                unlockedShipIds = Clone(source.unlockedShipIds),
                selectedShipId = source.selectedShipId,
                lastColossalBoss = source.schemaVersion >= 2
                    ? source.lastColossalBoss
                    : 0,
                continueStock = source.schemaVersion >= 3
                    ? source.continueStock
                    : 0
            };
            Seal(migrated);
            return migrated;
        }

        /// <summary>
        /// Marks a populated DTO as current and refreshes its canonical checksum.
        /// Intended for Core exporters and tests constructing serializer payloads.
        /// </summary>
        public static void Seal(RunSuspendData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.schemaVersion = RunSuspendData.CurrentSchemaVersion;
            data.checksum = ComputeChecksum(data);
        }

        public static void Seal(InputRecordingData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.schemaVersion = InputRecordingData.CurrentSchemaVersion;
            data.checksum = ComputeChecksum(data);
        }

        public static void Seal(MetaStateData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.schemaVersion = MetaStateData.CurrentSchemaVersion;
            data.checksum = ComputeChecksum(data);
        }

        public static bool HasValidChecksum(RunSuspendData data)
        {
            return data != null
                && IsChecksum(data.checksum)
                && string.Equals(
                    data.checksum,
                    ComputeChecksum(data),
                    StringComparison.Ordinal);
        }

        public static bool HasValidChecksum(InputRecordingData data)
        {
            return data != null
                && IsChecksum(data.checksum)
                && string.Equals(
                    data.checksum,
                    ComputeChecksum(data),
                    StringComparison.Ordinal);
        }

        public static bool HasValidChecksum(MetaStateData data)
        {
            return data != null
                && IsChecksum(data.checksum)
                && string.Equals(
                    data.checksum,
                    ComputeChecksum(data),
                    StringComparison.Ordinal);
        }

        static string ComputeChecksum(RunSuspendData data)
        {
            return ComputeChecksum(data, true);
        }

        static string ComputeChecksum(
            RunSuspendData data,
            bool includeBombsUsed)
        {
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.isHiddenBiome);
            hash.Add(data.eliteRoomsCleared);
            hash.Add(data.noHitBiomesCleared);
            hash.Add(data.rareEncountersCleared);
            hash.Add(data.currentBiomeHit);
            hash.Add(data.selectedColossalBoss);
            hash.Add(data.lastColossalBossAtRunStart);
            hash.Add(data.shieldStock);
            hash.Add(data.maxShieldStock);
            hash.Add(data.bombStock);
            hash.Add(data.maxBombStock);
            hash.Add(data.primaryWeaponFamily);
            hash.Add(data.powerUpProgress);
            hash.Add(data.hasStageStartContinuity);
            hash.Add(data.stageStartPlayerX);
            hash.Add(data.stageStartPlayerY);
            hash.Add(data.stageStartMultiplierLevel);
            hash.Add(data.stageStartComboGauge);
            hash.Add(data.stageStartTicksSinceLastKill);
            // Both spellings mean no contract. Unity's JSON serializer turns
            // a null string into an empty string, so hash the semantic value.
            hash.Add(data.activeContractId ?? string.Empty);
            Add(ref hash, data.contractChoices);
            hash.Add(data.capsuleDropWeightReduction);
            hash.Add(data.capsuleBalance);
            Add(ref hash, data.rewardDecisions);
            hash.Add(data.stageStartScrollX);
            if (includeBombsUsed)
                hash.Add(data.bombsUsed);
            hash.Add(data.lastMidbossOutcome);
            hash.Add(data.continueStock);
            hash.Add(data.initialContinueStock);
            hash.Add(data.continuesUsed);
            hash.Add(data.isDailyRun);
            hash.Add(data.finalWagerCommitted);
            hash.Add(data.finalWagerShieldGranted);
            hash.Add(data.finalWagerOverflowConverted);
            hash.Add(data.finalWagerScoreBonus);
            hash.Add(data.simulationTicksElapsed);
            Add(ref hash, data.continueDecisions);
            hash.Add(data.continueMaximumStock);
            hash.Add(data.continueFirstPurchasePrice);
            hash.Add(data.continuePurchasePriceIncrease);
            hash.Add(data.finalWagerShieldCap);
            hash.Add(data.continueOverflowScoreBonus);
            hash.Add(data.hitsTaken);
            Add(ref hash, data.ghostRecording);
            return hash.ToString();
        }

        static bool HasValidRunSuspendV21Checksum(
            RunSuspendData data)
        {
            return IsChecksum(data.checksum)
                && string.Equals(
                    data.checksum,
                    ComputeChecksum(data, false),
                    StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV13Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.isHiddenBiome);
            hash.Add(data.eliteRoomsCleared);
            hash.Add(data.noHitBiomesCleared);
            hash.Add(data.rareEncountersCleared);
            hash.Add(data.currentBiomeHit);
            hash.Add(data.selectedColossalBoss);
            hash.Add(data.lastColossalBossAtRunStart);
            hash.Add(data.shieldStock);
            hash.Add(data.maxShieldStock);
            hash.Add(data.bombStock);
            hash.Add(data.maxBombStock);
            hash.Add(data.primaryWeaponFamily);
            hash.Add(data.powerUpProgress);
            hash.Add(data.hasStageStartContinuity);
            hash.Add(data.stageStartPlayerX);
            hash.Add(data.stageStartPlayerY);
            hash.Add(data.stageStartMultiplierLevel);
            hash.Add(data.stageStartComboGauge);
            hash.Add(data.stageStartTicksSinceLastKill);
            hash.Add(data.activeContractId);
            AddLegacy(ref hash, data.contractChoices);
            hash.Add(data.capsuleDropWeightReduction);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV12Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.isHiddenBiome);
            hash.Add(data.eliteRoomsCleared);
            hash.Add(data.noHitBiomesCleared);
            hash.Add(data.rareEncountersCleared);
            hash.Add(data.currentBiomeHit);
            hash.Add(data.selectedColossalBoss);
            hash.Add(data.lastColossalBossAtRunStart);
            hash.Add(data.shieldStock);
            hash.Add(data.maxShieldStock);
            hash.Add(data.bombStock);
            hash.Add(data.maxBombStock);
            hash.Add(data.primaryWeaponFamily);
            hash.Add(data.powerUpProgress);
            hash.Add(data.hasStageStartContinuity);
            hash.Add(data.stageStartPlayerX);
            hash.Add(data.stageStartPlayerY);
            hash.Add(data.stageStartMultiplierLevel);
            hash.Add(data.stageStartComboGauge);
            hash.Add(data.stageStartTicksSinceLastKill);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV11Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.isHiddenBiome);
            hash.Add(data.eliteRoomsCleared);
            hash.Add(data.noHitBiomesCleared);
            hash.Add(data.rareEncountersCleared);
            hash.Add(data.currentBiomeHit);
            hash.Add(data.selectedColossalBoss);
            hash.Add(data.lastColossalBossAtRunStart);
            hash.Add(data.shieldStock);
            hash.Add(data.maxShieldStock);
            hash.Add(data.bombStock);
            hash.Add(data.maxBombStock);
            hash.Add(data.primaryWeaponFamily);
            hash.Add(data.powerUpProgress);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV10Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.isHiddenBiome);
            hash.Add(data.eliteRoomsCleared);
            hash.Add(data.noHitBiomesCleared);
            hash.Add(data.rareEncountersCleared);
            hash.Add(data.currentBiomeHit);
            hash.Add(data.selectedColossalBoss);
            hash.Add(data.lastColossalBossAtRunStart);
            hash.Add(data.shieldStock);
            hash.Add(data.maxShieldStock);
            hash.Add(data.bombStock);
            hash.Add(data.maxBombStock);
            hash.Add(data.primaryWeaponFamily);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV9Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.isHiddenBiome);
            hash.Add(data.eliteRoomsCleared);
            hash.Add(data.noHitBiomesCleared);
            hash.Add(data.rareEncountersCleared);
            hash.Add(data.currentBiomeHit);
            hash.Add(data.selectedColossalBoss);
            hash.Add(data.lastColossalBossAtRunStart);
            hash.Add(data.shieldStock);
            hash.Add(data.maxShieldStock);
            hash.Add(data.bombStock);
            hash.Add(data.maxBombStock);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static string ComputeChecksum(InputRecordingData data)
        {
            var hash = new CanonicalHash("InputRecordingData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalTicks);
            if (data.runs == null)
            {
                hash.Add(-1);
            }
            else
            {
                hash.Add(data.runs.Length);
                for (int i = 0; i < data.runs.Length; i++)
                {
                    InputRunData run = data.runs[i];
                    hash.Add(run != null);
                    if (run == null)
                        continue;
                    hash.Add(run.moveX);
                    hash.Add(run.moveY);
                    hash.Add(run.fire);
                    hash.Add(run.activate);
                    hash.Add(run.activateBomb);
                    hash.Add(run.tickCount);
                    hash.Add(run.useAnalogMovement);
                    hash.Add(run.analogDeltaXSubUnits);
                    hash.Add(run.analogDeltaYSubUnits);
                }
            }
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.lastColossalBossAtRunStart);
            Add(ref hash, data.contractChoices);
            Add(ref hash, data.rewardDecisions);
            hash.Add(data.initialContinueStock);
            hash.Add(data.isDailyRun);
            Add(ref hash, data.continueDecisions);
            hash.Add(data.continueMaximumStock);
            hash.Add(data.continueFirstPurchasePrice);
            hash.Add(data.continuePurchasePriceIncrease);
            hash.Add(data.finalWagerShieldCap);
            hash.Add(data.continueOverflowScoreBonus);
            return hash.ToString();
        }

        static bool HasValidInputRecordingV12Checksum(
            InputRecordingData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("InputRecordingData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalTicks);
            if (data.runs == null)
            {
                hash.Add(-1);
            }
            else
            {
                hash.Add(data.runs.Length);
                for (int i = 0; i < data.runs.Length; i++)
                {
                    InputRunData run = data.runs[i];
                    hash.Add(run != null);
                    if (run == null)
                        continue;
                    hash.Add(run.moveX);
                    hash.Add(run.moveY);
                    hash.Add(run.fire);
                    hash.Add(run.activate);
                    hash.Add(run.activateBomb);
                    hash.Add(run.tickCount);
                    hash.Add(run.useAnalogMovement);
                    hash.Add(run.analogDeltaXSubUnits);
                    hash.Add(run.analogDeltaYSubUnits);
                }
            }
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.lastColossalBossAtRunStart);
            AddLegacy(ref hash, data.contractChoices);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidInputRecordingV7Checksum(
            InputRecordingData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("InputRecordingData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalTicks);
            AddInputRuns(ref hash, data.runs);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidInputRecordingV8Checksum(
            InputRecordingData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("InputRecordingData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalTicks);
            AddInputRuns(ref hash, data.runs);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.lastColossalBossAtRunStart);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidInputRecordingV9Checksum(
            InputRecordingData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("InputRecordingData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalTicks);
            AddInputRunsWithBomb(ref hash, data.runs);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.lastColossalBossAtRunStart);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV8Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.isHiddenBiome);
            hash.Add(data.eliteRoomsCleared);
            hash.Add(data.noHitBiomesCleared);
            hash.Add(data.rareEncountersCleared);
            hash.Add(data.currentBiomeHit);
            hash.Add(data.selectedColossalBoss);
            hash.Add(data.lastColossalBossAtRunStart);
            hash.Add(data.shieldStock);
            hash.Add(data.maxShieldStock);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV7Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            hash.Add(data.isHiddenBiome);
            hash.Add(data.eliteRoomsCleared);
            hash.Add(data.noHitBiomesCleared);
            hash.Add(data.rareEncountersCleared);
            hash.Add(data.currentBiomeHit);
            hash.Add(data.selectedColossalBoss);
            hash.Add(data.lastColossalBossAtRunStart);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV5Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV6Checksum(
            RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            Add(ref hash, data.rewardAcquisitions);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeIndex);
            hash.Add(data.roomIndex);
            hash.Add(data.isBiomeBoss);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            hash.Add(data.roomsCleared);
            hash.Add(data.missileFamily);
            hash.Add(data.optionFormation);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidInputRecordingV6Checksum(
            InputRecordingData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("InputRecordingData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalTicks);
            AddInputRuns(ref hash, data.runs);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            Add(ref hash, data.routeChoices);
            hash.Add(data.finalStageIndex);
            hash.Add(data.biomeCount);
            hash.Add(data.roomsPerBiome);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidRunSuspendV4Checksum(RunSuspendData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("RunSuspendData");
            hash.Add(data.schemaVersion);
            hash.Add(data.runSeed);
            hash.Add(data.runNumber);
            hash.Add(data.stageIndex);
            hash.Add(data.score);
            hash.Add(data.shotsFired);
            hash.Add(data.shotsHit);
            hash.Add(data.kills);
            hash.Add(data.capsulesCollected);
            hash.Add(data.grazeCount);
            hash.Add(data.stagesCleared);
            hash.Add(data.powerUpLevels);
            hash.Add(data.powerUpCursor);
            hash.Add(data.playerHp);
            hash.Add(data.shieldRemaining);
            hash.Add(data.activeModifiers);
            hash.Add(data.shipId);
            hash.Add(data.fireIntervalTicks);
            hash.Add(data.mainShotBaseDamage);
            hash.Add(data.playerSpeedNumerator);
            hash.Add(data.playerSpeedDenominator);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            hash.Add(data.finalStageIndex);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidInputRecordingV5Checksum(InputRecordingData data)
        {
            if (!IsChecksum(data.checksum))
                return false;
            var hash = new CanonicalHash("InputRecordingData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalTicks);
            AddInputRuns(ref hash, data.runs);
            hash.Add(data.difficultyMultiplierNumerator);
            hash.Add(data.difficultyMultiplierDenominator);
            hash.Add(data.finalStageIndex);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static string ComputeChecksum(MetaStateData data)
        {
            var hash = new CanonicalHash("MetaStateData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalCurrency);
            hash.Add(data.unlockedShipIds);
            hash.Add(data.selectedShipId);
            hash.Add(data.lastColossalBoss);
            hash.Add(data.continueStock);
            return hash.ToString();
        }

        static bool HasValidMetaV2Checksum(MetaStateData data)
        {
            var hash = new CanonicalHash("MetaStateData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalCurrency);
            hash.Add(data.unlockedShipIds);
            hash.Add(data.selectedShipId);
            hash.Add(data.lastColossalBoss);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static bool HasValidLegacyMetaChecksum(
            MetaStateData data)
        {
            var hash = new CanonicalHash("MetaStateData");
            hash.Add(data.schemaVersion);
            hash.Add(data.totalCurrency);
            hash.Add(data.unlockedShipIds);
            hash.Add(data.selectedShipId);
            return string.Equals(
                data.checksum,
                hash.ToString(),
                StringComparison.Ordinal);
        }

        static void Add(
            ref CanonicalHash hash,
            RewardAcquisitionData[] acquisitions)
        {
            if (acquisitions == null)
            {
                hash.Add(-1);
                return;
            }
            hash.Add(acquisitions.Length);
            for (int i = 0; i < acquisitions.Length; i++)
            {
                RewardAcquisitionData acquisition = acquisitions[i];
                hash.Add(acquisition != null);
                if (acquisition == null)
                    continue;
                hash.Add(acquisition.rewardId);
                hash.Add(acquisition.count);
            }
        }

        static void Add(
            ref CanonicalHash hash,
            RouteChoiceData[] choices)
        {
            if (choices == null)
            {
                hash.Add(-1);
                return;
            }
            hash.Add(choices.Length);
            for (int i = 0; i < choices.Length; i++)
            {
                RouteChoiceData choice = choices[i];
                hash.Add(choice != null);
                if (choice == null)
                    continue;
                hash.Add(choice.stageIndex);
                hash.Add(choice.optionIndex);
                hash.Add(choice.themeId);
                hash.Add(choice.encounterType);
                hash.Add(choice.biomeIndex);
                hash.Add(choice.roomIndex);
            }
        }

        static void Add(
            ref CanonicalHash hash,
            ContractChoiceData[] choices)
        {
            if (choices == null)
            {
                hash.Add(-1);
                return;
            }
            hash.Add(choices.Length);
            for (int i = 0; i < choices.Length; i++)
            {
                ContractChoiceData choice = choices[i];
                hash.Add(choice != null);
                if (choice == null)
                    continue;
                hash.Add(choice.targetBiomeIndex);
                hash.Add(choice.optionIndex);
                hash.Add(choice.contractId);
                hash.Add(choice.destinationKind);
                hash.Add(choice.destinationThemeId);
                hash.Add(choice.destinationThemeStageIndex);
            }
        }

        static void AddLegacy(
            ref CanonicalHash hash,
            ContractChoiceData[] choices)
        {
            if (choices == null)
            {
                hash.Add(-1);
                return;
            }
            hash.Add(choices.Length);
            for (int i = 0; i < choices.Length; i++)
            {
                ContractChoiceData choice = choices[i];
                hash.Add(choice != null);
                if (choice == null)
                    continue;
                hash.Add(choice.targetBiomeIndex);
                hash.Add(choice.optionIndex);
                hash.Add(choice.contractId);
            }
        }

        static void Add(
            ref CanonicalHash hash,
            RewardDecisionData[] decisions)
        {
            if (decisions == null)
            {
                hash.Add(-1);
                return;
            }
            hash.Add(decisions.Length);
            for (int i = 0; i < decisions.Length; i++)
            {
                RewardDecisionData decision = decisions[i];
                hash.Add(decision != null);
                if (decision == null)
                    continue;
                hash.Add(decision.rewardSequence);
                hash.Add(decision.selectionKind);
                hash.Add(decision.decisionKind);
                hash.Add(decision.optionIndex);
            }
        }

        static void Add(
            ref CanonicalHash hash,
            ContinueDecisionData[] decisions)
        {
            if (decisions == null)
            {
                hash.Add(-1);
                return;
            }
            hash.Add(decisions.Length);
            for (int i = 0; i < decisions.Length; i++)
            {
                ContinueDecisionData decision = decisions[i];
                hash.Add(decision != null);
                if (decision != null)
                    hash.Add(decision.simulationTick);
            }
        }

        static void AddInputRuns(
            ref CanonicalHash hash,
            InputRunData[] runs)
        {
            if (runs == null)
            {
                hash.Add(-1);
                return;
            }
            hash.Add(runs.Length);
            for (int i = 0; i < runs.Length; i++)
            {
                InputRunData run = runs[i];
                hash.Add(run != null);
                if (run == null)
                    continue;
                hash.Add(run.moveX);
                hash.Add(run.moveY);
                hash.Add(run.fire);
                hash.Add(run.activate);
                hash.Add(run.tickCount);
            }
        }

        static void AddInputRunsWithBomb(
            ref CanonicalHash hash,
            InputRunData[] runs)
        {
            if (runs == null)
            {
                hash.Add(-1);
                return;
            }
            hash.Add(runs.Length);
            for (int i = 0; i < runs.Length; i++)
            {
                InputRunData run = runs[i];
                hash.Add(run != null);
                if (run == null)
                    continue;
                hash.Add(run.moveX);
                hash.Add(run.moveY);
                hash.Add(run.fire);
                hash.Add(run.activate);
                hash.Add(run.activateBomb);
                hash.Add(run.tickCount);
            }
        }

        static void Add(
            ref CanonicalHash hash,
            GhostRecordingData recording)
        {
            hash.Add(recording != null);
            if (recording == null)
                return;
            hash.Add(recording.hasStartState);
            hash.Add(recording.finalized);
            hash.Add(recording.totalTicks);
            if (recording.runs == null)
            {
                hash.Add(-1);
            }
            else
            {
                hash.Add(recording.runs.Length);
                for (int i = 0; i < recording.runs.Length; i++)
                {
                    InputRunData run = recording.runs[i];
                    hash.Add(run != null);
                    if (run == null)
                        continue;
                    hash.Add(run.moveX);
                    hash.Add(run.moveY);
                    hash.Add(run.fire);
                    hash.Add(run.activate);
                    hash.Add(run.activateBomb);
                    hash.Add(run.tickCount);
                    hash.Add(run.useAnalogMovement);
                    hash.Add(run.analogDeltaXSubUnits);
                    hash.Add(run.analogDeltaYSubUnits);
                }
            }
            hash.Add(recording.startX);
            hash.Add(recording.startY);
            hash.Add(recording.speedNumerator);
            hash.Add(recording.speedDenominator);
            hash.Add(recording.minimumX);
            hash.Add(recording.maximumX);
            hash.Add(recording.minimumY);
            hash.Add(recording.maximumY);
            hash.Add(recording.fixedWeaponLevel);
            hash.Add(recording.fireIntervalTicks);
            hash.Add(recording.maximumInputRuns);
            hash.Add(recording.playbackActive);
            hash.Add(recording.playbackX);
            hash.Add(recording.playbackY);
            hash.Add(recording.playbackTick);
            hash.Add(recording.playbackCooldownTicks);
            hash.Add(recording.playbackMovementRemainderX);
            hash.Add(recording.playbackMovementRemainderY);
            hash.Add(recording.playbackIsFiring);
        }

        static int MigrateLegacyDurabilityToShieldStock(
            int playerHp,
            int shieldRemaining,
            int maxShieldStock)
        {
            if (maxShieldStock < 1)
                return 0;
            long legacySurvivableHits =
                Math.Max(0L, (long)playerHp)
                + Math.Max(0L, (long)shieldRemaining);
            long stock = Math.Max(0L, legacySurvivableHits - 1L);
            return (int)Math.Min(stock, maxShieldStock);
        }

        static int[] Clone(int[] source)
        {
            return source == null ? null : (int[])source.Clone();
        }

        static string[] Clone(string[] source)
        {
            return source == null ? null : (string[])source.Clone();
        }

        static RewardAcquisitionData[] Clone(
            RewardAcquisitionData[] source)
        {
            if (source == null)
                return null;
            var copy = new RewardAcquisitionData[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                RewardAcquisitionData item = source[i];
                copy[i] = item == null
                    ? null
                    : new RewardAcquisitionData
                    {
                        rewardId = item.rewardId,
                        count = item.count
                    };
            }
            return copy;
        }

        static RouteChoiceData[] Clone(
            RouteChoiceData[] source,
            bool includeRoomPosition)
        {
            if (source == null)
                return null;
            var copy = new RouteChoiceData[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                RouteChoiceData item = source[i];
                copy[i] = item == null
                    ? null
                    : new RouteChoiceData
                    {
                        stageIndex = item.stageIndex,
                        biomeIndex = includeRoomPosition
                            ? item.biomeIndex
                            : item.stageIndex,
                        roomIndex = includeRoomPosition
                            ? item.roomIndex
                            : 1,
                        optionIndex = item.optionIndex,
                        themeId = item.themeId,
                        encounterType = item.encounterType
                    };
            }
            return copy;
        }

        static ContractChoiceData[] Clone(
            ContractChoiceData[] source)
        {
            if (source == null)
                return null;
            var copy =
                new ContractChoiceData[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                ContractChoiceData item = source[i];
                copy[i] = item == null
                    ? null
                    : new ContractChoiceData
                    {
                        targetBiomeIndex =
                            item.targetBiomeIndex,
                        optionIndex = item.optionIndex,
                        contractId = item.contractId,
                        destinationKind =
                            item.destinationKind,
                        destinationThemeId =
                            item.destinationThemeId,
                        destinationThemeStageIndex =
                            item.destinationThemeStageIndex
                    };
            }
            return copy;
        }

        static void ValidateCurrentContractDestinations(
            ContractChoiceData[] choices,
            string payloadName)
        {
            if (choices == null)
                return;
            for (int i = 0; i < choices.Length; i++)
            {
                ContractChoiceData choice = choices[i];
                if (choice != null
                    && choice.destinationKind
                        == (int)ContractDestinationKind.NextStage
                    && (string.IsNullOrEmpty(
                            choice.destinationThemeId)
                        || choice.destinationThemeStageIndex < 1))
                    throw Corrupted(
                        payloadName
                        + " contract destination is missing or invalid.");
            }
        }

        static RewardDecisionData[] Clone(
            RewardDecisionData[] source)
        {
            if (source == null)
                return null;
            var copy =
                new RewardDecisionData[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                RewardDecisionData item = source[i];
                copy[i] = item == null
                    ? null
                    : new RewardDecisionData
                    {
                        rewardSequence =
                            item.rewardSequence,
                        selectionKind =
                            item.selectionKind,
                        decisionKind =
                            item.decisionKind,
                        optionIndex =
                            item.optionIndex
                    };
            }
            return copy;
        }

        static ContinueDecisionData[] Clone(
            ContinueDecisionData[] source)
        {
            if (source == null)
                return null;
            var copy = new ContinueDecisionData[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                ContinueDecisionData item = source[i];
                copy[i] = item == null
                    ? null
                    : new ContinueDecisionData
                    {
                        simulationTick = item.simulationTick
                    };
            }
            return copy;
        }

        static InputRunData[] Clone(
            InputRunData[] source,
            bool includeActivate,
            bool includeBomb,
            bool includeAnalog)
        {
            if (source == null)
                return null;
            var copy = new InputRunData[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                InputRunData item = source[i];
                copy[i] = item == null
                    ? null
                    : new InputRunData
                    {
                        moveX = item.moveX,
                        moveY = item.moveY,
                        fire = item.fire,
                        activate = includeActivate && item.activate,
                        activateBomb =
                            includeBomb && item.activateBomb,
                        tickCount = item.tickCount,
                        useAnalogMovement =
                            includeAnalog
                            && item.useAnalogMovement,
                        analogDeltaXSubUnits =
                            includeAnalog
                                ? item.analogDeltaXSubUnits
                                : 0,
                        analogDeltaYSubUnits =
                            includeAnalog
                                ? item.analogDeltaYSubUnits
                                : 0
                    };
            }
            return copy;
        }

        static GhostRecordingData Clone(GhostRecordingData source)
        {
            if (source == null)
                return null;
            return new GhostRecordingData
            {
                hasStartState = source.hasStartState,
                finalized = source.finalized,
                totalTicks = source.totalTicks,
                runs = Clone(source.runs, true, true, true),
                startX = source.startX,
                startY = source.startY,
                speedNumerator = source.speedNumerator,
                speedDenominator = source.speedDenominator,
                minimumX = source.minimumX,
                maximumX = source.maximumX,
                minimumY = source.minimumY,
                maximumY = source.maximumY,
                fixedWeaponLevel = source.fixedWeaponLevel,
                fireIntervalTicks = source.fireIntervalTicks,
                maximumInputRuns = source.maximumInputRuns,
                playbackActive = source.playbackActive,
                playbackX = source.playbackX,
                playbackY = source.playbackY,
                playbackTick = source.playbackTick,
                playbackCooldownTicks =
                    source.playbackCooldownTicks,
                playbackMovementRemainderX =
                    source.playbackMovementRemainderX,
                playbackMovementRemainderY =
                    source.playbackMovementRemainderY,
                playbackIsFiring = source.playbackIsFiring
            };
        }

        static GhostRecordingData CreateEmptyGhostRecording()
        {
            GhostReplayConfig config = GhostReplayConfig.CreateDefault();
            return new GhostRecordingData
            {
                runs = Array.Empty<InputRunData>(),
                speedDenominator = 1,
                fixedWeaponLevel = config.FixedWeaponLevel,
                fireIntervalTicks = config.FireIntervalTicks,
                maximumInputRuns = config.MaximumInputRuns
            };
        }

        static ArgumentException Unsupported(
            string payloadName,
            int version)
        {
            return Corrupted(
                $"Unsupported {payloadName} schema version {version}.");
        }

        static ArgumentException Corrupted(string message)
        {
            return new ArgumentException(message, "data");
        }

        static bool IsChecksum(string value)
        {
            if (value == null || value.Length != 16)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if ((character < '0' || character > '9')
                    && (character < 'A' || character > 'F'))
                    return false;
            }
            return true;
        }

        struct CanonicalHash
        {
            const ulong OffsetBasis = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong _value;

            public CanonicalHash(string typeTag)
            {
                _value = OffsetBasis;
                Add(typeTag);
            }

            public void Add(bool value)
            {
                AddByte(value ? (byte)1 : (byte)0);
            }

            public void Add(int value)
            {
                Add(unchecked((uint)value));
            }

            public void Add(uint value)
            {
                AddByte((byte)value);
                AddByte((byte)(value >> 8));
                AddByte((byte)(value >> 16));
                AddByte((byte)(value >> 24));
            }

            public void Add(long value)
            {
                Add(unchecked((ulong)value));
            }

            public void Add(ulong value)
            {
                AddByte((byte)value);
                AddByte((byte)(value >> 8));
                AddByte((byte)(value >> 16));
                AddByte((byte)(value >> 24));
                AddByte((byte)(value >> 32));
                AddByte((byte)(value >> 40));
                AddByte((byte)(value >> 48));
                AddByte((byte)(value >> 56));
            }

            public void Add(string value)
            {
                if (value == null)
                {
                    Add(-1);
                    return;
                }
                Add(value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    AddByte((byte)character);
                    AddByte((byte)(character >> 8));
                }
            }

            public void Add(int[] values)
            {
                if (values == null)
                {
                    Add(-1);
                    return;
                }
                Add(values.Length);
                for (int i = 0; i < values.Length; i++)
                    Add(values[i]);
            }

            public void Add(string[] values)
            {
                if (values == null)
                {
                    Add(-1);
                    return;
                }
                Add(values.Length);
                for (int i = 0; i < values.Length; i++)
                    Add(values[i]);
            }

            public override string ToString()
            {
                return _value.ToString(
                    "X16",
                    CultureInfo.InvariantCulture);
            }

            void AddByte(byte value)
            {
                _value ^= value;
                _value *= Prime;
            }
        }
    }
}

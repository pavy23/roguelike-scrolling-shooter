using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Shmup.Core.Generation;

namespace Shmup.Core.Simulation
{
    /// <summary>Serializer-facing run-length encoded input command.</summary>
    [Serializable]
    [DataContract]
    public sealed class InputRunData
    {
        [DataMember(Order = 0)]
        public int moveX;

        [DataMember(Order = 1)]
        public int moveY;

        [DataMember(Order = 2)]
        public bool fire;

        [DataMember(Order = 3)]
        public bool activate;

        [DataMember(Order = 4)]
        public bool activateBomb;

        [DataMember(Order = 5)]
        public int tickCount;

        [DataMember(Order = 6)]
        public bool useAnalogMovement;

        [DataMember(Order = 7)]
        public int analogDeltaXSubUnits;

        [DataMember(Order = 8)]
        public int analogDeltaYSubUnits;
    }

    /// <summary>
    /// Allocation-free read-only view of one canonical RLE command run.
    /// </summary>
    public readonly struct RecordedInputRun
    {
        public RecordedInputRun(in InputCommand command, int tickCount)
        {
            if (tickCount < 1)
                throw new ArgumentOutOfRangeException(nameof(tickCount));
            Command = command;
            TickCount = tickCount;
        }

        public InputCommand Command { get; }
        public int TickCount { get; }
    }

    /// <summary>
    /// Serializer-facing input recording. Presentation owns persistence.
    /// </summary>
    [Serializable]
    [DataContract]
    public sealed class InputRecordingData
    {
        /// <summary>
        /// Schema 26 records unused ordinary segments supplementing exhausted
        /// post-midboss outcome pools. Schema 25 recordings are rejected
        /// because the same seed can now produce a different closing route.
        /// </summary>
        public const int CurrentSchemaVersion = 26;

        [DataMember(Order = 0)]
        public int schemaVersion;

        [DataMember(Order = 1)]
        public int totalTicks;

        [DataMember(Order = 2)]
        public InputRunData[] runs;

        [DataMember(Order = 3)]
        public int difficultyMultiplierNumerator = 1;

        [DataMember(Order = 4)]
        public int difficultyMultiplierDenominator = 1;

        [DataMember(Order = 5)]
        public RouteChoiceData[] routeChoices;

        [DataMember(Order = 6)]
        public int finalStageIndex;

        [DataMember(Order = 7)]
        public string checksum;

        [DataMember(Order = 8)]
        public int biomeCount;

        [DataMember(Order = 9)]
        public int roomsPerBiome;

        [DataMember(Order = 10)]
        public int missileFamily;

        [DataMember(Order = 11)]
        public int optionFormation;

        [DataMember(Order = 12)]
        public int lastColossalBossAtRunStart;

        [DataMember(Order = 13)]
        public ContractChoiceData[] contractChoices;

        [DataMember(Order = 14)]
        public RewardDecisionData[] rewardDecisions;

        [DataMember(Order = 15)]
        public int initialContinueStock;

        [DataMember(Order = 16)]
        public bool isDailyRun;

        [DataMember(Order = 17)]
        public ContinueDecisionData[] continueDecisions;

        [DataMember(Order = 18)]
        public int continueMaximumStock;

        [DataMember(Order = 19)]
        public long continueFirstPurchasePrice;

        [DataMember(Order = 20)]
        public long continuePurchasePriceIncrease;

        [DataMember(Order = 21)]
        public int finalWagerShieldCap;

        [DataMember(Order = 22)]
        public long continueOverflowScoreBonus;
    }

    /// <summary>
    /// Run-length recorder for commands supplied to RunManager.Step.
    /// Its value-type run buffer is fully reserved at construction, so every
    /// successful Record call is allocation-free. Export is the allocation
    /// boundary that creates serializer-facing reference types.
    /// </summary>
    public sealed class InputRecorder
    {
        const ulong DeterminismOffsetBasis = 14695981039346656037UL;
        const ulong DeterminismPrime = 1099511628211UL;
        /// <summary>
        /// Lossless worst case: touch analog deltas can differ every tick and
        /// therefore produce one run per tick. Sixty minutes is the supported
        /// default replay envelope at the fixed simulation rate.
        /// </summary>
        public const int DefaultMaximumRecordingMinutes = 60;
        public const int DefaultRunCapacity =
            SimSpace.TicksPerSecond
            * 60
            * DefaultMaximumRecordingMinutes;

        readonly InputRun[] _runs;
        readonly int _difficultyMultiplierNumerator;
        readonly int _difficultyMultiplierDenominator;
        readonly int _finalStageIndex;
        readonly int _roomsPerBiome;
        readonly MissileFamily _missileFamily;
        readonly OptionFormation _optionFormation;
        readonly ColossalBossKind _lastColossalBossAtRunStart;
        readonly List<RouteChoice> _recordedRouteChoices;
        readonly RunManager _routeSource;
        int _runCount;
        int _totalTicks;
        ulong _determinismHash = DeterminismOffsetBasis;

        public InputRecorder()
            : this(
                DefaultRunCapacity,
                1,
                1,
                RunProgressionConfig.DefaultBiomeCount,
                RunProgressionConfig.DefaultRoomsPerBiome)
        {
        }

        public InputRecorder(int runCapacity)
            : this(
                runCapacity,
                1,
                1,
                RunProgressionConfig.DefaultBiomeCount,
                RunProgressionConfig.DefaultRoomsPerBiome)
        {
        }

        public InputRecorder(RunManager run)
            : this(
                DefaultRunCapacity,
                GetDifficultyNumerator(run),
                GetDifficultyDenominator(run),
                GetFinalStageIndex(run),
                GetRoomsPerBiome(run))
        {
            _routeSource = run;
            _missileFamily = run.CurrentMissileFamily;
            _optionFormation = run.CurrentOptionFormation;
            _lastColossalBossAtRunStart =
                run.LastColossalBossAtRunStart;
        }

        public InputRecorder(
            int runCapacity,
            int difficultyMultiplierNumerator,
            int difficultyMultiplierDenominator)
            : this(
                runCapacity,
                difficultyMultiplierNumerator,
                difficultyMultiplierDenominator,
                RunProgressionConfig.DefaultBiomeCount,
                RunProgressionConfig.DefaultRoomsPerBiome)
        {
        }

        public InputRecorder(
            int runCapacity,
            int difficultyMultiplierNumerator,
            int difficultyMultiplierDenominator,
            int finalStageIndex)
            : this(
                runCapacity,
                difficultyMultiplierNumerator,
                difficultyMultiplierDenominator,
                finalStageIndex,
                1)
        {
        }

        public InputRecorder(
            int runCapacity,
            int difficultyMultiplierNumerator,
            int difficultyMultiplierDenominator,
            int biomeCount,
            int roomsPerBiome)
        {
            if (runCapacity < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(runCapacity));
            if (difficultyMultiplierNumerator < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(difficultyMultiplierNumerator));
            if (difficultyMultiplierDenominator < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(difficultyMultiplierDenominator));
            if (biomeCount < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(biomeCount));
            if (roomsPerBiome < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(roomsPerBiome));

            int divisor = GreatestCommonDivisor(
                difficultyMultiplierNumerator,
                difficultyMultiplierDenominator);
            _difficultyMultiplierNumerator =
                difficultyMultiplierNumerator / divisor;
            _difficultyMultiplierDenominator =
                difficultyMultiplierDenominator / divisor;
            _finalStageIndex = biomeCount;
            _roomsPerBiome = roomsPerBiome;
            _missileFamily = MissileFamily.Straight;
            _optionFormation = OptionFormation.Trail;
            _lastColossalBossAtRunStart =
                ColossalBossKind.None;
            _runs = new InputRun[runCapacity];
            _recordedRouteChoices = new List<RouteChoice>();
        }

        public int Capacity => _runs.Length;
        public int RunCount => _runCount;
        public int TotalTicks => _totalTicks;
        /// <summary>
        /// Incremental FNV-1a fold of every recorded command in tick order.
        /// Determinism audits can include the complete sequence in O(1) time.
        /// </summary>
        public ulong DeterminismHash => _determinismHash;
        public int DifficultyMultiplierNumerator =>
            _difficultyMultiplierNumerator;
        public int DifficultyMultiplierDenominator =>
            _difficultyMultiplierDenominator;
        public int FinalStageIndex => _finalStageIndex;
        public int BiomeCount => _finalStageIndex;
        public int RoomsPerBiome => _roomsPerBiome;

        public void Record(in InputCommand input)
        {
            if (_totalTicks == int.MaxValue)
                throw new InvalidOperationException(
                    "An input recording cannot exceed Int32.MaxValue ticks.");

            if (_runCount > 0
                && HasSameCommand(_runs[_runCount - 1], in input))
            {
                _runs[_runCount - 1].TickCount++;
                _totalTicks++;
                FoldDeterminismCommand(in input);
                return;
            }

            if (_runCount == _runs.Length)
            {
                throw new InvalidOperationException(
                    "The input recording run capacity has been exhausted.");
            }
            _runs[_runCount++] = new InputRun(in input, 1);
            _totalTicks++;
            FoldDeterminismCommand(in input);
        }

        /// <summary>
        /// Allocation-free bounded recording. False means a distinct command
        /// would exceed the reserved run capacity; existing identical runs can
        /// continue until the Int32 tick limit.
        /// </summary>
        public bool TryRecord(in InputCommand input)
        {
            if (_totalTicks == int.MaxValue)
                return false;
            if (_runCount > 0
                && HasSameCommand(_runs[_runCount - 1], in input))
            {
                _runs[_runCount - 1].TickCount++;
                _totalTicks++;
                FoldDeterminismCommand(in input);
                return true;
            }
            if (_runCount == _runs.Length)
                return false;

            _runs[_runCount++] = new InputRun(in input, 1);
            _totalTicks++;
            FoldDeterminismCommand(in input);
            return true;
        }

        public RecordedInputRun GetRun(int runIndex)
        {
            if (runIndex < 0 || runIndex >= _runCount)
                throw new ArgumentOutOfRangeException(nameof(runIndex));
            InputRun run = _runs[runIndex];
            return new RecordedInputRun(
                CreateCommand(in run),
                run.TickCount);
        }

        /// <summary>
        /// Serializer-facing copy of the prefix ending at totalTicks. The final
        /// RLE run is truncated when the boundary falls inside that run.
        /// </summary>
        public InputRunData[] ExportRunsPrefix(int totalTicks)
        {
            if (totalTicks < 0 || totalTicks > _totalTicks)
                throw new ArgumentOutOfRangeException(nameof(totalTicks));
            if (totalTicks == 0)
                return Array.Empty<InputRunData>();

            int runCount = 0;
            int remaining = totalTicks;
            while (remaining > 0)
            {
                remaining -= Math.Min(
                    remaining,
                    _runs[runCount].TickCount);
                runCount++;
            }
            var result = new InputRunData[runCount];
            remaining = totalTicks;
            for (int i = 0; i < runCount; i++)
            {
                InputRun run = _runs[i];
                int tickCount = Math.Min(remaining, run.TickCount);
                result[i] = CreateData(in run, tickCount);
                remaining -= tickCount;
            }
            return result;
        }

        /// <summary>
        /// Restores a canonical serialized prefix into this recorder. Intended
        /// for suspend resume before simulation stepping begins.
        /// </summary>
        public void RestoreRuns(InputRunData[] runs, int totalTicks)
        {
            if (runs == null)
                throw new ArgumentNullException(nameof(runs));
            Reset();
            long sum = 0;
            for (int i = 0; i < runs.Length; i++)
            {
                InputRunData run = runs[i]
                    ?? throw new ArgumentException(
                        "Input runs cannot contain null.",
                        nameof(runs));
                if (run.tickCount < 1)
                    throw new ArgumentException(
                        "Input run lengths must be positive.",
                        nameof(runs));
                InputCommand command = CreateCommand(run);
                for (int tick = 0; tick < run.tickCount; tick++)
                {
                    if (!TryRecord(in command))
                        throw new ArgumentException(
                            "Input runs exceed recorder capacity.",
                            nameof(runs));
                }
                sum += run.tickCount;
                if (sum > int.MaxValue)
                    throw new ArgumentException(
                        "Input tick count overflowed.",
                        nameof(runs));
            }
            if (sum != totalTicks)
                throw new ArgumentException(
                    "Input runs do not match totalTicks.",
                    nameof(totalTicks));
        }

        public void Reset()
        {
            _runCount = 0;
            _totalTicks = 0;
            _determinismHash = DeterminismOffsetBasis;
            _recordedRouteChoices.Clear();
        }

        public void RecordRouteChoice(
            int stageIndex,
            int optionIndex,
            in RouteOption option)
        {
            RecordRouteChoice(stageIndex, 1, optionIndex, in option);
        }

        public void RecordRouteChoice(
            int biomeIndex,
            int roomIndex,
            int optionIndex,
            in RouteOption option)
        {
            if (_routeSource != null)
                throw new InvalidOperationException(
                    "A run-bound recorder reads route choices from its run.");
            if (_recordedRouteChoices.Count > 0
                && !IsAfter(
                    biomeIndex,
                    roomIndex,
                    _recordedRouteChoices[
                        _recordedRouteChoices.Count - 1]))
            {
                throw new ArgumentException(
                    "Route choices must be recorded in increasing stage order.",
                    nameof(biomeIndex));
            }
            _recordedRouteChoices.Add(new RouteChoice(
                biomeIndex,
                roomIndex,
                optionIndex,
                option.ThemeId,
                option.EncounterType));
        }

        /// <summary>
        /// Creates an independent serializer-facing copy. Empty recordings are
        /// rejected because they cannot reproduce a run.
        /// </summary>
        public InputRecordingData Export()
        {
            if (_totalTicks == 0)
                throw new InvalidOperationException(
                    "An empty input recording cannot be exported.");

            var exportedRuns = new InputRunData[_runCount];
            for (int i = 0; i < _runCount; i++)
            {
                InputRun run = _runs[i];
                exportedRuns[i] = new InputRunData
                {
                    moveX = run.MoveX,
                    moveY = run.MoveY,
                    fire = run.Fire,
                    activate = run.Activate,
                    activateBomb = run.ActivateBomb,
                    tickCount = run.TickCount,
                    useAnalogMovement = run.UseAnalogMovement,
                    analogDeltaXSubUnits =
                        run.AnalogDeltaXSubUnits,
                    analogDeltaYSubUnits =
                        run.AnalogDeltaYSubUnits
                };
            }

            IReadOnlyList<RouteChoice> routeChoices =
                _routeSource != null
                    ? _routeSource.RouteChoiceHistory
                    : _recordedRouteChoices;
            var exportedRouteChoices =
                new RouteChoiceData[routeChoices.Count];
            for (int i = 0; i < routeChoices.Count; i++)
            {
                RouteChoice choice = routeChoices[i];
                exportedRouteChoices[i] = new RouteChoiceData
                {
                    stageIndex = choice.StageIndex,
                    biomeIndex = choice.BiomeIndex,
                    roomIndex = choice.RoomIndex,
                    optionIndex = choice.OptionIndex,
                    themeId = choice.ThemeId,
                    encounterType = (int)choice.EncounterType
                };
            }
            IReadOnlyList<ContractChoice> contractChoices =
                _routeSource != null
                    ? _routeSource.ContractChoiceHistory
                    : Array.Empty<ContractChoice>();
            var exportedContractChoices =
                new ContractChoiceData[contractChoices.Count];
            for (int i = 0; i < contractChoices.Count; i++)
            {
                ContractChoice choice = contractChoices[i];
                exportedContractChoices[i] =
                    new ContractChoiceData
                    {
                        targetBiomeIndex =
                            choice.TargetBiomeIndex,
                        optionIndex = choice.OptionIndex,
                        contractId = choice.ContractId,
                        destinationKind =
                            (int)choice.DestinationKind,
                        destinationThemeId =
                            choice.DestinationThemeId,
                        destinationThemeStageIndex =
                            choice.DestinationThemeStageIndex
                    };
            }
            IReadOnlyList<RewardDecision> rewardDecisions =
                _routeSource != null
                    ? _routeSource.RewardDecisionHistory
                    : Array.Empty<RewardDecision>();
            var exportedRewardDecisions =
                new RewardDecisionData[rewardDecisions.Count];
            for (int i = 0; i < rewardDecisions.Count; i++)
            {
                RewardDecision decision = rewardDecisions[i];
                exportedRewardDecisions[i] =
                    new RewardDecisionData
                    {
                        rewardSequence =
                            decision.RewardSequence,
                        selectionKind =
                            (int)decision.SelectionKind,
                        decisionKind =
                            (int)decision.DecisionKind,
                        optionIndex =
                            decision.OptionIndex
                    };
            }

            var data = new InputRecordingData
            {
                schemaVersion = InputRecordingData.CurrentSchemaVersion,
                totalTicks = _totalTicks,
                runs = exportedRuns,
                difficultyMultiplierNumerator =
                    _difficultyMultiplierNumerator,
                difficultyMultiplierDenominator =
                    _difficultyMultiplierDenominator,
                routeChoices = exportedRouteChoices,
                finalStageIndex = _finalStageIndex,
                biomeCount = _finalStageIndex,
                roomsPerBiome = _roomsPerBiome,
                missileFamily = (int)_missileFamily,
                optionFormation = (int)_optionFormation,
                lastColossalBossAtRunStart =
                    (int)_lastColossalBossAtRunStart,
                contractChoices = exportedContractChoices,
                rewardDecisions = exportedRewardDecisions,
                initialContinueStock = _routeSource == null
                    ? 0
                    : _routeSource.InitialContinueStockAtRunStart,
                isDailyRun = _routeSource != null
                    && _routeSource.IsDailyRun,
                continueDecisions = ExportContinueDecisions(),
                continueMaximumStock = GetContinueEconomy().MaximumStock,
                continueFirstPurchasePrice =
                    GetContinueEconomy().FirstPurchasePrice,
                continuePurchasePriceIncrease =
                    GetContinueEconomy().PurchasePriceIncrease,
                finalWagerShieldCap =
                    GetContinueEconomy().FinalWagerShieldCap,
                continueOverflowScoreBonus =
                    GetContinueEconomy().OverflowScoreBonus
            };
            SaveDataIntegrity.Seal(data);
            return data;
        }

        ContinueDecisionData[] ExportContinueDecisions()
        {
            IReadOnlyList<ContinueDecision> decisions =
                _routeSource == null
                    ? Array.Empty<ContinueDecision>()
                    : _routeSource.ContinueDecisionHistory;
            var exported = new ContinueDecisionData[decisions.Count];
            for (int i = 0; i < decisions.Count; i++)
            {
                exported[i] = new ContinueDecisionData
                {
                    simulationTick = decisions[i].SimulationTick
                };
            }
            return exported;
        }

        ContinueEconomyConfig GetContinueEconomy()
        {
            return _routeSource == null
                ? ContinueEconomyConfig.CreateDefault()
                : _routeSource.ContinueEconomy;
        }

        static int GetDifficultyNumerator(RunManager run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            return run.DifficultyMultiplierNumerator;
        }

        static int GetDifficultyDenominator(RunManager run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            return run.DifficultyMultiplierDenominator;
        }

        static int GetFinalStageIndex(RunManager run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            return run.FinalStageIndex;
        }

        static int GetRoomsPerBiome(RunManager run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            return run.RoomsPerBiome;
        }

        static bool IsAfter(
            int biomeIndex,
            int roomIndex,
            in RouteChoice previous)
        {
            return biomeIndex > previous.BiomeIndex
                || (biomeIndex == previous.BiomeIndex
                    && roomIndex > previous.RoomIndex);
        }

        static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                int remainder = left % right;
                left = right;
                right = remainder;
            }
            return left;
        }

        static bool HasSameCommand(
            in InputRun run,
            in InputCommand command)
        {
            return run.MoveX == command.MoveX
                && run.MoveY == command.MoveY
                && run.Fire == command.Fire
                && run.Activate == command.Activate
                && run.ActivateBomb == command.ActivateBomb
                && run.UseAnalogMovement
                    == command.UseAnalogMovement
                && run.AnalogDeltaXSubUnits
                    == command.AnalogDeltaXSubUnits
                && run.AnalogDeltaYSubUnits
                    == command.AnalogDeltaYSubUnits;
        }

        static InputCommand CreateCommand(in InputRun run)
        {
            return run.UseAnalogMovement
                ? new InputCommand(
                    run.MoveX,
                    run.MoveY,
                    run.Fire,
                    run.Activate,
                    run.ActivateBomb,
                    run.AnalogDeltaXSubUnits,
                    run.AnalogDeltaYSubUnits)
                : new InputCommand(
                    run.MoveX,
                    run.MoveY,
                    run.Fire,
                    run.Activate,
                    run.ActivateBomb);
        }

        static InputCommand CreateCommand(InputRunData run)
        {
            return run.useAnalogMovement
                ? new InputCommand(
                    run.moveX,
                    run.moveY,
                    run.fire,
                    run.activate,
                    run.activateBomb,
                    run.analogDeltaXSubUnits,
                    run.analogDeltaYSubUnits)
                : new InputCommand(
                    run.moveX,
                    run.moveY,
                    run.fire,
                    run.activate,
                    run.activateBomb);
        }

        static InputRunData CreateData(
            in InputRun run,
            int tickCount)
        {
            return new InputRunData
            {
                moveX = run.MoveX,
                moveY = run.MoveY,
                fire = run.Fire,
                activate = run.Activate,
                activateBomb = run.ActivateBomb,
                tickCount = tickCount,
                useAnalogMovement = run.UseAnalogMovement,
                analogDeltaXSubUnits = run.AnalogDeltaXSubUnits,
                analogDeltaYSubUnits = run.AnalogDeltaYSubUnits
            };
        }

        void FoldDeterminismCommand(in InputCommand command)
        {
            FoldDeterminismInt32(command.MoveX);
            FoldDeterminismInt32(command.MoveY);
            FoldDeterminismByte(command.Fire ? (byte)1 : (byte)0);
            FoldDeterminismByte(command.Activate ? (byte)1 : (byte)0);
            FoldDeterminismByte(command.ActivateBomb ? (byte)1 : (byte)0);
            FoldDeterminismByte(
                command.UseAnalogMovement ? (byte)1 : (byte)0);
            FoldDeterminismInt32(command.AnalogDeltaXSubUnits);
            FoldDeterminismInt32(command.AnalogDeltaYSubUnits);
        }

        void FoldDeterminismInt32(int value)
        {
            unchecked
            {
                uint bits = (uint)value;
                FoldDeterminismByte((byte)bits);
                FoldDeterminismByte((byte)(bits >> 8));
                FoldDeterminismByte((byte)(bits >> 16));
                FoldDeterminismByte((byte)(bits >> 24));
            }
        }

        void FoldDeterminismByte(byte value)
        {
            unchecked
            {
                _determinismHash ^= value;
                _determinismHash *= DeterminismPrime;
            }
        }

        struct InputRun
        {
            public InputRun(in InputCommand command, int tickCount)
            {
                MoveX = command.MoveX;
                MoveY = command.MoveY;
                Fire = command.Fire;
                Activate = command.Activate;
                ActivateBomb = command.ActivateBomb;
                UseAnalogMovement = command.UseAnalogMovement;
                AnalogDeltaXSubUnits =
                    command.AnalogDeltaXSubUnits;
                AnalogDeltaYSubUnits =
                    command.AnalogDeltaYSubUnits;
                TickCount = tickCount;
            }

            public int MoveX;
            public int MoveY;
            public bool Fire;
            public bool Activate;
            public bool ActivateBomb;
            public bool UseAnalogMovement;
            public int AnalogDeltaXSubUnits;
            public int AnalogDeltaYSubUnits;
            public int TickCount;
        }
    }

    /// <summary>
    /// Validates and snapshots an input recording, then enumerates exactly one
    /// InputCommand per recorded tick.
    /// </summary>
    public sealed class InputPlayback : IEnumerable<InputCommand>
    {
        readonly PlaybackRun[] _runs;
        readonly RouteChoice[] _routeChoices;
        readonly IReadOnlyList<RouteChoice> _routeChoiceView;
        readonly ContractChoice[] _contractChoices;
        readonly IReadOnlyList<ContractChoice>
            _contractChoiceView;
        readonly RewardDecision[] _rewardDecisions;
        readonly IReadOnlyList<RewardDecision>
            _rewardDecisionView;
        readonly ContinueDecision[] _continueDecisions;
        readonly IReadOnlyList<ContinueDecision>
            _continueDecisionView;

        public InputPlayback(InputRecordingData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion
                != InputRecordingData.CurrentSchemaVersion)
            {
                throw new ArgumentException(
                    "The input recording was captured by an incompatible "
                    + "simulation version.",
                    nameof(data));
            }
            data = SaveDataIntegrity.MigrateAndValidate(data);
            Validate(data);

            TotalTicks = data.totalTicks;
            int divisor = GreatestCommonDivisor(
                data.difficultyMultiplierNumerator,
                data.difficultyMultiplierDenominator);
            DifficultyMultiplierNumerator =
                data.difficultyMultiplierNumerator / divisor;
            DifficultyMultiplierDenominator =
                data.difficultyMultiplierDenominator / divisor;
            BiomeCount = data.biomeCount;
            RoomsPerBiome = data.roomsPerBiome;
            MissileFamily =
                (MissileFamily)data.missileFamily;
            OptionFormation =
                (OptionFormation)data.optionFormation;
            LastColossalBossAtRunStart =
                (ColossalBossKind)data.lastColossalBossAtRunStart;
            _runs = new PlaybackRun[data.runs.Length];
            for (int i = 0; i < data.runs.Length; i++)
            {
                InputRunData run = data.runs[i];
                InputCommand command = run.useAnalogMovement
                    ? new InputCommand(
                        run.moveX,
                        run.moveY,
                        run.fire,
                        run.activate,
                        run.activateBomb,
                        run.analogDeltaXSubUnits,
                        run.analogDeltaYSubUnits)
                    : new InputCommand(
                        run.moveX,
                        run.moveY,
                        run.fire,
                        run.activate,
                        run.activateBomb);
                _runs[i] = new PlaybackRun(
                    command,
                    run.tickCount);
            }
            RouteChoiceData[] serializedChoices =
                data.routeChoices ?? Array.Empty<RouteChoiceData>();
            _routeChoices = new RouteChoice[serializedChoices.Length];
            for (int i = 0; i < serializedChoices.Length; i++)
            {
                RouteChoiceData choice = serializedChoices[i];
                _routeChoices[i] = new RouteChoice(
                    choice.biomeIndex,
                    choice.roomIndex,
                    choice.optionIndex,
                    choice.themeId,
                    (EncounterType)choice.encounterType);
            }
            _routeChoiceView = Array.AsReadOnly(_routeChoices);
            ContractChoiceData[] serializedContracts =
                data.contractChoices
                ?? Array.Empty<ContractChoiceData>();
            _contractChoices =
                new ContractChoice[serializedContracts.Length];
            for (int i = 0; i < _contractChoices.Length; i++)
            {
                ContractChoiceData choice =
                    serializedContracts[i];
                _contractChoices[i] = new ContractChoice(
                    choice.targetBiomeIndex,
                    choice.optionIndex,
                    choice.contractId,
                    (ContractDestinationKind)
                        choice.destinationKind,
                    choice.destinationThemeId,
                    choice.destinationThemeStageIndex);
            }
            _contractChoiceView =
                Array.AsReadOnly(_contractChoices);
            RewardDecisionData[] serializedDecisions =
                data.rewardDecisions
                ?? Array.Empty<RewardDecisionData>();
            _rewardDecisions =
                new RewardDecision[serializedDecisions.Length];
            for (int i = 0; i < _rewardDecisions.Length; i++)
            {
                RewardDecisionData decision =
                    serializedDecisions[i];
                _rewardDecisions[i] = new RewardDecision(
                    decision.rewardSequence,
                    (RewardSelectionKind)
                        decision.selectionKind,
                    (RewardDecisionKind)
                        decision.decisionKind,
                    decision.optionIndex);
            }
            _rewardDecisionView =
                Array.AsReadOnly(_rewardDecisions);
            InitialContinueStock = data.initialContinueStock;
            IsDailyRun = data.isDailyRun;
            ContinueEconomy = new ContinueEconomyConfig(
                data.continueMaximumStock,
                data.continueFirstPurchasePrice,
                data.continuePurchasePriceIncrease,
                data.finalWagerShieldCap,
                data.continueOverflowScoreBonus);
            ContinueDecisionData[] serializedContinues =
                data.continueDecisions
                ?? Array.Empty<ContinueDecisionData>();
            _continueDecisions =
                new ContinueDecision[serializedContinues.Length];
            for (int i = 0; i < _continueDecisions.Length; i++)
            {
                _continueDecisions[i] = new ContinueDecision(
                    serializedContinues[i].simulationTick);
            }
            _continueDecisionView =
                Array.AsReadOnly(_continueDecisions);
        }

        public int TotalTicks { get; }
        public int RunCount => _runs.Length;
        public int DifficultyMultiplierNumerator { get; }
        public int DifficultyMultiplierDenominator { get; }
        public int FinalStageIndex => BiomeCount;
        public int BiomeCount { get; }
        public int RoomsPerBiome { get; }
        public MissileFamily MissileFamily { get; }
        public OptionFormation OptionFormation { get; }
        public ColossalBossKind LastColossalBossAtRunStart { get; }
        public int InitialContinueStock { get; }
        public bool IsDailyRun { get; }
        public ContinueEconomyConfig ContinueEconomy { get; }
        public IReadOnlyList<ContinueDecision> ContinueDecisions =>
            _continueDecisionView;
        public IReadOnlyList<RouteChoice> RouteChoices =>
            _routeChoiceView;
        public IReadOnlyList<ContractChoice> ContractChoices =>
            _contractChoiceView;
        public IReadOnlyList<RewardDecision> RewardDecisions =>
            _rewardDecisionView;

        public RunConfig CreateRunConfig(
            int startStageIndex = RunConfig.DefaultStartStageIndex)
        {
            return new RunConfig(
                startStageIndex,
                IsDailyRun,
                InitialContinueStock,
                ContinueEconomy);
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_runs);
        }

        IEnumerator<InputCommand>
            IEnumerable<InputCommand>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        static void Validate(InputRecordingData data)
        {
            if (data.schemaVersion
                != InputRecordingData.CurrentSchemaVersion)
            {
                throw Corrupted(
                    "The input recording schema version is unsupported.");
            }
            if (data.difficultyMultiplierNumerator < 1
                || data.difficultyMultiplierDenominator < 1)
            {
                throw Corrupted(
                    "The input recording difficulty multiplier "
                    + "must be positive.");
            }
            if (data.biomeCount < 1
                || data.finalStageIndex != data.biomeCount)
                throw Corrupted(
                    "The input recording biome count is invalid.");
            if (data.roomsPerBiome < 1)
                throw Corrupted(
                    "The input recording roomsPerBiome must be positive.");
            if (!Enum.IsDefined(
                    typeof(MissileFamily),
                    data.missileFamily)
                || !Enum.IsDefined(
                    typeof(OptionFormation),
                    data.optionFormation)
                || !Enum.IsDefined(
                    typeof(ColossalBossKind),
                    data.lastColossalBossAtRunStart))
                throw Corrupted(
                    "The input recording weapon loadout is invalid.");
            if (data.totalTicks < 1)
                throw Corrupted(
                    "The input recording must contain at least one tick.");
            if (data.runs == null || data.runs.Length == 0)
                throw Corrupted(
                    "The input recording must contain at least one run.");
            if (data.initialContinueStock < 0
                || data.initialContinueStock
                    > data.continueMaximumStock)
                throw Corrupted(
                    "The input recording continue stock is invalid.");
            ValidateContinueEconomy(data);
            ValidateContinueDecisions(
                data.continueDecisions,
                data.totalTicks,
                data.initialContinueStock,
                data.isDailyRun);
            if (data.runs.Length > data.totalTicks)
                throw Corrupted(
                    "The input recording has more runs than ticks.");

            long tickSum = 0;
            InputRunData previous = null;
            for (int i = 0; i < data.runs.Length; i++)
            {
                InputRunData run = data.runs[i];
                if (run == null)
                    throw Corrupted(
                        "Input recording runs cannot contain null.");
                if (run.moveX < -1 || run.moveX > 1
                    || run.moveY < -1 || run.moveY > 1)
                {
                    throw Corrupted(
                        "Input recording movement must be digital.");
                }
                if (!run.useAnalogMovement
                    && (run.analogDeltaXSubUnits != 0
                        || run.analogDeltaYSubUnits != 0))
                {
                    throw Corrupted(
                        "Input recording analog deltas require analog mode.");
                }
                if (run.tickCount < 1)
                    throw Corrupted(
                        "Input recording run lengths must be positive.");
                if (previous != null
                    && previous.moveX == run.moveX
                    && previous.moveY == run.moveY
                    && previous.fire == run.fire
                    && previous.activate == run.activate
                    && previous.activateBomb == run.activateBomb
                    && previous.useAnalogMovement
                        == run.useAnalogMovement
                    && previous.analogDeltaXSubUnits
                        == run.analogDeltaXSubUnits
                    && previous.analogDeltaYSubUnits
                        == run.analogDeltaYSubUnits)
                {
                    throw Corrupted(
                        "Adjacent identical input runs are not canonical.");
                }

                tickSum += run.tickCount;
                if (tickSum > int.MaxValue)
                    throw Corrupted(
                        "The input recording tick count overflowed.");
                previous = run;
            }

            if (tickSum != data.totalTicks)
                throw Corrupted(
                    "Input recording run lengths do not match totalTicks.");

            if (data.routeChoices == null)
            {
                throw Corrupted(
                    "Input recording routeChoices cannot be null.");
            }
            RouteChoiceData[] choices =
                data.routeChoices ?? Array.Empty<RouteChoiceData>();
            int previousBiomeIndex = 0;
            int previousRoomIndex = 0;
            for (int i = 0; i < choices.Length; i++)
            {
                RouteChoiceData choice = choices[i];
                if (choice == null
                    || choice.biomeIndex < 1
                    || choice.biomeIndex > data.biomeCount
                    || choice.roomIndex < 1
                    || choice.roomIndex > data.roomsPerBiome
                    || (choice.biomeIndex < previousBiomeIndex)
                    || (choice.biomeIndex == previousBiomeIndex
                        && choice.roomIndex <= previousRoomIndex)
                    || choice.stageIndex != choice.biomeIndex
                    || choice.optionIndex < 0
                    || choice.optionIndex
                        >= RunManager.MaximumRouteOptionCount
                    || string.IsNullOrEmpty(choice.themeId)
                    || !Enum.IsDefined(
                        typeof(EncounterType),
                        choice.encounterType))
                {
                    throw Corrupted(
                        "Input recording route choice history is invalid.");
                }
                previousBiomeIndex = choice.biomeIndex;
                previousRoomIndex = choice.roomIndex;
            }
            if (data.contractChoices == null)
                throw Corrupted(
                    "Input recording contractChoices cannot be null.");
            int previousContractBiome = 1;
            for (int i = 0;
                i < data.contractChoices.Length;
                i++)
            {
                ContractChoiceData choice =
                    data.contractChoices[i];
                if (choice == null
                    || choice.targetBiomeIndex
                        <= previousContractBiome
                    || choice.targetBiomeIndex
                        > data.biomeCount + 1
                    || choice.optionIndex < 0
                    || choice.optionIndex
                        >= RunManager.MaximumContractOptionCount
                    || string.IsNullOrEmpty(choice.contractId)
                    || !Enum.IsDefined(
                        typeof(ContractDestinationKind),
                        choice.destinationKind)
                    || (choice.targetBiomeIndex
                            == data.biomeCount + 1
                        && choice.destinationKind
                            == (int)ContractDestinationKind.NextStage)
                    || (choice.targetBiomeIndex
                            <= data.biomeCount
                        && choice.destinationKind
                            != (int)ContractDestinationKind.NextStage)
                    || (choice.destinationKind
                            == (int)ContractDestinationKind.NextStage
                        && (string.IsNullOrEmpty(
                                choice.destinationThemeId)
                            || choice.destinationThemeStageIndex < 1)))
                {
                    throw Corrupted(
                        "Input recording contract choice history is invalid.");
                }
                previousContractBiome =
                    choice.targetBiomeIndex;
            }
            if (data.rewardDecisions == null)
                throw Corrupted(
                    "Input recording rewardDecisions cannot be null.");
            int previousRewardSequence = 0;
            bool sequenceSelected = false;
            for (int i = 0;
                i < data.rewardDecisions.Length;
                i++)
            {
                RewardDecisionData decision =
                    data.rewardDecisions[i];
                if (decision == null
                    || decision.rewardSequence < 1
                    || decision.rewardSequence
                        < previousRewardSequence
                    || !Enum.IsDefined(
                        typeof(RewardSelectionKind),
                        decision.selectionKind)
                    || decision.selectionKind
                        == (int)RewardSelectionKind.None
                    || !Enum.IsDefined(
                        typeof(RewardDecisionKind),
                        decision.decisionKind))
                    throw Corrupted(
                        "Input recording reward decision history is invalid.");
                if (decision.rewardSequence
                    != previousRewardSequence)
                {
                    sequenceSelected = false;
                    previousRewardSequence =
                        decision.rewardSequence;
                }
                if (sequenceSelected)
                    throw Corrupted(
                        "Input recording reward decisions occur after selection.");
                if (decision.decisionKind
                    == (int)RewardDecisionKind.Reroll)
                {
                    if (decision.optionIndex != -1)
                        throw Corrupted(
                            "Input recording reroll option index is invalid.");
                }
                else
                {
                    if (decision.optionIndex < 0
                        || decision.optionIndex
                            >= RunManager.MainRewardOptionCount)
                        throw Corrupted(
                            "Input recording reward option index is invalid.");
                    sequenceSelected = true;
                }
            }
        }

        static void ValidateContinueDecisions(
            ContinueDecisionData[] decisions,
            int totalTicks,
            int initialStock,
            bool isDailyRun)
        {
            if (decisions == null)
                throw Corrupted(
                    "Input recording continue decisions cannot be null.");
            if (isDailyRun && decisions.Length != 0)
                throw Corrupted(
                    "Daily recordings cannot contain continue decisions.");
            if (decisions.Length > initialStock)
                throw Corrupted(
                    "Input recording uses more continues than its initial stock.");
            int previousTick = 0;
            for (int i = 0; i < decisions.Length; i++)
            {
                ContinueDecisionData decision = decisions[i];
                if (decision == null
                    || decision.simulationTick <= previousTick
                    || decision.simulationTick > totalTicks)
                    throw Corrupted(
                        "Input recording continue decisions are invalid.");
                previousTick = decision.simulationTick;
            }
        }

        static void ValidateContinueEconomy(InputRecordingData data)
        {
            try
            {
                _ = new ContinueEconomyConfig(
                    data.continueMaximumStock,
                    data.continueFirstPurchasePrice,
                    data.continuePurchasePriceIncrease,
                    data.finalWagerShieldCap,
                    data.continueOverflowScoreBonus);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw Corrupted(
                    "The input recording continue economy is invalid.");
            }
        }

        static ArgumentException Corrupted(string message)
        {
            return new ArgumentException(message, "data");
        }

        static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                int remainder = left % right;
                left = right;
                right = remainder;
            }
            return left;
        }

        internal readonly struct PlaybackRun
        {
            public PlaybackRun(
                InputCommand command,
                int tickCount)
            {
                Command = command;
                TickCount = tickCount;
            }

            public InputCommand Command { get; }
            public int TickCount { get; }
        }

        public struct Enumerator : IEnumerator<InputCommand>
        {
            readonly PlaybackRun[] _runs;
            int _runIndex;
            int _remainingTicks;
            InputCommand _current;

            internal Enumerator(PlaybackRun[] runs)
            {
                _runs = runs;
                _runIndex = -1;
                _remainingTicks = 0;
                _current = default;
            }

            public InputCommand Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_remainingTicks > 0)
                {
                    _remainingTicks--;
                    return true;
                }

                int nextRun = _runIndex + 1;
                if (nextRun >= _runs.Length)
                    return false;

                _runIndex = nextRun;
                PlaybackRun run = _runs[nextRun];
                _current = run.Command;
                _remainingTicks = run.TickCount - 1;
                return true;
            }

            public void Reset()
            {
                _runIndex = -1;
                _remainingTicks = 0;
                _current = default;
            }

            public void Dispose()
            {
            }
        }
    }
}

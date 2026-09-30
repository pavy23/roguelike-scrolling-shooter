using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Shmup.Core.Generation
{
    /// <summary>
    /// Immutable generation data normally populated from GameData/waves.json.
    /// Array order is significant and is preserved during deterministic selection.
    /// </summary>
    public sealed class StageGenerationCatalog
    {
        // REQ-127 measured baselines after the pending content clamp/count pass:
        // 39.5 s opening and 65.7 s closing at 60 deterministic ticks/second.
        // Content may override both without changing Core generation rules.
        public const int DefaultTargetDurationTicks = 2370;
        public const int DefaultClosingTargetDurationTicks = 3942;

        public StageGenerationCatalog(
            int laneCount,
            int segmentsPerStage,
            int startLaneMask,
            IReadOnlyList<StageSegmentTemplate> segments,
            IReadOnlyList<StageBossTemplate> bosses)
            : this(
                laneCount,
                segmentsPerStage,
                startLaneMask,
                segments,
                bosses,
                null)
        {
        }

        public StageGenerationCatalog(
            int laneCount,
            int segmentsPerStage,
            int startLaneMask,
            IReadOnlyList<StageSegmentTemplate> segments,
            IReadOnlyList<StageBossTemplate> bosses,
            IReadOnlyList<string> themeIds,
            IReadOnlyList<StageGimmickDefinition> gimmicks = null,
            int? closingSegmentsPerStage = null,
            int? targetDurationTicks = null,
            int? closingTargetDurationTicks = null,
            IReadOnlyList<int> stagePowerCurvePermille = null)
        {
            if (laneCount < 1 || laneCount > 30)
                throw new ArgumentOutOfRangeException(nameof(laneCount));
            if (segmentsPerStage < 1)
                throw new ArgumentOutOfRangeException(nameof(segmentsPerStage));
            if (closingSegmentsPerStage.HasValue
                && closingSegmentsPerStage.Value < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(closingSegmentsPerStage));
            if (targetDurationTicks.HasValue
                && targetDurationTicks.Value < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(targetDurationTicks));
            if (closingTargetDurationTicks.HasValue
                && closingTargetDurationTicks.Value < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(closingTargetDurationTicks));

            int validLanes = StagePlanClearability.GetValidLaneMask(laneCount);
            StagePlanClearability.ValidateLaneMask(
                startLaneMask, validLanes, nameof(startLaneMask));

            LaneCount = laneCount;
            SegmentsPerStage = segmentsPerStage;
            ClosingSegmentsPerStage =
                closingSegmentsPerStage ?? segmentsPerStage;
            TargetDurationTicks = targetDurationTicks
                ?? DefaultTargetDurationTicks;
            ClosingTargetDurationTicks = closingTargetDurationTicks
                ?? DefaultClosingTargetDurationTicks;
            StartLaneMask = startLaneMask;
            Segments = CopySegments(segments, validLanes);
            Bosses = CopyBosses(bosses, validLanes);

            if (Segments.Count == 0)
                throw new ArgumentException("At least one segment is required.", nameof(segments));
            if (Bosses.Count == 0)
                throw new ArgumentException("At least one boss is required.", nameof(bosses));

            ThemeIds = themeIds == null
                ? CollectThemeIds(Segments, Bosses)
                : CopyExplicitThemeIds(themeIds, Segments, Bosses);
            Gimmicks = CopyGimmicks(gimmicks, ThemeIds);
            StagePowerCurvePermille =
                CopyStagePowerCurve(stagePowerCurvePermille);
        }

        static IReadOnlyList<int> CopyStagePowerCurve(
            IReadOnlyList<int> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<int>();
            var copy = new int[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                if (source[i] < 1)
                    throw new ArgumentOutOfRangeException(
                        nameof(source),
                        "Stage power curve permille values must be positive.");
                copy[i] = source[i];
            }
            return copy;
        }

        public int LaneCount { get; }
        public int SegmentsPerStage { get; }
        public int ClosingSegmentsPerStage { get; }
        /// <summary>
        /// Normal-section duration target. SegmentsPerStage is a safety upper
        /// bound and fallback when the catalog cannot reach this duration.
        /// </summary>
        public int? TargetDurationTicks { get; }
        /// <summary>
        /// Closing-section duration target. ClosingSegmentsPerStage is a safety
        /// upper bound and fallback when the catalog cannot reach this duration.
        /// </summary>
        public int? ClosingTargetDurationTicks { get; }
        public int StartLaneMask { get; }
        public IReadOnlyList<StageSegmentTemplate> Segments { get; }
        public IReadOnlyList<StageBossTemplate> Bosses { get; }
        public IReadOnlyList<string> ThemeIds { get; }
        public IReadOnlyList<StageGimmickDefinition> Gimmicks { get; }

        /// <summary>
        /// 플레이어 파워 커브 (스테이지 1..N, 퍼밀). 테마 셔플로 보스가 홈
        /// 스테이지 밖에 등장할 때 HP를 curve[등장]/curve[홈] 비율로 맞춘다
        /// (REQ-187, 셔플 1안). 비어 있으면 스케일 없음 — 옵셔널 확장이라
        /// 기존 데이터의 파싱·생성 결과는 변하지 않는다.
        /// </summary>
        public IReadOnlyList<int> StagePowerCurvePermille { get; }

        public StageGimmickDefinition FindGimmick(string themeId)
        {
            for (int i = 0; i < Gimmicks.Count; i++)
                if (string.Equals(
                        Gimmicks[i].ThemeId,
                        themeId,
                        StringComparison.Ordinal))
                    return Gimmicks[i];
            return StageGimmickDefinition.None;
        }

        static IReadOnlyList<StageGimmickDefinition> CopyGimmicks(
            IReadOnlyList<StageGimmickDefinition> source,
            IReadOnlyList<string> themeIds)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<StageGimmickDefinition>();
            var copy = new StageGimmickDefinition[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                StageGimmickDefinition gimmick = source[i]
                    ?? throw new ArgumentException(
                        "Stage gimmicks cannot contain null.",
                        nameof(source));
                bool themeFound = false;
                for (int theme = 0; theme < themeIds.Count; theme++)
                    if (string.Equals(
                            themeIds[theme],
                            gimmick.ThemeId,
                            StringComparison.Ordinal))
                    {
                        themeFound = true;
                        break;
                    }
                if (!themeFound)
                    throw new ArgumentException(
                        $"Gimmick theme '{gimmick.ThemeId}' is not registered.",
                        nameof(source));
                for (int earlier = 0; earlier < i; earlier++)
                    if (string.Equals(
                            copy[earlier].ThemeId,
                            gimmick.ThemeId,
                            StringComparison.Ordinal))
                        throw new ArgumentException(
                            $"Gimmick theme '{gimmick.ThemeId}' is duplicated.",
                            nameof(source));
                copy[i] = gimmick;
            }
            return new ReadOnlyCollection<StageGimmickDefinition>(copy);
        }

        static IReadOnlyList<string> CollectThemeIds(
            IReadOnlyList<StageSegmentTemplate> segments,
            IReadOnlyList<StageBossTemplate> bosses)
        {
            var themes = new List<string>();
            for (int i = 0; i < segments.Count; i++)
                AddTheme(themes, segments[i].ThemeId);
            for (int i = 0; i < bosses.Count; i++)
                AddTheme(themes, bosses[i].ThemeId);

            themes.Sort(StringComparer.Ordinal);
            return new ReadOnlyCollection<string>(themes);
        }

        static IReadOnlyList<string> CopyExplicitThemeIds(
            IReadOnlyList<string> source,
            IReadOnlyList<StageSegmentTemplate> segments,
            IReadOnlyList<StageBossTemplate> bosses)
        {
            var copy = new string[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                string themeId = source[i];
                if (string.IsNullOrEmpty(themeId))
                    throw new ArgumentException(
                        "Theme ids cannot be null or empty.",
                        nameof(source));
                if (ContainsTheme(copy, i, themeId))
                    throw new ArgumentException(
                        $"Theme id '{themeId}' is duplicated.",
                        nameof(source));
                if (!CatalogContainsTheme(segments, bosses, themeId))
                    throw new ArgumentException(
                        $"Theme id '{themeId}' is not registered by a segment or boss.",
                        nameof(source));
                copy[i] = themeId;
            }

            for (int i = 0; i < segments.Count; i++)
                EnsureExplicitThemeContains(copy, segments[i].ThemeId);
            for (int i = 0; i < bosses.Count; i++)
                EnsureExplicitThemeContains(copy, bosses[i].ThemeId);
            return new ReadOnlyCollection<string>(copy);
        }

        static bool CatalogContainsTheme(
            IReadOnlyList<StageSegmentTemplate> segments,
            IReadOnlyList<StageBossTemplate> bosses,
            string themeId)
        {
            for (int i = 0; i < segments.Count; i++)
                if (string.Equals(
                        segments[i].ThemeId,
                        themeId,
                        StringComparison.Ordinal))
                    return true;
            for (int i = 0; i < bosses.Count; i++)
                if (string.Equals(
                        bosses[i].ThemeId,
                        themeId,
                        StringComparison.Ordinal))
                    return true;
            return false;
        }

        static void EnsureExplicitThemeContains(string[] themes, string themeId)
        {
            if (themeId == null)
                return;
            if (!ContainsTheme(themes, themes.Length, themeId))
                throw new ArgumentException(
                    $"Registered theme id '{themeId}' is missing from the explicit order.",
                    nameof(themes));
        }

        static bool ContainsTheme(string[] themes, int count, string themeId)
        {
            for (int i = 0; i < count; i++)
                if (string.Equals(themes[i], themeId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        static void AddTheme(List<string> themes, string themeId)
        {
            if (themeId == null)
                return;
            for (int i = 0; i < themes.Count; i++)
                if (string.Equals(themes[i], themeId, StringComparison.Ordinal))
                    return;
            themes.Add(themeId);
        }

        static IReadOnlyList<StageSegmentTemplate> CopySegments(
            IReadOnlyList<StageSegmentTemplate> source,
            int validLanes)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new StageSegmentTemplate[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                StageSegmentTemplate segment = source[i] ?? throw new ArgumentException(
                    "Segments cannot contain null.", nameof(source));
                segment.Validate(validLanes);
                copy[i] = segment;
            }
            return new ReadOnlyCollection<StageSegmentTemplate>(copy);
        }

        static IReadOnlyList<StageBossTemplate> CopyBosses(
            IReadOnlyList<StageBossTemplate> source,
            int validLanes)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new StageBossTemplate[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                StageBossTemplate boss = source[i] ?? throw new ArgumentException(
                    "Bosses cannot contain null.", nameof(source));
                boss.Validate(validLanes);
                copy[i] = boss;
            }
            return new ReadOnlyCollection<StageBossTemplate>(copy);
        }
    }

    public sealed class StageSegmentTemplate
    {
        public const int DefaultWeight = 10;
        readonly IReadOnlyList<MidbossOutcomeKind> _postMidbossOutcomes;

        public StageSegmentTemplate(
            string segmentId,
            int difficultyMin,
            int difficultyMax,
            int lengthTicks,
            int entryLaneMask,
            int exitLaneMask,
            IReadOnlyList<int> traversableLaneMasks,
            IReadOnlyList<SpawnEvent> spawns)
            : this(
                segmentId,
                difficultyMin,
                difficultyMax,
                lengthTicks,
                entryLaneMask,
                exitLaneMask,
                traversableLaneMasks,
                spawns,
                null)
        {
        }

        public StageSegmentTemplate(
            string segmentId,
            int difficultyMin,
            int difficultyMax,
            int lengthTicks,
            int entryLaneMask,
            int exitLaneMask,
            IReadOnlyList<int> traversableLaneMasks,
            IReadOnlyList<SpawnEvent> spawns,
            string themeId)
            : this(
                segmentId,
                difficultyMin,
                difficultyMax,
                lengthTicks,
                entryLaneMask,
                exitLaneMask,
                traversableLaneMasks,
                spawns,
                Array.Empty<ObstacleSpawn>(),
                themeId)
        {
        }

        public StageSegmentTemplate(
            string segmentId,
            int difficultyMin,
            int difficultyMax,
            int lengthTicks,
            int entryLaneMask,
            int exitLaneMask,
            IReadOnlyList<int> traversableLaneMasks,
            IReadOnlyList<SpawnEvent> spawns,
            IReadOnlyList<ObstacleSpawn> obstacles,
            string themeId)
            : this(
                segmentId,
                difficultyMin,
                difficultyMax,
                lengthTicks,
                entryLaneMask,
                exitLaneMask,
                traversableLaneMasks,
                spawns,
                obstacles,
                themeId,
                DefaultWeight)
        {
        }

        public StageSegmentTemplate(
            string segmentId,
            int difficultyMin,
            int difficultyMax,
            int lengthTicks,
            int entryLaneMask,
            int exitLaneMask,
            IReadOnlyList<int> traversableLaneMasks,
            IReadOnlyList<SpawnEvent> spawns,
            IReadOnlyList<ObstacleSpawn> obstacles,
            string themeId,
            int weight,
            SegmentEnvironmentDefinition environment = null,
            IReadOnlyList<MidbossOutcomeKind> postMidbossOutcomes = null,
            int scrollSpeedMultiplierNumerator = 1,
            int scrollSpeedMultiplierDenominator = 1)
        {
            if (scrollSpeedMultiplierNumerator < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(scrollSpeedMultiplierNumerator));
            if (scrollSpeedMultiplierDenominator < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(scrollSpeedMultiplierDenominator));
            SegmentId = segmentId ?? throw new ArgumentNullException(nameof(segmentId));
            DifficultyMin = difficultyMin;
            DifficultyMax = difficultyMax;
            LengthTicks = lengthTicks;
            EntryLaneMask = entryLaneMask;
            ExitLaneMask = exitLaneMask;
            TraversableLaneMasks = CopyMasks(traversableLaneMasks);
            Spawns = CopySpawns(spawns);
            Obstacles = CopyObstacles(obstacles);
            ThemeId = themeId;
            Weight = weight;
            Environment =
                environment ?? SegmentEnvironmentDefinition.None;
            _postMidbossOutcomes =
                CopyOutcomes(postMidbossOutcomes);
            ScrollSpeedMultiplierNumerator =
                scrollSpeedMultiplierNumerator;
            ScrollSpeedMultiplierDenominator =
                scrollSpeedMultiplierDenominator;
        }

        public string SegmentId { get; }
        public int DifficultyMin { get; }
        public int DifficultyMax { get; }
        public int LengthTicks { get; }
        public int EntryLaneMask { get; }
        public int ExitLaneMask { get; }
        public IReadOnlyList<int> TraversableLaneMasks { get; }
        public IReadOnlyList<SpawnEvent> Spawns { get; }
        public IReadOnlyList<ObstacleSpawn> Obstacles { get; }
        public string ThemeId { get; }
        public int Weight { get; }
        public SegmentEnvironmentDefinition Environment { get; }
        public IReadOnlyList<MidbossOutcomeKind> PostMidbossOutcomes =>
            _postMidbossOutcomes;
        public int ScrollSpeedMultiplierNumerator { get; }
        public int ScrollSpeedMultiplierDenominator { get; }

        internal bool SupportsDifficulty(int difficulty)
        {
            return difficulty >= DifficultyMin && difficulty <= DifficultyMax;
        }

        internal bool SupportsTheme(string themeId)
        {
            return ThemeId == null
                || string.Equals(ThemeId, themeId, StringComparison.Ordinal);
        }

        internal bool SupportsMidbossOutcome(MidbossOutcomeKind outcome)
        {
            if (_postMidbossOutcomes.Count == 0)
                return outcome == MidbossOutcomeKind.Default;
            for (int i = 0; i < _postMidbossOutcomes.Count; i++)
                if (_postMidbossOutcomes[i] == outcome)
                    return true;
            return false;
        }

        internal void Validate(int validLanes)
        {
            if (SegmentId.Length == 0)
                throw new ArgumentException("Segment id cannot be empty.");
            if (ThemeId != null && ThemeId.Length == 0)
                throw new ArgumentException("Segment theme id cannot be empty.");
            if (DifficultyMin < 1 || DifficultyMax < DifficultyMin)
                throw new ArgumentException("Segment difficulty range is invalid.");
            if (LengthTicks < 1)
                throw new ArgumentException("Segment length must be positive.");
            if (Weight < 1)
                throw new ArgumentException("Segment weight must be positive.");

            StagePlanClearability.ValidateLaneMask(
                EntryLaneMask, validLanes, nameof(EntryLaneMask));
            StagePlanClearability.ValidateLaneMask(
                ExitLaneMask, validLanes, nameof(ExitLaneMask));

            if (TraversableLaneMasks.Count == 0)
                throw new ArgumentException("A segment needs at least one traversal checkpoint.");
            for (int i = 0; i < TraversableLaneMasks.Count; i++)
                StagePlanClearability.ValidateLaneMask(
                    TraversableLaneMasks[i], validLanes, nameof(TraversableLaneMasks));

            if (StagePlanClearability.Advance(
                    EntryLaneMask, this, validLanes) == 0)
                throw new ArgumentException(
                    "Segment has no traversable path from its entry to its exit.");

            for (int i = 0; i < Spawns.Count; i++)
            {
                if (Spawns[i].Tick >= LengthTicks)
                    throw new ArgumentException(
                        "Spawn ticks must be earlier than the segment length.");
            }
        }

        internal StageSegment CreateSegment(Rng obstacleJitterRng)
        {
            if (obstacleJitterRng == null)
                throw new ArgumentNullException(nameof(obstacleJitterRng));
            return new StageSegment(
                SegmentId,
                LengthTicks,
                Spawns,
                EntryLaneMask,
                ExitLaneMask,
                TraversableLaneMasks,
                JitterObstacles(obstacleJitterRng),
                Environment,
                ScrollSpeedMultiplierNumerator,
                ScrollSpeedMultiplierDenominator);
        }

        IReadOnlyList<ObstacleSpawn> JitterObstacles(Rng rng)
        {
            if (Obstacles.Count == 0)
                return Obstacles;

            var jittered = new ObstacleSpawn[Obstacles.Count];
            for (int i = 0; i < jittered.Length; i++)
            {
                ObstacleSpawn source = Obstacles[i];
                int magnitude = source.Type == ObstacleType.Breakable
                    ? SegmentStageGenerator.BreakableJitterSubUnits
                    : source.Type == ObstacleType.Solid
                        ? SegmentStageGenerator.SolidJitterSubUnits
                        : 0;
                int offsetY = magnitude == 0
                    ? 0
                    : rng.Fork(i).NextInt(-magnitude, magnitude + 1);
                jittered[i] = new ObstacleSpawn(
                    source.Type,
                    source.X,
                    AddClamped(source.Y, offsetY),
                    source.Hp,
                    source.LaserAttack,
                    source.BlocksEnemyBullets,
                    source.RegenDelayTicks);
            }
            return jittered;
        }

        static int AddClamped(int value, int delta)
        {
            long sum = (long)value + delta;
            if (sum < int.MinValue)
                return int.MinValue;
            if (sum > int.MaxValue)
                return int.MaxValue;
            return (int)sum;
        }

        static IReadOnlyList<int> CopyMasks(IReadOnlyList<int> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return new ReadOnlyCollection<int>(copy);
        }

        static IReadOnlyList<SpawnEvent> CopySpawns(IReadOnlyList<SpawnEvent> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new SpawnEvent[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i] ?? throw new ArgumentException(
                    "Spawns cannot contain null.", nameof(source));
            return new ReadOnlyCollection<SpawnEvent>(copy);
        }

        static IReadOnlyList<ObstacleSpawn> CopyObstacles(
            IReadOnlyList<ObstacleSpawn> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new ObstacleSpawn[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i] ?? throw new ArgumentException(
                    "Obstacles cannot contain null.", nameof(source));
            return new ReadOnlyCollection<ObstacleSpawn>(copy);
        }

        static IReadOnlyList<MidbossOutcomeKind> CopyOutcomes(
            IReadOnlyList<MidbossOutcomeKind> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<MidbossOutcomeKind>();
            var copy = new MidbossOutcomeKind[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                MidbossOutcomeKind outcome = source[i];
                if (!Enum.IsDefined(typeof(MidbossOutcomeKind), outcome))
                    throw new ArgumentOutOfRangeException(nameof(source));
                for (int earlier = 0; earlier < i; earlier++)
                    if (copy[earlier] == outcome)
                        throw new ArgumentException(
                            "Post-midboss outcomes cannot contain duplicates.",
                            nameof(source));
                copy[i] = outcome;
            }
            return new ReadOnlyCollection<MidbossOutcomeKind>(copy);
        }
    }

    public sealed class StageBossTemplate
    {
        public StageBossTemplate(
            string bossId,
            int stageIndexMin,
            int stageIndexMax,
            int difficultyMin,
            int difficultyMax,
            int entryLaneMask)
            : this(
                bossId, stageIndexMin, stageIndexMax,
                difficultyMin, difficultyMax, entryLaneMask, 0)
        {
        }

        public StageBossTemplate(
            string bossId,
            int stageIndexMin,
            int stageIndexMax,
            int difficultyMin,
            int difficultyMax,
            int entryLaneMask,
            int maxHp)
            : this(
                bossId, stageIndexMin, stageIndexMax, difficultyMin,
                difficultyMax, entryLaneMask, maxHp, 0, 0, 0, null)
        {
        }

        public StageBossTemplate(
            string bossId,
            int stageIndexMin,
            int stageIndexMax,
            int difficultyMin,
            int difficultyMax,
            int entryLaneMask,
            int maxHp,
            int halfWidth,
            int halfHeight,
            int holdX,
            IReadOnlyList<BossPhase> phases)
            : this(
                bossId,
                stageIndexMin,
                stageIndexMax,
                difficultyMin,
                difficultyMax,
                entryLaneMask,
                maxHp,
                halfWidth,
                halfHeight,
                holdX,
                phases,
                null)
        {
        }

        public StageBossTemplate(
            string bossId,
            int stageIndexMin,
            int stageIndexMax,
            int difficultyMin,
            int difficultyMax,
            int entryLaneMask,
            int maxHp,
            int halfWidth,
            int halfHeight,
            int holdX,
            IReadOnlyList<BossPhase> phases,
            string themeId)
            : this(
                bossId,
                stageIndexMin,
                stageIndexMax,
                difficultyMin,
                difficultyMax,
                entryLaneMask,
                maxHp,
                halfWidth,
                halfHeight,
                holdX,
                phases,
                themeId,
                null)
        {
        }

        public StageBossTemplate(
            string bossId,
            int stageIndexMin,
            int stageIndexMax,
            int difficultyMin,
            int difficultyMax,
            int entryLaneMask,
            int maxHp,
            int halfWidth,
            int halfHeight,
            int holdX,
            IReadOnlyList<BossPhase> phases,
            string themeId,
            IReadOnlyList<BossPartDefinition> parts,
            WarshipEncounterDefinition warshipEncounter = null,
            BossFormDefinition form2 = null)
        {
            BossId = bossId ?? throw new ArgumentNullException(nameof(bossId));
            StageIndexMin = stageIndexMin;
            StageIndexMax = stageIndexMax;
            DifficultyMin = difficultyMin;
            DifficultyMax = difficultyMax;
            EntryLaneMask = entryLaneMask;
            MaxHp = maxHp;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
            HoldX = holdX;
            Phases = CopyPhases(phases);
            Parts = CopyParts(parts);
            ThemeId = themeId;
            WarshipEncounter = warshipEncounter;
            Form2 = form2;
        }

        public string BossId { get; }
        public int StageIndexMin { get; }
        public int StageIndexMax { get; }
        public int DifficultyMin { get; }
        public int DifficultyMax { get; }
        public int EntryLaneMask { get; }
        public int MaxHp { get; }
        public int HalfWidth { get; }
        public int HalfHeight { get; }
        public int HoldX { get; }
        public IReadOnlyList<BossPhase> Phases { get; }
        public IReadOnlyList<BossPartDefinition> Parts { get; }
        public string ThemeId { get; }
        public WarshipEncounterDefinition WarshipEncounter { get; }
        public BossFormDefinition Form2 { get; }

        internal bool Supports(int stageIndex, int difficulty)
        {
            return stageIndex >= StageIndexMin
                && stageIndex <= StageIndexMax
                && difficulty >= DifficultyMin
                && difficulty <= DifficultyMax;
        }

        internal bool SupportsTheme(string themeId)
        {
            return ThemeId == null
                || string.Equals(ThemeId, themeId, StringComparison.Ordinal);
        }

        internal void Validate(int validLanes)
        {
            if (BossId.Length == 0)
                throw new ArgumentException("Boss id cannot be empty.");
            if (ThemeId != null && ThemeId.Length == 0)
                throw new ArgumentException("Boss theme id cannot be empty.");
            if (StageIndexMin < 1 || StageIndexMax < StageIndexMin)
                throw new ArgumentException("Boss stage range is invalid.");
            if (DifficultyMin < 1 || DifficultyMax < DifficultyMin)
                throw new ArgumentException("Boss difficulty range is invalid.");
            if (MaxHp < 0)
                throw new ArgumentException("Boss HP cannot be negative.");
            if (HalfWidth < 0 || HalfHeight < 0)
                throw new ArgumentException("Boss hitbox dimensions cannot be negative.");
            ValidateParts();
            BossFormDefinition.ValidatePartsAndPhases(
                Parts,
                Phases,
                MaxHp);
            if (WarshipEncounter != null && Parts.Count == 0)
                throw new ArgumentException(
                    "A warship encounter requires multipart boss parts.");
            // Warship + form2 is allowed (REQ-139): hull completion hands off to
            // the robot second form via BeginWarshipFormTransition.
            WarshipEncounter?.ValidateParts(Parts);
            StagePlanClearability.ValidateLaneMask(
                EntryLaneMask, validLanes, nameof(EntryLaneMask));
        }

        void ValidateParts()
        {
            if (Parts.Count == 0)
                return;
            int coreCount = 0;
            long totalHp = 0;
            for (int i = 0; i < Parts.Count; i++)
            {
                BossPartDefinition part = Parts[i];
                totalHp += part.MaxHp;
                if (part.IsCore)
                    coreCount++;
                for (int previous = 0; previous < i; previous++)
                    if (string.Equals(
                            Parts[previous].PartId,
                            part.PartId,
                            StringComparison.Ordinal))
                        throw new ArgumentException(
                            $"Duplicate boss part id '{part.PartId}'.");
            }
            if (coreCount != 1)
                throw new ArgumentException(
                    "A multipart boss requires exactly one core.");
            if (totalHp != MaxHp)
                throw new ArgumentException(
                    "Multipart boss HP must equal the sum of its part HP.");
            for (int i = 0; i < Parts.Count; i++)
            {
                BossPartDefinition part = Parts[i];
                for (int gate = 0;
                    gate < part.CoreGatePartIds.Count;
                    gate++)
                {
                    bool found = false;
                    for (int candidate = 0;
                        candidate < Parts.Count;
                        candidate++)
                    {
                        if (candidate == i)
                            continue;
                        if (string.Equals(
                                Parts[candidate].PartId,
                                part.CoreGatePartIds[gate],
                                StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        throw new ArgumentException(
                            "Boss core gate references unknown part "
                            + $"'{part.CoreGatePartIds[gate]}'.");
                }
            }
        }

        static IReadOnlyList<BossPhase> CopyPhases(IReadOnlyList<BossPhase> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<BossPhase>();
            var copy = new BossPhase[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i] ?? throw new ArgumentException(
                    "Boss phases cannot contain null.", nameof(source));
            return new ReadOnlyCollection<BossPhase>(copy);
        }

        static IReadOnlyList<BossPartDefinition> CopyParts(
            IReadOnlyList<BossPartDefinition> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<BossPartDefinition>();
            var copy = new BossPartDefinition[source.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = source[i] ?? throw new ArgumentException(
                    "Boss parts cannot contain null.", nameof(source));
            return new ReadOnlyCollection<BossPartDefinition>(copy);
        }
    }

    /// <summary>
    /// Deterministically assembles compatible segment templates. Look-ahead removes
    /// choices that would strand the player before a later segment or the boss.
    /// </summary>
    public sealed class SegmentStageGenerator :
        IRouteStageGenerator,
        ISectionRouteStageGenerator,
        IMidbossOutcomeRouteStageGenerator,
        IColossalBossStageGenerator
    {
        const int StageGenerationStream = 0;
        const int SegmentSelectionStream = 0;
        const int BossSelectionStream = 1;
        const int ThemePermutationStream = 2;
        public const int ObstacleJitterStream = 3;
        public const int PostMidbossSegmentStream = 4;
        public const int SolidJitterSubUnits =
            Simulation.SimSpace.SubUnitsPerWorldUnit / 2;
        public const int BreakableJitterSubUnits =
            3 * Simulation.SimSpace.SubUnitsPerWorldUnit / 2;
        const int HazardCenterOffsetSubUnits = 256;
        public const string LeviathanBossId = "boss_leviathan";
        public const string BroodmotherBossId = "boss_broodmother";
        /// <summary>REQ-159: leviathan hidden-path presentation theme.</summary>
        public const string HiddenThemeAbyss = "abyss";
        /// <summary>REQ-159: broodmother hidden-path presentation theme.</summary>
        public const string HiddenThemeBrood = "brood";

        /// <summary>
        /// Hidden-only themes stay in the catalog (segments/bosses/gimmicks) but
        /// must not enter the primary 5-biome run order or normal GenerateRoute
        /// loops. Core routes uncharted rooms to these separately (REQ-159).
        /// </summary>
        public static bool IsHiddenOnlyTheme(string themeId)
        {
            return string.Equals(themeId, HiddenThemeAbyss, StringComparison.Ordinal)
                || string.Equals(themeId, HiddenThemeBrood, StringComparison.Ordinal);
        }

        readonly StageGenerationCatalog _catalog;
        readonly int _validLanes;

        public SegmentStageGenerator(StageGenerationCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _validLanes = StagePlanClearability.GetValidLaneMask(catalog.LaneCount);
        }

        public IReadOnlyList<string> ThemeIds => _catalog.ThemeIds;

        public IReadOnlyList<string> GetThemeOrder(ulong seed)
        {
            return Array.AsReadOnly(BuildThemeOrder(seed));
        }

        public StagePlan Generate(ulong seed, int stageIndex, int difficulty)
        {
            if (stageIndex < 1)
                throw new ArgumentOutOfRangeException(nameof(stageIndex));
            if (difficulty < 1)
                throw new ArgumentOutOfRangeException(nameof(difficulty));

            string requestedThemeId;
            string themeId = SelectTheme(
                seed,
                stageIndex,
                difficulty,
                out requestedThemeId);
            return GenerateCore(
                seed,
                stageIndex,
                difficulty,
                themeId,
                requestedThemeId,
                EncounterType.Normal,
                GetSegmentCount(
                    EncounterType.Normal,
                    StageRouteSection.Default),
                GetTargetDurationTicks(
                    EncounterType.Normal,
                    StageRouteSection.Default));
        }

        public bool CanGenerateRoute(
            string themeId,
            int stageIndex,
            int difficulty,
            EncounterType encounterType)
        {
            if (string.IsNullOrEmpty(themeId)
                || stageIndex < 1
                || difficulty < 1
                || !Enum.IsDefined(typeof(EncounterType), encounterType))
                return false;
            if (!ContainsTheme(themeId))
                return false;

            int segmentCount = GetSegmentCount(
                encounterType,
                StageRouteSection.Default);
            return CanAssemble(
                themeId,
                stageIndex,
                difficulty,
                segmentCount,
                MidbossOutcomeKind.Default,
                GetTargetDurationTicks(
                    encounterType,
                    StageRouteSection.Default));
        }

        public StagePlan GenerateRoute(
            ulong seed,
            int stageIndex,
            int difficulty,
            string themeId,
            EncounterType encounterType)
        {
            if (stageIndex < 1)
                throw new ArgumentOutOfRangeException(nameof(stageIndex));
            if (difficulty < 1)
                throw new ArgumentOutOfRangeException(nameof(difficulty));
            if (string.IsNullOrEmpty(themeId))
                throw new ArgumentException(
                    "Route theme id cannot be empty.",
                    nameof(themeId));
            if (!Enum.IsDefined(typeof(EncounterType), encounterType))
                throw new ArgumentOutOfRangeException(nameof(encounterType));
            if (!ContainsTheme(themeId))
                throw new ArgumentException(
                    $"Route theme '{themeId}' is not in the catalog.",
                    nameof(themeId));
            if (!CanGenerateRoute(
                    themeId,
                    stageIndex,
                    difficulty,
                    encounterType))
            {
                throw new InvalidOperationException(
                    CannotAssembleMessage(themeId));
            }

            return GenerateCore(
                seed,
                stageIndex,
                difficulty,
                themeId,
                themeId,
                encounterType,
                GetSegmentCount(
                    encounterType,
                    StageRouteSection.Default),
                GetTargetDurationTicks(
                    encounterType,
                    StageRouteSection.Default));
        }

        public bool CanGenerateRouteForSection(
            string themeId,
            int stageIndex,
            int difficulty,
            EncounterType encounterType,
            StageRouteSection section)
        {
            if (!Enum.IsDefined(typeof(StageRouteSection), section)
                || string.IsNullOrEmpty(themeId)
                || stageIndex < 1
                || difficulty < 1
                || !Enum.IsDefined(typeof(EncounterType), encounterType)
                || !ContainsTheme(themeId))
                return false;
            return CanAssemble(
                themeId,
                stageIndex,
                difficulty,
                GetSegmentCount(encounterType, section),
                MidbossOutcomeKind.Default,
                GetTargetDurationTicks(encounterType, section));
        }

        public StagePlan GenerateRouteForSection(
            ulong seed,
            int stageIndex,
            int difficulty,
            string themeId,
            EncounterType encounterType,
            StageRouteSection section)
        {
            if (!CanGenerateRouteForSection(
                    themeId,
                    stageIndex,
                    difficulty,
                    encounterType,
                    section))
                throw new InvalidOperationException(
                    CannotAssembleMessage(themeId));
            return GenerateCore(
                seed,
                stageIndex,
                difficulty,
                themeId,
                themeId,
                encounterType,
                GetSegmentCount(encounterType, section),
                GetTargetDurationTicks(encounterType, section));
        }

        public bool CanGenerateRouteForSection(
            string themeId,
            int stageIndex,
            int difficulty,
            EncounterType encounterType,
            StageRouteSection section,
            MidbossOutcomeKind outcome)
        {
            if (!Enum.IsDefined(typeof(MidbossOutcomeKind), outcome)
                || !Enum.IsDefined(typeof(StageRouteSection), section)
                || string.IsNullOrEmpty(themeId)
                || stageIndex < 1
                || difficulty < 1
                || !Enum.IsDefined(typeof(EncounterType), encounterType)
                || !ContainsTheme(themeId))
                return false;
            return CanAssemble(
                themeId,
                stageIndex,
                difficulty,
                GetSegmentCount(encounterType, section),
                ResolveAvailableOutcome(
                    themeId,
                    stageIndex,
                    difficulty,
                    GetSegmentCount(encounterType, section),
                    outcome,
                    GetTargetDurationTicks(encounterType, section)),
                GetTargetDurationTicks(encounterType, section));
        }

        public StagePlan GenerateRouteForSection(
            ulong seed,
            int stageIndex,
            int difficulty,
            string themeId,
            EncounterType encounterType,
            StageRouteSection section,
            MidbossOutcomeKind outcome)
        {
            if (!CanGenerateRouteForSection(
                    themeId,
                    stageIndex,
                    difficulty,
                    encounterType,
                    section,
                    outcome))
                throw new InvalidOperationException(
                    CannotAssembleMessage(themeId));
            return GenerateCore(
                seed,
                stageIndex,
                difficulty,
                themeId,
                themeId,
                encounterType,
                GetSegmentCount(encounterType, section),
                GetTargetDurationTicks(encounterType, section),
                ResolveAvailableOutcome(
                    themeId,
                    stageIndex,
                    difficulty,
                    GetSegmentCount(encounterType, section),
                    outcome,
                    GetTargetDurationTicks(encounterType, section)));
        }

        public StagePlan GeneratePostMidbossHalf(
            ulong seed,
            int stageIndex,
            int difficulty,
            string themeId,
            MidbossOutcomeKind outcome)
        {
            return GenerateRouteForSection(
                seed,
                stageIndex,
                difficulty,
                themeId,
                EncounterType.Normal,
                StageRouteSection.Closing,
                outcome);
        }

        public bool CanGenerateColossalBoss(ColossalBossKind kind)
        {
            string id = GetColossalBossId(kind);
            if (id == null)
                return false;
            for (int i = 0; i < _catalog.Bosses.Count; i++)
                if (string.Equals(
                        _catalog.Bosses[i].BossId,
                        id,
                        StringComparison.Ordinal))
                    return true;
            return false;
        }

        public StagePlan GenerateColossalBoss(
            ulong seed,
            int stageIndex,
            int difficulty,
            ColossalBossKind kind)
        {
            string id = GetColossalBossId(kind);
            if (id == null)
                throw new ArgumentOutOfRangeException(nameof(kind));
            StageBossTemplate selected = null;
            for (int i = 0; i < _catalog.Bosses.Count; i++)
            {
                StageBossTemplate candidate = _catalog.Bosses[i];
                if (string.Equals(
                    candidate.BossId,
                    id,
                    StringComparison.Ordinal))
                {
                    selected = candidate;
                    break;
                }
            }
            if (selected == null)
                throw new InvalidOperationException(
                    $"The stage catalog does not contain colossal boss '{id}'.");
            if (!selected.Supports(stageIndex, difficulty))
                throw new InvalidOperationException(
                    $"Colossal boss '{id}' does not support stage {stageIndex} "
                    + $"at difficulty {difficulty}.");

            int laneMask = selected.EntryLaneMask != 0
                ? selected.EntryLaneMask
                : _catalog.StartLaneMask;
            return new StagePlan(
                Array.Empty<StageSegment>(),
                selected.BossId,
                _catalog.LaneCount,
                laneMask,
                laneMask,
                selected.MaxHp,
                selected.HalfWidth,
                selected.HalfHeight,
                selected.HoldX,
                selected.Phases,
                selected.ThemeId,
                selected.ThemeId,
                EncounterType.Normal,
                selected.Parts,
                _catalog.FindGimmick(selected.ThemeId),
                selected.WarshipEncounter,
                selected.Form2);
        }

        public string GetColossalBossThemeId(ColossalBossKind kind)
        {
            string id = GetColossalBossId(kind);
            if (id == null)
                return null;
            for (int i = 0; i < _catalog.Bosses.Count; i++)
            {
                StageBossTemplate candidate = _catalog.Bosses[i];
                if (string.Equals(
                        candidate.BossId,
                        id,
                        StringComparison.Ordinal))
                    return string.IsNullOrEmpty(candidate.ThemeId)
                        ? null
                        : candidate.ThemeId;
            }
            return null;
        }

        static string GetColossalBossId(ColossalBossKind kind)
        {
            switch (kind)
            {
                case ColossalBossKind.Leviathan:
                    return LeviathanBossId;
                case ColossalBossKind.Broodmother:
                    return BroodmotherBossId;
                default:
                    return null;
            }
        }

        StagePlan GenerateCore(
            ulong seed,
            int stageIndex,
            int difficulty,
            string themeId,
            string requestedThemeId,
            EncounterType encounterType,
            int segmentCount,
            int? targetDurationTicks,
            MidbossOutcomeKind outcome = MidbossOutcomeKind.Default)
        {
            if (!Enum.IsDefined(typeof(MidbossOutcomeKind), outcome))
                throw new ArgumentOutOfRangeException(nameof(outcome));
            segmentCount = FindFeasibleSegmentCount(
                themeId,
                stageIndex,
                difficulty,
                segmentCount,
                outcome,
                targetDurationTicks);
            if (segmentCount == 0)
                throw new InvalidOperationException(
                    CannotAssembleMessage(themeId));
            Rng stageRng = new Rng(seed)
                .Fork(StageGenerationStream)
                .Fork(stageIndex)
                .Fork(difficulty);
            Rng segmentRng = outcome == MidbossOutcomeKind.Default
                ? stageRng.Fork(SegmentSelectionStream)
                : stageRng
                    .Fork(PostMidbossSegmentStream)
                    .Fork((int)outcome);
            Rng bossRng = stageRng.Fork(BossSelectionStream);
            Rng obstacleJitterRng = stageRng.Fork(ObstacleJitterStream);

            var assembled = new StageSegment[segmentCount];
            var completionCache = new Dictionary<long, bool>();
            var selectedTemplates = new bool[_catalog.Segments.Count];
            int reachable = _catalog.StartLaneMask;
            int previousTemplateIndex = -1;
            int assembledCount = 0;
            long assembledDurationTicks = 0;

            for (int position = 0; position < assembled.Length; position++)
            {
                int remaining = assembled.Length - position - 1;
                var viableIndices = new List<int>();
                var viableExits = new List<int>();

                CollectUniqueCompletionCandidates(
                    reachable,
                    remaining,
                    stageIndex,
                    difficulty,
                    themeId,
                    outcome,
                    selectedTemplates,
                    viableIndices,
                    viableExits);

                if (viableIndices.Count == 0)
                    CollectRelaxedCandidates(
                        reachable,
                        remaining,
                        stageIndex,
                        difficulty,
                        themeId,
                        outcome,
                        selectedTemplates,
                        previousTemplateIndex,
                        completionCache,
                        viableIndices,
                        viableExits);

                if (viableIndices.Count == 0)
                    throw new InvalidOperationException(
                        CannotAssembleMessage(themeId));

                int pick = encounterType == EncounterType.Supply
                    ? FindLowestCombatCandidate(viableIndices)
                    : PickWeightedCandidate(segmentRng, viableIndices);
                int selectedIndex = viableIndices[pick];
                StageSegmentTemplate selected = _catalog.Segments[selectedIndex];
                StageSegment segment = selected.CreateSegment(
                    obstacleJitterRng.Fork(position));
                assembled[position] = segment;
                assembledCount++;
                assembledDurationTicks += segment.LengthTicks;
                reachable = viableExits[pick];
                selectedTemplates[selectedIndex] = true;
                previousTemplateIndex = selectedIndex;

                // Segment boundaries carry spawn timing, obstacle timing, and
                // lane-transition contracts, so truncating the final segment
                // would corrupt authored content. Include it whole, allowing a
                // bounded overshoot, and stop only when its exit reaches a boss.
                // The count remains a hard safety cap if the target is unreachable.
                if (targetDurationTicks.HasValue
                    && assembledDurationTicks >= targetDurationTicks.Value
                    && HasReachableBoss(
                        reachable,
                        stageIndex,
                        difficulty,
                        themeId))
                    break;
            }

            if (assembledCount != assembled.Length)
                Array.Resize(ref assembled, assembledCount);

            var compatibleBosses = new List<int>();
            for (int i = 0; i < _catalog.Bosses.Count; i++)
            {
                StageBossTemplate boss = _catalog.Bosses[i];
                if (IsHiddenOnlyColossalBoss(boss.BossId)
                    && !IsHiddenOnlyTheme(themeId))
                    continue;
                if (boss.Supports(stageIndex, difficulty)
                    && boss.SupportsTheme(themeId)
                    && (reachable & boss.EntryLaneMask) != 0)
                    compatibleBosses.Add(i);
            }

            if (compatibleBosses.Count == 0)
                throw new InvalidOperationException(
                    themeId == null
                        ? "Catalog has no reachable boss for the requested inputs."
                        : $"Catalog has no reachable boss for theme '{themeId}' and the requested inputs.");

            int bossPick = bossRng.NextInt(0, compatibleBosses.Count);
            StageBossTemplate selectedBoss =
                _catalog.Bosses[compatibleBosses[bossPick]];
            int scaledMaxHp = selectedBoss.MaxHp;
            IReadOnlyList<BossPartDefinition> scaledParts = selectedBoss.Parts;
            ScaleBossHpForStage(
                themeId, stageIndex, ref scaledMaxHp, ref scaledParts);

            StagePlan normalPlan = new StagePlan(
                assembled,
                selectedBoss.BossId,
                _catalog.LaneCount,
                _catalog.StartLaneMask,
                selectedBoss.EntryLaneMask,
                scaledMaxHp,
                selectedBoss.HalfWidth,
                selectedBoss.HalfHeight,
                selectedBoss.HoldX,
                selectedBoss.Phases,
                themeId,
                requestedThemeId,
                encounterType,
                scaledParts,
                _catalog.FindGimmick(themeId),
                selectedBoss.WarshipEncounter,
                selectedBoss.Form2);
            return ApplyEncounterPlan(normalPlan, encounterType);
        }

        /// <summary>
        /// 셔플 1안 (REQ-187): 테마 셔플로 보스가 홈 스테이지 밖에 등장하면
        /// HP를 스테이지 파워 커브 비율(curve[등장]/curve[홈])로 맞춘다.
        /// 초반 장비로 후반용 보스(전함 44,400 등)를 만나는 벽을 없애면서,
        /// 홈 등장은 정확히 1.0이라 기존 밸런스가 그대로다. 커브가 없으면
        /// 아무것도 하지 않고, 히든 테마(콜로설)는 셔플 대상이 아니라 제외.
        /// </summary>
        void ScaleBossHpForStage(
            string themeId,
            int stageIndex,
            ref int maxHp,
            ref IReadOnlyList<BossPartDefinition> parts)
        {
            IReadOnlyList<int> curve = _catalog.StagePowerCurvePermille;
            if (curve.Count == 0
                || themeId == null
                || IsHiddenOnlyTheme(themeId))
                return;

            // 홈 스테이지 = 셔플 전 기본 순서(카탈로그의 프라이머리 테마 순서)
            // 에서의 위치. BuildThemeOrder가 섞는 것과 같은 목록을 센다.
            int homeStage = 0;
            int primary = 0;
            for (int i = 0; i < _catalog.ThemeIds.Count; i++)
            {
                if (IsHiddenOnlyTheme(_catalog.ThemeIds[i]))
                    continue;
                primary++;
                if (string.Equals(
                        _catalog.ThemeIds[i], themeId, StringComparison.Ordinal))
                {
                    homeStage = primary;
                    break;
                }
            }
            if (homeStage == 0)
                return;

            long numerator =
                curve[Math.Min(stageIndex, curve.Count) - 1];
            long denominator =
                curve[Math.Min(homeStage, curve.Count) - 1];
            if (numerator == denominator)
                return;

            if (parts != null && parts.Count > 0)
            {
                long originalSum = 0;
                for (int i = 0; i < parts.Count; i++)
                    originalSum += parts[i].MaxHp;
                var scaled = new BossPartDefinition[parts.Count];
                long scaledSum = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    BossPartDefinition part = parts[i];
                    int scaledHp =
                        ScaleHp(part.MaxHp, numerator, denominator);
                    scaled[i] = new BossPartDefinition(
                        part.PartId,
                        part.OffsetX,
                        part.OffsetY,
                        part.HalfWidth,
                        part.HalfHeight,
                        scaledHp,
                        part.IsCore,
                        part.CoreGatePartIds,
                        part.Attack,
                        part.RegenerationTicks);
                    scaledSum += scaledHp;
                }
                parts = scaled;
                // "부위 합 = 본체" 불변식(REQ-116)을 정수 반올림에서도 지킨다:
                // 원래 그 관계였다면 합으로 재계산하고, 아니면 본체만 스케일.
                maxHp = originalSum == maxHp
                    ? (int)Math.Min(int.MaxValue, scaledSum)
                    : ScaleHp(maxHp, numerator, denominator);
                return;
            }

            maxHp = ScaleHp(maxHp, numerator, denominator);
        }

        static int ScaleHp(int hp, long numerator, long denominator)
        {
            long scaled = hp * numerator / denominator;
            if (scaled < 1) return 1;
            if (scaled > int.MaxValue) return int.MaxValue;
            return (int)scaled;
        }

        int FindLowestCombatCandidate(IReadOnlyList<int> viableIndices)
        {
            int best = 0;
            int bestSpawnCount =
                _catalog.Segments[viableIndices[0]].Spawns.Count;
            for (int i = 1; i < viableIndices.Count; i++)
            {
                int spawnCount =
                    _catalog.Segments[viableIndices[i]].Spawns.Count;
                if (spawnCount < bestSpawnCount)
                {
                    best = i;
                    bestSpawnCount = spawnCount;
                }
            }
            return best;
        }

        int PickWeightedCandidate(
            Rng rng,
            IReadOnlyList<int> viableIndices)
        {
            var weights = new int[viableIndices.Count];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = _catalog.Segments[viableIndices[i]].Weight;
            return rng.PickWeighted(weights, weights.Length);
        }

        int GetSegmentCount(
            EncounterType encounterType,
            StageRouteSection section)
        {
            if (encounterType == EncounterType.Elite
                || encounterType == EncounterType.Supply)
                return 1;
            return section == StageRouteSection.Closing
                ? _catalog.ClosingSegmentsPerStage
                : _catalog.SegmentsPerStage;
        }

        int? GetTargetDurationTicks(
            EncounterType encounterType,
            StageRouteSection section)
        {
            // Elite and supply encounters deliberately remain single-segment.
            if (encounterType == EncounterType.Elite
                || encounterType == EncounterType.Supply)
                return null;
            return section == StageRouteSection.Closing
                ? _catalog.ClosingTargetDurationTicks
                : _catalog.TargetDurationTicks;
        }

        bool ContainsTheme(string themeId)
        {
            for (int i = 0; i < _catalog.ThemeIds.Count; i++)
            {
                if (string.Equals(
                        _catalog.ThemeIds[i],
                        themeId,
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static StagePlan ApplyEncounterPlan(
            StagePlan source,
            EncounterType encounterType)
        {
            IReadOnlyList<StageSegment> segments = source.Segments;
            string bossId = source.BossId;
            int bossMaxHp = source.BossMaxHp;
            int bossHalfWidth = source.BossHalfWidth;
            int bossHalfHeight = source.BossHalfHeight;
            int bossHoldX = source.BossHoldX;
            IReadOnlyList<BossPhase> bossPhases = source.BossPhases;
            IReadOnlyList<BossPartDefinition> bossParts = source.BossParts;
            WarshipEncounterDefinition warshipEncounter =
                source.WarshipEncounter;
            BossFormDefinition form2 = source.Form2;

            if (encounterType == EncounterType.Supply)
            {
                bossId = string.Empty;
                bossMaxHp = 0;
                bossHalfWidth = 0;
                bossHalfHeight = 0;
                bossHoldX = 0;
                bossPhases = Array.Empty<BossPhase>();
                bossParts = Array.Empty<BossPartDefinition>();
                warshipEncounter = null;
                form2 = null;
            }
            else if (encounterType == EncounterType.Hazard)
            {
                segments = AddHazardObstacles(source.Segments);
            }

            return new StagePlan(
                segments,
                bossId,
                source.LaneCount,
                source.StartLaneMask,
                source.BossEntryLaneMask,
                bossMaxHp,
                bossHalfWidth,
                bossHalfHeight,
                bossHoldX,
                bossPhases,
                source.ThemeId,
                source.RequestedThemeId,
                encounterType,
                bossParts,
                source.Gimmick,
                warshipEncounter,
                form2);
        }

        static IReadOnlyList<StageSegment> AddHazardObstacles(
            IReadOnlyList<StageSegment> source)
        {
            var segments = new StageSegment[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                StageSegment segment = source[i];
                int extraCount = (segment.Obstacles.Count + 1) / 2;
                if (extraCount == 0)
                {
                    segments[i] = segment;
                    continue;
                }

                var obstacles =
                    new ObstacleSpawn[segment.Obstacles.Count + extraCount];
                for (int obstacleIndex = 0;
                    obstacleIndex < segment.Obstacles.Count;
                    obstacleIndex++)
                {
                    obstacles[obstacleIndex] =
                        segment.Obstacles[obstacleIndex];
                }
                for (int extra = 0; extra < extraCount; extra++)
                {
                    ObstacleSpawn original = segment.Obstacles[extra];
                    int mirroredY = original.Y == 0
                        ? HazardCenterOffsetSubUnits
                        : -original.Y;
                    obstacles[segment.Obstacles.Count + extra] =
                        new ObstacleSpawn(
                            original.Type,
                            original.X,
                            mirroredY,
                            original.Hp,
                            original.LaserAttack,
                            original.BlocksEnemyBullets,
                            original.RegenDelayTicks);
                }

                segments[i] = new StageSegment(
                    segment.SegmentId,
                    segment.LengthTicks,
                    segment.Spawns,
                    segment.EntryLaneMask,
                    segment.ExitLaneMask,
                    segment.TraversableLaneMasks,
                    obstacles,
                    segment.Environment,
                    segment.ScrollSpeedMultiplierNumerator,
                    segment.ScrollSpeedMultiplierDenominator);
            }
            return Array.AsReadOnly(segments);
        }

        void CollectUniqueCompletionCandidates(
            int reachable,
            int segmentsRemaining,
            int stageIndex,
            int difficulty,
            string themeId,
            MidbossOutcomeKind outcome,
            bool[] selectedTemplates,
            ICollection<int> viableIndices,
            ICollection<int> viableExits)
        {
            for (int i = 0; i < _catalog.Segments.Count; i++)
            {
                if (selectedTemplates[i])
                    continue;

                StageSegmentTemplate candidate = _catalog.Segments[i];
                if (!candidate.SupportsDifficulty(difficulty)
                    || !candidate.SupportsTheme(themeId)
                    || !candidate.SupportsMidbossOutcome(outcome))
                    continue;

                int exit = StagePlanClearability.Advance(
                    reachable, candidate, _validLanes);
                if (exit == 0)
                    continue;

                selectedTemplates[i] = true;
                bool canComplete = CanCompleteWithoutReuse(
                    exit,
                    segmentsRemaining,
                    stageIndex,
                    difficulty,
                    themeId,
                    outcome,
                    selectedTemplates);
                selectedTemplates[i] = false;
                if (!canComplete)
                    continue;

                viableIndices.Add(i);
                viableExits.Add(exit);
            }
        }

        void CollectRelaxedCandidates(
            int reachable,
            int segmentsRemaining,
            int stageIndex,
            int difficulty,
            string themeId,
            MidbossOutcomeKind outcome,
            bool[] selectedTemplates,
            int previousTemplateIndex,
            IDictionary<long, bool> completionCache,
            ICollection<int> viableIndices,
            ICollection<int> viableExits)
        {
            for (int reusePriority = 0; reusePriority < 3; reusePriority++)
            {
                for (int i = 0; i < _catalog.Segments.Count; i++)
                {
                    bool wasSelected = selectedTemplates[i];
                    bool matchesPriority =
                        reusePriority == 0
                            ? !wasSelected
                            : reusePriority == 1
                                ? wasSelected && i != previousTemplateIndex
                                : i == previousTemplateIndex;
                    if (!matchesPriority)
                        continue;

                    StageSegmentTemplate candidate = _catalog.Segments[i];
                    if (!candidate.SupportsDifficulty(difficulty)
                        || !candidate.SupportsTheme(themeId)
                        || !candidate.SupportsMidbossOutcome(outcome))
                        continue;

                    int exit = StagePlanClearability.Advance(
                        reachable, candidate, _validLanes);
                    if (exit == 0
                        || !CanComplete(
                            exit,
                            segmentsRemaining,
                            stageIndex,
                            difficulty,
                            themeId,
                            outcome,
                            completionCache))
                        continue;

                    viableIndices.Add(i);
                    viableExits.Add(exit);
                }

                if (viableIndices.Count != 0)
                    return;

                // Keep the outcome's authored pieces first. Once its unused
                // pool is exhausted, prefer an unused ordinary theme piece to
                // repeating a tagged piece. The exit must still support the
                // original outcome pool for the rest of the route, so this
                // fallback cannot strand the assembler or change eligibility.
                if (reusePriority == 0 && outcome != MidbossOutcomeKind.Default)
                {
                    for (int i = 0; i < _catalog.Segments.Count; i++)
                    {
                        StageSegmentTemplate candidate = _catalog.Segments[i];
                        if (selectedTemplates[i]
                            || !candidate.SupportsDifficulty(difficulty)
                            || !candidate.SupportsTheme(themeId)
                            || !candidate.SupportsMidbossOutcome(MidbossOutcomeKind.Default))
                            continue;

                        int exit = StagePlanClearability.Advance(
                            reachable, candidate, _validLanes);
                        if (exit == 0
                            || !CanComplete(
                                exit, segmentsRemaining, stageIndex, difficulty,
                                themeId, outcome, completionCache))
                            continue;

                        viableIndices.Add(i);
                        viableExits.Add(exit);
                    }

                    if (viableIndices.Count != 0)
                        return;
                }
            }
        }

        bool CanCompleteWithoutReuse(
            int reachable,
            int segmentsRemaining,
            int stageIndex,
            int difficulty,
            string themeId,
            MidbossOutcomeKind outcome,
            bool[] selectedTemplates)
        {
            if (segmentsRemaining == 0)
                return HasReachableBoss(
                    reachable, stageIndex, difficulty, themeId);

            for (int i = 0; i < _catalog.Segments.Count; i++)
            {
                if (selectedTemplates[i])
                    continue;

                StageSegmentTemplate candidate = _catalog.Segments[i];
                if (!candidate.SupportsDifficulty(difficulty)
                    || !candidate.SupportsTheme(themeId)
                    || !candidate.SupportsMidbossOutcome(outcome))
                    continue;

                int exit = StagePlanClearability.Advance(
                    reachable, candidate, _validLanes);
                if (exit == 0)
                    continue;

                selectedTemplates[i] = true;
                bool canComplete = CanCompleteWithoutReuse(
                    exit,
                    segmentsRemaining - 1,
                    stageIndex,
                    difficulty,
                    themeId,
                    outcome,
                    selectedTemplates);
                selectedTemplates[i] = false;
                if (canComplete)
                    return true;
            }
            return false;
        }

        string SelectTheme(
            ulong seed,
            int stageIndex,
            int difficulty,
            out string requestedThemeId)
        {
            if (_catalog.ThemeIds.Count == 0)
            {
                requestedThemeId = null;
                return null;
            }

            string[] runOrder = BuildThemeOrder(seed);
            int requestedIndex = (stageIndex - 1) % runOrder.Length;
            requestedThemeId = runOrder[requestedIndex];

            if (CanAssemble(
                    requestedThemeId,
                    stageIndex,
                    difficulty))
                return requestedThemeId;

            for (int offset = 1; offset < runOrder.Length; offset++)
            {
                string fallback =
                    runOrder[(requestedIndex + offset) % runOrder.Length];
                if (CanAssemble(fallback, stageIndex, difficulty))
                    return fallback;
            }

            return requestedThemeId;
        }

        string[] BuildThemeOrder(ulong seed)
        {
            // Primary biome order only — hidden themes (abyss/brood) are catalog
            // entries for uncharted rooms, not stages 1–5 (REQ-159).
            int primaryCount = 0;
            for (int i = 0; i < _catalog.ThemeIds.Count; i++)
                if (!IsHiddenOnlyTheme(_catalog.ThemeIds[i]))
                    primaryCount++;

            var runOrder = new string[primaryCount];
            int write = 0;
            for (int i = 0; i < _catalog.ThemeIds.Count; i++)
            {
                string themeId = _catalog.ThemeIds[i];
                if (IsHiddenOnlyTheme(themeId))
                    continue;
                runOrder[write++] = themeId;
            }

            if (runOrder.Length == 0)
                return runOrder;

            Rng permutationRng = new Rng(seed)
                .Fork(StageGenerationStream)
                .Fork(ThemePermutationStream);
            int lastShuffledIndex = runOrder.Length >= 5
                ? runOrder.Length - 2
                : runOrder.Length - 1;
            for (int i = lastShuffledIndex; i > 1; i--)
            {
                int swapIndex = permutationRng.NextInt(1, i + 1);
                string held = runOrder[i];
                runOrder[i] = runOrder[swapIndex];
                runOrder[swapIndex] = held;
            }
            return runOrder;
        }

        bool CanAssemble(
            string themeId,
            int stageIndex,
            int difficulty)
        {
            return CanAssemble(
                themeId,
                stageIndex,
                difficulty,
                _catalog.SegmentsPerStage,
                MidbossOutcomeKind.Default,
                _catalog.TargetDurationTicks);
        }

        bool CanAssemble(
            string themeId,
            int stageIndex,
            int difficulty,
            int segmentCount,
            MidbossOutcomeKind outcome = MidbossOutcomeKind.Default,
            int? targetDurationTicks = null)
        {
            return FindFeasibleSegmentCount(
                themeId,
                stageIndex,
                difficulty,
                segmentCount,
                outcome,
                targetDurationTicks) > 0;
        }

        int FindFeasibleSegmentCount(
            string themeId,
            int stageIndex,
            int difficulty,
            int segmentCount,
            MidbossOutcomeKind outcome,
            int? targetDurationTicks)
        {
            int minimum = targetDurationTicks.HasValue ? 1 : segmentCount;
            for (int candidateCount = segmentCount;
                candidateCount >= minimum;
                candidateCount--)
            {
                if (CanComplete(
                    _catalog.StartLaneMask,
                    candidateCount,
                    stageIndex,
                    difficulty,
                    themeId,
                    outcome,
                    new Dictionary<long, bool>()))
                    return candidateCount;
            }
            return 0;
        }

        MidbossOutcomeKind ResolveAvailableOutcome(
            string themeId,
            int stageIndex,
            int difficulty,
            int segmentCount,
            MidbossOutcomeKind requested,
            int? targetDurationTicks)
        {
            if (!Enum.IsDefined(typeof(MidbossOutcomeKind), requested))
                throw new ArgumentOutOfRangeException(nameof(requested));
            if (requested != MidbossOutcomeKind.Default
                && CanAssemble(
                    themeId,
                    stageIndex,
                    difficulty,
                    segmentCount,
                    requested,
                    targetDurationTicks))
                return requested;
            return MidbossOutcomeKind.Default;
        }

        static string CannotAssembleMessage(string themeId)
        {
            return themeId == null
                ? "Catalog cannot assemble a clearable stage for the requested inputs."
                : $"Catalog cannot assemble a clearable stage for theme '{themeId}' and the requested inputs.";
        }

        bool CanComplete(
            int reachable,
            int segmentsRemaining,
            int stageIndex,
            int difficulty,
            string themeId,
            MidbossOutcomeKind outcome,
            IDictionary<long, bool> cache)
        {
            long cacheKey = ((long)segmentsRemaining << 32) | (uint)reachable;
            if (cache.TryGetValue(cacheKey, out bool cached))
                return cached;

            if (segmentsRemaining == 0)
            {
                bool bossReachable = HasReachableBoss(
                    reachable, stageIndex, difficulty, themeId);
                cache.Add(cacheKey, bossReachable);
                return bossReachable;
            }

            for (int i = 0; i < _catalog.Segments.Count; i++)
            {
                StageSegmentTemplate candidate = _catalog.Segments[i];
                if (!candidate.SupportsDifficulty(difficulty)
                    || !candidate.SupportsTheme(themeId)
                    || !candidate.SupportsMidbossOutcome(outcome))
                    continue;

                int exit = StagePlanClearability.Advance(
                    reachable, candidate, _validLanes);
                if (exit != 0
                    && CanComplete(
                        exit,
                        segmentsRemaining - 1,
                        stageIndex,
                        difficulty,
                        themeId,
                        outcome,
                        cache))
                {
                    cache.Add(cacheKey, true);
                    return true;
                }
            }
            cache.Add(cacheKey, false);
            return false;
        }

        bool HasReachableBoss(
            int reachable,
            int stageIndex,
            int difficulty,
            string themeId)
        {
            for (int i = 0; i < _catalog.Bosses.Count; i++)
            {
                StageBossTemplate boss = _catalog.Bosses[i];
                // 히든 보스는 공개 테마의 스테이지 끝에 나타나면 안 된다. 하지만
                // 자기 전용 테마(abyss/brood)에서는 **그 보스가 유일한 종착점**이다 —
                // 무조건 건너뛰면 히든 접근 구간이 "도달 가능한 보스가 없다"로
                // 조립 자체에 실패한다. REQ-159에서 실제로 그랬고, 증상은 크래시가
                // 아니라 "히든 테마로는 아무 스테이지도 만들어지지 않음"이었다.
                // 공개 테마는 abyss/brood를 부르지 않으므로 새는 길은 없다.
                if (IsHiddenOnlyColossalBoss(boss.BossId)
                    && !IsHiddenOnlyTheme(themeId))
                    continue;
                if (boss.Supports(stageIndex, difficulty)
                    && boss.SupportsTheme(themeId)
                    && (reachable & boss.EntryLaneMask) != 0)
                    return true;
            }
            return false;
        }

        static bool IsHiddenOnlyColossalBoss(string bossId)
        {
            return string.Equals(
                    bossId,
                    LeviathanBossId,
                    StringComparison.Ordinal)
                || string.Equals(
                    bossId,
                    BroodmotherBossId,
                    StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Integer-only reachability check shared by generation and tests. Each
    /// checkpoint permits staying in a lane or moving to one adjacent lane.
    /// </summary>
    public static class StagePlanClearability
    {
        public static bool IsClearable(StagePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.LaneCount < 1 || plan.LaneCount > 30)
                return false;

            int validLanes = GetValidLaneMask(plan.LaneCount);
            if (!IsLaneMaskValid(plan.StartLaneMask, validLanes)
                || !IsLaneMaskValid(plan.BossEntryLaneMask, validLanes))
                return false;

            int reachable = plan.StartLaneMask;
            for (int i = 0; i < plan.Segments.Count; i++)
            {
                reachable = Advance(reachable, plan.Segments[i], validLanes);
                if (reachable == 0)
                    return false;
            }
            if (plan.EncounterType == EncounterType.Supply
                && plan.BossMaxHp == 0)
                return true;
            return (reachable & plan.BossEntryLaneMask) != 0;
        }

        internal static int Advance(
            int reachable,
            StageSegmentTemplate segment,
            int validLanes)
        {
            reachable &= segment.EntryLaneMask;
            for (int i = 0;
                i < segment.TraversableLaneMasks.Count && reachable != 0;
                i++)
            {
                reachable = Expand(reachable, validLanes)
                    & segment.TraversableLaneMasks[i];
            }
            return reachable & segment.ExitLaneMask;
        }

        static int Advance(
            int reachable,
            StageSegment segment,
            int validLanes)
        {
            if (!IsLaneMaskValid(segment.EntryLaneMask, validLanes)
                || !IsLaneMaskValid(segment.ExitLaneMask, validLanes)
                || segment.TraversableLaneMasks.Count == 0)
                return 0;

            reachable &= segment.EntryLaneMask;
            for (int i = 0;
                i < segment.TraversableLaneMasks.Count && reachable != 0;
                i++)
            {
                int checkpoint = segment.TraversableLaneMasks[i];
                if (!IsLaneMaskValid(checkpoint, validLanes))
                    return 0;
                reachable = Expand(reachable, validLanes) & checkpoint;
            }
            return reachable & segment.ExitLaneMask;
        }

        static int Expand(int lanes, int validLanes)
        {
            return (lanes | (lanes << 1) | (lanes >> 1)) & validLanes;
        }

        internal static int GetValidLaneMask(int laneCount)
        {
            return (1 << laneCount) - 1;
        }

        internal static void ValidateLaneMask(
            int laneMask,
            int validLanes,
            string parameterName)
        {
            if (!IsLaneMaskValid(laneMask, validLanes))
                throw new ArgumentOutOfRangeException(
                    parameterName, "Lane mask must contain only configured lanes.");
        }

        static bool IsLaneMaskValid(int laneMask, int validLanes)
        {
            return laneMask != 0 && (laneMask & ~validLanes) == 0;
        }
    }
}

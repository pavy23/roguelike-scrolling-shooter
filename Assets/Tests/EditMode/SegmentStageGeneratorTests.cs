using System;
using NUnit.Framework;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;

namespace Shmup.Core.Tests
{
    [TestFixture]
    public class SegmentStageGeneratorTests
    {
        const int Left = 1 << 0;
        const int Center = 1 << 1;
        const int Right = 1 << 2;

        [Test]
        public void SameInputs_ProduceIdenticalStagePlan()
        {
            var generator = CreateGenerator();

            StagePlan first = generator.Generate(0xC0FFEEUL, 1, 2);
            StagePlan second = generator.Generate(0xC0FFEEUL, 1, 2);

            AssertPlansEqual(first, second);
            Assert.IsNull(first.ThemeId);
        }

        [Test]
        public void DurationTargetsDefaultToMeasuredLegacyDurations()
        {
            var catalog = new StageGenerationCatalog(
                3,
                1,
                Center,
                new[] { Segment("segment", Center, Center, Center) },
                new[] { Boss("boss", Center, null) });

            Assert.AreEqual(
                2370,
                StageGenerationCatalog.DefaultTargetDurationTicks);
            Assert.AreEqual(
                3942,
                StageGenerationCatalog.DefaultClosingTargetDurationTicks);
            Assert.AreEqual(
                StageGenerationCatalog.DefaultTargetDurationTicks,
                catalog.TargetDurationTicks);
            Assert.AreEqual(
                StageGenerationCatalog.DefaultClosingTargetDurationTicks,
                catalog.ClosingTargetDurationTicks);
        }

        [Test]
        public void DurationTargetIncludesWholeCrossingSegmentDeterministically()
        {
            var catalog = new StageGenerationCatalog(
                3,
                4,
                Center,
                new[]
                {
                    Segment(
                        "duration_segment",
                        Center,
                        Center,
                        Center,
                        "core")
                },
                new[] { Boss("duration_boss", Center, "core") },
                new[] { "core" },
                targetDurationTicks: 900);
            var generator = new SegmentStageGenerator(catalog);

            StagePlan first = generator.Generate(0x127UL, 1, 1);
            StagePlan second = generator.Generate(0x127UL, 1, 1);

            Assert.AreEqual(2, first.Segments.Count);
            Assert.AreEqual(1200, TotalDurationTicks(first));
            AssertPlansEqual(first, second);
        }

        [Test]
        public void SectionDurationTargetsTakePriorityAndCountsRemainCaps()
        {
            var catalog = new StageGenerationCatalog(
                3,
                3,
                Center,
                new[]
                {
                    Segment(
                        "duration_segment",
                        Center,
                        Center,
                        Center,
                        "core")
                },
                new[] { Boss("duration_boss", Center, "core") },
                new[] { "core" },
                closingSegmentsPerStage: 4,
                targetDurationTicks: 5000,
                closingTargetDurationTicks: 1500);
            var generator = new SegmentStageGenerator(catalog);

            StagePlan opening = generator.GenerateRouteForSection(
                0x1271UL,
                1,
                1,
                "core",
                EncounterType.Normal,
                StageRouteSection.Default);
            StagePlan closing = generator.GenerateRouteForSection(
                0x1271UL,
                1,
                1,
                "core",
                EncounterType.Normal,
                StageRouteSection.Closing);

            Assert.AreEqual(3, opening.Segments.Count,
                "The count must cap an unreachable duration target.");
            Assert.AreEqual(3, closing.Segments.Count,
                "The closing target must stop before its count cap.");
            Assert.AreEqual(1800, TotalDurationTicks(opening));
            Assert.AreEqual(1800, TotalDurationTicks(closing));
        }

        [Test]
        public void DurationTargetTreatsCountAsUpperBoundForShortTerminalRoute()
        {
            var catalog = new StageGenerationCatalog(
                3,
                3,
                Center,
                new[]
                {
                    Segment(
                        "terminal_segment",
                        Center,
                        Left,
                        Center | Left,
                        "core")
                },
                new[] { Boss("left_boss", Left, "core") },
                new[] { "core" },
                targetDurationTicks: 600);
            var generator = new SegmentStageGenerator(catalog);

            Assert.IsTrue(generator.CanGenerateRoute(
                "core",
                1,
                1,
                EncounterType.Normal));
            StagePlan plan = generator.GenerateRoute(
                0x1273UL,
                1,
                1,
                "core",
                EncounterType.Normal);

            Assert.AreEqual(1, plan.Segments.Count);
            Assert.AreEqual(600, TotalDurationTicks(plan));
        }

        [Test]
        public void DurationTargetsRejectNonPositiveValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new StageGenerationCatalog(
                    3,
                    1,
                    Center,
                    new[] { Segment("segment", Center, Center, Center) },
                    new[] { Boss("boss", Center, null) },
                    null,
                    targetDurationTicks: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new StageGenerationCatalog(
                    3,
                    1,
                    Center,
                    new[] { Segment("segment", Center, Center, Center) },
                    new[] { Boss("boss", Center, null) },
                    null,
                    closingTargetDurationTicks: -1));
        }

        [TestCase(24, 27)]
        [TestCase(25, 28)]
        public void PreviousDeterminismSchemasAreExplicitlyRejected(
            int recordingVersion, int suspendVersion)
        {
            Assert.Throws<ArgumentException>(() =>
                SaveDataIntegrity.MigrateAndValidate(
                    new InputRecordingData { schemaVersion = recordingVersion }));
            Assert.Throws<ArgumentException>(() =>
                SaveDataIntegrity.MigrateAndValidate(
                    new RunSuspendData { schemaVersion = suspendVersion }));
        }

        [Test]
        public void PostMidbossOutcome_SelectsTaggedPoolAndFallsBackToDefault()
        {
            var defaultSegment = new StageSegmentTemplate(
                "fortress_default",
                1,
                5,
                60,
                Center,
                Center,
                new[] { Center },
                Array.Empty<SpawnEvent>(),
                Array.Empty<ObstacleSpawn>(),
                "fortress",
                10);
            var cleanSegment = new StageSegmentTemplate(
                "fortress_clean",
                1,
                5,
                60,
                Center,
                Center,
                new[] { Center },
                Array.Empty<SpawnEvent>(),
                Array.Empty<ObstacleSpawn>(),
                "fortress",
                10,
                null,
                new[] { MidbossOutcomeKind.CleanKill });
            var generator = new SegmentStageGenerator(
                new StageGenerationCatalog(
                    3,
                    1,
                    Center,
                    new[] { defaultSegment, cleanSegment },
                    new[] { Boss("fortress_boss", Center, "fortress") },
                    new[] { "fortress" },
                    null,
                    1));

            StagePlan first = generator.GeneratePostMidbossHalf(
                0x101UL,
                3,
                2,
                "fortress",
                MidbossOutcomeKind.CleanKill);
            StagePlan second = generator.GeneratePostMidbossHalf(
                0x101UL,
                3,
                2,
                "fortress",
                MidbossOutcomeKind.CleanKill);
            StagePlan fallback = generator.GeneratePostMidbossHalf(
                0x101UL,
                3,
                2,
                "fortress",
                MidbossOutcomeKind.Attrition);

            Assert.AreEqual("fortress_clean", first.Segments[0].SegmentId);
            AssertPlansEqual(first, second);
            Assert.AreEqual(
                "fortress_default",
                fallback.Segments[0].SegmentId);
        }

        [Test]
        public void MidbossOutcomeEvaluator_PrioritizesPartThenUsesTickThreshold()
        {
            Assert.AreEqual(
                MidbossOutcomeKind.PartFocus,
                MidbossOutcomeEvaluator.Evaluate(999, 100, true));
            Assert.AreEqual(
                MidbossOutcomeKind.CleanKill,
                MidbossOutcomeEvaluator.Evaluate(100, 100, false));
            Assert.AreEqual(
                MidbossOutcomeKind.Attrition,
                MidbossOutcomeEvaluator.Evaluate(101, 100, false));
            Assert.AreEqual(
                MidbossOutcomeKind.Default,
                MidbossOutcomeEvaluator.Evaluate(101, 0, false));
        }

        [Test]
        public void PostMidbossOutcome_UsesTaggedPiecesBeforeUnusedOrdinaryPieces()
        {
            var generator = OutcomeGenerator(5, new[]
            {
                OutcomeSegment("clean_a"), OutcomeSegment("clean_b"),
                Segment("normal_a", Center, Center, Center, "fortress"),
                Segment("normal_b", Center, Center, Center, "fortress"),
                Segment("normal_c", Center, Center, Center, "fortress")
            });

            for (ulong seed = 0; seed < 64; seed++)
            {
                StagePlan plan = generator.GeneratePostMidbossHalf(
                    seed, 3, 3, "fortress", MidbossOutcomeKind.CleanKill);
                Assert.AreEqual(5, plan.Segments.Count);
                Assert.AreEqual(0, plan.SegmentReuseCount, $"seed {seed}");
                StringAssert.StartsWith("clean_", plan.Segments[0].SegmentId);
                StringAssert.StartsWith("clean_", plan.Segments[1].SegmentId);
                for (int i = 2; i < plan.Segments.Count; i++)
                    StringAssert.StartsWith("normal_", plan.Segments[i].SegmentId);
                Assert.IsTrue(StagePlanClearability.IsClearable(plan));
                AssertPlansEqual(plan, generator.GeneratePostMidbossHalf(
                    seed, 3, 3, "fortress", MidbossOutcomeKind.CleanKill));
            }
        }

        [TestCase("other_theme")]
        [TestCase("too_hard")]
        [TestCase("too_easy")]
        [TestCase("other_outcome")]
        [TestCase("unreachable_entry")]
        [TestCase("no_outcome_continuation")]
        public void PostMidbossOutcome_DoesNotBorrowIneligibleOrdinaryPieces(string reason)
        {
            int entry = reason == "unreachable_entry" ? Left : Center;
            int exit = reason == "no_outcome_continuation" ? Left : entry;
            var excluded = new StageSegmentTemplate(
                "excluded", reason == "too_hard" ? 4 : 1,
                reason == "too_easy" ? 2 : 5, 600, entry, exit,
                new[] { entry | exit }, Array.Empty<SpawnEvent>(),
                Array.Empty<ObstacleSpawn>(),
                reason == "other_theme" ? "hive" : "fortress", 10, null,
                reason == "other_outcome" ? new[] { MidbossOutcomeKind.PartFocus } : null);
            // The ordinary piece could end at the left boss, but cannot return
            // to the tagged pool when another slot remains.
            var generator = new SegmentStageGenerator(new StageGenerationCatalog(
                3, 1, Center, new[] { OutcomeSegment("clean"), excluded },
                new[] { Boss("boss", Center | Left, "fortress") },
                null, closingSegmentsPerStage: 3));

            StagePlan plan = generator.GeneratePostMidbossHalf(
                17, 3, 3, "fortress", MidbossOutcomeKind.CleanKill);
            Assert.AreEqual("clean", plan.Segments[0].SegmentId);
            Assert.AreEqual("clean", plan.Segments[1].SegmentId);
            // At the final slot, a terminal piece may legitimately reach a boss.
            Assert.AreEqual(reason == "no_outcome_continuation" ? "excluded" : "clean",
                plan.Segments[2].SegmentId);
            Assert.IsTrue(StagePlanClearability.IsClearable(plan));
        }

        [Test]
        public void PostMidbossOutcome_ExhaustedCombinedPoolStillAvoidsAdjacentRepeats()
        {
            var generator = OutcomeGenerator(6, new[]
            {
                OutcomeSegment("clean_a"), OutcomeSegment("clean_b"),
                Segment("normal", Center, Center, Center, "fortress")
            });
            for (ulong seed = 0; seed < 64; seed++)
            {
                StagePlan plan = generator.GeneratePostMidbossHalf(
                    seed, 3, 3, "fortress", MidbossOutcomeKind.CleanKill);
                Assert.AreEqual(3, plan.SegmentReuseCount);
                Assert.AreEqual("normal", plan.Segments[2].SegmentId);
                for (int i = 1; i < plan.Segments.Count; i++)
                    Assert.AreNotEqual(plan.Segments[i - 1].SegmentId, plan.Segments[i].SegmentId);
                Assert.IsTrue(StagePlanClearability.IsClearable(plan));
            }
        }

        [Test]
        public void PostMidbossOutcome_BorrowedPiecesStillRespectWholeSegmentDurationTarget()
        {
            var generator = OutcomeGenerator(5, new[]
            {
                OutcomeSegment("clean_a"), OutcomeSegment("clean_b"),
                Segment("normal_a", Center, Center, Center, "fortress"),
                Segment("normal_b", Center, Center, Center, "fortress")
            }, 1500);
            StagePlan plan = generator.GeneratePostMidbossHalf(
                17, 3, 3, "fortress", MidbossOutcomeKind.CleanKill);
            Assert.AreEqual(3, plan.Segments.Count);
            Assert.AreEqual(1800, TotalDurationTicks(plan));
            Assert.AreEqual(0, plan.SegmentReuseCount);
            Assert.AreEqual(600, plan.Segments[2].LengthTicks);
            Assert.AreEqual(60, plan.Segments[2].Spawns[0].Tick);
        }

        [TestCase(MidbossOutcomeKind.Default)]
        [TestCase(MidbossOutcomeKind.Attrition)]
        [TestCase(MidbossOutcomeKind.PartFocus)]
        public void PostMidbossOutcome_OrdinaryAndUnsupportedOutcomesKeepOriginalPool(
            MidbossOutcomeKind outcome)
        {
            var ordinary = new[]
            {
                Segment("normal_a", Center, Center, Center, "fortress"),
                Segment("normal_b", Center, Center, Center, "fortress")
            };
            var original = OutcomeGenerator(5, ordinary);
            var withTagged = OutcomeGenerator(5, new[]
            {
                ordinary[0], ordinary[1], OutcomeSegment("clean")
            });
            for (ulong seed = 0; seed < 32; seed++)
                AssertPlansEqual(
                    original.GeneratePostMidbossHalf(seed, 3, 3, "fortress", outcome),
                    withTagged.GeneratePostMidbossHalf(seed, 3, 3, "fortress", outcome));
        }

        static SegmentStageGenerator OutcomeGenerator(
            int closingCount, StageSegmentTemplate[] segments, int? duration = null)
        {
            return new SegmentStageGenerator(new StageGenerationCatalog(
                3, 1, Center, segments,
                new[] { Boss("fortress_boss", Center, "fortress") },
                new[] { "fortress" }, closingSegmentsPerStage: closingCount,
                closingTargetDurationTicks: duration));
        }

        static StageSegmentTemplate OutcomeSegment(string id)
        {
            return new StageSegmentTemplate(id, 1, 5, 600, Center, Center,
                new[] { Center }, new[] { new SpawnEvent(60, "zako_straight", 100, 0) },
                Array.Empty<ObstacleSpawn>(), "fortress", 10, null,
                new[] { MidbossOutcomeKind.CleanKill });
        }

        [Test]
        public void NormalGenerationNeverSelectsHiddenOnlyColossalBosses()
        {
            var catalog = new StageGenerationCatalog(
                3,
                1,
                Center,
                new[]
                {
                    Segment(
                        "core_segment",
                        Center,
                        Center,
                        Center,
                        "core")
                },
                new[]
                {
                    Boss("normal_core_boss", Center, "core"),
                    Boss(
                        SegmentStageGenerator.LeviathanBossId,
                        Center,
                        null),
                    Boss(
                        SegmentStageGenerator.BroodmotherBossId,
                        Center,
                        null)
                },
                new[] { "core" });
            var generator = new SegmentStageGenerator(catalog);

            for (ulong seed = 0; seed < 128; seed++)
            {
                StagePlan plan = generator.Generate(
                    seed,
                    5,
                    1);
                Assert.AreEqual("normal_core_boss", plan.BossId);
            }
            Assert.IsTrue(
                generator.CanGenerateColossalBoss(
                    ColossalBossKind.Leviathan));
            Assert.IsTrue(
                generator.CanGenerateColossalBoss(
                    ColossalBossKind.Broodmother));
            Assert.AreEqual(
                SegmentStageGenerator.LeviathanBossId,
                generator.GenerateColossalBoss(
                    1UL,
                    5,
                    1,
                    ColossalBossKind.Leviathan).BossId);
        }

        [Test]
        public void ThemePermutation_UsesExplicitOrderAndFiltersTemplates()
        {
            var catalog = new StageGenerationCatalog(
                3,
                1,
                Center,
                new[]
                {
                    Segment("nebula_segment", Center, Center, Center, "nebula"),
                    Segment("core_segment", Center, Center, Center, "core"),
                    Segment("hive_segment", Center, Center, Center, "hive")
                },
                new[]
                {
                    Boss("nebula_boss", Center, "nebula"),
                    Boss("core_boss", Center, "core"),
                    Boss("hive_boss", Center, "hive")
                },
                new[] { "nebula", "hive", "core" });
            var generator = new SegmentStageGenerator(catalog);

            CollectionAssert.AreEqual(
                new[] { "nebula", "hive", "core" },
                catalog.ThemeIds);
            AssertThemePlan(generator.Generate(77UL, 1, 1), "nebula");
            StagePlan second = generator.Generate(77UL, 2, 1);
            StagePlan third = generator.Generate(77UL, 3, 1);
            CollectionAssert.AreEquivalent(
                new[] { "hive", "core" },
                new[] { second.ThemeId, third.ThemeId });
            AssertThemePlan(second, second.ThemeId);
            AssertThemePlan(third, third.ThemeId);
            AssertThemePlan(generator.Generate(77UL, 4, 1), "nebula");
            AssertPlansEqual(
                generator.Generate(77UL, 2, 1),
                generator.Generate(77UL, 2, 1));
        }

        [Test]
        public void ThemePermutation_WithoutExplicitOrderUsesOrdinalUnion()
        {
            var catalog = new StageGenerationCatalog(
                3,
                1,
                Center,
                new[]
                {
                    Segment("nebula_segment", Center, Center, Center, "nebula"),
                    Segment("core_segment", Center, Center, Center, "core"),
                    Segment("hive_segment", Center, Center, Center, "hive")
                },
                new[]
                {
                    Boss("nebula_boss", Center, "nebula"),
                    Boss("core_boss", Center, "core"),
                    Boss("hive_boss", Center, "hive")
                });
            var generator = new SegmentStageGenerator(catalog);

            CollectionAssert.AreEqual(
                new[] { "core", "hive", "nebula" },
                catalog.ThemeIds);
            AssertThemePlan(generator.Generate(77UL, 1, 1), "core");
            StagePlan second = generator.Generate(77UL, 2, 1);
            StagePlan third = generator.Generate(77UL, 3, 1);
            CollectionAssert.AreEquivalent(
                new[] { "hive", "nebula" },
                new[] { second.ThemeId, third.ThemeId });
        }

        [Test]
        public void UntaggedTemplates_AreEligibleForEveryCatalogTheme()
        {
            var catalog = new StageGenerationCatalog(
                3,
                1,
                Center,
                new[] { Segment("global_segment", Center, Center, Center) },
                new[]
                {
                    Boss("scrapyard_boss", Center, "scrapyard"),
                    Boss("hive_boss", Center, "hive")
                });
            var generator = new SegmentStageGenerator(catalog);

            StagePlan hive = generator.Generate(1UL, 1, 1);
            StagePlan scrapyard = generator.Generate(1UL, 2, 1);

            Assert.AreEqual("hive", hive.ThemeId);
            Assert.AreEqual("global_segment", hive.Segments[0].SegmentId);
            Assert.AreEqual("hive_boss", hive.BossId);
            Assert.AreEqual("scrapyard", scrapyard.ThemeId);
            Assert.AreEqual("global_segment", scrapyard.Segments[0].SegmentId);
            Assert.AreEqual("scrapyard_boss", scrapyard.BossId);
        }

        [Test]
        public void UnassemblableShuffledTheme_UsesObservableDeterministicFallback()
        {
            var catalog = new StageGenerationCatalog(
                3,
                1,
                Center,
                new[]
                {
                    Segment("hive_dead_end", Center, Left, Center | Left, "hive"),
                    Segment("scrapyard_route", Center, Right, Center | Right, "scrapyard")
                },
                new[]
                {
                    Boss("hive_boss", Right, "hive"),
                    Boss("scrapyard_boss", Right, "scrapyard")
                });
            var generator = new SegmentStageGenerator(catalog);

            StagePlan first = generator.Generate(1UL, 1, 1);
            StagePlan repeated = generator.Generate(1UL, 1, 1);

            Assert.AreEqual("hive", first.RequestedThemeId);
            Assert.AreEqual("scrapyard", first.ThemeId);
            Assert.IsTrue(first.ThemeFallbackApplied);
            Assert.AreEqual(
                "scrapyard_route",
                first.Segments[0].SegmentId);
            Assert.AreEqual("scrapyard_boss", first.BossId);
            Assert.IsTrue(StagePlanClearability.IsClearable(first));
            AssertPlansEqual(first, repeated);
        }

        [Test]
        public void SeededPermutation_FixesStageOneAndVariesAcrossRunSeeds()
        {
            var generator = CreateThemedGenerator();
            string firstPermutation = null;
            bool foundDifferentPermutation = false;

            for (ulong seed = 0; seed < 64; seed++)
            {
                StagePlan stageOne = generator.Generate(seed, 1, 1);
                Assert.AreEqual("scrapyard", stageOne.ThemeId);
                Assert.AreEqual("scrapyard", stageOne.RequestedThemeId);
                Assert.IsFalse(stageOne.ThemeFallbackApplied);
                StagePlan stageFive =
                    generator.Generate(seed, 5, 1);
                Assert.AreEqual("core", stageFive.ThemeId);
                Assert.AreEqual("core", stageFive.RequestedThemeId);
                Assert.IsFalse(stageFive.ThemeFallbackApplied);

                string permutation = ThemeSequence(
                    generator,
                    seed,
                    2,
                    4);
                Assert.AreEqual(
                    permutation,
                    ThemeSequence(generator, seed, 2, 4));

                if (firstPermutation == null)
                    firstPermutation = permutation;
                else if (!string.Equals(
                    firstPermutation,
                    permutation,
                    StringComparison.Ordinal))
                    foundDifferentPermutation = true;
            }

            Assert.IsTrue(
                foundDifferentPermutation,
                "Different run seeds should produce more than one theme order.");
        }

        [Test]
        public void IndependentStageCalls_ReproduceResumeAndReplayThemeOrder()
        {
            const ulong Seed = 0x5EEDC0DEUL;
            var uninterrupted = CreateThemedGenerator();
            var resumed = CreateThemedGenerator();
            var replayed = CreateThemedGenerator();

            var expected = new StagePlan[7];
            for (int stageIndex = 1; stageIndex <= expected.Length; stageIndex++)
                expected[stageIndex - 1] =
                    uninterrupted.Generate(Seed, stageIndex, 1);

            int[] resumeOrder = { 5, 2, 7, 1, 6, 3, 4 };
            for (int i = 0; i < resumeOrder.Length; i++)
            {
                int stageIndex = resumeOrder[i];
                AssertPlansEqual(
                    expected[stageIndex - 1],
                    resumed.Generate(Seed, stageIndex, 1));
            }

            for (int stageIndex = 1; stageIndex <= expected.Length; stageIndex++)
                AssertPlansEqual(
                    expected[stageIndex - 1],
                    replayed.Generate(Seed, stageIndex, 1));

            Assert.AreEqual(expected[0].ThemeId, expected[5].ThemeId);
        }

        [Test]
        public void GeneratedPlans_AreClearableAcrossManySeeds()
        {
            var generator = CreateGenerator();

            for (ulong seed = 0; seed < 1000; seed++)
            {
                StagePlan plan = generator.Generate(seed, 1, 2);
                Assert.IsTrue(
                    StagePlanClearability.IsClearable(plan),
                    $"seed {seed} produced an unclearable stage");
            }
        }

        [Test]
        public void Generator_LooksAheadAndRejectsLocallyValidDeadEnd()
        {
            var generator = CreateGenerator();

            for (ulong seed = 0; seed < 100; seed++)
            {
                StagePlan plan = generator.Generate(seed, 1, 2);
                Assert.AreEqual("to_right", plan.Segments[0].SegmentId);
                Assert.AreEqual("hold_right", plan.Segments[1].SegmentId);
            }
        }

        [Test]
        public void SufficientPool_UsesUniqueSegmentsAcrossWholeStage()
        {
            var catalog = new StageGenerationCatalog(
                3,
                3,
                Center,
                new[]
                {
                    Segment("alpha", Center, Center, Center),
                    Segment("beta", Center, Center, Center),
                    Segment("gamma", Center, Center, Center),
                    Segment("delta", Center, Center, Center)
                },
                new[]
                {
                    new StageBossTemplate("boss", 1, 1, 1, 5, Center)
                });
            var generator = new SegmentStageGenerator(catalog);

            for (ulong seed = 0; seed < 100; seed++)
            {
                StagePlan plan = generator.Generate(seed, 1, 2);

                Assert.AreEqual(0, plan.SegmentReuseCount);
                Assert.IsFalse(plan.SegmentReuseApplied);
                for (int i = 0; i < plan.Segments.Count; i++)
                {
                    for (int earlier = 0; earlier < i; earlier++)
                    {
                        Assert.AreNotEqual(
                            plan.Segments[earlier].SegmentId,
                            plan.Segments[i].SegmentId,
                            $"seed {seed} repeated a segment");
                    }
                }
                Assert.IsTrue(StagePlanClearability.IsClearable(plan));
            }
        }

        [Test]
        public void SegmentWeightsBiasSelectionWithoutBreakingDeterminism()
        {
            var catalog = new StageGenerationCatalog(
                3,
                1,
                Center,
                new[]
                {
                    WeightedSegment("common", 100),
                    WeightedSegment("special", 1)
                },
                new[]
                {
                    new StageBossTemplate("boss", 1, 1, 1, 5, Center)
                });
            var generator = new SegmentStageGenerator(catalog);
            int commonCount = 0;
            int specialCount = 0;

            for (ulong seed = 0; seed < 1000; seed++)
            {
                StagePlan first = generator.Generate(seed, 1, 2);
                StagePlan repeated = generator.Generate(seed, 1, 2);
                Assert.AreEqual(
                    first.Segments[0].SegmentId,
                    repeated.Segments[0].SegmentId);
                Assert.IsTrue(StagePlanClearability.IsClearable(first));
                if (first.Segments[0].SegmentId == "common")
                    commonCount++;
                else
                    specialCount++;
            }

            Assert.Greater(commonCount, specialCount * 20);
            Assert.Greater(specialCount, 0);
        }

        [Test]
        public void InsufficientPool_AssemblesAndAvoidsAdjacentRepeats()
        {
            var generator = CreateInsufficientPoolGenerator();

            for (ulong seed = 0; seed < 100; seed++)
            {
                StagePlan plan = generator.Generate(seed, 1, 2);

                Assert.AreEqual(4, plan.Segments.Count);
                Assert.AreEqual(2, plan.SegmentReuseCount);
                Assert.IsTrue(plan.SegmentReuseApplied);
                for (int i = 1; i < plan.Segments.Count; i++)
                {
                    Assert.AreNotEqual(
                        plan.Segments[i - 1].SegmentId,
                        plan.Segments[i].SegmentId,
                        $"seed {seed} repeated adjacent segments");
                }
                Assert.IsTrue(StagePlanClearability.IsClearable(plan));
            }
        }

        [Test]
        public void InsufficientPool_RelaxedSelectionRemainsDeterministic()
        {
            var generator = CreateInsufficientPoolGenerator();

            StagePlan first = generator.Generate(20260729UL, 1, 2);
            StagePlan repeated = generator.Generate(20260729UL, 1, 2);

            AssertPlansEqual(first, repeated);
            Assert.IsTrue(first.SegmentReuseApplied);
        }

        [Test]
        public void SingleCandidatePool_AllowsAdjacentRepeatsAsLastResort()
        {
            var catalog = new StageGenerationCatalog(
                3,
                3,
                Center,
                new[]
                {
                    Segment("only_route", Center, Center, Center)
                },
                new[]
                {
                    new StageBossTemplate("boss", 1, 1, 1, 5, Center)
                });
            var generator = new SegmentStageGenerator(catalog);

            StagePlan plan = generator.Generate(42UL, 1, 2);

            Assert.AreEqual(3, plan.Segments.Count);
            Assert.AreEqual(2, plan.SegmentReuseCount);
            Assert.IsTrue(plan.SegmentReuseApplied);
            Assert.AreEqual(
                plan.Segments[0].SegmentId,
                plan.Segments[1].SegmentId);
            Assert.AreEqual(
                plan.Segments[1].SegmentId,
                plan.Segments[2].SegmentId);
            Assert.IsTrue(StagePlanClearability.IsClearable(plan));
        }

        [Test]
        public void NoClearableAssembly_ThrowsInsteadOfReturningImpossiblePlan()
        {
            var catalog = new StageGenerationCatalog(
                3,
                1,
                Center,
                new[]
                {
                    Segment("to_left", Center, Left, Center | Left)
                },
                new[]
                {
                    new StageBossTemplate("right_boss", 1, 1, 1, 5, Right)
                });
            var generator = new SegmentStageGenerator(catalog);

            Assert.Throws<InvalidOperationException>(
                () => generator.Generate(1UL, 1, 2));
        }

        [Test]
        public void InvalidInputs_AreRejected()
        {
            var generator = CreateGenerator();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => generator.Generate(1UL, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => generator.Generate(1UL, 1, 0));
        }

        [Test]
        public void BossTemplate_DefensivelyCopiesPhaseList()
        {
            var original = new BossPhase(30, 2, 256, 60);
            var replacement = new BossPhase(10, 5, 512, 60);
            var source = new[] { original };
            var boss = new StageBossTemplate(
                "boss", 1, 5, 1, 5, Center,
                100, 256, 256, 2048, source);

            source[0] = replacement;

            Assert.AreSame(original, boss.Phases[0]);
            Assert.Throws<ArgumentException>(
                () => new StageBossTemplate(
                    "invalid", 1, 5, 1, 5, Center,
                    100, 256, 256, 2048, new BossPhase[] { null }));
        }

        [Test]
        public void SeededObstacleJitter_IsDeterministicAndVariesAcrossAllStages()
        {
            var generator = CreateObstacleGenerator();
            int units = SimSpace.SubUnitsPerWorldUnit;

            for (int stageIndex = 1; stageIndex <= 7; stageIndex++)
            {
                StagePlan first = generator.Generate(0UL, stageIndex, 1);
                StagePlan repeated = generator.Generate(0UL, stageIndex, 1);
                AssertPlansEqual(first, repeated);

                ObstacleSpawn solid = first.Segments[0].Obstacles[0];
                ObstacleSpawn breakable = first.Segments[0].Obstacles[1];
                Assert.AreEqual(10 * units, solid.X);
                Assert.AreEqual(12 * units, breakable.X);
                Assert.LessOrEqual(
                    Math.Abs(solid.Y - 2 * units),
                    SegmentStageGenerator.SolidJitterSubUnits);
                Assert.LessOrEqual(
                    Math.Abs(breakable.Y + 2 * units),
                    SegmentStageGenerator.BreakableJitterSubUnits);

                bool foundDifferentCoordinates = false;
                for (ulong seed = 1; seed <= 64; seed++)
                {
                    StagePlan candidate = generator.Generate(
                        seed,
                        stageIndex,
                        1);
                    if (candidate.Segments[0].Obstacles[0].Y != solid.Y
                        || candidate.Segments[0].Obstacles[1].Y
                            != breakable.Y)
                    {
                        foundDifferentCoordinates = true;
                        break;
                    }
                }
                Assert.IsTrue(
                    foundDifferentCoordinates,
                    $"stage {stageIndex} did not vary obstacle coordinates");
            }
        }

        [Test]
        public void ObstacleJitterStream_DoesNotChangeSegmentOrBossSelection()
        {
            var plain = CreateGenerator();
            var withObstacles = new SegmentStageGenerator(
                new StageGenerationCatalog(
                    3,
                    2,
                    Center,
                    new[]
                    {
                        ObstacleSegment("to_left", Center, Left, Center | Left),
                        ObstacleSegment("to_right", Center, Right, Center | Right),
                        ObstacleSegment("hold_left", Left, Left, Left),
                        ObstacleSegment("hold_right", Right, Right, Right)
                    },
                    new[]
                    {
                        new StageBossTemplate(
                            "right_boss_a", 1, 1, 1, 5, Right),
                        new StageBossTemplate(
                            "right_boss_b", 1, 1, 1, 5, Right)
                    }));

            for (ulong seed = 0; seed < 128; seed++)
            {
                StagePlan expected = plain.Generate(seed, 1, 2);
                StagePlan actual = withObstacles.Generate(seed, 1, 2);
                Assert.AreEqual(
                    expected.Segments[0].SegmentId,
                    actual.Segments[0].SegmentId);
                Assert.AreEqual(
                    expected.Segments[1].SegmentId,
                    actual.Segments[1].SegmentId);
                Assert.AreEqual(expected.BossId, actual.BossId);
            }
        }

        static SegmentStageGenerator CreateGenerator()
        {
            var catalog = new StageGenerationCatalog(
                3,
                2,
                Center,
                new[]
                {
                    Segment("to_left", Center, Left, Center | Left),
                    Segment("to_right", Center, Right, Center | Right),
                    Segment("hold_left", Left, Left, Left),
                    Segment("hold_right", Right, Right, Right)
                },
                new[]
                {
                    new StageBossTemplate("right_boss_a", 1, 1, 1, 5, Right),
                    new StageBossTemplate("right_boss_b", 1, 1, 1, 5, Right)
                });
            return new SegmentStageGenerator(catalog);
        }

        static SegmentStageGenerator CreateInsufficientPoolGenerator()
        {
            var catalog = new StageGenerationCatalog(
                3,
                5,
                Center,
                new[]
                {
                    Segment("alpha", Center, Center, Center),
                    Segment("beta", Center, Center, Center)
                },
                new[]
                {
                    new StageBossTemplate("boss", 1, 1, 1, 5, Center)
                });
            return new SegmentStageGenerator(catalog);
        }

        static SegmentStageGenerator CreateObstacleGenerator()
        {
            return new SegmentStageGenerator(
                new StageGenerationCatalog(
                    3,
                    1,
                    Center,
                    new[]
                    {
                        ObstacleSegment(
                            "obstacle_route",
                            Center,
                            Center,
                            Center)
                    },
                    new[]
                    {
                        new StageBossTemplate(
                            "boss", 1, 10, 1, 5, Center)
                    }));
        }

        static StageSegmentTemplate ObstacleSegment(
            string id,
            int entry,
            int exit,
            int checkpoint)
        {
            int units = SimSpace.SubUnitsPerWorldUnit;
            return new StageSegmentTemplate(
                id,
                1,
                5,
                600,
                entry,
                exit,
                new[] { checkpoint },
                Array.Empty<SpawnEvent>(),
                new[]
                {
                    new ObstacleSpawn(
                        ObstacleType.Solid,
                        10 * units,
                        2 * units,
                        0),
                    new ObstacleSpawn(
                        ObstacleType.Breakable,
                        12 * units,
                        -2 * units,
                        3)
                },
                null);
        }

        static SegmentStageGenerator CreateThemedGenerator()
        {
            string[] themes =
            {
                "scrapyard",
                "hive",
                "fortress",
                "nebula",
                "core"
            };
            var segments = new StageSegmentTemplate[themes.Length];
            var bosses = new StageBossTemplate[themes.Length];
            for (int i = 0; i < themes.Length; i++)
            {
                segments[i] = Segment(
                    themes[i] + "_segment",
                    Center,
                    Center,
                    Center,
                    themes[i]);
                bosses[i] = Boss(
                    themes[i] + "_boss",
                    Center,
                    themes[i]);
            }

            return new SegmentStageGenerator(
                new StageGenerationCatalog(
                    3,
                    1,
                    Center,
                    segments,
                    bosses,
                    themes));
        }

        static string ThemeSequence(
            SegmentStageGenerator generator,
            ulong seed,
            int firstStage,
            int lastStage)
        {
            string sequence = string.Empty;
            for (int stageIndex = firstStage;
                stageIndex <= lastStage;
                stageIndex++)
            {
                if (sequence.Length > 0)
                    sequence += "|";
                sequence += generator.Generate(seed, stageIndex, 1).ThemeId;
            }
            return sequence;
        }

        static int TotalDurationTicks(StagePlan plan)
        {
            int total = 0;
            for (int i = 0; i < plan.Segments.Count; i++)
                total += plan.Segments[i].LengthTicks;
            return total;
        }

        static StageSegmentTemplate Segment(
            string id,
            int entry,
            int exit,
            int checkpoint,
            string themeId = null)
        {
            return new StageSegmentTemplate(
                id,
                1,
                5,
                600,
                entry,
                exit,
                new[] { checkpoint },
                new[]
                {
                    new SpawnEvent(
                        60,
                        "zako_straight",
                        12 * SimSpace.SubUnitsPerWorldUnit,
                        0)
                },
                themeId);
        }

        static StageSegmentTemplate WeightedSegment(
            string id,
            int weight)
        {
            return new StageSegmentTemplate(
                id,
                1,
                5,
                600,
                Center,
                Center,
                new[] { Center },
                Array.Empty<SpawnEvent>(),
                Array.Empty<ObstacleSpawn>(),
                null,
                weight);
        }

        static StageBossTemplate Boss(string id, int entry, string themeId)
        {
            return new StageBossTemplate(
                id,
                1,
                10,
                1,
                5,
                entry,
                0,
                0,
                0,
                0,
                Array.Empty<BossPhase>(),
                themeId);
        }

        static void AssertThemePlan(StagePlan plan, string expectedTheme)
        {
            Assert.AreEqual(expectedTheme, plan.ThemeId);
            Assert.AreEqual(expectedTheme + "_segment", plan.Segments[0].SegmentId);
            Assert.AreEqual(expectedTheme + "_boss", plan.BossId);
            Assert.IsTrue(StagePlanClearability.IsClearable(plan));
        }

        static void AssertPlansEqual(StagePlan expected, StagePlan actual)
        {
            Assert.AreEqual(expected.ThemeId, actual.ThemeId);
            Assert.AreEqual(
                expected.RequestedThemeId,
                actual.RequestedThemeId);
            Assert.AreEqual(
                expected.ThemeFallbackApplied,
                actual.ThemeFallbackApplied);
            Assert.AreEqual(
                expected.EncounterType,
                actual.EncounterType);
            Assert.AreEqual(
                expected.SegmentReuseCount,
                actual.SegmentReuseCount);
            Assert.AreEqual(
                expected.SegmentReuseApplied,
                actual.SegmentReuseApplied);
            Assert.AreEqual(expected.BossId, actual.BossId);
            Assert.AreEqual(expected.LaneCount, actual.LaneCount);
            Assert.AreEqual(expected.StartLaneMask, actual.StartLaneMask);
            Assert.AreEqual(expected.BossEntryLaneMask, actual.BossEntryLaneMask);
            Assert.AreEqual(expected.BossMaxHp, actual.BossMaxHp);
            Assert.AreEqual(expected.BossHalfWidth, actual.BossHalfWidth);
            Assert.AreEqual(expected.BossHalfHeight, actual.BossHalfHeight);
            Assert.AreEqual(expected.BossHoldX, actual.BossHoldX);
            Assert.AreEqual(expected.BossPhases.Count, actual.BossPhases.Count);
            for (int i = 0; i < expected.BossPhases.Count; i++)
            {
                Assert.AreEqual(
                    expected.BossPhases[i].FireIntervalTicks,
                    actual.BossPhases[i].FireIntervalTicks);
                Assert.AreEqual(expected.BossPhases[i].Ways, actual.BossPhases[i].Ways);
                Assert.AreEqual(
                    expected.BossPhases[i].BulletSpeedNumerator,
                    actual.BossPhases[i].BulletSpeedNumerator);
                Assert.AreEqual(
                    expected.BossPhases[i].BulletSpeedDenominator,
                    actual.BossPhases[i].BulletSpeedDenominator);
                Assert.AreEqual(
                    expected.BossPhases[i].DurationTicks,
                    actual.BossPhases[i].DurationTicks);
                Assert.AreEqual(
                    expected.BossPhases[i].TelegraphTicks,
                    actual.BossPhases[i].TelegraphTicks);
                Assert.AreEqual(
                    expected.BossPhases[i].FirePattern,
                    actual.BossPhases[i].FirePattern);
            }
            Assert.AreEqual(expected.Segments.Count, actual.Segments.Count);

            for (int i = 0; i < expected.Segments.Count; i++)
            {
                StageSegment expectedSegment = expected.Segments[i];
                StageSegment actualSegment = actual.Segments[i];
                Assert.AreEqual(expectedSegment.SegmentId, actualSegment.SegmentId);
                Assert.AreEqual(expectedSegment.LengthTicks, actualSegment.LengthTicks);
                Assert.AreEqual(
                    expectedSegment.ScrollSpeedMultiplierNumerator,
                    actualSegment.ScrollSpeedMultiplierNumerator);
                Assert.AreEqual(
                    expectedSegment.ScrollSpeedMultiplierDenominator,
                    actualSegment.ScrollSpeedMultiplierDenominator);
                Assert.AreEqual(expectedSegment.EntryLaneMask, actualSegment.EntryLaneMask);
                Assert.AreEqual(expectedSegment.ExitLaneMask, actualSegment.ExitLaneMask);
                CollectionAssert.AreEqual(
                    expectedSegment.TraversableLaneMasks,
                    actualSegment.TraversableLaneMasks);
                Assert.AreEqual(expectedSegment.Spawns.Count, actualSegment.Spawns.Count);

                for (int spawn = 0; spawn < expectedSegment.Spawns.Count; spawn++)
                {
                    SpawnEvent expectedSpawn = expectedSegment.Spawns[spawn];
                    SpawnEvent actualSpawn = actualSegment.Spawns[spawn];
                    Assert.AreEqual(expectedSpawn.Tick, actualSpawn.Tick);
                    Assert.AreEqual(expectedSpawn.EnemyId, actualSpawn.EnemyId);
                    Assert.AreEqual(expectedSpawn.X, actualSpawn.X);
                    Assert.AreEqual(expectedSpawn.Y, actualSpawn.Y);
                }

                Assert.AreEqual(
                    expectedSegment.Obstacles.Count,
                    actualSegment.Obstacles.Count);
                for (int obstacle = 0;
                    obstacle < expectedSegment.Obstacles.Count;
                    obstacle++)
                {
                    ObstacleSpawn expectedObstacle =
                        expectedSegment.Obstacles[obstacle];
                    ObstacleSpawn actualObstacle =
                        actualSegment.Obstacles[obstacle];
                    Assert.AreEqual(
                        expectedObstacle.Type,
                        actualObstacle.Type);
                    Assert.AreEqual(
                        expectedObstacle.X,
                        actualObstacle.X);
                    Assert.AreEqual(
                        expectedObstacle.Y,
                        actualObstacle.Y);
                    Assert.AreEqual(
                        expectedObstacle.Hp,
                        actualObstacle.Hp);
                    Assert.AreEqual(
                        expectedObstacle.BlocksEnemyBullets,
                        actualObstacle.BlocksEnemyBullets);
                    Assert.AreEqual(
                        expectedObstacle.RegenDelayTicks,
                        actualObstacle.RegenDelayTicks);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Shmup.Core;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;
using Shmup.Presentation.Battle;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class BattlePresentationRegressionTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root;
        GameObject _prefab;
        BattleDirector _director;

        [SetUp]
        public void SetUp()
        {
            // Keep scene bootstrapping, saves and automatic updates out of EditMode tests.
            _root = new GameObject("Presentation regression test");
            _root.SetActive(false);
            _director = _root.AddComponent<BattleDirector>();
            _prefab = new GameObject("Test sprite", typeof(SpriteRenderer));
            _prefab.transform.SetParent(_root.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [TestCase(0, 0f)]
        [TestCase(192, 0f)]
        [TestCase(256, 0f)]
        [TestCase(0, 0.29f)]
        [TestCase(192, 0.29f)]
        [TestCase(256, 0.29f)]
        public void RegeneratingObstacleKeepsItsAuthoredScale(int halfWidth, float age)
        {
            var config = BattleSimConfig.CreateDefault();
            config.ObstacleHalfWidth = 128;
            var obstacle = new ObstacleSpawn(ObstacleType.Breakable, 1024, 0, 10,
                null, false, 60, halfWidth, halfWidth);
            var plan = Plan(obstacle);
            var sim = new BattleSim(config, new Rng(1UL), plan, Content(), PowerUpGauge.CreateDefault());
            Set(_director, "_sim", sim);
            Set(_director, "_obstacleBaseHalfWidth", config.ObstacleHalfWidth);
            var pool = new SpritePool(_prefab, _root.transform, 1);
            Set(_director, "_obstaclePool", pool);
            Invoke(_director, "SyncObstacles", 0f);
            var views = Get<Dictionary<int, Transform>>(_director, "_obstacleViews");
            int id = sim.Obstacles[0].Id;
            float originalScale = views[id].localScale.x;

            // The destroyed wall returns its view, then the same id regenerates.
            pool.Release(views[id]);
            views.Remove(id);
            Get<Dictionary<int, float>>(_director, "_obstacleRegenAges")[id] = age;
            Invoke(_director, "SyncObstacles", 0.02f);
            if (age > 0f)
            {
                Assert.That(views[id].localScale.x, Is.EqualTo(originalScale).Within(0.0001f));
                Assert.IsFalse(Get<Dictionary<int, float>>(_director, "_obstacleRegenAges").ContainsKey(id));
            }
            else
            {
                // Compare two actual views at the same animation age, with different hitboxes.
                float largeScale = views[id].localScale.x;
                var defaultSim = new BattleSim(config, new Rng(1UL), Plan(new ObstacleSpawn(
                    ObstacleType.Breakable, 1024, 0, 10, null, false, 60)), Content(), PowerUpGauge.CreateDefault());
                Set(_director, "_sim", defaultSim);
                Get<Dictionary<int, float>>(_director, "_obstacleRegenAges")[id] = 0f;
                Invoke(_director, "SyncObstacles", 0.02f);
                Assert.That(largeScale / views[id].localScale.x,
                    Is.EqualTo(halfWidth > 0 ? halfWidth / 128f : 1f).Within(0.0001f));
            }
        }

        [Test]
        public void FinalResultWaitsForBossDeathThenBecomesVisible()
        {
            RunManager run = SetRun(RunState.RunCleared);
            StartBossDeath(run);
            var screen = ResultScreen(run);
            Invoke(screen, "Update");
            var panel = Get<GameObject>(screen, "_root");
            Assert.IsFalse(panel.activeSelf, "The result must not cover the boss death sequence.");
            SetProperty(_director, "BossDeathCinematicRemaining", 0f);
            Invoke(screen, "Update");
            Assert.IsTrue(panel.activeSelf);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayerDeathShowsResultImmediately(bool cinematicActive)
        {
            RunManager run = SetRun(RunState.RunOver);
            if (cinematicActive) StartBossDeath(run);
            var screen = ResultScreen(run);
            Invoke(screen, "Update");
            Assert.IsTrue(Get<GameObject>(screen, "_root").activeSelf);
        }

        [Test]
        public void PlayerDeathMusicIsNotDelayedByAnEarlierCinematic()
        {
            RunManager run = SetRun(RunState.RunOver);
            StartBossDeath(run);
            var music = _root.AddComponent<BgmPlayer>();
            Set(music, "_director", _director);
            Set(music, "_source", _root.AddComponent<AudioSource>());
            Invoke(music, "Update");
            Assert.AreEqual(run.RunNumber, Get<int>(music, "_jingledRunNumber"));
        }

        [TestCase(RunState.RunCleared)]
        [TestCase(RunState.AwaitingReward)]
        public void ClearMusicWaitsForBossDeath(RunState state)
        {
            RunManager run = SetRun(state);
            StartBossDeath(run);
            var music = _root.AddComponent<BgmPlayer>();
            Set(music, "_director", _director);
            Set(music, "_source", _root.AddComponent<AudioSource>());
            Set(music, "_bossTrackActive", true);
            Invoke(music, "Update");
            Assert.AreEqual(int.MinValue, Get<int>(music, "_jingledRunNumber"));
            Assert.IsFalse(Get<bool>(music, "_wasAwaitingReward"));
            Assert.IsTrue(Get<bool>(music, "_bossTrackActive"));

            SetProperty(_director, "BossDeathCinematicRemaining", 0f);
            Invoke(music, "Update");
            if (state == RunState.RunCleared)
                Assert.AreEqual(run.RunNumber, Get<int>(music, "_jingledRunNumber"));
            else
                Assert.IsTrue(Get<bool>(music, "_wasAwaitingReward"));
        }

        RunManager SetRun(RunState state)
        {
            var run = new RunManager(1UL, new TestGenerator(), BattleSimConfig.CreateDefault(),
                Content(), PowerUpGauge.CreateDefault());
            // Core completion is covered by Core tests; here seed the presentation boundary.
            SetProperty(run, "State", state);
            Set(_director, "_run", run);
            return run;
        }

        void StartBossDeath(RunManager run)
        {
            SetProperty(run, "IsHiddenBiome", true);
            SetProperty(run, "IsBiomeBoss", true);
            Set(_director, "_bossRenderer", _prefab.GetComponent<SpriteRenderer>());
            Set(_director, "_fxPool", new SpritePool(_prefab, _root.transform, 1));
            Invoke(_director, "TriggerBossDeathSequence");
            Assert.That(_director.BossDeathCinematicRemaining, Is.EqualTo(3.3f).Within(0.0001f));
        }

        GameOverScreen ResultScreen(RunManager run)
        {
            var screen = _root.AddComponent<GameOverScreen>();
            var panel = new GameObject("Result panel");
            panel.transform.SetParent(_root.transform);
            panel.SetActive(false);
            Set(screen, "_director", _director);
            Set(screen, "_root", panel);
            Set(screen, "_shownRun", run.RunNumber);
            Set(screen, "_shownCleared", run.State == RunState.RunCleared);
            return screen;
        }

        static void Set(object target, string field, object value)
            => target.GetType().GetField(field, Private).SetValue(target, value);
        static T Get<T>(object target, string field)
            => (T)target.GetType().GetField(field, Private).GetValue(target);
        static void SetProperty(object target, string name, object value)
            => target.GetType().GetProperty(name).SetValue(target, value);
        static void Invoke(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, Private).Invoke(target, args);

        static BattleContent Content()
        {
            var weapon = new WeaponDefinition("shot", 1, 1, 1, 1, 0, 0);
            return new BattleContent(Array.Empty<EnemyDefinition>(), new[] { weapon }, weapon.Id);
        }

        static StagePlan Plan(params ObstacleSpawn[] obstacles)
            => new StagePlan(new[] { new StageSegment("view_test", 1000, Array.Empty<SpawnEvent>(),
                1, 1, new[] { 1 }, obstacles) }, "none", 1, 1, 1);

        sealed class TestGenerator : IStageGenerator
        {
            public StagePlan Generate(ulong seed, int stageIndex, int difficulty) => Plan();
        }
    }
}

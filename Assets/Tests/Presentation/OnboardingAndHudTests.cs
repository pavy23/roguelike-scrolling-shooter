using System;
using System.Reflection;
using NUnit.Framework;
using Shmup.Core;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;
using Shmup.Presentation.Battle;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class OnboardingAndHudTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root;
        BattleDirector _director;
        OnboardingHints _guide;
        RunManager _run;
        bool _hadPreference, _touch, _autopilot;
        int _preference;
        float _timeScale;
        Font _font;

        [SetUp]
        public void SetUp()
        {
            _hadPreference = PlayerPrefs.HasKey("rss.onboarded");
            _preference = PlayerPrefs.GetInt("rss.onboarded");
            PlayerPrefs.DeleteKey("rss.onboarded");
            _touch = UiPlatform.ForceTouch;
            _autopilot = PlayerInputReader.AutopilotEnabled;
            _timeScale = Time.timeScale;
            UiPlatform.ForceTouch = false;
            PlayerInputReader.AutopilotEnabled = false;
            Time.timeScale = 1f;
            _root = new GameObject("Guide fixture");
            _root.SetActive(false);
            _director = _root.AddComponent<BattleDirector>();
            _guide = _root.AddComponent<OnboardingHints>();
            _font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11.ttf");
            Set(_guide, "_director", _director);
            Set(_guide, "_font", _font);
            var enemy = new EnemyDefinition("dropper", "Dropper", 10, 0, 25,
                EnemyMovePattern.Static, 0, 1, 0, 0, 0, 1, 0, 1, 64);
            var weapon = new WeaponDefinition("shot", 10, 1, 2, 1, 0, 0);
            var content = new BattleContent(new[] { enemy }, new[] { weapon }, weapon.Id);
            var config = new BattleSimConfig
            {
                PlayerSpeedPerTick = 2, PlayerBulletSpeedPerTick = 1, FireIntervalTicks = 1,
                MaxBullets = 64, PlayerMinX = -1000, PlayerMaxX = 1000,
                PlayerMinY = -1000, PlayerMaxY = 1000, BulletDespawnX = 1000, EnemyDespawnX = -1000,
                PlayerSpawnX = 0, PlayerSpawnY = 0, PlayerMaxHp = 5,
                PlayerHalfWidth = 0, PlayerHalfHeight = 0, CapsuleHalfWidth = 0, CapsuleHalfHeight = 0,
                CapsuleNoDropWeight = 0, ScrollSpeedNumerator = 0, ScrollSpeedDenominator = 1
            };
            _run = new RunManager(3UL, new GuideStage(), config, content, PowerUpGauge.CreateDefault());
            Set(_director, "_run", _run);
            Set(_director, "_sim", _run.Battle);
            _root.SetActive(true);
            Invoke(_guide, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            if (_hadPreference) PlayerPrefs.SetInt("rss.onboarded", _preference);
            else PlayerPrefs.DeleteKey("rss.onboarded");
            UiPlatform.ForceTouch = _touch;
            PlayerInputReader.AutopilotEnabled = _autopilot;
            Time.timeScale = _timeScale;
            typeof(OptionsScreen).GetField("_pauseInputConsumedFrame", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, -1);
        }

        [Test]
        public void IdleTimeDoesNotCompleteTheGuide()
        {
            for (int i = 0; i < 1200; i++) Step(InputCommand.None);
            Assert.AreEqual(0, _guide.CurrentStep);
            Assert.IsFalse(PlayerPrefs.HasKey("rss.onboarded"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MovementRequiresDisplacementAndSupportsAnalog(bool analog)
        {
            typeof(BattleSim).GetProperty("PlayerX").SetValue(_run.Battle, 1000);
            Step(analog ? InputCommand.Analog(2, 0, false, false, false) : new InputCommand(1, 0, false));
            Assert.AreEqual(0, _guide.CurrentStep, "Pushing against the edge did not move the ship.");
            Step(analog ? InputCommand.Analog(-2, 0, false, false, false) : new InputCommand(-1, 0, false));
            Assert.AreEqual(1, _guide.CurrentStep);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealPickupAndSuccessfulInvestmentCompleteGuide(bool partialInvestment)
        {
            CollectRealCapsule();
            Assert.AreEqual(2, _guide.CurrentStep);
            if (partialInvestment)
            {
                var levels = _run.PowerUpGauge.ExportLevels();
                levels[(int)_run.PowerUpGauge.SelectedSlot] = 1;
                _run.PowerUpGauge.ImportLevels(levels);
            }
            _run.PowerUpGauge.SetContractActivationBans(true, false, false);
            Step(new InputCommand(0, 0, false, true));
            Assert.IsFalse(_guide.Completed, "A rejected activation must not count.");
            _run.PowerUpGauge.SetContractActivationBans(false, false, false);
            Step(InputCommand.None);
            Step(new InputCommand(0, 0, false, true));
            Assert.IsTrue(_guide.Completed);
            Assert.AreEqual(1, PlayerPrefs.GetInt("rss.onboarded"));
            if (partialInvestment)
                Assert.AreEqual(PowerUpActivationResult.ProgressAdded, _run.PowerUpGauge.LastActivationResult);
        }

        [TestCase("replay")]
        [TestCase("autopilot")]
        [TestCase("pause")]
        [TestCase("disabled")]
        public void NonPlayerActivityCannotCompleteGuide(string context)
        {
            if (context == "replay") Set(_director, "_replayMode", true);
            if (context == "autopilot") PlayerInputReader.AutopilotEnabled = true;
            if (context == "pause") Time.timeScale = 0f;
            if (context == "disabled") _guide.enabled = false;
            CollectRealCapsule();
            Step(new InputCommand(0, 0, false, true));
            Assert.AreEqual(0, _guide.CurrentStep);
            Assert.IsFalse(PlayerPrefs.HasKey("rss.onboarded"));
        }

        [Test]
        public void DuplicateObservationAndEmptyActivationDoNotCompleteGuide()
        {
            Step(new InputCommand(-1, 0, false, true));
            Assert.AreEqual(1, _guide.CurrentStep);
            _guide.AfterStep(_run.Battle);
            Assert.IsFalse(_guide.Completed);
            Assert.IsFalse(PlayerPrefs.HasKey("rss.onboarded"));
        }

        [TestCase(RunState.AwaitingReward)]
        [TestCase(RunState.AwaitingContract)]
        [TestCase(RunState.RunOver)]
        public void GuideHidesOverOtherScreens(RunState state)
        {
            Invoke(_guide, "Update");
            Assert.IsTrue(Get<GameObject>(_guide, "_root").activeSelf);
            typeof(RunManager).GetProperty("State").SetValue(_run, state);
            Invoke(_guide, "Update");
            Assert.IsFalse(Get<GameObject>(_guide, "_root").activeSelf);
            Assert.IsFalse(Get<CanvasGroup>(_guide, "_group").blocksRaycasts);
        }

        [Test]
        public void ReplayFromOptionsKeepsRunAndPauseState()
        {
            CollectRealCapsule();
            Step(new InputCommand(0, 0, false, true));
            Assert.IsTrue(_guide.Completed);
            int tick = _run.Battle.Tick;
            var levels = _run.PowerUpGauge.ExportLevels();
            Time.timeScale = 0f;
            var options = _root.AddComponent<OptionsScreen>();
            Set(options, "_onboarding", _guide);
            Invoke(options, "SetOpen", true);
            var item = Enum.Parse(typeof(OptionsScreen).GetNestedType("Item", BindingFlags.NonPublic), "ReplayGuide");
            Invoke(options, "ActivateItem", item);
            Assert.AreEqual(0, _guide.CurrentStep);
            Assert.IsFalse(PlayerPrefs.HasKey("rss.onboarded"));
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsFalse(OptionsScreen.IsOpen);
            Assert.AreEqual(tick, _run.Battle.Tick);
            CollectionAssert.AreEqual(levels, _run.PowerUpGauge.ExportLevels());
        }

        [Test]
        public void ExistingCompletionRemainsRespectedUntilExplicitReplay()
        {
            PlayerPrefs.SetInt("rss.onboarded", 1);
            Set(_guide, "_loaded", false);
            Step(new InputCommand(-1, 0, false));
            Invoke(_guide, "Update");
            Assert.IsTrue(_guide.Completed);
            Assert.IsFalse(Get<GameObject>(_guide, "_root").activeSelf);
            _guide.RestartGuide();
            Invoke(_guide, "Update");
            Assert.IsTrue(Get<GameObject>(_guide, "_root").activeSelf);
        }

        [Test]
        public void GaugeDistinguishesCollectInvestUpgradeMaxAndContractLock()
        {
            var hud = CreateHud();
            var gauge = _run.PowerUpGauge;
            StringAssert.Contains("COLLECT", (string)Invoke(hud, "SelectionHint", gauge));
            gauge.Collect();
            StringAssert.Contains("UPGRADE", (string)Invoke(hud, "SelectionHint", gauge));
            var levels = gauge.ExportLevels();
            var slot = gauge.SelectedSlot;
            levels[(int)slot] = 1;
            gauge.ImportLevels(levels);
            StringAssert.Contains("INVEST", (string)Invoke(hud, "SelectionHint", gauge));
            gauge.SetContractActivationBans(true, false, false);
            StringAssert.Contains("CONTRACT LOCK", (string)Invoke(hud, "SelectionHint", gauge));
            gauge.SetContractActivationBans(false, false, false);
            levels[(int)slot] = gauge.GetMaxLevel(slot);
            gauge.ImportLevels(levels);
            StringAssert.Contains("MAX", (string)Invoke(hud, "SelectionHint", gauge));
        }

        [Test]
        public void GuideExplainsBlockedOrMissingSelectionWithinTheSameStep()
        {
            CollectRealCapsule();
            Invoke(_guide, "Update");
            StringAssert.Contains("Press", Get<Text>(_guide, "_text").text);
            _run.PowerUpGauge.SetContractActivationBans(true, false, false);
            Invoke(_guide, "Update");
            StringAssert.Contains("locked", Get<Text>(_guide, "_text").text);
            _run.PowerUpGauge.SetContractActivationBans(false, false, false);
            _run.PowerUpGauge.ActivateDetailed();
            Invoke(_guide, "Update");
            StringAssert.Contains("Collect a capsule to highlight", Get<Text>(_guide, "_text").text);
            Assert.IsFalse(_guide.Completed, "Changes outside a player step must not complete the guide.");
        }

        [Test]
        public void HudShowsEmptyShieldInWordsAndSelectionWithoutRelyingOnColor()
        {
            var hud = CreateHud();
            _run.PowerUpGauge.Collect();
            typeof(BattleSim).GetProperty("ShieldStock").SetValue(_run.Battle, 0);
            Invoke(hud, "Start");
            Invoke(hud, "LateUpdate");
            Text[] labels = Get<Text[]>(hud, "_labels");
            StringAssert.StartsWith("> ", labels[0].text);
            Assert.IsTrue(Array.Exists(labels, label => label.text.Contains("SHIELD\nEMPTY")));
        }

        void CollectRealCapsule()
        {
            Step(new InputCommand(0, 0, true));
            Step(InputCommand.None);
            Step(InputCommand.None);
            Step(new InputCommand(1, 0, false));
            Step(new InputCommand(1, 0, false));
            Assert.AreEqual(1, _run.Battle.Statistics.CapsulesCollected);
        }

        void Step(InputCommand command)
        {
            var battle = _run.Battle;
            _guide.BeforeStep(battle, _run.PowerUpGauge, in command);
            battle.Step(in command);
            _guide.AfterStep(battle);
        }

        PowerUpHudView CreateHud()
        {
            var hud = _root.AddComponent<PowerUpHudView>();
            Set(hud, "_director", _director);
            Set(hud, "_font", _font);
            return hud;
        }

        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);

        sealed class GuideStage : IStageGenerator
        {
            public StagePlan Generate(ulong seed, int stageIndex, int difficulty) =>
                new StagePlan(new[] { new StageSegment("guide", 10000,
                    new[] { new SpawnEvent(0, "dropper", 4, 0) }, 1, 1, new[] { 1 }) }, "boss", 1, 1, 1);
        }
    }
}

using System.IO;
using System.Reflection;
using NUnit.Framework;
using Shmup.Core.Content;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;
using Shmup.Presentation.Battle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class BattleUiLayerTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root, _events;
        BattleDirector _director;
        RunManager _run;
        PauseScreen _pause;
        OptionsScreen _options;
        float _timeScale;
        bool _touch, _audioPaused;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            _touch = UiPlatform.ForceTouch;
            _audioPaused = AudioListener.pause;
            Time.timeScale = 1f;
            ResetModalState();
            var data = GameDataParser.Parse(Read("enemies"), Read("weapons"), Read("waves"),
                Read("rewards"), Read("ships"), Read("scoring"));
            _run = new RunManager(12345UL, new SegmentStageGenerator(data.StageGeneration),
                data.CreateBattleSimConfig(), data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip),
                data.Rewards, data.DefaultShip);
            _run.PowerUpGauge.Collect();
            _root = new GameObject("Battle UI layer fixture");
            _root.SetActive(false);
            _director = _root.AddComponent<BattleDirector>();
            Set(_director, "_run", _run);
            Set(_director, "_sim", _run.Battle);
            if (EventSystem.current == null)
            {
                _events = new GameObject("UI layer events", typeof(EventSystem));
                // EditMode does not run EventSystem.OnEnable automatically.
                Invoke(_events.GetComponent<EventSystem>(), "OnEnable");
            }
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            if (_events != null)
            {
                Invoke(_events.GetComponent<EventSystem>(), "OnDisable");
                Object.DestroyImmediate(_events);
            }
            Time.timeScale = _timeScale;
            UiPlatform.ForceTouch = _touch;
            AudioListener.pause = _audioPaused;
            ResetModalState();
        }

        [TestCase(RunState.AwaitingReward)]
        [TestCase(RunState.AwaitingContract)]
        [TestCase(RunState.RunOver)]
        [TestCase(RunState.RunCleared)]
        public void ModalStatesHideAllCombatReadoutsAndRestoreExistingGauge(RunState state)
        {
            Build(true);
            var gauge = _root.GetComponent<PowerUpHudView>();
            Invoke(gauge, "LateUpdate");
            var frames = Get<Image[]>(gauge, "_frames");
            int cursor = _run.PowerUpGauge.Cursor;
            AssertHud(true);

            typeof(RunManager).GetProperty("State").SetValue(_run, state);
            // Transition happens after HUD Update, as it can when a choice is made.
            RefreshLayers();
            AssertHud(false);
            Assert.AreEqual(cursor, _run.PowerUpGauge.Cursor);

            typeof(RunManager).GetProperty("State").SetValue(_run, RunState.Playing);
            RefreshLayers();
            AssertHud(true);
            Assert.AreSame(frames, Get<Image[]>(gauge, "_frames"));
            Assert.AreEqual(cursor, _run.PowerUpGauge.Cursor);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PauseAndResumeRestoreHudWithoutLeavingDuplicatePauseButton(bool touch)
        {
            Build(touch);
            Invoke(_pause, "SetPaused", true);
            RefreshLayers();
            AssertHud(false);
            Assert.IsTrue(Get<GameObject>(_pause, "_root").activeSelf);
            if (touch) Assert.IsFalse(Get<GameObject>(_pause, "_toggleRoot").activeSelf);
            Invoke(_pause, "SetPaused", false);
            RefreshLayers();
            AssertHud(true);
            Assert.IsFalse(Get<GameObject>(_pause, "_root").activeSelf);
            if (touch) Assert.IsTrue(Get<GameObject>(_pause, "_toggleRoot").activeSelf);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OptionsHidePauseAndRestoreResumeFocusWhenClosed(bool touch)
        {
            Build(touch);
            Invoke(_pause, "SetPaused", true);
            Invoke(_options, "SetOpen", true);
            RefreshLayers();
            Assert.IsFalse(Get<GameObject>(_pause, "_root").activeSelf);
            AssertHud(false);
            if (touch) Assert.IsFalse(Get<GameObject>(_options, "_openButtonRoot").activeSelf);
            Invoke(_options, "SetOpen", false);
            RefreshLayers();
            AssertPauseRestored(touch);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AudioHidesPauseAndOptionsEntryEvenWhenOptionsUpdateIsBlocked(bool touch)
        {
            Build(touch);
            Invoke(_pause, "SetPaused", true);
            RefreshLayers();
            if (touch) Assert.IsTrue(Get<GameObject>(_options, "_openButtonRoot").activeSelf);
            var audio = Get<AudioSettingsPanel>(_pause, "_audioSettings");
            audio.Open();
            Invoke(_options, "Update");
            RefreshLayers();
            Assert.IsFalse(Get<GameObject>(_pause, "_root").activeSelf);
            AssertHud(false);
            if (touch) Assert.IsFalse(Get<GameObject>(_options, "_openButtonRoot").activeSelf);

            // Model the closed dialog without invoking its unrelated preference-save path.
            Get<GameObject>(audio, "_root").SetActive(false);
            ResetModalState();
            EventSystem.current.SetSelectedGameObject(null);
            RefreshLayers();
            AssertPauseRestored(touch);
        }

        void AssertPauseRestored(bool touch)
        {
            Assert.IsTrue(Get<GameObject>(_pause, "_root").activeSelf);
            Assert.AreSame(Get<Button>(_pause, "_resumeButton").gameObject, EventSystem.current.currentSelectedGameObject);
            if (touch) Assert.IsTrue(Get<GameObject>(_options, "_openButtonRoot").activeSelf);
            Assert.AreEqual(0f, Time.timeScale, "Closing a child dialog must not resume combat.");
            AssertHud(false);
        }

        void Build(bool touch)
        {
            UiPlatform.ForceTouch = touch;
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11.ttf");
            var bold = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11-Bold.ttf");
            var views = new MonoBehaviour[] { _root.AddComponent<ScoreHud>(), _root.AddComponent<ProgressHud>(),
                _root.AddComponent<PowerUpHudView>(), _pause = _root.AddComponent<PauseScreen>(),
                _options = _root.AddComponent<OptionsScreen>() };
            foreach (var view in views)
            {
                view.GetType().GetField("_director", Private)?.SetValue(view, _director);
                view.GetType().GetField("_font", Private)?.SetValue(view, font);
                view.GetType().GetField("_fontBold", Private)?.SetValue(view, bold);
                Invoke(view, "Start");
            }
            _root.SetActive(true);
            RefreshLayers();
        }

        void RefreshLayers()
        {
            Invoke(_pause, "LateUpdate");
            Invoke(_options, "LateUpdate");
            foreach (var layer in _root.GetComponentsInChildren<BattleHudVisibility>(true)) Invoke(layer, "LateUpdate");
        }

        void AssertHud(bool visible)
        {
            var layers = _root.GetComponentsInChildren<BattleHudVisibility>(true);
            Assert.AreEqual(3, layers.Length, "Score, stage and gauge must all yield together.");
            foreach (var layer in layers)
            {
                Assert.AreEqual(visible, layer.GetComponent<Canvas>().enabled, layer.name);
                Assert.AreEqual(visible, layer.GetComponent<GraphicRaycaster>().enabled, layer.name + " raycasts");
                Assert.IsTrue(layer.gameObject.activeInHierarchy, "Hidden HUDs must keep receiving state updates.");
            }
        }

        static void ResetModalState()
        {
            typeof(AudioSettingsPanel).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            typeof(OptionsScreen).GetProperty("IsOpen").SetValue(null, false);
            typeof(OptionsScreen).GetField("_pauseInputConsumedFrame", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, -1);
        }
        static string Read(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "../GameData", name + ".json"));
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    }
}

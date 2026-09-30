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
    public sealed class BossIntroTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root, _events;
        BossIntro _intro;
        JuiceDirector _juice;
        RunManager _run;
        float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
            ResetModals();
            if (EventSystem.current == null)
            {
                _events = new GameObject("Boss intro events", typeof(EventSystem));
                Invoke(_events.GetComponent<EventSystem>(), "OnEnable");
            }
            _root = new GameObject("Boss intro fixture");
            _root.SetActive(false);
            var data = GameDataParser.Parse(Read("enemies"), Read("weapons"), Read("waves"),
                Read("rewards"), Read("ships"), Read("scoring"));
            _run = new RunManager(12345UL, new SegmentStageGenerator(data.StageGeneration),
                data.CreateBattleSimConfig(), data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip),
                data.Rewards, data.DefaultShip);
            var director = _root.AddComponent<BattleDirector>();
            Set(director, "_run", _run);
            Set(director, "_sim", _run.Battle);
            _juice = _root.AddComponent<JuiceDirector>();
            _intro = _root.AddComponent<BossIntro>();
            Set(_intro, "_fontBold", UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11-Bold.ttf"));
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
            ResetModals();
        }

        [TestCase(BossIntroKind.StageBoss, "!! WARNING !!", "STAGE BOSS DETECTED")]
        [TestCase(BossIntroKind.HiddenBoss, "!! WARNING !!", "COLOSSUS DETECTED")]
        [TestCase(BossIntroKind.FormTransition, "SECOND FORM", "TRANSFORMATION IN PROGRESS")]
        [TestCase(BossIntroKind.SecondForm, "SECOND FORM", "SECOND FORM ENGAGED")]
        public void EncounterKindsUseDistinctInformation(BossIntroKind kind, string heading, string detail)
        {
            Start();
            _intro.Trigger(kind);
            Render(.1f);
            Assert.IsTrue(Get<GameObject>(_intro, "_banner").activeSelf);
            Assert.AreEqual(heading, Get<Text>(_intro, "_heading").text);
            Assert.AreEqual(detail, Get<Text>(_intro, "_detail").text);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WarningTextStaysVisibleAcrossTheEntireFormerBlinkCycle(bool reduced)
        {
            Start();
            Set(_juice, "_flashReduced", reduced);
            _intro.Trigger();
            for (int i = 0; i <= 20; i++)
            {
                Render(i * .1f);
                Assert.IsTrue(Get<GameObject>(_intro, "_banner").activeSelf);
                Assert.AreEqual(1f, Get<CanvasGroup>(_intro, "_group").alpha);
                Assert.AreEqual(1f, Get<Text>(_intro, "_heading").color.a);
            }
        }

        [Test]
        public void ReducedFlashCanBeEnabledDuringAnAlertAndHoldsFrameColorSteady()
        {
            Start();
            _intro.Trigger();
            Render(.1f);
            Set(_juice, "_flashReduced", true);
            Render(.2f);
            var color = Get<Image>(_intro, "_border").color;
            Render(.7f);
            Assert.AreEqual(color, Get<Image>(_intro, "_border").color);
            Set(_juice, "_flashReduced", false);
            Render(.1f);
            Assert.AreNotEqual(color, Get<Image>(_intro, "_border").color);
        }

        [Test]
        public void TriggerBeforeStartRetainsTheSecondFormMessage()
        {
            _intro.Trigger(BossIntroKind.SecondForm);
            Start();
            Invoke(_intro, "LateUpdate");
            Assert.IsTrue(Get<GameObject>(_intro, "_banner").activeSelf);
            Assert.AreEqual("SECOND FORM", Get<Text>(_intro, "_heading").text);
        }

        [TestCase(false, false, "STAGE BOSS DETECTED")]
        [TestCase(true, false, "COLOSSUS DETECTED")]
        [TestCase(false, true, "SECOND FORM ENGAGED")]
        public void DirectorArrivalUsesHiddenAndSecondFormContext(bool hidden, bool secondForm, string detail)
        {
            Start();
            var director = _root.GetComponent<BattleDirector>();
            Set(director, "_bossIntro", _intro);
            Set(director, "_bossFormId", secondForm ? "boss_warship_form2" : null);
            typeof(RunManager).GetProperty("IsBiomeBoss").SetValue(_run, true);
            typeof(RunManager).GetProperty("IsHiddenBiome").SetValue(_run, hidden);
            Invoke(director, "TriggerBossArrival");
            Invoke(_intro, "LateUpdate");
            Assert.AreEqual(detail, Get<Text>(_intro, "_detail").text);
        }

        [Test]
        public void MidbossArrivalKeepsTheExistingNoBannerPolicy()
        {
            Start();
            var director = _root.GetComponent<BattleDirector>();
            Set(director, "_bossIntro", _intro);
            typeof(RunManager).GetProperty("RoomIndex").SetValue(_run, 2);
            Assert.AreEqual(RunStageSection.MidBoss, _run.StageSection);
            Invoke(director, "TriggerBossArrival");
            Invoke(_intro, "LateUpdate");
            Assert.IsFalse(Get<GameObject>(_intro, "_banner").activeSelf);
        }

        [Test]
        public void PausingHidesAndFreezesAlertThenRestoresRemainingTime()
        {
            Start();
            _intro.Trigger();
            Render(.4f);
            Time.timeScale = 0f;
            Invoke(_intro, "Update");
            Invoke(_intro, "LateUpdate");
            Assert.IsFalse(Get<GameObject>(_intro, "_banner").activeSelf);
            Assert.AreEqual(.4f, Get<float>(_intro, "_age"));
            Time.timeScale = 1f;
            Invoke(_intro, "LateUpdate");
            Assert.IsTrue(Get<GameObject>(_intro, "_banner").activeSelf);
        }

        [TestCase(RunState.AwaitingReward)]
        [TestCase(RunState.RunOver)]
        public void LeavingCombatCancelsWarningInsteadOfLeakingIntoTheNextRoom(RunState state)
        {
            Start();
            _intro.Trigger();
            typeof(RunManager).GetProperty("State").SetValue(_run, state);
            Invoke(_intro, "Update");
            Invoke(_intro, "LateUpdate");
            Assert.IsFalse(Get<GameObject>(_intro, "_banner").activeSelf);
            typeof(RunManager).GetProperty("State").SetValue(_run, RunState.Playing);
            Invoke(_intro, "LateUpdate");
            Assert.IsFalse(Get<GameObject>(_intro, "_banner").activeSelf);
        }

        [Test]
        public void ExpiredAndRetriggeredAlertResetsFadeAndMessage()
        {
            Start();
            _intro.Trigger(BossIntroKind.SecondForm);
            Render(2.3f);
            Assert.Less(Get<CanvasGroup>(_intro, "_group").alpha, 1f);
            Render(2.4f);
            Assert.IsFalse(Get<GameObject>(_intro, "_banner").activeSelf);
            _intro.Trigger();
            Invoke(_intro, "LateUpdate");
            Assert.AreEqual(1f, Get<CanvasGroup>(_intro, "_group").alpha);
            Assert.AreEqual("STAGE BOSS DETECTED", Get<Text>(_intro, "_detail").text);
        }

        [TestCase(640f, 360f)]
        [TestCase(650f, 380f)]
        public void AlertFitsAboveFlightCenterBelowHudAndBetweenTouchControls(float width, float height)
        {
            Start();
            var canvas = _root.GetComponentInChildren<Canvas>(true);
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(width, height);
            foreach (BossIntroKind kind in System.Enum.GetValues(typeof(BossIntroKind)))
            {
                _intro.Trigger(kind);
                Invoke(_intro, "LateUpdate");
                Canvas.ForceUpdateCanvases();
                var band = Get<GameObject>(_intro, "_banner").GetComponent<RectTransform>();
                var corners = new Vector3[4];
                band.GetWorldCorners(corners);
                Vector3 min = canvasRect.InverseTransformPoint(corners[0]);
                Vector3 max = canvasRect.InverseTransformPoint(corners[2]);
                Assert.Greater(min.y, 0f, "Central flight lane must stay open.");
                Assert.Less(max.y, height / 2f - 58f, "Stage text ends 58px below the top.");
                Assert.Greater(min.x, -width / 2f + 76f, "Pause button ends at x=76.");
                Assert.Less(max.x, width / 2f - 92f, "Select button starts 92px from the right.");
                Assert.IsFalse(Get<CanvasGroup>(_intro, "_group").blocksRaycasts);
                foreach (var text in new[] { Get<Text>(_intro, "_heading"), Get<Text>(_intro, "_detail") })
                    Assert.LessOrEqual(text.preferredWidth, text.rectTransform.rect.width, text.text);
            }
        }

        void Start() { Invoke(_intro, "Start"); _root.SetActive(true); }
        void Render(float age) { Set(_intro, "_age", age); Invoke(_intro, "LateUpdate"); }
        static void ResetModals()
        {
            typeof(AudioSettingsPanel).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            typeof(OptionsScreen).GetProperty("IsOpen").SetValue(null, false);
        }
        static string Read(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "../GameData", name + ".json"));
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    }
}

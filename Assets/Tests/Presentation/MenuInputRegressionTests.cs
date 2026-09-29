using System.Reflection;
using NUnit.Framework;
using Shmup.Presentation.Battle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class MenuInputRegressionTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root, _events;
        InputActionAsset _actions;
        bool _touch, _autopilot;
        bool _hadAutoFirePref;
        int _autoFirePref;

        [SetUp]
        public void SetUp()
        {
            _touch = UiPlatform.ForceTouch;
            _autopilot = PlayerInputReader.AutopilotEnabled;
            PlayerInputReader.AutopilotEnabled = false;
            _hadAutoFirePref = PlayerPrefs.HasKey("rss.autofire");
            _autoFirePref = PlayerPrefs.GetInt("rss.autofire");
            _root = new GameObject("Menu regression fixture");
            _root.SetActive(false);
            // Supply our own EventSystem so UiKit does not leave a global object behind.
            if (EventSystem.current == null)
            {
                _events = new GameObject("Menu test events", typeof(EventSystem));
                // EditMode does not invoke EventSystem's runtime OnEnable automatically.
                if (EventSystem.current == null) Invoke(_events.GetComponent<EventSystem>(), "OnEnable");
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
            if (_actions != null) Object.DestroyImmediate(_actions);
            UiPlatform.ForceTouch = _touch;
            PlayerInputReader.AutopilotEnabled = _autopilot;
            if (_hadAutoFirePref) PlayerPrefs.SetInt("rss.autofire", _autoFirePref);
            else PlayerPrefs.DeleteKey("rss.autofire");
        }

        [TestCase(0)]
        [TestCase(1)]
        public void AlwaysFireDoesNotRequireLegacyAttackActionOrPreference(int savedValue)
        {
            PlayerPrefs.SetInt("rss.autofire", savedValue);
            _actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = _actions.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value).expectedControlType = "Vector2";
            map.AddAction("Activate", InputActionType.Button);
            var reader = _root.AddComponent<PlayerInputReader>();
            Set(reader, "_actions", _actions);
            Invoke(reader, "Awake");
            Assert.IsTrue(reader.enabled, "An obsolete Attack action must not disable movement or firing.");
            Assert.IsTrue(reader.ConsumeCommand().Fire);
            reader.enabled = false;
            Assert.IsFalse(reader.ConsumeCommand().Fire, "Disabled input must still produce no command.");
        }

        [Test]
        public void OptionsExposeAutomaticFireAsInformationAndKeepCloseReachable()
        {
            var options = _root.AddComponent<OptionsScreen>();
            var item = typeof(OptionsScreen).GetNestedType("Item", BindingFlags.NonPublic);
            string[] names = System.Enum.GetNames(item);
            CollectionAssert.DoesNotContain(names, "AutoFire");
            CollectionAssert.DoesNotContain(names, "RebindFire");
            CollectionAssert.Contains(names, "RebindActivate");
            Set(options, "_cursor", (int)System.Enum.Parse(item, "Close"));
            Invoke(options, "RefreshPanelText");
            string text = Get<string>(options, "_panelText");
            StringAssert.Contains("WEAPONS FIRE AUTOMATICALLY", text);
            StringAssert.Contains("▶ CLOSE", text);
            StringAssert.DoesNotContain("REBIND FIRE", text);
            Set(options, "_open", true);
            Invoke(options, "ActivateItem", System.Enum.Parse(item, "Close"));
            Assert.IsFalse(Get<bool>(options, "_open"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BaseMenuActionsAreBlockedWhileDevModalIsOpen(bool touch)
        {
            UiPlatform.ForceTouch = touch;
            var title = Title();
            Invoke(title, "BuildMenuButtons", _root.transform);
            Invoke(title, "BuildDevPanel", _root.transform);
            Assert.IsFalse(title.ModalOpen, "Developer tools must start collapsed.");
            var mode = FindButton("ModeButton");
            mode.onClick.Invoke();
            Assert.IsTrue(Get<bool>(title, "_dailyMode"));
            Invoke(title, "ToggleDevPanel");
            Assert.IsTrue(title.ModalOpen);
            Assert.AreEqual(FindButton("DevStage").gameObject, EventSystem.current.currentSelectedGameObject,
                "Opening the tools must provide a starting point for controller navigation.");
            mode.onClick.Invoke();
            Assert.IsTrue(Get<bool>(title, "_dailyMode"), "A modal must block background actions.");
            var dim = Get<GameObject>(title, "_devRoot").transform.Find("Dim").GetComponent<Image>();
            Assert.IsTrue(dim.raycastTarget, "Pointer events must not reach the hangar behind the modal.");
            Invoke(title, "ToggleDevPanel");
            mode.onClick.Invoke();
            Assert.IsFalse(Get<bool>(title, "_dailyMode"));
            foreach (string name in new[] { "ModeButton", "DifficultyButton", "LaunchButton" })
                Assert.AreEqual(Navigation.Mode.None, FindButton(name).navigation.mode,
                    "Shortcut confirmation must not also submit a previously clicked button.");
        }

        [Test]
        public void RankingModalBlocksBackgroundButtonsWithoutRequestingTheServer()
        {
            var title = Title();
            Invoke(title, "BuildMenuButtons", _root.transform);
            Invoke(title, "BuildRankingPanel");
            var ranking = Get<GameObject>(title, "_rankingRoot");
            ranking.SetActive(true);
            FindButton("ModeButton").onClick.Invoke();
            Assert.IsFalse(Get<bool>(title, "_dailyMode"));
            Assert.IsTrue(ranking.transform.Find("Dim").GetComponent<Image>().raycastTarget);
            ranking.SetActive(false);
            FindButton("ModeButton").onClick.Invoke();
            Assert.IsTrue(Get<bool>(title, "_dailyMode"));
        }

        [Test]
        public void ManualSeedWarningStaysVisibleAfterClosingToolsAndClearsForDaily()
        {
            var title = Title();
            var prompt = new GameObject("Prompt", typeof(Text));
            prompt.transform.SetParent(_root.transform);
            Set(title, "_promptText", prompt.GetComponent<Text>());
            Set(title, "_seedManual", true);
            Invoke(title, "RefreshLaunchHint");
            StringAssert.Contains("NO SCORE SUBMIT", prompt.GetComponent<Text>().text);
            Invoke(title, "ToggleMode");
            StringAssert.DoesNotContain("NO SCORE SUBMIT", prompt.GetComponent<Text>().text);
        }

        TitleScreen Title()
        {
            var title = _root.AddComponent<TitleScreen>();
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11.ttf");
            Set(title, "_font", font);
            Set(title, "_fontBold", font);
            Set(title, "_seedText", "12345");
            _root.SetActive(true);
            return title;
        }

        Button FindButton(string name)
        {
            foreach (var button in _root.GetComponentsInChildren<Button>(true))
                if (button.name == name) return button;
            Assert.Fail("Missing button: " + name);
            return null;
        }

        static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, Private).SetValue(target, value);
        static T Get<T>(object target, string name) =>
            (T)target.GetType().GetField(name, Private).GetValue(target);
        static void Invoke(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Private).Invoke(target, args);
    }
}

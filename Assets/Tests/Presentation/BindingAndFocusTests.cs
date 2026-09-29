using System.Reflection;
using NUnit.Framework;
using Shmup.Presentation.Battle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class BindingAndFocusTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root;
        InputActionAsset _actions, _reloaded;
        InputSettings _originalSettings, _testSettings;
        PlayerInputReader _input;
        OptionsScreen _options;
        Keyboard _keyboard;
        Gamepad _pad;
        bool _hadBindings, _touch, _audioPaused;
        string _savedBindings;
        float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _hadBindings = PlayerPrefs.HasKey(PlayerBindings.PrefKey);
            _savedBindings = PlayerPrefs.GetString(PlayerBindings.PrefKey);
            PlayerPrefs.DeleteKey(PlayerBindings.PrefKey);
            // Input System 1.19 otherwise updates editor device state without running player actions.
            // Enable its explicit EditMode test path on a disposable settings clone.
            _originalSettings = InputSystem.settings;
            _testSettings = Object.Instantiate(_originalSettings);
            InputSystem.settings = _testSettings;
            _testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            _timeScale = Time.timeScale;
            _audioPaused = AudioListener.pause;
            _touch = UiPlatform.ForceTouch;
            UiPlatform.ForceTouch = false;
            _root = new GameObject("Binding test fixture");
            _root.SetActive(false);
            _actions = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/Settings/InputSystem_Actions.inputactions"));
            _input = _root.AddComponent<PlayerInputReader>();
            Set(_input, "_actions", _actions);
            Invoke(_input, "Awake");
            Invoke(_input, "OnEnable");
            _options = _root.AddComponent<OptionsScreen>();
            Set(_options, "_input", _input);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
            Time.timeScale = 0f;
            Invoke(_options, "SetOpen", true);
        }

        [TearDown]
        public void TearDown()
        {
            Invoke(_options, "CancelRebind");
            Invoke(_options, "SetOpen", false);
            Object.DestroyImmediate(_root);
            _actions.Disable();
            Object.DestroyImmediate(_actions);
            if (_reloaded != null) Object.DestroyImmediate(_reloaded);
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.RemoveDevice(_pad);
            InputSystem.settings = _originalSettings;
            Object.DestroyImmediate(_testSettings);
            if (_hadBindings) PlayerPrefs.SetString(PlayerBindings.PrefKey, _savedBindings);
            else PlayerPrefs.DeleteKey(PlayerBindings.PrefKey);
            Time.timeScale = _timeScale;
            AudioListener.pause = _audioPaused;
            UiPlatform.ForceTouch = _touch;
            typeof(OptionsScreen).GetField("_pauseInputConsumedFrame", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, -1);
        }

        [Test]
        public void KeyboardRebindPreservesPadAndUpdatesSavedEffectiveLabel()
        {
            string padPath = _input.ActivateAction.bindings[1].effectivePath;
            Invoke(_options, "StartRebindActivate");
            Press(Key.J);
            Assert.IsNull(Get<object>(_options, "_rebind"));
            Assert.IsTrue(_input.ActivateAction.enabled);
            Assert.AreEqual("<Keyboard>/j", _input.ActivateAction.bindings[0].effectivePath);
            Assert.AreEqual(padPath, _input.ActivateAction.bindings[1].effectivePath);
            StringAssert.Contains("J", PlayerBindings.KeyboardLabel(_input.ActivateAction).ToUpperInvariant());
            _reloaded = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/Settings/InputSystem_Actions.inputactions"));
            PlayerBindings.Load(_reloaded);
            Assert.AreEqual("<Keyboard>/j", _reloaded.FindAction("Player/Activate").bindings[0].effectivePath);
            Invoke(_options, "ResetBindings");
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerBindings.PrefKey));
            Assert.AreEqual("<Keyboard>/x", _input.ActivateAction.bindings[0].effectivePath);
        }

        [Test]
        public void RebindIgnoresPadAndRejectsBombAndMovementKeys()
        {
            Invoke(_options, "StartRebindActivate");
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.North));
            InputSystem.Update();
            Assert.IsNotNull(Get<object>(_options, "_rebind"));
            Press(Key.B);
            Assert.IsNotNull(Get<object>(_options, "_rebind"));
            StringAssert.Contains("RESERVED", Get<string>(_options, "_rebindPrompt"));
            Press(Key.W);
            Assert.IsNotNull(Get<object>(_options, "_rebind"));
            StringAssert.Contains("IN USE", Get<string>(_options, "_rebindPrompt"));
            Press(Key.J);
            Assert.IsNull(Get<object>(_options, "_rebind"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EscapeCancelKeepsOriginalBindingAndActionEnableState(bool enabled)
        {
            if (!enabled) _input.ActivateAction.Disable();
            Invoke(_options, "StartRebindActivate");
            Press(Key.Escape);
            Assert.IsNull(Get<object>(_options, "_rebind"));
            Assert.AreEqual(enabled, _input.ActivateAction.enabled);
            Assert.AreEqual("<Keyboard>/x", _input.ActivateAction.bindings[0].effectivePath);
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerBindings.PrefKey), "Cancellation must not persist a new override.");
        }

        [Test]
        public void EscapeCancellationAndClosingOptionsCannotResumeTheSameFrame()
        {
            var pause = _root.AddComponent<PauseScreen>();
            Set(pause, "_paused", true);
            Invoke(_options, "StartRebindActivate");
            Press(Key.Escape);
            Invoke(pause, "Update");
            Assert.AreEqual(0f, Time.timeScale);
            Invoke(_options, "SetOpen", false);
            Assert.IsFalse(OptionsScreen.IsOpen);
            Invoke(pause, "Update");
            Assert.AreEqual(0f, Time.timeScale, "Back must not also consume the pause shortcut.");
        }

        [Test]
        public void MovementRebindPreservesArrowAlternativeAndOnboardingUsesNewKeys()
        {
            var move = _input.MoveAction;
            int index = PlayerBindings.KeyboardIndex(move, "up");
            string alternate = move.bindings[index + 1].effectivePath;
            Invoke(_options, "StartRebindMovePart", "up");
            Press(Key.I);
            Assert.AreEqual("<Keyboard>/i", move.bindings[index].effectivePath);
            Assert.AreEqual(alternate, move.bindings[index + 1].effectivePath);
            var director = _root.AddComponent<BattleDirector>();
            Set(director, "_input", _input);
            var hints = _root.AddComponent<OnboardingHints>();
            Set(hints, "_director", director);
            string movement = (string)Invoke(hints, "HintAt", 0);
            StringAssert.Contains("I/", movement.ToUpperInvariant());
            _input.ActivateAction.ApplyBindingOverride(0, "<Keyboard>/j");
            PlayerBindings.Save(_actions);
            StringAssert.Contains("J /", ((string)Invoke(hints, "HintAt", 2)).ToUpperInvariant());
            Invoke(_options, "RefreshPanelText");
            StringAssert.Contains("MOVE UP KEY    I", Get<string>(_options, "_panelText").ToUpperInvariant());
        }

        [Test]
        public void ReboundKeyDrivesGameplayButPausedInputDoesNotQueueActivation()
        {
            Invoke(_options, "StartRebindActivate");
            Press(Key.J);
            Invoke(_input, "Update");
            Assert.IsFalse(_input.ConsumeCommand().Activate);
            Time.timeScale = 1f;
            Press(Key.J);
            Invoke(_input, "Update");
            Assert.IsTrue(_input.ConsumeCommand().Activate);
            Invoke(_input, "OnDisable");
            Assert.IsFalse(_input.ConsumeCommand().Activate);
        }

        [Test]
        public void ClosingOptionsDisposesPendingCaptureWithoutSaving()
        {
            Invoke(_options, "StartRebindActivate");
            Invoke(_options, "SetOpen", false);
            Press(Key.J);
            Assert.IsNull(Get<object>(_options, "_rebind"));
            Assert.AreEqual("<Keyboard>/x", _input.ActivateAction.bindings[0].effectivePath);
            Assert.IsTrue(_input.ActivateAction.enabled);
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerBindings.PrefKey));
        }

        [Test]
        public void FocusOutlineFollowsSelectionAndDoesNotBlockClicks()
        {
            _root.SetActive(true);
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11.ttf");
            var button = UiKit.CreateTouchButton(_root.transform, font, "FOCUS", 11,
                Vector2.zero, Vector2.zero, new Vector2(120f, 40f), null);
            var outline = button.transform.Find("FocusOutline").GetComponent<Image>();
            Assert.IsFalse(outline.gameObject.activeSelf);
            var data = new BaseEventData(null);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.selectHandler);
            Assert.IsTrue(outline.gameObject.activeSelf);
            Assert.IsFalse(outline.raycastTarget);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.deselectHandler);
            Assert.IsFalse(outline.gameObject.activeSelf);
        }

        void Press(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            InputSystem.Update();
        }

        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    }
}

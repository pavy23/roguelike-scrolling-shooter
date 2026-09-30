using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Shmup.Presentation.Battle;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class AudioSettingsTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Keys = { "rss.volume", "rss.audio.music", "rss.audio.effects", "rss.audio.ui" };
        readonly bool[] _had = new bool[4];
        readonly float[] _saved = new float[4];
        readonly List<AudioChannelSource> _channels = new List<AudioChannelSource>();
        GameObject _root, _events;
        AudioClip _clip;
        Keyboard _keyboard;
        Gamepad _pad;
        InputSettings _originalInput, _testInput;
        float _volume, _timeScale;
        bool _paused, _touch;

        [SetUp]
        public void SetUp()
        {
            _volume = AudioListener.volume;
            _paused = AudioListener.pause;
            _timeScale = Time.timeScale;
            _touch = UiPlatform.ForceTouch;
            for (int i = 0; i < Keys.Length; i++)
            {
                _had[i] = PlayerPrefs.HasKey(Keys[i]);
                _saved[i] = PlayerPrefs.GetFloat(Keys[i]);
                PlayerPrefs.DeleteKey(Keys[i]);
            }
            AudioPreferences.Reload();
            UiPlatform.ForceTouch = false;
            Time.timeScale = 1f;
            ResetModalState();
            _root = new GameObject("Audio test fixture");
            _root.SetActive(false);
            if (EventSystem.current == null) _events = new GameObject("Audio test events", typeof(EventSystem));
            _originalInput = InputSystem.settings;
            _testInput = Object.Instantiate(_originalInput);
            InputSystem.settings = _testInput;
            _testInput.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var channel in _channels) if (channel != null) Invoke(channel, "OnDisable");
            _channels.Clear();
            foreach (var panel in _root.GetComponentsInChildren<AudioSettingsPanel>(true)) Invoke(panel, "OnDisable");
            Object.DestroyImmediate(_root);
            if (_events != null) Object.DestroyImmediate(_events);
            if (_clip != null) Object.DestroyImmediate(_clip);
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.RemoveDevice(_pad);
            InputSystem.settings = _originalInput;
            Object.DestroyImmediate(_testInput);
            for (int i = 0; i < Keys.Length; i++)
                if (_had[i]) PlayerPrefs.SetFloat(Keys[i], _saved[i]); else PlayerPrefs.DeleteKey(Keys[i]);
            AudioPreferences.Reload();
            PlayerPrefs.Save();
            AudioListener.volume = _volume;
            AudioListener.pause = _paused;
            Time.timeScale = _timeScale;
            UiPlatform.ForceTouch = _touch;
            ResetModalState();
        }

        [TestCase(0f)]
        [TestCase(0.3f)]
        public void ExistingMasterPreferenceIsPreservedIncludingMute(float volume)
        {
            PlayerPrefs.SetFloat("rss.volume", volume);
            AudioPreferences.Reload();
            Assert.AreEqual(volume, AudioListener.volume);
            Assert.AreEqual(1f, AudioPreferences.Get(AudioChannel.Music));
            Assert.AreEqual(1f, AudioPreferences.Get(AudioChannel.Effects));
            Assert.AreEqual(1f, AudioPreferences.Get(AudioChannel.Interface));
        }

        [Test]
        public void IndependentChannelsPersistAndReloadWithoutChangingMaster()
        {
            AudioPreferences.Set(AudioChannel.Master, 0.6f);
            AudioPreferences.Set(AudioChannel.Music, 0.2f);
            AudioPreferences.Set(AudioChannel.Effects, 0.8f);
            AudioPreferences.Set(AudioChannel.Interface, 0f);
            AudioPreferences.Save();
            AudioPreferences.Reload();
            Assert.AreEqual(0.6f, AudioListener.volume);
            Assert.AreEqual(0.2f, AudioPreferences.Get(AudioChannel.Music));
            Assert.AreEqual(0.8f, AudioPreferences.Get(AudioChannel.Effects));
            Assert.AreEqual(0f, AudioPreferences.Get(AudioChannel.Interface));
        }

        [TestCase(-3f, 0f)]
        [TestCase(3f, 1f)]
        [TestCase(float.NaN, 1f)]
        public void CorruptSavedGainIsSanitized(float saved, float expected)
        {
            PlayerPrefs.SetFloat("rss.audio.music", saved);
            AudioPreferences.Reload();
            Assert.AreEqual(expected, AudioPreferences.Get(AudioChannel.Music));
        }

        [Test]
        public void MusicMuteAndUnmutePreserveAuthoredLevelAndDuckingWhilePaused()
        {
            var music = Channel(AudioChannel.Music, 0.45f);
            var effects = Channel(AudioChannel.Effects, 0.75f);
            AudioPreferences.Set(AudioChannel.Music, 0.5f);
            music.SetMixGain(0.4f);
            Assert.That(music.GetComponent<AudioSource>().volume, Is.EqualTo(0.09f).Within(0.0001f));
            Time.timeScale = 0f;
            AudioListener.pause = true;
            AudioPreferences.Set(AudioChannel.Music, 0f);
            Assert.AreEqual(0f, music.GetComponent<AudioSource>().volume);
            Assert.AreEqual(0.75f, effects.GetComponent<AudioSource>().volume);
            AudioPreferences.Set(AudioChannel.Music, 1f);
            Assert.That(music.GetComponent<AudioSource>().volume, Is.EqualTo(0.18f).Within(0.0001f));
            music.SetMixGain(1f);
            Assert.That(music.GetComponent<AudioSource>().volume, Is.EqualTo(0.45f).Within(0.0001f));
        }

        [Test]
        public void ReenabledAndNewSceneSourcesNeverMultiplyTheUserGainTwice()
        {
            AudioPreferences.Set(AudioChannel.Music, 0.5f);
            var first = Channel(AudioChannel.Music, 0.45f);
            Invoke(first, "OnDisable");
            Invoke(first, "OnEnable");
            var next = Channel(AudioChannel.Music, 0.45f);
            Assert.That(first.GetComponent<AudioSource>().volume, Is.EqualTo(0.225f).Within(0.0001f));
            Assert.AreEqual(first.GetComponent<AudioSource>().volume, next.GetComponent<AudioSource>().volume);
        }

        [Test]
        public void MusicUpdatesAndCinematicEarlyReturnCannotOverrideUserMute()
        {
            var channel = Channel(AudioChannel.Music, 0.45f);
            var music = channel.gameObject.AddComponent<BgmPlayer>();
            var director = _root.AddComponent<BattleDirector>();
            Set(music, "_director", director);
            Set(music, "_source", channel.GetComponent<AudioSource>());
            Invoke(music, "Awake");
            AudioPreferences.Set(AudioChannel.Music, 0f);
            Invoke(music, "Update");
            Assert.AreEqual(0f, channel.GetComponent<AudioSource>().volume);
            typeof(BattleDirector).GetProperty("BossDeathCinematicRemaining").SetValue(director, 1f);
            AudioPreferences.Set(AudioChannel.Music, 0.3f);
            Invoke(music, "Update");
            Assert.That(channel.GetComponent<AudioSource>().volume, Is.EqualTo(0.135f).Within(0.0001f));
        }

        [Test]
        public void UiFeedbackUsesItsOwnPausedVoiceAndDeduplicatesOneDecision()
        {
            var ui = UiVoice();
            AudioListener.pause = true;
            Assert.IsTrue(UiAudio.Play(UiCue.Navigate));
            Assert.IsFalse(UiAudio.Play(UiCue.Navigate));
            Assert.IsTrue(UiAudio.Play(UiCue.Confirm));
            Assert.IsFalse(UiAudio.Play(UiCue.Confirm));
            Assert.IsTrue(UiAudio.Play(UiCue.Reject));
            Assert.IsFalse(UiAudio.Play(UiCue.Confirm));
            Assert.IsTrue(ui.GetComponent<AudioSource>().ignoreListenerPause);
            Assert.IsFalse(Channel(AudioChannel.Effects, 1f).GetComponent<AudioSource>().ignoreListenerPause);
        }

        [TestCase(AudioChannel.Master)]
        [TestCase(AudioChannel.Interface)]
        public void UiFeedbackRespectsBothMuteControls(AudioChannel muted)
        {
            UiVoice();
            AudioPreferences.Set(muted, 0f);
            Assert.IsFalse(UiAudio.Play(UiCue.Confirm));
            AudioPreferences.Set(muted, 1f);
            Assert.IsTrue(UiAudio.Play(UiCue.Confirm));
        }

        [Test]
        public void MissingUiClipIsASilentNoOp()
        {
            var ui = UiVoice();
            Set(ui, "_reject", null);
            Assert.IsFalse(UiAudio.Play(UiCue.Reject));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KeyboardAndPadAdjustOnlySelectedChannelAndMuteRestoresIt(bool pad)
        {
            var panel = Panel();
            panel.Open();
            Set(panel, "_openedFrame", -1);
            Press(pad, Key.DownArrow, GamepadButton.DpadDown);
            Invoke(panel, "Update");
            Press(pad, Key.LeftArrow, GamepadButton.DpadLeft);
            Invoke(panel, "Update");
            Assert.That(AudioPreferences.Get(AudioChannel.Music), Is.EqualTo(0.9f).Within(0.0001f));
            Assert.AreEqual(1f, AudioPreferences.Get(AudioChannel.Master));
            Press(pad, Key.Enter, GamepadButton.South);
            Invoke(panel, "Update");
            Assert.AreEqual(0f, AudioPreferences.Get(AudioChannel.Music));
            Press(pad, Key.None, null);
            Press(pad, Key.Enter, GamepadButton.South);
            Invoke(panel, "Update");
            Assert.That(AudioPreferences.Get(AudioChannel.Music), Is.EqualTo(0.9f).Within(0.0001f));
        }

        [Test]
        public void OpeningConfirmIsNotAlsoAnImmediateMute()
        {
            var panel = Panel();
            Press(false, Key.Enter, null);
            panel.Open();
            Invoke(panel, "Update");
            Assert.AreEqual(1f, AudioPreferences.Get(AudioChannel.Master));
        }

        [Test]
        public void SliderCallbackUpdatesOnlyItsChannelAndResetAffectsOnlyAudio()
        {
            var panel = Panel();
            panel.Open();
            Get<Slider[]>(panel, "_sliders")[2].value = 3f;
            Assert.That(AudioPreferences.Get(AudioChannel.Effects), Is.EqualTo(0.3f).Within(0.0001f));
            Assert.AreEqual(1f, AudioPreferences.Get(AudioChannel.Music));
            Invoke(panel, "ResetAudio");
            for (int i = 0; i < 4; i++) Assert.AreEqual(1f, AudioPreferences.Get((AudioChannel)i));
        }

        [Test]
        public void ClosingAudioCannotResumePauseOrLaunchTitleInTheSameFrame()
        {
            var panel = Panel();
            var pause = _root.AddComponent<PauseScreen>();
            Invoke(pause, "SetPaused", true);
            panel.Open();
            Set(panel, "_openedFrame", -1);
            Press(false, Key.Escape, null);
            Invoke(panel, "Update");
            Invoke(pause, "Update");
            Assert.IsFalse(panel.IsOpen);
            Assert.AreEqual(0f, Time.timeScale);
            var title = _root.AddComponent<TitleScreen>();
            Assert.IsTrue(title.ModalOpen);
        }

        [TestCase("Title", 1, 0)]
        [TestCase("Battle", 1, 1)]
        public void SavedScenesRouteAllSourcesAndReferenceOnlyAdoptedUiClips(string name, int musicCount, int effectsCount)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity", OpenSceneMode.Additive);
            try
            {
                var sources = new List<AudioChannelSource>();
                int listeners = 0, uiCount = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    sources.AddRange(root.GetComponentsInChildren<AudioChannelSource>(true));
                    listeners += root.GetComponentsInChildren<AudioListener>(true).Length;
                    foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                        Assert.IsNotNull(source.GetComponent<AudioChannelSource>(), source.name);
                    foreach (var ui in root.GetComponentsInChildren<UiAudio>(true))
                    {
                        uiCount++;
                        // ignoreListenerPause is configured at runtime, not serialized by AudioSource.
                        Invoke(ui, "Awake");
                        Assert.IsTrue(ui.GetComponent<AudioSource>().ignoreListenerPause);
                        Assert.AreEqual("sfx_pickup", Get<AudioClip>(ui, "_navigate").name);
                        Assert.AreEqual("sfx_powerup", Get<AudioClip>(ui, "_confirm").name);
                        Assert.AreEqual("sfx_hit", Get<AudioClip>(ui, "_reject").name);
                    }
                }
                Assert.AreEqual(musicCount, sources.FindAll(s => s.Channel == AudioChannel.Music).Count);
                Assert.AreEqual(effectsCount, sources.FindAll(s => s.Channel == AudioChannel.Effects).Count);
                Assert.AreEqual(1, sources.FindAll(s => s.Channel == AudioChannel.Interface).Count);
                Assert.AreEqual(1, uiCount);
                Assert.AreEqual(1, listeners);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        AudioChannelSource Channel(AudioChannel channel, float authoredVolume)
        {
            var go = new GameObject(channel.ToString());
            go.transform.SetParent(_root.transform);
            go.AddComponent<AudioSource>().volume = authoredVolume;
            var gain = go.AddComponent<AudioChannelSource>();
            gain.Configure(channel);
            Invoke(gain, "OnEnable");
            _channels.Add(gain);
            return gain;
        }

        UiAudio UiVoice()
        {
            var gain = Channel(AudioChannel.Interface, 1f);
            var ui = gain.gameObject.AddComponent<UiAudio>();
            _clip = AudioClip.Create("test cue", 100, 1, 48000, false);
            Set(ui, "_navigate", _clip); Set(ui, "_confirm", _clip); Set(ui, "_reject", _clip);
            Invoke(ui, "Awake");
            return ui;
        }

        AudioSettingsPanel Panel()
        {
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11.ttf");
            var panel = AudioSettingsPanel.Create(_root.transform, font, font);
            Invoke(panel, "OnEnable");
            return panel;
        }

        void Press(bool pad, Key key, GamepadButton? button)
        {
            if (pad) InputSystem.QueueStateEvent(_pad, button.HasValue ? new GamepadState().WithButton(button.Value) : new GamepadState());
            else InputSystem.QueueStateEvent(_keyboard, key == Key.None ? new KeyboardState() : new KeyboardState(key));
            InputSystem.Update();
        }

        static void ResetModalState() => typeof(AudioSettingsPanel).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    }
}

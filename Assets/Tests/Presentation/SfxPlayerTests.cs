using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Shmup.Core.Simulation;
using Shmup.Presentation.Battle;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class SfxPlayerTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        delegate void PlayAt(ReadOnlySpan<SimEvent> events, double time);
        static readonly string[] Keys = { "rss.volume", "rss.audio.music", "rss.audio.effects", "rss.audio.ui" };
        readonly float[] _saved = new float[4];
        readonly bool[] _had = new bool[4];
        readonly List<AudioClip> _clips = new List<AudioClip>();
        GameObject _root;
        SfxPlayer _player;
        PlayAt _play;
        AudioClip _hit, _explosion, _pickup, _powerup, _warning, _charge, _fire, _heavy;
        bool _paused;
        float _volume;
        UnityEngine.Random.State _random;

        [SetUp]
        public void SetUp()
        {
            _paused = AudioListener.pause;
            _volume = AudioListener.volume;
            _random = UnityEngine.Random.state;
            for (int i = 0; i < Keys.Length; i++)
            {
                _had[i] = PlayerPrefs.HasKey(Keys[i]);
                _saved[i] = PlayerPrefs.GetFloat(Keys[i]);
                PlayerPrefs.DeleteKey(Keys[i]);
            }
            AudioPreferences.Reload();
            AudioListener.pause = false;
            _root = new GameObject("Sfx test");
            _root.SetActive(false);
            var source = _root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = .8f;
            _player = _root.AddComponent<SfxPlayer>();
            Set("_source", source);
            _hit = Clip("_hit", .1f);
            _explosion = Clip("_explosion", .4f);
            _pickup = Clip("_pickup", .18f);
            _powerup = Clip("_powerup", .35f);
            _warning = Clip("_warning", .6f);
            _charge = Clip("_laserCharge", .4f);
            _fire = Clip("_laserFire", .2f);
            _heavy = Clip("_laserFireHeavy", .5f);
            _root.SetActive(true);
            Invoke("Awake");
            Invoke("OnEnable");
            _play = (PlayAt)typeof(SfxPlayer).GetMethod("PlayEventsAt", Private).CreateDelegate(typeof(PlayAt), _player);
        }

        [TearDown]
        public void TearDown()
        {
            Invoke("OnDisable");
            foreach (var channel in _root.GetComponentsInChildren<AudioChannelSource>(true))
                typeof(AudioChannelSource).GetMethod("OnDisable", Private).Invoke(channel, null);
            Object.DestroyImmediate(_root);
            foreach (var clip in _clips) Object.DestroyImmediate(clip);
            _clips.Clear();
            for (int i = 0; i < Keys.Length; i++)
                if (_had[i]) PlayerPrefs.SetFloat(Keys[i], _saved[i]); else PlayerPrefs.DeleteKey(Keys[i]);
            AudioPreferences.Reload();
            PlayerPrefs.Save();
            AudioListener.pause = _paused;
            AudioListener.volume = _volume;
            UnityEngine.Random.state = _random;
        }

        [Test]
        public void DenseImpactTrafficIsRateLimitedAcrossTicksAndUsesOneVoice()
        {
            var events = new[] { E(SimEventType.EnemyHit), E(SimEventType.EnemyHit), E(SimEventType.ObstacleDamaged) };
            for (int i = 0; i < 300; i++) _play(events, i / 60d);
            Assert.That(_player.StartedVoiceCount, Is.InRange(49, 51), "Five seconds of 60 Hz traffic must not start 300 impacts.");
            Assert.AreEqual(_hit, Voice("Impact").clip);
            Assert.AreEqual(1, AssignedVoices());
            Assert.AreEqual(6, _root.GetComponentsInChildren<AudioSource>().Length);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BombWinsOverMassKillsRegardlessOfOrder(bool reverse)
        {
            var events = new[] { E(SimEventType.EnemyKilled), E(SimEventType.ObstacleDestroyed), E(SimEventType.BombActivated) };
            if (reverse) Array.Reverse(events);
            _play(events, 1);
            Assert.AreEqual(1, _player.StartedVoiceCount);
            Assert.AreEqual(_explosion, Voice("Destruction").clip);
            Assert.AreEqual(.8f, Voice("Destruction").volume, .001f);
            _play(new[] { E(SimEventType.EnemyKilled) }, 1.3);
            Assert.AreEqual(1, _player.StartedVoiceCount, "A lesser explosion cannot cut the bomb tail.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WidestHostileBeamWinsEvenWhenTelegraphAndFireShareBatch(bool reverse)
        {
            var events = new[] { E(SimEventType.LaserFired, 99), E(SimEventType.LaserFired, 1, 64),
                E(SimEventType.LaserFired, 2, 1280), E(SimEventType.LaserTelegraphStarted, 1),
                E(SimEventType.LaserTelegraphStarted, 2), E(SimEventType.LaserEnded, 2) };
            if (reverse) Array.Reverse(events);
            _play(events, 1);
            Assert.AreEqual(_heavy, Voice("Threat").clip);
            Assert.AreEqual(.8f * .72f, Voice("Threat").volume, .001f);
            Assert.AreEqual(1f, Voice("Threat").pitch);
            _play(new[] { E(SimEventType.LaserFired, 1, 64) }, 1.2);
            Assert.AreEqual(_heavy, Voice("Threat").clip, "Thin beams must not interrupt the heavy beam.");
        }

        [Test]
        public void MissingHeavyClipFallsBackToOrdinaryHostileFire()
        {
            Set("_laserFireHeavy", null);
            _play(new[] { E(SimEventType.LaserTelegraphStarted, 1), E(SimEventType.LaserFired, 1, 1280) }, 1);
            Assert.AreEqual(_fire, Voice("Threat").clip);
            Assert.AreEqual(.8f * .5f, Voice("Threat").volume, .001f);
        }

        [Test]
        public void MissingWarningDoesNotConsumeLowerValidCue()
        {
            Set("_warning", null);
            _play(new[] { E(SimEventType.BossSpawned), E(SimEventType.LaserTelegraphStarted, 1) }, 1);
            Assert.AreEqual(_charge, Voice("Warning").clip);
            Assert.AreEqual(1, _player.StartedVoiceCount);
        }

        [Test]
        public void PlayerDeathOverridesHitAndKeepsItsVoiceUnderExplosionTraffic()
        {
            _play(new[] { E(SimEventType.EnemyHit), E(SimEventType.EnemyKilled) }, 1);
            _play(new[] { E(SimEventType.PlayerHit), E(SimEventType.PlayerKilled), E(SimEventType.BombActivated) }, 1.01);
            Assert.AreEqual(_explosion, Voice("Player").clip);
            Assert.AreEqual(.8f, Voice("Player").volume, .001f);
            Assert.AreEqual(.8f * .5f * .35f, Voice("Impact").volume, .001f);
            Assert.AreEqual(.8f * .35f, Voice("Destruction").volume, .001f);
            _play(new[] { E(SimEventType.PlayerHit) }, 1.2);
            Assert.AreEqual(_explosion, Voice("Player").clip);
        }

        [Test]
        public void WarningAndPlayerHitBothStartWithStablePitchWhileBackgroundIsDucked()
        {
            _play(new[] { E(SimEventType.EnemyHit), E(SimEventType.BossAttackTelegraphed), E(SimEventType.PlayerHit) }, 1);
            Assert.AreEqual(3, _player.StartedVoiceCount);
            Assert.AreEqual(_warning, Voice("Warning").clip);
            Assert.AreEqual(_hit, Voice("Player").clip);
            Assert.AreEqual(1f, Voice("Warning").pitch);
            float pitch = Voice("Impact").pitch;
            _play(new[] { E(SimEventType.CapsulePicked) }, 1.01);
            Assert.AreEqual(pitch, Voice("Impact").pitch, "Other cue starts must not retune this voice.");
            Assert.AreEqual(1f, Voice("Warning").pitch);
        }

        [TestCase(SimEventType.BossMovementTelegraphed)]
        [TestCase(SimEventType.BossPartMeleeTelegraphed)]
        [TestCase(SimEventType.WarshipWarningStarted)]
        public void ExistingWarningClipAlsoSignalsPreviouslySilentDanger(SimEventType type)
        {
            _play(new[] { E(type) }, 1);
            Assert.AreEqual(_warning, Voice("Warning").clip);
        }

        [Test]
        public void OrdinaryGunfireRemainsSilentAndPlayerBeamStaysQuiet()
        {
            _play(new[] { E(SimEventType.PlayerFired), E(SimEventType.PlayerFired, 0, 1) }, 1);
            Assert.AreEqual(0, _player.StartedVoiceCount);
            _play(new[] { E(SimEventType.LaserFired, 20) }, 1.1);
            Assert.AreEqual(.8f * .15f, Voice("Impact").volume, .001f);
            Assert.IsNull(Voice("Threat").clip);
        }

        [Test]
        public void ImportantRewardInterruptsPickupAndIsNotInterruptedByPickupSpam()
        {
            _play(new[] { E(SimEventType.CapsulePicked) }, 1);
            _play(new[] { E(SimEventType.PowerUpLevelChanged) }, 1.01);
            _play(new[] { E(SimEventType.CapsulePicked) }, 1.25);
            Assert.AreEqual(2, _player.StartedVoiceCount);
            Assert.AreEqual(_powerup, Voice("Reward").clip);
            _play(new[] { E(SimEventType.CapsulePicked) }, 1.5);
            Assert.AreEqual(_pickup, Voice("Reward").clip);
        }

        [TestCase(AudioChannel.Master)]
        [TestCase(AudioChannel.Effects)]
        public void MuteStopsTailsAndKeepsHostileTrackingWithoutQueuingOldSounds(AudioChannel channel)
        {
            _play(new[] { E(SimEventType.EnemyKilled) }, 1);
            AudioPreferences.Set(channel, 0);
            Assert.AreEqual(0, AssignedVoices());
            _play(new[] { E(SimEventType.LaserTelegraphStarted, 1), E(SimEventType.CapsulePicked) }, 1.1);
            Assert.AreEqual(1, _player.StartedVoiceCount);
            AudioPreferences.Set(channel, 1);
            Assert.AreEqual(0, AssignedVoices());
            _play(new[] { E(SimEventType.LaserFired, 1, 1280) }, 1.2);
            Assert.AreEqual(_heavy, Voice("Threat").clip);
        }

        [Test]
        public void PauseDiscardsNewCuesAndDoesNotReplaceExistingClip()
        {
            _play(new[] { E(SimEventType.BossAttackTelegraphed) }, 1);
            AudioListener.pause = true;
            _play(new[] { E(SimEventType.PlayerHit) }, 1);
            Assert.AreEqual(1, _player.StartedVoiceCount);
            Assert.IsNull(Voice("Player").clip);
            foreach (var source in _root.GetComponentsInChildren<AudioSource>()) Assert.IsFalse(source.ignoreListenerPause);
            AudioListener.pause = false;
            _play(new[] { E(SimEventType.BossAttackTelegraphed) }, 1);
            Assert.AreEqual(1, _player.StartedVoiceCount, "Frozen DSP time must retain the warning cooldown.");
            _play(new[] { E(SimEventType.BossAttackTelegraphed) }, 1.7);
            Assert.AreEqual(2, _player.StartedVoiceCount);
        }

        [Test]
        public void ResetClearsCooldownAndOldLaserIdentityWithoutAllocatingMoreSources()
        {
            _play(new[] { E(SimEventType.LaserTelegraphStarted, 5), E(SimEventType.EnemyHit) }, 1);
            _player.ResetPlayback();
            Assert.AreEqual(0, AssignedVoices());
            _play(new[] { E(SimEventType.LaserFired, 5) }, 1);
            Assert.IsNull(Voice("Threat").clip);
            Assert.AreEqual(_fire, Voice("Impact").clip);
            Invoke("OnDisable");
            Invoke("OnEnable");
            Invoke("Awake");
            Assert.AreEqual(6, _root.GetComponentsInChildren<AudioSource>().Length);
            Assert.AreEqual(0, AssignedVoices());
        }

        [Test]
        public void AllVoicesRespectEffectsLevelWithoutMultiplyingMasterTwice()
        {
            AudioPreferences.Set(AudioChannel.Master, .5f);
            AudioPreferences.Set(AudioChannel.Effects, .4f);
            _play(new[] { E(SimEventType.BossSpawned), E(SimEventType.PlayerHit), E(SimEventType.BombActivated) }, 1);
            foreach (var channel in _root.GetComponentsInChildren<AudioChannelSource>())
            {
                Assert.AreEqual(AudioChannel.Effects, channel.Channel);
                Assert.AreEqual(.8f, channel.BaseVolume, .001f);
            }
            Assert.AreEqual(.8f * .4f, Voice("Player").volume, .001f);
            Assert.AreEqual(.5f, AudioListener.volume);
        }

        [Test]
        public void DuckRecoversSmoothlyAndPreservesUserEffectsLevel()
        {
            AudioPreferences.Set(AudioChannel.Effects, .5f);
            _play(new[] { E(SimEventType.EnemyKilled), E(SimEventType.PlayerHit) }, 1);
            var update = typeof(SfxPlayer).GetMethod("UpdateMix", Private);
            update.Invoke(_player, new object[] { 1.1, .1f });
            Assert.AreEqual(.8f * .8f * .5f * .35f, Voice("Destruction").volume, .001f);
            update.Invoke(_player, new object[] { 1.3, .1f });
            Assert.AreEqual(.8f * .8f * .5f * .75f, Voice("Destruction").volume, .001f);
            update.Invoke(_player, new object[] { 1.4, .1f });
            Assert.AreEqual(.8f * .8f * .5f, Voice("Destruction").volume, .001f);
        }

        [Test]
        public void ActualBattleSceneSupportsDenseTrafficWithSixRoutedVoices()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity", OpenSceneMode.Additive);
            SfxPlayer player = null;
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    player = root.GetComponentInChildren<SfxPlayer>(true);
                    if (player != null) break;
                }
                Assert.IsNotNull(player);
                foreach (string field in new[] { "_hit", "_explosion", "_pickup", "_powerup", "_warning", "_laserCharge", "_laserFire", "_laserFireHeavy" })
                {
                    var clip = (AudioClip)typeof(SfxPlayer).GetField(field, Private).GetValue(player);
                    Assert.IsNotNull(clip, field);
                    Assert.Greater(clip.length, 0, field);
                }
                typeof(SfxPlayer).GetMethod("Awake", Private).Invoke(player, null);
                var play = (PlayAt)typeof(SfxPlayer).GetMethod("PlayEventsAt", Private).CreateDelegate(typeof(PlayAt), player);
                var ordinary = new[] { E(SimEventType.EnemyHit), E(SimEventType.EnemyKilled), E(SimEventType.CapsulePicked) };
                var danger = new[] { E(SimEventType.PlayerHit), E(SimEventType.BossAttackTelegraphed),
                    E(SimEventType.LaserTelegraphStarted, 1), E(SimEventType.LaserFired, 1, 1280), E(SimEventType.LaserEnded, 1) };
                for (int tick = 0; tick < 600; tick++)
                {
                    play(ordinary, tick / 60d);
                    if (tick % 60 == 0) play(danger, tick / 60d);
                }
                var sources = player.GetComponentsInChildren<AudioSource>();
                Assert.AreEqual(6, sources.Length);
                foreach (var source in sources)
                {
                    Assert.IsFalse(source.loop);
                    Assert.IsFalse(source.ignoreListenerPause);
                    Assert.AreEqual(AudioChannel.Effects, source.GetComponent<AudioChannelSource>().Channel);
                }
                Assert.AreEqual("sfx_laser_heavy", player.transform.Find("Sfx Threat").GetComponent<AudioSource>().clip.name);
                Assert.That(player.StartedVoiceCount, Is.InRange(180, 300));
                Debug.Log($"SFX_QA: actual Battle clips, 600 ticks / 10 seconds, {player.StartedVoiceCount} admitted starts, {sources.Length} reusable sources; Editor dry-run, no audition.");
            }
            finally
            {
                if (player != null)
                {
                    typeof(SfxPlayer).GetMethod("OnDisable", Private).Invoke(player, null);
                    foreach (var channel in player.GetComponentsInChildren<AudioChannelSource>())
                        typeof(AudioChannelSource).GetMethod("OnDisable", Private).Invoke(channel, null);
                }
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void VoiceGateProtectsHigherPriorityAndAllowsUrgentPreemption()
        {
            var gate = new SfxVoiceGate();
            Assert.IsTrue(gate.TryStart(1, 1, .1, .5));
            Assert.IsFalse(gate.TryStart(1.05, 1, .1, .5));
            Assert.IsTrue(gate.TryStart(1.05, 2, .1, .5));
            Assert.IsFalse(gate.TryStart(1.4, 1, .1, .5));
            Assert.IsTrue(gate.TryStart(1.6, 1, .1, .5));
            gate.Reset();
            Assert.IsTrue(gate.TryStart(0, 0, .1, .5));
        }

        AudioSource Voice(string lane) => lane == "Impact" ? _root.GetComponent<AudioSource>()
            : _root.transform.Find("Sfx " + lane).GetComponent<AudioSource>();
        int AssignedVoices()
        {
            int count = 0;
            foreach (var voice in _root.GetComponentsInChildren<AudioSource>()) if (voice.clip != null) count++;
            return count;
        }
        AudioClip Clip(string field, float seconds)
        {
            var clip = AudioClip.Create(field, (int)(48000 * seconds), 1, 48000, false);
            _clips.Add(clip);
            Set(field, clip);
            return clip;
        }
        void Set(string field, object value) => typeof(SfxPlayer).GetField(field, Private).SetValue(_player, value);
        void Invoke(string method) => typeof(SfxPlayer).GetMethod(method, Private).Invoke(_player, null);
        static SimEvent E(SimEventType type, int id = 0, int arg = 0) => new SimEvent(type, id, 0, 0, arg);
    }
}

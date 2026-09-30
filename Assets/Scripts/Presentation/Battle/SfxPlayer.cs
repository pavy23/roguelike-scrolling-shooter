using System;
using System.Collections.Generic;
using Shmup.Core.Simulation;
using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// Core 시뮬 이벤트(REQ-005)를 효과음으로 번역한다. 순수 표현 — 게임 상태에 영향 없음.
    /// 채택음은 Tools/SfxGen/sfxgen_snes.py 산출물이고, 획득·강화 차임만
    /// Tools/SfxGen/sfxgen_chime.py 후보 a(유리 벨 완전5도)로 교체했다
    /// ("파워업과 봄 아이템 ... 사운드가 너무 거슬려", 2026-08-02).
    /// 레이저 예고·발사음은 Tools/SfxGen/sfxgen_laser.py 후보 b(험 + 흡기)다
    /// ("레이저 발사하는 소리도 따로 있어야 할듯", 2026-08-02).
    /// 6개의 재사용 소스와 시간 간격으로 동시 발음과 반복을 제한한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SfxPlayer : MonoBehaviour
    {
        [SerializeField] AudioSource _source;
        [SerializeField] AudioClip _laser;
        [SerializeField] AudioClip _hit;
        [SerializeField] AudioClip _explosion;
        [SerializeField] AudioClip _pickup;
        [SerializeField] AudioClip _powerup;
        [SerializeField] AudioClip _laserBeam;     // laser 계열 발사음
        [SerializeField] AudioClip _spreadShot;    // spread 계열 발사음
        [SerializeField] AudioClip _warning;       // 보스 위험 패턴 예고 (REQ-059)
        [SerializeField] AudioClip _laserCharge;   // 적·지형 레이저 예고 차지 (REQ-042)
        [SerializeField] AudioClip _laserFire;     // 적·지형 레이저 발사

        /// <summary>
        /// 초대형 빔 발사음. 반폭이 <see cref="HeavyBeamHalfWidthSubUnits"/>를
        /// 넘는 빔에만 쓴다 (사람 지시 2026-08-05: "3페이즈 대형 레이저는 소리가
        /// 너무 썰렁해서 대형 레이저를 쏘는 박력있는 레이저음으로").
        ///
        /// 기존 발사음은 잡몹 빔(반폭 0.25유닛)에 맞춰 짧고 얇다. 화면을 관통하는
        /// 반폭 5유닛 빔에 그것을 붙이면 크기와 소리가 어긋난다.
        /// </summary>
        [SerializeField] AudioClip _laserFireHeavy;

        /// <summary>
        /// 이 반폭(서브유닛)을 넘으면 초대형 빔 소리를 쓴다. 2유닛 = 512 —
        /// 잡몹 빔(0.25)과 갑판 포탑(0.75~0.875)은 아래, 코어 빔(5.0)과 레비아탄
        /// 레일건(5.625)은 위다.
        /// </summary>
        const int HeavyBeamHalfWidthSubUnits = 512;

        // Legacy ship/clip bindings are retained for serialized scene compatibility.
        // PlayerFired remains silent under the existing user-approved audio policy.
        public Shmup.Core.WeaponType WeaponFamily { get; set; } = Shmup.Core.WeaponType.Vulcan;

        [Range(0f, 1f)]
        [SerializeField] float _laserVolume = 0.35f;

        enum Lane { Warning, Threat, Player, Impact, Destruction, Reward, Count }

        struct Cue
        {
            public AudioClip Clip;
            public float Gain, Interval;
            public int Priority;
            public bool VaryPitch;
        }

        sealed class Voice
        {
            public AudioSource Source;
            public AudioChannelSource Channel;
            public readonly SfxVoiceGate Gate = new SfxVoiceGate();
            public float Gain;
        }

        readonly Voice[] _voices = new Voice[(int)Lane.Count];
        readonly Cue[] _pending = new Cue[(int)Lane.Count];
        // Only hostile beams have a telegraph. Keep tracking even while muted.
        readonly HashSet<int> _hostileLasers = new HashSet<int>();
        const int MaxTrackedLasers = 256;
        bool _initialized;
        double _duckUntil;
        float _backgroundGain = 1f;

        /// <summary>Cumulative admitted starts, for headless event-stream QA (not hardware voices).</summary>
        public int StartedVoiceCount { get; private set; }

        void Awake() => Initialize();
        void OnEnable() { Initialize(); AudioPreferences.Changed += OnPreferencesChanged; }
        void OnDisable() { AudioPreferences.Changed -= OnPreferencesChanged; ResetPlayback(); }

        void Initialize()
        {
            if (_initialized || _source == null) return;
            var originalChannel = _source.GetComponent<AudioChannelSource>();
            float baseVolume = originalChannel != null ? originalChannel.BaseVolume : _source.volume;
            for (int i = 0; i < _voices.Length; i++)
            {
                AudioSource source;
                GameObject child = null;
                if (i == (int)Lane.Impact) source = _source;
                else
                {
                    child = new GameObject("Sfx " + (Lane)i);
                    child.SetActive(false);
                    child.transform.SetParent(transform, false);
                    source = child.AddComponent<AudioSource>();
                    source.volume = baseVolume;
                    source.outputAudioMixerGroup = _source.outputAudioMixerGroup;
                    source.mute = _source.mute;
                }
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = false;
                source.priority = i < (int)Lane.Impact ? 24 : 160;
                var channel = source.GetComponent<AudioChannelSource>();
                if (channel == null) channel = source.gameObject.AddComponent<AudioChannelSource>();
                channel.Configure(AudioChannel.Effects);
                _voices[i] = new Voice { Source = source, Channel = channel };
                if (child != null) child.SetActive(true);
            }
            _initialized = true;
        }

        public void PlayEvents(ReadOnlySpan<SimEvent> events) => PlayEventsAt(events, AudioSettings.dspTime);

        // Explicit clock seam lets EditMode QA replay dense traffic without playing sound or waiting.
        void PlayEventsAt(ReadOnlySpan<SimEvent> events, double now)
        {
            if (!_initialized || !isActiveAndEnabled) return;
            Array.Clear(_pending, 0, _pending.Length);
            // Pre-register telegraphs so an immediate fire is classified correctly regardless of
            // event ordering in the batch. Remove ended ids only after the fire pass.
            for (int i = 0; i < events.Length; i++)
                if (events[i].Type == SimEventType.LaserTelegraphStarted)
                {
                    if (_hostileLasers.Count >= MaxTrackedLasers) _hostileLasers.Clear();
                    _hostileLasers.Add(events[i].EntityId);
                }

            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                switch (e.Type)
                {
                    // Existing user policy: main-shot and missile auto-fire remain silent.
                    case SimEventType.PlayerFired: break;
                    case SimEventType.EnemyHit:
                        Offer(Lane.Impact, _hit, .5f, 2, .09f, true); break;
                    case SimEventType.ObstacleDamaged:
                        Offer(Lane.Impact, _hit, .3f, 1, .12f, true); break;
                    case SimEventType.EnemyKilled:
                        Offer(Lane.Destruction, _explosion, .8f, 2, .14f, true); break;
                    case SimEventType.ObstacleDestroyed:
                        Offer(Lane.Destruction, _explosion, .6f, 1, .18f, true); break;
                    case SimEventType.BombActivated:
                        Offer(Lane.Destruction, _explosion, 1f, 3, .25f); break;
                    case SimEventType.PlayerHit:
                        Offer(Lane.Player, _hit, 1f, 1, .12f); break;
                    case SimEventType.PlayerKilled:
                        Offer(Lane.Player, _explosion, 1f, 2, .25f); break;
                    case SimEventType.CapsulePicked:
                        Offer(Lane.Reward, _pickup, .5f, 1, .14f, true); break;
                    case SimEventType.PowerUpLevelChanged:
                        Offer(Lane.Reward, _powerup, .6f, 3, .2f); break;
                    case SimEventType.BombAcquired:
                        Offer(Lane.Reward, _powerup, .55f, 2, .2f); break;
                    case SimEventType.StageCleared:
                        Offer(Lane.Reward, _powerup, .45f, 4, .3f); break;
                    case SimEventType.BombActivationRejectedEmpty:
                        Offer(Lane.Reward, _hit, .25f, 0, .3f); break;
                    case SimEventType.BossSpawned:
                    case SimEventType.WarshipWarningStarted:
                        Offer(Lane.Warning, _warning, .85f, 3, .35f); break;
                    case SimEventType.BossPhaseChanged:
                        Offer(Lane.Warning, _hit, 1f, 1, .2f); break;
                    case SimEventType.BossAttackTelegraphed:
                    case SimEventType.BossMovementTelegraphed:
                    case SimEventType.BossPartMeleeTelegraphed:
                        Offer(Lane.Warning, _warning, .7f, 2, .25f); break;
                    case SimEventType.LaserTelegraphStarted:
                        Offer(Lane.Warning, _laserCharge, .35f, 0, .2f); break;
                    case SimEventType.LaserFired:
                        if (_hostileLasers.Contains(e.EntityId))
                        {
                            bool heavy = e.Arg >= HeavyBeamHalfWidthSubUnits;
                            Offer(Lane.Threat, heavy && _laserFireHeavy != null ? _laserFireHeavy : _laserFire,
                                heavy && _laserFireHeavy != null ? .72f : .5f, heavy ? 2 : 1, .12f);
                        }
                        else Offer(Lane.Impact, _laserFire, .15f, 0, .25f);
                        break;
                }
            }
            for (int i = 0; i < events.Length; i++)
                if (events[i].Type == SimEventType.LaserEnded) _hostileLasers.Remove(events[i].EntityId);

            if (AudioListener.pause || AudioPreferences.Get(AudioChannel.Master) <= 0f
                || AudioPreferences.Get(AudioChannel.Effects) <= 0f) return;

            // Protected voices go first so background attacks are already ducked at their onset.
            for (int i = 0; i < _voices.Length; i++)
            {
                var cue = _pending[i];
                if (cue.Clip == null) continue;
                var voice = _voices[i];
                // Admission uses the longest possible duration; random pitch never retunes a
                // sound already playing. Warnings and major feedback retain their authored pitch.
                double duration = cue.Clip.length / (cue.VaryPitch ? .96f : 1f);
                double interval = i <= (int)Lane.Player || i == (int)Lane.Reward
                    ? Math.Max(cue.Interval, duration) : cue.Interval;
                if (!voice.Gate.TryStart(now, cue.Priority, interval, duration)) continue;
                if (i < (int)Lane.Impact)
                {
                    _duckUntil = Math.Max(_duckUntil, now + Math.Min(.5, Math.Max(.25, duration)));
                    _backgroundGain = .35f;
                }
                voice.Source.Stop();
                voice.Source.clip = cue.Clip;
                voice.Source.pitch = cue.VaryPitch ? UnityEngine.Random.Range(.96f, 1.04f) : 1f;
                voice.Gain = cue.Gain;
                ApplyGain(i);
                // Batch Editor tests inspect admission and source wiring without audible output.
                if (Application.isPlaying) voice.Source.Play();
                StartedVoiceCount++;
            }
            RefreshGains();
        }

        void Offer(Lane lane, AudioClip clip, float gain, int priority, float interval, bool varyPitch = false)
        {
            if (clip == null) return; // An absent high-priority clip must not silence a valid fallback.
            ref var candidate = ref _pending[(int)lane];
            if (candidate.Clip != null && candidate.Priority >= priority) return;
            candidate = new Cue { Clip = clip, Gain = gain, Priority = priority,
                Interval = interval, VaryPitch = varyPitch };
        }

        void Update()
            => UpdateMix(AudioSettings.dspTime, Time.unscaledDeltaTime);

        void UpdateMix(double now, float deltaTime)
        {
            if (!_initialized || AudioListener.pause) return;
            if (now >= _duckUntil)
                _backgroundGain = Mathf.MoveTowards(_backgroundGain, 1f, deltaTime * 4f);
            RefreshGains();
        }

        void ApplyGain(int lane)
        {
            var voice = _voices[lane];
            voice.Channel.SetMixGain(voice.Gain * (lane < (int)Lane.Impact ? 1f : _backgroundGain));
        }
        void RefreshGains() { if (_initialized) for (int i = 0; i < _voices.Length; i++) ApplyGain(i); }
        void OnPreferencesChanged()
        {
            if (AudioPreferences.Get(AudioChannel.Master) <= 0f || AudioPreferences.Get(AudioChannel.Effects) <= 0f)
                StopVoices(); // Unmuting must not resurrect an old warning/explosion tail.
        }
        void StopVoices()
        {
            if (!_initialized) return;
            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];
                voice.Source.Stop();
                voice.Source.clip = null;
                voice.Gate.Reset();
                voice.Gain = 0;
            }
            _duckUntil = 0;
            _backgroundGain = 1f;
            RefreshGains();
        }
        public void ResetPlayback() { StopVoices(); _hostileLasers.Clear(); }
    }
}

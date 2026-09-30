using UnityEngine;

namespace Shmup.Presentation.Battle
{
    [DisallowMultipleComponent, RequireComponent(typeof(AudioSource)), DefaultExecutionOrder(-500)]
    public sealed class AudioChannelSource : MonoBehaviour
    {
        [SerializeField] AudioChannel _channel = AudioChannel.Music;
        AudioSource _source;
        float _baseVolume, _mixGain = 1f;
        bool _initialized;
        public AudioChannel Channel => _channel;
        public float BaseVolume { get { Initialize(); return _baseVolume; } }

        public void Configure(AudioChannel channel)
        {
            if (channel == AudioChannel.Master) throw new System.ArgumentException("Master is controlled by the listener.");
            _channel = channel;
            if (Application.isPlaying) Refresh();
        }

        void Initialize()
        {
            if (_initialized) return;
            _source = GetComponent<AudioSource>();
            _baseVolume = _source.volume;
            _initialized = true;
        }

        void Awake() => Initialize();
        void OnEnable() { AudioPreferences.Changed += Refresh; Refresh(); }
        void OnDisable() => AudioPreferences.Changed -= Refresh;

        public void SetMixGain(float gain) { _mixGain = Mathf.Clamp01(gain); Refresh(); }
        void Refresh()
        {
            Initialize();
            _source.volume = _baseVolume * _mixGain * AudioPreferences.Get(_channel);
        }
    }
}

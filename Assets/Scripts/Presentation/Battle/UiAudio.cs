using UnityEngine;

namespace Shmup.Presentation.Battle
{
    public enum UiCue { Navigate, Confirm, Back, Reject }

    /// <summary>One persistent UI voice, independent of paused combat audio. Uses adopted clips.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
    public sealed class UiAudio : MonoBehaviour
    {
        [SerializeField] AudioClip _navigate, _confirm, _reject;
        static UiAudio _instance;
        AudioSource _source;
        int _lastFrame = -1;
        UiCue _lastCue;
        float _lastNavigate = float.NegativeInfinity;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.ignoreListenerPause = true;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        public static bool Play(UiCue cue) => _instance != null && _instance.PlayCue(cue);

        bool PlayCue(UiCue cue)
        {
            if (_source == null || AudioPreferences.Get(AudioChannel.Master) <= 0f
                || AudioPreferences.Get(AudioChannel.Interface) <= 0f) return false;
            if (_lastFrame == Time.frameCount && cue <= _lastCue) return false;
            if (cue == UiCue.Navigate && Time.unscaledTime - _lastNavigate < 0.075f) return false;
            var clip = cue == UiCue.Confirm ? _confirm : cue == UiCue.Reject ? _reject : _navigate;
            if (clip == null) return false;
            _lastFrame = Time.frameCount;
            _lastCue = cue;
            if (cue == UiCue.Navigate) _lastNavigate = Time.unscaledTime;
            _source.Stop();
            _source.clip = clip;
            _source.pitch = cue == UiCue.Back ? 0.8f : cue == UiCue.Reject ? 0.9f : 1f;
            float gain = cue == UiCue.Confirm ? 0.25f : cue == UiCue.Reject ? 0.18f : 0.12f;
            // Editor UI inspections/tests do not play sound on the shared PC.
            if (Application.isPlaying) _source.PlayOneShot(clip, gain);
            return true;
        }

        void OnApplicationPause(bool paused) { if (paused) AudioPreferences.Save(); }
        void OnApplicationFocus(bool focused) { if (!focused) AudioPreferences.Save(); }
        void OnApplicationQuit() => AudioPreferences.Save();
        void OnDestroy() { if (_instance == this) _instance = null; }
    }
}

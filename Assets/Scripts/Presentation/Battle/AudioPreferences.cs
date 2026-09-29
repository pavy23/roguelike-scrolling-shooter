using System;
using UnityEngine;

namespace Shmup.Presentation.Battle
{
    public enum AudioChannel { Master, Music, Effects, Interface }

    /// <summary>User gains only; authored source levels and music ducking remain separate.</summary>
    public static class AudioPreferences
    {
        static readonly string[] Keys = { "rss.volume", "rss.audio.music", "rss.audio.effects", "rss.audio.ui" };
        static readonly float[] Gains = new float[4];
        static bool _loaded;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _loaded = false; Changed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Reload()
        {
            for (int i = 0; i < Gains.Length; i++) Gains[i] = Sanitize(PlayerPrefs.GetFloat(Keys[i], 1f));
            _loaded = true;
            AudioListener.volume = Gains[(int)AudioChannel.Master];
            Changed?.Invoke();
        }

        public static float Get(AudioChannel channel)
        {
            if (!_loaded) Reload();
            return Gains[(int)channel];
        }

        public static bool Set(AudioChannel channel, float value)
        {
            value = Sanitize(value);
            if (Mathf.Approximately(Get(channel), value)) return false;
            Gains[(int)channel] = value;
            PlayerPrefs.SetFloat(Keys[(int)channel], value);
            if (channel == AudioChannel.Master) AudioListener.volume = value;
            Changed?.Invoke();
            SaveFlush.Request();
            return true;
        }

        public static void RestoreDefaults()
        {
            for (int i = 0; i < Gains.Length; i++) Set((AudioChannel)i, 1f);
        }

        public static void Save() { PlayerPrefs.Save(); SaveFlush.FlushNow(); }
        static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);
    }
}

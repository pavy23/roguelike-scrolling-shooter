namespace Shmup.Presentation.Battle
{
    /// <summary>Presentation-only admission for one reusable voice, timed by the audio clock.</summary>
    public sealed class SfxVoiceGate
    {
        double _nextStart, _protectedUntil;
        int _priority;
        bool _used;

        public bool TryStart(double now, int priority, double interval, double duration)
        {
            if (_used && ((now < _nextStart && priority <= _priority)
                || (now < _protectedUntil && priority < _priority))) return false;
            _used = true;
            _priority = priority;
            _nextStart = now + interval;
            _protectedUntil = now + duration;
            return true;
        }

        public void Reset() { _used = false; _nextStart = _protectedUntil = 0; _priority = 0; }
    }
}

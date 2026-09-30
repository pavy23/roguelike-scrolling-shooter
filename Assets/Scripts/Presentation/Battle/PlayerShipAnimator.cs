using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 플레이어 엔진/뱅킹 프레임. BattleDirector가 관측한 실제 이동과 전투 틱을 사용한다.
    /// 전투가 멈추면 같은 프레임을 유지하며, 시뮬 상태나 판정은 수정하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerShipAnimator : MonoBehaviour
    {
        [SerializeField] SpriteRenderer _renderer;
        [SerializeField] Sprite[] _frames;
        [SerializeField] float _framesPerSecond = 10f;
        // Each optional clip holds one banked body pose; only exhaust may loop.
        // Unapproved/missing bank art falls back to the existing neutral engine.
        [SerializeField] Sprite[] _bankUpFrames;
        [SerializeField] Sprite[] _bankDownFrames;
        const int BankEntryTicks = 3;
        bool _hasMovementSample;
        int _lastTick, _lastY, _wantedDirection, _directionSinceTick, _bankDirection;

        void OnEnable() => ResetPose();
        void OnDisable() => ResetPose();

        public void ResetPose()
        {
            _hasMovementSample = false;
            _wantedDirection = _bankDirection = 0;
        }

        /// <summary>Read once per Core step. Held input at a boundary is not movement.</summary>
        public void ObserveMovementAtTick(int tick, int playerY)
        {
            if (!enabled) return;
            if (!_hasMovementSample || tick < _lastTick || (long)tick - _lastTick > 1)
            {
                // New battle, seek or skipped observations: establish a neutral baseline.
                ResetPose();
                _hasMovementSample = true;
                _lastTick = _directionSinceTick = tick;
                _lastY = playerY;
                return;
            }
            if (tick == _lastTick) return; // Pause/render repetition cannot consume movement twice.
            int direction = playerY.CompareTo(_lastY);
            _lastTick = tick;
            _lastY = playerY;
            if (direction != _wantedDirection)
            {
                _wantedDirection = direction;
                _directionSinceTick = tick;
                _bankDirection = 0; // Reverse through neutral; release/boundary returns immediately.
            }
            if ((long)tick - _directionSinceTick >= BankEntryTicks) _bankDirection = direction;
        }

        public void RenderAtTick(int tick)
        {
            if (!enabled || _renderer == null) return;
            var bank = _bankDirection > 0 ? _bankUpFrames : _bankDirection < 0 ? _bankDownFrames : null;
            var sprite = Sample(bank, tick) ?? Sample(_frames, tick);
            if (sprite != null) _renderer.sprite = sprite;
        }

        Sprite Sample(Sprite[] frames, int tick)
            => frames == null || frames.Length == 0 ? null
                : frames[SpriteAnimationPlayback.FrameAt(tick, _framesPerSecond, frames.Length)];
    }
}

using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 플레이어 엔진 프레임. BattleDirector가 관측한 전투 틱에서 샘플링한다.
    /// 전투가 멈추면 같은 프레임을 유지하며, 시뮬 상태나 판정은 수정하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerShipAnimator : MonoBehaviour
    {
        [SerializeField] SpriteRenderer _renderer;
        [SerializeField] Sprite[] _frames;
        [SerializeField] float _framesPerSecond = 10f;

        public void RenderAtTick(int tick)
        {
            if (!enabled || _renderer == null || _frames == null || _frames.Length == 0) return;
            int index = SpriteAnimationPlayback.FrameAt(tick, _framesPerSecond, _frames.Length);
            if (_frames[index] != null) _renderer.sprite = _frames[index];
        }
    }
}

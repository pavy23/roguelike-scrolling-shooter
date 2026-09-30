using System;
using Shmup.Core.Simulation;

namespace Shmup.Presentation.Battle
{
    /// <summary>Presentation-only sampling; observing a battle tick never advances the simulation.</summary>
    public static class SpriteAnimationPlayback
    {
        public static int FrameAt(int tick, float framesPerSecond, int frameCount, int phase = 0)
        {
            if (frameCount <= 0) return -1;
            if (framesPerSecond <= 0f || float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond))
                return 0;
            double frame = Math.Floor(Math.Max(0, tick) * (double)framesPerSecond / SimSpace.TicksPerSecond);
            int index = (int)(frame % frameCount);
            return (int)(((long)index + phase % frameCount + frameCount) % frameCount);
        }

        public static int IdleFrameAt(string clipId, int tick, float framesPerSecond,
            int frameCount, int phase, bool reducedFlash)
        {
            if (frameCount <= 0) return -1;
            // The adopted clips have decorative luminance peaks, not attack state cues.
            // Preserve a readable silhouette when flashes are reduced; movement still comes from Core.
            if (reducedFlash)
            {
                switch (clipId)
                {
                    case "void_moth": return Math.Min(1, frameCount - 1);
                    case "echo_wisp":
                    case "phase_disc":
                    case "rift_blade":
                    case "mini_crystal":
                    case "zako_sine":
                    case "interceptor": return 0;
                }
            }

            // Frames 03/04 of the current five-frame echo clip dissolve a living enemy.
            // Ping-pong 00/01/02/01 keeps its body present and closes the loop without that snap.
            if (clipId == "echo_wisp" && frameCount == 5)
            {
                int step = FrameAt(tick, framesPerSecond, 4, phase);
                return step == 3 ? 1 : step;
            }
            return FrameAt(tick, framesPerSecond, frameCount, phase);
        }
    }
}

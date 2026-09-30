using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>Visual emphasis only. Collision widths, timing and simulation stay in Core.</summary>
    public static class CombatReadability
    {
        public const int FriendlyBulletOrder = 5;
        public const int FriendlyLaserOrder = 12;
        public const int ExplosionOrder = 20;
        public const int HostileLaserOrder = 21;
        public const int PlayerOrder = 26;
        public const int HostileBulletOrder = 30;

        public static Color ExplosionColor(Color tint, float progress, bool reduced)
        {
            float fade = 1f - Mathf.InverseLerp(.45f, 1f, progress);
            tint.a *= (reduced ? .4f : .8f) * fade;
            return tint;
        }

        public static Color ScreenFlash(float damageAge, float bombAge, bool reduced)
        {
            // Damage takes priority; a bomb must not postpone the player's damage feedback.
            if (damageAge < .3f)
                return new Color(1f, .2f, .2f, Mathf.Clamp01(1f - damageAge / .3f) * (reduced ? .06f : .2f));
            return new Color(1f, .55f, 1f, Mathf.Clamp01(1f - bombAge / .5f) * (reduced ? .035f : .14f));
        }
    }
}

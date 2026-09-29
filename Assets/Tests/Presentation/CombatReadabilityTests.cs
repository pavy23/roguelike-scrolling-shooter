using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Shmup.Presentation.Battle;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class CombatReadabilityTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(false, .2f, .14f)]
        [TestCase(true, .06f, .035f)]
        public void OverlappingScreenFlashPrioritizesDamageAndIsBounded(bool reduced, float damageMax, float bombMax)
        {
            var overlap = CombatReadability.ScreenFlash(0, 0, reduced);
            Assert.AreEqual(new Color(1f, .2f, .2f, damageMax), overlap);
            Assert.AreEqual(bombMax, CombatReadability.ScreenFlash(1, 0, reduced).a);
            Assert.AreEqual(0, CombatReadability.ScreenFlash(1, 1, reduced).a);
            Assert.AreEqual(0, CombatReadability.ScreenFlash(float.MaxValue, float.MaxValue, reduced).a);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AnimatedExplosionFadesWithoutChangingAuthoredHue(bool reduced)
        {
            var tint = new Color(.3f, .6f, .9f, .7f);
            var first = CombatReadability.ExplosionColor(tint, 0, reduced);
            var tail = CombatReadability.ExplosionColor(tint, .8f, reduced);
            Assert.AreEqual(tint.r, tail.r);
            Assert.AreEqual(tint.g, tail.g);
            Assert.AreEqual(tint.b, tail.b);
            Assert.Less(tail.a, first.a);
            Assert.AreEqual(0, CombatReadability.ExplosionColor(tint, 1f, reduced).a);
        }

        [Test]
        public void ExplosionPoolReuseRestoresTintAndReflectsReducedFlashImmediately()
        {
            var root = new GameObject("FX fixture");
            var prefab = new GameObject("FX prefab", typeof(SpriteRenderer));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 16);
            try
            {
                var director = root.AddComponent<BattleDirector>();
                var juice = root.AddComponent<JuiceDirector>();
                Set(director, "_juice", juice);
                Set(director, "_explosionFrames", new[] { sprite, sprite });
                var pool = new SpritePool(prefab, root.transform, 1);
                Set(director, "_fxPool", pool);
                var spawn = typeof(BattleDirector).GetMethod("SpawnExplosion", Private, null,
                    new[] { typeof(Vector3), typeof(float), typeof(Color) }, null);
                spawn.Invoke(director, new object[] { Vector3.zero, 1f, Color.red });
                var view = root.transform.GetChild(0).GetComponent<SpriteRenderer>();
                Assert.AreEqual(.8f, view.color.a, .001f);
                Set(juice, "_flashReduced", true);
                // EditMode deltaTime includes runner delays; inspect the first animation frame.
                var ages = (List<float>)typeof(BattleDirector).GetField("_activeFxAges", Private).GetValue(director);
                ages[0] = -Time.deltaTime;
                Invoke(director, "AnimateExplosions");
                Assert.LessOrEqual(view.color.a, .4f);
                ages[0] = 20;
                Invoke(director, "AnimateExplosions");
                Assert.AreEqual(1, pool.FreeCount);
                Set(juice, "_flashReduced", false);
                spawn.Invoke(director, new object[] { Vector3.zero, 1f, Color.cyan });
                Assert.AreEqual(new Color(0, 1, 1, .8f), view.color);
                Assert.AreEqual(CombatReadability.ExplosionOrder, view.sortingOrder);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(prefab); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void PlayerAndShieldStayAboveExplosionsButBelowHostileProjectiles()
        {
            var root = new GameObject("Player fixture");
            try
            {
                var director = root.AddComponent<BattleDirector>();
                var ship = new GameObject("Ship", typeof(SpriteRenderer));
                ship.transform.SetParent(root.transform);
                var shield = new GameObject("Shield", typeof(SpriteRenderer));
                shield.transform.SetParent(ship.transform);
                Set(director, "_playerTransform", ship.transform);
                Set(director, "_shieldView", shield.GetComponent<SpriteRenderer>());
                Invoke(director, "ApplyPlayerReadability");
                int order = ship.GetComponent<SpriteRenderer>().sortingOrder;
                Assert.Greater(order, CombatReadability.ExplosionOrder);
                Assert.Less(order, CombatReadability.HostileBulletOrder);
                Assert.AreEqual(order - 1, shield.GetComponent<SpriteRenderer>().sortingOrder);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    }
}

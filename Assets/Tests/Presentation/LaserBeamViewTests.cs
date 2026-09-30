using System.Reflection;
using NUnit.Framework;
using Shmup.Core.Simulation;
using Shmup.Presentation.Battle;
using UnityEngine;

namespace Shmup.Presentation.Tests
{
    public sealed class LaserBeamViewTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Unit = SimSpace.SubUnitsPerWorldUnit;
        GameObject _root;
        LaserBeamView _view;
        Texture2D _texture;
        Sprite _sprite;
        JuiceDirector _juice;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Laser view test");
            _view = _root.AddComponent<LaserBeamView>();
            _view.enabled = false;
            _texture = new Texture2D(2, 2);
            _sprite = Sprite.Create(_texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 16);
            typeof(LaserBeamView).GetField("_pixelSprite", PrivateInstance).SetValue(_view, _sprite);
            typeof(LaserBeamView).GetField("_capacity", PrivateInstance).SetValue(_view, 1);
            typeof(LaserBeamView).GetMethod("Start", PrivateInstance).Invoke(_view, null);
            var director = _root.AddComponent<BattleDirector>();
            _juice = _root.AddComponent<JuiceDirector>();
            typeof(BattleDirector).GetField("_juice", PrivateInstance).SetValue(director, _juice);
            typeof(LaserBeamView).GetField("_director", PrivateInstance).SetValue(_view, director);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_sprite);
            Object.DestroyImmediate(_texture);
        }

        [TestCase(LaserSourceKind.Terrain)]
        [TestCase(LaserSourceKind.Enemy)]
        [TestCase(LaserSourceKind.Boss)]
        [TestCase(LaserSourceKind.BossPart)]
        public void FirstWarningCoversTheAuthoredFullWidth(LaserSourceKind source)
        {
            Draw(Beam(1, source, LaserPhase.Telegraph, Unit / 8, Unit * 5));
            Assert.That(Thickness("Band"), Is.EqualTo(10f).Within(0.0001f));
        }

        [TestCase(0)]
        [TestCase(Unit * 4)]
        public void FirstDamagingFrameShowsTheEntireSegment(int endY)
        {
            LaserState beam = Beam(1, LaserSourceKind.Enemy, LaserPhase.Firing, Unit / 8, Unit / 2,
                endY: endY);
            Draw(beam);
            Vector3 start = new Vector3(beam.StartX, beam.StartY) / SimSpace.SubUnitsPerWorldUnit;
            Vector3 end = new Vector3(beam.EndX, beam.EndY) / SimSpace.SubUnitsPerWorldUnit;
            foreach (string layer in new[] { "Band", "Core" })
            {
                SpriteRenderer renderer = Renderer(layer);
                Assert.IsTrue(renderer.enabled);
                Assert.Greater(renderer.color.a, 0f);
                float length = renderer.sprite.bounds.size.x * renderer.transform.localScale.x;
                Vector3 extent = renderer.transform.right * (length * 0.5f);
                Assert.That(Vector3.Distance(renderer.transform.position - extent, start), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(renderer.transform.position + extent, end), Is.LessThan(0.0001f));
            }
            Assert.That(Vector3.Distance(Renderer("Impact").transform.position, end), Is.LessThan(0.0001f));
        }

        [Test]
        public void ReusedSlotAndSourceUseTheNewAttacksWarningWidth()
        {
            Draw(Beam(1, LaserSourceKind.BossPart, LaserPhase.Sustaining, Unit / 2, Unit / 2));
            Draw(Beam(2, LaserSourceKind.BossPart, LaserPhase.Telegraph, Unit / 8, Unit * 5));
            Assert.That(Thickness("Band"), Is.EqualTo(10f).Within(0.0001f));
            Draw(Beam(3, LaserSourceKind.BossPart, LaserPhase.Telegraph, Unit / 8, Unit / 4));
            Assert.That(Thickness("Band"), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ReducedFlashWarningStaysSteadyAndKeepsItsFullWidth()
        {
            typeof(JuiceDirector).GetField("_flashReduced", PrivateInstance).SetValue(_juice, true);
            var beam = Beam(1, LaserSourceKind.Boss, LaserPhase.Telegraph, Unit / 8, Unit * 5);
            Draw(beam);
            var first = Renderer("Band").color;
            for (int i = 0; i < 20; i++) Draw(beam);
            Assert.AreEqual(first, Renderer("Band").color);
            Assert.Greater(first.a, .4f);
            Assert.That(Thickness("Band"), Is.EqualTo(10f).Within(.0001f));
            Assert.Greater(Renderer("Core").color.a, .5f);
        }

        [TestCase(LaserPhase.Firing)]
        [TestCase(LaserPhase.Sustaining)]
        public void ReducedFlashPreservesDangerBodyAndAttenuatesOnlyDecoration(LaserPhase phase)
        {
            var beam = Beam(1, LaserSourceKind.Enemy, phase, Unit / 8, Unit);
            Draw(beam);
            float width = Thickness("Band");
            float alpha = Renderer("Core").color.a;
            float outer = Renderer("Outer").color.a;
            typeof(JuiceDirector).GetField("_flashReduced", PrivateInstance).SetValue(_juice, true);
            Draw(beam);
            Assert.AreEqual(width, Thickness("Band"));
            Assert.AreEqual(alpha, Renderer("Core").color.a);
            Assert.Less(Renderer("Outer").color.a, outer);
            Assert.LessOrEqual(Renderer("Impact").color.a, .35f);
        }

        [Test]
        public void PooledLaserOrderingResetsBetweenHostileAndPlayerSources()
        {
            Draw(Beam(1, LaserSourceKind.Enemy, LaserPhase.Sustaining, Unit, Unit));
            Assert.Greater(Renderer("Band").sortingOrder, CombatReadability.ExplosionOrder);
            Draw(Beam(2, LaserSourceKind.Player, LaserPhase.Sustaining, Unit, Unit));
            Assert.Less(Renderer("Core").sortingOrder, CombatReadability.ExplosionOrder);
            Draw(Beam(3, LaserSourceKind.Boss, LaserPhase.Telegraph, Unit, Unit));
            Assert.Greater(Renderer("Core").sortingOrder, CombatReadability.ExplosionOrder);
        }

        [Test]
        public void PlayerBeamDisplaysItsCurrentGrowingWidth()
        {
            Draw(Beam(1, LaserSourceKind.Player, LaserPhase.Sustaining, Unit / 8, 0));
            Assert.That(Thickness("Band"), Is.EqualTo(0.25f).Within(0.0001f));
            Draw(Beam(1, LaserSourceKind.Player, LaserPhase.Sustaining, Unit / 4, 0));
            Assert.That(Thickness("Band"), Is.EqualTo(0.5f).Within(0.0001f));
        }

        void Draw(LaserState beam)
            => typeof(LaserBeamView).GetMethod("Draw", PrivateInstance)
                .Invoke(_view, new object[] { 0, beam, 1f / 60f });

        SpriteRenderer Renderer(string layer)
            => _root.transform.Find("Laser_00_" + layer).GetComponent<SpriteRenderer>();

        float Thickness(string layer)
        {
            SpriteRenderer renderer = Renderer(layer);
            return renderer.sprite.bounds.size.y * renderer.transform.localScale.y;
        }

        static LaserState Beam(int id, LaserSourceKind source, LaserPhase phase,
            int halfWidth, int fullHalfWidth, int endY = 0)
            => new LaserState(id, source, 42, Unit * 8, 0, -Unit * 8, endY, phase,
                phase == LaserPhase.Sustaining ? LaserThicknessStage.Full : LaserThicknessStage.Thin,
                halfWidth, 60, 1, fullHalfWidth);
    }
}

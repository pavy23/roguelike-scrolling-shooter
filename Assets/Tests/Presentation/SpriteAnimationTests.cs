using System;
using System.Reflection;
using NUnit.Framework;
using Shmup.Core;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;
using Shmup.Presentation.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class SpriteAnimationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root;
        BattleDirector _director;
        JuiceDirector _juice;
        SpriteRenderer _renderer;
        PlayerShipAnimator _player;
        Texture2D _texture;
        Sprite[] _frames;
        BattleSim _sim;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Sprite animation fixture");
            _root.SetActive(false);
            _director = _root.AddComponent<BattleDirector>();
            _juice = _root.AddComponent<JuiceDirector>();
            var ship = new GameObject("Ship", typeof(SpriteRenderer), typeof(PlayerShipAnimator));
            ship.transform.SetParent(_root.transform);
            _renderer = ship.GetComponent<SpriteRenderer>();
            _player = ship.GetComponent<PlayerShipAnimator>();
            _texture = new Texture2D(10, 2);
            _frames = new Sprite[5];
            for (int i = 0; i < _frames.Length; i++)
                _frames[i] = Sprite.Create(_texture, new Rect(i * 2, 0, 2, 2), Vector2.one * .5f, 16);
            Set(_player, "_renderer", _renderer);
            Set(_player, "_frames", _frames);
            Set(_director, "_playerTransform", ship.transform);
            Set(_director, "_juice", _juice);
            SetSim(NewSim());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            foreach (var sprite in _frames) Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(_texture);
        }

        [Test]
        public void PlayerEngineFollowsBattleProgressAndRestartsWithNewBattle()
        {
            Invoke(_director, "SyncPlayerAnimation");
            Assert.AreSame(_frames[0], _renderer.sprite);
            AdvanceTo(6);
            Invoke(_director, "SyncPlayerAnimation");
            Assert.AreSame(_frames[1], _renderer.sprite);
            for (int i = 0; i < 20; i++) Invoke(_director, "SyncPlayerAnimation");
            Assert.AreSame(_frames[1], _renderer.sprite, "Rendering a paused tick must not advance the engine.");
            Assert.AreEqual(6, _sim.Tick, "Rendering must never step Core.");
            SetSim(NewSim());
            Invoke(_director, "SyncPlayerAnimation");
            Assert.AreSame(_frames[0], _renderer.sprite);
        }

        [Test]
        public void ShipSelectionDoesNotOverwriteOtherShipsAndDoesNotDependOnArrayOrder()
        {
            Set(_director, "_shipSpriteIds", new[] { "interceptor", "starter", "bulwark" });
            Set(_director, "_shipSprites", new[] { _frames[3], _frames[0], _frames[4] });
            AdvanceTo(6);
            foreach (string id in new[] { "interceptor", "bulwark", "starter" })
            {
                Invoke(_director, "ApplyShipSprite", id);
                Invoke(_director, "SyncPlayerAnimation");
                Assert.AreSame(_frames[id == "starter" ? 1 : id == "interceptor" ? 3 : 4], _renderer.sprite);
                Assert.AreEqual(id == "starter", _player.enabled);
            }
        }

        [Test]
        public void MissingEngineFrameDoesNotEraseCurrentShip()
        {
            Set(_player, "_frames", new Sprite[] { null });
            _renderer.sprite = _frames[2];
            _player.RenderAtTick(100);
            Assert.AreSame(_frames[2], _renderer.sprite);
        }

        [TestCase("echo_wisp", 0)]
        [TestCase("void_moth", 1)]
        [TestCase("phase_disc", 0)]
        [TestCase("rift_blade", 0)]
        [TestCase("mini_crystal", 0)]
        [TestCase("zako_sine", 0)]
        [TestCase("interceptor", 0)]
        public void ReducedFlashHoldsReadableFrameAcrossWholeLoopAndVariant(string clip, int frame)
        {
            Clip(clip);
            Set(_juice, "_flashReduced", true);
            var position = _renderer.transform.localPosition = new Vector3(2, 3, 0);
            var scale = _renderer.transform.localScale = Vector3.one * 1.5f;
            _renderer.color = Color.cyan;
            for (int tick = 0; tick < 90; tick++)
            {
                AdvanceTo(tick);
                Draw(clip + "_elite", 11);
                Assert.AreSame(_frames[frame], _renderer.sprite);
            }
            Assert.AreEqual(position, _renderer.transform.localPosition);
            Assert.AreEqual(scale, _renderer.transform.localScale);
            Assert.AreEqual(Color.cyan, _renderer.color, "Idle sampling must not clear hit feedback.");
        }

        [Test]
        public void EchoLoopKeepsBodyVisibleAndReturnsThroughIntermediatePose()
        {
            Clip("echo_wisp");
            int[] ticks = { 0, 8, 15, 23, 30, 38, 45, 53, 60 };
            int[] frames = { 0, 1, 2, 1, 0, 1, 2, 1, 0 };
            for (int i = 0; i < ticks.Length; i++)
            {
                AdvanceTo(ticks[i]);
                Draw("echo_wisp");
                Assert.AreSame(_frames[frames[i]], _renderer.sprite);
            }
        }

        [Test]
        public void ReducedFlashToggleAndReusedViewRestoreCurrentClipAndPhase()
        {
            Clip("phase_disc");
            AdvanceTo(15);
            Draw("phase_disc");
            Assert.AreSame(_frames[2], _renderer.sprite);
            Set(_juice, "_flashReduced", true);
            Draw("phase_disc");
            Assert.AreSame(_frames[0], _renderer.sprite);
            Set(_juice, "_flashReduced", false);
            Draw("phase_disc");
            Assert.AreSame(_frames[2], _renderer.sprite);
            // Same renderer reused with another id phase: no held-frame state leaks.
            Draw("phase_disc", 2);
            Assert.AreSame(_frames[4], _renderer.sprite);
            Draw("phase_disc");
            Assert.AreSame(_frames[2], _renderer.sprite);
        }

        [Test]
        public void OtherClipsKeepMotionInReducedModeAndBossSecondFormDoesNotInheritFirstForm()
        {
            Clip("boss_stage1");
            Set(_juice, "_flashReduced", true);
            AdvanceTo(15);
            Draw("boss_stage1", exact: true);
            Assert.AreSame(_frames[2], _renderer.sprite);
            _renderer.sprite = _frames[4]; // replacement form's static art
            Draw("boss_stage1_core", exact: true);
            Assert.AreSame(_frames[4], _renderer.sprite);
        }

        [Test]
        public void MoreSpecificClipKeepsItsOwnPlaybackPolicy()
        {
            Set(_director, "_animPrefixes", new[] { "echo_wisp", "echo_wisp_armored" });
            Set(_director, "_animFrameCounts", new[] { 0, 5 });
            Set(_director, "_animFrames", _frames);
            AdvanceTo(30);
            Draw("echo_wisp_armored");
            Assert.AreSame(_frames[4], _renderer.sprite, "A separately authored clip must not inherit the five-frame echo repair.");
        }

        [Test]
        public void EngineSamplingRemainsValidAtLongRunningTickAndInvalidRate()
        {
            _player.RenderAtTick(int.MaxValue);
            CollectionAssert.Contains(_frames, _renderer.sprite);
            Set(_player, "_framesPerSecond", 0f);
            _player.RenderAtTick(int.MaxValue);
            Assert.AreSame(_frames[0], _renderer.sprite);
        }

        [Test]
        public void AdoptedPulseFramesAndPlayerClipRemainWiredWithConsistentGeometry()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity", OpenSceneMode.Additive);
            try
            {
                BattleDirector director = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    director = root.GetComponentInChildren<BattleDirector>(true);
                    if (director != null) break;
                }
                Assert.IsNotNull(director);
                var serialized = new SerializedObject(director);
                var ids = serialized.FindProperty("_animPrefixes");
                var counts = serialized.FindProperty("_animFrameCounts");
                var frames = serialized.FindProperty("_animFrames");
                int offset = 0, checkedClips = 0;
                for (int i = 0; i < ids.arraySize; i++)
                {
                    string id = ids.GetArrayElementAtIndex(i).stringValue;
                    int count = counts.GetArrayElementAtIndex(i).intValue;
                    if (id == "echo_wisp" || id == "void_moth")
                    {
                        Assert.AreEqual(5, count, id + " changed; re-review the frame policy.");
                        for (int f = 0; f < count; f++)
                        {
                            var sprite = frames.GetArrayElementAtIndex(offset + f).objectReferenceValue as Sprite;
                            Assert.IsNotNull(sprite);
                            Assert.AreEqual(new Vector2(24, 24), sprite.rect.size);
                            Assert.AreEqual(new Vector2(12, 12), sprite.pivot);
                            Assert.AreEqual(16, sprite.pixelsPerUnit);
                        }
                        checkedClips++;
                    }
                    offset += count;
                }
                Assert.AreEqual(2, checkedClips);
                var transform = serialized.FindProperty("_playerTransform").objectReferenceValue as Transform;
                Assert.IsNotNull(transform.GetComponent<PlayerShipAnimator>());
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        void Clip(string id)
        {
            Set(_director, "_animPrefixes", new[] { id });
            Set(_director, "_animFrameCounts", new[] { 5 });
            Set(_director, "_animFrames", _frames);
        }
        void SetSim(BattleSim sim) { _sim = sim; Set(_director, "_sim", sim); }
        void AdvanceTo(int tick) { while (_sim.Tick < tick) _sim.Step(InputCommand.None); }
        void Draw(string id, int phase = 0, bool exact = false)
            => Invoke(_director, "ApplyIdleAnimation", _renderer, id, phase, exact);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        static BattleSim NewSim()
        {
            var weapon = new WeaponDefinition("shot", 1, 1, 1, 1, 0, 0);
            var content = new BattleContent(Array.Empty<EnemyDefinition>(), new[] { weapon }, weapon.Id);
            var plan = new StagePlan(new[] { new StageSegment("animation", 1000,
                Array.Empty<SpawnEvent>(), 1, 1, new[] { 1 }) }, "none", 1, 1, 1);
            return new BattleSim(BattleSimConfig.CreateDefault(), new Rng(1UL), plan, content, PowerUpGauge.CreateDefault());
        }
    }
}

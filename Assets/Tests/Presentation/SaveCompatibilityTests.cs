using System.IO;
using System.Reflection;
using NUnit.Framework;
using Shmup.Core;
using Shmup.Core.Content;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;
using Shmup.Presentation.Battle;
using UnityEngine;

namespace Shmup.Presentation.Tests
{
    public sealed class SaveCompatibilityTests
    {
        static RunSuspendData CreateRunSave()
        {
            string Read(string name) => File.ReadAllText(
                Path.Combine(Application.dataPath, "../GameData", name + ".json"));
            var data = GameDataParser.Parse(Read("enemies"), Read("weapons"),
                Read("waves"), Read("rewards"), Read("ships"), Read("scoring"));
            return new RunManager(12345UL, new SegmentStageGenerator(data.StageGeneration),
                data.CreateBattleSimConfig(), data.BattleContent,
                data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip)
                .ExportSuspendData();
        }

        static InputRecordingData CreateRecording()
        {
            var recorder = new InputRecorder(4);
            InputCommand none = InputCommand.None;
            recorder.Record(in none);
            return recorder.Export();
        }

        // Test the SafeFile candidate predicates without touching player files.
        static bool AcceptsRun(RunSuspendData data)
        {
            return (bool)typeof(RunSave).GetMethod("IsUsable",
                BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { JsonUtility.ToJson(data) });
        }

        static bool AcceptsReplay(InputRecordingData data)
        {
            return (bool)typeof(ReplaySave).GetMethod("IsUsable",
                BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { JsonUtility.ToJson(new ReplayFileData { recording = data }) });
        }

        [Test]
        public void CurrentChecksummedRunRemainsAvailable()
        {
            var data = CreateRunSave();
            Assert.DoesNotThrow(() => SaveDataIntegrity.MigrateAndValidate(
                JsonUtility.FromJson<RunSuspendData>(JsonUtility.ToJson(data))));
            Assert.IsTrue(AcceptsRun(data));
            Assert.IsTrue(SaveDataIntegrity.HasValidChecksum(data));
        }

        [Test]
        public void CurrentChecksummedRecordingRemainsAvailable()
        {
            var data = CreateRecording();
            Assert.DoesNotThrow(() => SaveDataIntegrity.MigrateAndValidate(
                JsonUtility.FromJson<InputRecordingData>(JsonUtility.ToJson(data))));
            Assert.IsTrue(AcceptsReplay(data));
            Assert.IsTrue(SaveDataIntegrity.HasValidChecksum(data));
        }

        [TestCase(27)]
        [TestCase(28)]
        [TestCase(30)]
        public void IncompatibleRunIsNotOfferedForResume(int version)
        {
            Assert.IsFalse(AcceptsRun(new RunSuspendData { schemaVersion = version }));
        }

        [TestCase(24)]
        [TestCase(25)]
        [TestCase(27)]
        public void IncompatibleRecordingIsNotOfferedForReplay(int version)
        {
            Assert.IsFalse(AcceptsReplay(new InputRecordingData { schemaVersion = version }));
        }

        [Test]
        public void CorruptCurrentRunIsRejectedBeforeResume()
        {
            var data = CreateRunSave();
            data.score++;
            Assert.IsFalse(AcceptsRun(data));
        }

        [Test]
        public void CorruptCurrentRecordingIsRejectedBeforeReplay()
        {
            var data = CreateRecording();
            data.totalTicks++;
            Assert.IsFalse(AcceptsReplay(data));
        }
    }
}

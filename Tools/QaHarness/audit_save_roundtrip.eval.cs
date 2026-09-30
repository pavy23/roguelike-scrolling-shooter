// In-memory serializer audit. Never reads or writes player save files.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Save audit requires a disposable batch Editor.");
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
var run = new Shmup.Core.Simulation.RunManager(12345UL,
    new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
    data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
var recording = new Shmup.Core.Simulation.InputRecorder(4);
var input = Shmup.Core.Simulation.InputCommand.None;
recording.Record(in input);
var report = new System.Collections.Generic.List<object>();
foreach (var original in new object[] { run.ExportSuspendData(), recording.Export() })
{
    string json = UnityEngine.JsonUtility.ToJson(original);
    object restored = UnityEngine.JsonUtility.FromJson(json, original.GetType());
    var before = Newtonsoft.Json.Linq.JObject.FromObject(original);
    var after = Newtonsoft.Json.Linq.JObject.FromObject(restored);
    var changes = new System.Collections.Generic.List<object>();
    foreach (var property in before.Properties())
        if (!Newtonsoft.Json.Linq.JToken.DeepEquals(property.Value, after[property.Name]))
            changes.Add(new { field = property.Name, original = property.Value, restored = after[property.Name] });
    report.Add(new { type = original.GetType().Name, changes });
}
string path = System.IO.Path.GetFullPath("out/revamp/save-roundtrip-audit.json");
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
return new { path, report };

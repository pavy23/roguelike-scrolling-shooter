// Read-only generation audit, not an autoplay or a claim of real combat clearability.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Stage audit requires a disposable batch Editor.");
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
var generator = new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration);
var difficultyCurve = Shmup.Core.Simulation.StageDifficultyCurve.CreateDefault();
var report = new System.Collections.Generic.List<object>();
foreach (ulong seed in new ulong[] { 1, 42, 12345, 99991 })
{
    var order = generator.GetThemeOrder(seed);
    for (int stage = 1; stage <= order.Count; stage++)
    foreach (var section in new[] { Shmup.Core.Generation.StageRouteSection.Default, Shmup.Core.Generation.StageRouteSection.Closing })
    foreach (var outcome in new[] { Shmup.Core.Generation.MidbossOutcomeKind.Default, Shmup.Core.Generation.MidbossOutcomeKind.CleanKill,
        Shmup.Core.Generation.MidbossOutcomeKind.Attrition, Shmup.Core.Generation.MidbossOutcomeKind.PartFocus })
    {
        if (section == Shmup.Core.Generation.StageRouteSection.Default && outcome != Shmup.Core.Generation.MidbossOutcomeKind.Default) continue;
        int difficulty = difficultyCurve.GetDifficulty(stage);
        var plan = generator.GenerateRouteForSection(seed, stage, difficulty, order[stage - 1],
            Shmup.Core.Generation.EncounterType.Normal, section, outcome);
        int length = 0, enemies = 0, obstacles = 0, lastSpawn = 0, largestGap = 0, consecutiveRepeats = 0;
        var sequence = new System.Collections.Generic.List<object>();
        var spawnTicks = new System.Collections.Generic.List<int>();
        string previousId = null;
        foreach (var segment in plan.Segments)
        {
            if (previousId == segment.SegmentId) consecutiveRepeats++;
            previousId = segment.SegmentId;
            foreach (var spawn in segment.Spawns) spawnTicks.Add(length + spawn.Tick);
            sequence.Add(new { id = segment.SegmentId, startTick = length, durationTicks = segment.LengthTicks,
                enemySpawns = segment.Spawns.Count, obstacles = segment.Obstacles.Count });
            length += segment.LengthTicks;
            enemies += segment.Spawns.Count;
            obstacles += segment.Obstacles.Count;
        }
        spawnTicks.Sort();
        foreach (int tick in spawnTicks) { largestGap = System.Math.Max(largestGap, tick - lastSpawn); lastSpawn = tick; }
        largestGap = System.Math.Max(largestGap, length - lastSpawn);
        report.Add(new { seed, stage, difficulty, theme = plan.ThemeId, section = section.ToString(), outcome = outcome.ToString(),
            durationTicks = length, enemySpawns = enemies, obstacles, largestSpawnGapTicks = largestGap,
            plan.SegmentReuseCount, consecutiveRepeats, lanePathClearable = Shmup.Core.Generation.StagePlanClearability.IsClearable(plan), sequence });
    }
}
string path = System.IO.Path.GetFullPath("out/revamp/stage-rhythm-audit.json");
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    difficultyCurve = "StageDifficultyCurve.CreateDefault", encounter = "Normal", note = "Direct generator probes use the listed seed; not RunManager's forked live-run seeds. Gaps are new-spawn gaps, not safe combat windows.", plans = report
}, Newtonsoft.Json.Formatting.Indented));
return new { path, plans = report.Count };

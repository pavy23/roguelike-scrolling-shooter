// Actual Battle UI, isolated data-backed choice fixtures. GPU batch only; no Play Mode or saves.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Choice capture requires a batch Editor outside Play Mode.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("A graphics device is required; omit -nographics.");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
UnityEngine.AudioListener.volume = 0f;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
int scale = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_SCALE") == "1" ? 1 : 2;
bool touch = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_TOUCH") == "1";
string version = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_VERSION") ?? "after";
Shmup.Presentation.Battle.UiPlatform.ForceTouch = touch;
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
var run = new Shmup.Core.Simulation.RunManager(12345UL,
    new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
    data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
var rewards = new System.Collections.Generic.List<Shmup.Core.Simulation.RewardOption>();
foreach (string id in new[] { "glass_cannon", "missile_family_piercing_lance", "ammo_mod", "option_formation_orbit" })
    foreach (var r in data.Rewards.All)
        if (r.Id == id) rewards.Add(new Shmup.Core.Simulation.RewardOption(r.Id, r.Type, r.Slot, r.Amount,
            r.ModifierId, r.MissileFamily, r.OptionFormation, r.PrimaryWeaponFamily, r.ModifierKey, r.Costs));
var contracts = new System.Collections.Generic.List<Shmup.Core.Simulation.ContractOption>();
foreach (string id in new[] { "spartan_protocol", "lockdown_zone", "high_stakes" })
    foreach (var c in data.Contracts.All)
        if (c.Id == id) contracts.Add(new Shmup.Core.Simulation.ContractOption(c, "fortress"));
if (rewards.Count != 4 || contracts.Count != 3) throw new System.InvalidOperationException("Missing QA fixture candidates.");
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
director.GetType().GetField("_run", flags).SetValue(director, run);
director.GetType().GetField("_sim", flags).SetValue(director, run.Battle);
run.GetType().GetField("_capsuleBalance", flags).SetValue(run, touch ? 2 : 8);
run.GetType().GetField("_rewardSelectionKind", flags).SetValue(run, Shmup.Core.Simulation.RewardSelectionKind.Main);
var rewardView = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.RewardScreen>();
var contractView = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.ContractScreen>();
rewardView.GetType().GetMethod("Start", flags).Invoke(rewardView, null);
contractView.GetType().GetMethod("Start", flags).Invoke(contractView, null);
var camera = UnityEngine.Camera.main;
var previousTarget = camera.targetTexture;
var previousActive = UnityEngine.RenderTexture.active;
var target = UnityEngine.RenderTexture.GetTemporary(640 * scale, 360 * scale, 24);
var paths = new System.Collections.Generic.List<string>();
try
{
    camera.targetTexture = target;
    foreach (string state in new[] { "reward", "reward-four", "reward-rerolled", "contract", "contract-final" })
    {
        bool isContract = state.StartsWith("contract");
        run.GetType().GetProperty("State").SetValue(run, isContract
            ? Shmup.Core.Simulation.RunState.AwaitingContract : Shmup.Core.Simulation.RunState.AwaitingReward);
        run.GetType().GetField("_rewardOptions", flags).SetValue(run, rewards.GetRange(0, state == "reward-four" ? 4 : 3));
        var shownContracts = contracts;
        if (state == "contract-final")
        {
            shownContracts = new System.Collections.Generic.List<Shmup.Core.Simulation.ContractOption>();
            foreach (var c in data.Contracts.All)
                if (c.DestinationKind != Shmup.Core.Simulation.ContractDestinationKind.NextStage)
                    shownContracts.Add(new Shmup.Core.Simulation.ContractOption(c, null));
        }
        run.GetType().GetField("_contractOptions", flags).SetValue(run, shownContracts);
        rewardView.GetType().GetField("_labelsBuilt", flags).SetValue(rewardView, false);
        contractView.GetType().GetField("_built", flags).SetValue(contractView, false);
        rewardView.GetType().GetMethod("Update", flags).Invoke(rewardView, null);
        if (state == "reward-rerolled" && !touch)
            rewardView.GetType().GetMethod("OnReroll", flags).Invoke(rewardView, null);
        contractView.GetType().GetMethod("Update", flags).Invoke(contractView, null);
        foreach (var canvas in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>())
        {
            canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null) scaler.scaleFactor = scale;
            canvas.scaleFactor = scale;
        }
        UnityEngine.Canvas.ForceUpdateCanvases();
        camera.Render();
        UnityEngine.RenderTexture.active = target;
        var image = new UnityEngine.Texture2D(640 * scale, 360 * scale, UnityEngine.TextureFormat.RGB24, false);
        try
        {
            image.ReadPixels(new UnityEngine.Rect(0, 0, 640 * scale, 360 * scale), 0, 0);
            image.Apply();
            string suffix = (touch ? "-touch" : "") + (scale == 1 ? "-640" : "");
            string path = System.IO.Path.GetFullPath("out/revamp/choice-" + state + "-" + version + suffix + ".png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(image));
            paths.Add(path);
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    return new { paths, kind = "Editor UI render with injected choice state; not a playthrough" };
}
finally
{
    camera.targetTexture = previousTarget;
    UnityEngine.RenderTexture.active = previousActive;
    UnityEngine.RenderTexture.ReleaseTemporary(target);
}

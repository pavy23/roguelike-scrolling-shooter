// Integrated UI fixtures in a disposable GPU batch Editor. No Play Mode or scene/save writes.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("UI capture requires a batch Editor outside Play Mode.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("A graphics device is required; omit -nographics.");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
string version = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_VERSION") ?? "after";
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
var paths = new System.Collections.Generic.List<string>();
bool hadGuide = UnityEngine.PlayerPrefs.HasKey("rss.onboarded");
int guide = UnityEngine.PlayerPrefs.GetInt("rss.onboarded");
bool oldTouch = Shmup.Presentation.Battle.UiPlatform.ForceTouch;
float oldVolume = UnityEngine.AudioListener.volume, oldTime = UnityEngine.Time.timeScale;
bool oldPause = UnityEngine.AudioListener.pause;
try
{
    UnityEngine.AudioListener.volume = 0f;
    UnityEngine.PlayerPrefs.SetInt("rss.onboarded", 0);
    foreach (string device in new[] { "touch-640", "touch-1280", "desktop-1300" })
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
        typeof(Shmup.Presentation.Battle.AudioSettingsPanel).GetMethod("ResetStatics",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
        Shmup.Presentation.Battle.UiPlatform.ForceTouch = device.StartsWith("touch");
        UnityEngine.Time.timeScale = 1f;
        UnityEngine.AudioListener.pause = false;
        int scale = device.EndsWith("640") ? 1 : 2;
        int width = device.EndsWith("1300") ? 1300 : 640 * scale;
        int height = device.EndsWith("1300") ? 760 : 360 * scale;
        var run = new Shmup.Core.Simulation.RunManager(12345UL,
            new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
            data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
        run.PowerUpGauge.Collect();
        var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
        director.GetType().GetField("_run", flags).SetValue(director, run);
        director.GetType().GetField("_sim", flags).SetValue(director, run.Battle);
        director.GetType().GetProperty("IsDailyRun").SetValue(director, true);
        ((UnityEngine.GameObject)director.GetType().GetField("_bossHpRoot", flags).GetValue(director)).SetActive(false);
        var backgrounds = (UnityEngine.GameObject[])director.GetType().GetField("_themeBackgrounds", flags).GetValue(director);
        for (int i = 0; i < backgrounds.Length; i++) backgrounds[i].SetActive(i == 0);
        foreach (var c in data.Contracts.All)
            if (c.Id == "spartan_protocol") run.GetType().GetProperty("ActiveContract").SetValue(run, c);
        run.GetType().GetField("_completedStageScore", flags).SetValue(run, 123456789L);
        // Exercise the widest multiplier label, including its maximum scale pulse.
        run.Battle.GetType().GetField("_multiplierLevel", flags).SetValue(run.Battle, 5);
        var rewards = new System.Collections.Generic.List<Shmup.Core.Simulation.RewardOption>();
        foreach (string id in new[] { "glass_cannon", "missile_family_piercing_lance", "ammo_mod", "option_formation_orbit" })
            foreach (var r in data.Rewards.All)
                if (r.Id == id) rewards.Add(new Shmup.Core.Simulation.RewardOption(r.Id, r.Type, r.Slot, r.Amount,
                    r.ModifierId, r.MissileFamily, r.OptionFormation, r.PrimaryWeaponFamily, r.ModifierKey, r.Costs));
        var contracts = new System.Collections.Generic.List<Shmup.Core.Simulation.ContractOption>();
        foreach (string id in new[] { "spartan_protocol", "lockdown_zone", "high_stakes" })
            foreach (var c in data.Contracts.All)
                if (c.Id == id) contracts.Add(new Shmup.Core.Simulation.ContractOption(c, "fortress"));
        run.GetType().GetField("_rewardOptions", flags).SetValue(run, rewards);
        run.GetType().GetField("_contractOptions", flags).SetValue(run, contracts);
        run.GetType().GetField("_capsuleBalance", flags).SetValue(run, 2);
        run.GetType().GetField("_rewardSelectionKind", flags).SetValue(run, Shmup.Core.Simulation.RewardSelectionKind.Main);
        var views = new System.Collections.Generic.List<UnityEngine.MonoBehaviour>();
        // Order emulates Awake-before-Start and includes controls omitted by isolated older captures.
        foreach (string name in new[] { "TouchControls", "PowerUpHudView", "ScoreHud", "ProgressHud", "OnboardingHints",
            "BombButton", "RewardScreen", "ContractScreen", "PauseScreen", "OptionsScreen" })
            foreach (var view in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>())
            {
                if (view.GetType().Name != name) continue;
                if (name == "TouchControls") view.GetType().GetMethod("Awake", flags).Invoke(view, null);
                view.GetType().GetMethod("Start", flags)?.Invoke(view, null);
                if (name == "OnboardingHints")
                {
                    view.GetType().GetField("_moved", flags).SetValue(view, true);
                    view.GetType().GetField("_collected", flags).SetValue(view, true);
                }
                if (name == "ProgressHud") view.GetType().GetField("_bannerBiome", flags).SetValue(view, director.BiomeIndex);
                views.Add(view);
            }
        var pause = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.PauseScreen>();
        var options = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.OptionsScreen>();
        var camera = UnityEngine.Camera.main;
        var previousTarget = camera.targetTexture;
        var previousActive = UnityEngine.RenderTexture.active;
        var target = UnityEngine.RenderTexture.GetTemporary(width, height, 24);
        try
        {
            camera.targetTexture = target;
            foreach (string state in new[] { "battle", "reward", "reward-paused", "contract", "contract-paused", "pause", "options", "audio" })
            {
                run.GetType().GetProperty("State").SetValue(run, state.StartsWith("reward")
                    ? Shmup.Core.Simulation.RunState.AwaitingReward : state.StartsWith("contract")
                    ? Shmup.Core.Simulation.RunState.AwaitingContract : Shmup.Core.Simulation.RunState.Playing);
                if (state != "options" && state != "audio") pause.GetType().GetMethod("SetPaused", flags)
                    .Invoke(pause, new object[] { state == "pause" || state.EndsWith("paused") });
                if (state == "options") options.GetType().GetMethod("SetOpen", flags).Invoke(options, new object[] { true });
                if (state == "audio")
                {
                    options.GetType().GetMethod("SetOpen", flags).Invoke(options, new object[] { false });
                    ((Shmup.Presentation.Battle.AudioSettingsPanel)pause.GetType().GetField("_audioSettings", flags).GetValue(pause)).Open();
                }
                foreach (var view in views) view.GetType().GetMethod("Update", flags)?.Invoke(view, null);
                foreach (var view in views) view.GetType().GetMethod("LateUpdate", flags)?.Invoke(view, null);
                var multiplier = UnityEngine.GameObject.Find("Multiplier");
                if (multiplier != null) multiplier.transform.localScale = UnityEngine.Vector3.one * 1.1f;
                // Includes runtime-created visibility components, after every UI Update.
                foreach (var view in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>())
                    if (view.GetType().Name == "BattleHudVisibility") view.GetType().GetMethod("LateUpdate", flags).Invoke(view, null);
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(UnityEngine.FindObjectsInactive.Include))
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
                var image = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGB24, false);
                try
                {
                    image.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    string path = System.IO.Path.GetFullPath("out/revamp/ui-layers-" + state + "-" + device + "-" + version + ".png");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(image));
                    paths.Add(path);
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
        }
        finally
        {
            camera.targetTexture = previousTarget;
            UnityEngine.RenderTexture.active = previousActive;
            UnityEngine.RenderTexture.ReleaseTemporary(target);
            if (Shmup.Presentation.Battle.UiPlatform.TouchMode) UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();
        }
    }
    return new { paths, kind = "Integrated Editor UI fixtures; not a browser playthrough" };
}
finally
{
    if (hadGuide) UnityEngine.PlayerPrefs.SetInt("rss.onboarded", guide);
    else UnityEngine.PlayerPrefs.DeleteKey("rss.onboarded");
    Shmup.Presentation.Battle.UiPlatform.ForceTouch = oldTouch;
    UnityEngine.Time.timeScale = oldTime;
    UnityEngine.AudioListener.pause = oldPause;
    UnityEngine.AudioListener.volume = oldVolume;
}

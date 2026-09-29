// Disposable GPU batch Editor: real Battle scene UI with an isolated, data-backed run.
// No Play Mode, gameplay traversal, scene save, or user save mutation.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Battle UI capture requires a batch Editor outside Play Mode.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("A graphics device is required; omit -nographics.");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
UnityEngine.AudioListener.volume = 0f;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
int scale = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_SCALE") == "1" ? 1 : 2;
bool touch = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_TOUCH") == "1";
string state = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_HUD") ?? "ready";
Shmup.Presentation.Battle.UiPlatform.ForceTouch = touch;
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
var run = new Shmup.Core.Simulation.RunManager(12345UL,
    new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
    data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
if (state != "empty") run.PowerUpGauge.Collect();
if (state == "locked") run.PowerUpGauge.SetContractActivationBans(true, false, false);
if (state == "empty") typeof(Shmup.Core.Simulation.BattleSim).GetProperty("ShieldStock").SetValue(run.Battle, 0);
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
if (director == null) throw new System.InvalidOperationException("BattleDirector is missing.");
director.GetType().GetField("_run", flags).SetValue(director, run);
director.GetType().GetField("_sim", flags).SetValue(director, run.Battle);
bool hadGuidePreference = UnityEngine.PlayerPrefs.HasKey("rss.onboarded");
int guidePreference = UnityEngine.PlayerPrefs.GetInt("rss.onboarded");
var camera = UnityEngine.Camera.main;
var previousTarget = camera.targetTexture;
var previousActive = UnityEngine.RenderTexture.active;
var target = UnityEngine.RenderTexture.GetTemporary(640 * scale, 360 * scale, 24);
UnityEngine.Texture2D image = null;
try
{
    UnityEngine.PlayerPrefs.SetInt("rss.onboarded", 0);
    // Invoke only UI lifecycles; the director's save/replay/bootstrap paths are not run.
    foreach (var view in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>())
    {
        string name = view.GetType().Name;
        if (name != "PowerUpHudView" && name != "ScoreHud" && name != "OnboardingHints"
            && name != "BombButton" && name != "ProgressHud") continue;
        view.GetType().GetMethod("Start", flags)?.Invoke(view, null);
        if (name == "OnboardingHints")
        {
            // Inject a guide display state without persisting completion.
            view.GetType().GetField("_age", flags)?.SetValue(view, 12f); // pre-revamp comparison
            view.GetType().GetField("_moved", flags)?.SetValue(view, true);
            view.GetType().GetField("_collected", flags)?.SetValue(view, true);
        }
        view.GetType().GetMethod("Update", flags)?.Invoke(view, null);
        view.GetType().GetMethod("LateUpdate", flags)?.Invoke(view, null);
    }
    camera.targetTexture = target;
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
    image = new UnityEngine.Texture2D(640 * scale, 360 * scale, UnityEngine.TextureFormat.RGB24, false);
    image.ReadPixels(new UnityEngine.Rect(0, 0, 640 * scale, 360 * scale), 0, 0);
    image.Apply();
    var png = UnityEngine.ImageConversion.EncodeToPNG(image);
    string suffix = state + (touch ? "-touch" : "") + (scale == 1 ? "-640" : "");
    string path = System.IO.Path.GetFullPath("out/revamp/battle-ui-" + suffix + ".png");
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
    System.IO.File.WriteAllBytes(path, png);
    return new { path, bytes = png.Length, kind = "Editor UI render with injected run/guide state; not a playthrough" };
}
finally
{
    if (hadGuidePreference) UnityEngine.PlayerPrefs.SetInt("rss.onboarded", guidePreference);
    else UnityEngine.PlayerPrefs.DeleteKey("rss.onboarded");
    if (image != null) UnityEngine.Object.DestroyImmediate(image);
    camera.targetTexture = previousTarget;
    UnityEngine.RenderTexture.active = previousActive;
    UnityEngine.RenderTexture.ReleaseTemporary(target);
}

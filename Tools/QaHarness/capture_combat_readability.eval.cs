// Actual Battle assets/components with an injected overlap fixture. No playthrough or save writes.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires disposable batch Editor outside Play Mode.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("GPU required; omit -nographics.");
string DetectPipeline()
{
    var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
    if (asset == null) return "BuiltIn";
    string name = asset.GetType().FullName;
    return name.Contains("Universal") ? "URP" : name.Contains("HighDefinition") ? "HDRP" : "Custom";
}
if (DetectPipeline() != "URP") throw new System.InvalidOperationException("Expected project URP pipeline.");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
string label = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_LABEL") ?? "after";
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
object Read(string name) => director.GetType().GetField(name, flags).GetValue(director);
void Set(string name, object value) => director.GetType().GetField(name, flags).SetValue(director, value);
void Call(string name) => director.GetType().GetMethod(name, flags).Invoke(director, null);
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
var run = new Shmup.Core.Simulation.RunManager(12345UL,
    new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
    data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
Set("_run", run);
Set("_sim", run.Battle);
var themes = (UnityEngine.GameObject[])Read("_themeBackgrounds");
for (int i = 0; i < themes.Length; i++) if (themes[i] != null) themes[i].SetActive(i == 0);
var bulletPool = new Shmup.Presentation.Battle.SpritePool((UnityEngine.GameObject)Read("_bulletPrefab"),
    (UnityEngine.Transform)Read("_bulletRoot"), 24, "Capture bullet");
Set("_bulletPool", bulletPool);
Set("_fxPool", new Shmup.Presentation.Battle.SpritePool((UnityEngine.GameObject)Read("_explosionPrefab"),
    (UnityEngine.Transform)Read("_fxRoot"), 16, "Capture explosion"));
var bullets = (System.Collections.Generic.List<Shmup.Core.Simulation.BulletState>)run.Battle.GetType().GetField("_bullets", flags).GetValue(run.Battle);
for (int i = 0; i < 12; i++)
    bullets.Add(new Shmup.Core.Simulation.BulletState(i + 100, Shmup.Core.Simulation.BulletFaction.Enemy,
        Shmup.Core.Simulation.BulletKind.EnemyShot, (-6 + i) * 256, ((i % 3) - 1) * 256));
Call("SyncBullets");
var laser = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.LaserBeamView>();
laser.GetType().GetMethod("Start", flags).Invoke(laser, null);
var juice = (Shmup.Presentation.Battle.JuiceDirector)Read("_juice");
var camera = UnityEngine.Camera.main;
var ppc = camera.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
if (ppc.assetsPPU != 16 || ppc.refResolutionX != 640 || ppc.refResolutionY != 360)
    throw new System.InvalidOperationException("Pixel reference must stay 640x360 / PPU 16.");
var target = UnityEngine.RenderTexture.GetTemporary(1280, 720, 24);
var previousTarget = camera.targetTexture;
var previousActive = UnityEngine.RenderTexture.active;
bool hadReduce = UnityEngine.PlayerPrefs.HasKey(Shmup.Presentation.Battle.JuiceDirector.FlashReducePrefKey);
int oldReduce = UnityEngine.PlayerPrefs.GetInt(Shmup.Presentation.Battle.JuiceDirector.FlashReducePrefKey);
float previousVolume = UnityEngine.AudioListener.volume;
var paths = new System.Collections.Generic.List<string>();
try
{
    UnityEngine.AudioListener.volume = 0;
    foreach (bool reduced in new[] { false, true })
    {
        UnityEngine.PlayerPrefs.SetInt(Shmup.Presentation.Battle.JuiceDirector.FlashReducePrefKey, reduced ? 1 : 0);
        juice.ReloadPrefs();
        // Refresh the same explosion objects for a comparable early frame.
        if (!reduced)
        {
            var spawn = director.GetType().GetMethod("SpawnExplosion", flags, null,
                new[] { typeof(UnityEngine.Vector3), typeof(float), typeof(UnityEngine.Color) }, null);
            foreach (float x in new[] { -8f, -4f, 0f, 4f })
                spawn.Invoke(director, new object[] { new UnityEngine.Vector3(x, 0, 0), 1.8f, UnityEngine.Color.white });
        }
        var ages = (System.Collections.Generic.List<float>)Read("_activeFxAges");
        for (int i = 0; i < ages.Count; i++) ages[i] = .06f;
        Call("AnimateExplosions");
        Set("_bombFlashAge", .05f);
        Set("_damageFlashAge", .04f);
        Call("AnimateDamageFlash");
        // Enemy telegraph crosses explosions; a second, active beam occupies the lower lane.
        for (int i = 0; i < 2; i++)
        {
            var beam = new Shmup.Core.Simulation.LaserState(700 + i, Shmup.Core.Simulation.LaserSourceKind.Enemy,
                99, 9 * 256, -i * 3 * 256, -9 * 256, -i * 3 * 256,
                i == 0 ? Shmup.Core.Simulation.LaserPhase.Telegraph : Shmup.Core.Simulation.LaserPhase.Sustaining,
                Shmup.Core.Simulation.LaserThicknessStage.Full, 96, 8, 1, 128);
            laser.GetType().GetMethod("Draw", flags).Invoke(laser, new object[] { i, beam, 1f / 60f });
        }
        // Runtime versions can provide the shared player ordering in this method.
        director.GetType().GetMethod("ApplyPlayerReadability", flags)?.Invoke(director, null);
        camera.targetTexture = target;
        camera.Render();
        UnityEngine.RenderTexture.active = target;
        var image = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGB24, false);
        image.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        string path = System.IO.Path.GetFullPath("out/revamp/combat-" + label + (reduced ? "-reduced" : "-normal") + ".png");
        System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(image));
        UnityEngine.Object.DestroyImmediate(image);
        paths.Add(path);
    }
    return new { paths, pipeline = DetectPipeline(), ppu = ppc.assetsPPU, width = ppc.refResolutionX,
        height = ppc.refResolutionY,
        kind = "Actual Battle assets with injected overlap state; GPU Editor render, not a playthrough" };
}
finally
{
    if (hadReduce) UnityEngine.PlayerPrefs.SetInt(Shmup.Presentation.Battle.JuiceDirector.FlashReducePrefKey, oldReduce);
    else UnityEngine.PlayerPrefs.DeleteKey(Shmup.Presentation.Battle.JuiceDirector.FlashReducePrefKey);
    juice.ReloadPrefs();
    UnityEngine.AudioListener.volume = previousVolume;
    camera.targetTexture = previousTarget;
    UnityEngine.RenderTexture.active = previousActive;
    UnityEngine.RenderTexture.ReleaseTemporary(target);
}

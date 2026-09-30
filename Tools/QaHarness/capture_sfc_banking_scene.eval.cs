// Real scene and Core movement, candidate sprites injected only into this disposable Editor.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires batch Editor outside Play Mode.");
var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
if (pipeline == null || !pipeline.GetType().FullName.Contains("Universal")
    || UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("URP GPU required.");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
string root = "ArtRevamp/SFC-20260930/banking";
string outputRoot = System.IO.Path.GetFullPath(root + "/review"); System.IO.Directory.CreateDirectory(outputRoot);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
object Read(string field) => director.GetType().GetField(field, flags).GetValue(director);
void Set(object target, string field, object value) => target.GetType().GetField(field, flags).SetValue(target, value);
void Call(string method, params object[] args) => director.GetType().GetMethod(method, flags).Invoke(director, args);
UnityEngine.Sprite Load(string relative, string name)
{
    var texture = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
    if (!UnityEngine.ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(root + "/" + relative)) || texture.width != 48 || texture.height != 30)
        throw new System.InvalidOperationException("Wrong native PNG " + relative);
    texture.filterMode = UnityEngine.FilterMode.Point; texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
    var sprite = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, 48, 30), UnityEngine.Vector2.one * .5f, 16, 0, UnityEngine.SpriteMeshType.FullRect);
    sprite.name = name; return sprite;
}
var up = new[] { Load("raw/masked-up-fold/candidate_00.png", "up-A"), Load("raw/bank-up-engine-warm/candidate_00.png", "up-B") };
var down = new[] { Load("raw/masked-down-fold/candidate_00.png", "down-A"), Load("raw/bank-down-engine-warm/candidate_00.png", "down-B") };
var player = (UnityEngine.Transform)Read("_playerTransform");
var renderer = player.GetComponent<UnityEngine.SpriteRenderer>();
var animator = player.GetComponent<Shmup.Presentation.Battle.PlayerShipAnimator>();
Set(animator, "_bankUpFrames", up); Set(animator, "_bankDownFrames", down);
Call("ApplyShipSprite", "starter");
var muzzle = (UnityEngine.SpriteRenderer)Read("_muzzleFlash");
var muzzlePosition = muzzle.transform.localPosition;
foreach (var canvas in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None)) canvas.gameObject.SetActive(false);
var themes = (UnityEngine.GameObject[])Read("_themeBackgrounds");
var camera = UnityEngine.Camera.main;
var ppc = camera.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
if (ppc.assetsPPU != 16 || ppc.refResolutionX != 640 || ppc.refResolutionY != 360)
    throw new System.InvalidOperationException("Pixel camera contract changed.");
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
void Capture(string file)
{
    var target = UnityEngine.RenderTexture.GetTemporary(1280, 720, 24);
    var previous = UnityEngine.RenderTexture.active; var previousTarget = camera.targetTexture;
    try
    {
        camera.targetTexture = target; camera.Render(); UnityEngine.RenderTexture.active = target;
        var image = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGB24, false);
        image.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outputRoot, file), UnityEngine.ImageConversion.EncodeToPNG(image));
        UnityEngine.Object.DestroyImmediate(image);
    }
    finally { camera.targetTexture = previousTarget; UnityEngine.RenderTexture.active = previous; UnityEngine.RenderTexture.ReleaseTemporary(target); }
}
var observations = new System.Collections.Generic.List<object>();
foreach (int theme in new[] { 0, 2, 4 })
{
    for (int i = 0; i < themes.Length; i++) if (themes[i] != null) themes[i].SetActive(i == theme);
    var run = new Shmup.Core.Simulation.RunManager(12345UL,
        new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
        data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
    Set(director, "_sim", run.Battle);
    for (int tick = 0; tick <= 24; tick++)
    {
        int direction = tick <= 8 ? 1 : tick <= 12 ? 0 : tick <= 20 ? -1 : 0;
        if (tick > 0) run.Battle.Step(new Shmup.Core.Simulation.InputCommand(0, direction, false));
        Call("ObservePlayerAnimation", run.Battle);
        player.localPosition = Shmup.Presentation.Battle.SimView.ToWorld(run.Battle.PlayerX, run.Battle.PlayerY);
        var beforePosition = player.localPosition; var beforeRotation = player.localRotation; var beforeColor = renderer.color;
        Call("SyncPlayerAnimation");
        if (player.localPosition != beforePosition || player.localRotation != beforeRotation || renderer.color != beforeColor || muzzle.transform.localPosition != muzzlePosition)
            throw new System.InvalidOperationException("Bank animation changed geometry or tint.");
        if (tick == 0 || tick == 4 || tick == 6 || tick == 9 || tick == 13 || tick == 16 || tick == 18 || tick == 21)
        {
            string file = "battle-candidate-theme-" + theme + "-tick-" + tick + ".png";
            muzzle.enabled = tick == 4 || tick == 16;
            Capture(file);
            var sampled = renderer.sprite;
            for (int pause = 0; pause < 6; pause++) Call("SyncPlayerAnimation");
            if (renderer.sprite != sampled) throw new System.InvalidOperationException("Paused pose changed.");
            observations.Add(new { theme, tick = run.Battle.Tick, inputY = direction, sprite = renderer.sprite.name,
                x = run.Battle.PlayerX, y = run.Battle.PlayerY, file, pausedPoseStable = true });
        }
    }
}
System.IO.File.WriteAllText(System.IO.Path.Combine(outputRoot, "scene-capture.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
    kind = "Disposable Editor candidate integration preview; not deployed or browser playtested",
    candidatesInjectedInMemoryOnly = true, sceneSaved = false, assetsImported = false,
    playerPrefsAccessed = false, userSavesAccessed = false, gameplayDataChanged = false,
    ppu = 16, referenceResolution = new[] { 640, 360 }, captureResolution = new[] { 1280, 720 },
    muzzleLocalPosition = new[] { muzzlePosition.x, muzzlePosition.y, muzzlePosition.z }, observations
}, Newtonsoft.Json.Formatting.Indented));
return new { outputRoot, captures = observations.Count, sceneSaved = false, assetsImported = false };

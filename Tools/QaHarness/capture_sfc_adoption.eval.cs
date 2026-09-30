// Real scene bindings and presentation methods, driven by an isolated in-memory fixture.
// No Play Mode, PlayerPrefs, save files, scene saves, or gameplay data mutations.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires a disposable batch Editor outside Play Mode.");
var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
if (pipeline == null || !pipeline.GetType().FullName.Contains("Universal"))
    throw new System.InvalidOperationException("Expected URP.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("GPU required.");
string label = System.Environment.GetEnvironmentVariable("RSS_SFC_CAPTURE") ?? "after";
if (label != "before" && label != "after") throw new System.InvalidOperationException("Unknown capture label.");
string outputRoot = System.IO.Path.GetFullPath("out/revamp/sfc-adoption");
System.IO.Directory.CreateDirectory(outputRoot);
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
object Read(string name) => director.GetType().GetField(name, flags).GetValue(director);
void Set(string name, object value) => director.GetType().GetField(name, flags).SetValue(director, value);
void Call(string name, params object[] args) => director.GetType().GetMethod(name, flags).Invoke(director, args);
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
var run = new Shmup.Core.Simulation.RunManager(12345UL,
    new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
    data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
Set("_run", run); Set("_sim", run.Battle); Call("CacheEnemyExtents", data);
Set("_enemyPool", new Shmup.Presentation.Battle.SpritePool((UnityEngine.GameObject)Read("_enemyPrefab"),
    (UnityEngine.Transform)Read("_enemyRoot"), 8, "SFC capture enemy"));
Set("_bulletPool", new Shmup.Presentation.Battle.SpritePool((UnityEngine.GameObject)Read("_bulletPrefab"),
    (UnityEngine.Transform)Read("_bulletRoot"), 32, "SFC capture bullet"));
var enemies = (System.Collections.Generic.List<Shmup.Core.Simulation.EnemyState>)run.Battle.GetType().GetField("_enemies", flags).GetValue(run.Battle);
enemies.Clear();
enemies.Add(new Shmup.Core.Simulation.EnemyState(101, "zako_straight", -2 * 256, 3 * 256, 12));
enemies.Add(new Shmup.Core.Simulation.EnemyState(102, "zako_fast", 6 * 256, 128, 8));
enemies.Add(new Shmup.Core.Simulation.EnemyState(103, "turret_ground", 10 * 256, -3 * 256, 140));
var bullets = (System.Collections.Generic.List<Shmup.Core.Simulation.BulletState>)run.Battle.GetType().GetField("_bullets", flags).GetValue(run.Battle);
bullets.Clear();
for (int i = 0; i < 12; i++)
    bullets.Add(new Shmup.Core.Simulation.BulletState(201 + i, Shmup.Core.Simulation.BulletFaction.Enemy,
        Shmup.Core.Simulation.BulletKind.EnemyShot, (i - 5) * 256, ((i % 3) - 1) * 256));
var player = (UnityEngine.Transform)Read("_playerTransform"); player.localPosition = new UnityEngine.Vector3(-11, 0, 0);
Call("ApplyShipSprite", "starter"); Call("SyncBullets");
var muzzle = (UnityEngine.SpriteRenderer)Read("_muzzleFlash");
var originalMuzzle = muzzle.transform.localPosition;
foreach (var canvas in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
    canvas.gameObject.SetActive(false);
var themes = (UnityEngine.GameObject[])Read("_themeBackgrounds");
var camera = UnityEngine.Camera.main;
var ppc = camera.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
if (ppc.assetsPPU != 16 || ppc.refResolutionX != 640 || ppc.refResolutionY != 360)
    throw new System.InvalidOperationException("Pixel camera contract changed.");
void Capture(string name)
{
    var target = UnityEngine.RenderTexture.GetTemporary(1280, 720, 24);
    var previous = UnityEngine.RenderTexture.active; var previousTarget = camera.targetTexture;
    try
    {
        camera.targetTexture = target; camera.Render(); UnityEngine.RenderTexture.active = target;
        var image = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGB24, false);
        image.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outputRoot, name + ".png"), UnityEngine.ImageConversion.EncodeToPNG(image));
        UnityEngine.Object.DestroyImmediate(image);
    }
    finally { camera.targetTexture = previousTarget; UnityEngine.RenderTexture.active = previous; UnityEngine.RenderTexture.ReleaseTemporary(target); }
}
var samples = new System.Collections.Generic.List<object>();
var imports = new System.Collections.Generic.List<object>();
if (label == "after")
{
    foreach (string name in new[] { "sfc_player_ship", "sfc_player_engine_01", "enemy_sfc_drone", "enemy_sfc_fast", "enemy_sfc_turret" })
    {
        string path = "Assets/Art/Sprites/" + name + ".png";
        var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(path);
        var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
        int width = name.StartsWith("sfc_player") ? 48 : 24;
        int height = name.StartsWith("sfc_player") ? 30 : 24;
        if (sprite == null || sprite.rect != new UnityEngine.Rect(0, 0, width, height)
            || sprite.pivot != new UnityEngine.Vector2(width / 2f, height / 2f) || sprite.pixelsPerUnit != 16
            || importer.filterMode != UnityEngine.FilterMode.Point || importer.mipmapEnabled
            || importer.textureCompression != UnityEditor.TextureImporterCompression.Uncompressed)
            throw new System.InvalidOperationException("SFC import contract changed: " + path);
        imports.Add(new { path, width, height, ppu = sprite.pixelsPerUnit,
            pivot = new[] { sprite.pivot.x, sprite.pivot.y }, filter = importer.filterMode.ToString(),
            mipmaps = importer.mipmapEnabled, compression = importer.textureCompression.ToString() });
    }
}
var views = (System.Collections.Generic.Dictionary<int, UnityEngine.Transform>)Read("_enemyViews");
foreach (int theme in new[] { 0, 2, 4 })
{
    for (int i = 0; i < themes.Length; i++) if (themes[i] != null) themes[i].SetActive(i == theme);
    foreach (int tick in new[] { 0, 6, 12 })
    {
        run.Battle.GetType().GetProperty("Tick").SetValue(run.Battle, tick);
        Call("SyncPlayerAnimation"); Call("SyncEnemies"); Call("ApplyPlayerReadability");
        muzzle.enabled = tick == 6;
        Capture("battle-" + label + "-theme-" + theme + "-tick-" + tick);
        var frames = new System.Collections.Generic.List<object>();
        foreach (var enemy in enemies)
        {
            var view = views[enemy.Id]; var renderer = view.GetComponent<UnityEngine.SpriteRenderer>();
            frames.Add(new { enemy.DefinitionId, sprite = UnityEditor.AssetDatabase.GetAssetPath(renderer.sprite),
                scaleX = view.localScale.x, scaleY = view.localScale.y });
        }
        samples.Add(new { theme, tick, player = UnityEditor.AssetDatabase.GetAssetPath(player.GetComponent<UnityEngine.SpriteRenderer>().sprite), enemies = frames });
    }
}
if (muzzle.transform.localPosition != originalMuzzle) throw new System.InvalidOperationException("Muzzle anchor changed.");
// Diagnostic-only overlays: yellow = unchanged Core collision bounds; cyan = saved muzzle anchor.
var geometry = new System.Collections.Generic.List<object>();
if (label == "after")
{
    var pixel = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/Art/Sprites/px_white.png");
    var overlayRoot = new UnityEngine.GameObject("SFC diagnostic overlays");
    void Bar(UnityEngine.Vector3 center, float width, float height, UnityEngine.Color color)
    {
        var go = new UnityEngine.GameObject("diagnostic line"); go.transform.SetParent(overlayRoot.transform);
        go.transform.position = center;
        go.transform.localScale = new UnityEngine.Vector3(width / pixel.bounds.size.x, height / pixel.bounds.size.y, 1);
        var renderer = go.AddComponent<UnityEngine.SpriteRenderer>(); renderer.sprite = pixel;
        renderer.color = color; renderer.sortingOrder = 30000;
    }
    void Box(UnityEngine.Vector3 center, float halfWidth, float halfHeight)
    {
        Bar(center + new UnityEngine.Vector3(0, halfHeight, 0), 2 * halfWidth, 1f / 16, UnityEngine.Color.yellow);
        Bar(center - new UnityEngine.Vector3(0, halfHeight, 0), 2 * halfWidth, 1f / 16, UnityEngine.Color.yellow);
        Bar(center + new UnityEngine.Vector3(halfWidth, 0, 0), 1f / 16, 2 * halfHeight, UnityEngine.Color.yellow);
        Bar(center - new UnityEngine.Vector3(halfWidth, 0, 0), 1f / 16, 2 * halfHeight, UnityEngine.Color.yellow);
    }
    var config = data.CreateBattleSimConfig();
    Box(player.position, config.PlayerHalfWidth / 256f, config.PlayerHalfHeight / 256f);
    geometry.Add(new { id = "starter", halfWidth = config.PlayerHalfWidth / 256f, halfHeight = config.PlayerHalfHeight / 256f });
    foreach (var enemy in enemies)
    {
        var definition = data.BattleContent.FindEnemy(enemy.DefinitionId);
        Box(views[enemy.Id].position, definition.HalfWidth / 256f, definition.HalfHeight / 256f);
        geometry.Add(new { id = enemy.DefinitionId, halfWidth = definition.HalfWidth / 256f, halfHeight = definition.HalfHeight / 256f });
    }
    Bar(muzzle.transform.position, .375f, 1f / 16, UnityEngine.Color.cyan);
    Bar(muzzle.transform.position, 1f / 16, .375f, UnityEngine.Color.cyan);
    Capture("battle-after-geometry-theme-4");
    UnityEngine.Object.DestroyImmediate(overlayRoot);
}
// The pool must replace the old identity's art on reuse, including animated -> static.
enemies.Clear(); Call("SyncEnemies");
enemies.Add(new Shmup.Core.Simulation.EnemyState(104, "zako_fast", 0, 0, 8)); Call("SyncEnemies");
string reusedSprite = UnityEditor.AssetDatabase.GetAssetPath(views[104].GetComponent<UnityEngine.SpriteRenderer>().sprite);
if (label == "after" && !reusedSprite.EndsWith("enemy_sfc_fast.png")) throw new System.InvalidOperationException("Old art leaked through pooled reuse.");
System.IO.File.WriteAllText(System.IO.Path.Combine(outputRoot, label + "-capture.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
    label, kind = "Real scene bindings, SyncPlayerAnimation/SyncEnemies/SyncBullets with isolated injected state; not a playthrough; UI hidden",
    sourceSeed = 12345, imports, samples, muzzleLocalPosition = new[] { originalMuzzle.x, originalMuzzle.y, originalMuzzle.z },
    reusedSprite, collisionOverlay = geometry, sceneSaved = false, playerSavesAccessed = false
}, Newtonsoft.Json.Formatting.Indented));
return new { outputRoot, captures = samples.Count, reusedSprite, sceneSaved = false };

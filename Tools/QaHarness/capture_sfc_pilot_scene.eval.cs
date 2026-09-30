// Native pilot review: old/new contact sheet and temporary Battle-scene fixtures.
// Does not import candidates, save scenes, enter Play, or read/write player saves.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires disposable batch Editor outside Play Mode.");
string DetectPipeline()
{
    var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
    return pipeline == null ? "BuiltIn" : pipeline.GetType().FullName.Contains("Universal") ? "URP" : "Other";
}
if (DetectPipeline() != "URP") throw new System.InvalidOperationException("Expected URP.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("GPU required.");
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
string outputRoot = System.IO.Path.GetFullPath("ArtRevamp/SFC-20260930/pilot/review");
System.IO.Directory.CreateDirectory(outputRoot);
var definitions = new[] {
    new[] { "STARTER / candidate 05", "player_ship", "starter/candidate_05.png" },
    new[] { "ROUND DRONE", "enemy", "drone.png" },
    new[] { "FAST INTERCEPTOR / revised", "anim_zako_fast_00", "fast-v2.png" },
    new[] { "TURRET / revised", "enemy_turret", "turret-v2.png" }
};
var originals = new System.Collections.Generic.List<UnityEngine.Sprite>();
var candidates = new System.Collections.Generic.List<UnityEngine.Sprite>();
var metrics = new System.Collections.Generic.List<object>();
foreach (var definition in definitions)
{
    var original = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/Art/Sprites/" + definition[1] + ".png");
    if (original == null) throw new System.InvalidOperationException("Missing baseline " + definition[1]);
    originals.Add(original);
    string path = "ArtRevamp/SFC-20260930/pilot/raw/" + definition[2];
    var texture = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
    texture.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
    if (!UnityEngine.ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path)))
        throw new System.InvalidOperationException("Cannot decode " + path);
    if (texture.width != original.rect.width || texture.height != original.rect.height)
        throw new System.InvalidOperationException("Native canvas mismatch " + path);
    texture.filterMode = UnityEngine.FilterMode.Point; texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
    var colors = new System.Collections.Generic.HashSet<uint>(); int visible = 0, partial = 0;
    foreach (var p in texture.GetPixels32())
    {
        if (p.a == 0) continue;
        visible++; if (p.a < 255) partial++;
        colors.Add(((uint)p.r << 24) | ((uint)p.g << 16) | ((uint)p.b << 8) | p.a);
    }
    var sprite = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, texture.width, texture.height),
        new UnityEngine.Vector2(.5f, .5f), 16, 0, UnityEngine.SpriteMeshType.FullRect);
    sprite.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
    sprite.name = definition[0]; candidates.Add(sprite);
    metrics.Add(new { label = definition[0], source = path, texture.width, texture.height,
        visibleRgbaColors = colors.Count, partialAlphaPixels = partial, visiblePixels = visible,
        paletteBudgetPass = colors.Count <= 48, postprocessed = false });
}
void Capture(UnityEngine.Camera camera, string name, int width, int height)
{
    var target = UnityEngine.RenderTexture.GetTemporary(width, height, 24);
    var previous = UnityEngine.RenderTexture.active; var previousTarget = camera.targetTexture;
    try
    {
        camera.targetTexture = target; camera.Render(); UnityEngine.RenderTexture.active = target;
        var image = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGB24, false);
        image.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0); image.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outputRoot, name + ".png"), UnityEngine.ImageConversion.EncodeToPNG(image));
        UnityEngine.Object.DestroyImmediate(image);
    }
    finally { camera.targetTexture = previousTarget; UnityEngine.RenderTexture.active = previous; UnityEngine.RenderTexture.ReleaseTemporary(target); }
}
var reviewCamera = new UnityEngine.GameObject("Native pair review camera").AddComponent<UnityEngine.Camera>();
const int boardWidth = 1280, boardHeight = 1080;
reviewCamera.orthographic = true; reviewCamera.orthographicSize = boardHeight / 32f;
reviewCamera.transform.position = new UnityEngine.Vector3(0, 0, -10);
reviewCamera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
reviewCamera.backgroundColor = new UnityEngine.Color(.025f, .04f, .075f);
reviewCamera.allowHDR = false; reviewCamera.allowMSAA = false;
var font = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
UnityEngine.Vector3 At(float x, float y) => new UnityEngine.Vector3((x - boardWidth / 2f) / 16f, (boardHeight / 2f - y) / 16f, 0);
void Label(string value, float x, float y, float size = .32f)
{
    var go = new UnityEngine.GameObject(value); go.transform.position = At(x, y);
    var text = go.AddComponent<UnityEngine.TextMesh>(); text.text = value; text.font = font;
    text.fontSize = 32; text.characterSize = size; text.anchor = UnityEngine.TextAnchor.UpperLeft;
    text.color = new UnityEngine.Color(.7f, .82f, 1f);
    go.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = font.material;
}
void Draw(UnityEngine.Sprite sprite, float x, float y, int scale)
{
    var go = new UnityEngine.GameObject(sprite.name); go.transform.position = At(x, y);
    go.transform.localScale = UnityEngine.Vector3.one * scale;
    go.AddComponent<UnityEngine.SpriteRenderer>().sprite = sprite;
}
Label("SFC STYLE 2 / NATIVE SPRITE PILOT / REVIEW ONLY", 24, 18, .45f);
Label("CURRENT", 280, 70); Label("CANDIDATE", 760, 70); Label("1x", 1130, 70);
for (int i = 0; i < definitions.Length; i++)
{
    int y = 188 + i * 188;
    int scale = 5;
    Label(definitions[i][0], 24, y - 66, .255f);
    Draw(originals[i], 390, y, scale); Draw(candidates[i], 840, y, scale);
    Draw(candidates[i], 1150, y, 1);
    Label(candidates[i].rect.width + "x" + candidates[i].rect.height + " / " + scale + "x + 1x", 760, y + 70, .255f);
}
var engineTexture = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
engineTexture.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
UnityEngine.ImageConversion.LoadImage(engineTexture, System.IO.File.ReadAllBytes("ArtRevamp/SFC-20260930/pilot/raw/starter-engine/frame_01.png"));
engineTexture.filterMode = UnityEngine.FilterMode.Point;
if (engineTexture.width != 48 || engineTexture.height != 30) throw new System.InvalidOperationException("Engine canvas mismatch.");
var basePixels = candidates[0].texture.GetPixels32(); var enginePixels = engineTexture.GetPixels32();
int flameChanges = 0;
for (int p = 0; p < basePixels.Length; p++)
{
    var a = basePixels[p]; var b = enginePixels[p];
    if (a.a == 0 && b.a == 0 || a.Equals(b)) continue;
    // Input image coordinates: upper-left (0..8, 6..17). Body pixels are immutable.
    int x = p % 48, yTop = 29 - p / 48;
    if (x >= 9 || yTop < 6 || yTop >= 18) throw new System.InvalidOperationException("Engine loop moves the hull.");
    flameChanges++;
}
if (flameChanges == 0) throw new System.InvalidOperationException("Engine poses are identical.");
var engineSprite = UnityEngine.Sprite.Create(engineTexture, new UnityEngine.Rect(0, 0, 48, 30), new UnityEngine.Vector2(.5f, .5f), 16, 0, UnityEngine.SpriteMeshType.FullRect);
engineSprite.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
Label("ENGINE LOOP / 2 POSES", 24, 918, .29f);
Draw(candidates[0], 490, 952, 4); Draw(engineSprite, 890, 952, 4);
Label("A", 490, 1018); Label("B", 890, 1018);
Label("10 fps / hull + muzzle identical / only " + flameChanges + " exhaust pixels change", 24, 1040, .27f);
Capture(reviewCamera, "pilot-static-comparison", boardWidth, boardHeight);

UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
object Read(string name) => director.GetType().GetField(name, flags).GetValue(director);
var themes = (UnityEngine.GameObject[])Read("_themeBackgrounds");
var player = (UnityEngine.Transform)Read("_playerTransform");
var playerRenderer = player.GetComponent<UnityEngine.SpriteRenderer>();
player.position = new UnityEngine.Vector3(-11, 0, 0);
foreach (var canvas in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
    canvas.gameObject.SetActive(false);
var camera = UnityEngine.Camera.main;
var ppc = camera.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
if (ppc.assetsPPU != 16 || ppc.refResolutionX != 640 || ppc.refResolutionY != 360)
    throw new System.InvalidOperationException("Pixel reference must stay 640x360 / PPU16.");
var previews = new System.Collections.Generic.List<UnityEngine.SpriteRenderer>();
var positions = new[] { new UnityEngine.Vector3(-2, 3, 0), new UnityEngine.Vector3(6, .5f, 0), new UnityEngine.Vector3(10, -3, 0) };
var fixtureIds = new[] { "zako_straight", "zako_fast", "turret_ground" };
var enemyData = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("GameData/enemies.json"))["enemies"];
var fixtureScales = new System.Collections.Generic.List<object>();
for (int i = 0; i < 3; i++)
{
    var go = new UnityEngine.GameObject("Pilot fixture " + definitions[i + 1][0]); go.transform.position = positions[i];
    float halfWidth = 0;
    foreach (var enemy in enemyData)
        if ((string)enemy["id"] == fixtureIds[i]) halfWidth = (float)enemy["halfWidth"];
    if (halfWidth <= 0) throw new System.InvalidOperationException("Missing enemy size " + fixtureIds[i]);
    // Same uniform width formula as BattleDirector.ApplyEnemyScale. Never edit GameData.
    float scale = 2f * halfWidth / (candidates[i + 1].rect.width / 16f);
    go.transform.localScale = new UnityEngine.Vector3(scale, scale, 1);
    fixtureScales.Add(new { id = fixtureIds[i], halfWidthWorld = halfWidth, scale });
    var renderer = go.AddComponent<UnityEngine.SpriteRenderer>(); renderer.sharedMaterial = playerRenderer.sharedMaterial;
    renderer.sortingLayerID = playerRenderer.sortingLayerID; renderer.sortingOrder = playerRenderer.sortingOrder;
    previews.Add(renderer);
}
var outputs = new System.Collections.Generic.List<string>();
foreach (int theme in new[] { 0, 2, 4 })
{
    if (theme >= themes.Length) throw new System.InvalidOperationException("Missing theme " + theme);
    for (int i = 0; i < themes.Length; i++) if (themes[i] != null) themes[i].SetActive(i == theme);
    foreach (bool replacement in new[] { false, true })
    {
        playerRenderer.sprite = replacement ? candidates[0] : originals[0];
        for (int i = 0; i < previews.Count; i++) previews[i].sprite = replacement ? candidates[i + 1] : originals[i + 1];
        string name = "battle-theme-" + theme + (replacement ? "-candidate" : "-baseline");
        Capture(camera, name, 1280, 720); outputs.Add(name + ".png");
    }
}
System.IO.File.WriteAllText(System.IO.Path.Combine(outputRoot, "pilot-audit.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
    kind = "GPU Editor art fixtures, not gameplay; UI hidden; backgrounds unchanged; candidates never imported",
    ppu = 16, referenceWidth = 640, referenceHeight = 360, metrics, battleCaptures = outputs, fixtureScales,
    engine = new { frames = 2, framesPerSecond = 10, flameChanges, hullAndMuzzleUnchanged = true },
    sceneSaved = false, playerSavesAccessed = false, productionArtAdopted = false
}, Newtonsoft.Json.Formatting.Indented));
return new { outputRoot, spritePairs = candidates.Count, battleCaptures = outputs.Count, sceneSaved = false, assetsImported = false };

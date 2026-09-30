// Disposable Unity contact sheet. Candidate PNG bytes are decoded, never edited/imported.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires a disposable batch Editor outside Play Mode.");
var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
if (pipeline == null || !pipeline.GetType().FullName.Contains("Universal")
    || UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("URP and GPU required.");
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
const int width = 1440, height = 900;
var camera = new UnityEngine.GameObject("Banking review camera").AddComponent<UnityEngine.Camera>();
camera.orthographic = true; camera.orthographicSize = height / 32f;
camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(.025f, .04f, .075f);
camera.allowHDR = false; camera.allowMSAA = false;
var font = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
UnityEngine.Vector3 At(float x, float y) => new UnityEngine.Vector3((x - width / 2f) / 16f, (height / 2f - y) / 16f, 0);
void Label(string value, float x, float y, float size = .36f)
{
    var go = new UnityEngine.GameObject(value); go.transform.position = At(x, y);
    var text = go.AddComponent<UnityEngine.TextMesh>();
    text.text = value; text.font = font; text.fontSize = 32; text.characterSize = size;
    text.anchor = UnityEngine.TextAnchor.UpperLeft; text.color = new UnityEngine.Color(.7f, .82f, 1f);
    go.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = font.material;
}
void Draw(UnityEngine.Sprite sprite, float x, float y, int scale)
{
    var go = new UnityEngine.GameObject(sprite.name); go.transform.position = At(x, y);
    go.transform.localScale = UnityEngine.Vector3.one * scale;
    go.AddComponent<UnityEngine.SpriteRenderer>().sprite = sprite;
}
string root = "ArtRevamp/SFC-20260930/banking";
string variant = System.Environment.GetEnvironmentVariable("RSS_BANK_VARIANT") ?? "fold";
if (variant != "fold" && variant != "refined") throw new System.InvalidOperationException("Unknown review variant.");
string[] files = {
    root + "/raw/masked-up-" + variant + "/candidate_00.png",
    "Assets/Art/Sprites/sfc_player_ship.png",
    root + "/raw/masked-down-" + variant + "/candidate_00.png"
};
string[] labels = { "UP / wing pose candidate", "NEUTRAL / already in game", "DOWN / wing pose candidate" };
string[] engineFiles = {
    root + "/raw/bank-up-engine-warm/candidate_00.png",
    "Assets/Art/Sprites/sfc_player_engine_01.png",
    root + "/raw/bank-down-engine-warm/candidate_00.png"
};
Label("SFC STARTER / NATIVE 48x30 / BANKING CANDIDATES / NOT YET ADOPTED", 28, 20, .5f);
Label("Wings change for the bank; flames loop independently. Hull, canopy and nose stay fixed.", 28, 58, .35f);
for (int i = 0; i < files.Length; i++)
{
    var texture = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
    if (!UnityEngine.ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(files[i])) || texture.width != 48 || texture.height != 30)
        throw new System.InvalidOperationException("Wrong native image: " + files[i]);
    texture.filterMode = UnityEngine.FilterMode.Point; texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
    var sprite = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, 48, 30), new UnityEngine.Vector2(.5f, .5f), 16, 0, UnityEngine.SpriteMeshType.FullRect);
    sprite.name = labels[i];
    int x = i * 480;
    Label(labels[i], x + 32, 115);
    Draw(sprite, x + 240, 280, 8);
    Label("8x / engine A", x + 32, 422, .32f);
    if (variant == "fold")
    {
        var engineTexture = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
        if (!UnityEngine.ImageConversion.LoadImage(engineTexture, System.IO.File.ReadAllBytes(engineFiles[i])) || engineTexture.width != 48 || engineTexture.height != 30)
            throw new System.InvalidOperationException("Wrong engine image.");
        engineTexture.filterMode = UnityEngine.FilterMode.Point;
        var engineSprite = UnityEngine.Sprite.Create(engineTexture, new UnityEngine.Rect(0, 0, 48, 30), UnityEngine.Vector2.one * .5f, 16, 0, UnityEngine.SpriteMeshType.FullRect);
        Draw(engineSprite, x + 240, 590, 8);
        Label("8x / engine B / hull fixed", x + 32, 732, .32f);
    }
    Draw(sprite, x + 95, 810, 2); Draw(sprite, x + 255, 810, 1);
    Label("2x", x + 151, 793, .31f); Label("1x", x + 288, 793, .31f);
}
var target = UnityEngine.RenderTexture.GetTemporary(width, height, 24);
var previous = UnityEngine.RenderTexture.active;
string outputRoot = System.IO.Path.GetFullPath(root + "/review"); System.IO.Directory.CreateDirectory(outputRoot);
try
{
    camera.targetTexture = target; camera.Render(); UnityEngine.RenderTexture.active = target;
    var capture = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGB24, false);
    capture.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0); capture.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(outputRoot, "banking-poses-" + variant + ".png"), UnityEngine.ImageConversion.EncodeToPNG(capture));
    UnityEngine.Object.DestroyImmediate(capture);
}
finally { camera.targetTexture = null; UnityEngine.RenderTexture.active = previous; UnityEngine.RenderTexture.ReleaseTemporary(target); }
return new { outputRoot, sceneSaved = false, assetsImported = false, gameplayCapture = false, pngPostprocessed = false };

// Review rendering only. Decode raw generator output; never modify/import candidate PNGs.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires batch Editor outside Play Mode.");
var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
if (pipeline == null || !pipeline.GetType().FullName.Contains("Universal")
    || UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("Expected URP with windowless GPU rendering.");
string root = "ArtRevamp/SFC-20260930/combat";
string batch = System.Environment.GetEnvironmentVariable("RSS_COMBAT_BATCH") ?? "explosion-ignition";
if (!System.Text.RegularExpressions.Regex.IsMatch(batch, "^[a-z0-9-]+$"))
    throw new System.InvalidOperationException("Invalid review batch name.");
var files = System.IO.Directory.GetFiles(root + "/raw/" + batch, "*.png");
System.Array.Sort(files, System.StringComparer.Ordinal);
if (files.Length == 0 || files.Length > 32) throw new System.InvalidOperationException("Unexpected candidate count.");
int columns = 4, cellWidth = 280, cellHeight = 250;
int width = columns * cellWidth, height = 95 + ((files.Length + columns - 1) / columns) * cellHeight;
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
var camera = new UnityEngine.GameObject("Review Camera").AddComponent<UnityEngine.Camera>();
camera.orthographic = true; camera.orthographicSize = height / 32f; camera.aspect = width / (float)height;
camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(.025f, .045f, .085f); camera.allowHDR = false; camera.allowMSAA = false;
var font = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
UnityEngine.Vector3 At(float x, float y) => new UnityEngine.Vector3((x - width / 2f) / 16f, (height / 2f - y) / 16f, 0);
void Label(string value, float x, float y, float size = .32f)
{
    var go = new UnityEngine.GameObject(value); go.transform.position = At(x, y);
    var text = go.AddComponent<UnityEngine.TextMesh>(); text.text = value; text.font = font; text.fontSize = 32;
    text.characterSize = size; text.anchor = UnityEngine.TextAnchor.UpperLeft; text.color = new UnityEngine.Color(.72f, .83f, 1);
    go.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = font.material;
}
void Draw(UnityEngine.Sprite sprite, float x, float y, int scale)
{
    var go = new UnityEngine.GameObject(sprite.name); go.transform.position = At(x, y);
    go.transform.localScale = UnityEngine.Vector3.one * scale;
    go.AddComponent<UnityEngine.SpriteRenderer>().sprite = sprite;
}
Label("SFC COMBAT / " + batch + " / RAW CANDIDATES / NOT ADOPTED", 24, 18, .43f);
Label("4x and 1x display / original PNG bytes / inspect silhouette, center and hard alpha", 24, 54);
for (int i = 0; i < files.Length; i++)
{
    var texture = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
    if (!UnityEngine.ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(files[i])))
        throw new System.InvalidOperationException("Cannot decode candidate.");
    texture.filterMode = UnityEngine.FilterMode.Point; texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
    var sprite = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, texture.width, texture.height),
        UnityEngine.Vector2.one * .5f, 16, 0, UnityEngine.SpriteMeshType.FullRect);
    sprite.name = System.IO.Path.GetFileNameWithoutExtension(files[i]);
    int left = (i % columns) * cellWidth, top = 95 + (i / columns) * cellHeight;
    Label(sprite.name + " / " + texture.width + "x" + texture.height, left + 20, top);
    Draw(sprite, left + 125, top + 125, 4); Draw(sprite, left + 242, top + 205, 1);
}
string outputRoot = root + "/review"; System.IO.Directory.CreateDirectory(outputRoot);
var target = UnityEngine.RenderTexture.GetTemporary(width, height, 24);
var previous = UnityEngine.RenderTexture.active;
try
{
    camera.targetTexture = target; camera.Render(); UnityEngine.RenderTexture.active = target;
    var capture = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGB24, false);
    capture.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0); capture.Apply();
    System.IO.File.WriteAllBytes(outputRoot + "/" + batch + "-contact.png", UnityEngine.ImageConversion.EncodeToPNG(capture));
    UnityEngine.Object.DestroyImmediate(capture);
}
finally { camera.targetTexture = null; UnityEngine.RenderTexture.active = previous; UnityEngine.RenderTexture.ReleaseTemporary(target); }
return new { batch, candidates = files.Length, output = outputRoot + "/" + batch + "-contact.png",
    sceneSaved = false, assetsImported = false, candidatePixelsEdited = false };

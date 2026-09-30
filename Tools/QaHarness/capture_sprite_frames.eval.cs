// Diagnostic contact sheets of unchanged, imported sprite frames. Not gameplay captures.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires batch Editor outside Play Mode.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("GPU required.");
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
var camera = new UnityEngine.GameObject("Frame review camera").AddComponent<UnityEngine.Camera>();
camera.orthographic = true;
camera.orthographicSize = 5.5f;
camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(.025f, .04f, .07f);
camera.allowHDR = false; camera.allowMSAA = false;
var font = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
void Label(string text, float x, float y, float size = .055f)
{
    var go = new UnityEngine.GameObject(text);
    go.transform.position = new UnityEngine.Vector3(x, y, 0);
    var label = go.AddComponent<UnityEngine.TextMesh>();
    label.text = text; label.font = font; label.fontSize = 32; label.characterSize = size;
    label.anchor = UnityEngine.TextAnchor.MiddleLeft;
    label.color = new UnityEngine.Color(.7f, .82f, 1f);
    go.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = font.material;
}
var pages = new[] {
    new[] { "ship", "zako_sine", "interceptor", "echo_wisp", "void_moth", "phase_disc", "rift_blade" },
    new[] { "mini_crystal", "boss_storm", "boss_broodmother", "boss_leviathan" }
};
var paths = new System.Collections.Generic.List<string>();
for (int page = 0; page < pages.Length; page++)
{
    foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        if (root != camera.gameObject) UnityEngine.Object.DestroyImmediate(root);
    Label("IMPORTED FRAMES / 00 > 01 > 02 > 03 > 04 > 00", -8.7f, 5f, .07f);
    var rows = pages[page];
    for (int row = 0; row < rows.Length; row++)
    {
        float y = 3.8f - row * (8f / rows.Length);
        Label(rows[row], -8.7f, y - .37f, .05f);
        for (int frame = 0; frame < 6; frame++)
        {
            string prefix = rows[row] == "ship" ? "ship_anim_" : "anim_" + rows[row] + "_";
            string path = "Assets/Art/Sprites/" + prefix + (frame % 5).ToString("D2") + ".png";
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(path);
            if (sprite == null) throw new System.InvalidOperationException("Missing " + path);
            var go = new UnityEngine.GameObject(path);
            go.transform.position = new UnityEngine.Vector3(-5.4f + frame * 2.65f, y, 0);
            var renderer = go.AddComponent<UnityEngine.SpriteRenderer>(); renderer.sprite = sprite;
            float maxDimension = UnityEngine.Mathf.Max(sprite.rect.width, sprite.rect.height);
            float scale = UnityEngine.Mathf.Min(1f, (page == 0 ? 35f : 40f) / maxDimension);
            go.transform.localScale = UnityEngine.Vector3.one * scale;
        }
    }
    var target = UnityEngine.RenderTexture.GetTemporary(1440, 810, 24);
    var previous = UnityEngine.RenderTexture.active;
    camera.targetTexture = target;
    camera.Render();
    UnityEngine.RenderTexture.active = target;
    var image = new UnityEngine.Texture2D(1440, 810, UnityEngine.TextureFormat.RGB24, false);
    image.ReadPixels(new UnityEngine.Rect(0, 0, 1440, 810), 0, 0); image.Apply();
    string output = System.IO.Path.GetFullPath("out/revamp/animation-frames-" + page + ".png");
    System.IO.File.WriteAllBytes(output, UnityEngine.ImageConversion.EncodeToPNG(image));
    paths.Add(output);
    camera.targetTexture = null; UnityEngine.RenderTexture.active = previous;
    UnityEngine.RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image);
}
return new { paths, sceneSaved = false, kind = "Imported frame contact sheet, not gameplay" };

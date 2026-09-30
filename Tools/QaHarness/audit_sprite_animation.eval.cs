// Read-only frame audit of the actual Battle scene. Requires a disposable batch Editor.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Use batch Editor outside Play Mode.");
string DetectPipeline()
{
    var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
    string name = asset == null ? "" : asset.GetType().FullName;
    return asset == null ? "BuiltIn" : name.Contains("Universal") ? "URP" : "Other";
}
if (DetectPipeline() != "URP") throw new System.InvalidOperationException("Expected URP.");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
var serialized = new UnityEditor.SerializedObject(director);
var player = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.PlayerShipAnimator>();
var groups = new System.Collections.Generic.List<(string id, UnityEngine.Sprite[] frames)>();
var ship = new UnityEditor.SerializedObject(player).FindProperty("_frames");
var shipFrames = new UnityEngine.Sprite[ship.arraySize];
for (int i = 0; i < shipFrames.Length; i++) shipFrames[i] = ship.GetArrayElementAtIndex(i).objectReferenceValue as UnityEngine.Sprite;
groups.Add(("player", shipFrames));
var prefixes = serialized.FindProperty("_animPrefixes");
var counts = serialized.FindProperty("_animFrameCounts");
var flat = serialized.FindProperty("_animFrames");
int offset = 0;
for (int i = 0; i < prefixes.arraySize; i++)
{
    var sprites = new UnityEngine.Sprite[counts.GetArrayElementAtIndex(i).intValue];
    for (int j = 0; j < sprites.Length; j++) sprites[j] = flat.GetArrayElementAtIndex(offset++).objectReferenceValue as UnityEngine.Sprite;
    groups.Add((prefixes.GetArrayElementAtIndex(i).stringValue, sprites));
}
var report = new System.Collections.Generic.List<object>();
foreach (var group in groups)
{
    var frames = new System.Collections.Generic.List<object>();
    var pixels = new System.Collections.Generic.List<UnityEngine.Color32[]>();
    foreach (var sprite in group.frames)
    {
        if (sprite == null) throw new System.InvalidOperationException("Missing sprite: " + group.id);
        string path = UnityEditor.AssetDatabase.GetAssetPath(sprite);
        var texture = new UnityEngine.Texture2D(2, 2);
        UnityEngine.ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path));
        var colors = texture.GetPixels32();
        pixels.Add(colors);
        int area = 0, minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
        double sx = 0, sy = 0, light = 0;
        foreach (var color in colors) if (color.a > 0) light += (.2126 * color.r + .7152 * color.g + .0722 * color.b) * color.a / 255.0;
        for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
        {
            if (colors[y * texture.width + x].a < 128) continue;
            area++; sx += x; sy += y;
            minX = System.Math.Min(minX, x); maxX = System.Math.Max(maxX, x);
            minY = System.Math.Min(minY, y); maxY = System.Math.Max(maxY, y);
        }
        var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
        frames.Add(new { path, width = texture.width, height = texture.height, area,
            centerX = System.Math.Round(sx / System.Math.Max(1, area), 2), centerY = System.Math.Round(sy / System.Math.Max(1, area), 2),
            light = System.Math.Round(light, 1), bounds = new[] { minX, minY, maxX, maxY },
            pivot = new[] { sprite.pivot.x, sprite.pivot.y }, ppu = sprite.pixelsPerUnit,
            filter = importer.filterMode.ToString(), mipmaps = importer.mipmapEnabled, compression = importer.textureCompression.ToString() });
        UnityEngine.Object.DestroyImmediate(texture);
    }
    var changes = new System.Collections.Generic.List<int>();
    for (int i = 0; i < pixels.Count; i++)
    {
        var a = pixels[i]; var b = pixels[(i + 1) % pixels.Count];
        int changed = 0;
        if (a.Length == b.Length)
        {
            for (int p = 0; p < a.Length; p++)
                if ((a[p].a != 0 || b[p].a != 0) && !a[p].Equals(b[p])) changed++;
        }
        else changed = -1;
        changes.Add(changed);
    }
    report.Add(new { id = group.id, frames, adjacentChangedPixelsIncludingWrap = changes });
}
var ppc = UnityEngine.Camera.main.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
string output = System.IO.Path.GetFullPath("out/revamp/animation-audit.json");
System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    pipeline = DetectPipeline(), ppu = ppc.assetsPPU, resolution = new[] { ppc.refResolutionX, ppc.refResolutionY }, groups = report
}, Newtonsoft.Json.Formatting.Indented));
return new { output, groups = groups.Count, frames = offset + shipFrames.Length, sceneSaved = false };

// Read-only inventory for the user-requested foreground art overhaul.
// Opens saved scenes in a disposable batch Editor; never saves or enters Play.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Art audit requires a disposable batch Editor.");
string DetectPipeline()
{
    var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
    string name = asset == null ? "" : asset.GetType().FullName;
    return asset == null ? "BuiltIn" : name.Contains("Universal") ? "URP" : "Other";
}
if (DetectPipeline() != "URP") throw new System.InvalidOperationException("Expected the existing URP pipeline.");
var references = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
foreach (string scenePath in new[] { "Assets/Scenes/Title.unity", "Assets/Scenes/Battle.unity" })
{
    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
    foreach (var root in scene.GetRootGameObjects())
    foreach (var component in root.GetComponentsInChildren<UnityEngine.Component>(true))
    {
        if (component == null) continue;
        var serialized = new UnityEditor.SerializedObject(component);
        var property = serialized.GetIterator();
        while (property.Next(true))
        {
            if (property.propertyType != UnityEditor.SerializedPropertyType.ObjectReference) continue;
            if (!(property.objectReferenceValue is UnityEngine.Sprite)) continue;
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(property.objectReferenceValue);
            if (!assetPath.StartsWith("Assets/Art/Sprites/", System.StringComparison.Ordinal)) continue;
            if (!references.ContainsKey(assetPath)) references.Add(assetPath, new System.Collections.Generic.List<string>());
            references[assetPath].Add(scenePath + ":" + component.gameObject.name + ":" + component.GetType().Name + "." + property.propertyPath);
        }
    }
}
string Category(string name)
{
    if (System.Text.RegularExpressions.Regex.IsMatch(name,
        @"^(scrap|hive|fort|nebula|core|abyss|brood)_(far(_dark|_dusk)?|mid|near|fg|landmark)$")
        || name.StartsWith("stars_", System.StringComparison.Ordinal) || name == "title_keyart") return "excluded_background";
    if (name == "px_white") return "excluded_utility";
    if (name.StartsWith("ship_", System.StringComparison.Ordinal) || name == "player_ship") return "player";
    if (name.StartsWith("boss_", System.StringComparison.Ordinal) || name.StartsWith("anim_boss_", System.StringComparison.Ordinal)
        || name.StartsWith("warship_", System.StringComparison.Ordinal)) return "boss_and_parts";
    if (name.StartsWith("enemy_mini_", System.StringComparison.Ordinal) || name.StartsWith("anim_mini_", System.StringComparison.Ordinal)) return "midboss";
    if (name.StartsWith("bullet", System.StringComparison.Ordinal) || name.StartsWith("missile", System.StringComparison.Ordinal)
        || name == "enemy_shot" || name.StartsWith("laser_", System.StringComparison.Ordinal)) return "projectile";
    if (name.StartsWith("enemy", System.StringComparison.Ordinal) || name.StartsWith("anim_", System.StringComparison.Ordinal)) return "enemy";
    if (name.StartsWith("obstacle_", System.StringComparison.Ordinal)) return "obstacle";
    if (name.StartsWith("fx_", System.StringComparison.Ordinal) || name == "explosion" || name == "shield") return "effect";
    if (name == "capsule" || name == "bomb_pickup" || name == "option") return "pickup_and_equipment";
    if (name.StartsWith("hud_", System.StringComparison.Ordinal) || name.StartsWith("icon_", System.StringComparison.Ordinal)
        || name.StartsWith("clear_", System.StringComparison.Ordinal)) return "ui_art";
    return "review_unclassified";
}
var paths = System.IO.Directory.GetFiles("Assets/Art/Sprites", "*.png");
System.Array.Sort(paths, System.StringComparer.Ordinal);
var rows = new System.Collections.Generic.List<object>();
var counts = new System.Collections.Generic.SortedDictionary<string, int>(System.StringComparer.Ordinal);
int foreground = 0, partialAlphaAssets = 0, overPaletteBudget = 0;
foreach (string rawPath in paths)
{
    string path = rawPath.Replace('\\', '/');
    string name = System.IO.Path.GetFileNameWithoutExtension(path);
    string category = Category(name);
    bool inScope = !category.StartsWith("excluded_", System.StringComparison.Ordinal);
    byte[] bytes = System.IO.File.ReadAllBytes(path);
    var texture = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
    UnityEngine.ImageConversion.LoadImage(texture, bytes);
    var colors = new System.Collections.Generic.HashSet<uint>();
    int visible = 0, partialAlpha = 0;
    if (inScope)
    {
        foreground++;
        foreach (var p in texture.GetPixels32())
        {
            if (p.a == 0) continue;
            visible++;
            if (p.a < 255) partialAlpha++;
            colors.Add(((uint)p.r << 24) | ((uint)p.g << 16) | ((uint)p.b << 8) | p.a);
        }
        if (partialAlpha > 0) partialAlphaAssets++;
        if (colors.Count > 48) overPaletteBudget++;
    }
    var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
    string sha256;
    using (var sha = System.Security.Cryptography.SHA256.Create())
        sha256 = System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    var directReferences = references.ContainsKey(path) ? references[path].ToArray() : new string[0];
    rows.Add(new {
        path, category, inScope, texture.width, texture.height, sha256,
        visiblePixelCount = inScope ? (int?)visible : null,
        visibleRgbaColors = inScope ? (int?)colors.Count : null,
        partialAlphaPixels = inScope ? (int?)partialAlpha : null,
        ppu = importer == null ? 0 : importer.spritePixelsPerUnit,
        pivotNormalized = importer == null ? new float[0] : new[] { importer.spritePivot.x, importer.spritePivot.y },
        filter = importer == null ? "unknown" : importer.filterMode.ToString(),
        mipmaps = importer != null && importer.mipmapEnabled,
        compression = importer == null ? "unknown" : importer.textureCompression.ToString(),
        directSceneReferences = directReferences
    });
    UnityEngine.Object.DestroyImmediate(texture);
    counts[category] = counts.ContainsKey(category) ? counts[category] + 1 : 1;
}
string output = System.IO.Path.GetFullPath("ArtRevamp/SFC-20260930/foreground-inventory.json");
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
var summary = new { totalPng = paths.Length, foreground, partialAlphaAssets, overPaletteBudget, counts };
System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    sourceBaseline = "f19355f", pipeline = DetectPipeline(), summary,
    notes = new[] {
        "Counts are PNG files, including animation frames and alternates, not unique enemy types.",
        "Backgrounds include parallax foreground layers, stars and title key art; all are excluded.",
        "px_white is an excluded rendering utility. Foreground names are explicitly classified; unclassified assets need review.",
        "Direct scene references exclude runtime lookups and builder-only assignments; an empty list does not prove an unused asset.",
        "Visible RGBA colors count alpha variants; effects can intentionally use partial alpha. This is a review signal, not automatic rejection."
    }, assets = rows
}, Newtonsoft.Json.Formatting.Indented));
return new { output, summary };

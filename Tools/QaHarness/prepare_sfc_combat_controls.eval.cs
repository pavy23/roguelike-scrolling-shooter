// API control inputs only. Existing sprites are copied 1:1 into transparent padding.
// The generated output must retain this padding before a Sprite rect can select it.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires batch Editor outside Play Mode.");
var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
if (pipeline == null || !pipeline.GetType().FullName.Contains("Universal"))
    throw new System.InvalidOperationException("Expected the existing URP project.");
string outputRoot = "ArtRevamp/SFC-20260930/combat/controls";
System.IO.Directory.CreateDirectory(outputRoot);
var records = new System.Collections.Generic.List<object>();
foreach (var spec in new[] {
    new { id = "player-shot", file = "bullet.png", width = 8, height = 3 },
    new { id = "enemy-shot", file = "enemy_shot.png", width = 6, height = 6 },
    new { id = "capsule", file = "capsule.png", width = 10, height = 8 } })
{
    string sourcePath = "Assets/Art/Sprites/" + spec.file;
    var source = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
    if (!UnityEngine.ImageConversion.LoadImage(source, System.IO.File.ReadAllBytes(sourcePath))
        || source.width != spec.width || source.height != spec.height)
        throw new System.InvalidOperationException("Unexpected source canvas: " + sourcePath);
    var sourcePixels = source.GetPixels32();
    var padded = new UnityEngine.Color32[16 * 16];
    var mask = new UnityEngine.Color32[16 * 16];
    int x0 = (16 - source.width) / 2, y0 = (16 - source.height) / 2;
    for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
    {
        bool inside = x >= x0 && x < x0 + source.width && y >= y0 && y < y0 + source.height;
        if (inside) padded[y * 16 + x] = sourcePixels[(y - y0) * source.width + x - x0];
        mask[y * 16 + x] = inside ? new UnityEngine.Color32(255, 255, 255, 255) : new UnityEngine.Color32(0, 0, 0, 255);
    }
    void Save(string suffix, UnityEngine.Color32[] pixels)
    {
        var image = new UnityEngine.Texture2D(16, 16, UnityEngine.TextureFormat.RGBA32, false);
        image.SetPixels32(pixels); image.Apply();
        System.IO.File.WriteAllBytes(outputRoot + "/" + spec.id + suffix + ".png", UnityEngine.ImageConversion.EncodeToPNG(image));
        UnityEngine.Object.DestroyImmediate(image);
    }
    Save("-input", padded); Save("-mask", mask);
    records.Add(new { spec.id, source = sourcePath, textureSize = new[] { 16, 16 },
        spriteRectBottomLeft = new[] { x0, y0, spec.width, spec.height },
        pivot = new[] { .5f, .5f }, ppu = 16, sourcePixelsChanged = false });
    UnityEngine.Object.DestroyImmediate(source);
}
System.IO.File.WriteAllText(outputRoot + "/inputs.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    kind = "PixelLab mask/padded reference controls; not production artwork",
    generatedBy = "Tools/QaHarness/prepare_sfc_combat_controls.eval.cs", sources = records,
    productionAssetsWritten = false, scenesSaved = false
}, Newtonsoft.Json.Formatting.Indented));
return new { outputRoot, count = records.Count, productionAssetsWritten = false };

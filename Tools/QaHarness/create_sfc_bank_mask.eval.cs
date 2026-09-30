// Author an API control mask, not a sprite edit. The source PNG is never modified.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires a disposable batch Editor outside Play Mode.");
const int width = 48, height = 30;
var mask = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGB24, false);
int editablePixels = 0;
for (int y = 0; y < height; y++)
for (int x = 0; x < width; x++)
{
    int topY = height - 1 - y;
    bool wing = x >= 9 && x < 27 && (topY >= 1 && topY < 10 || topY >= 17 && topY < 27);
    mask.SetPixel(x, y, wing ? UnityEngine.Color.white : UnityEngine.Color.black);
    if (wing) editablePixels++;
}
mask.Apply();
string path = "ArtRevamp/SFC-20260930/banking/control/wing-mask.png";
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(mask));
var source = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
UnityEngine.ImageConversion.LoadImage(source, System.IO.File.ReadAllBytes("Assets/Art/Sprites/sfc_player_ship.png"));
int silhouettePixels = 0;
for (int y = 0; y < height; y++)
for (int x = 0; x < width; x++)
{
    int topY = height - 1 - y;
    bool wingArea = x >= 9 && x < 27 && (topY >= 1 && topY < 10 || topY >= 17 && topY < 25);
    bool nearWing = false;
    if (wingArea)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height
                && source.GetPixel(x + dx, y + dy).a > 0) nearWing = true;
    mask.SetPixel(x, y, nearWing ? UnityEngine.Color.white : UnityEngine.Color.black);
    if (nearWing) silhouettePixels++;
}
mask.Apply();
string silhouettePath = "ArtRevamp/SFC-20260930/banking/control/wing-silhouette-mask.png";
System.IO.File.WriteAllBytes(silhouettePath, UnityEngine.ImageConversion.EncodeToPNG(mask));
var flameColors = new System.Collections.Generic.HashSet<uint> { 0xe14312, 0xff9d1c, 0xffeb5d, 0xfffb9a, 0xfbefb5 };
int flamePixels = 0;
for (int y = 0; y < height; y++)
for (int x = 0; x < width; x++)
{
    int topY = height - 1 - y;
    UnityEngine.Color32 color = source.GetPixel(x, y);
    uint rgb = ((uint)color.r << 16) | ((uint)color.g << 8) | color.b;
    bool flame = x < 9 && topY >= 6 && topY < 18 && (color.a == 0 || flameColors.Contains(rgb));
    mask.SetPixel(x, y, flame ? UnityEngine.Color.white : UnityEngine.Color.black);
    if (flame) flamePixels++;
}
mask.Apply();
string flamePath = "ArtRevamp/SFC-20260930/banking/control/engine-mask.png";
System.IO.File.WriteAllBytes(flamePath, UnityEngine.ImageConversion.EncodeToPNG(mask));
for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
{
    int topY = height - 1 - y;
    UnityEngine.Color32 color = source.GetPixel(x, y);
    uint rgb = ((uint)color.r << 16) | ((uint)color.g << 8) | color.b;
    bool flame = x < 9 && topY >= 6 && topY < 18 && color.a == 255 && flameColors.Contains(rgb);
    mask.SetPixel(x, y, flame ? UnityEngine.Color.white : UnityEngine.Color.black);
}
mask.Apply();
string tightFlamePath = "ArtRevamp/SFC-20260930/banking/control/engine-tight-mask.png";
System.IO.File.WriteAllBytes(tightFlamePath, UnityEngine.ImageConversion.EncodeToPNG(mask));
var palette = new UnityEngine.Texture2D(20, 20, UnityEngine.TextureFormat.RGB24, false);
uint[] paletteColors = { 0xe14312, 0xff9d1c, 0xffeb5d, 0xfffb9a, 0xfbefb5 };
for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++)
{
    uint color = paletteColors[x / 4];
    palette.SetPixel(x, y, new UnityEngine.Color32((byte)(color >> 16), (byte)(color >> 8), (byte)color, 255));
}
palette.Apply();
string palettePath = "ArtRevamp/SFC-20260930/banking/control/flame-palette.png";
System.IO.File.WriteAllBytes(palettePath, UnityEngine.ImageConversion.EncodeToPNG(palette));
UnityEngine.Object.DestroyImmediate(palette);
UnityEngine.Object.DestroyImmediate(source);
UnityEngine.Object.DestroyImmediate(mask);
return new { path, silhouettePath, flamePath, tightFlamePath, palettePath, width, height, editablePixels, silhouettePixels, flamePixels, sourceImageChanged = false };

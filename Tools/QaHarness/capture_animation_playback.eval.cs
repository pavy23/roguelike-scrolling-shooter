// Same adopted frames, sampled through the real BattleDirector; no saved scene or gameplay writes.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Requires batch Editor outside Play Mode.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("GPU required.");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
var privateFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var juice = (Shmup.Presentation.Battle.JuiceDirector)director.GetType().GetField("_juice", privateFlags).GetValue(director);
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
var run = new Shmup.Core.Simulation.RunManager(12345UL,
    new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
    data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
director.GetType().GetField("_sim", privateFlags).SetValue(director, run.Battle);
foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) root.SetActive(false);
var camera = new UnityEngine.GameObject("Animation comparison camera").AddComponent<UnityEngine.Camera>();
camera.orthographic = true; camera.orthographicSize = 5.0625f;
camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(.025f, .04f, .07f);
camera.allowHDR = false; camera.allowMSAA = false;
var font = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
void Label(string text, float x, float y, float size)
{
    var go = new UnityEngine.GameObject(text); go.transform.position = new UnityEngine.Vector3(x, y, 0);
    var label = go.AddComponent<UnityEngine.TextMesh>(); label.text = text; label.font = font;
    label.fontSize = 32; label.characterSize = size; label.anchor = UnityEngine.TextAnchor.MiddleLeft;
    label.color = new UnityEngine.Color(.7f, .82f, 1f);
    go.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = font.material;
}
Label("IDLE ANIMATION / SAME ART / 125ms SAMPLES", -8.5f, 4.65f, .065f);
var rows = new[] { "echo_wisp", "echo_wisp", "void_moth", "void_moth" };
var labels = new[] { "ECHO / BEFORE", "ECHO / AFTER", "MOTH / BEFORE (REDUCED)", "MOTH / AFTER (REDUCED)" };
int[] ticks = { 0, 8, 15, 23, 30, 38, 45, 53 };
for (int row = 0; row < rows.Length; row++)
{
    float y = 2.85f - row * 2.05f;
    Label(labels[row], -8.5f, y + .85f, .048f);
    juice.GetType().GetField("_flashReduced", privateFlags).SetValue(juice, row >= 2);
    for (int column = 0; column < ticks.Length; column++)
    {
        int tick = ticks[column];
        run.Battle.GetType().GetProperty("Tick").SetValue(run.Battle, tick);
        var go = new UnityEngine.GameObject(labels[row] + column);
        go.transform.position = new UnityEngine.Vector3(-7.35f + column * 2.1f, y, 0);
        var renderer = go.AddComponent<UnityEngine.SpriteRenderer>();
        if (row % 2 == 0)
        {
            int frame = (int)System.Math.Floor(tick * 8.0 / 60) % 5;
            renderer.sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(
                "Assets/Art/Sprites/anim_" + rows[row] + "_" + frame.ToString("D2") + ".png");
        }
        else
            director.GetType().GetMethod("ApplyIdleAnimation", privateFlags)
                .Invoke(director, new object[] { renderer, rows[row], 0, false });
    }
}
var target = UnityEngine.RenderTexture.GetTemporary(1440, 810, 24);
var previous = UnityEngine.RenderTexture.active;
camera.targetTexture = target; camera.Render(); UnityEngine.RenderTexture.active = target;
var image = new UnityEngine.Texture2D(1440, 810, UnityEngine.TextureFormat.RGB24, false);
image.ReadPixels(new UnityEngine.Rect(0, 0, 1440, 810), 0, 0); image.Apply();
string output = System.IO.Path.GetFullPath("out/revamp/animation-playback-comparison.png");
System.IO.File.WriteAllBytes(output, UnityEngine.ImageConversion.EncodeToPNG(image));
camera.targetTexture = null; UnityEngine.RenderTexture.active = previous;
UnityEngine.RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image);
return new { output, kind = "Actual BattleDirector frame sampling; before policy reconstructed at identical ticks; not gameplay", sceneSaved = false };

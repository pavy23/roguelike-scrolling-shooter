// Unity Pipeline eval_file statement block; run in a disposable batch Editor with GPU access.
// Initializes the actual title UI without entering Play Mode. No scene is saved.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Title capture requires a batch Editor outside Play Mode.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("Title capture requires a graphics device; omit -nographics.");
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Title.unity");
UnityEngine.AudioListener.volume = 0f;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var title = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.TitleScreen>();
var hangar = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.HangarScreen>();
if (title == null || hangar == null) throw new System.InvalidOperationException("Title scene UI is missing.");
title.GetType().GetMethod("Start", flags).Invoke(title, null);
hangar.GetType().GetMethod("Start", flags).Invoke(hangar, null);
title.GetType().GetMethod("Update", flags).Invoke(title, null);
hangar.GetType().GetMethod("Update", flags).Invoke(hangar, null);
var camera = UnityEngine.Camera.main;
if (camera == null) throw new System.InvalidOperationException("Title camera is missing.");
var previousTarget = camera.targetTexture;
var previousActive = UnityEngine.RenderTexture.active;
var target = UnityEngine.RenderTexture.GetTemporary(1280, 720, 24);
UnityEngine.Texture2D image = null;
try
{
    camera.targetTexture = target;
    // Camera rendering does not include overlay canvases. This conversion is in memory only.
    foreach (var canvas in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>())
    {
        canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler != null) scaler.scaleFactor = 2f;
        canvas.scaleFactor = 2f;
    }
    UnityEngine.Canvas.ForceUpdateCanvases();
    camera.Render();
    UnityEngine.RenderTexture.active = target;
    image = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGB24, false);
    image.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0);
    image.Apply();
    var png = UnityEngine.ImageConversion.EncodeToPNG(image);
    var path = System.IO.Path.GetFullPath("out/revamp/title-ui-review.png");
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
    System.IO.File.WriteAllBytes(path, png);
    return new { path, width = 1280, height = 720, bytes = png.Length,
        kind = "Editor offscreen render of current Title scene and runtime UI; not a playthrough" };
}
finally
{
    if (image != null) UnityEngine.Object.DestroyImmediate(image);
    camera.targetTexture = previousTarget;
    UnityEngine.RenderTexture.active = previousActive;
    UnityEngine.RenderTexture.ReleaseTemporary(target);
}

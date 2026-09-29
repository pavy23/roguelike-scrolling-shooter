// Real title/pause UI in a disposable GPU batch Editor; no Play Mode or scene save.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Audio UI capture requires batch Editor outside Play Mode.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("A graphics device is required; omit -nographics.");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
bool touch = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_TOUCH") == "1";
int scale = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_SCALE") == "1" ? 1 : 2;
string sceneName = System.Environment.GetEnvironmentVariable("RSS_CAPTURE_AUDIO_SCENE") ?? "Title";
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/" + sceneName + ".unity");
Shmup.Presentation.Battle.UiPlatform.ForceTouch = touch;
string[] keys = { "rss.volume", "rss.audio.music", "rss.audio.effects", "rss.audio.ui" };
var had = new bool[4];
var saved = new float[4];
for (int i = 0; i < keys.Length; i++) { had[i] = UnityEngine.PlayerPrefs.HasKey(keys[i]); saved[i] = UnityEngine.PlayerPrefs.GetFloat(keys[i]); }
float previousTime = UnityEngine.Time.timeScale;
bool previousPause = UnityEngine.AudioListener.pause;
var camera = UnityEngine.Camera.main;
var previousTarget = camera.targetTexture;
var previousActive = UnityEngine.RenderTexture.active;
var target = UnityEngine.RenderTexture.GetTemporary(640 * scale, 360 * scale, 24);
var paths = new System.Collections.Generic.List<string>();
try
{
    // Deliberate display fixture. Restore the user's preferences in finally.
    Shmup.Presentation.Battle.AudioPreferences.Set(Shmup.Presentation.Battle.AudioChannel.Master, 0.8f);
    Shmup.Presentation.Battle.AudioPreferences.Set(Shmup.Presentation.Battle.AudioChannel.Music, 0f);
    Shmup.Presentation.Battle.AudioPreferences.Set(Shmup.Presentation.Battle.AudioChannel.Effects, 1f);
    Shmup.Presentation.Battle.AudioPreferences.Set(Shmup.Presentation.Battle.AudioChannel.Interface, 0.5f);
    UnityEngine.MonoBehaviour owner;
    if (sceneName == "Title")
    {
        var title = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.TitleScreen>();
        var hangar = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.HangarScreen>();
        title.GetType().GetMethod("Start", flags).Invoke(title, null);
        hangar.GetType().GetMethod("Start", flags).Invoke(hangar, null);
        title.GetType().GetMethod("Update", flags).Invoke(title, null);
        hangar.GetType().GetMethod("Update", flags).Invoke(hangar, null);
        owner = title;
    }
    else
    {
        var pause = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.PauseScreen>();
        pause.GetType().GetMethod("Start", flags).Invoke(pause, null);
        pause.GetType().GetMethod("SetPaused", flags).Invoke(pause, new object[] { true });
        owner = pause;
    }
    var panel = (Shmup.Presentation.Battle.AudioSettingsPanel)owner.GetType().GetField("_audioSettings", flags).GetValue(owner);
    camera.targetTexture = target;
    foreach (bool open in new[] { false, true })
    {
        if (open) panel.Open();
        foreach (var canvas in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>())
        {
            canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null) scaler.scaleFactor = scale;
            canvas.scaleFactor = scale;
        }
        UnityEngine.Canvas.ForceUpdateCanvases();
        camera.Render();
        UnityEngine.RenderTexture.active = target;
        var image = new UnityEngine.Texture2D(640 * scale, 360 * scale, UnityEngine.TextureFormat.RGB24, false);
        try
        {
            image.ReadPixels(new UnityEngine.Rect(0, 0, 640 * scale, 360 * scale), 0, 0);
            image.Apply();
            string suffix = (touch ? "-touch" : "") + (scale == 1 ? "-640" : "");
            string path = System.IO.Path.GetFullPath("out/revamp/audio-" + sceneName.ToLowerInvariant()
                + (open ? "-settings" : "-entry") + suffix + ".png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(image));
            paths.Add(path);
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    return new { paths, kind = "Editor UI render with injected audio preferences; no playback/listening validation" };
}
finally
{
    for (int i = 0; i < keys.Length; i++)
        if (had[i]) UnityEngine.PlayerPrefs.SetFloat(keys[i], saved[i]); else UnityEngine.PlayerPrefs.DeleteKey(keys[i]);
    Shmup.Presentation.Battle.AudioPreferences.Reload();
    UnityEngine.Time.timeScale = previousTime;
    UnityEngine.AudioListener.pause = previousPause;
    camera.targetTexture = previousTarget;
    UnityEngine.RenderTexture.active = previousActive;
    UnityEngine.RenderTexture.ReleaseTemporary(target);
}

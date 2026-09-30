// Actual scene/UI with injected encounter display states. No Play Mode, save writes or scene save.
if (!UnityEngine.Application.isBatchMode || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Boss intro capture requires a disposable batch Editor.");
if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
    throw new System.InvalidOperationException("A graphics device is required; omit -nographics.");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var paths = new System.Collections.Generic.List<string>();
var data = Shmup.Core.Content.GameDataParser.Parse(
    System.IO.File.ReadAllText("GameData/enemies.json"), System.IO.File.ReadAllText("GameData/weapons.json"),
    System.IO.File.ReadAllText("GameData/waves.json"), System.IO.File.ReadAllText("GameData/rewards.json"),
    System.IO.File.ReadAllText("GameData/ships.json"), System.IO.File.ReadAllText("GameData/scoring.json"));
bool oldTouch = Shmup.Presentation.Battle.UiPlatform.ForceTouch;
float oldVolume = UnityEngine.AudioListener.volume;
try
{
    UnityEngine.AudioListener.volume = 0f;
    Shmup.Presentation.Battle.UiPlatform.ForceTouch = true;
    foreach (int scale in new[] { 1, 2 })
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
        var run = new Shmup.Core.Simulation.RunManager(12345UL,
            new Shmup.Core.Generation.SegmentStageGenerator(data.StageGeneration), data.CreateBattleSimConfig(),
            data.BattleContent, data.CreatePowerUpGauge(data.DefaultShip), data.Rewards, data.DefaultShip);
        run.GetType().GetProperty("IsBiomeBoss").SetValue(run, true);
        var director = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BattleDirector>();
        director.GetType().GetField("_run", flags).SetValue(director, run);
        director.GetType().GetField("_sim", flags).SetValue(director, run.Battle);
        var backgrounds = (UnityEngine.GameObject[])director.GetType().GetField("_themeBackgrounds", flags).GetValue(director);
        for (int i = 0; i < backgrounds.Length; i++) backgrounds[i].SetActive(i == 0);
        var boss = (UnityEngine.SpriteRenderer)director.GetType().GetField("_bossRenderer", flags).GetValue(director);
        director.GetType().GetMethod("ApplyBossSprite", flags).Invoke(director, null);
        boss.gameObject.SetActive(true);
        boss.enabled = true;
        boss.transform.position = new UnityEngine.Vector3(11f, 0f, 0f);
        ((UnityEngine.GameObject)director.GetType().GetField("_bossHpRoot", flags).GetValue(director)).SetActive(true);
        var views = new System.Collections.Generic.List<UnityEngine.MonoBehaviour>();
        foreach (string name in new[] { "TouchControls", "PowerUpHudView", "ScoreHud", "ProgressHud", "BombButton", "PauseScreen", "BossIntro" })
            foreach (var view in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>())
            {
                if (view.GetType().Name != name) continue;
                if (name == "TouchControls") view.GetType().GetMethod("Awake", flags).Invoke(view, null);
                view.GetType().GetMethod("Start", flags)?.Invoke(view, null);
                if (name == "ProgressHud") view.GetType().GetField("_bannerBiome", flags).SetValue(view, director.BiomeIndex);
                views.Add(view);
            }
        var intro = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.BossIntro>();
        var juice = UnityEngine.Object.FindAnyObjectByType<Shmup.Presentation.Battle.JuiceDirector>();
        var font = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Font>("Assets/Fonts/Galmuri11-Bold.ttf");
        // Reconstruct only the old banner geometry from source eec91b9 for a reference.
        // This is not a historical playthrough or a runtime path shipped in the game.
        var legacy = Shmup.Presentation.Battle.UiKit.CreateCanvas("LegacyBannerReference", 60);
        var band = new UnityEngine.GameObject("LegacyBand", typeof(UnityEngine.UI.Image));
        band.transform.SetParent(legacy.transform, false);
        var bandImage = band.GetComponent<UnityEngine.UI.Image>();
        bandImage.color = new UnityEngine.Color(.6f, .05f, .05f, .3f);
        bandImage.raycastTarget = false;
        var bandRect = bandImage.rectTransform;
        bandRect.anchorMin = new UnityEngine.Vector2(0f, .5f);
        bandRect.anchorMax = new UnityEngine.Vector2(1f, .5f);
        bandRect.sizeDelta = new UnityEngine.Vector2(0f, 56f);
        Shmup.Presentation.Battle.UiKit.CreateTextStretch(bandRect, font, Shmup.Presentation.Battle.UiText.BossWarning,
            26, Shmup.Presentation.Battle.UiKit.TextDanger, UnityEngine.TextAnchor.MiddleCenter, 0f, "Warning");
        var camera = UnityEngine.Camera.main;
        var oldTarget = camera.targetTexture;
        var oldActive = UnityEngine.RenderTexture.active;
        var target = UnityEngine.RenderTexture.GetTemporary(640 * scale, 360 * scale, 24);
        try
        {
            camera.targetTexture = target;
            foreach (string state in new[] { "legacy-reference", "stage-boss", "hidden-boss", "form-transition", "second-form", "reduced-flash" })
            {
                legacy.gameObject.SetActive(state == "legacy-reference");
                var kind = state == "second-form" ? Shmup.Presentation.Battle.BossIntroKind.SecondForm
                    : state == "form-transition" ? Shmup.Presentation.Battle.BossIntroKind.FormTransition
                    : state == "hidden-boss" ? Shmup.Presentation.Battle.BossIntroKind.HiddenBoss
                    : Shmup.Presentation.Battle.BossIntroKind.StageBoss;
                juice.GetType().GetField("_flashReduced", flags).SetValue(juice, state == "reduced-flash");
                intro.Trigger(kind);
                foreach (var view in views) if (view != intro) view.GetType().GetMethod("Update", flags)?.Invoke(view, null);
                intro.GetType().GetField("_age", flags).SetValue(intro, state == "legacy-reference" ? float.MaxValue : .25f);
                foreach (var view in views) view.GetType().GetMethod("LateUpdate", flags)?.Invoke(view, null);
                foreach (var layer in UnityEngine.Object.FindObjectsByType<Shmup.Presentation.Battle.BattleHudVisibility>())
                    layer.GetType().GetMethod("LateUpdate", flags).Invoke(layer, null);
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(UnityEngine.FindObjectsInactive.Include))
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
                    string path = System.IO.Path.GetFullPath("out/revamp/boss-intro-" + state + "-" + 640 * scale + ".png");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(image));
                    paths.Add(path);
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
        }
        finally
        {
            camera.targetTexture = oldTarget;
            UnityEngine.RenderTexture.active = oldActive;
            UnityEngine.RenderTexture.ReleaseTemporary(target);
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();
        }
    }
    return new { paths, kind = "Editor encounter UI fixtures. Legacy geometry reconstructed from eec91b9; boss art is an illustrative scene fixture, not a live encounter." };
}
finally
{
    Shmup.Presentation.Battle.UiPlatform.ForceTouch = oldTouch;
    UnityEngine.AudioListener.volume = oldVolume;
}

using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Shmup.EditorTools
{
    /// <summary>
    /// 원격 플레이용 모바일/웹 빌드. 배치모드에서 -executeMethod로 호출한다.
    ///
    /// APK:   Unity.exe -batchmode -projectPath . -executeMethod Shmup.EditorTools.MobileBuilder.BuildAndroid -quit
    /// WebGL: Unity.exe -batchmode -projectPath . -executeMethod Shmup.EditorTools.MobileBuilder.BuildWebGl -quit
    /// </summary>
    public static class MobileBuilder
    {
        static readonly string[] Scenes =
        {
            "Assets/Scenes/Title.unity",
            "Assets/Scenes/Battle.unity"
        };

        public static void BuildAndroid()
        {
            // 세로 화면이 아니라 가로 고정 (횡스크롤 슈팅)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.companyName = "pavy";
            PlayerSettings.productName = "Roguelike Scrolling Shooter";
            PlayerSettings.SetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android, "com.pavy.rss");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // 디버그 서명으로 충분 (사이드로드 설치용)
            PlayerSettings.Android.useCustomKeystore = false;
            EditorUserBuildSettings.buildAppBundle = false;

            string outDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Builds", "Mobile"));
            Directory.CreateDirectory(outDir);
            string apk = Path.Combine(outDir, "RSS.apk");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = apk,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            Report(report, apk);
        }

        public static void BuildWebGl()
        {
            // PROJECT: 접두는 Assets/WebGLTemplates/ 하위를 가리킨다 — 없으면 빌드가 실패한다.
            // 내장 템플릿을 쓴다 (APPLICATION:).
            // GameData 동기화는 여기서 부르지 않는다 — GameDataSyncOnBuild
            // (IPreprocessBuildWithReport)가 모든 빌드 타깃에서 자동으로 돈다.
            PlayerSettings.WebGL.template = "APPLICATION:Default";
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.WebGL.memorySize = 512;
            PlayerSettings.runInBackground = false;
            // 정적 호스팅은 Content-Encoding 헤더를 붙일 수 없으므로, gzip으로 줄이고
            // decompressionFallback으로 로더가 직접 풀게 한다 (76MB -> 약 20MB).
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            // 모바일 데이터 절약을 위해 코드 최적화를 크기 우선으로
            PlayerSettings.SetIl2CppCodeGeneration(
                UnityEditor.Build.NamedBuildTarget.WebGL,
                UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);

            string outDir = Path.GetFullPath(
                System.Environment.GetEnvironmentVariable("RSS_WEB_BUILD_OUTPUT")
                ?? Path.Combine(Application.dataPath, "..", "Builds", "Web"));
            Directory.CreateDirectory(outDir);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            Report(report, outDir);
        }

        static void Report(BuildReport report, string path)
        {
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[MobileBuilder] 성공: {path} " +
                          $"({summary.totalSize / 1024 / 1024}MB, {summary.totalTime})");
            }
            else
            {
                Debug.LogError($"[MobileBuilder] 실패: {summary.result} " +
                               $"(errors={summary.totalErrors})");
                EditorApplication.Exit(1);
            }
        }
    }
}

using System;
using System.IO;
using Shmup.Core;
using Shmup.Core.Simulation;
using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 런 중단 저장 파일 (REQ-017). SafeFile로 백업 승격 방식 쓰기를 하고,
    /// 손상/버전 불일치는 백업 폴백 후에도 실패하면 null로 처리한다.
    /// 삭제는 복원이 성공한 뒤에만 하도록 호출부(TitleScreen)가 순서를 지킨다.
    /// </summary>
    public static class RunSave
    {
        static string FilePath => Path.Combine(Application.persistentDataPath, "run.json");

        public static void Save(RunSuspendData data)
        {
            if (data == null) return;
            SafeFile.Write(FilePath, JsonUtility.ToJson(data, prettyPrint: true));
        }

        public static RunSuspendData TryLoad()
        {
            string text = SafeFile.Read(FilePath, IsUsable);
            if (text == null) return null;
            try
            {
                return JsonUtility.FromJson<RunSuspendData>(text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RunSave] 파싱 실패({e.GetType().Name}) — 이어하기 무시. {e.Message}");
                return null;
            }
        }

        /// <summary>파싱 가능하고 이 빌드가 복원할 수 있는 스키마인지 확인한다.</summary>
        static bool IsUsable(string text)
        {
            try
            {
                var data = JsonUtility.FromJson<RunSuspendData>(text);
                // Use the same compatibility boundary as resume, including
                // retired deterministic layouts and checksum validation.
                SaveDataIntegrity.MigrateAndValidate(data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Delete() => SafeFile.Delete(FilePath);
    }
}

using System;
using System.IO;
using Shmup.Core;
using Shmup.Core.Simulation;
using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 마지막 런 리플레이 파일 (REQ-018/019 Presentation 몫).
    /// 시드+함선+입력 기록이면 결정론 코어가 런 전체를 재현한다.
    /// </summary>
    [Serializable]
    public sealed class ReplayFileData
    {
        public long seed;
        public string shipId;
        public long finalScore;
        public int difficultyNumerator = 1;
        public int difficultyDenominator = 1;
        public int[] rewardChoices;
        public int[] routeChoices;
        public int[] contractChoices;   // 섹터 계약 선택 (REQ-070)
        public InputRecordingData recording;
    }

    public static class ReplaySave
    {
        static string FilePath => Path.Combine(Application.persistentDataPath, "replay_last.json");

        public static void Save(ReplayFileData data)
        {
            if (data == null || data.recording == null) return;
            SafeFile.Write(FilePath, JsonUtility.ToJson(data));
        }

        public static ReplayFileData TryLoad()
        {
            string text = SafeFile.Read(FilePath, IsUsable);
            if (text == null) return null;
            try
            {
                return JsonUtility.FromJson<ReplayFileData>(text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ReplaySave] 파싱 실패({e.GetType().Name}) — 리플레이 무시. {e.Message}");
                return null;
            }
        }

        static bool IsUsable(string text)
        {
            try
            {
                var data = JsonUtility.FromJson<ReplayFileData>(text);
                if (data == null || data.recording == null) return false;
                SaveDataIntegrity.MigrateAndValidate(data.recording);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

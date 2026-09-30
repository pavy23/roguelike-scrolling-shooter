using Shmup.Core.Simulation;
using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// BGM 레이어 (M4 완성): 테마별 루프 + 보스 트랙 전환 + 덕킹 + 징글.
    /// director 상태를 폴링만 한다 (순수 표현). 트랙은 Tools/SfxGen/bgmgen.py
    /// 프리셋 산출물 (시드 커밋으로 재현).
    /// - 보스전 진입/이탈: bgm_boss로 크로스 전환 (덕킹으로 이음새 완화)
    /// - 스테이지 클리어(AwaitingReward): 루프 덕킹 + 클리어 징글 1회
    /// - 게임오버(IsRunOver): 루프 정지 + 게임오버 스팅어 1회
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BgmPlayer : MonoBehaviour
    {
        [SerializeField] BattleDirector _director;
        [SerializeField] AudioSource _source;
        [SerializeField] string[] _themeIds;
        [SerializeField] AudioClip[] _clips;
        [SerializeField] AudioClip _bossClip;
        [Tooltip("스테이지 보스 전용 — 느리고 웅장하게")]
        [SerializeField] AudioClip _stageBossClip;
        [Tooltip("히든(거대) 보스 전용 — 가장 무겁게")]
        [SerializeField] AudioClip _hiddenBossClip;
        [SerializeField] AudioClip _clearJingle;
        [SerializeField] AudioClip _gameOverJingle;

        const float DuckLerpSpeed = 6f;

        float _baseVolume = 1f;   // 빌더가 지정한 믹스 레벨을 기준으로 덕킹/복원
        AudioChannelSource _channel;
        float _duckGain = 1f;
        string _activeThemeId;
        bool _bossTrackActive;
        RunStageSection _bossTrackSection;
        bool _bossTrackSecondForm;
        bool _wasAwaitingReward;
        int _jingledRunNumber = int.MinValue;
        float _duckUntilRealtime;

        void Awake()
        {
            if (_source != null)
            {
                _channel = _source.GetComponent<AudioChannelSource>();
                if (_channel == null)
                {
                    _channel = _source.gameObject.AddComponent<AudioChannelSource>();
                    _channel.Configure(AudioChannel.Music);
                }
                _baseVolume = _channel.BaseVolume;
            }
        }

        void Update()
        {
            if (_director == null || _source == null) return;

            // 격파·함체 붕괴 중에는 현재 곡을 이어간다. 결과/보상 화면이 열릴 때
            // 클리어 징글과 다음 트랙으로 전환한다. 플레이어 사망은 지연하지 않는다.
            if (_director.BossDeathCinematicActive && !_director.IsRunOver) return;

            // 런 종료: 루프 멈추고 스팅어 1회 (런당 1번). 완주는 승리 징글로 구분한다.
            if (_director.IsRunFinished)
            {
                if (_jingledRunNumber != _director.RunNumber)
                {
                    _jingledRunNumber = _director.RunNumber;
                    _source.Stop();
                    var sting = _director.IsRunCleared ? _clearJingle : _gameOverJingle;
                    if (sting != null)
                        _source.PlayOneShot(sting, 0.95f);
                }
                return;
            }

            // 재출격 직후 루프 복구
            if (!_source.isPlaying && _source.clip != null && Time.timeScale > 0f)
                _source.Play();

            // 스테이지 클리어: 덕킹 + 클리어 징글 (AwaitingReward 상승 에지)
            bool awaiting = _director.AwaitingReward;
            if (awaiting && !_wasAwaitingReward)
            {
                Duck(2.2f);
                if (_clearJingle != null)
                    _source.PlayOneShot(_clearJingle, 0.9f);
            }
            _wasAwaitingReward = awaiting;

            // 보스 트랙 전환 (진입: 덕킹 후 교체, 이탈: 테마 트랙 복귀)
            // 보스 종류마다 다른 곡을 쓴다 (사람 지시 2026-08-03: "각 스테이지
            // 중간보스랑 최종보스 BGM이 같으니까 흥미가 떨어져"). 중간보스는 스테이지마다
            // 거치는 통과 의례고, 스테이지 보스와 히든 보스는 그 스테이지의 끝이다 —
            // 같은 곡이 흐르면 무게 차이가 사라진다.
            bool bossActive = _director.BossActive;
            var section = _director.StageSection;
            // 형태 전환도 트랙 전환 조건이다 — 이걸 빼면 코어가 나와도 곡이
            // 그대로 흘러 장면이 바뀐 것을 귀가 모른다.
            bool secondForm = _director.IsBossSecondForm;
            if (bossActive != _bossTrackActive
                || (bossActive && section != _bossTrackSection)
                || (bossActive && secondForm != _bossTrackSecondForm))
            {
                _bossTrackActive = bossActive;
                _bossTrackSection = section;
                _bossTrackSecondForm = secondForm;
                Duck(0.5f);
                SwapClip(bossActive
                    ? (BossClipFor(section, secondForm)
                        ?? FindClip(_director.CurrentThemeId))
                    : FindClip(_director.CurrentThemeId));
            }
            else if (!bossActive)
            {
                // 일반 구간: 테마 변화 폴링
                string themeId = _director.CurrentThemeId;
                if (!string.IsNullOrEmpty(themeId) && themeId != _activeThemeId)
                {
                    _activeThemeId = themeId;
                    SwapClip(FindClip(themeId));
                }
            }

            // 덕킹 볼륨 복원
            float targetGain = Time.realtimeSinceStartup < _duckUntilRealtime ? 0.4f : 1f;
            _duckGain = Mathf.MoveTowards(_duckGain, targetGain,
                DuckLerpSpeed * Time.unscaledDeltaTime / Mathf.Max(_baseVolume, 0.001f));
            if (_channel != null) _channel.SetMixGain(_duckGain);
        }

        void Duck(float seconds)
        {
            _duckUntilRealtime = Mathf.Max(
                _duckUntilRealtime, Time.realtimeSinceStartup + seconds);
        }

        /// <summary>구간별 보스 곡. 전용 곡이 없으면 기존 전투곡으로 되돌아간다.</summary>
        AudioClip BossClipFor(RunStageSection section, bool secondForm)
        {
            switch (section)
            {
                // 히든 보스는 **처음부터 끝까지 히든곡**이다. 잠깐 일반 보스곡으로
                // 갈아타 봤는데(2026-08-05), 사람이 듣고 "페이즈4로 넘어갈 때 잠깐
                // 들리는 옛날 bgm을 그대로 유지해줘. 일반 보스 음악으로 넘어가지
                // 말고"라고 정했다. 형태가 바뀌어도 곡은 이어진다 — 같은 싸움이다.
                case RunStageSection.HiddenBoss:
                    return _hiddenBossClip != null ? _hiddenBossClip : _bossClip;
                case RunStageSection.StageBoss:
                    return _stageBossClip != null ? _stageBossClip : _bossClip;
                default:
                    return _bossClip;      // 중간보스는 기존 전투곡 그대로
            }
        }

        void SwapClip(AudioClip clip)
        {
            if (clip == null || _source.clip == clip) return;
            _source.clip = clip;
            _source.Play();
        }

        AudioClip FindClip(string themeId)
        {
            if (_themeIds == null || _clips == null) return null;
            int count = Mathf.Min(_themeIds.Length, _clips.Length);
            for (int i = 0; i < count; i++)
                if (string.Equals(_themeIds[i], themeId, System.StringComparison.Ordinal))
                    return _clips[i];
            return _clips.Length > 0 ? _clips[0] : null;
        }
    }
}

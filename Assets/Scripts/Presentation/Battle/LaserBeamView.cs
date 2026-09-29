using System.Collections.Generic;
using Shmup.Core.Simulation;
using UnityEngine;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 적·지형·보스가 쏘는 지속 레이저와 플레이어 빔을 그린다 (REQ-042).
    ///
    /// Core는 선분(시작·끝점)과 4단계 진행(Telegraph → Firing → Sustaining →
    /// Dissipating), 굵기 단계를 상태로 노출한다. 여기서는 그 상태를 흰 스프라이트
    /// 두 겹(외곽 + 코어)으로 표현한다 — 선분 렌더러를 쓰면 픽셀아트 화면에서 뜬다.
    ///
    /// **예고 단계를 확실히 다르게 보여야 한다.** 예고 없는 레이저는 불공정하고,
    /// 예고가 발사와 비슷해 보이면 예고가 있으나 마나다. 그래서 예고는 반투명하게
    /// 맥동하고, 발사는 굵고 밝게 나간다.
    ///
    /// **2026-08-02 전면 개선** ("레이저는 여전히 패턴에서도 잘 안 보이고, 갑자기
    /// 출현하는 문제가 여전해"). 세 가지를 고쳤다:
    ///
    /// 1. **스케일 버그.** px_white는 2px 스프라이트라 PPU 16에서 스케일 1이
    ///    0.125 월드 유닛이다. 예전 코드는 localScale에 월드 길이를 그대로 넣어
    ///    모든 레이저를 길이·두께 모두 1/8로 그렸다 — 최대 굵기 빔(16px)이 2px,
    ///    예고선(2px)은 0.25px로 사실상 안 보였다. 스프라이트의 실제 월드 크기로
    ///    나눠 준다.
    /// 2. **예고 가시성.** 알파 0.30 고정 가는 선 → 2.5Hz 맥동(0.25~0.70)에
    ///    발사 실폭에 가까운 띠 + 정확한 조준선 코어. 발사 0.2초 전부터 8Hz로
    ///    빨라져 "곧 쏜다"를 알린다.
    /// 3. **원점 표식.** 예고 내내 발사 원점에 차지 글로우를 띄운다. 어디서
    ///    나오는지 보이지 않으면 예고선을 봐도 피할 방향을 못 정한다.
    ///
    /// 예고 폭은 Core의 FullHalfWidth로 첫 사이클부터 표시한다. Firing 첫 틱부터
    /// 전장에 피해 판정이 있으므로 본체·코어도 즉시 전장을 덮는다. 원점에서 앞으로
    /// 뻗는 연출은 바깥 광채에만 적용해 위험 구간의 표시를 지연시키지 않는다.
    ///
    /// 순수 표현 — 판정은 전부 Core의 선분 대 원 정수 연산이 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LaserBeamView : MonoBehaviour
    {
        [SerializeField] BattleDirector _director;
        [SerializeField] Sprite _pixelSprite;

        [Tooltip("예고 중 발사 원점에 띄우는 차지 글로우. 비면 픽셀 스프라이트로 대체한다.")]
        [SerializeField] Sprite _glowSprite;

        /// <summary>
        /// 세로 가우시안 감쇠가 **알파에 구워진** 8×64 띠 (art-input/laser_soft.png).
        ///
        /// 사람 지시 2026-08-04로 후보 C를 채택했다. 예전에는 단색 사각형 두 겹이라
        /// 가장자리가 딱 떨어져 "색칠한 막대"로 읽혔다 — 상용 게임 레이저와의 차이는
        /// 기술이 아니라 **감쇠**다. 감쇠를 사각형 여러 겹으로 흉내 내면 겹마다
        /// 드로우콜이 붙고 경계가 계단으로 남으므로, 알파에 구워 한 겹으로 끝낸다.
        /// 가로로 아무리 늘려도 세로 단면은 그대로다.
        ///
        /// 없으면 픽셀 스프라이트로 폴백한다(예전 각진 모습).
        /// </summary>
        [SerializeField] Sprite _softSprite;

        /// <summary>총구·착탄 플레어 (art-input/laser_cap.png). 방사 감쇠 + 십자 스파이크.</summary>
        [SerializeField] Sprite _capSprite;

        [SerializeField] Transform _root;

        [Tooltip("동시에 그릴 수 있는 레이저 수. Core의 MaxLasers와 맞춘다.")]
        /// <summary>
        /// 동시에 그릴 수 있는 빔 수. **모자라면 넘치는 빔은 아예 안 그려진다** —
        /// 판정은 살아 있는데 화면에 없으니 "레이저가 중간에 끊긴다"로 보인다
        /// (사람 보고 2026-08-04).
        ///
        /// 8은 전함 포탑이 4문이던 시절 값이다. 지금은 포탑만 6문이고 여기에
        /// 레이저 잡몹(laser_sentry · prism_beamer) 여러 마리와 플레이어 레이저가
        /// 겹칠 수 있다. 렌더러는 풀이고 꺼져 있을 때는 비용이 없으므로 넉넉히
        /// 잡는 편이 맞다.
        /// </summary>
        [SerializeField] int _capacity = 24;

        // ── 색 ────────────────────────────────────────────────────────────────
        // 적 빔은 적색 계열, 아군(PRISM BEAM)은 시안 계열로 갈라 둔다. 화면에 둘이
        // 동시에 있을 때 "내가 쏜 것"과 "나를 노리는 것"을 색만으로 구분해야 한다.

        static readonly Color TelegraphBand = new Color(1f, 0.24f, 0.30f, 1f);
        static readonly Color TelegraphAim = new Color(1f, 0.82f, 0.78f, 1f);
        static readonly Color FiringBand = new Color(1f, 0.42f, 0.26f, 1f);
        static readonly Color FiringCore = new Color(1f, 1f, 0.96f, 1f);
        static readonly Color SustainBand = new Color(1f, 0.46f, 0.20f, 1f);
        static readonly Color SustainCore = new Color(1f, 0.96f, 0.86f, 1f);
        static readonly Color DissipateBand = new Color(1f, 0.38f, 0.38f, 1f);
        static readonly Color DissipateCore = new Color(1f, 0.80f, 0.76f, 1f);

        static readonly Color PlayerBand = new Color(0.16f, 0.86f, 1f, 1f);
        static readonly Color PlayerCore = new Color(0.86f, 1f, 1f, 1f);

        // ── 예고 맥동 ─────────────────────────────────────────────────────────

        /// <summary>평상시 예고 점멸 주기. 사인 맥동이라 사각 점멸보다 눈이 덜 피로하다.</summary>
        const float TelegraphPulseHz = 2.5f;

        /// <summary>발사 임박 구간의 점멸 주기 — "곧 쏜다"가 별도 신호로 읽혀야 한다.</summary>
        const float ImminentPulseHz = 8f;

        /// <summary>임박 판정 시간. Core의 PhaseTicksRemaining으로 잰다.</summary>
        const float ImminentSeconds = 0.2f;

        const float TelegraphAlphaMin = 0.25f;
        const float TelegraphAlphaMax = 0.70f;
        const float ImminentAlphaMin = 0.50f;
        const float ImminentAlphaMax = 1.00f;

        // ── 두께 (월드 유닛, PPU 16 → 1u = 16px) ──────────────────────────────

        /// <summary>
        /// 예고 띠 = 실제 빔 폭의 이 비율. **1.0이다** — 예고는 "여기까지 위험하다"를
        /// 말하는 것이므로 실제 폭과 같아야 한다.
        ///
        /// 예전에는 0.85로 살짝 좁혀 두었다. 얇은 빔에서는 티가 안 났지만, 3막
        /// 코어 빔이 반폭 5유닛으로 굵어지자 예고와 실제가 눈에 띄게 달라졌다 —
        /// 사람이 "서치레이저 / 실제레이저 크기가 다른거 수정해줘 (실제 레이저
        /// 기준)"라고 했다. 예고가 실제보다 좁으면 예고선 밖에 서 있다가 맞는다.
        /// </summary>
        const float TelegraphWidthFraction = 1.0f;

        /// <summary>예고 띠 최소 두께 (6px). 얇은 빔이라도 폰 화면에서 읽혀야 한다.</summary>
        const float MinTelegraphThickness = 0.375f;

        /// <summary>코어(중심 흰 선) 최소 두께 (2px).</summary>
        const float MinCoreThickness = 0.125f;

        /// <summary>Sustaining 코어가 외곽에서 차지하는 비율.</summary>
        const float CoreFraction = 0.42f;

        /// <summary>
        /// 발사 순간 바깥 광채가 원점에서 끝점까지 뻗는 시간. 본체·코어의 길이에는
        /// 영향을 주지 않는다.
        /// </summary>
        const float GrowSeconds = 0.18f;

        /// <summary>그로우 첫 프레임에도 선단이 보이게 하는 최소 길이 비율.</summary>
        const float MinGrowFraction = 0.06f;

        // ── 원점 글로우 ───────────────────────────────────────────────────────

        /// <summary>차지 글로우가 커지기 시작하는 시점 (발사까지 남은 시간).</summary>
        const float ChargeWindupSeconds = 0.8f;

        const float ChargeMinSize = 0.45f;
        const float ChargeMaxSize = 1.25f;

        /// <summary>발사 직후 원점 섬광이 남는 시간.</summary>
        const float MuzzleFlashSeconds = 0.14f;

        // 정렬: 보스(15)·보스 파츠(16)보다 앞. 보스가 자기 몸에서 쏘는 빔의 원점이
        // 몸통에 가려지면 "어디서 나오는지"가 안 보인다.
        const int OuterOrder = 16;
        const int BandOrder = 17;
        const int CoreOrder = 18;
        /// <summary>캡은 빔 위에 얹는다 — 끝점이 빔에 묻히면 터진 느낌이 죽는다.</summary>
        const int CapOrder = 19;

        /// <summary>바깥 광채는 본체보다 이만큼 넓다.</summary>
        const float OuterWidthScale = 2.6f;
        /// <summary>바깥 광채 최대 알파. 옅어야 광채이지 띠가 되면 안 된다.</summary>
        const float OuterAlphaScale = 0.34f;
        /// <summary>착탄 캡 크기 (본체 두께 대비).</summary>
        const float ImpactSizeScale = 2.2f;

        /// <summary>
        /// 총구 캡 크기 (본체 두께 대비). 1보다 커야 캡의 원호가 빔의 네모난
        /// 시작 단면을 덮어 둥글게 만든다.
        /// </summary>
        const float MuzzleCapWidthScale = 1.35f;

        /// <summary>가장 넓고 옅은 바깥 광채. 빔이 공기를 밀어내는 것처럼 보이게 한다.</summary>
        readonly List<SpriteRenderer> _outers = new List<SpriteRenderer>(8);
        readonly List<SpriteRenderer> _bands = new List<SpriteRenderer>(8);
        readonly List<SpriteRenderer> _cores = new List<SpriteRenderer>(8);
        readonly List<SpriteRenderer> _glows = new List<SpriteRenderer>(8);
        /// <summary>착탄점 스플래시. 빔이 "어디에 닿는지"를 말해 준다.</summary>
        readonly List<SpriteRenderer> _impacts = new List<SpriteRenderer>(8);

        // 이번 프레임에 살아 있는 레이저 id — 사라진 빔의 추적 기록을 걷어낸다.
        readonly HashSet<int> _seen = new HashSet<int>();

        // 빔별 추적 (id → 그로우 나이·점멸 위상). 동시 8줄이 상한이라 선형 탐색이
        // 사전보다 싸고, 매 프레임 할당이 없다.
        readonly List<int> _trackIds = new List<int>(8);
        readonly List<float> _growAges = new List<float>(8);
        readonly List<float> _blinkPhases = new List<float>(8);

        // 스프라이트 스케일 1이 만드는 월드 크기 — px_white는 2px/PPU16 = 0.125u다.
        float _unitX = 1f;
        float _unitY = 1f;
        float _glowUnit = 1f;
        /// <summary>감쇠 띠 스프라이트의 단위 크기. 없으면 픽셀 스프라이트 값을 쓴다.</summary>
        float _softUnitX = 1f;
        float _softUnitY = 1f;
        float _capUnit = 1f;

        void Start()
        {
            var parent = _root != null ? _root : transform;
            var glowSprite = _glowSprite != null ? _glowSprite : _pixelSprite;
            // 감쇠 띠·캡이 없으면 예전 각진 모습으로 폴백한다 — 아트가 빠져도
            // 레이저가 아예 안 보이는 것보다는 낫다.
            var beamSprite = _softSprite != null ? _softSprite : _pixelSprite;
            var capSprite = _capSprite != null ? _capSprite : glowSprite;

            if (_pixelSprite != null)
            {
                Vector3 size = _pixelSprite.bounds.size;
                if (size.x > 0.0001f) _unitX = size.x;
                if (size.y > 0.0001f) _unitY = size.y;
            }
            if (glowSprite != null)
            {
                float size = glowSprite.bounds.size.x;
                if (size > 0.0001f) _glowUnit = size;
            }

            if (beamSprite != null)
            {
                Vector3 size = beamSprite.bounds.size;
                if (size.x > 0.0001f) _softUnitX = size.x;
                if (size.y > 0.0001f) _softUnitY = size.y;
            }
            if (capSprite != null)
            {
                float size = capSprite.bounds.size.x;
                if (size > 0.0001f) _capUnit = size;
            }

            for (int i = 0; i < _capacity; i++)
            {
                _outers.Add(CreateRenderer(parent, $"Laser_{i:D2}_Outer", beamSprite, OuterOrder));
                _bands.Add(CreateRenderer(parent, $"Laser_{i:D2}_Band", beamSprite, BandOrder));
                _cores.Add(CreateRenderer(parent, $"Laser_{i:D2}_Core", beamSprite, CoreOrder));
                _glows.Add(CreateRenderer(parent, $"Laser_{i:D2}_Muzzle", capSprite, CapOrder));
                _impacts.Add(CreateRenderer(parent, $"Laser_{i:D2}_Impact", capSprite, CapOrder));
            }
        }

        static SpriteRenderer CreateRenderer(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.enabled = false;
            return renderer;
        }

        void LateUpdate()
        {
            if (_director == null || _bands.Count == 0) return;
            var lasers = _director.Lasers;
            _seen.Clear();

            int slot = 0;
            if (lasers != null)
            {
                for (int i = 0; i < lasers.Count; i++)
                {
                    var laser = lasers[i];
                    // 풀이 모자라 못 그린 빔도 살아 있는 것은 맞다 — 여기서 빠뜨리면
                    // 추적 기록이 지워졌다가 다음 프레임에 처음부터 다시 뻗는다.
                    _seen.Add(laser.Id);
                    if (slot >= _bands.Count) continue;
                    Draw(slot++, laser, Time.deltaTime);
                }
            }

            // 남은 슬롯은 끈다 (풀이므로 파괴하지 않는다).
            for (; slot < _bands.Count; slot++)
            {
                if (_outers[slot].enabled) _outers[slot].enabled = false;
                if (_bands[slot].enabled) _bands[slot].enabled = false;
                if (_cores[slot].enabled) _cores[slot].enabled = false;
                if (_glows[slot].enabled) _glows[slot].enabled = false;
                if (_impacts[slot].enabled) _impacts[slot].enabled = false;
            }

            ForgetDeadBeams();
        }

        /// <summary>사라진 빔의 추적 기록 정리 — 같은 슬롯을 다음 빔이 물려받아도 새로 뻗는다.</summary>
        void ForgetDeadBeams()
        {
            for (int i = _trackIds.Count - 1; i >= 0; i--)
            {
                if (_seen.Contains(_trackIds[i])) continue;
                _trackIds.RemoveAt(i);
                _growAges.RemoveAt(i);
                _blinkPhases.RemoveAt(i);
            }
        }

        int Track(int id)
        {
            int index = _trackIds.IndexOf(id);
            if (index >= 0) return index;
            _trackIds.Add(id);
            _growAges.Add(0f);
            // 위상을 흩뜨리지 않고 0에서 시작한다 — 여러 예고가 같이 맥동해야
            // "여러 줄이 동시에 온다"가 한눈에 읽힌다.
            _blinkPhases.Add(0f);
            return _trackIds.Count - 1;
        }

        /// <summary>
        /// 바깥 광채의 길이 비율 (0~1). Firing 구간에만 적용한다.
        /// </summary>
        float GrowFraction(int track, in LaserState laser, float deltaTime)
        {
            if (laser.Phase != LaserPhase.Firing) return 1f;

            float age = _growAges[track] + deltaTime;
            _growAges[track] = age;
            if (age >= GrowSeconds) return 1f;

            // 감속 곡선: 광채가 초반에 확 튀어나가고 끝에서 붙는다.
            float t = age / GrowSeconds;
            return Mathf.Max(1f - (1f - t) * (1f - t), MinGrowFraction);
        }

        /// <summary>
        /// 예고 맥동 값 (0~1). 주기를 위상 누적으로 굴려 임박 구간에서 주파수가
        /// 바뀌어도 밝기가 튀지 않는다.
        /// </summary>
        float Pulse(int track, float hz, float deltaTime)
        {
            float phase = Mathf.Repeat(_blinkPhases[track] + deltaTime * hz, 1f);
            _blinkPhases[track] = phase;
            return 0.5f - 0.5f * Mathf.Cos(phase * 2f * Mathf.PI);
        }

        void Draw(int slot, in LaserState laser, float deltaTime)
        {
            var outer = _outers[slot];
            var band = _bands[slot];
            var core = _cores[slot];
            var glow = _glows[slot];
            var impact = _impacts[slot];

            Vector3 start = SimView.ToWorld(laser.StartX, laser.StartY);
            Vector3 end = SimView.ToWorld(laser.EndX, laser.EndY);
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.0001f)
            {
                outer.enabled = false;
                band.enabled = false;
                core.enabled = false;
                glow.enabled = false;
                impact.enabled = false;
                return;
            }

            int track = Track(laser.Id);
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, 0f, angle);

            // Core가 준 반폭 = 지금 이 순간의 실제 히트박스 폭.
            float trueThickness = Mathf.Max(
                2f * laser.HalfWidth / SimSpace.SubUnitsPerWorldUnit, MinCoreThickness);
            float fullThickness = laser.SourceKind == LaserSourceKind.Player
                ? trueThickness
                : Mathf.Max(2f * laser.FullHalfWidth / SimSpace.SubUnitsPerWorldUnit, trueThickness);

            float grow = GrowFraction(track, laser, deltaTime);
            bool player = laser.SourceKind == LaserSourceKind.Player;
            bool reduced = _director != null && _director.FlashReduced;
            int order = player ? CombatReadability.FriendlyLaserOrder : CombatReadability.HostileLaserOrder;
            outer.sortingOrder = order;
            band.sortingOrder = order + 1;
            core.sortingOrder = order + 2;
            glow.sortingOrder = impact.sortingOrder = order + 3;

            float bandThickness, coreThickness;
            Color bandColor, coreColor;
            float chargeSize = 0f;
            float glowAlpha = 0f;

            switch (laser.Phase)
            {
                case LaserPhase.Telegraph:
                {
                    float toFire = laser.PhaseTicksRemaining / (float)SimSpace.TicksPerSecond;
                    bool imminent = toFire <= ImminentSeconds;
                    // Reduced-flash keeps the complete warning shape and a steady urgency level.
                    float pulse = reduced ? .5f : Pulse(track, imminent ? ImminentPulseHz : TelegraphPulseHz, deltaTime);
                    float alpha = imminent
                        ? Mathf.Lerp(ImminentAlphaMin, ImminentAlphaMax, pulse)
                        : Mathf.Lerp(TelegraphAlphaMin, TelegraphAlphaMax, pulse);

                    bandThickness = Mathf.Max(
                        fullThickness * TelegraphWidthFraction, MinTelegraphThickness);
                    bandColor = player ? PlayerBand : TelegraphBand;
                    bandColor.a = alpha;

                    // 정확한 조준선은 실폭 그대로 — 띠는 "이 정도가 위험 범위",
                    // 코어는 "선은 정확히 여기"를 각각 말한다.
                    coreThickness = trueThickness;
                    coreColor = player ? PlayerCore : TelegraphAim;
                    coreColor.a = Mathf.Min(1f, alpha * 1.35f);

                    // 차지 글로우: 발사가 가까울수록 커진다.
                    float charge = 1f - Mathf.Clamp01(toFire / ChargeWindupSeconds);
                    chargeSize = Mathf.Lerp(ChargeMinSize, ChargeMaxSize, charge)
                        * (0.85f + 0.3f * pulse);
                    glowAlpha = Mathf.Min(1f, alpha * 1.2f);
                    break;
                }
                case LaserPhase.Firing:
                {
                    // 실히트박스는 아직 얇다(ThinHalfWidth). 코어는 그 실폭으로 정직하게
                    // 그리고, 외곽에 곧 도착할 실폭 띠를 옅게 깔아 Sustaining 진입의
                    // 굵기 도약("갑자기 굵어진다")을 없앤다.
                    bandThickness = fullThickness;
                    bandColor = player ? PlayerBand : FiringBand;
                    bandColor.a = 0.55f;
                    coreThickness = trueThickness;
                    coreColor = player ? PlayerCore : FiringCore;
                    coreColor.a = 1f;

                    float flash = 1f - Mathf.Clamp01(_growAges[track] / MuzzleFlashSeconds);
                    chargeSize = ChargeMaxSize * (1f + 0.5f * flash);
                    glowAlpha = flash;
                    break;
                }
                case LaserPhase.Sustaining:
                {
                    bandThickness = fullThickness;
                    bandColor = player ? PlayerBand : SustainBand;
                    bandColor.a = 0.95f;
                    coreThickness = Mathf.Max(fullThickness * CoreFraction, MinCoreThickness);
                    coreColor = player ? PlayerCore : SustainCore;
                    coreColor.a = 1f;

                    // 지속 중에도 원점이 타오른다 — 발사원을 계속 지목해 준다.
                    chargeSize = ChargeMaxSize * 0.9f;
                    glowAlpha = 0.7f;
                    break;
                }
                default:
                {
                    bandThickness = fullThickness * 0.7f;
                    bandColor = player ? PlayerBand : DissipateBand;
                    bandColor.a = 0.30f;
                    coreThickness = trueThickness;
                    coreColor = player ? PlayerCore : DissipateCore;
                    coreColor.a = 0.50f;
                    break;
                }
            }

            // 본체·코어는 첫 피해 틱부터 판정 선분 전체를 표시한다.
            Vector3 center = start + delta * 0.5f;

            // 후보 C: 바깥 광채 → 본체 → 흰 코어. 세 겹 다 감쇠가 구워진 같은
            // 스프라이트라, 겹칠수록 가운데가 단단해지고 바깥이 부드럽게 사라진다.
            Color outerColor = bandColor;
            outerColor.a = bandColor.a * OuterAlphaScale;
            if (reduced) outerColor.a *= .45f;
            PlaceQuad(
                outer, start + delta * (0.5f * grow), rotation, length * grow,
                bandThickness * OuterWidthScale, outerColor);
            PlaceQuad(band, center, rotation, length, bandThickness, bandColor);
            PlaceQuad(core, center, rotation, length, coreThickness, coreColor);

            // 총구 플레어 — 원점에 박혀 "여기서 나온다"를 말한다.
            //
            // 크기가 **빔 두께를 따라간다.** 예전에는 고정 상수라, 굵은 빔에서는
            // 캡이 빔보다 작아 시작부가 네모로 뚝 잘렸다 (사람 지적 2026-08-05:
            // "레이저 크기가 커지면서 시작부분이 부드럽게 커브가 져야하는데
            // 네모처럼 되어있는 부분도 수정해줘").
            //
            // 캡은 방사 감쇠라, 빔 두께보다 조금 크게 걸면 그 원호가 빔의 시작
            // 단면을 덮어 둥근 머리가 된다.
            float capSize = Mathf.Max(chargeSize, bandThickness * MuzzleCapWidthScale);
            PlaceCap(glow, start, rotation, capSize, glowAlpha * (reduced ? .4f : 1f),
                player ? PlayerCore : bandColor);

            // 착탄 캡도 실제 선분 끝에 고정한다. 예고 중에는 띄우지 않는다.
            bool hitting = laser.Phase == LaserPhase.Firing
                || laser.Phase == LaserPhase.Sustaining;
            PlaceCap(
                impact,
                end,
                rotation,
                hitting ? coreThickness * ImpactSizeScale : 0f,
                hitting ? coreColor.a * (reduced ? .35f : .9f) : 0f,
                coreColor);
        }

        /// <summary>총구·착탄 캡 한 장. 크기나 알파가 0이면 끈다.</summary>
        void PlaceCap(
            SpriteRenderer renderer,
            Vector3 position,
            Quaternion rotation,
            float size,
            float alpha,
            Color color)
        {
            if (alpha <= 0.01f || size <= 0.001f)
            {
                if (renderer.enabled) renderer.enabled = false;
                return;
            }
            var t = renderer.transform;
            t.localPosition = position;
            t.localRotation = rotation;
            float scale = size / _capUnit;
            t.localScale = new Vector3(scale, scale, 1f);
            color.a = Mathf.Min(1f, alpha);
            renderer.color = color;
            if (!renderer.enabled) renderer.enabled = true;
        }

        /// <summary>
        /// 스프라이트 한 장을 선분으로 늘려 놓는다. **스케일은 월드 길이가 아니라
        /// 스프라이트 실크기로 나눈 값이다** — px_white는 2px(=0.125u)라 그냥 넣으면
        /// 1/8 크기로 그려진다 (2026-08-02 이전 버그).
        /// </summary>
        void PlaceQuad(
            SpriteRenderer renderer,
            Vector3 center,
            Quaternion rotation,
            float length,
            float thickness,
            Color color)
        {
            var t = renderer.transform;
            t.localPosition = center;
            t.localRotation = rotation;
            t.localScale = new Vector3(length / _softUnitX, thickness / _softUnitY, 1f);
            renderer.color = color;
            if (!renderer.enabled) renderer.enabled = true;
        }
    }
}

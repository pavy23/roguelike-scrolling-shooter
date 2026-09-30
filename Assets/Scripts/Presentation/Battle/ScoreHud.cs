using UnityEngine;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>우상단 점수·배율 표시 (UGUI + 픽셀 폰트). Core RunManager를 읽기만 한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ScoreHud : MonoBehaviour
    {
        [SerializeField] BattleDirector _director;
        [SerializeField] Font _fontBold;
        [Tooltip("접근성 플래시 감소 설정을 읽어 맥동 폭을 줄인다.")]
        [SerializeField] JuiceDirector _juice;

        /// <summary>
        /// SELECT 버튼(TouchControls)이 우상단 앵커에 (-56,-56) 크기 72로 앉는다.
        /// 즉 x는 -20~-92를 먹는다. 점수·배율을 그 왼쪽으로 밀어야 겹치지 않는다
        /// (사람 지적 2026-08-03: "스코어 주변 아이템 셀렉 UI 겹침, 배율이 잘 안 보임").
        /// 여유 8px을 더해 -100에 세운다 — 우측 정렬이라 자릿수가 늘어도 왼쪽으로만 자란다.
        /// </summary>
        const float RightInset = -100f;

        /// <summary>배율 글자 크기. 12는 SELECT 아이콘 옆에서 묻혔다 — 점수와 같은 급으로 올린다.</summary>
        const int MultiplierFontSize = 16;

        /// <summary>이 배율이 상한이다 (ComboMultipliers = 1·2·4·8·16·32).</summary>
        const int MaxMultiplier = 32;

        Text _text, _multiplierText;
        long _lastScore = long.MinValue;
        int _lastMultiplier = -1;

        // 발광은 매 프레임 색을 갱신한다. 문자열은 값이 바뀔 때만 만들므로
        // 프레임 루프 할당은 없다 (REQ-009).
        Color _glowBase = UiKit.TextDim;
        float _glowAmplitude;
        float _glowHz;
        float _swellAmplitude;

        void Start()
        {
            var canvas = UiKit.CreateCanvas("ScoreCanvas", 40);
            canvas.transform.SetParent(transform, false);
            canvas.gameObject.AddComponent<BattleHudVisibility>().Initialize(_director);
            _text = UiKit.CreateCornerText(canvas.transform, _fontBold, "00000000", 16,
                UiKit.TextAccent, new Vector2(1f, 1f), new Vector2(RightInset, -4f),
                TextAnchor.UpperRight, "Score");
            _multiplierText = UiKit.CreateCornerText(canvas.transform, _fontBold, "x1",
                MultiplierFontSize, UiKit.TextDim, new Vector2(1f, 1f),
                new Vector2(RightInset, -26f), TextAnchor.UpperRight, "Multiplier");
            UiKit.AddShadow(_multiplierText);   // 밝은 배경(성운·용암) 위에서도 읽히게
            ApplyMultiplierStyle(1);
        }

        void Update()
        {
            if (_director == null || _text == null) return;
            long score = _director.TotalScore;
            if (score != _lastScore)   // 값이 바뀐 프레임에만 문자열 생성 (REQ-009)
            {
                _lastScore = score;
                _text.text = score.ToString("D8");
            }
            int multiplier = _director.ScoreMultiplier;
            if (multiplier != _lastMultiplier && _multiplierText != null)
            {
                _lastMultiplier = multiplier;
                _multiplierText.text = MultiplierLabel(multiplier);
                ApplyMultiplierStyle(multiplier);
            }
            DriveGlow();
        }

        /// <summary>
        /// 배율이 오를수록 색이 진해지고 맥동이 세진다. 숫자를 읽지 않아도
        /// "지금 잘 하고 있다"가 눈 가장자리로 들어와야 한다 — 배율은 화면 구석에 있고
        /// 플레이어의 시선은 기체에 있기 때문이다.
        ///
        /// 접근성: JuiceDirector.FlashReduced가 켜지면 맥동을 크게 줄인다(끄지는 않는다 —
        /// 완전히 없애면 최대 배율의 특별함이 사라진다).
        /// </summary>
        void ApplyMultiplierStyle(int multiplier)
        {
            if (multiplier >= MaxMultiplier)
            {
                _glowBase = new Color(1f, 0.35f, 0.85f, 1f);   // 최대 — 마젠타 백열
                _glowAmplitude = 0.55f;
                _glowHz = 6.5f;
                _swellAmplitude = 0.10f;
            }
            else if (multiplier >= 16)
            {
                _glowBase = new Color(1f, 0.30f, 0.22f, 1f);   // 적열
                _glowAmplitude = 0.40f;
                _glowHz = 5f;
                _swellAmplitude = 0.06f;
            }
            else if (multiplier >= 8)
            {
                _glowBase = new Color(1f, 0.52f, 0.14f, 1f);   // 주황
                _glowAmplitude = 0.28f;
                _glowHz = 4f;
                _swellAmplitude = 0.03f;
            }
            else if (multiplier >= 4)
            {
                _glowBase = UiKit.TextAccent;                  // 앰버
                _glowAmplitude = 0.16f;
                _glowHz = 3f;
                _swellAmplitude = 0f;
            }
            else if (multiplier >= 2)
            {
                _glowBase = UiKit.TextMain;
                _glowAmplitude = 0.07f;
                _glowHz = 2.2f;
                _swellAmplitude = 0f;
            }
            else
            {
                _glowBase = UiKit.TextDim;                     // 배율 없음 — 조용히
                _glowAmplitude = 0f;
                _glowHz = 0f;
                _swellAmplitude = 0f;
            }
        }

        void DriveGlow()
        {
            if (_multiplierText == null) return;
            if (_glowAmplitude <= 0.001f)
            {
                _multiplierText.color = _glowBase;
                if (_multiplierText.rectTransform.localScale != Vector3.one)
                    _multiplierText.rectTransform.localScale = Vector3.one;
                return;
            }

            float amplitude = _glowAmplitude;
            float swell = _swellAmplitude;
            if (_juice != null && _juice.FlashReduced)
            {
                amplitude *= 0.35f;
                swell *= 0.35f;
            }

            float pulse = (Mathf.Sin(Time.unscaledTime * _glowHz) + 1f) * 0.5f;
            _multiplierText.color = Color.Lerp(_glowBase, Color.white, pulse * amplitude);
            if (swell > 0.0001f)
            {
                float scale = 1f + pulse * swell;
                _multiplierText.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        static string MultiplierLabel(int multiplier)
        {
            switch (multiplier)   // 고정 문자열 — 프레임 루프 무할당
            {
                case 1: return "x1";
                case 2: return "x2";
                case 4: return "x4";
                case 8: return "x8";
                case 16: return "x16";          // REQ-105: 콤보 6단계 (1·2·4·8·16·32)
                case 32: return "x32 MAX";      // 상한에 닿았음을 글자로도 말한다
                default:
                    return multiplier >= MaxMultiplier
                        ? "x" + multiplier + " MAX"
                        : "x" + multiplier;
            }
        }
    }
}

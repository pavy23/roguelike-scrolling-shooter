using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// 정식 게임오버 화면 (UGUI, DevCheats 임시 표시 대체).
    /// [Enter]/패드 South = 재출격(파워업 승계), [R]/패드 East = 타이틀.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameOverScreen : MonoBehaviour
    {
        [SerializeField] BattleDirector _director;
        [SerializeField] Font _font;
        [SerializeField] Font _fontBold;

        GameObject _root;
        Text _titleText, _scoreText, _statsText, _extraText, _modifierText, _hintsText;

        // 완주 축하 그림 (사람 지시 2026-08-04). 죽었을 때는 뜨지 않는다 —
        // 패배 화면에 축하 일러스트를 걸면 그 자체로 조롱이 된다.
        [SerializeField] Sprite _clearStandardSprite;
        [SerializeField] Sprite _clearPerfectSprite;
        Image _clearArt;
        Text _bonusText, _continueWarning;
        Button _retryButton, _titleButton, _continueButton;
        Text _continueLabel;
        Image _dim;
        RectTransform _panelRect;

        /// <summary>
        /// 축하 그림이 차지하는 세로 길이(그림 162 + 여백 6). 그림이 없는
        /// 게임오버에서는 이만큼 패널을 줄이고 글줄을 끌어올린다 — 안 그러면
        /// 카드 위쪽에 빈 구멍이 남는다.
        /// </summary>
        const float ArtBlockHeight = 168f;

        /// <summary>그림이 있을 때의 패널 높이. 640x360 기준을 넘기면 잘린다.</summary>
        const float PanelHeightWithArt = 360f;

        readonly System.Collections.Generic.List<(RectTransform Rect, float Y)>
            _topAnchoredTexts =
                new System.Collections.Generic.List<(RectTransform, float)>(6);
        int _shownRun = int.MinValue;
        bool _shownCleared;

        /// <summary>컨티뉴 버튼이 지금 화면에 있는가. 있고 없고에 따라 버튼 열이 재배치된다.</summary>
        bool _continueShown;
        int _shownContinueStock = -1;

        /// <summary>버튼 열 좌표. 컨티뉴가 끼면 두 칸이 양옆으로 밀린다.</summary>
        const float TouchButtonY = 36f;
        const float TouchButtonSpread = 66f;
        const float TouchButtonSpreadWide = 132f;
        const float SubmitY = 40f;
        const float SubmitSpread = 84f;

        /// <summary>스코어보드 제출 상태. Sending/Done에서는 버튼이 다시 눌리지 않는다.</summary>
        enum SubmitState { Idle, Sending, Done }

        const string SubmitIdleLabel = "SUBMIT SCORE";

        /// <summary>데일리는 전원이 같은 시드로 겨루는 별도 보드다 — 어디에 올리는지 라벨로 밝힌다.</summary>
        const string SubmitDailyLabel = "SUBMIT DAILY SCORE";

        /// <summary>치트를 쓴 런. 개발 검증 주행이 보드에 섞이지 않게 제출 자체를 닫는다.</summary>
        const string SubmitCheatLabel = "DEV RUN - NO SUBMIT";

        /// <summary>
        /// 시드를 직접 지정한 런. 같은 판을 반복 연습해 만든 점수라 랜덤 시드 기록과
        /// 같은 보드에 세울 수 없다 — 막히는 이유가 치트와 다르므로 문면도 다르다.
        /// </summary>
        const string SubmitSeededLabel = "SEEDED RUN - NO SUBMIT";

        /// <summary>재생은 기록의 재현일 뿐 새 기록이 아니다.</summary>
        const string SubmitReplayLabel = "REPLAY - NO SUBMIT";

        Button _submitButton;
        Text _submitLabel;
        SubmitState _submitState;

        void Start()
        {
            var canvas = UiKit.CreateCanvas("GameOverCanvas", 90);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;

            // 제출 버튼 한 줄이 늘어난 만큼 패널을 키운다 (요약 텍스트는 전부 상단 앵커라
            // 위치가 그대로고, 힌트는 하단 앵커라 바닥에 붙어 따라 내려간다).
            // 288 = 상단 요약 6줄(보너스 줄 포함, ~154px) + 하단 3단(경고·버튼열·제출,
            // ~114px) + 여유. 컨티뉴 줄이 붙으면서 224로는 두 블록이 겹친다.
            var panel = UiKit.CreatePanel(
                canvas.transform, new Vector2(400f, PanelHeightWithArt));
            _panelRect = panel as RectTransform ?? panel.GetComponent<RectTransform>();

            // 축하 그림을 패널 **안쪽 위**에 넣는다 (사람 지시 2026-08-05:
            // "클리어 이미지도 아래 네모난 카드 안에 들어오도록"). 예전에는 패널
            // 바깥 위에 띄웠는데, 카드와 그림이 따로 떠 있어 한 장으로 읽히지
            // 않았다. 요약 여섯 줄을 밀어내지 않으려고 패널을 288 → 460으로
            // 키우고 그 늘어난 만큼을 그림에 준다.
            //
            // 288x162는 원본 320x180의 0.9배다. 정수배가 아니라 픽셀 아트로서는
            // 손해지만, 패널 폭 400에 320을 넣으면 좌우 여백이 40씩뿐이라 그림이
            // 카드에 낀 것처럼 보인다. 카드 안에 놓이는 것이 이번 요구다.
            //
            // **패널 높이는 360을 넘기면 안 된다.** UI 캔버스는 640x360 기준 정수
            // 배율(PixelUiScaler)이라, 기준 높이를 넘는 패널은 화면에서 잘린다.
            // 그림을 넣으면서 460으로 키웠더니 폰에서 아래 버튼 줄이 통째로
            // 잘려 나갔다 (사람 보고 2026-08-05, 첨부 스크린샷). 그림 크기는
            // 그대로 두고 글줄 간격과 버튼 높이를 깎아 360 안에 넣는다.
            var artGo = new GameObject("ClearArt");
            artGo.transform.SetParent(panel, false);
            _clearArt = artGo.AddComponent<Image>();
            _clearArt.raycastTarget = false;
            _clearArt.preserveAspect = true;
            var artRect = _clearArt.rectTransform;
            artRect.anchorMin = artRect.anchorMax = artRect.pivot = new Vector2(0.5f, 1f);
            artRect.anchoredPosition = new Vector2(0f, -6f);
            artRect.sizeDelta = new Vector2(288f, 162f);
            _clearArt.enabled = false;

            _titleText = UiKit.CreateCornerText(panel, _fontBold, UiText.GameOverTitle, 20, UiKit.TextDanger,
                new Vector2(0.5f, 1f), new Vector2(0f, -162f), TextAnchor.UpperCenter, "Title");
            _scoreText = UiKit.CreateCornerText(panel, _fontBold, "", 11, UiKit.TextAccent,
                new Vector2(0.5f, 1f), new Vector2(0f, -186f), TextAnchor.UpperCenter, "Score");
            _statsText = UiKit.CreateCornerText(panel, _font, "", 11, UiKit.TextMain,
                new Vector2(0.5f, 1f), new Vector2(0f, -201f), TextAnchor.UpperCenter, "Stats");
            _extraText = UiKit.CreateCornerText(panel, _font, "", 11, UiKit.TextDim,
                new Vector2(0.5f, 1f), new Vector2(0f, -248f), TextAnchor.UpperCenter, "Extra");
            // 완주 본문은 유일하게 긴 문장이라 패널 폭(400)을 넘겼다 — 텍스트 상자가
            // 300 고정이고 넘침 모드가 Overflow라 그냥 삐져나온다 (사람 보고
            // 2026-08-05: "글자가 삐져나오잖아"). 이 줄만 상자를 패널 안쪽 폭으로
            // 넓히고 **줄바꿈**을 켠다. 두 줄이 되어도 아래 줄과 겹치지 않도록
            // 아래 두 줄(모디파이어·보너스)은 그만큼 내려 잡았다.
            // 본문 상자에 **세 줄 분량**을 준다. 30(두 줄)으로 잡았더니 히든 조건
            // 안내처럼 줄바꿈이 이미 들어 있는 문면이 세 줄이 되면서 위쪽
            // SHIELD BONUS 줄과 겹쳐 글자가 뭉갰다 (사람 보고 2026-08-05, 첨부).
            _extraText.rectTransform.sizeDelta = new Vector2(372f, 46f);
            _extraText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _modifierText = UiKit.CreateCornerText(panel, _font, "", 11, UiKit.TextAccent,
                new Vector2(0.5f, 1f), new Vector2(0f, -216f), TextAnchor.UpperCenter, "Modifiers");
            // 실드 보너스(REQ-105)와 컨티뉴 사용 표시(REQ-104). 둘 다 "이 점수가 어떻게
            // 만들어졌는가"에 대한 단서라 점수 줄과 같은 앰버 계열로 한 줄에 둔다.
            _bonusText = UiKit.CreateCornerText(panel, _font, "", 11, UiKit.TextAccent,
                new Vector2(0.5f, 1f), new Vector2(0f, -231f), TextAnchor.UpperCenter, "Bonus");
            // 그림 유무에 따라 통째로 끌어올릴 대상 (아래 앵커 요소는 패널 높이를
            // 따라오므로 여기 넣지 않는다).
            foreach (var text in new[]
            {
                _titleText, _scoreText, _statsText,
                _extraText, _modifierText, _bonusText
            })
                _topAnchoredTexts.Add(
                    (text.rectTransform, text.rectTransform.anchoredPosition.y));

            _hintsText = UiKit.CreateCornerText(panel, _font,
                UiText.GameOverHints, 11, UiKit.TextDim,
                new Vector2(0.5f, 0f), new Vector2(0f, 16f), TextAnchor.LowerCenter, "Hints");

            bool touch = UiPlatform.TouchMode;
            if (touch)
            {
                _hintsText.gameObject.SetActive(false);
                _retryButton = UiKit.CreateTouchButton(panel, _font, "REDEPLOY", 11,
                    new Vector2(0.5f, 0f), new Vector2(-TouchButtonSpread, TouchButtonY),
                    new Vector2(124f, 32f), Retry, "RetryButton", accent: true);
                _titleButton = UiKit.CreateTouchButton(panel, _font, "TITLE", 11,
                    new Vector2(0.5f, 0f), new Vector2(TouchButtonSpread, TouchButtonY),
                    new Vector2(124f, 32f), ToTitle, "TitleButton");
            }

            // 컨티뉴 (REQ-104). 재고가 있고 Core가 허용할 때만 나타난다 — 데일리 런과
            // 최종전 판돈 정산 뒤에는 Core가 거절하므로 여기서 조건을 다시 쓰지 않는다.
            // 키보드에서도 클릭이 되도록 항상 버튼으로 만든다(단축키는 [C]/(X)).
            _continueButton = UiKit.CreateTouchButton(panel, _font, "CONTINUE", 11,
                new Vector2(0.5f, 0f),
                touch ? new Vector2(0f, TouchButtonY) : new Vector2(SubmitSpread, SubmitY),
                touch ? new Vector2(124f, 32f) : new Vector2(160f, 28f),
                UseContinue, "ContinueButton");
            _continueLabel = _continueButton.GetComponentInChildren<Text>();
            _continueWarning = UiKit.CreateCornerText(panel, _font, UiText.ContinueWarning, 9,
                UiKit.TextDanger, new Vector2(0.5f, 0f),
                new Vector2(0f, touch ? 74f : 66f), TextAnchor.LowerCenter, "ContinueWarning");
            _continueButton.gameObject.SetActive(false);
            _continueWarning.gameObject.SetActive(false);

            // 글로벌 스코어보드 제출 (P1). 앰버 CTA는 화면당 하나(REDEPLOY)라는 원칙을
            // 지켜 헤어라인 셀로 둔다 — 제출은 선택지지 이 화면의 주 동작이 아니다.
            _submitButton = UiKit.CreateTouchButton(panel, _font, SubmitIdleLabel, 11,
                new Vector2(0.5f, 0f),
                touch ? new Vector2(0f, 4f) : new Vector2(0f, SubmitY),
                touch ? new Vector2(256f, 30f) : new Vector2(160f, 28f),
                SubmitScore, "SubmitButton");
            _submitLabel = _submitButton.GetComponentInChildren<Text>();

            _dim = UiKit.CreateDim(canvas.transform, Color.clear, "Tint");
            _dim.transform.SetAsFirstSibling();

            _root.SetActive(false);
        }

        /// <summary>
        /// 그림이 있으면 원래 자리, 없으면 그림 높이만큼 끌어올리고 패널도 줄인다.
        /// 완주 두 종류(PERFECT / CLEAR)는 그림만 다르고 배치는 같다 — 사람 지시
        /// 2026-08-05: "퍼팩트 클리어 말고 일반 클리어도 똑같이 처리해줘".
        /// </summary>
        void ApplyArtLayout(bool hasArt)
        {
            float shift = hasArt ? 0f : ArtBlockHeight;
            for (int i = 0; i < _topAnchoredTexts.Count; i++)
            {
                var (rect, baseY) = _topAnchoredTexts[i];
                if (rect == null) continue;
                var position = rect.anchoredPosition;
                position.y = baseY + shift;
                rect.anchoredPosition = position;
            }
            if (_panelRect == null) return;
            var size = _panelRect.sizeDelta;
            size.y = PanelHeightWithArt - shift;
            _panelRect.sizeDelta = size;
        }

        void Retry()
        {
            if (_director != null) _director.RestartRun();
        }

        /// <summary>
        /// 컨티뉴 사용. 성공하면 런이 다시 Playing으로 돌아가므로 다음 프레임에
        /// 이 패널은 스스로 닫힌다 — 여기서 화면을 직접 끄지 않는다.
        /// 실패(재고 소진·데일리·판돈 정산 뒤)는 Core 판정이고, 그 경우 버튼은
        /// 애초에 떠 있지 않다.
        /// </summary>
        void UseContinue()
        {
            if (_director == null) return;
            if (!_director.UseContinue()) return;
            // 이어간 런의 결과는 새 요약이다 — 지난 사망의 표시를 재사용하지 않는다.
            _shownRun = int.MinValue;
            _shownContinueStock = -1;
        }

        /// <summary>
        /// 컨티뉴 버튼을 넣고 빼면서 같은 줄의 이웃을 재배치한다. 버튼만 감추면
        /// 가운데가 빈 두 칸이 남아 "뭔가 사라진 자리"로 읽힌다.
        /// </summary>
        void SetContinueVisible(bool visible)
        {
            if (_continueShown == visible) return;
            _continueShown = visible;
            if (_continueButton != null)
                _continueButton.gameObject.SetActive(visible);
            if (_continueWarning != null)
                _continueWarning.gameObject.SetActive(visible);

            float spread = visible ? TouchButtonSpreadWide : TouchButtonSpread;
            if (_retryButton != null)
                _retryButton.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(-spread, TouchButtonY);
            if (_titleButton != null)
                _titleButton.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(spread, TouchButtonY);
            // 키보드/패드 레이아웃에서는 제출 버튼이 컨티뉴와 한 줄을 나눠 쓴다.
            if (!UiPlatform.TouchMode && _submitButton != null)
                _submitButton.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(visible ? -SubmitSpread : 0f, SubmitY);
        }

        static void ToTitle()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
        }

        /// <summary>
        /// 스코어보드 제출. 이름이 없으면 먼저 받고(폰 브라우저는 window.prompt), 그다음
        /// 비동기 제출이다. 실패해도 화면은 그대로 살아 있고 버튼만 재시도로 바뀐다 —
        /// 네트워크 사정이 재출격을 막으면 안 된다.
        /// </summary>
        void SubmitScore()
        {
            if (_submitState != SubmitState.Idle) return;
            if (_director == null || !_director.IsRunFinished) return;

            // 애초에 보드에 오를 수 없는 런은 여기서 끝낸다 (Update가 이미 버튼을 닫아
            // 두지만, 제출 경로 자체를 막아야 다른 입력으로 새는 길이 없다).
            string blocked = BlockedSubmitLabel();
            if (blocked != null)
            {
                _submitState = SubmitState.Done;
                SetSubmitLabel(blocked);
                if (_submitButton != null) _submitButton.interactable = false;
                return;
            }

            string playerName = ScoreboardClient.PlayerName;
            if (string.IsNullOrEmpty(playerName))
            {
                playerName = ScoreboardClient.SanitizeName(NamePrompt.Ask(playerName));
                if (string.IsNullOrEmpty(playerName))
                {
                    // 취소했거나 2자 미만이다. 상태는 Idle로 두어 다시 누를 수 있게 한다.
                    SetSubmitLabel("NAME 2-10 CHARS");
                    return;
                }
                ScoreboardClient.PlayerName = playerName;
            }

            _submitState = SubmitState.Sending;
            SetSubmitLabel("SENDING...");
            var runStats = _director.RunStats;
            var gauge = _director.Gauge;
            ScoreboardClient.Submit(new ScoreSubmission
            {
                Score = _director.TotalScore,
                Seed = _director.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Ship = _director.ShipId,
                Difficulty = _director.DifficultyLabel,
                Grade = GradeLabel(_director.CompletionGrade),
                // TODO: 리플레이 검증 해시. Core에 "이 런의 결과 해시"를 내주는 API가 없어
                // 비워 둔다 (Reviews/from-claude/requests.md REQ-093 — CODEX 요청).
                ReplayHash = "",
                Daily = _director.IsDailyRun,

                // P1.5 상세 통계 (REQ-094 관측 소비). 전부 사망 시점의 Core 권위 값이다 —
                // 화면에 이미 그려 둔 숫자를 다시 계산하지 않고 같은 출처에서 읽는다.
                Stage = _director.StageIndex,
                Room = _director.RoomIndex,
                // 옵션은 게이지 Option 레벨이 곧 개수다 (Core가 이 값으로 옵션을 띄운다).
                // 사망 시점의 IBattleSim.Options는 이미 정리됐을 수 있어 게이지를 읽는다.
                Options = gauge != null
                    ? gauge.GetLevel(Shmup.Core.PowerUpSlot.Option) : 0,
                Levels = GaugeLevelTotal(gauge),
                Bombs = ToInt(runStats.BombsUsed),
                Graze = ToInt(runStats.GrazeCount),
                MaxCombo = _director.BestMultiplier,
                // 피격 수 (REQ-105). 적을수록 좋은 유일한 통계라 보드에서 0이 강조된다 —
                // BOMB 0과 같은 문법이다. 상한(999)은 클라이언트가 먼저 자른다.
                HitsTaken = ToInt(runStats.HitsTaken),
                // 컨티뉴 사용 횟수 (REQ-109). 요약의 CONTINUED xN과 같은 Core 값이다 —
                // 화면에 이미 그린 숫자를 보드에도 그대로 올려 두 곳이 어긋나지 않게 한다.
                ContinuesUsed = _director.ContinuesUsed
            }, OnSubmitDone);
        }

        /// <summary>
        /// 이 런이 글로벌 보드에 오를 수 없다면 그 이유를 담은 버튼 문면, 오를 수 있으면 null.
        /// 세 경우 다 "제출 실패"가 아니라 "제출 대상이 아님"이라 버튼이 눌리지 않는다 —
        /// 이유가 서로 다르므로 문면도 갈라 놓는다.
        /// </summary>
        string BlockedSubmitLabel()
        {
            if (_director == null) return null;
            if (_director.CheatUsed) return SubmitCheatLabel;
            if (_director.IsSeededRun) return SubmitSeededLabel;
            if (_director.ReplayMode) return SubmitReplayLabel;
            return null;
        }

        /// <summary>
        /// 게이지 전 슬롯 레벨의 합 (SHOT 포함). "빌드를 얼마나 키웠나"를 한 숫자로
        /// 압축한 값이라 슬롯 구성이 기체마다 달라도 비교가 된다.
        /// </summary>
        static int GaugeLevelTotal(Shmup.Core.PowerUpGauge gauge)
        {
            if (gauge == null) return 0;
            int total = 0;
            for (int i = 0; i < gauge.GaugeSlotCount; i++)
                total += gauge.GetGaugeSlotView(i).Level;
            return total;
        }

        /// <summary>Core 통계는 포화 누계(long)다 — 제출 필드 폭에 맞춰 자른다.</summary>
        static int ToInt(long value)
        {
            if (value <= 0) return 0;
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        void OnSubmitDone(int rank, string error)
        {
            // 응답이 늦게 오면 화면이 이미 파괴됐을 수 있다 (재출격/타이틀 복귀).
            if (this == null || _submitButton == null) return;
            if (error != null)
            {
                _submitState = SubmitState.Idle;
                SetSubmitLabel("RETRY SUBMIT");
                return;
            }
            _submitState = SubmitState.Done;
            SetSubmitLabel(rank > 0 ? $"RANK #{rank}" : "SUBMITTED");
            _submitButton.interactable = false;
        }

        void SetSubmitLabel(string text)
        {
            if (_submitLabel != null) _submitLabel.text = text;
        }

        static string GradeLabel(Shmup.Core.Simulation.RunCompletionGrade grade)
        {
            switch (grade)
            {
                case Shmup.Core.Simulation.RunCompletionGrade.PerfectClear: return "PERFECT";
                case Shmup.Core.Simulation.RunCompletionGrade.StandardClear: return "CLEAR";
                default: return "KIA";
            }
        }

        void Update()
        {
            if (_director == null || _root == null) return;
            // 사망(RunOver)과 완주(RunCleared)를 같은 패널로 처리하되 문면을 바꾼다 (REQ-031)
            // 최종 격파도 보상 화면과 같은 연출 경계를 지킨다. 플레이어 사망은 즉시 표시한다.
            bool finished = _director.IsRunFinished
                && !(_director.IsRunCleared && _director.BossDeathCinematicActive);
            if (_root.activeSelf != finished)
                _root.SetActive(finished);
            if (!finished) return;

            if (_shownRun != _director.RunNumber || _shownCleared != _director.IsRunCleared)
            {
                _shownRun = _director.RunNumber;
                _shownCleared = _director.IsRunCleared;
                bool cleared = _shownCleared;
                // 완주에도 두 종류가 있다 (사람 지시 2026-08-04). 미지의 구역까지 잡은
                // 런과 5바이옴만 돈 런을 같은 문면으로 끝내면, 히든 루트가 있다는
                // 사실조차 모른 채 게임을 끝낸다.
                bool perfect = _director.CompletionGrade
                    == Shmup.Core.Simulation.RunCompletionGrade.PerfectClear;
                _titleText.text = !cleared ? UiText.GameOverTitle
                    : perfect ? UiText.RunClearedPerfectTitle
                    : UiText.RunClearedTitle;
                _titleText.color = cleared ? UiKit.TextAccent : UiKit.TextDanger;
                if (_clearArt != null)
                {
                    var art = !cleared ? null
                        : perfect ? _clearPerfectSprite : _clearStandardSprite;
                    _clearArt.sprite = art;
                    _clearArt.enabled = art != null;
                    ApplyArtLayout(art != null);
                }
                _hintsText.text = cleared
                    ? UiText.RunClearedHints
                    : (CanContinue() ? UiText.GameOverHintsContinue : UiText.GameOverHints);
                // 완주 뒤에는 파워업을 승계하지 않으므로 "재출격"이 아니라 새 런이다.
                if (_retryButton != null)
                {
                    var label = _retryButton.GetComponentInChildren<Text>();
                    if (label != null) label.text = cleared ? "NEW RUN" : "REDEPLOY";
                }
                // 새 런의 결과다 — 지난 런의 제출 결과(RANK #n)를 그대로 두면 오독된다.
                if (_submitButton != null)
                {
                    // 제출이 막힌 런이면 눌러 보게 두지 않는다: 이유를 여기서 읽히게 한다.
                    string blocked = BlockedSubmitLabel();
                    _submitState = blocked != null ? SubmitState.Done : SubmitState.Idle;
                    _submitButton.interactable = blocked == null;
                    SetSubmitLabel(blocked
                        ?? (_director.IsDailyRun ? SubmitDailyLabel : SubmitIdleLabel));
                }
                if (_dim != null)
                    _dim.color = cleared
                        ? new Color(0.06f, 0.22f, 0.12f, 0.45f)   // 승리: 청록 틴트
                        : new Color(0.35f, 0.02f, 0.05f, 0.45f);  // 패배: 적색 틴트
                var stats = _director.RunStats;
                float accuracy = stats.ShotsFired > 0
                    ? (float)stats.ShotsHit / stats.ShotsFired * 100f : 0f;
                _scoreText.text =
                    $"SCORE  {_director.TotalScore:D8}   (run {_director.RunNumber}, stage {_director.StageIndex})";
                _statsText.text =
                    $"KILLS {stats.Kills}   CAPSULES {stats.CapsulesCollected}   ACC {accuracy:0.#}%   SHOTS {stats.ShotsFired}";
                // BOMBS/HITS는 보드 BOMB·HIT 칸과 같은 값이다 — 여기서 0을 확인할 수
                // 있어야 보드에서 그 0이 왜 앰버로 강조됐는지 납득이 된다.
                _extraText.text =
                    $"BEST COMBO x{_director.BestMultiplier}   GRAZE {stats.GrazeCount}"
                    + $"   BOMBS {stats.BombsUsed}   HITS {stats.HitsTaken}";
                _modifierText.text = cleared
                    ? ClearMessage(perfect)
                    : DescribeModifiers(_director.ActiveModifiers);
                _bonusText.text = DescribeBonuses();
            }

            // 치트는 게임오버 화면이 떠 있는 동안에도 눌릴 수 있다 — 런 전환 블록 밖에서
            // 매 프레임 다시 본다 (프로퍼티 몇 개 비교 — 할당 없음).
            // 시드/리플레이 낙인은 런 시작에 굳지만 같은 문으로 통과시켜 경로를 하나로 둔다.
            if (_submitState != SubmitState.Done)
            {
                string blocked = BlockedSubmitLabel();
                if (blocked != null)
                {
                    _submitState = SubmitState.Done;
                    if (_submitButton != null) _submitButton.interactable = false;
                    SetSubmitLabel(blocked);
                }
            }

            // 컨티뉴 가능 여부는 Core 판정이고 화면이 떠 있는 동안 바뀌지 않지만,
            // 첫 프레임에 런 전환 블록이 이미 지나갔을 수 있어 여기서 매 프레임 맞춘다
            // (bool 비교 — 바뀔 때만 배치가 움직인다).
            bool canContinue = CanContinue();
            SetContinueVisible(canContinue);
            if (canContinue)
            {
                int stock = _director.ContinueStock;
                if (stock != _shownContinueStock)
                {
                    _shownContinueStock = stock;
                    if (_continueLabel != null)
                        _continueLabel.text =
                            string.Format(UiText.ContinueButtonFormat, stock);
                }
            }

            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            bool restart = (keyboard != null && keyboard.enterKey.wasPressedThisFrame)
                        || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            bool toTitle = (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                        || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
            bool continueRun = canContinue
                && ((keyboard != null && keyboard.cKey.wasPressedThisFrame)
                    || (gamepad != null && gamepad.buttonWest.wasPressedThisFrame));
            if (continueRun) UseContinue();
            else if (restart) Retry();
            else if (toTitle) ToTitle();
        }

        /// <summary>
        /// Core가 지금 컨티뉴를 허용하는가. 재생 중에는 관객이 결정을 내릴 수 없으므로
        /// (기록된 결정만 재현된다) 화면에서도 닫는다.
        /// </summary>
        bool CanContinue()
        {
            return _director != null
                && !_director.ReplayMode
                && _director.ContinueAvailability.CanUse;
        }

        /// <summary>
        /// 점수에 얹힌 보너스 줄. 클리어 실드 보너스(REQ-105)와 컨티뉴 사용 횟수를
        /// 같이 세운다 — 둘 다 "이 숫자가 왜 이렇게 나왔는가"의 설명이다.
        ///
        /// 컨티뉴 사용은 보드 스키마에 실어 보낼 칸이 아직 없어(서버 필드 미배포)
        /// 이 요약에만 남는다. 보드 표기는 후속 제안으로 넘겼다.
        /// </summary>
        /// <summary>
        /// 완주 축하 문면. 세 갈래다 — 완전 클리어 / 히든이 열려 있었던 완주 /
        /// 조건 미달 완주. 마지막 경우에는 **무엇이 모자랐는지** 숫자로 보여 준다.
        ///
        /// 조건(엘리트 3 · 무피해 2 · 희귀 1 중 2개)은 게임 안 어디에도 적혀 있지
        /// 않았다. 그러면 미지의 구역은 우연히 열리는 기능이 된다 — 완주 화면은
        /// 그것을 말해 줄 마지막 자리다.
        /// </summary>
        string ClearMessage(bool perfect)
        {
            if (perfect) return UiText.RunClearedPerfectBody;
            return _director.HiddenConditionsMet >= 2
                ? UiText.RunClearedHiddenReadyBody
                : string.Format(
                    UiText.RunClearedHiddenLockedBody,
                    _director.HiddenConditionsDetail);
        }

        string DescribeBonuses()
        {
            if (_director == null) return "";
            long shieldBonus = _director.RunClearShieldBonus;
            int continues = _director.ContinuesUsed;
            if (shieldBonus <= 0 && continues <= 0) return "";
            var sb = new System.Text.StringBuilder(48);
            if (shieldBonus > 0)
                sb.Append(string.Format(
                    UiText.ShieldBonusFormat, shieldBonus.ToString("N0")));
            if (continues > 0)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(string.Format(UiText.ContinuedFormat, continues));
            }
            return sb.ToString();
        }

        static string DescribeModifiers(Shmup.Core.Simulation.BattleModifier modifiers)
        {
            if (modifiers == Shmup.Core.Simulation.BattleModifier.None) return "";
            var sb = new System.Text.StringBuilder(64);
            sb.Append("BUILD: ");
            AppendModifier(sb, modifiers, Shmup.Core.Simulation.BattleModifier.PierceShot, "PIERCE");
            AppendModifier(sb, modifiers, Shmup.Core.Simulation.BattleModifier.Ricochet, "RICOCHET");
            AppendModifier(sb, modifiers, Shmup.Core.Simulation.BattleModifier.HomingMissile, "HOMING");
            AppendModifier(sb, modifiers, Shmup.Core.Simulation.BattleModifier.KillExplosion, "BLAST");
            return sb.ToString();
        }

        static void AppendModifier(
            System.Text.StringBuilder sb,
            Shmup.Core.Simulation.BattleModifier modifiers,
            Shmup.Core.Simulation.BattleModifier flag,
            string label)
        {
            if ((modifiers & flag) == 0) return;
            if (sb.Length > 7) sb.Append(" + ");
            sb.Append(label);
        }
    }
}

using Shmup.Core;
using Shmup.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// Observes real movement, capsule pickups and successful gauge investment.
    /// Only the guide's completion is saved; simulation state is never changed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnboardingHints : MonoBehaviour
    {
        const string OnboardedPrefKey = "rss.onboarded";
        const float CompletionSeconds = 2.5f;

        [SerializeField] BattleDirector _director;
        [SerializeField] Font _font;

        public int CurrentStep => _done ? 3 : !_moved ? 0 : !_collected ? 1 : 2;
        public bool Completed => _done;

        /// <summary>조작 안내는 기기에 맞는 문면이어야 한다 — 폰에 WASD를 알려줘도 소용없다.</summary>
        string HintAt(int index)
        {
            bool touch = UiPlatform.TouchMode;
            switch (index)
            {
                case 0: return touch ? UiText.OnboardingMoveTouch
                    : PlayerBindings.MoveHint(_director?.Input?.MoveAction)
                      + "\n" + UiText.OnboardingAutomaticFire;
                case 1: return UiText.OnboardingCollect;
                case 3: return UiText.OnboardingComplete;
                default:
                    int context = HintContext();
                    if (context == 2) return UiText.OnboardingContract;
                    if (context == 3) return UiText.OnboardingUnavailable;
                    return string.Format(context == 1 ? UiText.OnboardingSelect : UiText.OnboardingInvest,
                        PlayerBindings.ActivateHint(_director?.Input?.ActivateAction));
            }
        }

        Text _text, _heading;
        GameObject _root;
        Canvas _canvas;
        RectTransform _panel;
        CanvasGroup _group;
        Camera _camera;
        int _hintIndex = -1;
        int _bindingRevision = -1;
        int _hintContext = -1;
        float _completionRemaining;
        bool _done, _loaded, _moved, _collected, _invested;
        bool _observing, _moveRequested, _investRequested;
        IBattleSim _observedBattle;
        PowerUpGauge _observedGauge;
        PowerUpSlot _selectedSlot;
        int _previousX, _previousY, _previousTick, _previousLevel, _previousProgress;

        int HintContext()
        {
            var gauge = _director != null ? _director.Gauge : null;
            if (gauge == null) return 0;
            if (gauge.GaugeActivationBanned) return 2;
            if (!gauge.HasSelection) return 1;
            return gauge.CanActivate ? 0 : 3;
        }

        void LoadPreference()
        {
            if (_loaded) return;
            _loaded = true;
            _done = PlayerPrefs.GetInt(OnboardedPrefKey, 0) == 1;
        }

        /// <summary>Restart from Options without restarting or modifying the run.</summary>
        public void RestartGuide()
        {
            _loaded = true;
            _done = _moved = _collected = _invested = _observing = false;
            _completionRemaining = 0f;
            _hintIndex = -1;
            PlayerPrefs.DeleteKey(OnboardedPrefKey);
            SaveFlush.Request();
        }

        // Called around the same Core step. Snapshots distinguish a successful investment
        // from a rejected press or a stale LastActivationResult, including partial costs.
        public void BeforeStep(IBattleSim battle, PowerUpGauge gauge, in InputCommand command)
        {
            LoadPreference();
            _observing = !_done && isActiveAndEnabled && _director != null && _director.IsPlaying
                && !_director.ReplayMode && !PlayerInputReader.AutopilotEnabled && Time.timeScale > 0f;
            if (!_observing) return;
            _observedBattle = battle;
            _observedGauge = gauge;
            _previousX = battle.PlayerX;
            _previousY = battle.PlayerY;
            _previousTick = battle.Tick;
            _moveRequested = command.UseAnalogMovement
                ? command.AnalogDeltaXSubUnits != 0 || command.AnalogDeltaYSubUnits != 0
                : command.MoveX != 0 || command.MoveY != 0;
            _investRequested = command.Activate && gauge != null && gauge.HasSelection;
            if (_investRequested)
            {
                _selectedSlot = gauge.SelectedSlot;
                _previousLevel = gauge.GetLevel(_selectedSlot);
                _previousProgress = gauge.GetProgress(_selectedSlot);
            }
        }

        public void AfterStep(IBattleSim battle)
        {
            if (!_observing) return;
            _observing = false;
            if (!ReferenceEquals(battle, _observedBattle) || battle.Tick == _previousTick) return;
            _moved |= _moveRequested && (battle.PlayerX != _previousX || battle.PlayerY != _previousY);
            foreach (var e in battle.EventsThisTick)
                if (e.Type == SimEventType.CapsulePicked) _collected = true;
            _invested |= _investRequested && (_observedGauge.GetLevel(_selectedSlot) != _previousLevel
                || _observedGauge.GetProgress(_selectedSlot) != _previousProgress);
            if (!_moved || !_collected || !_invested) return;
            _done = true;
            _completionRemaining = CompletionSeconds;
            PlayerPrefs.SetInt(OnboardedPrefKey, 1);
            SaveFlush.Request();
        }

        void Start()
        {
            LoadPreference();
            _canvas = UiKit.CreateCanvas("OnboardingCanvas", 45);
            _canvas.transform.SetParent(transform, false);
            _root = _canvas.gameObject;
            _group = _root.AddComponent<CanvasGroup>();
            _group.interactable = _group.blocksRaycasts = false;
            _panel = UiKit.CreatePanel(_canvas.transform, new Vector2(528f, 54f), "GuidePanel");
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0f);
            _panel.anchoredPosition = new Vector2(-40f, 68f);
            _heading = UiKit.CreateCornerText(_panel, _font, "", 9, UiKit.TextAccent,
                new Vector2(0.5f, 1f), new Vector2(0f, -5f), TextAnchor.UpperCenter, "GuideStep");
            _text = UiKit.CreateTextStretch(_panel, _font, "", 11, UiKit.TextMain,
                TextAnchor.MiddleCenter, 6f, "Hint");
            _text.rectTransform.offsetMax = new Vector2(-6f, -18f);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _root.SetActive(false);
        }

        void Update()
        {
            if (_root == null || _director == null) return;

            // 게임오버/보상/경로 화면 중에는 숨긴다
            bool visible = (!_done || _completionRemaining > 0f) && _director.IsPlaying
                           && !_director.ReplayMode && !_director.BossDeathCinematicActive
                           && Time.timeScale > 0f;
            if (_root.activeSelf != visible) _root.SetActive(visible);
            if (!visible) return;

            if (_done) _completionRemaining -= Time.deltaTime;
            int index = CurrentStep;
            int context = index == 2 ? HintContext() : 0;
            if (index != _hintIndex || _bindingRevision != PlayerBindings.Revision || context != _hintContext)
            {
                _hintIndex = index;
                _hintContext = context;
                _bindingRevision = PlayerBindings.Revision;
                _text.text = HintAt(index);
                _heading.text = index == 3 ? UiText.OnboardingCompleteTitle
                    : string.Format(UiText.OnboardingStep, index + 1);
            }
            // The persistent guide must yield when the ship passes behind it.
            if (_camera == null) _camera = Camera.main;
            if (_camera != null)
            {
                var screen = _camera.WorldToScreenPoint(_director.PlayerWorldPosition);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_panel, screen,
                    _canvas.worldCamera, out var local);
                var bounds = _panel.rect;
                bounds.xMin -= 24f; bounds.xMax += 24f;
                bounds.yMin -= 15f; bounds.yMax += 15f;
                _group.alpha = Mathf.MoveTowards(_group.alpha, bounds.Contains(local) ? 0.2f : 1f,
                    6f * Time.unscaledDeltaTime);
            }
        }
    }
}

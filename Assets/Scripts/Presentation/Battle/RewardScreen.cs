using Shmup.Core;
using Shmup.Core.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>Read-only reward cards; every decision is validated by BattleDirector/Core.</summary>
    [DisallowMultipleComponent]
    public sealed class RewardScreen : MonoBehaviour
    {
        // A contract can add a fourth card to the usual three.
        const int MaxOptions = RunManager.MainRewardOptionCount + 1;
        [SerializeField] BattleDirector _director;
        [SerializeField] Font _font;
        [SerializeField] Font _fontBold;

        GameObject _root;
        Text _titleText, _contextText, _hints, _rerollStatus, _rerollLabel;
        readonly Image[] _boxBorders = new Image[MaxOptions];
        readonly Text[] _boxTitles = new Text[MaxOptions];
        readonly Text[] _boxTexts = new Text[MaxOptions];
        readonly Text[] _boxCosts = new Text[MaxOptions];
        readonly Text[] _boxMarkers = new Text[MaxOptions];
        readonly RectTransform[] _boxRects = new RectTransform[MaxOptions];
        readonly ChoiceButton[] _buttons = new ChoiceButton[MaxOptions];
        bool _labelsBuilt;
        int _shownOptionCount, _cursor;
        float _emptyOptionsAge, _rerollFeedbackUntil;
        const float EmptyOptionsGrace = 2.5f;
        ChoiceButton _rerollButton;

        void LayoutBoxes(int count)
        {
            _shownOptionCount = count;
            float width = count == 4 ? 146f : 176f;
            const float gap = 10f;
            float total = count * width + (count - 1) * gap;
            for (int i = 0; i < MaxOptions; i++)
            {
                _boxRects[i].sizeDelta = new Vector2(width, 164f);
                _boxRects[i].anchoredPosition = new Vector2(
                    -total / 2f + width / 2f + i * (width + gap), 10f);
            }
        }

        Text CardText(RectTransform panel, Font font, int size, float top, float height, string name)
        {
            var text = UiKit.CreateText(panel, font, "", size, UiKit.TextMain, TextAnchor.UpperCenter, name);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-16f, height);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        void Start()
        {
            UiKit.EnsureEventSystem();
            var canvas = UiKit.CreateCanvas("RewardCanvas", 70);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;
            UiKit.CreateDim(canvas.transform, new Color(0f, 0.01f, 0.05f, 0.7f)).raycastTarget = true;
            // Keep the requested portrait-free reward screen.
            _contextText = UiKit.CreateCornerText(canvas.transform, _font, "", 10,
                UiKit.TextDim, new Vector2(0.5f, 1f), new Vector2(0f, -24f),
                TextAnchor.UpperCenter, "Context");
            _titleText = UiKit.CreateCornerText(canvas.transform, _fontBold, UiText.RewardTitle, 16,
                UiKit.TextAccent, new Vector2(0.5f, 1f), new Vector2(0f, -44f),
                TextAnchor.UpperCenter, "Title");
            for (int i = 0; i < MaxOptions; i++)
            {
                var panel = UiKit.CreatePanel(canvas.transform, new Vector2(176f, 164f), $"Option{i}");
                _boxRects[i] = panel;
                _boxBorders[i] = panel.GetComponent<Image>();
                _boxMarkers[i] = CardText(panel, _font, 9, 8f, 14f, "Selection");
                _boxTitles[i] = CardText(panel, _fontBold, 11, 28f, 30f, "Name");
                _boxTexts[i] = CardText(panel, _font, 10, 65f, 44f, "Effect");
                _boxCosts[i] = CardText(panel, _font, 9, 118f, 42f, "Cost");
                int index = i;
                _buttons[i] = ChoiceButton.Create(_boxBorders[i], () => Choose(index), () => SetCursor(index));
            }
            var reroll = UiKit.CreatePanel(canvas.transform, new Vector2(244f, 40f), "Reroll");
            reroll.anchorMin = reroll.anchorMax = new Vector2(0.5f, 0f);
            reroll.anchoredPosition = new Vector2(0f, 55f);
            _rerollLabel = UiKit.CreateTextStretch(reroll, _font, "", 10,
                UiKit.TextAccent, TextAnchor.MiddleCenter, 4f, "Label");
            _rerollButton = ChoiceButton.Create(reroll.GetComponent<Image>(), OnReroll);
            _rerollStatus = UiKit.CreateCornerText(canvas.transform, _font, "", 10, UiKit.TextDim,
                new Vector2(0.5f, 0f), new Vector2(0f, 88f), TextAnchor.MiddleCenter, "RerollStatus");
            _hints = UiKit.CreateCornerText(canvas.transform, _font, "", 10, UiKit.TextDim,
                new Vector2(0.5f, 0f), new Vector2(0f, 16f), TextAnchor.MiddleCenter, "Hints");
            _hints.rectTransform.sizeDelta = new Vector2(620f, 20f);
            _root.SetActive(false);
        }

        void SetCursor(int index)
        {
            if (!_director.CanInteractWithChoices) return;
            _cursor = Mathf.Clamp(index, 0, _shownOptionCount - 1);
            RefreshSelection();
        }

        void RefreshSelection()
        {
            for (int i = 0; i < _shownOptionCount; i++)
            {
                bool selected = i == _cursor;
                _boxBorders[i].color = selected ? UiKit.TextAccent : UiKit.PanelBorder;
                _boxMarkers[i].color = selected ? UiKit.TextAccent : UiKit.TextDim;
                _boxMarkers[i].text = (UiPlatform.TouchMode ? "" : $"[{i + 1}]  ")
                    + (selected ? "> SELECTED" : "CHOOSE");
            }
        }

        void OnReroll()
        {
            if (_director == null || !_director.RerollRewards()) return;
            _labelsBuilt = false;
            _rerollFeedbackUntil = Time.unscaledTime + 1.8f;
            RefreshCards();
            UpdateRerollButton();
        }

        void UpdateRerollButton()
        {
            int cost = _director.RewardRerollCost;
            int balance = _director.CapsuleBalance;
            bool can = _director.CanRerollRewards;
            _rerollButton.interactable = can;
            _rerollLabel.text = $"REROLL  {cost} CAPS" + (UiPlatform.TouchMode ? "" : "  [R / (Y)]");
            _rerollLabel.color = can ? UiKit.TextAccent : UiKit.TextDim;
            string status = balance < cost
                ? $"NEED {cost - balance} MORE CAPS  |  HAVE {balance}"
                : $"HAVE {balance} CAPS  |  AFTER REROLL {balance - cost}";
            bool rerolled = Time.unscaledTime < _rerollFeedbackUntil;
            _rerollStatus.text = rerolled ? $"REROLLED  |  {balance} CAPS LEFT" : status;
            _rerollStatus.color = rerolled ? UiKit.TextAccent : UiKit.TextDim;
        }

        void Choose(int index)
        {
            if (_director == null || !_director.ChooseReward(index)) return;
            _labelsBuilt = false;
            _rerollFeedbackUntil = 0f;
            // Hide immediately; a second reward round is rebuilt on the next Update.
            _root.SetActive(false);
        }

        void RefreshCards()
        {
            var options = _director.RewardOptions;
            if (options == null || options.Count == 0) return;
            _labelsBuilt = true;
            _cursor = 0;
            _titleText.text = _director.RewardKind == RewardSelectionKind.MidStage
                ? UiText.MidRewardTitle : UiText.RewardTitle;
            _contextText.text = _director.StageContextLabel;
            LayoutBoxes(Mathf.Min(options.Count, MaxOptions));
            _hints.text = UiPlatform.TouchMode ? UiText.ChoiceHintsTouch
                : $"[1]-[{_shownOptionCount}] PICK   LEFT / RIGHT MOVE   (A) / ENTER CONFIRM";
            for (int i = 0; i < MaxOptions; i++)
            {
                _boxRects[i].gameObject.SetActive(i < _shownOptionCount);
                if (i >= _shownOptionCount) continue;
                string description = Describe(options[i]);
                int split = description.IndexOf('\n');
                _boxTitles[i].text = split < 0 ? description : description.Substring(0, split);
                _boxTexts[i].text = split < 0 ? "" : description.Substring(split + 1);
                bool hasCost = options[i].Costs != null && options[i].Costs.Count > 0;
                _boxCosts[i].text = DescribeCosts(options[i]);
                _boxCosts[i].color = hasCost ? new Color(1f, 0.45f, 0.38f) : UiKit.TextDim;
            }
            RefreshSelection();
        }

        void Update()
        {
            if (_director == null || _root == null) return;
            bool awaiting = _director.AwaitingReward && !_director.BossDeathCinematicActive;
            _root.SetActive(awaiting);
            if (!awaiting)
            {
                _labelsBuilt = false;
                _emptyOptionsAge = _rerollFeedbackUntil = 0f;
                return;
            }
            var options = _director.RewardOptions;
            if (options == null || options.Count == 0)
            {
                _labelsBuilt = false;
                foreach (var rect in _boxRects) rect.gameObject.SetActive(false);
                _rerollButton.interactable = false;
                _titleText.text = "WAITING FOR REWARDS";
                _rerollStatus.text = "NO CARDS AVAILABLE";
                _hints.text = "";
                _emptyOptionsAge += Time.unscaledDeltaTime;
                if (_emptyOptionsAge > EmptyOptionsGrace)
                {
                    _emptyOptionsAge = 0f;
                    Debug.LogError($"[RewardScreen] No reward options (kind={_director.RewardKind}).");
                    _titleText.text = "REWARD ERROR - EMPTY OPTIONS";
                }
                return;
            }
            _emptyOptionsAge = 0f;
            if (!_labelsBuilt || _shownOptionCount != options.Count) RefreshCards();
            UpdateRerollButton();
            foreach (var button in _buttons) button.interactable = _director.CanInteractWithChoices;
            if (!_director.CanInteractWithChoices) return;

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            // Reroll consumes this frame before any confirmation of the old cards.
            if ((kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame))
            { OnReroll(); return; }
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) { Choose(0); return; }
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) { Choose(1); return; }
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) { Choose(2); return; }
                if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) { Choose(3); return; }
            }
            int move = 0;
            if ((kb != null && kb.leftArrowKey.wasPressedThisFrame)
                || (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame))) move = -1;
            if ((kb != null && kb.rightArrowKey.wasPressedThisFrame)
                || (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame))) move = 1;
            if (move != 0) SetCursor(_cursor + move);
            if ((kb != null && kb.enterKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame))
                Choose(_cursor);
        }

        static string DescribeCosts(in RewardOption option)
        {
            if (option.Costs == null || option.Costs.Count == 0) return "NO COST";
            var sb = new System.Text.StringBuilder("COST FOR THIS RUN");
            for (int i = 0; i < option.Costs.Count; i++)
            {
                var cost = option.Costs[i];
                if (cost.Type == RewardEffectType.CapsuleDropWeightDown)
                {
                    // A spawn weight is not a percentage or a number of lost capsules.
                    sb.Append("\nFEWER CAPSULE DROPS");
                    continue;
                }
                string label;
                switch (cost.Type)
                {
                    case RewardEffectType.ShieldMaxDown: label = "SHIELD CAP"; break;
                    case RewardEffectType.MoveSpeedDown: label = "SPEED"; break;
                    case RewardEffectType.BombMaxDown: label = "BOMB CAP"; break;
                    default: label = cost.Type.ToString().ToUpperInvariant(); break;
                }
                sb.Append('\n').Append(label).Append(" -").Append(cost.Amount);
            }
            return sb.ToString();
        }

        static string Describe(in RewardOption option)
        {
            switch (option.Type)
            {
                case RewardType.Capsules:
                    return $"CAPSULES +{option.Amount}\nMoves gauge selection\nAdds reroll currency";
                case RewardType.SlotLevel:
                    return $"{SlotName(option.Slot)} +{option.Amount}\n{SlotEffect(option.Slot)}";
                case RewardType.RepairHp: return $"SHIELD +{option.Amount}\nRestores shield stock\nup to capacity";
                case RewardType.FireRateUp: return $"RAPID FIRE +{option.Amount}\nShorter firing interval";
                case RewardType.DamageUp: return $"FIREPOWER +{option.Amount}\nMain shot damage +{2L * option.Amount}";
                case RewardType.MoveSpeedUp: return $"ENGINE +{option.Amount}\nFaster ship movement";
                case RewardType.Modifier: return ModifierName(option.ModifierId);
                case RewardType.BombStock: return $"BOMBS +{option.Amount}\nAdds bomb stock\nup to capacity";
                case RewardType.MissileFamily:
                    switch (option.MissileFamily)
                    {
                        case MissileFamily.SpreadBomb: return "SPREAD BOMB\nSwitch missile type\nWide bomb spread";
                        case MissileFamily.PiercingLance: return "PIERCING LANCE\nSwitch missile type\nPierces enemies";
                        case MissileFamily.DownwardDrop: return "DOWNWARD DROP\nSwitch missile type\nDrops below your ship";
                        case MissileFamily.Homing: return "HOMING MISSILE\nSwitch missile type\nTracks enemies";
                        default: return "STRAIGHT MISSILE\nSwitch missile type\nFires straight ahead";
                    }
                case RewardType.OptionFormation:
                    switch (option.OptionFormation)
                    {
                        case OptionFormation.Fixed: return "FIXED FORMATION\nDrones hold positions\naround your ship";
                        case OptionFormation.Orbit: return "ORBIT FORMATION\nDrones circle your ship";
                        default: return "TRAIL FORMATION\nDrones follow your path";
                    }
                case RewardType.PrimaryWeaponFamily:
                    return option.PrimaryWeaponFamily.ToString().ToUpperInvariant() + "\nReplaces your main gun";
                default: return option.Type.ToString().ToUpperInvariant();
            }
        }

        static string SlotEffect(PowerUpSlot slot)
        {
            switch (slot)
            {
                case PowerUpSlot.MainShot: return "Stronger front gun";
                case PowerUpSlot.Missile: return "Upgrades missile fire";
                case PowerUpSlot.Option: return "Adds a support drone";
                case PowerUpSlot.Shield: return "Upgrades shield level\nand restores stock";
                case PowerUpSlot.Speed: return "Faster ship movement";
                case PowerUpSlot.Double: return "Activates double-shot mode";
                case PowerUpSlot.Laser: return "Activates laser mode";
                case PowerUpSlot.Triple: return "Activates triple-shot mode";
                default: return "Upgrades this gauge slot";
            }
        }

        static string ModifierName(BattleModifier modifier)
        {
            switch (modifier)
            {
                case BattleModifier.PierceShot: return "PIERCE SHOT\nShots pierce an extra enemy";
                case BattleModifier.Ricochet: return "RICOCHET\nShots bounce to nearby foes";
                case BattleModifier.HomingMissile: return "HOMING MISSILE\nMissiles seek enemies";
                case BattleModifier.KillExplosion: return "KILL EXPLOSION\nKills damage nearby foes";
                default: return modifier.ToString();
            }
        }

        static string SlotName(PowerUpSlot slot)
        {
            return slot == PowerUpSlot.MainShot ? "SHOT" : slot.ToString().ToUpperInvariant();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Shmup.Core.Content;
using Shmup.Core.Generation;
using Shmup.Core.Simulation;
using Shmup.Presentation.Battle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Shmup.Presentation.Tests
{
    public sealed class ChoiceScreenTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root, _ownedEventSystem;
        BattleDirector _director;
        RewardScreen _rewards;
        ContractScreen _contracts;
        RunManager _run;
        GameDataSet _data;
        Keyboard _keyboard;
        Gamepad _pad;
        InputSettings _originalSettings, _testSettings;
        bool _touch, _audioPaused;
        float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _touch = UiPlatform.ForceTouch;
            _audioPaused = AudioListener.pause;
            _timeScale = Time.timeScale;
            UiPlatform.ForceTouch = false;
            Time.timeScale = 1f;
            _originalSettings = InputSystem.settings;
            _testSettings = Object.Instantiate(_originalSettings);
            InputSystem.settings = _testSettings;
            _testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
            if (EventSystem.current == null)
                _ownedEventSystem = new GameObject("Choice test events", typeof(EventSystem));
            _data = GameDataParser.Parse(Read("enemies"), Read("weapons"), Read("waves"),
                Read("rewards"), Read("ships"), Read("scoring"));
            _run = new RunManager(12345UL, new SegmentStageGenerator(_data.StageGeneration),
                _data.CreateBattleSimConfig(), _data.BattleContent, _data.CreatePowerUpGauge(_data.DefaultShip),
                _data.Rewards, _data.DefaultShip);
            _root = new GameObject("Choice UI fixture");
            _root.SetActive(false);
            _director = _root.AddComponent<BattleDirector>();
            Set(_director, "_run", _run);
            Set(_director, "_sim", _run.Battle);
            Set(_director, "_recordingActive", true);
            var player = new GameObject("Player view");
            player.transform.SetParent(_root.transform);
            Set(_director, "_playerTransform", player.transform);
            _rewards = _root.AddComponent<RewardScreen>();
            _contracts = _root.AddComponent<ContractScreen>();
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11.ttf");
            var bold = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11-Bold.ttf");
            foreach (var view in new MonoBehaviour[] { _rewards, _contracts })
            {
                Set(view, "_director", _director);
                Set(view, "_font", font);
                Set(view, "_fontBold", bold);
                Invoke(view, "Start");
            }
            _root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            if (_ownedEventSystem != null) Object.DestroyImmediate(_ownedEventSystem);
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.RemoveDevice(_pad);
            InputSystem.settings = _originalSettings;
            Object.DestroyImmediate(_testSettings);
            UiPlatform.ForceTouch = _touch;
            Time.timeScale = _timeScale;
            AudioListener.pause = _audioPaused;
            typeof(OptionsScreen).GetField("_pauseInputConsumedFrame", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, -1);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void AllContractAdjustedRewardsAreVisibleAndInsideReferenceWidth(int count)
        {
            BeginRewards(count);
            Invoke(_rewards, "Update");
            var rects = Get<RectTransform[]>(_rewards, "_boxRects");
            for (int i = 0; i < rects.Length; i++)
            {
                Assert.AreEqual(i < count, rects[i].gameObject.activeSelf);
                if (i >= count) continue;
                Assert.LessOrEqual(Mathf.Abs(rects[i].anchoredPosition.x) + rects[i].sizeDelta.x / 2f, 320f);
            }
            Assert.AreEqual(count, _run.RewardOptions.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FourthCardCanBePickedAndSameFrameCannotAlsoChooseAContract(bool pointer)
        {
            BeginRewards(4);
            Invoke(_rewards, "Update");
            if (pointer)
            {
                var button = Get<ChoiceButton[]>(_rewards, "_buttons")[3];
                ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current)
                    { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            }
            else
            {
                Press(Key.Digit4);
                Invoke(_rewards, "Update");
            }
            CollectionAssert.AreEqual(new[] { 3 }, Get<List<int>>(_director, "_recordedChoices"));
            Assert.AreEqual(RunState.AwaitingContract, _run.State);
            Assert.IsFalse(_director.ChooseContract(0));
            Assert.IsEmpty(Get<List<int>>(_director, "_recordedContractChoices"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RerollAndConfirmTogetherOnlyChargeOnceAndRefreshCards(bool gamepad)
        {
            BeginRewards(4);
            Set(_run, "_capsuleBalance", 8);
            Invoke(_rewards, "Update");
            if (gamepad)
            {
                InputSystem.QueueStateEvent(_pad, new GamepadState()
                    .WithButton(GamepadButton.North).WithButton(GamepadButton.South));
                InputSystem.Update();
            }
            else Press(Key.R, Key.Enter);
            Invoke(_rewards, "Update");
            Assert.AreEqual(4, _run.CapsuleBalance);
            Assert.AreEqual(RunState.AwaitingReward, _run.State);
            Assert.IsFalse(_director.RerollRewards());
            Assert.IsFalse(_director.ChooseReward(0));
            CollectionAssert.AreEqual(new[] { BattleDirector.RerollChoiceSentinel }, Get<List<int>>(_director, "_recordedChoices"));
            StringAssert.Contains("REROLLED", Get<Text>(_rewards, "_rerollStatus").text);
            AssertCardDescriptionsMatchCurrentOptions();
        }

        [Test]
        public void InsufficientCurrencyDisablesRerollAndExplainsShortfall()
        {
            BeginRewards(2);
            Set(_run, "_capsuleBalance", 2);
            Invoke(_rewards, "Update");
            Assert.IsFalse(Get<ChoiceButton>(_rewards, "_rerollButton").interactable);
            StringAssert.Contains("NEED 2 MORE", Get<Text>(_rewards, "_rerollStatus").text);
            Press(Key.R);
            Invoke(_rewards, "Update");
            Assert.AreEqual(2, _run.CapsuleBalance);
            Assert.IsEmpty(Get<List<int>>(_director, "_recordedChoices"));
        }

        [TestCase("paused")]
        [TestCase("replay")]
        [TestCase("cinematic")]
        public void BlockedRewardInputDoesNotSpendOrRecord(string reason)
        {
            BeginRewards(3);
            Set(_run, "_capsuleBalance", 8);
            if (reason == "paused") Time.timeScale = 0f;
            if (reason == "replay") Set(_director, "_replayMode", true);
            if (reason == "cinematic") SetProperty(_director, "BossDeathCinematicRemaining", 1f);
            Assert.IsFalse(_director.ChooseReward(0));
            Assert.IsFalse(_director.RerollRewards());
            Assert.AreEqual(8, _run.CapsuleBalance);
            Assert.IsEmpty(Get<List<int>>(_director, "_recordedChoices"));
        }

        [Test]
        public void InvalidRewardIndexIsNeverWrittenIntoReplay()
        {
            BeginRewards(2);
            Assert.IsFalse(_director.ChooseReward(2));
            Assert.IsFalse(_director.ChooseReward(-1));
            Assert.IsEmpty(Get<List<int>>(_director, "_recordedChoices"));
        }

        [Test]
        public void ResumeConfirmationCannotPickRewardUnderneathPauseMenu()
        {
            BeginRewards(3);
            var pause = _root.AddComponent<PauseScreen>();
            Set(pause, "_director", _director);
            Invoke(pause, "SetPaused", true);
            Invoke(pause, "SetPaused", false);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(_director.ChooseReward(0));
            Assert.IsEmpty(Get<List<int>>(_director, "_recordedChoices"));
        }

        [Test]
        public void ContractStickNavigationAndPauseRespectTheVisibleCursor()
        {
            SetProperty(_run, "State", RunState.AwaitingContract);
            var route = new ContractOption(_data.Contracts.All[0], "fortress");
            Set(_run, "_contractOptions", new[] { route, route, route });
            Invoke(_contracts, "Update");
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = Vector2.right });
            InputSystem.Update();
            Invoke(_contracts, "Update");
            Assert.AreEqual(1, Get<int>(_contracts, "_cursor"));
            StringAssert.Contains("SELECTED", Get<Text[]>(_contracts, "_boxMarkers")[1].text);
            Time.timeScale = 0f;
            Assert.IsFalse(_director.ChooseContract(1));
            Assert.IsEmpty(Get<List<int>>(_director, "_recordedContractChoices"));
            Set(_run, "_contractOptions", Array.Empty<ContractOption>());
            Invoke(_contracts, "Update");
            foreach (var rect in Get<RectTransform[]>(_contracts, "_boxRects")) Assert.IsFalse(rect.gameObject.activeSelf);
        }

        [Test]
        public void PointerHighlightAndEventSystemSubmitCannotUseDifferentCursors()
        {
            BeginRewards(3);
            Invoke(_rewards, "Update");
            var button = Get<ChoiceButton[]>(_rewards, "_buttons")[2];
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            Assert.AreEqual(2, Get<int>(_rewards, "_cursor"));
            ExecuteEvents.Execute(button.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.AreEqual(RunState.AwaitingReward, _run.State);
            Assert.IsEmpty(Get<List<int>>(_director, "_recordedChoices"));
            StringAssert.Contains("SELECTED", Get<Text[]>(_rewards, "_boxMarkers")[2].text);
        }

        [Test]
        public void EmptyRewardOptionsCannotLeaveOldClickableCardsOrReroll()
        {
            BeginRewards(3);
            Invoke(_rewards, "Update");
            Set(_run, "_rewardOptions", Array.Empty<RewardOption>());
            Invoke(_rewards, "Update");
            foreach (var rect in Get<RectTransform[]>(_rewards, "_boxRects")) Assert.IsFalse(rect.gameObject.activeSelf);
            Assert.IsFalse(Get<ChoiceButton>(_rewards, "_rerollButton").interactable);
        }

        [Test]
        public void EveryAuthoredRewardFitsTheFourCardLayoutIncludingItsCosts()
        {
            foreach (var definition in _data.Rewards.All)
            {
                var option = new RewardOption(definition.Id, definition.Type, definition.Slot, definition.Amount,
                    definition.ModifierId, definition.MissileFamily, definition.OptionFormation,
                    definition.PrimaryWeaponFamily, definition.ModifierKey, definition.Costs);
                SetProperty(_run, "State", RunState.AwaitingReward);
                Set(_run, "_rewardOptions", new[] { option, option, option, option });
                Set(_rewards, "_labelsBuilt", false);
                Invoke(_rewards, "Update");
                Canvas.ForceUpdateCanvases();
                foreach (string field in new[] { "_boxTitles", "_boxTexts", "_boxCosts" })
                    foreach (var text in Get<Text[]>(_rewards, field))
                        Assert.LessOrEqual(text.preferredHeight, text.rectTransform.rect.height + 1f, definition.Id + " / " + field);
            }
        }

        [Test]
        public void ContractScoreAndBanAreIndependentAndEveryAuthoredEffectIsPresent()
        {
            SetProperty(_run, "State", RunState.AwaitingContract);
            foreach (var definition in _data.Contracts.All)
            {
                var option = new ContractOption(definition,
                    definition.DestinationKind == ContractDestinationKind.NextStage ? "fortress" : null);
                Set(_run, "_contractOptions", new[] { option });
                Set(_contracts, "_built", false);
                Invoke(_contracts, "Update");
                string text = Get<Text[]>(_contracts, "_boxTexts")[0].text;
                foreach (var effect in option.Effects)
                {
                    string line = (string)typeof(ContractScreen).GetMethod("DescribeEffect", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { effect });
                    StringAssert.Contains(line, text, definition.Id);
                }
                if (definition.Id == "spartan_protocol")
                {
                    StringAssert.Contains("SCORE x1.6", text);
                    StringAssert.Contains("GAUGE INPUT LOCKED", text);
                    StringAssert.DoesNotContain("LOCKED x", text);
                }
                // Next-stage previews consume 54px even when test sprites are absent.
                if (option.DestinationKind == ContractDestinationKind.NextStage)
                    Get<Text[]>(_contracts, "_boxTexts")[0].rectTransform.offsetMax = new Vector2(-12f, -102f);
                Canvas.ForceUpdateCanvases();
                var body = Get<Text[]>(_contracts, "_boxTexts")[0];
                Assert.LessOrEqual(body.preferredHeight, body.rectTransform.rect.height + 1f, definition.Id);
            }
        }

        void BeginRewards(int count)
        {
            var kind = count == 1 ? RewardSelectionKind.MidStage : RewardSelectionKind.Main;
            int baseCount = count == 1 ? RunManager.MidStageRewardOptionCount : RunManager.MainRewardOptionCount;
            SetProperty(_run, "ActiveContract", new ContractDefinition("test", 1, ContractRiskTier.High,
                rewardOptionCountDelta: count - baseCount));
            Invoke(_run, "BeginRewardSelection", kind);
        }

        void AssertCardDescriptionsMatchCurrentOptions()
        {
            for (int i = 0; i < _run.RewardOptions.Count; i++)
            {
                string expected = (string)typeof(RewardScreen).GetMethod("Describe", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { _run.RewardOptions[i] });
                Assert.AreEqual(expected.Split('\n')[0], Get<Text[]>(_rewards, "_boxTitles")[i].text);
            }
        }

        void Press(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        static string Read(string name) => File.ReadAllText("GameData/" + name + ".json");
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        static void SetProperty(object target, string property, object value) => target.GetType().GetProperty(property).SetValue(target, value);
        static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    }
}

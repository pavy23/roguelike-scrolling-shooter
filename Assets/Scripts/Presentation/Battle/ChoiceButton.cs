using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// Choice screens own keyboard/pad confirmation. Pointer clicks use Button's pressed
    /// feedback, but EventSystem Submit must not confirm a second (or stale) choice.
    /// </summary>
    public sealed class ChoiceButton : Button
    {
        UnityAction _highlight;

        public static ChoiceButton Create(Image image, UnityAction choose, UnityAction highlight = null)
        {
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<ChoiceButton>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = colors.highlightedColor = colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.65f, 0.75f, 0.9f, 1f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.onClick.AddListener(choose);
            button._highlight = highlight;
            return button;
        }

        public override void OnPointerEnter(PointerEventData data)
        {
            base.OnPointerEnter(data);
            if (IsActive() && IsInteractable()) _highlight?.Invoke();
        }

        public override void OnSubmit(BaseEventData data) { }
    }
}

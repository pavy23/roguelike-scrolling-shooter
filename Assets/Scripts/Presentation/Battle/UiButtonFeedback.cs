using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    public sealed class UiButtonFeedback : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        Button _button;
        public void Initialize(Button button)
        {
            _button = button;
            button.onClick.AddListener(() => UiAudio.Play(UiCue.Confirm));
        }
        void Highlight()
        {
            if (_button != null && _button.IsInteractable()) UiAudio.Play(UiCue.Navigate);
        }
        public void OnSelect(BaseEventData data) => Highlight();
        public void OnPointerEnter(PointerEventData data) => Highlight();
    }
}

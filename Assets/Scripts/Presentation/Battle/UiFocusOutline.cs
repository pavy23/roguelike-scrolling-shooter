using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>Extra frame for keyboard/controller selection; never receives pointer events.</summary>
    public sealed class UiFocusOutline : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        GameObject _outline;
        Selectable _selectable;

        public void Initialize()
        {
            _selectable = GetComponent<Selectable>();
            _outline = new GameObject("FocusOutline", typeof(RectTransform), typeof(Image));
            _outline.transform.SetParent(transform, false);
            var image = _outline.GetComponent<Image>();
            image.sprite = UiSkin.Frame;
            image.type = Image.Type.Sliced;
            image.color = UiKit.TextAccent;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-2f, -2f);
            rect.offsetMax = new Vector2(2f, 2f);
            Refresh();
        }

        public void OnSelect(BaseEventData data) => Show(true);
        public void OnDeselect(BaseEventData data) => Show(false);
        void OnEnable() => Refresh();
        void OnDisable() => Show(false);
        void Refresh() => Show(EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == gameObject);
        void Show(bool selected)
        {
            if (_outline != null)
                _outline.SetActive(selected && _selectable != null && _selectable.IsInteractable());
        }
    }
}

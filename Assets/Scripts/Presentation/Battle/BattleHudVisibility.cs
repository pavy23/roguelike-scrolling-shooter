using UnityEngine;
using UnityEngine.UI;

namespace Shmup.Presentation.Battle
{
    /// <summary>
    /// Combat readouts yield to choice, result and settings screens. Keep their objects
    /// alive so closing a modal restores the same HUD, including current gauge state.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    public sealed class BattleHudVisibility : MonoBehaviour
    {
        BattleDirector _director;
        Canvas _canvas;
        GraphicRaycaster _raycaster;

        public void Initialize(BattleDirector director)
        {
            _director = director;
            _canvas = GetComponent<Canvas>();
            _raycaster = GetComponent<GraphicRaycaster>();
            LateUpdate();
        }

        void LateUpdate()
        {
            if (_canvas == null) return;
            // LateUpdate observes transitions made by any of this frame's Update callbacks.
            bool visible = _director != null && _director.IsPlaying && Time.timeScale > 0f
                && !OptionsScreen.IsOpen && !AudioSettingsPanel.BlocksInput;
            _canvas.enabled = visible;
            if (_raycaster != null) _raycaster.enabled = visible;
        }
    }
}

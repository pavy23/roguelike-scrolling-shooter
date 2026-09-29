using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shmup.Presentation.Battle
{
    /// <summary>Saved bindings and their labels share the same effective InputAction paths.</summary>
    public static class PlayerBindings
    {
        public const string PrefKey = "rss.bindings";
        public static int Revision { get; private set; }

        public static void Load(InputActionAsset actions)
        {
            if (actions == null) return;
            string json = PlayerPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { actions.LoadBindingOverridesFromJson(json); }
                catch (ArgumentException)
                {
                    actions.RemoveAllBindingOverrides();
                    Debug.LogWarning("[Bindings] Invalid saved bindings; using defaults.");
                }
            }
            Revision++;
        }

        public static void Save(InputActionAsset actions)
        {
            if (actions == null) return;
            PlayerPrefs.SetString(PrefKey, actions.SaveBindingOverridesAsJson());
            SaveFlush.Request();
            Revision++;
        }

        public static void Reset(InputActionAsset actions)
        {
            if (actions == null) return;
            actions.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefKey);
            SaveFlush.Request();
            Revision++;
        }

        public static int KeyboardIndex(InputAction action, string part = null)
        {
            if (action == null) return -1;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (!binding.isComposite && binding.path != null
                    && binding.path.StartsWith("<Keyboard>", StringComparison.Ordinal)
                    && (part == null || string.Equals(binding.name, part, StringComparison.OrdinalIgnoreCase)))
                    return i;
            }
            return -1;
        }

        public static string KeyboardLabel(InputAction action, string part = null)
        {
            int index = KeyboardIndex(action, part);
            return index >= 0 && !string.IsNullOrEmpty(action.bindings[index].effectivePath)
                ? action.GetBindingDisplayString(index) : "UNBOUND";
        }

        public static string GamepadLabel(InputAction action)
        {
            if (action != null)
                for (int i = 0; i < action.bindings.Count; i++)
                    if (!action.bindings[i].isComposite
                        && action.bindings[i].path?.StartsWith("<Gamepad>", StringComparison.Ordinal) == true)
                        return string.IsNullOrEmpty(action.bindings[i].effectivePath)
                            ? "UNBOUND" : action.GetBindingDisplayString(i);
            return "UNBOUND";
        }

        public static string MoveHint(InputAction move) =>
            $"MOVE {KeyboardLabel(move, "up")}/{KeyboardLabel(move, "left")}/"
            + $"{KeyboardLabel(move, "down")}/{KeyboardLabel(move, "right")} / {GamepadLabel(move)}";
    }
}

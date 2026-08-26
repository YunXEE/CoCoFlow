using System;
using UnityEngine.InputSystem;

namespace CoCoFlow.Runtime.Modules.Input
{
    public readonly struct InputActionEvent
    {
        public InputActionEvent(InputAction action, InputActionPhase phase)
        {
            Action = action;
            Phase = phase;
        }

        public InputAction Action { get; }
        public InputActionPhase Phase { get; }
        public bool IsValid => Action != null &&
                               (Phase == InputActionPhase.Performed ||
                                Phase == InputActionPhase.Canceled);
    }

    public interface IInputBindingOverrideStore
    {
        bool TryLoad(string storageKey, out string overrideJson);

        bool TrySave(string storageKey, string overrideJson);
    }
}

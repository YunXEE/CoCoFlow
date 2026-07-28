using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Input
{
    public enum InputPromptFallbackState : byte
    {
        None = 0,
        BindingText = 1
    }

    public readonly struct InputPromptSnapshot
    {
        public InputPromptSnapshot(
            Guid actionId,
            int bindingIndex,
            string bindingDisplay,
            string deviceLayout,
            string controlPath,
            Sprite glyph)
        {
            ActionId = actionId;
            BindingIndex = bindingIndex;
            BindingDisplay = bindingDisplay ?? string.Empty;
            DeviceLayout = deviceLayout ?? string.Empty;
            ControlPath = controlPath ?? string.Empty;
            Glyph = glyph;
            FallbackState = glyph == null
                ? InputPromptFallbackState.BindingText
                : InputPromptFallbackState.None;
        }

        public Guid ActionId { get; }
        public int BindingIndex { get; }
        public string BindingDisplay { get; }
        public string DeviceLayout { get; }
        public string ControlPath { get; }
        public Sprite Glyph { get; }
        public InputPromptFallbackState FallbackState { get; }
        public bool HasBindingDisplay => !string.IsNullOrEmpty(BindingDisplay);
        public bool HasGlyph => Glyph != null;
        public bool IsValid => ActionId != Guid.Empty &&
                               BindingIndex >= 0 &&
                               HasBindingDisplay;
    }
}

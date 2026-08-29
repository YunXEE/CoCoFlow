using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlow.Runtime.Modules.Input
{
    [CreateAssetMenu(
        fileName = "InputGlyphCatalog",
        menuName = "CoCoFlow/Input/Glyph Catalog")]
    public sealed class InputGlyphCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string deviceLayout;
            [SerializeField] private string controlPath;
            [SerializeField] private Sprite glyph;

            public string DeviceLayout => deviceLayout;
            public string ControlPath => controlPath;
            public Sprite Glyph => glyph;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public bool TryResolve(
            string deviceLayout,
            string controlPath,
            out Sprite glyph)
        {
            glyph = null;
            if (string.IsNullOrEmpty(controlPath))
            {
                return false;
            }

            for (int index = 0; index < entries.Count; index++)
            {
                Entry entry = entries[index];
                if (entry == null ||
                    entry.Glyph == null ||
                    !string.Equals(
                        entry.ControlPath,
                        controlPath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        entry.DeviceLayout,
                        deviceLayout,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                glyph = entry.Glyph;
                return true;
            }

            if (string.IsNullOrEmpty(deviceLayout))
            {
                return false;
            }

            for (int index = 0; index < entries.Count; index++)
            {
                Entry entry = entries[index];
                if (entry == null ||
                    entry.Glyph == null ||
                    string.IsNullOrEmpty(entry.DeviceLayout) ||
                    !string.Equals(
                        entry.ControlPath,
                        controlPath,
                        StringComparison.Ordinal) ||
                    !InputSystem.IsFirstLayoutBasedOnSecond(
                        deviceLayout,
                        entry.DeviceLayout))
                {
                    continue;
                }

                glyph = entry.Glyph;
                return true;
            }

            return false;
        }
    }
}

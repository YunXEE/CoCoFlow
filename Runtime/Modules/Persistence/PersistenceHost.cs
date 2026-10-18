using System;
using System.IO;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    /// <summary>Scene-facing entry point into the existing shared save/load flow.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-9000)]
    public sealed class PersistenceHost : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional explicit reference. Leave empty to use the active global ContainerHost, or for a Graph-only project.")]
        private PersistenceContainerHost containerHost;
        [SerializeField, Min(1)] private int maxSaveSlots = 3;
        [SerializeField, Min(0)] private int initialSlotIndex;
        [SerializeField, Tooltip("Editor only. Save and load under Assets/DebugSaves instead of Save Directory.")]
        private bool debugMode;
        [SerializeField, Tooltip("Empty uses Application.persistentDataPath. Relative folders go under it; absolute paths are used directly.")]
        private string saveDirectory = string.Empty;
        private int _previousMaxSlots;
        private int _previousSlot;
        private string _previousDirectory;

        internal static PersistenceHost ActiveHost { get; private set; }
        internal int ConfiguredMaxSaveSlots => Mathf.Max(1, maxSaveSlots);
        internal int ConfiguredInitialSlot => Mathf.Clamp(initialSlotIndex, 0, ConfiguredMaxSaveSlots - 1);
        internal bool UsesDebugDirectory => Application.isEditor && debugMode;

        public void SaveGame(int slotIndex = -1)
        {
            if (!CanUseConfiguration()) return;
            PersistenceSaveLoadSystem.SaveGame(slotIndex);
        }

        public bool LoadGame(int slotIndex = -1)
        {
            return CanUseConfiguration() && PersistenceSaveLoadSystem.LoadGame(slotIndex);
        }

        private bool CanUseConfiguration()
        {
            if (ActiveHost == this) return true;
            Debug.LogError("[Persistence] PersistenceHost must be active with valid configuration before saving or loading.", this);
            return false;
        }

        internal string GetConfiguredSaveDirectory()
        {
            if (UsesDebugDirectory) return Path.GetFullPath(Path.Combine(Application.dataPath, "DebugSaves"));
            string directory = string.IsNullOrWhiteSpace(saveDirectory) ? Application.persistentDataPath :
                Path.IsPathRooted(saveDirectory) ? saveDirectory : Path.Combine(Application.persistentDataPath, saveDirectory);
            return Path.GetFullPath(directory);
        }

        internal PersistenceContainerHost ResolveContainerHost()
        {
            PersistenceContainerHost resolved = containerHost != null ? containerHost : PersistenceContainerHost.ActiveHost;
            if (resolved != null && (resolved != PersistenceContainerHost.ActiveHost || !resolved.isActiveAndEnabled))
                throw new InvalidOperationException("The assigned ContainerHost must be the enabled global ContainerHost.");
            return resolved;
        }

        private void OnEnable()
        {
            if (ActiveHost != null && ActiveHost != this)
            {
                Debug.LogError("[Persistence] Only one PersistenceHost may be active.", this);
                return;
            }
            string directory;
            try { directory = GetConfiguredSaveDirectory(); }
            catch (Exception exception)
            {
                Debug.LogError("[Persistence] Invalid save directory: " + exception.Message, this);
                return;
            }
            _previousMaxSlots = PersistenceSaveLoadSystem.MaxSaveSlots;
            _previousSlot = PersistenceSaveLoadSystem.CurrentSlotIndex;
            _previousDirectory = PersistenceFileStore.SaveDirectoryOverride;
            ActiveHost = this;
            PersistenceSaveLoadSystem.MaxSaveSlots = ConfiguredMaxSaveSlots;
            PersistenceSaveLoadSystem.CurrentSlotIndex = ConfiguredInitialSlot;
            PersistenceFileStore.SaveDirectoryOverride = directory;
        }

        private void OnDisable()
        {
            if (ActiveHost != this) return;
            ActiveHost = null;
            PersistenceSaveLoadSystem.MaxSaveSlots = _previousMaxSlots;
            PersistenceSaveLoadSystem.CurrentSlotIndex = _previousSlot;
            PersistenceFileStore.SaveDirectoryOverride = _previousDirectory;
        }

        private void OnValidate()
        {
            maxSaveSlots = ConfiguredMaxSaveSlots;
            initialSlotIndex = ConfiguredInitialSlot;
        }
    }
}

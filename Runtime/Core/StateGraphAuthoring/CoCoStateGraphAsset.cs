using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    [CreateAssetMenu(fileName = "StateGraph", menuName = "CoCoFlow/State Graph")]
    public sealed class CoCoStateGraphAsset : ScriptableObject
    {
        internal const uint CurrentSchemaVersion = 1U;

        [SerializeField, HideInInspector] private uint schemaVersion = CurrentSchemaVersion;
        [SerializeField, HideInInspector] private CoCoSerializedId128 graphId;
        [SerializeField, HideInInspector] private string assetGuidStamp = string.Empty;
        [SerializeField] private List<CoCoStateGraphLayerRecord> layers =
            new List<CoCoStateGraphLayerRecord>();
        [SerializeField] private List<CoCoStateGraphEventAdapterDeclarationRecord> eventAdapterDeclarations =
            new List<CoCoStateGraphEventAdapterDeclarationRecord>();
        [SerializeField, HideInInspector] private CoCoStateGraphEditorLayout editorLayout =
            new CoCoStateGraphEditorLayout();

        public uint SchemaVersion => schemaVersion;

        public CoCoGraphId GraphId
        {
            get
            {
                CoCoGraphId.TryCreate(graphId.High, graphId.Low, out CoCoGraphId value);
                return value;
            }
        }

        internal CoCoSerializedId128 SerializedGraphId => graphId;
        internal string AssetGuidStamp => assetGuidStamp ?? string.Empty;
        internal List<CoCoStateGraphLayerRecord> Layers =>
            layers ?? (layers = new List<CoCoStateGraphLayerRecord>());
        internal List<CoCoStateGraphEventAdapterDeclarationRecord> EventAdapterDeclarations =>
            eventAdapterDeclarations ??
            (eventAdapterDeclarations = new List<CoCoStateGraphEventAdapterDeclarationRecord>());
        internal CoCoStateGraphEditorLayout EditorLayout =>
            editorLayout ?? (editorLayout = new CoCoStateGraphEditorLayout());

        internal bool EnsureAssetIdentity(string currentAssetGuid)
        {
            if (string.IsNullOrWhiteSpace(currentAssetGuid))
            {
                throw new ArgumentException("An asset GUID is required.", nameof(currentAssetGuid));
            }

            if (string.IsNullOrEmpty(assetGuidStamp))
            {
                if (!graphId.IsValid)
                {
                    graphId = CoCoSerializedId128.NewId();
                }

                assetGuidStamp = currentAssetGuid;
                return true;
            }

            if (string.Equals(assetGuidStamp, currentAssetGuid, StringComparison.Ordinal))
            {
                return false;
            }

            RegenerateTopologyIdsForAssetCopy(currentAssetGuid);
            return true;
        }

        internal void RegenerateTopologyIdsForAssetCopy(string currentAssetGuid)
        {
            if (string.IsNullOrWhiteSpace(currentAssetGuid))
            {
                throw new ArgumentException("An asset GUID is required.", nameof(currentAssetGuid));
            }

            Dictionary<CoCoSerializedId128, CoCoSerializedId128> layerIds =
                new Dictionary<CoCoSerializedId128, CoCoSerializedId128>();
            Dictionary<CoCoSerializedId128, CoCoSerializedId128> stateIds =
                new Dictionary<CoCoSerializedId128, CoCoSerializedId128>();
            Dictionary<CoCoSerializedId128, CoCoSerializedId128> transitionIds =
                new Dictionary<CoCoSerializedId128, CoCoSerializedId128>();

            graphId = CreateCopyId(graphId, currentAssetGuid, "graph");

            foreach (CoCoStateGraphLayerRecord layer in Layers)
            {
                if (layer == null)
                {
                    continue;
                }

                RegisterRemap(layer.LayerId, layerIds, currentAssetGuid, "layer");
                foreach (CoCoStateGraphStateRecord state in layer.States)
                {
                    if (state != null)
                    {
                        RegisterRemap(state.StateId, stateIds, currentAssetGuid, "state");
                    }
                }

                foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
                {
                    if (transition != null)
                    {
                        RegisterRemap(
                            transition.TransitionId,
                            transitionIds,
                            currentAssetGuid,
                            "transition");
                    }
                }
            }

            foreach (CoCoStateGraphLayerRecord layer in Layers)
            {
                if (layer == null)
                {
                    continue;
                }

                layer.LayerId = Remap(layer.LayerId, layerIds);
                layer.InitialStateId = Remap(layer.InitialStateId, stateIds);
                foreach (CoCoStateGraphStateRecord state in layer.States)
                {
                    if (state == null)
                    {
                        continue;
                    }

                    state.StateId = Remap(state.StateId, stateIds);
                    state.ParentStateId = Remap(state.ParentStateId, stateIds);
                    state.InitialChildStateId = Remap(state.InitialChildStateId, stateIds);
                }

                foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
                {
                    if (transition == null)
                    {
                        continue;
                    }

                    transition.TransitionId = Remap(transition.TransitionId, transitionIds);
                    transition.SourceStateId = Remap(transition.SourceStateId, stateIds);
                    transition.TargetStateId = Remap(transition.TargetStateId, stateIds);
                }
            }

            EditorLayout.RemapStateIds(stateIds);

            assetGuidStamp = currentAssetGuid;
        }

        private static void RegisterRemap(
            CoCoSerializedId128 source,
            IDictionary<CoCoSerializedId128, CoCoSerializedId128> remaps,
            string assetGuid,
            string identityKind)
        {
            if (source.IsValid && !remaps.ContainsKey(source))
            {
                remaps.Add(source, CreateCopyId(source, assetGuid, identityKind));
            }
        }

        private static CoCoSerializedId128 CreateCopyId(
            CoCoSerializedId128 source,
            string assetGuid,
            string identityKind)
        {
            ulong high = 14695981039346656037UL;
            ulong low = 1099511628211UL;
            AddHash(ref high, assetGuid);
            AddHash(ref high, identityKind);
            AddHash(ref high, source.High);
            AddHash(ref high, source.Low);
            AddHash(ref low, identityKind);
            AddHash(ref low, source.Low);
            AddHash(ref low, source.High);
            AddHash(ref low, assetGuid);
            if (high == 0UL && low == 0UL)
            {
                low = 1UL;
            }

            return new CoCoSerializedId128(high, low);
        }

        private static void AddHash(ref ulong hash, string value)
        {
            if (value == null)
            {
                AddHash(ref hash, ulong.MaxValue);
                return;
            }

            AddHash(ref hash, unchecked((ulong)value.Length));
            for (int index = 0; index < value.Length; index++)
            {
                AddHash(ref hash, value[index]);
            }
        }

        private static void AddHash(ref ulong hash, ulong value)
        {
            const ulong prime = 1099511628211UL;
            for (int byteIndex = 0; byteIndex < sizeof(ulong); byteIndex++)
            {
                hash ^= (byte)(value >> (byteIndex * 8));
                hash *= prime;
            }
        }

        private static CoCoSerializedId128 Remap(
            CoCoSerializedId128 source,
            IReadOnlyDictionary<CoCoSerializedId128, CoCoSerializedId128> remaps)
        {
            return source.IsValid && remaps.TryGetValue(source, out CoCoSerializedId128 remapped)
                ? remapped
                : source;
        }
    }
}

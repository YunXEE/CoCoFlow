using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Runtime.StateGraph
{
    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core.StateGraphAuthoring", sourceClassName: "CoCoStateGraphAsset")]
    [CreateAssetMenu(fileName = "StateGraph", menuName = "CoCoFlow/State Graph")]
    public sealed class StateGraphAsset : ScriptableObject
    {
        internal const uint CurrentSchemaVersion = 1U;

        [SerializeField, HideInInspector] private uint schemaVersion = CurrentSchemaVersion;
        [SerializeField, HideInInspector] private SerializedId128 graphId;
        [SerializeField, HideInInspector] private string assetGuidStamp = string.Empty;
        [SerializeField] private List<StateGraphLayerRecord> layers =
            new List<StateGraphLayerRecord>();
        [SerializeField] private List<StateGraphEventAdapterDeclarationRecord> eventAdapterDeclarations =
            new List<StateGraphEventAdapterDeclarationRecord>();
        [SerializeField, HideInInspector] private StateGraphEditorLayout editorLayout =
            new StateGraphEditorLayout();

        public uint SchemaVersion => schemaVersion;

        public GraphId GraphId
        {
            get
            {
                GraphId.TryCreate(graphId.High, graphId.Low, out GraphId value);
                return value;
            }
        }

        internal SerializedId128 SerializedGraphId => graphId;
        internal string AssetGuidStamp => assetGuidStamp ?? string.Empty;
        internal List<StateGraphLayerRecord> Layers =>
            layers ?? (layers = new List<StateGraphLayerRecord>());
        internal List<StateGraphEventAdapterDeclarationRecord> EventAdapterDeclarations =>
            eventAdapterDeclarations ??
            (eventAdapterDeclarations = new List<StateGraphEventAdapterDeclarationRecord>());
        internal StateGraphEditorLayout EditorLayout =>
            editorLayout ?? (editorLayout = new StateGraphEditorLayout());

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
                    graphId = SerializedId128.NewId();
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

            Dictionary<SerializedId128, SerializedId128> layerIds =
                new Dictionary<SerializedId128, SerializedId128>();
            Dictionary<SerializedId128, SerializedId128> stateIds =
                new Dictionary<SerializedId128, SerializedId128>();
            Dictionary<SerializedId128, SerializedId128> transitionIds =
                new Dictionary<SerializedId128, SerializedId128>();

            graphId = CreateCopyId(graphId, currentAssetGuid, "graph");

            foreach (StateGraphLayerRecord layer in Layers)
            {
                if (layer == null)
                {
                    continue;
                }

                RegisterRemap(layer.LayerId, layerIds, currentAssetGuid, "layer");
                foreach (StateGraphStateRecord state in layer.AllStates)
                {
                    if (state != null)
                    {
                        RegisterRemap(state.StateId, stateIds, currentAssetGuid, "state");
                    }
                }

                foreach (StateGraphTransitionRecord transition in layer.Transitions)
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

            foreach (StateGraphLayerRecord layer in Layers)
            {
                if (layer == null)
                {
                    continue;
                }

                layer.LayerId = Remap(layer.LayerId, layerIds);
                layer.InitialStateId = Remap(layer.InitialStateId, stateIds);
                foreach (StateGraphStateRecord state in layer.AllStates)
                {
                    if (state == null)
                    {
                        continue;
                    }

                    state.StateId = Remap(state.StateId, stateIds);
                    state.ParentStateId = Remap(state.ParentStateId, stateIds);
                    state.InitialChildStateId = Remap(state.InitialChildStateId, stateIds);
                }

                foreach (StateGraphTransitionRecord transition in layer.Transitions)
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
            SerializedId128 source,
            IDictionary<SerializedId128, SerializedId128> remaps,
            string assetGuid,
            string identityKind)
        {
            if (source.IsValid && !remaps.ContainsKey(source))
            {
                remaps.Add(source, CreateCopyId(source, assetGuid, identityKind));
            }
        }

        private static SerializedId128 CreateCopyId(
            SerializedId128 source,
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

            return new SerializedId128(high, low);
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

        private static SerializedId128 Remap(
            SerializedId128 source,
            IReadOnlyDictionary<SerializedId128, SerializedId128> remaps)
        {
            return source.IsValid && remaps.TryGetValue(source, out SerializedId128 remapped)
                ? remapped
                : source;
        }
    }
}

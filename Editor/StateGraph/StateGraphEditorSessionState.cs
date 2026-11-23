using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed class StateGraphEditorSessionState
    {
        private const string KeyPrefix = "CoCoFlow.StateGraph.Editor.";

        private readonly string sessionKey;
        private readonly HashSet<StateId> collapsedStateIds = new HashSet<StateId>();
        private readonly HashSet<StateId> _canvasCollapsedStateIds = new HashSet<StateId>();
        private readonly Dictionary<string, StateGraphCanvasView> canvasViews =
            new Dictionary<string, StateGraphCanvasView>(StringComparer.Ordinal);

        private StateGraphEditorSessionState(string sessionKey)
        {
            this.sessionKey = sessionKey;
        }

        internal LayerId SelectedLayerId { get; set; }
        internal StateId DrillRootStateId { get; set; }
        internal StateId SelectedStateId { get; set; }
        internal TransitionId SelectedTransitionId { get; set; }
        internal string SearchText { get; set; } = string.Empty;
        internal bool AnalysisRequested { get; set; }
        internal GraphDiagnosticLocation? SelectedDiagnosticLocation { get; set; }
        internal IReadOnlyCollection<StateId> CollapsedStateIds => collapsedStateIds;
        internal IReadOnlyCollection<StateId> CanvasCollapsedStateIds => _canvasCollapsedStateIds;

        internal static StateGraphEditorSessionState Load(StateGraphAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            string key = BuildKey(asset);
            var result = new StateGraphEditorSessionState(key);
            string json = UnityEditor.SessionState.GetString(key, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    StateGraphEditorSessionDto dto =
                        JsonUtility.FromJson<StateGraphEditorSessionDto>(json);
                    result.Read(dto);
                }
                catch (Exception)
                {
                    // Corrupt per-session UI data is intentionally discarded.
                }
            }

            result.Validate(asset);
            return result;
        }

        internal void Save()
        {
            var dto = new StateGraphEditorSessionDto
            {
                selectedLayerId = Format(SelectedLayerId),
                drillRootStateId = Format(DrillRootStateId),
                selectedStateId = Format(SelectedStateId),
                selectedTransitionId = Format(SelectedTransitionId),
                searchText = SearchText ?? string.Empty,
                analysisRequested = AnalysisRequested,
                collapsedStateIds = new List<string>(),
                canvasCollapsedStateIds = new List<string>(),
                canvasViews = new List<StateGraphCanvasViewDto>(),
                selectedDiagnostic = CreateDiagnosticDto(SelectedDiagnosticLocation)
            };
            foreach (StateId stateId in collapsedStateIds)
            {
                dto.collapsedStateIds.Add(Format(stateId));
            }

            foreach (KeyValuePair<string, StateGraphCanvasView> entry in canvasViews)
            {
                dto.canvasViews.Add(new StateGraphCanvasViewDto
                {
                    key = entry.Key,
                    panX = entry.Value.Pan.x,
                    panY = entry.Value.Pan.y,
                    zoom = entry.Value.Zoom
                });
            }

            dto.collapsedStateIds.Sort(StringComparer.Ordinal);
            foreach (StateId stateId in _canvasCollapsedStateIds)
                dto.canvasCollapsedStateIds.Add(Format(stateId));
            dto.canvasCollapsedStateIds.Sort(StringComparer.Ordinal);
            dto.canvasViews.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.key, right.key));
            UnityEditor.SessionState.SetString(sessionKey, JsonUtility.ToJson(dto));
        }

        internal void Validate(StateGraphAsset asset)
        {
            var layerIds = new HashSet<LayerId>();
            var stateOwners = new Dictionary<StateId, LayerId>();
            var compositeIds = new HashSet<StateId>();
            var transitionOwners = new Dictionary<TransitionId, LayerId>();
            var validCanvasKeys = new HashSet<string>(StringComparer.Ordinal);
            LayerId firstLayerId = default;
            foreach (StateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null || !TryLayerId(layer.LayerId, out LayerId layerId))
                {
                    continue;
                }

                layerIds.Add(layerId);
                if (!firstLayerId.IsValid)
                {
                    firstLayerId = layerId;
                }

                validCanvasKeys.Add(CanvasKey(layerId, default));
                foreach (StateGraphStateRecord state in layer.AllStates)
                {
                    if (state != null && TryStateId(state.StateId, out StateId stateId))
                    {
                        stateOwners[stateId] = layerId;
                        validCanvasKeys.Add(CanvasKey(layerId, stateId));
                    }
                }

                foreach (StateGraphTransitionRecord transition in layer.Transitions)
                {
                    if (transition != null &&
                        TryTransitionId(transition.TransitionId, out TransitionId transitionId))
                    {
                        transitionOwners[transitionId] = layerId;
                    }
                }

                foreach (StateGraphStateRecord state in layer.States)
                    if (state != null && TryStateId(state.ParentStateId, out StateId parentId) &&
                        stateOwners.TryGetValue(parentId, out LayerId owner) && owner == layerId)
                        compositeIds.Add(parentId);
            }

            if (!SelectedLayerId.IsValid || !layerIds.Contains(SelectedLayerId))
            {
                SelectedLayerId = firstLayerId;
            }

            if (!DrillRootStateId.IsValid ||
                !stateOwners.TryGetValue(DrillRootStateId, out LayerId drillLayer) ||
                drillLayer != SelectedLayerId)
            {
                DrillRootStateId = default;
            }

            if (!SelectedStateId.IsValid ||
                !stateOwners.TryGetValue(SelectedStateId, out LayerId stateLayer) ||
                stateLayer != SelectedLayerId)
            {
                SelectedStateId = default;
            }

            if (!SelectedTransitionId.IsValid ||
                !transitionOwners.TryGetValue(SelectedTransitionId, out LayerId transitionLayer) ||
                transitionLayer != SelectedLayerId)
            {
                SelectedTransitionId = default;
            }

            collapsedStateIds.RemoveWhere(stateId => !stateOwners.ContainsKey(stateId));
            _canvasCollapsedStateIds.RemoveWhere(stateId => !compositeIds.Contains(stateId));
            var invalidCanvasKeys = new List<string>();
            foreach (string key in canvasViews.Keys)
            {
                if (!validCanvasKeys.Contains(key))
                {
                    invalidCanvasKeys.Add(key);
                }
            }

            foreach (string key in invalidCanvasKeys)
            {
                canvasViews.Remove(key);
            }

            if (SelectedDiagnosticLocation.HasValue)
            {
                GraphDiagnosticLocation location = SelectedDiagnosticLocation.Value;
                bool validLocation = (!location.LayerId.IsValid || layerIds.Contains(location.LayerId)) &&
                                     (!location.StateId.IsValid || stateOwners.ContainsKey(location.StateId)) &&
                                     (!location.TransitionId.IsValid ||
                                      transitionOwners.ContainsKey(location.TransitionId));
                if (!validLocation)
                {
                    SelectedDiagnosticLocation = null;
                }
            }
        }

        internal bool IsCollapsed(StateId stateId) => collapsedStateIds.Contains(stateId);

        internal bool IsCanvasCollapsed(StateId stateId) => _canvasCollapsedStateIds.Contains(stateId);

        internal void SetCanvasCollapsed(StateId stateId, bool collapsed)
        {
            if (!stateId.IsValid) return;
            if (collapsed) _canvasCollapsedStateIds.Add(stateId);
            else _canvasCollapsedStateIds.Remove(stateId);
        }

        internal void SetCollapsed(StateId stateId, bool collapsed)
        {
            if (!stateId.IsValid)
            {
                return;
            }

            if (collapsed)
            {
                collapsedStateIds.Add(stateId);
            }
            else
            {
                collapsedStateIds.Remove(stateId);
            }
        }

        internal StateGraphCanvasView GetCanvasView(LayerId layerId, StateId parentStateId)
        {
            return canvasViews.TryGetValue(
                CanvasKey(layerId, parentStateId),
                out StateGraphCanvasView view)
                ? view
                : StateGraphCanvasView.Default;
        }

        internal void SetCanvasView(
            LayerId layerId,
            StateId parentStateId,
            StateGraphCanvasView view)
        {
            canvasViews[CanvasKey(layerId, parentStateId)] = view.Clamp();
        }

        private void Read(StateGraphEditorSessionDto dto)
        {
            if (dto == null)
            {
                return;
            }

            LayerId.TryParse(dto.selectedLayerId, out LayerId selectedLayerId);
            StateId.TryParse(dto.drillRootStateId, out StateId drillRootStateId);
            StateId.TryParse(dto.selectedStateId, out StateId selectedStateId);
            TransitionId.TryParse(dto.selectedTransitionId, out TransitionId selectedTransitionId);
            SelectedLayerId = selectedLayerId;
            DrillRootStateId = drillRootStateId;
            SelectedStateId = selectedStateId;
            SelectedTransitionId = selectedTransitionId;
            SearchText = dto.searchText ?? string.Empty;
            AnalysisRequested = dto.analysisRequested;
            SelectedDiagnosticLocation = ReadDiagnostic(dto.selectedDiagnostic);

            if (dto.collapsedStateIds != null)
            {
                foreach (string value in dto.collapsedStateIds)
                {
                    if (StateId.TryParse(value, out StateId stateId))
                    {
                        collapsedStateIds.Add(stateId);
                    }
                }
            }

            if (dto.canvasViews != null)
            {
                foreach (StateGraphCanvasViewDto view in dto.canvasViews)
                {
                    if (view != null && !string.IsNullOrEmpty(view.key))
                    {
                        canvasViews[view.key] = new StateGraphCanvasView(
                            new Vector2(view.panX, view.panY),
                            view.zoom).Clamp();
                    }
                }
            }

            if (dto.canvasCollapsedStateIds != null)
                foreach (string value in dto.canvasCollapsedStateIds)
                    if (StateId.TryParse(value, out StateId stateId))
                        _canvasCollapsedStateIds.Add(stateId);
        }

        private static string BuildKey(StateGraphAsset asset)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string assetGuid = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);
            string identity = string.IsNullOrEmpty(assetGuid)
                ? asset.GraphId.IsValid ? asset.GraphId.ToString() : GetObjectEntityId(asset).ToString()
                : assetGuid;
            return KeyPrefix + identity;
        }

        private static EntityId GetObjectEntityId(UnityEngine.Object value)
        {
#if UNITY_6000_5_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }

        private static string CanvasKey(LayerId layerId, StateId parentStateId) =>
            Format(layerId) + ":" + Format(parentStateId);

        private static string Format(LayerId id) => id.IsValid ? id.ToString() : string.Empty;
        private static string Format(StateId id) => id.IsValid ? id.ToString() : string.Empty;
        private static string Format(TransitionId id) => id.IsValid ? id.ToString() : string.Empty;

        private static bool TryLayerId(SerializedId128 id, out LayerId value) =>
            LayerId.TryCreate(id.High, id.Low, out value);

        private static bool TryStateId(SerializedId128 id, out StateId value) =>
            StateId.TryCreate(id.High, id.Low, out value);

        private static bool TryTransitionId(SerializedId128 id, out TransitionId value) =>
            TransitionId.TryCreate(id.High, id.Low, out value);

        private static StateGraphDiagnosticDto CreateDiagnosticDto(
            GraphDiagnosticLocation? location)
        {
            if (!location.HasValue)
            {
                return null;
            }

            GraphDiagnosticLocation value = location.Value;
            return new StateGraphDiagnosticDto
            {
                elementKind = (int)value.ElementKind,
                field = (int)value.Field,
                layerId = Format(value.LayerId),
                stateId = Format(value.StateId),
                transitionId = Format(value.TransitionId),
                layerIndex = value.LayerIndex,
                stateIndex = value.StateIndex,
                transitionIndex = value.TransitionIndex,
                conditionIndex = value.ConditionIndex,
                eventAdapterDeclarationIndex = value.EventAdapterDeclarationIndex
            };
        }

        private static GraphDiagnosticLocation? ReadDiagnostic(StateGraphDiagnosticDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            LayerId.TryParse(dto.layerId, out LayerId layerId);
            StateId.TryParse(dto.stateId, out StateId stateId);
            TransitionId.TryParse(dto.transitionId, out TransitionId transitionId);
            return new GraphDiagnosticLocation(
                (GraphElementKind)dto.elementKind,
                (GraphField)dto.field,
                default,
                layerId,
                stateId,
                transitionId,
                dto.layerIndex,
                dto.stateIndex,
                dto.transitionIndex,
                dto.conditionIndex,
                dto.eventAdapterDeclarationIndex);
        }

        [Serializable]
        private sealed class StateGraphEditorSessionDto
        {
            public string selectedLayerId;
            public string drillRootStateId;
            public string selectedStateId;
            public string selectedTransitionId;
            public string searchText;
            public bool analysisRequested;
            public List<string> collapsedStateIds;
            public List<string> canvasCollapsedStateIds;
            public List<StateGraphCanvasViewDto> canvasViews;
            public StateGraphDiagnosticDto selectedDiagnostic;
        }

        [Serializable]
        private sealed class StateGraphCanvasViewDto
        {
            public string key;
            public float panX;
            public float panY;
            public float zoom;
        }

        [Serializable]
        private sealed class StateGraphDiagnosticDto
        {
            public int elementKind;
            public int field;
            public string layerId;
            public string stateId;
            public string transitionId;
            public int layerIndex;
            public int stateIndex;
            public int transitionIndex;
            public int conditionIndex;
            public int eventAdapterDeclarationIndex;
        }
    }

    internal readonly struct StateGraphCanvasView
    {
        internal StateGraphCanvasView(Vector2 pan, float zoom)
        {
            Pan = pan;
            Zoom = zoom;
        }

        internal static StateGraphCanvasView Default =>
            new StateGraphCanvasView(Vector2.zero, 1f);

        internal Vector2 Pan { get; }
        internal float Zoom { get; }

        internal StateGraphCanvasView Clamp() =>
            new StateGraphCanvasView(Pan, Mathf.Clamp(Zoom, 0.25f, 2f));
    }
}

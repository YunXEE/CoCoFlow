using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed class CoCoStateGraphEditorSessionState
    {
        private const string KeyPrefix = "CoCoFlow.StateGraph.Editor.";

        private readonly string sessionKey;
        private readonly HashSet<CoCoStateId> collapsedStateIds = new HashSet<CoCoStateId>();
        private readonly Dictionary<string, CoCoStateGraphCanvasView> canvasViews =
            new Dictionary<string, CoCoStateGraphCanvasView>(StringComparer.Ordinal);

        private CoCoStateGraphEditorSessionState(string sessionKey)
        {
            this.sessionKey = sessionKey;
        }

        internal CoCoLayerId SelectedLayerId { get; set; }
        internal CoCoStateId DrillRootStateId { get; set; }
        internal CoCoStateId SelectedStateId { get; set; }
        internal CoCoTransitionId SelectedTransitionId { get; set; }
        internal string SearchText { get; set; } = string.Empty;
        internal bool AnalysisRequested { get; set; }
        internal CoCoGraphDiagnosticLocation? SelectedDiagnosticLocation { get; set; }
        internal IReadOnlyCollection<CoCoStateId> CollapsedStateIds => collapsedStateIds;

        internal static CoCoStateGraphEditorSessionState Load(CoCoStateGraphAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            string key = BuildKey(asset);
            var result = new CoCoStateGraphEditorSessionState(key);
            string json = UnityEditor.SessionState.GetString(key, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    CoCoStateGraphEditorSessionDto dto =
                        JsonUtility.FromJson<CoCoStateGraphEditorSessionDto>(json);
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
            var dto = new CoCoStateGraphEditorSessionDto
            {
                selectedLayerId = Format(SelectedLayerId),
                drillRootStateId = Format(DrillRootStateId),
                selectedStateId = Format(SelectedStateId),
                selectedTransitionId = Format(SelectedTransitionId),
                searchText = SearchText ?? string.Empty,
                analysisRequested = AnalysisRequested,
                collapsedStateIds = new List<string>(),
                canvasViews = new List<CoCoStateGraphCanvasViewDto>(),
                selectedDiagnostic = CreateDiagnosticDto(SelectedDiagnosticLocation)
            };
            foreach (CoCoStateId stateId in collapsedStateIds)
            {
                dto.collapsedStateIds.Add(Format(stateId));
            }

            foreach (KeyValuePair<string, CoCoStateGraphCanvasView> entry in canvasViews)
            {
                dto.canvasViews.Add(new CoCoStateGraphCanvasViewDto
                {
                    key = entry.Key,
                    panX = entry.Value.Pan.x,
                    panY = entry.Value.Pan.y,
                    zoom = entry.Value.Zoom
                });
            }

            dto.collapsedStateIds.Sort(StringComparer.Ordinal);
            dto.canvasViews.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.key, right.key));
            UnityEditor.SessionState.SetString(sessionKey, JsonUtility.ToJson(dto));
        }

        internal void Validate(CoCoStateGraphAsset asset)
        {
            var layerIds = new HashSet<CoCoLayerId>();
            var stateOwners = new Dictionary<CoCoStateId, CoCoLayerId>();
            var transitionOwners = new Dictionary<CoCoTransitionId, CoCoLayerId>();
            var validCanvasKeys = new HashSet<string>(StringComparer.Ordinal);
            CoCoLayerId firstLayerId = default;
            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null || !TryLayerId(layer.LayerId, out CoCoLayerId layerId))
                {
                    continue;
                }

                layerIds.Add(layerId);
                if (!firstLayerId.IsValid)
                {
                    firstLayerId = layerId;
                }

                validCanvasKeys.Add(CanvasKey(layerId, default));
                foreach (CoCoStateGraphStateRecord state in layer.States)
                {
                    if (state != null && TryStateId(state.StateId, out CoCoStateId stateId))
                    {
                        stateOwners[stateId] = layerId;
                        validCanvasKeys.Add(CanvasKey(layerId, stateId));
                    }
                }

                foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
                {
                    if (transition != null &&
                        TryTransitionId(transition.TransitionId, out CoCoTransitionId transitionId))
                    {
                        transitionOwners[transitionId] = layerId;
                    }
                }
            }

            if (!SelectedLayerId.IsValid || !layerIds.Contains(SelectedLayerId))
            {
                SelectedLayerId = firstLayerId;
            }

            if (!DrillRootStateId.IsValid ||
                !stateOwners.TryGetValue(DrillRootStateId, out CoCoLayerId drillLayer) ||
                drillLayer != SelectedLayerId)
            {
                DrillRootStateId = default;
            }

            if (!SelectedStateId.IsValid ||
                !stateOwners.TryGetValue(SelectedStateId, out CoCoLayerId stateLayer) ||
                stateLayer != SelectedLayerId)
            {
                SelectedStateId = default;
            }

            if (!SelectedTransitionId.IsValid ||
                !transitionOwners.TryGetValue(SelectedTransitionId, out CoCoLayerId transitionLayer) ||
                transitionLayer != SelectedLayerId)
            {
                SelectedTransitionId = default;
            }

            collapsedStateIds.RemoveWhere(stateId => !stateOwners.ContainsKey(stateId));
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
                CoCoGraphDiagnosticLocation location = SelectedDiagnosticLocation.Value;
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

        internal bool IsCollapsed(CoCoStateId stateId) => collapsedStateIds.Contains(stateId);

        internal void SetCollapsed(CoCoStateId stateId, bool collapsed)
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

        internal CoCoStateGraphCanvasView GetCanvasView(CoCoLayerId layerId, CoCoStateId parentStateId)
        {
            return canvasViews.TryGetValue(
                CanvasKey(layerId, parentStateId),
                out CoCoStateGraphCanvasView view)
                ? view
                : CoCoStateGraphCanvasView.Default;
        }

        internal void SetCanvasView(
            CoCoLayerId layerId,
            CoCoStateId parentStateId,
            CoCoStateGraphCanvasView view)
        {
            canvasViews[CanvasKey(layerId, parentStateId)] = view.Clamp();
        }

        private void Read(CoCoStateGraphEditorSessionDto dto)
        {
            if (dto == null)
            {
                return;
            }

            CoCoLayerId.TryParse(dto.selectedLayerId, out CoCoLayerId selectedLayerId);
            CoCoStateId.TryParse(dto.drillRootStateId, out CoCoStateId drillRootStateId);
            CoCoStateId.TryParse(dto.selectedStateId, out CoCoStateId selectedStateId);
            CoCoTransitionId.TryParse(dto.selectedTransitionId, out CoCoTransitionId selectedTransitionId);
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
                    if (CoCoStateId.TryParse(value, out CoCoStateId stateId))
                    {
                        collapsedStateIds.Add(stateId);
                    }
                }
            }

            if (dto.canvasViews != null)
            {
                foreach (CoCoStateGraphCanvasViewDto view in dto.canvasViews)
                {
                    if (view != null && !string.IsNullOrEmpty(view.key))
                    {
                        canvasViews[view.key] = new CoCoStateGraphCanvasView(
                            new Vector2(view.panX, view.panY),
                            view.zoom).Clamp();
                    }
                }
            }
        }

        private static string BuildKey(CoCoStateGraphAsset asset)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string assetGuid = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);
            string identity = string.IsNullOrEmpty(assetGuid)
                ? asset.GraphId.IsValid ? asset.GraphId.ToString() : asset.GetInstanceID().ToString()
                : assetGuid;
            return KeyPrefix + identity;
        }

        private static string CanvasKey(CoCoLayerId layerId, CoCoStateId parentStateId) =>
            Format(layerId) + ":" + Format(parentStateId);

        private static string Format(CoCoLayerId id) => id.IsValid ? id.ToString() : string.Empty;
        private static string Format(CoCoStateId id) => id.IsValid ? id.ToString() : string.Empty;
        private static string Format(CoCoTransitionId id) => id.IsValid ? id.ToString() : string.Empty;

        private static bool TryLayerId(CoCoSerializedId128 id, out CoCoLayerId value) =>
            CoCoLayerId.TryCreate(id.High, id.Low, out value);

        private static bool TryStateId(CoCoSerializedId128 id, out CoCoStateId value) =>
            CoCoStateId.TryCreate(id.High, id.Low, out value);

        private static bool TryTransitionId(CoCoSerializedId128 id, out CoCoTransitionId value) =>
            CoCoTransitionId.TryCreate(id.High, id.Low, out value);

        private static CoCoStateGraphDiagnosticDto CreateDiagnosticDto(
            CoCoGraphDiagnosticLocation? location)
        {
            if (!location.HasValue)
            {
                return null;
            }

            CoCoGraphDiagnosticLocation value = location.Value;
            return new CoCoStateGraphDiagnosticDto
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

        private static CoCoGraphDiagnosticLocation? ReadDiagnostic(CoCoStateGraphDiagnosticDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            CoCoLayerId.TryParse(dto.layerId, out CoCoLayerId layerId);
            CoCoStateId.TryParse(dto.stateId, out CoCoStateId stateId);
            CoCoTransitionId.TryParse(dto.transitionId, out CoCoTransitionId transitionId);
            return new CoCoGraphDiagnosticLocation(
                (CoCoGraphElementKind)dto.elementKind,
                (CoCoGraphField)dto.field,
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
        private sealed class CoCoStateGraphEditorSessionDto
        {
            public string selectedLayerId;
            public string drillRootStateId;
            public string selectedStateId;
            public string selectedTransitionId;
            public string searchText;
            public bool analysisRequested;
            public List<string> collapsedStateIds;
            public List<CoCoStateGraphCanvasViewDto> canvasViews;
            public CoCoStateGraphDiagnosticDto selectedDiagnostic;
        }

        [Serializable]
        private sealed class CoCoStateGraphCanvasViewDto
        {
            public string key;
            public float panX;
            public float panY;
            public float zoom;
        }

        [Serializable]
        private sealed class CoCoStateGraphDiagnosticDto
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

    internal readonly struct CoCoStateGraphCanvasView
    {
        internal CoCoStateGraphCanvasView(Vector2 pan, float zoom)
        {
            Pan = pan;
            Zoom = zoom;
        }

        internal static CoCoStateGraphCanvasView Default =>
            new CoCoStateGraphCanvasView(Vector2.zero, 1f);

        internal Vector2 Pan { get; }
        internal float Zoom { get; }

        internal CoCoStateGraphCanvasView Clamp() =>
            new CoCoStateGraphCanvasView(Pan, Mathf.Clamp(Zoom, 0.25f, 2f));
    }
}

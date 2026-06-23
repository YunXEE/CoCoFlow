using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Editor.Core.StateGraph
{
    public enum CoCoStateGraphNodeKind
    {
        Controller,
        Layer,
        State,
        Context,
        Operation
    }

    public enum CoCoStateGraphEdgeKind
    {
        Transition,
        Layer,
        LayerState,
        ContextRead,
        ContextWrite,
        Operation
    }

    public sealed class CoCoStateGraphRenderSettings
    {
        public bool ShowTransitions = true;
        public bool ShowStateLayerLinks = true;
        public bool ShowContextDependencies;
        public bool ShowOperations;
        public bool ShowCommonTransitions;

        public bool ShowControllerLinks
        {
            get => ShowStateLayerLinks;
            set => ShowStateLayerLinks = value;
        }
    }

    public sealed class CoCoStateGraphRenderStats
    {
        public int ControllerNodes;
        public int LayerNodes;
        public int StateNodes;
        public int ContextNodes;
        public int OperationNodes;
        public int TransitionEdges;
        public int ReciprocalTransitionPairs;
        public int LayerEdges;
        public int LayerStateEdges;
        public int ContextReadEdges;
        public int ContextWriteEdges;
        public int OperationEdges;
        public int HiddenCommonTransitionEdges;

        public int ControllerEdges => LayerEdges;
        public int TotalNodes => ControllerNodes + LayerNodes + StateNodes + ContextNodes + OperationNodes;
        public int TotalEdges => TransitionEdges + LayerEdges + LayerStateEdges + ContextReadEdges + ContextWriteEdges + OperationEdges;
    }

    public sealed class CoCoStateGraphRenderModel
    {
        public readonly List<CoCoStateGraphRenderNode> Nodes = new List<CoCoStateGraphRenderNode>();
        public readonly List<CoCoStateGraphRenderEdge> Edges = new List<CoCoStateGraphRenderEdge>();
        public readonly CoCoStateGraphRenderStats Stats = new CoCoStateGraphRenderStats();
    }

    public sealed class CoCoStateGraphRenderNode
    {
        public string Id;
        public string Title;
        public string Subtitle;
        public string Caption;
        public Rect Position;
        public Color Color;
        public int IncomingTransitionCount;
        public int OutgoingTransitionCount;
        public CoCoStateGraphNodeKind Kind;
        public CoCoStateController Controller;
        public CoCoStateLayerNode Layer;
        public CoCoStateNode State;
        public Type OperationType;
    }

    public sealed class CoCoStateGraphRenderEdge
    {
        public string Id;
        public string SourceNodeId;
        public string TargetNodeId;
        public string Title;
        public string Tooltip;
        public Color Color;
        public CoCoStateGraphEdgeKind Kind;
        public CoCoStateLayerEdge LayerEdge;
        public CoCoStateLayerStateEdge LayerStateEdge;
        public CoCoStateTransitionEdge Transition;
        public CoCoStateTransitionEdge ReverseTransition;
        public CoCoStateNode State;
        public CoCoStateLayerNode Layer;
        public Type OperationType;
        public readonly List<CoCoStateContextDependency> ContextDependencies =
            new List<CoCoStateContextDependency>();
        public readonly List<CoCoStateOperationDependency> OperationDependencies =
            new List<CoCoStateOperationDependency>();
    }

    internal static class CoCoStateGraphLayout
    {
        private const float StartX = 80f;
        private const float StartY = 80f;
        private const float DepthWidth = 900f;
        private const float ContextX = 0f;
        private const float ControllerX = 250f;
        private const float LayerX = 520f;
        private const float StateX = 800f;
        private const float StateYStep = 126f;
        private const float MinLayerHeight = 260f;
        private const float OperationYStep = 116f;

        private static readonly Color ControllerColor = new Color(0.2f, 0.24f, 0.32f);
        private static readonly Color StateColor = new Color(0.2f, 0.2f, 0.2f);
        private static readonly Color DefaultStateColor = new Color(0.32f, 0.3f, 0.16f);
        private static readonly Color CurrentStateColor = new Color(0.18f, 0.36f, 0.22f);
        private static readonly Color ContextColor = new Color(0.16f, 0.23f, 0.2f);
        private static readonly Color OperationColor = new Color(0.13f, 0.27f, 0.32f);

        public static CoCoStateGraphRenderModel Build(
            CoCoStateGraphModel model,
            CoCoStateGraphRenderSettings settings)
        {
            var renderModel = new CoCoStateGraphRenderModel();
            if (model == null || model.RootController == null)
            {
                return renderModel;
            }

            if (settings == null)
            {
                settings = new CoCoStateGraphRenderSettings();
            }

            var statesByLayer = BuildStatesByLayer(model);
            var layerPositions = BuildLayerPositions(model, statesByLayer);
            var transitionPlan = BuildTransitionPlan(model, settings);
            var nodeIds = new HashSet<string>();

            var contextLayer = ResolveContextLayer(model);
            var controllerOrigin = contextLayer != null
                ? layerPositions[contextLayer.Id]
                : new Vector2(StartX, StartY);
            if (contextLayer != null)
            {
                AddContextNode(renderModel, contextLayer, controllerOrigin, nodeIds);
            }
            AddControllerNode(renderModel, model.RootController, contextLayer, controllerOrigin, nodeIds);

            foreach (var layer in model.Layers)
            {
                AddLayerNode(renderModel, layer, layerPositions[layer.Id], nodeIds);
                AddStateNodes(renderModel, layer, statesByLayer, layerPositions[layer.Id], transitionPlan, nodeIds);
            }

            var operationPositions = new Dictionary<Type, Vector2>();
            if (settings.ShowOperations)
            {
                AddOperationNodes(model, renderModel, operationPositions, nodeIds);
            }

            AddLayerEdges(model, renderModel, settings, nodeIds);
            AddTransitionEdges(renderModel, settings, transitionPlan, nodeIds);
            AddContextDependencyEdges(model, renderModel, settings, nodeIds);
            AddOperationDependencyEdges(model, renderModel, settings, operationPositions, nodeIds);
            AddLayerStateEdges(model, renderModel, settings, nodeIds);

            return renderModel;
        }

        private static Dictionary<string, List<CoCoStateNode>> BuildStatesByLayer(
            CoCoStateGraphModel model)
        {
            var statesByLayer = new Dictionary<string, List<CoCoStateNode>>();
            foreach (var state in model.States)
            {
                if (string.IsNullOrEmpty(state.LayerId)) continue;

                if (!statesByLayer.TryGetValue(state.LayerId, out var states))
                {
                    states = new List<CoCoStateNode>();
                    statesByLayer[state.LayerId] = states;
                }

                states.Add(state);
            }

            return statesByLayer;
        }

        private static Dictionary<string, Vector2> BuildLayerPositions(
            CoCoStateGraphModel model,
            Dictionary<string, List<CoCoStateNode>> statesByLayer)
        {
            var positions = new Dictionary<string, Vector2>();
            float nextY = StartY;
            foreach (var layer in model.Layers)
            {
                int stateCount = statesByLayer.TryGetValue(layer.Id, out var states)
                    ? states.Count
                    : 0;
                float height = Mathf.Max(MinLayerHeight, stateCount * StateYStep + 120f);
                positions[layer.Id] = new Vector2(StartX + layer.Depth * DepthWidth, nextY);
                nextY += height;
            }

            return positions;
        }

        private static void AddContextNode(
            CoCoStateGraphRenderModel renderModel,
            CoCoStateLayerNode rootLayer,
            Vector2 origin,
            HashSet<string> nodeIds)
        {
            var node = new CoCoStateGraphRenderNode
            {
                Id = BuildContextNodeId(),
                Title = "Shared Context",
                Subtitle = FormatType(rootLayer.ContextType),
                Caption = FormatProvider(rootLayer.ContextProvider),
                Kind = CoCoStateGraphNodeKind.Context,
                Layer = rootLayer,
                Position = new Rect(origin.x + ContextX, origin.y, 210f, 116f),
                Color = ContextColor
            };
            AddNode(renderModel, node, nodeIds);
            renderModel.Stats.ContextNodes++;
        }

        private static void AddControllerNode(
            CoCoStateGraphRenderModel renderModel,
            CoCoStateController controller,
            CoCoStateLayerNode rootLayer,
            Vector2 origin,
            HashSet<string> nodeIds)
        {
            var node = new CoCoStateGraphRenderNode
            {
                Id = BuildControllerNodeId(controller),
                Title = controller.name,
                Subtitle = "State Controller",
                Caption = $"Layers: {FormatLayerCount(controller)}",
                Kind = CoCoStateGraphNodeKind.Controller,
                Controller = controller,
                Layer = rootLayer,
                Position = new Rect(origin.x + ControllerX, origin.y, 230f, 116f),
                Color = ControllerColor
            };
            AddNode(renderModel, node, nodeIds);
            renderModel.Stats.ControllerNodes++;
        }

        private static void AddLayerNode(
            CoCoStateGraphRenderModel renderModel,
            CoCoStateLayerNode layer,
            Vector2 origin,
            HashSet<string> nodeIds)
        {
            var node = new CoCoStateGraphRenderNode
            {
                Id = BuildLayerNodeId(layer.Id),
                Title = layer.Name,
                Subtitle = $"State Layer order:{layer.Order}",
                Caption = $"Default: {FormatState(layer.DefaultState)}",
                Kind = CoCoStateGraphNodeKind.Layer,
                Layer = layer,
                Position = new Rect(origin.x + LayerX, origin.y, 230f, 116f),
                Color = EdgeColors.StateLayer
            };
            AddNode(renderModel, node, nodeIds);
            renderModel.Stats.LayerNodes++;
        }

        private static void AddStateNodes(
            CoCoStateGraphRenderModel renderModel,
            CoCoStateLayerNode layer,
            Dictionary<string, List<CoCoStateNode>> statesByLayer,
            Vector2 origin,
            TransitionRenderPlan transitionPlan,
            HashSet<string> nodeIds)
        {
            if (!statesByLayer.TryGetValue(layer.Id, out var states)) return;

            for (int i = 0; i < states.Count; i++)
            {
                var state = states[i];
                var node = new CoCoStateGraphRenderNode
                {
                    Id = BuildStateNodeId(state.Id),
                    Title = state.Name,
                    Subtitle = ResolveStateMarker(state),
                    Caption = FormatStateCaption(state, transitionPlan),
                    IncomingTransitionCount = transitionPlan.GetIncomingCount(state.Id),
                    OutgoingTransitionCount = transitionPlan.GetOutgoingCount(state.Id),
                    Kind = CoCoStateGraphNodeKind.State,
                    State = state,
                    Position = new Rect(origin.x + StateX, origin.y + i * StateYStep, 245f, 104f),
                    Color = ResolveStateColor(state)
                };
                AddNode(renderModel, node, nodeIds);
                renderModel.Stats.StateNodes++;
            }
        }

        private static void AddOperationNodes(
            CoCoStateGraphModel model,
            CoCoStateGraphRenderModel renderModel,
            Dictionary<Type, Vector2> operationPositions,
            HashSet<string> nodeIds)
        {
            var operationTypes = new List<Type>();
            foreach (var state in model.States)
            {
                foreach (var operation in state.Definition.OperationDependencies)
                {
                    if (operation.ComponentType == null ||
                        operationTypes.Contains(operation.ComponentType))
                    {
                        continue;
                    }

                    operationTypes.Add(operation.ComponentType);
                }
            }

            float operationX = StartX + StateX + 360f;
            for (int i = 0; i < operationTypes.Count; i++)
            {
                var operationType = operationTypes[i];
                var position = new Vector2(operationX, StartY + i * OperationYStep);
                operationPositions[operationType] = position;

                var node = new CoCoStateGraphRenderNode
                {
                    Id = BuildOperationNodeId(operationType),
                    Title = operationType.Name,
                    Subtitle = "Operation",
                    Caption = "External component",
                    Kind = CoCoStateGraphNodeKind.Operation,
                    OperationType = operationType,
                    Position = new Rect(position.x, position.y, 230f, 100f),
                    Color = OperationColor
                };
                AddNode(renderModel, node, nodeIds);
                renderModel.Stats.OperationNodes++;
            }
        }

        private static void AddLayerEdges(
            CoCoStateGraphModel model,
            CoCoStateGraphRenderModel renderModel,
            CoCoStateGraphRenderSettings settings,
            HashSet<string> nodeIds)
        {
            if (!settings.ShowStateLayerLinks) return;

            string controllerId = BuildControllerNodeId(model.RootController);
            foreach (var edge in model.LayerEdges)
            {
                string sourceId = controllerId;
                string targetId = BuildLayerNodeId(edge.ChildLayerId);
                if (!nodeIds.Contains(sourceId) || !nodeIds.Contains(targetId)) continue;

                var childLayer = FindLayer(model, edge.ChildLayerId);
                renderModel.Edges.Add(new CoCoStateGraphRenderEdge
                {
                    Id = $"controller-layer-edge:{edge.ChildLayerId}:{edge.Name}",
                    SourceNodeId = sourceId,
                    TargetNodeId = targetId,
                    Title = edge.Name,
                    Tooltip = $"State Controller owns State Layer: {edge.Name} order:{edge.Order}",
                    Kind = CoCoStateGraphEdgeKind.Layer,
                    LayerEdge = edge,
                    Layer = childLayer,
                    Color = EdgeColors.StateLayer
                });
                renderModel.Stats.LayerEdges++;
            }
        }

        private static void AddLayerStateEdges(
            CoCoStateGraphModel model,
            CoCoStateGraphRenderModel renderModel,
            CoCoStateGraphRenderSettings settings,
            HashSet<string> nodeIds)
        {
            if (!settings.ShowStateLayerLinks) return;

            foreach (var edge in model.LayerStateEdges)
            {
                if (!edge.IsDefault) continue;

                string sourceId = BuildLayerNodeId(edge.LayerId);
                string targetId = BuildStateNodeId(edge.StateId);
                if (!nodeIds.Contains(sourceId) || !nodeIds.Contains(targetId)) continue;

                renderModel.Edges.Add(new CoCoStateGraphRenderEdge
                {
                    Id = $"layer-state-edge:{edge.LayerId}:{edge.StateId}",
                    SourceNodeId = sourceId,
                    TargetNodeId = targetId,
                    Title = edge.IsDefault ? "default" : "contains",
                    Tooltip = edge.IsDefault
                        ? $"{edge.LayerName} default state: {edge.StateName}"
                        : $"{edge.LayerName} contains state: {edge.StateName}",
                    Kind = CoCoStateGraphEdgeKind.LayerState,
                    LayerStateEdge = edge,
                    Color = edge.IsDefault ? EdgeColors.LayerDefaultState : EdgeColors.LayerState
                });
                renderModel.Stats.LayerStateEdges++;
            }
        }

        private static void AddTransitionEdges(
            CoCoStateGraphRenderModel renderModel,
            CoCoStateGraphRenderSettings settings,
            TransitionRenderPlan transitionPlan,
            HashSet<string> nodeIds)
        {
            if (!settings.ShowTransitions) return;

            renderModel.Stats.HiddenCommonTransitionEdges = transitionPlan.HiddenCommonTransitions;
            renderModel.Stats.ReciprocalTransitionPairs = transitionPlan.ReciprocalPairCount;
            foreach (var transition in transitionPlan.VisibleTransitions)
            {
                string sourceId = BuildStateNodeId(transition.SourceStateId);
                string targetId = BuildStateNodeId(transition.TargetStateId);
                if (!nodeIds.Contains(sourceId) || !nodeIds.Contains(targetId)) continue;

                renderModel.Edges.Add(new CoCoStateGraphRenderEdge
                {
                    Id = $"transition:{transition.SourceStateId}:{transition.TargetStateId}:{transition.Note}",
                    SourceNodeId = sourceId,
                    TargetNodeId = targetId,
                    Title = "transition",
                    Tooltip = $"{transition.TargetStateName} {transition.Note}",
                    Kind = CoCoStateGraphEdgeKind.Transition,
                    Transition = transition,
                    Color = transitionPlan.IsReciprocalTransition(transition)
                        ? EdgeColors.Reciprocal
                        : EdgeColors.Transition
                });
                renderModel.Stats.TransitionEdges++;
            }
        }

        private static TransitionRenderPlan BuildTransitionPlan(
            CoCoStateGraphModel model,
            CoCoStateGraphRenderSettings settings)
        {
            var plan = new TransitionRenderPlan();
            var statesById = BuildStateLookup(model);
            var hiddenCommonTransitions = settings.ShowCommonTransitions
                ? new HashSet<string>()
                : BuildCommonTransitionLookup(model);

            foreach (var transition in model.Transitions)
            {
                if (!transition.TargetResolved) continue;
                if (statesById.TryGetValue(transition.SourceStateId, out var sourceState) &&
                    hiddenCommonTransitions.Contains(BuildLayerTransitionSignature(
                        sourceState.LayerId,
                        transition)))
                {
                    plan.HiddenCommonTransitions++;
                    continue;
                }

                plan.VisibleTransitions.Add(transition);
                plan.IncrementOutgoing(transition.SourceStateId);
                plan.IncrementIncoming(transition.TargetStateId);
            }

            plan.ResolveReciprocalPairs();
            return plan;
        }

        private static string BuildDirectionalPairKey(string sourceStateId, string targetStateId)
        {
            return $"{sourceStateId}->{targetStateId}";
        }

        private static string BuildUnorderedPairKey(string sourceStateId, string targetStateId)
        {
            return string.CompareOrdinal(sourceStateId, targetStateId) <= 0
                ? $"{sourceStateId}<->{targetStateId}"
                : $"{targetStateId}<->{sourceStateId}";
        }

        private static Dictionary<string, CoCoStateNode> BuildStateLookup(
            CoCoStateGraphModel model)
        {
            var states = new Dictionary<string, CoCoStateNode>();
            foreach (var state in model.States)
            {
                states[state.Id] = state;
            }

            return states;
        }

        private static HashSet<string> BuildCommonTransitionLookup(
            CoCoStateGraphModel model)
        {
            var statesByLayer = BuildStatesByLayer(model);
            var transitionsBySource = new Dictionary<string, HashSet<string>>();
            foreach (var transition in model.Transitions)
            {
                if (!transition.TargetResolved) continue;

                if (!transitionsBySource.TryGetValue(transition.SourceStateId, out var signatures))
                {
                    signatures = new HashSet<string>();
                    transitionsBySource[transition.SourceStateId] = signatures;
                }

                signatures.Add(BuildTransitionSignature(transition));
            }

            var commonTransitions = new HashSet<string>();
            foreach (var pair in statesByLayer)
            {
                var layerId = pair.Key;
                var states = pair.Value;
                if (states.Count < 3) continue;

                HashSet<string> intersection = null;
                foreach (var state in states)
                {
                    if (!transitionsBySource.TryGetValue(state.Id, out var signatures))
                    {
                        intersection = null;
                        break;
                    }

                    if (intersection == null)
                    {
                        intersection = new HashSet<string>(signatures);
                    }
                    else
                    {
                        intersection.IntersectWith(signatures);
                    }
                }

                if (intersection == null) continue;

                foreach (var signature in intersection)
                {
                    commonTransitions.Add(BuildLayerTransitionSignature(layerId, signature));
                }
            }

            return commonTransitions;
        }

        private static void AddContextDependencyEdges(
            CoCoStateGraphModel model,
            CoCoStateGraphRenderModel renderModel,
            CoCoStateGraphRenderSettings settings,
            HashSet<string> nodeIds)
        {
            if (!settings.ShowContextDependencies) return;

            var layersById = BuildLayerLookup(model);
            var edgesById = new Dictionary<string, CoCoStateGraphRenderEdge>();
            foreach (var state in model.States)
            {
                if (!layersById.TryGetValue(state.LayerId, out var layer)) continue;

                foreach (var dependency in state.Definition.ContextDependencies)
                {
                    bool isRead = dependency.Access == CoCoStateContextAccess.Read;
                    string contextId = BuildContextNodeId();
                    string stateId = BuildStateNodeId(state.Id);
                    string sourceId = isRead ? contextId : stateId;
                    string targetId = isRead ? stateId : contextId;
                    if (!nodeIds.Contains(sourceId) || !nodeIds.Contains(targetId)) continue;

                    string id = $"{(isRead ? "context-read" : "context-write")}:{state.Id}";
                    if (!edgesById.TryGetValue(id, out var edge))
                    {
                        edge = new CoCoStateGraphRenderEdge
                        {
                            Id = id,
                            SourceNodeId = sourceId,
                            TargetNodeId = targetId,
                            Title = isRead ? "read" : "write",
                            Kind = isRead ? CoCoStateGraphEdgeKind.ContextRead : CoCoStateGraphEdgeKind.ContextWrite,
                            State = state,
                            Layer = layer,
                            Color = isRead
                                ? EdgeColors.ContextRead
                                : EdgeColors.ContextWrite
                        };
                        edgesById[id] = edge;
                        renderModel.Edges.Add(edge);
                        if (isRead) renderModel.Stats.ContextReadEdges++;
                        else renderModel.Stats.ContextWriteEdges++;
                    }

                    edge.ContextDependencies.Add(dependency);
                    edge.Tooltip = FormatContextDependencies(edge.ContextDependencies);
                }
            }
        }

        private static void AddOperationDependencyEdges(
            CoCoStateGraphModel model,
            CoCoStateGraphRenderModel renderModel,
            CoCoStateGraphRenderSettings settings,
            Dictionary<Type, Vector2> operationPositions,
            HashSet<string> nodeIds)
        {
            if (!settings.ShowOperations) return;

            var edgesById = new Dictionary<string, CoCoStateGraphRenderEdge>();
            foreach (var state in model.States)
            {
                foreach (var dependency in state.Definition.OperationDependencies)
                {
                    if (dependency.ComponentType == null ||
                        !operationPositions.ContainsKey(dependency.ComponentType))
                    {
                        continue;
                    }

                    string sourceId = BuildStateNodeId(state.Id);
                    string targetId = BuildOperationNodeId(dependency.ComponentType);
                    if (!nodeIds.Contains(sourceId) || !nodeIds.Contains(targetId)) continue;

                    string id = $"operation:{state.Id}:{dependency.ComponentType.FullName}";
                    if (!edgesById.TryGetValue(id, out var edge))
                    {
                        edge = new CoCoStateGraphRenderEdge
                        {
                            Id = id,
                            SourceNodeId = sourceId,
                            TargetNodeId = targetId,
                            Title = "uses",
                            Kind = CoCoStateGraphEdgeKind.Operation,
                            State = state,
                            OperationType = dependency.ComponentType,
                            Color = EdgeColors.Operation
                        };
                        edgesById[id] = edge;
                        renderModel.Edges.Add(edge);
                        renderModel.Stats.OperationEdges++;
                    }

                    edge.OperationDependencies.Add(dependency);
                    edge.Tooltip = FormatOperationDependencies(edge.OperationDependencies);
                }
            }
        }

        private static Dictionary<string, CoCoStateLayerNode> BuildLayerLookup(
            CoCoStateGraphModel model)
        {
            var layers = new Dictionary<string, CoCoStateLayerNode>();
            foreach (var layer in model.Layers)
            {
                layers[layer.Id] = layer;
            }

            return layers;
        }

        private static void AddNode(
            CoCoStateGraphRenderModel renderModel,
            CoCoStateGraphRenderNode node,
            HashSet<string> nodeIds)
        {
            if (!nodeIds.Add(node.Id)) return;

            renderModel.Nodes.Add(node);
        }

        private static int ResolveMaxDepth(CoCoStateGraphModel model)
        {
            int maxDepth = 0;
            foreach (var layer in model.Layers)
            {
                maxDepth = Mathf.Max(maxDepth, layer.Depth);
            }

            return maxDepth;
        }

        private static CoCoStateLayerNode ResolveContextLayer(CoCoStateGraphModel model)
        {
            return model.Layers.Count > 0 ? model.Layers[0] : null;
        }

        private static CoCoStateLayerNode FindLayer(CoCoStateGraphModel model, string layerId)
        {
            foreach (var layer in model.Layers)
            {
                if (layer.Id == layerId) return layer;
            }

            return null;
        }

        private static string FormatLayerCount(CoCoStateController controller)
        {
            return controller != null ? controller.StateLayers.Count.ToString() : "0";
        }

        private static string ResolveStateMarker(CoCoStateNode state)
        {
            if (state.IsCurrent) return "current";
            if (state.IsDefault) return "default";
            return "state";
        }

        private static Color ResolveStateColor(CoCoStateNode state)
        {
            if (state.IsCurrent) return CurrentStateColor;
            if (state.IsDefault) return DefaultStateColor;
            return StateColor;
        }

        private static string FormatContextDependencies(
            IReadOnlyList<CoCoStateContextDependency> dependencies)
        {
            var parts = new List<string>();
            foreach (var dependency in dependencies)
            {
                parts.Add($"{dependency.ContextType.Name}.{dependency.Path}");
            }

            return string.Join("\n", parts);
        }

        private static string FormatOperationDependencies(
            IReadOnlyList<CoCoStateOperationDependency> dependencies)
        {
            var parts = new List<string>();
            foreach (var dependency in dependencies)
            {
                parts.Add(string.IsNullOrEmpty(dependency.Usage)
                    ? dependency.ComponentType.Name
                    : $"{dependency.ComponentType.Name}: {dependency.Usage}");
            }

            return string.Join("\n", parts);
        }

        private static string FormatType(Type type)
        {
            return type != null ? type.Name : "None";
        }

        private static string FormatProvider(UnityEngine.Object provider)
        {
            return provider != null ? provider.GetType().Name : "No provider";
        }

        private static string FormatState(CoCoStateBase state)
        {
            return state != null ? state.GetType().Name : "None";
        }

        private static string FormatStateCaption(
            CoCoStateNode state,
            TransitionRenderPlan transitionPlan)
        {
            int incoming = transitionPlan.GetIncomingCount(state.Id);
            int outgoing = transitionPlan.GetOutgoingCount(state.Id);
            return $"IN:{incoming} OUT:{outgoing} ops:{state.Definition.OperationDependencies.Count} deps:{state.Definition.ContextDependencies.Count}";
        }

        private static string BuildLayerTransitionSignature(
            string layerId,
            CoCoStateTransitionEdge transition)
        {
            return BuildLayerTransitionSignature(layerId, BuildTransitionSignature(transition));
        }

        private static string BuildLayerTransitionSignature(
            string layerId,
            string transitionSignature)
        {
            return $"{layerId}:{transitionSignature}";
        }

        private static string BuildTransitionSignature(CoCoStateTransitionEdge transition)
        {
            string target = transition.TargetStateType != null
                ? transition.TargetStateType.FullName
                : transition.TargetStateName;
            return $"{target}:{transition.Note}";
        }

        private static string BuildContextNodeId()
        {
            return "render-context:shared";
        }

        private static string BuildControllerNodeId(CoCoStateController controller)
        {
            return controller != null
                ? $"render-controller:{controller.GetInstanceID()}"
                : "render-controller:null";
        }

        private static string BuildLayerNodeId(string layerId)
        {
            return $"render-layer:{layerId}";
        }

        private static string BuildStateNodeId(string stateId)
        {
            return $"render-state:{stateId}";
        }

        private static string BuildOperationNodeId(Type type)
        {
            return $"render-operation:{type.FullName}";
        }

        private sealed class TransitionRenderPlan
        {
            public readonly List<CoCoStateTransitionEdge> VisibleTransitions =
                new List<CoCoStateTransitionEdge>();

            private readonly Dictionary<string, int> _incomingCounts =
                new Dictionary<string, int>();

            private readonly Dictionary<string, int> _outgoingCounts =
                new Dictionary<string, int>();

            private readonly HashSet<string> _reciprocalDirectionalKeys =
                new HashSet<string>();

            public int HiddenCommonTransitions;
            public int ReciprocalPairCount;

            public int GetIncomingCount(string stateId)
            {
                return !string.IsNullOrEmpty(stateId) &&
                       _incomingCounts.TryGetValue(stateId, out int count)
                    ? count
                    : 0;
            }

            public int GetOutgoingCount(string stateId)
            {
                return !string.IsNullOrEmpty(stateId) &&
                       _outgoingCounts.TryGetValue(stateId, out int count)
                    ? count
                    : 0;
            }

            public bool IsReciprocalTransition(CoCoStateTransitionEdge transition)
            {
                if (transition == null) return false;

                return _reciprocalDirectionalKeys.Contains(BuildDirectionalPairKey(
                    transition.SourceStateId,
                    transition.TargetStateId));
            }

            public void IncrementIncoming(string stateId)
            {
                Increment(_incomingCounts, stateId);
            }

            public void IncrementOutgoing(string stateId)
            {
                Increment(_outgoingCounts, stateId);
            }

            public void ResolveReciprocalPairs()
            {
                var directionalKeys = new HashSet<string>();
                foreach (var transition in VisibleTransitions)
                {
                    directionalKeys.Add(BuildDirectionalPairKey(
                        transition.SourceStateId,
                        transition.TargetStateId));
                }

                var unorderedPairs = new HashSet<string>();
                foreach (var transition in VisibleTransitions)
                {
                    if (transition.SourceStateId == transition.TargetStateId) continue;

                    string key = BuildDirectionalPairKey(
                        transition.SourceStateId,
                        transition.TargetStateId);
                    string reverseKey = BuildDirectionalPairKey(
                        transition.TargetStateId,
                        transition.SourceStateId);
                    if (!directionalKeys.Contains(reverseKey)) continue;

                    unorderedPairs.Add(BuildUnorderedPairKey(
                        transition.SourceStateId,
                        transition.TargetStateId));
                    _reciprocalDirectionalKeys.Add(key);
                    _reciprocalDirectionalKeys.Add(reverseKey);
                }

                ReciprocalPairCount = unorderedPairs.Count;
            }

            private static void Increment(Dictionary<string, int> counts, string stateId)
            {
                if (string.IsNullOrEmpty(stateId)) return;

                counts.TryGetValue(stateId, out int count);
                counts[stateId] = count + 1;
            }
        }
    }
}

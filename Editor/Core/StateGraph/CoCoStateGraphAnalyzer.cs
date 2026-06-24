using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Editor.Core.StateGraph
{
    public static class CoCoStateGraphAnalyzer
    {
        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static CoCoStateGraphModel Analyze(CoCoStateController rootController)
        {
            var model = new CoCoStateGraphModel
            {
                RootController = rootController
            };

            if (rootController == null)
            {
                model.Warnings.Add("No CoCoStateController selected.");
                return model;
            }

            var contextProvider = rootController.ContextProvider;
            var contextType = ResolveContextType(contextProvider);
            var layers = rootController.StateLayers;
            if (layers == null || layers.Count == 0)
            {
                model.Warnings.Add($"{rootController.name} has no explicit State Layers. Create a Main Layer explicitly.");
                return model;
            }

            var orderedLayers = BuildOrderedLayers(layers);
            foreach (var entry in orderedLayers)
            {
                var layer = entry.Layer;

                string layerId = BuildLayerId(rootController, layer, entry.DeclarationIndex);
                AnalyzeLayer(
                    model,
                    rootController,
                    null,
                    layerId,
                    layer.Name,
                    0,
                    layer.Order,
                    layer.Update,
                    layer.FixedUpdate,
                    layer,
                    layer.DefaultCoCoState,
                    layer.AvailableStates,
                    contextProvider,
                    contextType);
            }

            BuildTransitionEdges(model);
            return model;
        }

        private static List<LayerOrderEntry> BuildOrderedLayers(IReadOnlyList<CoCoStateLayer> layers)
        {
            var orderedLayers = new List<LayerOrderEntry>();
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null) continue;
                orderedLayers.Add(new LayerOrderEntry(layer, i));
            }

            orderedLayers.Sort(CompareLayerOrder);
            return orderedLayers;
        }

        private static int CompareLayerOrder(
            LayerOrderEntry left,
            LayerOrderEntry right)
        {
            int orderComparison = left.Layer.Order.CompareTo(right.Layer.Order);
            return orderComparison != 0
                ? orderComparison
                : left.DeclarationIndex.CompareTo(right.DeclarationIndex);
        }

        private static void AnalyzeLayer(
            CoCoStateGraphModel model,
            CoCoStateController controller,
            string parentLayerId,
            string layerId,
            string name,
            int depth,
            int order,
            bool update,
            bool fixedUpdate,
            CoCoStateLayer layer,
            CoCoStateBase defaultState,
            IReadOnlyList<CoCoStateBase> states,
            MonoBehaviour contextProvider,
            Type contextType)
        {
            var layerNode = new CoCoStateLayerNode
            {
                Id = layerId,
                ParentId = parentLayerId,
                Name = name,
                Depth = depth,
                Order = order,
                Updates = update,
                FixedUpdates = fixedUpdate,
                Controller = controller,
                Layer = layer,
                ContextProvider = contextProvider,
                ContextType = contextType,
                DefaultState = defaultState,
                CurrentState = ResolveCurrentState(controller, layer)
            };

            PopulateContextFields(layerNode);
            PopulateContextSources(layerNode);
            model.Layers.Add(layerNode);

            model.LayerEdges.Add(new CoCoStateLayerEdge
            {
                ParentLayerId = parentLayerId,
                ChildLayerId = layerId,
                Name = name,
                Order = order,
                Updates = update,
                FixedUpdates = fixedUpdate
            });

            if (states == null || states.Count == 0)
            {
                model.Warnings.Add($"{name} has no explicitly declared available states.");
                return;
            }

            foreach (var state in states)
            {
                if (state == null) continue;

                var definition = state.Definition;
                var stateNode = new CoCoStateNode
                {
                    Id = BuildStateId(layerId, state),
                    LayerId = layerId,
                    Name = definition.DisplayName,
                    Depth = depth,
                    State = state,
                    Definition = definition,
                    IsDefault = state == defaultState,
                    IsCurrent = Application.isPlaying && state == layerNode.CurrentState,
                    HasDefinition = definition.HasDeclarations
                };
                model.States.Add(stateNode);
                if (stateNode.IsDefault)
                {
                    model.LayerStateEdges.Add(new CoCoStateLayerStateEdge
                    {
                        LayerId = layerId,
                        StateId = stateNode.Id,
                        LayerName = name,
                        StateName = stateNode.Name,
                        IsDefault = true
                    });
                }

                if (!definition.HasDeclarations)
                {
                    model.Warnings.Add($"{state.GetType().Name} has no state definition declarations.");
                }
            }
        }

        private static CoCoStateBase ResolveCurrentState(
            CoCoStateController controller,
            CoCoStateLayer layer)
        {
            if (!Application.isPlaying || controller == null) return null;
            return controller.GetCurrentState(layer);
        }

        private static void BuildTransitionEdges(CoCoStateGraphModel model)
        {
            var statesByLayerAndType = new Dictionary<string, Dictionary<Type, List<CoCoStateNode>>>();
            foreach (var state in model.States)
            {
                if (state.State == null) continue;

                if (!statesByLayerAndType.TryGetValue(state.LayerId, out var statesByType))
                {
                    statesByType = new Dictionary<Type, List<CoCoStateNode>>();
                    statesByLayerAndType[state.LayerId] = statesByType;
                }

                var type = state.State.GetType();
                if (!statesByType.TryGetValue(type, out var nodes))
                {
                    nodes = new List<CoCoStateNode>();
                    statesByType[type] = nodes;
                }

                nodes.Add(state);
            }

            foreach (var state in model.States)
            {
                foreach (var target in state.Definition.TransitionTargets)
                {
                    CoCoStateNode targetNode = null;
                    if (target.StateType != null &&
                        statesByLayerAndType.TryGetValue(state.LayerId, out var statesByType) &&
                        statesByType.TryGetValue(target.StateType, out var candidates))
                    {
                        targetNode = candidates[0];
                        if (candidates.Count > 1)
                        {
                            model.Warnings.Add(
                                $"{state.Name} transition target {target.StateType.Name} is duplicated inside one State Layer.");
                        }
                    }

                    model.Transitions.Add(new CoCoStateTransitionEdge
                    {
                        SourceStateId = state.Id,
                        TargetStateId = targetNode?.Id,
                        TargetStateName = target.StateType != null ? target.StateType.Name : "Unknown",
                        TargetStateType = target.StateType,
                        Note = target.Note,
                        TargetResolved = targetNode != null
                    });
                }
            }
        }

        private static Type ResolveContextType(MonoBehaviour provider)
        {
            var context = ResolveContextInstance(provider);
            if (context != null) return context.GetType();

            if (provider == null) return null;

            foreach (var interfaceType in provider.GetType().GetInterfaces())
            {
                if (interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == typeof(ICoCoContextProvider<>))
                {
                    return interfaceType.GetGenericArguments()[0];
                }
            }

            return null;
        }

        private static ICoCoContext ResolveContextInstance(MonoBehaviour provider)
        {
            if (provider == null) return null;

            var property = provider.GetType().GetProperty(
                "Context",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(provider) as ICoCoContext;
        }

        private static void PopulateContextFields(CoCoStateLayerNode node)
        {
            if (node.ContextType == null) return;

            var fields = new List<FieldInfo>();
            for (Type type = node.ContextType;
                 type != null && type != typeof(object);
                 type = type.BaseType)
            {
                fields.AddRange(type.GetFields(InstanceFields | BindingFlags.DeclaredOnly));
            }

            fields.Reverse();
            foreach (var field in fields)
            {
                if (field.IsStatic) continue;
                if (field.Name.StartsWith("<", StringComparison.Ordinal)) continue;

                node.ContextFields.Add(new CoCoContextFieldNode
                {
                    Path = field.Name,
                    TypeName = field.FieldType.Name,
                    DeclaringTypeName = field.DeclaringType != null ? field.DeclaringType.Name : string.Empty,
                    IsExtension = IsExtensionContextField(node.ContextType, field)
                });
            }
        }

        private static bool IsExtensionContextField(Type contextType, FieldInfo field)
        {
            if (contextType == null || field.DeclaringType == null) return false;
            if (field.DeclaringType != contextType) return false;

            return InheritsFromTypeName(
                       contextType,
                       "CoCoFlow.Runtime.Gameplay.Character.CharacterContext") &&
                   contextType.FullName != "CoCoFlow.Runtime.Gameplay.Character.CharacterContext";
        }

        private static bool InheritsFromTypeName(Type type, string fullName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                if (current.FullName == fullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PopulateContextSources(CoCoStateLayerNode node)
        {
            if (node.ContextProvider == null) return;

            var sourceField = FindField(node.ContextProvider.GetType(), "contextSources");
            var sources = sourceField?.GetValue(node.ContextProvider) as IEnumerable;
            if (sources == null)
            {
                return;
            }

            foreach (var source in sources)
            {
                var behaviour = source as MonoBehaviour;
                if (behaviour == null) continue;

                node.ContextSources.Add(new CoCoContextSourceNode
                {
                    Name = behaviour.name,
                    TypeName = behaviour.GetType().Name,
                    Priority = ResolvePriority(behaviour)
                });
            }
        }

        private static int? ResolvePriority(object source)
        {
            var property = source.GetType().GetProperty(
                "Priority",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || property.PropertyType != typeof(int)) return null;

            return (int)property.GetValue(source);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(fieldName, InstanceFields);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static string BuildLayerId(
            CoCoStateController controller,
            CoCoStateLayer layer,
            int index)
        {
            string layerName = layer != null ? layer.Name : "layer";
            return $"layer:{controller.GetInstanceID()}:{index}:{layerName}";
        }

        private static string BuildStateId(string layerId, CoCoStateBase state)
        {
            return $"state:{layerId}:{state.GetInstanceID()}";
        }

        private readonly struct LayerOrderEntry
        {
            public LayerOrderEntry(CoCoStateLayer layer, int declarationIndex)
            {
                Layer = layer;
                DeclarationIndex = declarationIndex;
            }

            public CoCoStateLayer Layer { get; }
            public int DeclarationIndex { get; }
        }
    }
}

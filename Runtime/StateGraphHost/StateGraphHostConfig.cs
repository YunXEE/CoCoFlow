using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    public sealed partial class StateGraphHost
    {
        private StateGraphAsset ConfiguredGraph(ref IStateGraphProjectBindingProvider provider)
        {
            if (!(provider is StandardBindingProvider standard)) return stateGraphAsset;
            var graph = Instantiate(stateGraphAsset);
            try
            {
                var aliases = new Dictionary<StateDescriptorId, Type>();
                foreach (var layer in graph.Layers)
                {
                    foreach (var state in layer.States)
                        ConfigureStateAlias(state, provider.Catalog, aliases);
                    if (layer.AnyState != null)
                        ConfigureStateAlias(layer.AnyState, provider.Catalog, aliases);
                }
                provider = StandardBindingProvider.Build(standard.Assemblies, aliases);
                return graph;
            }
            catch
            {
                // Until return, the creator still owns the configured copy.
                ReleaseConfiguredGraph(graph);
                throw;
            }
        }

        private void ReleaseConfiguredGraph(StateGraphAsset graph)
        {
            if (graph == null || graph == stateGraphAsset) return;
            if (Application.isPlaying)
                Destroy(graph);
            else
                DestroyImmediate(graph);
        }

        private static void ConfigureStateAlias(StateGraphStateRecord state,
            GraphDescriptorCatalog catalog, Dictionary<StateDescriptorId, Type> aliases)
        {
            StateDescriptorId.TryCreate(state.StateDescriptorId.High, state.StateDescriptorId.Low, out var original);
            catalog.TryGetStateDescriptor(original, out var descriptor);
            string stateId = $"{state.StateId.High:x16}{state.StateId.Low:x16}";
            StandardDescriptors.TryCreate(descriptor.LogicType, "State:" + stateId, out var alias);
            aliases.Add(alias, descriptor.LogicType);
            state.StateDescriptorId = new SerializedId128(alias.High, alias.Low);
            if (descriptor.AuthoringConfigType == typeof(FieldConfig))
                state.Config = state.Config as FieldConfig ?? new FieldConfig();
        }
    }
}

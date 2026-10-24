using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public sealed partial class CoCoStateGraphHost
    {
        private CoCoStateGraphAsset ConfiguredGraph(ref ICoCoStateGraphProjectBindingProvider provider)
        {
            if (!(provider is CoCoStandardBindingProvider standard)) return stateGraphAsset;
            var graph = Instantiate(stateGraphAsset);
            var aliases = new Dictionary<CoCoStateDescriptorId, Type>();
            foreach (var layer in graph.Layers)
            {
                foreach (var state in layer.States)
                    ConfigureStateAlias(state, provider.Catalog, aliases);
                if (layer.AnyState != null)
                    ConfigureStateAlias(layer.AnyState, provider.Catalog, aliases);
            }
            provider = CoCoStandardBindingProvider.Build(standard.Assemblies, aliases);
            return graph;
        }

        private static void ConfigureStateAlias(CoCoStateGraphStateRecord state,
            CoCoGraphDescriptorCatalog catalog, Dictionary<CoCoStateDescriptorId, Type> aliases)
        {
            CoCoStateDescriptorId.TryCreate(state.StateDescriptorId.High, state.StateDescriptorId.Low, out var original);
            catalog.TryGetStateDescriptor(original, out var descriptor);
            string stateId = $"{state.StateId.High:x16}{state.StateId.Low:x16}";
            StandardDescriptors.TryCreate(descriptor.LogicType, "State:" + stateId, out var alias);
            aliases.Add(alias, descriptor.LogicType);
            state.StateDescriptorId = new CoCoSerializedId128(alias.High, alias.Low);
            if (descriptor.AuthoringConfigType == typeof(CoCoFieldConfig))
                state.Config = state.Config as CoCoFieldConfig ?? new CoCoFieldConfig();
        }
    }
}

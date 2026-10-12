using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public sealed partial class CoCoStateExecutionContext
    {
        private readonly CoCoStateIntentFrameView _intentView;

        /// <summary>
        /// Reads a frozen Intent declared by this State. Missing samples and
        /// undeclared types return false; another State's declarations grant no access.
        /// The standard catalog permits exactly one Intent identity per type.
        /// </summary>
        public bool TryReadIntent<TIntent>(out TIntent value) where TIntent : unmanaged =>
            _intentView.TryFirst(out value);

        private sealed class CoCoStateIntentFrameView : ICoCoIntentFrameLookup
        {
            private readonly CoCoStateExecutionContext _context;
            private CoCoCompiledStateGraph _graph;
            private CoCoStateId _stateId;
            private CoCoIntentRequirement[] _requirements = Array.Empty<CoCoIntentRequirement>();

            public CoCoStateIntentFrameView(CoCoStateExecutionContext context) => _context = context;
            private ICoCoIntentFrame Frame => _context._isCallbackActive ? _context._intents : null;
            public CoCoGraphInstanceId GraphInstanceId => Frame?.GraphInstanceId ?? default;
            public CoCoStateFlowFrameHeader Header => Frame?.Header ?? default;
            public CoCoFrameLayoutId LayoutId => Frame?.LayoutId ?? default;
            public bool IsFrozen => Frame?.IsFrozen == true;

            public void Configure(CoCoCompiledStateGraph graph, CoCoLayerId layerId, CoCoStateId stateId)
            {
                if (ReferenceEquals(graph, _graph) && stateId == _stateId) return;
                _graph = graph;
                _stateId = stateId;
                var requirements = new List<CoCoIntentRequirement>();
                if (graph.TryGetLayer(layerId, out CoCoCompiledStateLayer layer) &&
                    layer.TryGetState(stateId, out CoCoCompiledState state))
                {
                    foreach (CoCoIntentId id in state.Descriptor.IntentRequirements)
                        foreach (CoCoIntentRequirement requirement in graph.IntentRequirements.Requirements)
                            if (requirement.IntentId == id) requirements.Add(requirement);
                }
                _requirements = requirements.ToArray();
            }

            private bool Allows<TIntent>(CoCoIntentId id) where TIntent : unmanaged
            {
                foreach (CoCoIntentRequirement requirement in _requirements)
                    if (requirement.IntentId == id && requirement.ValueType == typeof(TIntent)) return true;
                return false;
            }

            public bool TryGet<TIntent>(CoCoIntentHandle<TIntent> handle, out TIntent value)
                where TIntent : unmanaged
            {
                if (Frame != null && Allows<TIntent>(handle.IntentId)) return Frame.TryGet(handle, out value);
                value = default;
                return false;
            }

            public bool IsPresent<TIntent>(CoCoIntentHandle<TIntent> handle) where TIntent : unmanaged =>
                Frame != null && Allows<TIntent>(handle.IntentId) && Frame.IsPresent(handle);

            public bool TryGet<TIntent>(CoCoIntentId intentId, out TIntent value) where TIntent : unmanaged
            {
                if (Frame is ICoCoIntentFrameLookup lookup && Allows<TIntent>(intentId))
                    return lookup.TryGet(intentId, out value);
                value = default;
                return false;
            }

            public bool TryFirst<TIntent>(out TIntent value) where TIntent : unmanaged
            {
                foreach (CoCoIntentRequirement requirement in _requirements)
                    if (requirement.ValueType == typeof(TIntent) && TryGet(requirement.IntentId, out value))
                        return true;
                value = default;
                return false;
            }
        }
    }
}

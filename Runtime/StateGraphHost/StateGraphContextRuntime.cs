using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraphHost
{
    internal enum GraphContextProducerKind
    {
        State = 1,
        Value = 2,
        Claim = 3
    }

    internal interface IGraphContextProducerBinding
    {
        GraphContextProducerKind Kind { get; }
        StateBlockId BlockId { get; }
        StateSlotId SlotId { get; }
        Type ValueType { get; }

        bool TryResolve(
            ContextFrameLayout layout,
            out CoCoDiagnostic diagnostic);
    }

    internal interface IGraphStateContextBinding : IGraphContextProducerBinding
    {
        LayerId LayerId { get; }
        StateId StateId { get; }
        Type MemoryType { get; }

        bool TryCapture(
            ulong token,
            ActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in PreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic);

        bool TryValidateInitialDefault(
            ActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            ContextFrameReadView defaults,
            out CoCoDiagnostic diagnostic);

        bool TryGetCaptured<TState>(
            ulong token,
            out GraphStateRecord<TState> record)
            where TState : unmanaged;

        bool TryGetCapturedHeader(
            ulong token,
            out bool isOnActivePath);

        bool TryReadRestoreHeader(
            ContextRestoreReadView source,
            out StateGraphRestoreState header);

        bool TryPrepareRestore(
            ContextRestoreReadView source,
            ActivationMemory candidateMemory,
            out CoCoDiagnostic diagnostic);

        void ClearCapture();
    }

    internal interface IGraphValueContextBinding : IGraphContextProducerBinding
    {
        bool TryCapture(
            in GraphContextCaptureContext context,
            in PreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic);
    }

    internal sealed class GraphStateContextBinding<TMemory, TState, TBinding> :
        IGraphStateContextBinding
        where TMemory : ActivationMemory
        where TState : unmanaged
        where TBinding : IActivationMemoryStateBinding<TMemory, TState>
    {
        private readonly TBinding _binding;
        private StateBlockHandle _block;
        private StateSlot<GraphStateRecord<TState>> _slot;
        private GraphStateRecord<TState> _captured;
        private ulong _captureToken;

        internal GraphStateContextBinding(
            LayerId layerId,
            StateId stateId,
            StateBlockId blockId,
            StateSlotId slotId,
            TBinding binding)
        {
            LayerId = layerId;
            StateId = stateId;
            BlockId = blockId;
            SlotId = slotId;
            _binding = binding;
        }

        public GraphContextProducerKind Kind => GraphContextProducerKind.State;
        public LayerId LayerId { get; }
        public StateId StateId { get; }
        public StateBlockId BlockId { get; }
        public StateSlotId SlotId { get; }
        public Type ValueType => typeof(GraphStateRecord<TState>);
        public Type MemoryType => typeof(TMemory);

        public bool TryResolve(
            ContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            if (layout == null ||
                !_binding.SemanticFingerprint.IsNonZero() ||
                !layout.TryResolveBlock(BlockId, out _block) ||
                _block.Owner != StateBlockOwner.Graph ||
                !layout.TryResolveSlot(SlotId, out _slot) ||
                FindSlot(layout, SlotId)?.RestorePolicy == ContextRestorePolicy.Derived)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Graph State producer must resolve one writable Graph-owned State record Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryCapture(
            ulong token,
            ActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in PreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic)
        {
            if (token == 0UL || !(memory is TMemory typedMemory))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph State memory did not match its registered portable binding.");
                return false;
            }

            TState state;
            try
            {
                if (!_binding.TryCapture(typedMemory, out state, out diagnostic) || diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.ContextCaptureFailed,
                            "Graph State memory binding rejected candidate capture.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph State memory binding threw during candidate capture.");
                return false;
            }

            if (!GraphStateRecord<TState>.TryCreate(
                    LayerId,
                    StateId,
                    isOnActivePath,
                    activationId,
                    localSeconds,
                    actionProgress,
                    enterPending,
                    memoryFingerprint,
                    state,
                    out _captured) ||
                !prepared.TryGetWriter(_block, out ContextFrameWriter writer) ||
                !writer.Write(_slot, _captured))
            {
                _captured = default;
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph State record could not be written to its declared Context Slot.");
                return false;
            }

            _captureToken = token;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryValidateInitialDefault(
            ActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            ContextFrameReadView defaults,
            out CoCoDiagnostic diagnostic)
        {
            if (!(memory is TMemory typedMemory) || !defaults.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Initial Graph State validation received incompatible memory or defaults.");
                return false;
            }

            TState state;
            try
            {
                if (!_binding.TryCapture(typedMemory, out state, out diagnostic) || diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.ContextCaptureFailed,
                            "Initial Graph State capture was rejected.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Initial Graph State capture threw.");
                return false;
            }

            if (!GraphStateRecord<TState>.TryCreate(
                    LayerId,
                    StateId,
                    isOnActivePath,
                    activationId,
                    localSeconds,
                    actionProgress,
                    enterPending,
                    memoryFingerprint,
                    state,
                    out GraphStateRecord<TState> captured) ||
                defaults.Read(_slot) != captured)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Trusted Layout defaults do not match the Runtime's initial Graph authority.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryGetCaptured<TRequested>(
            ulong token,
            out GraphStateRecord<TRequested> record)
            where TRequested : unmanaged
        {
            if (token == 0UL || token != _captureToken || typeof(TRequested) != typeof(TState))
            {
                record = default;
                return false;
            }

            ReadOnlySpan<GraphStateRecord<TState>> source =
                MemoryMarshal.CreateReadOnlySpan(ref _captured, 1);
            record = MemoryMarshal.Cast<
                GraphStateRecord<TState>,
                GraphStateRecord<TRequested>>(source)[0];
            return true;
        }

        public bool TryGetCapturedHeader(
            ulong token,
            out bool isOnActivePath)
        {
            if (token == 0UL || token != _captureToken)
            {
                isOnActivePath = false;
                return false;
            }

            isOnActivePath = _captured.IsOnActivePath;
            return true;
        }

        public bool TryReadRestoreHeader(
            ContextRestoreReadView source,
            out StateGraphRestoreState header)
        {
            if (!source.TryRead(_slot, out GraphStateRecord<TState> record))
            {
                header = default;
                return false;
            }

            header = new StateGraphRestoreState(
                record.LayerId,
                record.StateId,
                record.IsOnActivePath,
                record.ActivationId,
                record.LocalSeconds,
                record.ActionProgress,
                record.EnterPending,
                record.MemoryFingerprint,
                record.IsValid);
            return header.IsValid;
        }

        public bool TryPrepareRestore(
            ContextRestoreReadView source,
            ActivationMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            if (!source.TryRead(_slot, out GraphStateRecord<TState> record) ||
                !(candidateMemory is TMemory typedMemory))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Graph restore memory did not match its registered State binding.");
                return false;
            }

            try
            {
                if (!_binding.TryPrepareRestore(record.State, typedMemory, out diagnostic) ||
                    diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidGraphRestore,
                            "Graph State memory binding rejected restore preparation.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Graph State memory binding threw during restore preparation.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public void ClearCapture()
        {
            _captureToken = 0UL;
            _captured = default;
        }

        private static StateSlotDescriptor FindSlot(
            ContextFrameLayout layout,
            StateSlotId slotId)
        {
            for (int index = 0; index < layout.Slots.Count; index++)
            {
                if (layout.Slots[index].SlotId == slotId)
                {
                    return layout.Slots[index];
                }
            }

            return null;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, code, message);
    }

    internal sealed class GraphValueContextBinding<TValue, TProducer> :
        IGraphValueContextBinding
        where TValue : unmanaged
        where TProducer : IGraphContextValueProducer<TValue>
    {
        private readonly TProducer _producer;
        private StateBlockHandle _block;
        private StateSlot<TValue> _slot;

        internal GraphValueContextBinding(
            StateBlockId blockId,
            StateSlotId slotId,
            TProducer producer)
        {
            BlockId = blockId;
            SlotId = slotId;
            _producer = producer;
        }

        public GraphContextProducerKind Kind => GraphContextProducerKind.Value;
        public StateBlockId BlockId { get; }
        public StateSlotId SlotId { get; }
        public Type ValueType => typeof(TValue);

        public bool TryResolve(
            ContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            if (layout == null ||
                !_producer.SemanticFingerprint.IsNonZero() ||
                !layout.TryResolveBlock(BlockId, out _block) ||
                _block.Owner != StateBlockOwner.Graph ||
                !layout.TryResolveSlot(SlotId, out _slot))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Graph value producer must resolve one writable Graph-owned Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryCapture(
            in GraphContextCaptureContext context,
            in PreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic)
        {
            TValue value;
            try
            {
                if (!_producer.TryProduce(context, out value, out diagnostic) || diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.ContextCaptureFailed,
                            "Graph value producer rejected candidate capture.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph value producer threw during candidate capture.");
                return false;
            }

            if (!prepared.TryGetWriter(_block, out ContextFrameWriter writer) ||
                !writer.Write(_slot, value))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph value could not be written to its declared Context Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, code, message);
    }

    internal sealed class GraphClaimContextBinding : IGraphContextProducerBinding
    {
        internal GraphClaimContextBinding(
            StateBlockId blockId,
            StateSlotId slotId)
        {
            BlockId = blockId;
            SlotId = slotId;
        }

        public GraphContextProducerKind Kind => GraphContextProducerKind.Claim;
        public StateBlockId BlockId { get; }
        public StateSlotId SlotId { get; }
        public Type ValueType => typeof(OperatorClaimState);

        public bool TryResolve(
            ContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            StateSlotDescriptor descriptor = null;
            for (int index = 0; layout != null && index < layout.Slots.Count; index++)
            {
                if (layout.Slots[index].SlotId == SlotId)
                {
                    descriptor = layout.Slots[index];
                    break;
                }
            }

            if (layout == null ||
                !layout.TryResolveBlock(BlockId, out StateBlockHandle block) ||
                block.Owner != StateBlockOwner.Graph ||
                !layout.TryResolveSlot(SlotId, out StateSlot<OperatorClaimState> slot) ||
                descriptor?.RestorePolicy == ContextRestorePolicy.Derived)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Claim producer must resolve one writable Graph-owned Claim State Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    internal static class ProducerFingerprintExtensions
    {
        internal static bool IsNonZero(this ulong value) => value != 0UL;
    }

    internal sealed class StateGraphContextRuntime :
        IStateGraphContextRuntime,
        IStagedGraphReadSource,
        IActorContextValueSink,
        IDisposable
    {
        private readonly CompiledStateGraph _graph;
        private readonly GraphInstanceId _graphInstanceId;
        private readonly ContextFrameLayout _layout;
        private readonly IGraphStateContextBinding[] _states;
        private readonly IGraphValueContextBinding[] _values;
        private readonly StateSlotId[] _claimSlots;
        private readonly MonoBehaviour _actorComponent;
        private readonly IActorContextBinding _actorBinding;
        private readonly ActorContextBindingDescriptor _actorDescriptor;
        private readonly ActorValueBinding[] _actorValues;
        private readonly bool[] _actorWritten;
        private PreparedContextCommit _prepared;
        private ContextFrameReadView _previous;
        private TickFrame _tickFrame;
        private StagedOperationFrame _operationFrame;
        private ulong _activeToken;
        private bool _actorCaptureActive;
        private bool _actorWriteFault;
        private bool _isDisposed;

        private StateGraphContextRuntime(
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            IGraphStateContextBinding[] states,
            IGraphValueContextBinding[] values,
            StateSlotId[] claimSlots,
            MonoBehaviour actorComponent,
            IActorContextBinding actorBinding,
            ActorContextBindingDescriptor actorDescriptor,
            ActorValueBinding[] actorValues)
        {
            _graph = graph;
            _graphInstanceId = graphInstanceId;
            _layout = layout;
            _states = states;
            _values = values;
            _claimSlots = claimSlots;
            _actorComponent = actorComponent;
            _actorBinding = actorBinding;
            _actorDescriptor = actorDescriptor;
            _actorValues = actorValues;
            _actorWritten = new bool[actorValues.Length];
        }

        internal IReadOnlyList<StateSlotId> ClaimSlots => _claimSlots;

        internal static bool TryCreate(
            StateGraphHost host,
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            IReadOnlyList<IGraphContextProducerBinding> producers,
            MonoBehaviour actorComponent,
            out StateGraphContextRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            diagnostic = CoCoDiagnostic.None;
            if (host == null || graph == null || !graphInstanceId.IsValid ||
                layout == null || producers == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Context producer preflight requires one Host, Graph, exact Layout, and producer mapping.");
                return false;
            }

            var stateCandidates = new List<IGraphStateContextBinding>();
            var valueCandidates = new List<IGraphValueContextBinding>();
            var claimSlots = new List<StateSlotId>();
            var coveredSlots = new HashSet<StateSlotId>();
            for (int index = 0; index < producers.Count; index++)
            {
                IGraphContextProducerBinding producer = producers[index];
                if (producer == null || !coveredSlots.Add(producer.SlotId) ||
                    !producer.TryResolve(layout, out diagnostic))
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidContextProducer,
                            "Every Graph-owned direct Slot requires exactly one valid producer mapping.");
                    }

                    return false;
                }

                if (producer is IGraphStateContextBinding state)
                {
                    stateCandidates.Add(state);
                }
                else if (producer is IGraphValueContextBinding value)
                {
                    valueCandidates.Add(value);
                }
                else if (producer.Kind == GraphContextProducerKind.Claim)
                {
                    claimSlots.Add(producer.SlotId);
                }
                else
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidContextProducer,
                        "Graph Context producer kind is unsupported.");
                    return false;
                }
            }

            var orderedStates = new IGraphStateContextBinding[CountStates(graph)];
            int orderedIndex = 0;
            for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
            {
                CompiledStateLayer layer = graph.Layers[layerIndex];
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    CompiledState compiledState = layer.States[stateIndex];
                    IGraphStateContextBinding match = null;
                    for (int candidateIndex = 0; candidateIndex < stateCandidates.Count; candidateIndex++)
                    {
                        IGraphStateContextBinding candidate = stateCandidates[candidateIndex];
                        if (candidate.LayerId == layer.LayerId &&
                            candidate.StateId == compiledState.StateId)
                        {
                            if (match != null)
                            {
                                diagnostic = Error(
                                    CoCoDiagnosticCode.InvalidContextProducer,
                                    "A compiled State cannot map to multiple Graph State records.");
                                return false;
                            }

                            match = candidate;
                        }
                    }

                    if (match == null ||
                        match.MemoryType != compiledState.Descriptor.ActivationMemoryType)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidContextProducer,
                            "Every compiled State must map to one exact Graph State record and ActivationMemory binding.");
                        return false;
                    }

                    orderedStates[orderedIndex++] = match;
                }
            }

            if (stateCandidates.Count != orderedStates.Length)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Graph State producer mappings contain an extra compiled State record.");
                return false;
            }

            if (!TryValidateGraphSlotCoverage(layout, coveredSlots, out diagnostic) ||
                !TryResolveActor(
                    host,
                    layout,
                    actorComponent,
                    out IActorContextBinding actorBinding,
                    out ActorContextBindingDescriptor actorDescriptor,
                    out ActorValueBinding[] actorValues,
                    out diagnostic))
            {
                return false;
            }

            runtime = new StateGraphContextRuntime(
                graph,
                graphInstanceId,
                layout,
                orderedStates,
                valueCandidates.ToArray(),
                claimSlots.ToArray(),
                actorBinding == null ? null : actorComponent,
                actorBinding,
                actorDescriptor,
                actorValues);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryBeginGraphCapture(
            in StagedGraphStep stagedStep,
            ContextFrameReadView previous,
            in PreparedContextCommit prepared,
            ulong token,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed || _activeToken != 0UL || token == 0UL ||
                !stagedStep.IsValid ||
                !previous.IsValid || !prepared.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph Context capture requires one fresh staged transaction.");
                return false;
            }

            _activeToken = token;
            _previous = previous;
            _prepared = prepared;
            _tickFrame = stagedStep.TickFrame;
            _operationFrame = stagedStep.OperationFrame;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryCaptureState(
            int orderedStateIndex,
            ActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            out CoCoDiagnostic diagnostic)
        {
            if (_activeToken == 0UL || orderedStateIndex < 0 || orderedStateIndex >= _states.Length)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph State capture order no longer matches its preflight mapping.");
                return false;
            }

            return _states[orderedStateIndex].TryCapture(
                _activeToken,
                memory,
                isOnActivePath,
                activationId,
                localSeconds,
                actionProgress,
                enterPending,
                memoryFingerprint,
                _prepared,
                out diagnostic);
        }

        public bool TryValidateInitialStateDefault(
            int orderedStateIndex,
            ActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            ContextFrameReadView defaults,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed || orderedStateIndex < 0 || orderedStateIndex >= _states.Length)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Initial Graph State validation order no longer matches its producer mapping.");
                return false;
            }

            return _states[orderedStateIndex].TryValidateInitialDefault(
                memory,
                isOnActivePath,
                activationId,
                localSeconds,
                actionProgress,
                enterPending,
                memoryFingerprint,
                defaults,
                out diagnostic);
        }

        public bool TryCompleteGraphCapture(out CoCoDiagnostic diagnostic)
        {
            if (_activeToken == 0UL)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph Context capture token is not active.");
                return false;
            }

            var context = new GraphContextCaptureContext(
                _graphInstanceId,
                _tickFrame,
                _previous,
                new StagedGraphReadView(this, _activeToken),
                _operationFrame);
            for (int index = 0; index < _values.Length; index++)
            {
                if (!_values[index].TryCapture(context, _prepared, out diagnostic))
                {
                    EndGraphCapture();
                    return false;
                }
            }

            EndGraphCapture();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public void CancelCapture() => EndGraphCapture();

        internal bool TryCaptureActor(
            in TickFrame tickFrame,
            ContextFrameReadView previous,
            in PreparedContextCommit prepared,
            ulong token,
            out bool worldMayBeDirty,
            out CoCoDiagnostic diagnostic)
        {
            worldMayBeDirty = false;
            if (_actorBinding == null)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (_actorComponent == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Actor Context component was destroyed after Host startup.");
                return false;
            }

            if (_isDisposed || _actorCaptureActive || token == 0UL ||
                !tickFrame.IsValid || !previous.IsValid || !prepared.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Actor Context capture requires one fresh candidate transaction.");
                return false;
            }

            _activeToken = token;
            _previous = previous;
            _prepared = prepared;
            _tickFrame = tickFrame;
            _actorCaptureActive = true;
            _actorWriteFault = false;
            for (int index = 0; index < _actorWritten.Length; index++)
            {
                _actorWritten[index] = false;
            }

            worldMayBeDirty = true;
            bool captured;
            try
            {
                var context = new ActorContextCaptureContext(
                    _graphInstanceId,
                    tickFrame,
                    previous,
                    new ActorContextWriter(
                        _actorBinding,
                        _actorDescriptor,
                        this,
                        token));
                captured = _actorBinding.TryCapture(context, out diagnostic);
            }
            catch (Exception)
            {
                captured = false;
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Actor Context binding threw during candidate capture.");
            }
            finally
            {
                _actorCaptureActive = false;
            }

            bool succeeded = captured &&
                             !diagnostic.IsError &&
                             !_actorWriteFault &&
                             DidWriteAllActorValues();
            if (!succeeded && !diagnostic.IsError)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Actor Context binding rejected capture or attempted an invalid write.");
            }

            _prepared = default;
            _previous = default;
            _tickFrame = default;
            _activeToken = 0UL;
            return succeeded;
        }

        public bool TryValidateRestore(
            ContextRestoreReadView source,
            out ulong nextActivationValue,
            out CoCoDiagnostic diagnostic)
        {
            nextActivationValue = 1UL;
            if (_isDisposed || !source.IsValid || !_layout.IsSameInstance(source.Layout))
            {
                diagnostic = RestoreError("Graph restore requires one alive frame with the exact Host Layout.");
                return false;
            }

            int stateOffset = 0;
            for (int layerIndex = 0; layerIndex < _graph.Layers.Count; layerIndex++)
            {
                CompiledStateLayer layer = _graph.Layers[layerIndex];
                int activeLeafIndex = -1;
                int activeLeafCount = 0;
                int enterPendingStart = -1;
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    int absoluteStateIndex = stateOffset + stateIndex;
                    IGraphStateContextBinding binding = _states[absoluteStateIndex];
                    if (!binding.TryReadRestoreHeader(source, out StateGraphRestoreState header) ||
                        header.LayerId != layer.LayerId ||
                        header.StateId != layer.States[stateIndex].StateId)
                    {
                        diagnostic = RestoreError("Graph State restore record identity or structure is invalid.");
                        return false;
                    }

                    if (layer.States[stateIndex].IsAnyState &&
                        (!header.ActivationId.IsValid || header.IsOnActivePath || header.EnterPending))
                    {
                        diagnostic = RestoreError(
                            "Restored AnyState requires its own Activation without active-path or EnterPending markers.");
                        return false;
                    }

                    if (header.ActivationId.IsValid)
                    {
                        for (int priorIndex = 0; priorIndex < absoluteStateIndex; priorIndex++)
                        {
                            if (_states[priorIndex].TryReadRestoreHeader(
                                    source,
                                    out StateGraphRestoreState prior) &&
                                prior.ActivationId == header.ActivationId)
                            {
                                diagnostic = RestoreError(
                                    "Graph restore Activation identities must be globally unique.");
                                return false;
                            }
                        }

                        if (header.ActivationId.Value == ulong.MaxValue)
                        {
                            diagnostic = RestoreError("Graph restore cannot rebuild the next Activation identity.");
                            return false;
                        }

                        nextActivationValue = Math.Max(
                            nextActivationValue,
                            header.ActivationId.Value + 1UL);
                    }

                    if (header.IsOnActivePath && layer.States[stateIndex].IsLeaf)
                    {
                        activeLeafIndex = stateIndex;
                        activeLeafCount++;
                    }
                }

                if (activeLeafCount != 1)
                {
                    diagnostic = RestoreError("Each restored Layer requires exactly one active leaf.");
                    return false;
                }

                IReadOnlyList<int> path = layer.States[activeLeafIndex].RootPathStateIndices;
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    bool expectedActive = false;
                    for (int depth = 0; depth < path.Count; depth++)
                    {
                        if (path[depth] == stateIndex)
                        {
                            expectedActive = true;
                            break;
                        }
                    }

                    if (!_states[stateOffset + stateIndex].TryReadRestoreHeader(
                            source,
                            out StateGraphRestoreState header) ||
                        header.IsOnActivePath != expectedActive)
                    {
                        diagnostic = RestoreError(
                            "Restored Graph active markers must form exactly one Root-to-Leaf path.");
                        return false;
                    }
                }

                for (int depth = 0; depth < path.Count; depth++)
                {
                    IGraphStateContextBinding binding = _states[stateOffset + path[depth]];
                    binding.TryReadRestoreHeader(source, out StateGraphRestoreState header);
                    if (header.EnterPending && enterPendingStart < 0)
                    {
                        enterPendingStart = depth;
                    }

                    if (enterPendingStart >= 0 && !header.EnterPending)
                    {
                        diagnostic = RestoreError("EnterPending markers must be one contiguous active-path suffix.");
                        return false;
                    }
                }

                stateOffset += layer.States.Count;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPrepareStateRestore(
            int orderedStateIndex,
            ContextRestoreReadView source,
            ActivationMemory candidateMemory,
            out StateGraphRestoreState header,
            out CoCoDiagnostic diagnostic)
        {
            header = default;
            diagnostic = CoCoDiagnostic.None;
            if (orderedStateIndex < 0 || orderedStateIndex >= _states.Length ||
                !_states[orderedStateIndex].TryReadRestoreHeader(source, out header) ||
                !_states[orderedStateIndex].TryPrepareRestore(source, candidateMemory, out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = RestoreError("Graph State restore preparation failed.");
                }

                return false;
            }

            return true;
        }

        internal bool IsRestoredActiveActivation(
            ContextRestoreReadView source,
            ActivationId activationId)
        {
            if (!source.IsValid || !activationId.IsValid)
            {
                return false;
            }

            for (int index = 0; index < _states.Length; index++)
            {
                if (_states[index].TryReadRestoreHeader(source, out StateGraphRestoreState header) &&
                    header.IsOnActivePath && header.ActivationId == activationId)
                {
                    return true;
                }
            }

            // Only the Layer's declared AnyState may retain ownership outside
            // the normal path. Exited ordinary States also retain Activation IDs.
            int stateOffset = 0;
            for (int layerIndex = 0; layerIndex < _graph.Layers.Count; layerIndex++)
            {
                CompiledStateLayer layer = _graph.Layers[layerIndex];
                if (layer.AnyStateIndex >= 0 &&
                    _states[stateOffset + layer.AnyStateIndex].TryReadRestoreHeader(
                        source, out StateGraphRestoreState header) &&
                    !header.IsOnActivePath && !header.EnterPending &&
                    header.ActivationId == activationId)
                {
                    return true;
                }

                stateOffset += layer.States.Count;
            }

            return false;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            EndGraphCapture();
            _isDisposed = true;
        }

        bool IStagedGraphReadSource.IsActive(ulong token) =>
            !_isDisposed && token != 0UL && token == _activeToken && !_actorCaptureActive;

        bool IStagedGraphReadSource.TryGetActiveLeaf(
            ulong token,
            LayerId layerId,
            out StateId stateId)
        {
            if (!((IStagedGraphReadSource)this).IsActive(token))
            {
                stateId = default;
                return false;
            }

            for (int layerIndex = 0, stateOffset = 0;
                 layerIndex < _graph.Layers.Count;
                 stateOffset += _graph.Layers[layerIndex++].States.Count)
            {
                CompiledStateLayer layer = _graph.Layers[layerIndex];
                if (layer.LayerId != layerId)
                {
                    continue;
                }

                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    IGraphStateContextBinding binding = _states[stateOffset + stateIndex];
                    if (binding.TryGetCapturedHeader(token, out bool isOnActivePath) &&
                        isOnActivePath && layer.States[stateIndex].IsLeaf)
                    {
                        stateId = layer.States[stateIndex].StateId;
                        return true;
                    }
                }

                break;
            }

            stateId = default;
            return false;
        }

        bool IStagedGraphReadSource.TryGetState<TState>(
            ulong token,
            StateId stateId,
            out GraphStateRecord<TState> state)
        {
            if (((IStagedGraphReadSource)this).IsActive(token))
            {
                for (int index = 0; index < _states.Length; index++)
                {
                    if (_states[index].StateId == stateId &&
                        _states[index].TryGetCaptured(token, out state))
                    {
                        return true;
                    }
                }
            }

            state = default;
            return false;
        }

        bool IActorContextValueSink.IsActive(
            ulong token,
            IActorContextBinding binding) =>
            !_isDisposed && _actorCaptureActive && token != 0UL &&
            token == _activeToken && ReferenceEquals(binding, _actorBinding);

        void IActorContextValueSink.RejectWrite(
            ulong token,
            IActorContextBinding binding)
        {
            if (_actorCaptureActive)
            {
                _actorWriteFault = true;
            }
        }

        bool IActorContextValueSink.TryWrite<TValue>(
            ulong token,
            IActorContextBinding binding,
            StateSlotId slotId,
            in TValue value)
        {
            if (!((IActorContextValueSink)this).IsActive(token, binding) ||
                !_layout.TryResolveSlot(slotId, out StateSlot<TValue> slot))
            {
                return false;
            }

            for (int index = 0; index < _actorValues.Length; index++)
            {
                ActorValueBinding actorValue = _actorValues[index];
                if (actorValue.SlotId == slotId && actorValue.ValueType == typeof(TValue) &&
                    !_actorWritten[index] &&
                    _prepared.TryGetWriter(actorValue.Block, out ContextFrameWriter writer) &&
                    writer.Write(slot, value))
                {
                    _actorWritten[index] = true;
                    return true;
                }
            }

            return false;
        }

        private bool DidWriteAllActorValues()
        {
            for (int index = 0; index < _actorWritten.Length; index++)
            {
                if (!_actorWritten[index])
                {
                    return false;
                }
            }

            return true;
        }

        private void EndGraphCapture()
        {
            for (int index = 0; index < _states.Length; index++)
            {
                _states[index].ClearCapture();
            }

            _prepared = default;
            _previous = default;
            _tickFrame = default;
            _operationFrame = default;
            _activeToken = 0UL;
            _actorCaptureActive = false;
            _actorWriteFault = false;
        }

        private static int CountStates(CompiledStateGraph graph)
        {
            int count = 0;
            for (int index = 0; index < graph.Layers.Count; index++)
            {
                count += graph.Layers[index].States.Count;
            }

            return count;
        }

        private static bool TryValidateGraphSlotCoverage(
            ContextFrameLayout layout,
            HashSet<StateSlotId> covered,
            out CoCoDiagnostic diagnostic)
        {
            for (int blockIndex = 0; blockIndex < layout.Blocks.Count; blockIndex++)
            {
                StateBlockDescriptor block = layout.Blocks[blockIndex];
                if (block.Owner != StateBlockOwner.Graph)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    StateSlotDescriptor slot = block.Slots[slotIndex];
                    if (slot.RestorePolicy != ContextRestorePolicy.Derived &&
                        !covered.Contains(slot.SlotId))
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidContextProducer,
                            "Every direct Graph-owned Context Slot requires exactly one producer.");
                        return false;
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryResolveActor(
            StateGraphHost host,
            ContextFrameLayout layout,
            MonoBehaviour component,
            out IActorContextBinding actorBinding,
            out ActorContextBindingDescriptor descriptor,
            out ActorValueBinding[] values,
            out CoCoDiagnostic diagnostic)
        {
            var required = new List<StateSlotDescriptor>();
            for (int blockIndex = 0; blockIndex < layout.Blocks.Count; blockIndex++)
            {
                StateBlockDescriptor block = layout.Blocks[blockIndex];
                if (block.Owner != StateBlockOwner.Actor)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    if (block.Slots[slotIndex].RestorePolicy != ContextRestorePolicy.Derived)
                    {
                        required.Add(block.Slots[slotIndex]);
                    }
                }
            }

            actorBinding = null;
            descriptor = null;
            values = Array.Empty<ActorValueBinding>();
            if (required.Count == 0)
            {
                if (!ReferenceEquals(component, null))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidActorBinding,
                        "Actor Context component must be empty when the Layout has no direct Actor-owned Slot.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (component == null || !(component is IActorContextBinding typed) ||
                !StateGraphHostBoundary.Contains(host, component))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidActorBinding,
                    "Direct Actor-owned Slots require one live binding inside the Host boundary.");
                return false;
            }

            try
            {
                descriptor = typed.Descriptor;
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidActorBinding,
                    "Actor Context descriptor getter threw during preflight.");
                return false;
            }

            if (descriptor == null || !descriptor.IsValid ||
                descriptor.BindingType != component.GetType() ||
                descriptor.ValueCount != required.Count)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidActorBinding,
                    "Actor Context descriptor must exactly match its component and every Actor-owned Slot.");
                return false;
            }

            values = new ActorValueBinding[required.Count];
            var seen = new HashSet<StateSlotId>();
            for (int index = 0; index < descriptor.Values.Count; index++)
            {
                ActorContextValueRequirement declared = descriptor.Values[index];
                StateSlotDescriptor slot = null;
                StateBlockDescriptor block = null;
                for (int blockIndex = 0; blockIndex < layout.Blocks.Count && slot == null; blockIndex++)
                {
                    StateBlockDescriptor candidateBlock = layout.Blocks[blockIndex];
                    for (int slotIndex = 0; slotIndex < candidateBlock.Slots.Count; slotIndex++)
                    {
                        if (candidateBlock.Slots[slotIndex].SlotId == declared.SlotId)
                        {
                            block = candidateBlock;
                            slot = candidateBlock.Slots[slotIndex];
                            break;
                        }
                    }
                }

                if (slot == null || block == null || block.Owner != StateBlockOwner.Actor ||
                    slot.RestorePolicy == ContextRestorePolicy.Derived ||
                    slot.ValueType != declared.ValueType || !seen.Add(declared.SlotId) ||
                    !layout.TryResolveBlock(block.BlockId, out StateBlockHandle blockHandle))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidActorBinding,
                        "Actor Context descriptor contains a missing, duplicate, derived, or type-mismatched Slot.");
                    return false;
                }

                values[index] = new ActorValueBinding(
                    declared.SlotId,
                    declared.ValueType,
                    blockHandle);
            }

            actorBinding = typed;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, code, message);

        private static CoCoDiagnostic RestoreError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.InvalidGraphRestore,
                message);

        private readonly struct ActorValueBinding
        {
            internal ActorValueBinding(
                StateSlotId slotId,
                Type valueType,
                StateBlockHandle block)
            {
                SlotId = slotId;
                ValueType = valueType;
                Block = block;
            }

            internal StateSlotId SlotId { get; }
            internal Type ValueType { get; }
            internal StateBlockHandle Block { get; }
        }
    }

}

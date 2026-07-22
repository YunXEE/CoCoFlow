using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    internal enum CoCoGraphContextProducerKind
    {
        State = 1,
        Value = 2,
        Claim = 3
    }

    internal interface ICoCoGraphContextProducerBinding
    {
        CoCoGraphContextProducerKind Kind { get; }
        CoCoStateBlockId BlockId { get; }
        CoCoStateSlotId SlotId { get; }
        Type ValueType { get; }

        bool TryResolve(
            CoCoContextFrameLayout layout,
            out CoCoDiagnostic diagnostic);
    }

    internal interface ICoCoGraphStateContextBinding : ICoCoGraphContextProducerBinding
    {
        CoCoLayerId LayerId { get; }
        CoCoStateId StateId { get; }
        Type MemoryType { get; }

        bool TryCapture(
            ulong token,
            CoCoActivationMemory memory,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in CoCoPreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic);

        bool TryValidateInitialDefault(
            CoCoActivationMemory memory,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            CoCoContextFrameReadView defaults,
            out CoCoDiagnostic diagnostic);

        bool TryGetCaptured<TState>(
            ulong token,
            out CoCoGraphStateRecord<TState> record)
            where TState : unmanaged;

        bool TryGetCapturedHeader(
            ulong token,
            out bool isOnActivePath);

        bool TryReadRestoreHeader(
            CoCoContextRestoreReadView source,
            out CoCoStateGraphRestoreState header);

        bool TryPrepareRestore(
            CoCoContextRestoreReadView source,
            CoCoActivationMemory candidateMemory,
            out CoCoDiagnostic diagnostic);

        void ClearCapture();
    }

    internal interface ICoCoGraphValueContextBinding : ICoCoGraphContextProducerBinding
    {
        bool TryCapture(
            in CoCoGraphContextCaptureContext context,
            in CoCoPreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic);
    }

    internal sealed class CoCoGraphStateContextBinding<TMemory, TState, TBinding> :
        ICoCoGraphStateContextBinding
        where TMemory : CoCoActivationMemory
        where TState : unmanaged
        where TBinding : ICoCoActivationMemoryStateBinding<TMemory, TState>
    {
        private readonly TBinding _binding;
        private CoCoStateBlockHandle _block;
        private CoCoStateSlot<CoCoGraphStateRecord<TState>> _slot;
        private CoCoGraphStateRecord<TState> _captured;
        private ulong _captureToken;

        internal CoCoGraphStateContextBinding(
            CoCoLayerId layerId,
            CoCoStateId stateId,
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            TBinding binding)
        {
            LayerId = layerId;
            StateId = stateId;
            BlockId = blockId;
            SlotId = slotId;
            _binding = binding;
        }

        public CoCoGraphContextProducerKind Kind => CoCoGraphContextProducerKind.State;
        public CoCoLayerId LayerId { get; }
        public CoCoStateId StateId { get; }
        public CoCoStateBlockId BlockId { get; }
        public CoCoStateSlotId SlotId { get; }
        public Type ValueType => typeof(CoCoGraphStateRecord<TState>);
        public Type MemoryType => typeof(TMemory);

        public bool TryResolve(
            CoCoContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            if (layout == null ||
                !_binding.SemanticFingerprint.IsNonZero() ||
                !layout.TryResolveBlock(BlockId, out _block) ||
                _block.Owner != CoCoStateBlockOwner.Graph ||
                !layout.TryResolveSlot(SlotId, out _slot) ||
                FindSlot(layout, SlotId)?.RestorePolicy == CoCoContextRestorePolicy.Derived)
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
            CoCoActivationMemory memory,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in CoCoPreparedContextCommit prepared,
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

            if (!CoCoGraphStateRecord<TState>.TryCreate(
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
                !prepared.TryGetWriter(_block, out CoCoContextFrameWriter writer) ||
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
            CoCoActivationMemory memory,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            CoCoContextFrameReadView defaults,
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

            if (!CoCoGraphStateRecord<TState>.TryCreate(
                    LayerId,
                    StateId,
                    isOnActivePath,
                    activationId,
                    localSeconds,
                    actionProgress,
                    enterPending,
                    memoryFingerprint,
                    state,
                    out CoCoGraphStateRecord<TState> captured) ||
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
            out CoCoGraphStateRecord<TRequested> record)
            where TRequested : unmanaged
        {
            if (token == 0UL || token != _captureToken || typeof(TRequested) != typeof(TState))
            {
                record = default;
                return false;
            }

            ReadOnlySpan<CoCoGraphStateRecord<TState>> source =
                MemoryMarshal.CreateReadOnlySpan(ref _captured, 1);
            record = MemoryMarshal.Cast<
                CoCoGraphStateRecord<TState>,
                CoCoGraphStateRecord<TRequested>>(source)[0];
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
            CoCoContextRestoreReadView source,
            out CoCoStateGraphRestoreState header)
        {
            if (!source.TryRead(_slot, out CoCoGraphStateRecord<TState> record))
            {
                header = default;
                return false;
            }

            header = new CoCoStateGraphRestoreState(
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
            CoCoContextRestoreReadView source,
            CoCoActivationMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            if (!source.TryRead(_slot, out CoCoGraphStateRecord<TState> record) ||
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

        private static CoCoStateSlotDescriptor FindSlot(
            CoCoContextFrameLayout layout,
            CoCoStateSlotId slotId)
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

    internal sealed class CoCoGraphValueContextBinding<TValue, TProducer> :
        ICoCoGraphValueContextBinding
        where TValue : unmanaged
        where TProducer : ICoCoGraphContextValueProducer<TValue>
    {
        private readonly TProducer _producer;
        private CoCoStateBlockHandle _block;
        private CoCoStateSlot<TValue> _slot;

        internal CoCoGraphValueContextBinding(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            TProducer producer)
        {
            BlockId = blockId;
            SlotId = slotId;
            _producer = producer;
        }

        public CoCoGraphContextProducerKind Kind => CoCoGraphContextProducerKind.Value;
        public CoCoStateBlockId BlockId { get; }
        public CoCoStateSlotId SlotId { get; }
        public Type ValueType => typeof(TValue);

        public bool TryResolve(
            CoCoContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            if (layout == null ||
                !_producer.SemanticFingerprint.IsNonZero() ||
                !layout.TryResolveBlock(BlockId, out _block) ||
                _block.Owner != CoCoStateBlockOwner.Graph ||
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
            in CoCoGraphContextCaptureContext context,
            in CoCoPreparedContextCommit prepared,
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

            if (!prepared.TryGetWriter(_block, out CoCoContextFrameWriter writer) ||
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

    internal sealed class CoCoGraphClaimContextBinding : ICoCoGraphContextProducerBinding
    {
        internal CoCoGraphClaimContextBinding(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId)
        {
            BlockId = blockId;
            SlotId = slotId;
        }

        public CoCoGraphContextProducerKind Kind => CoCoGraphContextProducerKind.Claim;
        public CoCoStateBlockId BlockId { get; }
        public CoCoStateSlotId SlotId { get; }
        public Type ValueType => typeof(CoCoOperatorClaimState);

        public bool TryResolve(
            CoCoContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            CoCoStateSlotDescriptor descriptor = null;
            for (int index = 0; layout != null && index < layout.Slots.Count; index++)
            {
                if (layout.Slots[index].SlotId == SlotId)
                {
                    descriptor = layout.Slots[index];
                    break;
                }
            }

            if (layout == null ||
                !layout.TryResolveBlock(BlockId, out CoCoStateBlockHandle block) ||
                block.Owner != CoCoStateBlockOwner.Graph ||
                !layout.TryResolveSlot(SlotId, out CoCoStateSlot<CoCoOperatorClaimState> slot) ||
                descriptor?.RestorePolicy == CoCoContextRestorePolicy.Derived)
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

    internal static class CoCoProducerFingerprintExtensions
    {
        internal static bool IsNonZero(this ulong value) => value != 0UL;
    }

    internal sealed class CoCoStateGraphContextRuntime :
        ICoCoStateGraphContextRuntime,
        ICoCoStagedGraphReadSource,
        ICoCoActorContextValueSink,
        IDisposable
    {
        private readonly CoCoCompiledStateGraph _graph;
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoContextFrameLayout _layout;
        private readonly ICoCoGraphStateContextBinding[] _states;
        private readonly ICoCoGraphValueContextBinding[] _values;
        private readonly CoCoStateSlotId[] _claimSlots;
        private readonly MonoBehaviour _actorComponent;
        private readonly ICoCoActorContextBinding _actorBinding;
        private readonly CoCoActorContextBindingDescriptor _actorDescriptor;
        private readonly ActorValueBinding[] _actorValues;
        private readonly bool[] _actorWritten;
        private CoCoPreparedContextCommit _prepared;
        private CoCoContextFrameReadView _previous;
        private CoCoTickFrame _tickFrame;
        private CoCoStagedOperationFrame _operationFrame;
        private ulong _activeToken;
        private bool _actorCaptureActive;
        private bool _actorWriteFault;
        private bool _isDisposed;

        private CoCoStateGraphContextRuntime(
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout layout,
            ICoCoGraphStateContextBinding[] states,
            ICoCoGraphValueContextBinding[] values,
            CoCoStateSlotId[] claimSlots,
            MonoBehaviour actorComponent,
            ICoCoActorContextBinding actorBinding,
            CoCoActorContextBindingDescriptor actorDescriptor,
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

        internal IReadOnlyList<CoCoStateSlotId> ClaimSlots => _claimSlots;

        internal static bool TryCreate(
            CoCoStateGraphHost host,
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout layout,
            IReadOnlyList<ICoCoGraphContextProducerBinding> producers,
            MonoBehaviour actorComponent,
            out CoCoStateGraphContextRuntime runtime,
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

            var stateCandidates = new List<ICoCoGraphStateContextBinding>();
            var valueCandidates = new List<ICoCoGraphValueContextBinding>();
            var claimSlots = new List<CoCoStateSlotId>();
            var coveredSlots = new HashSet<CoCoStateSlotId>();
            for (int index = 0; index < producers.Count; index++)
            {
                ICoCoGraphContextProducerBinding producer = producers[index];
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

                if (producer is ICoCoGraphStateContextBinding state)
                {
                    stateCandidates.Add(state);
                }
                else if (producer is ICoCoGraphValueContextBinding value)
                {
                    valueCandidates.Add(value);
                }
                else if (producer.Kind == CoCoGraphContextProducerKind.Claim)
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

            var orderedStates = new ICoCoGraphStateContextBinding[CountStates(graph)];
            int orderedIndex = 0;
            for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
            {
                CoCoCompiledStateLayer layer = graph.Layers[layerIndex];
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    CoCoCompiledState compiledState = layer.States[stateIndex];
                    ICoCoGraphStateContextBinding match = null;
                    for (int candidateIndex = 0; candidateIndex < stateCandidates.Count; candidateIndex++)
                    {
                        ICoCoGraphStateContextBinding candidate = stateCandidates[candidateIndex];
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
                    out ICoCoActorContextBinding actorBinding,
                    out CoCoActorContextBindingDescriptor actorDescriptor,
                    out ActorValueBinding[] actorValues,
                    out diagnostic))
            {
                return false;
            }

            runtime = new CoCoStateGraphContextRuntime(
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
            in CoCoStagedGraphStep stagedStep,
            CoCoContextFrameReadView previous,
            in CoCoPreparedContextCommit prepared,
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
            CoCoActivationMemory memory,
            bool isOnActivePath,
            CoCoActivationId activationId,
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
            CoCoActivationMemory memory,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            CoCoContextFrameReadView defaults,
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

            var context = new CoCoGraphContextCaptureContext(
                _graphInstanceId,
                _tickFrame,
                _previous,
                new CoCoStagedGraphReadView(this, _activeToken),
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
            in CoCoTickFrame tickFrame,
            CoCoContextFrameReadView previous,
            in CoCoPreparedContextCommit prepared,
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
                var context = new CoCoActorContextCaptureContext(
                    _graphInstanceId,
                    tickFrame,
                    previous,
                    new CoCoActorContextWriter(
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
            CoCoContextRestoreReadView source,
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
                CoCoCompiledStateLayer layer = _graph.Layers[layerIndex];
                int activeLeafIndex = -1;
                int activeLeafCount = 0;
                int enterPendingStart = -1;
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    int absoluteStateIndex = stateOffset + stateIndex;
                    ICoCoGraphStateContextBinding binding = _states[absoluteStateIndex];
                    if (!binding.TryReadRestoreHeader(source, out CoCoStateGraphRestoreState header) ||
                        header.LayerId != layer.LayerId ||
                        header.StateId != layer.States[stateIndex].StateId)
                    {
                        diagnostic = RestoreError("Graph State restore record identity or structure is invalid.");
                        return false;
                    }

                    if (header.ActivationId.IsValid)
                    {
                        for (int priorIndex = 0; priorIndex < absoluteStateIndex; priorIndex++)
                        {
                            if (_states[priorIndex].TryReadRestoreHeader(
                                    source,
                                    out CoCoStateGraphRestoreState prior) &&
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
                            out CoCoStateGraphRestoreState header) ||
                        header.IsOnActivePath != expectedActive)
                    {
                        diagnostic = RestoreError(
                            "Restored Graph active markers must form exactly one Root-to-Leaf path.");
                        return false;
                    }
                }

                for (int depth = 0; depth < path.Count; depth++)
                {
                    ICoCoGraphStateContextBinding binding = _states[stateOffset + path[depth]];
                    binding.TryReadRestoreHeader(source, out CoCoStateGraphRestoreState header);
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
            CoCoContextRestoreReadView source,
            CoCoActivationMemory candidateMemory,
            out CoCoStateGraphRestoreState header,
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
            CoCoContextRestoreReadView source,
            CoCoActivationId activationId)
        {
            if (!source.IsValid || !activationId.IsValid)
            {
                return false;
            }

            for (int index = 0; index < _states.Length; index++)
            {
                if (_states[index].TryReadRestoreHeader(source, out CoCoStateGraphRestoreState header) &&
                    header.IsOnActivePath && header.ActivationId == activationId)
                {
                    return true;
                }
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

        bool ICoCoStagedGraphReadSource.IsActive(ulong token) =>
            !_isDisposed && token != 0UL && token == _activeToken && !_actorCaptureActive;

        bool ICoCoStagedGraphReadSource.TryGetActiveLeaf(
            ulong token,
            CoCoLayerId layerId,
            out CoCoStateId stateId)
        {
            if (!((ICoCoStagedGraphReadSource)this).IsActive(token))
            {
                stateId = default;
                return false;
            }

            for (int layerIndex = 0, stateOffset = 0;
                 layerIndex < _graph.Layers.Count;
                 stateOffset += _graph.Layers[layerIndex++].States.Count)
            {
                CoCoCompiledStateLayer layer = _graph.Layers[layerIndex];
                if (layer.LayerId != layerId)
                {
                    continue;
                }

                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    ICoCoGraphStateContextBinding binding = _states[stateOffset + stateIndex];
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

        bool ICoCoStagedGraphReadSource.TryGetState<TState>(
            ulong token,
            CoCoStateId stateId,
            out CoCoGraphStateRecord<TState> state)
        {
            if (((ICoCoStagedGraphReadSource)this).IsActive(token))
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

        bool ICoCoActorContextValueSink.IsActive(
            ulong token,
            ICoCoActorContextBinding binding) =>
            !_isDisposed && _actorCaptureActive && token != 0UL &&
            token == _activeToken && ReferenceEquals(binding, _actorBinding);

        void ICoCoActorContextValueSink.RejectWrite(
            ulong token,
            ICoCoActorContextBinding binding)
        {
            if (_actorCaptureActive)
            {
                _actorWriteFault = true;
            }
        }

        bool ICoCoActorContextValueSink.TryWrite<TValue>(
            ulong token,
            ICoCoActorContextBinding binding,
            CoCoStateSlotId slotId,
            in TValue value)
        {
            if (!((ICoCoActorContextValueSink)this).IsActive(token, binding) ||
                !_layout.TryResolveSlot(slotId, out CoCoStateSlot<TValue> slot))
            {
                return false;
            }

            for (int index = 0; index < _actorValues.Length; index++)
            {
                ActorValueBinding actorValue = _actorValues[index];
                if (actorValue.SlotId == slotId && actorValue.ValueType == typeof(TValue) &&
                    !_actorWritten[index] &&
                    _prepared.TryGetWriter(actorValue.Block, out CoCoContextFrameWriter writer) &&
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

        private static int CountStates(CoCoCompiledStateGraph graph)
        {
            int count = 0;
            for (int index = 0; index < graph.Layers.Count; index++)
            {
                count += graph.Layers[index].States.Count;
            }

            return count;
        }

        private static bool TryValidateGraphSlotCoverage(
            CoCoContextFrameLayout layout,
            HashSet<CoCoStateSlotId> covered,
            out CoCoDiagnostic diagnostic)
        {
            for (int blockIndex = 0; blockIndex < layout.Blocks.Count; blockIndex++)
            {
                CoCoStateBlockDescriptor block = layout.Blocks[blockIndex];
                if (block.Owner != CoCoStateBlockOwner.Graph)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    CoCoStateSlotDescriptor slot = block.Slots[slotIndex];
                    if (slot.RestorePolicy != CoCoContextRestorePolicy.Derived &&
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
            CoCoStateGraphHost host,
            CoCoContextFrameLayout layout,
            MonoBehaviour component,
            out ICoCoActorContextBinding actorBinding,
            out CoCoActorContextBindingDescriptor descriptor,
            out ActorValueBinding[] values,
            out CoCoDiagnostic diagnostic)
        {
            var required = new List<CoCoStateSlotDescriptor>();
            for (int blockIndex = 0; blockIndex < layout.Blocks.Count; blockIndex++)
            {
                CoCoStateBlockDescriptor block = layout.Blocks[blockIndex];
                if (block.Owner != CoCoStateBlockOwner.Actor)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    if (block.Slots[slotIndex].RestorePolicy != CoCoContextRestorePolicy.Derived)
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

            if (component == null || !(component is ICoCoActorContextBinding typed) ||
                !CoCoStateGraphHostBoundary.Contains(host, component))
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
            var seen = new HashSet<CoCoStateSlotId>();
            for (int index = 0; index < descriptor.Values.Count; index++)
            {
                CoCoActorContextValueRequirement declared = descriptor.Values[index];
                CoCoStateSlotDescriptor slot = null;
                CoCoStateBlockDescriptor block = null;
                for (int blockIndex = 0; blockIndex < layout.Blocks.Count && slot == null; blockIndex++)
                {
                    CoCoStateBlockDescriptor candidateBlock = layout.Blocks[blockIndex];
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

                if (slot == null || block == null || block.Owner != CoCoStateBlockOwner.Actor ||
                    slot.RestorePolicy == CoCoContextRestorePolicy.Derived ||
                    slot.ValueType != declared.ValueType || !seen.Add(declared.SlotId) ||
                    !layout.TryResolveBlock(block.BlockId, out CoCoStateBlockHandle blockHandle))
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
                CoCoStateSlotId slotId,
                Type valueType,
                CoCoStateBlockHandle block)
            {
                SlotId = slotId;
                ValueType = valueType;
                Block = block;
            }

            internal CoCoStateSlotId SlotId { get; }
            internal Type ValueType { get; }
            internal CoCoStateBlockHandle Block { get; }
        }
    }

}

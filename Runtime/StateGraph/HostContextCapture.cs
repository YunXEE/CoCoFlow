using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

// 候选提交从这里采集图与场景状态；场景组件只取得本次回调的写入权限。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>验证场景所有者及写入权限，在同一份候选 Context 上采集或准备恢复。</summary>
    internal sealed class HostContextCapture :
        IStateGraphContextRuntime,
        IStagedGraphReadSource,
        IHostContextValueSink,
        IDisposable
    {
        private readonly CompiledStateGraph _graph;
        private readonly GraphInstanceId _graphInstanceId;
        private readonly ContextFrameLayout _layout;
        private readonly IGraphStateContextBinding[] _states;
        private readonly IGraphValueContextBinding[] _values;
        private readonly StateSlotId[] _claimSlots;
        private readonly MonoBehaviour _sceneComponent;
        private readonly IHostContextBinding _sceneBinding;
        private readonly HostContextBindingDescriptor _sceneDescriptor;
        private readonly HostContextValueBinding[] _sceneValues;
        private readonly bool[] _sceneWritten;
        private PreparedContextCommit _prepared;
        private ContextFrameReadView _previous;
        private TickFrame _tickFrame;
        private StagedOperationFrame _operationFrame;
        private ulong _activeToken;
        private bool _sceneCaptureActive;
        private bool _sceneWriteFault;
        private bool _isDisposed;

        private HostContextCapture(
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            IGraphStateContextBinding[] states,
            IGraphValueContextBinding[] values,
            StateSlotId[] claimSlots,
            MonoBehaviour actorComponent,
            IHostContextBinding actorBinding,
            HostContextBindingDescriptor actorDescriptor,
            HostContextValueBinding[] actorValues)
        {
            _graph = graph;
            _graphInstanceId = graphInstanceId;
            _layout = layout;
            _states = states;
            _values = values;
            _claimSlots = claimSlots;
            _sceneComponent = actorComponent;
            _sceneBinding = actorBinding;
            _sceneDescriptor = actorDescriptor;
            _sceneValues = actorValues;
            _sceneWritten = new bool[actorValues.Length];
        }

        internal IReadOnlyList<StateSlotId> ClaimSlots => _claimSlots;

        internal static bool TryCreate(
            HostComponentBoundary boundary,
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            IReadOnlyList<IGraphContextProducerBinding> producers,
            MonoBehaviour actorComponent,
            out HostContextCapture runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            diagnostic = CoCoDiagnostic.None;
            if (!boundary.HasOwner || graph == null || !graphInstanceId.IsValid ||
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
                            "Every compiled State must map to one exact Graph State record and StateActivationMemory binding.");
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
                !TryResolveHostContext(
                    boundary,
                    layout,
                    actorComponent,
                    out IHostContextBinding actorBinding,
                    out HostContextBindingDescriptor actorDescriptor,
                    out HostContextValueBinding[] actorValues,
                    out diagnostic))
            {
                return false;
            }

            runtime = new HostContextCapture(
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
            StateActivationMemory memory,
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
            StateActivationMemory memory,
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

        internal bool TryCaptureHostContext(
            in TickFrame tickFrame,
            ContextFrameReadView previous,
            in PreparedContextCommit prepared,
            ulong token,
            out bool worldMayBeDirty,
            out CoCoDiagnostic diagnostic)
        {
            worldMayBeDirty = false;
            if (_sceneBinding == null)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (_sceneComponent == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Actor Context component was destroyed after Host startup.");
                return false;
            }

            if (_isDisposed || _sceneCaptureActive || token == 0UL ||
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
            _sceneCaptureActive = true;
            _sceneWriteFault = false;
            for (int index = 0; index < _sceneWritten.Length; index++)
            {
                _sceneWritten[index] = false;
            }

            worldMayBeDirty = true;
            bool captured;
            try
            {
                var context = new HostContextCaptureContext(
                    _graphInstanceId,
                    tickFrame,
                    previous,
                    new HostContextWriter(
                        _sceneBinding,
                        _sceneDescriptor,
                        this,
                        token));
                captured = _sceneBinding.TryCapture(context, out diagnostic);
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
                _sceneCaptureActive = false;
            }

            bool succeeded = captured &&
                             !diagnostic.IsError &&
                             !_sceneWriteFault &&
                             DidWriteAllHostContextValues();
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
            StateActivationMemory candidateMemory,
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
            !_isDisposed && token != 0UL && token == _activeToken && !_sceneCaptureActive;

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

        bool IHostContextValueSink.IsActive(
            ulong token,
            IHostContextBinding binding) =>
            !_isDisposed && _sceneCaptureActive && token != 0UL &&
            token == _activeToken && ReferenceEquals(binding, _sceneBinding);

        void IHostContextValueSink.RejectWrite(
            ulong token,
            IHostContextBinding binding)
        {
            if (_sceneCaptureActive)
            {
                _sceneWriteFault = true;
            }
        }

        bool IHostContextValueSink.TryWrite<TValue>(
            ulong token,
            IHostContextBinding binding,
            StateSlotId slotId,
            in TValue value)
        {
            if (!((IHostContextValueSink)this).IsActive(token, binding) ||
                !_layout.TryResolveSlot(slotId, out StateSlot<TValue> slot))
            {
                return false;
            }

            for (int index = 0; index < _sceneValues.Length; index++)
            {
                HostContextValueBinding actorValue = _sceneValues[index];
                if (actorValue.SlotId == slotId && actorValue.ValueType == typeof(TValue) &&
                    !_sceneWritten[index] &&
                    _prepared.TryGetWriter(actorValue.Block, out ContextFrameWriter writer) &&
                    writer.Write(slot, value))
                {
                    _sceneWritten[index] = true;
                    return true;
                }
            }

            return false;
        }

        private bool DidWriteAllHostContextValues()
        {
            for (int index = 0; index < _sceneWritten.Length; index++)
            {
                if (!_sceneWritten[index])
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
            _sceneCaptureActive = false;
            _sceneWriteFault = false;
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

        private static bool TryResolveHostContext(
            HostComponentBoundary boundary,
            ContextFrameLayout layout,
            MonoBehaviour component,
            out IHostContextBinding actorBinding,
            out HostContextBindingDescriptor descriptor,
            out HostContextValueBinding[] values,
            out CoCoDiagnostic diagnostic)
        {
            var required = new List<StateSlotDescriptor>();
            for (int blockIndex = 0; blockIndex < layout.Blocks.Count; blockIndex++)
            {
                StateBlockDescriptor block = layout.Blocks[blockIndex];
                if (block.Owner != StateBlockOwner.Host)
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
            values = Array.Empty<HostContextValueBinding>();
            if (required.Count == 0)
            {
                if (!ReferenceEquals(component, null))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidHostConfiguration,
                        "Actor Context component must be empty when the Layout has no direct Actor-owned Slot.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (component == null || !(component is IHostContextBinding typed) ||
                !boundary.Contains(component))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidHostConfiguration,
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
                    CoCoDiagnosticCode.InvalidHostConfiguration,
                    "Actor Context descriptor getter threw during preflight.");
                return false;
            }

            if (descriptor == null || !descriptor.IsValid ||
                descriptor.BindingType != component.GetType() ||
                descriptor.ValueCount != required.Count)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidHostConfiguration,
                    "Actor Context descriptor must exactly match its component and every Actor-owned Slot.");
                return false;
            }

            values = new HostContextValueBinding[required.Count];
            var seen = new HashSet<StateSlotId>();
            for (int index = 0; index < descriptor.Values.Count; index++)
            {
                HostContextValueRequirement declared = descriptor.Values[index];
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

                if (slot == null || block == null || block.Owner != StateBlockOwner.Host ||
                    slot.RestorePolicy == ContextRestorePolicy.Derived ||
                    slot.ValueType != declared.ValueType || !seen.Add(declared.SlotId) ||
                    !layout.TryResolveBlock(block.BlockId, out StateBlockHandle blockHandle))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidHostConfiguration,
                        "Actor Context descriptor contains a missing, duplicate, derived, or type-mismatched Slot.");
                    return false;
                }

                values[index] = new HostContextValueBinding(
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

        /// <summary>记录场景组件被授权写入的槽、类型和块。</summary>
        private readonly struct HostContextValueBinding
        {
            internal HostContextValueBinding(
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

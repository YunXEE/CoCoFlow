using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

// 按 Inspector 的次序调用获得全部所需资源的 Operator，并核对每个回调的结果与实际写入。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>执行场景 Operator，在回调期间授权 Outcome 与事件写入。</summary>
    internal sealed class HostOperatorRunner : IOperatorOutcomeSink, IEventOutboxSink, IDisposable
    {
        private readonly OperatorBinding[] _operators;
        private readonly GraphInstanceId _graphInstanceId;
        private readonly ContextFrameLayout _contextLayout;
        private readonly HostOperatorClaims _claims;
        private HostEventOutbox _outbox;
        private PreparedContextCommit _preparedContext;
        private ulong _activeToken;
        private int _activeOperatorIndex = -1;
        private int _currentOutcomeWriteCount;
        private bool _outcomeWriteFault;
        private bool _worldMayBeDirty;
        private bool _isDisposed;

        private HostOperatorRunner(GraphInstanceId instanceId, ContextFrameLayout layout,
            OperatorBinding[] operators, HostOperatorClaims claims)
        {
            _graphInstanceId = instanceId;
            _contextLayout = layout;
            _operators = operators;
            _claims = claims;
        }

        internal void BeginStep(HostEventOutbox outbox) => _outbox = outbox;

        internal int Count => _operators.Length;
        internal bool WorldMayBeDirty => _worldMayBeDirty;

        internal bool TryCreateOutboxLanes(
            int ledgerCapacity,
            out IEventOutboxLane[] lanes,
            out CoCoDiagnostic diagnostic)
        {
            var requirements = new List<EventOutboxRequirement>();
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                IReadOnlyList<EventOutboxRequirement> emits =
                    _operators[operatorIndex].Descriptor.Emits;
                for (int emitIndex = 0; emitIndex < emits.Count; emitIndex++)
                {
                    EventOutboxRequirement requirement = emits[emitIndex];
                    int existingIndex = -1;
                    for (int laneIndex = 0; laneIndex < requirements.Count; laneIndex++)
                    {
                        EventOutboxRequirement existing = requirements[laneIndex];
                        if (existing.EventTypeId != requirement.EventTypeId)
                        {
                            continue;
                        }

                        if (existing != requirement)
                        {
                            lanes = null;
                            diagnostic = Error(
                                CoCoDiagnosticCode.InvalidOperatorDescriptor,
                                "One EventType cannot use conflicting Outbox domain, payload, or capacity declarations.");
                            return false;
                        }

                        existingIndex = laneIndex;
                        break;
                    }

                    if (existingIndex < 0)
                    {
                        requirements.Add(requirement);
                    }
                }
            }

            int requiredLedgerCapacity = 0;
            for (int index = 0; index < requirements.Count; index++)
            {
                int laneCapacity = requirements[index].Capacity;
                if (laneCapacity > int.MaxValue - requiredLedgerCapacity)
                {
                    lanes = null;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.EventOutbox,
                        CoCoDiagnosticCode.EventOutboxOverflow,
                        "Declared EventOutbox lane capacities overflow the Host ledger limit.");
                    return false;
                }

                requiredLedgerCapacity += laneCapacity;
            }

            if (ledgerCapacity < requiredLedgerCapacity)
            {
                lanes = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.EventOutbox,
                    CoCoDiagnosticCode.EventOutboxOverflow,
                    "Host EventOutbox capacity must reserve every declared typed lane before callbacks begin.");
                return false;
            }

            lanes = new IEventOutboxLane[requirements.Count];
            for (int index = 0; index < lanes.Length; index++)
            {
                lanes[index] = requirements[index].CreateLane();
                if (lanes[index] == null)
                {
                    lanes = null;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.EventOutbox,
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "An AOT EventOutbox lane factory could not materialize its declared payload.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryCreate(
            HostComponentBoundary boundary,
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            ContextFrameLayout contextLayout,
            OperationSectionRegistry operationRegistry,
            IReadOnlyList<MonoBehaviour> components,
            IReadOnlyList<StateSlotId> claimStateSlots,
            out HostOperatorRunner runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            if (!boundary.HasOwner || graph == null || !graphInstanceId.IsValid ||
                contextLayout == null || operationRegistry == null || components == null ||
                claimStateSlots == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperatorDescriptor,
                    "Operator setup requires one Host, compiled Graph, Context layout, and explicit component list.");
                return false;
            }

            var bindings = new OperatorBinding[components.Count];
            var operatorIds = new HashSet<OperatorId>();
            var componentIds = new HashSet<EntityId>();
            var ownedOutcomes = new HashSet<StateSlotId>();
            var claimsBuilder = new HostOperatorClaims.Builder(contextLayout, operationRegistry);
            for (int operatorIndex = 0; operatorIndex < components.Count; operatorIndex++)
            {
                MonoBehaviour component = components[operatorIndex];
                if (component == null ||
                    !componentIds.Add(GetObjectEntityId(component)) ||
                    !(component is IOperator executable) ||
                    !boundary.Contains(component))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "Every Host Operator entry must be one unique live component inside that Host boundary.");
                    return false;
                }

                OperatorDescriptor descriptor;
                try
                {
                    descriptor = executable.Descriptor;
                }
                catch (Exception)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "An Operator descriptor getter threw during Host setup.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;

                if (descriptor == null ||
                    !descriptor.IsValid ||
                    descriptor.OperatorType != component.GetType() ||
                    !operatorIds.Add(descriptor.OperatorId) ||
                    !TryValidateRequirements(graph, descriptor, out diagnostic) ||
                    !TryResolveOutcomes(
                        contextLayout,
                        descriptor,
                        ownedOutcomes,
                        out OutcomeBinding[] outcomes,
                        out diagnostic) ||
                    !claimsBuilder.TryAdd(descriptor, out diagnostic))
                {
                    if (diagnostic.IsNone)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidOperatorDescriptor,
                            "Operator descriptors and ids must be immutable, exact, and unique per Host.");
                    }

                    return false;
                }

                bindings[operatorIndex] = new OperatorBinding(
                    component,
                    executable,
                    descriptor,
                    outcomes);
            }

            if (!TryValidateCoverage(graph, bindings, out diagnostic) ||
                !TryValidateOutcomeCoverage(contextLayout, ownedOutcomes, out diagnostic) ||
                !claimsBuilder.TryFreeze(claimStateSlots, out HostOperatorClaims claims, out diagnostic))
            {
                return false;
            }

            runtime = new HostOperatorRunner(graphInstanceId, contextLayout, bindings, claims);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryExecute(
            in StagedGraphStep stagedStep,
            in ContextFrameReadView previousContext,
            in PreparedContextCommit preparedContext,
            ulong token,
            IStateGraphCommitGuard commitGuard,
            StateFlowTraceBuffer trace,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                _activeToken != 0UL ||
                token == 0UL ||
                !stagedStep.IsValid ||
                !previousContext.IsValid ||
                !preparedContext.IsValid ||
                _outbox == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.OperatorExecutionFailed,
                    "Operator execution requires one fresh staged transaction.");
                return false;
            }

            _activeToken = token;
            _preparedContext = preparedContext;
            _worldMayBeDirty = false;
            _outcomeWriteFault = false;
            StateFlowTraceFrameReference previousReference = default;
            if (trace != null &&
                !StateFlowTraceFrameReference.TryCreate(
                    previousContext,
                    out previousReference))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.OperatorExecutionFailed,
                    "Operator trace could not snapshot the previous Context identity.");
                ClearActiveTransaction();
                return false;
            }

            if (!_claims.TryStage(stagedStep.FinalizedOperationFrame, preparedContext, out diagnostic))
            {
                ClearActiveTransaction();
                return false;
            }

            // Materialize every normal loser before the first real callback. A later
            // callback failure must not prevent ClaimDenied from becoming observable.
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                if (_claims.IsEligible(operatorIndex))
                {
                    continue;
                }

                OperatorOutcome denied = OperatorOutcome.Denied(Error(
                    CoCoDiagnosticCode.OperatorClaimConflict,
                    "Operator execution was skipped because it did not win all requested Claims."));
                trace?.Append(StateFlowTraceEntry.Operator(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    binding.Descriptor.OperatorId,
                    denied.Status,
                    previousReference));
            }

            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                if (!_claims.IsEligible(operatorIndex))
                {
                    continue;
                }

                if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                {
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled Operator execution before the next callback.");
                    ClearActiveTransaction();
                    return false;
                }

                OperatorOutcome outcome;

                if (binding.Component == null)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorExecutionFailed,
                        "An explicit Operator component was destroyed after Host startup.");
                    ClearActiveTransaction();
                    return false;
                }

                _activeOperatorIndex = operatorIndex;
                _currentOutcomeWriteCount = 0;
                int outboxCountBefore = _outbox.Count;
                _worldMayBeDirty = true;
                bool executed;
                try
                {
                    var context = new OperatorExecutionContext(
                        binding.Descriptor,
                        stagedStep.TickFrame,
                        previousContext,
                        stagedStep.FinalizedOperationFrame,
                        this,
                        new EventOutboxWriter(binding.Descriptor, this, token),
                        token);
                    executed = binding.Operator.TryExecute(context, out outcome);
                }
                catch (Exception)
                {
                    executed = false;
                    outcome = default;
                }
                finally
                {
                    _activeOperatorIndex = -1;
                }

                int eventWrites = _outbox.Count - outboxCountBefore;
                if (!executed ||
                    !outcome.IsValid ||
                    _outcomeWriteFault ||
                    _outbox.HasWriteFault ||
                    outcome.Status == OperatorOutcomeStatus.ClaimDenied ||
                    ((outcome.Status == OperatorOutcomeStatus.NoOp ||
                      outcome.Status == OperatorOutcomeStatus.Rejected) &&
                     (_currentOutcomeWriteCount != 0 || eventWrites != 0)))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorExecutionFailed,
                        "Operator execution failed, returned an invalid Outcome, or left writes behind for a non-writing Outcome.");
                    ClearActiveTransaction();
                    return false;
                }

                trace?.Append(StateFlowTraceEntry.Operator(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    binding.Descriptor.OperatorId,
                    outcome.Status,
                    previousReference));
                if (outcome.Status == OperatorOutcomeStatus.Rejected)
                {
                    diagnostic = outcome.Diagnostic.IsError
                        ? outcome.Diagnostic
                        : Error(
                            CoCoDiagnosticCode.OperatorExecutionFailed,
                            "Operator rejected the candidate transaction.");
                    ClearActiveTransaction();
                    return false;
                }

                if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                {
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled Operator execution after a callback.");
                    ClearActiveTransaction();
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool IsOperatorActive(ulong token, OperatorId operatorId)
        {
            return !_isDisposed &&
                   token != 0UL &&
                   token == _activeToken &&
                   _activeOperatorIndex >= 0 &&
                   _activeOperatorIndex < _operators.Length &&
                   _operators[_activeOperatorIndex].Descriptor.OperatorId == operatorId;
        }

        bool IOperatorOutcomeSink.IsActive(ulong token, OperatorId operatorId) =>
            IsOperatorActive(token, operatorId);

        void IOperatorOutcomeSink.RejectWrite(
            ulong token,
            OperatorId operatorId)
        {
            if (_activeToken != 0UL)
            {
                _outcomeWriteFault = true;
            }
        }

        bool IOperatorOutcomeSink.TryWrite<TValue>(
            ulong token,
            OperatorId operatorId,
            StateSlotId slotId,
            in TValue value)
        {
            if (!IsOperatorActive(token, operatorId) ||
                !_contextLayout.TryResolveSlot(
                    slotId,
                    out StateSlot<TValue> slot))
            {
                return false;
            }

            OutcomeBinding[] outcomes = _operators[_activeOperatorIndex].Outcomes;
            for (int index = 0; index < outcomes.Length; index++)
            {
                OutcomeBinding outcome = outcomes[index];
                if (outcome.SlotId != slotId || outcome.ValueType != typeof(TValue) ||
                    !_preparedContext.TryGetWriter(
                        outcome.Block,
                        out ContextFrameWriter writer) ||
                    !writer.Write(slot, value))
                {
                    continue;
                }

                _currentOutcomeWriteCount++;
                return true;
            }

            return false;
        }

        bool IEventOutboxSink.IsActive(ulong token, OperatorId operatorId) =>
            IsOperatorActive(token, operatorId);

        void IEventOutboxSink.RejectWrite(ulong token, OperatorId operatorId) => _outbox?.RejectWrite();

        EventOutboxWriteResult IEventOutboxSink.TryWrite<TEvent>(ulong token, OperatorId operatorId,
            EventOutboxRequirement requirement, EventOutboxTarget target, in TEvent payload) =>
            IsOperatorActive(token, operatorId)
                ? _outbox.TryWrite(requirement, target, payload)
                : EventOutboxWriteResult.InvalidWriter;

        internal void CommitClaimsNoFail()
        {
            _claims.CommitClaimsNoFail();
            ClearActiveTransaction();
        }

        internal void Cancel()
        {
            _claims.Cancel();
            ClearActiveTransaction();
        }

        internal HostOperatorClaims Claims => _claims;

        public void Dispose()
        {
            _claims.Dispose();
            ClearActiveTransaction();
            _isDisposed = true;
        }

        private void ClearActiveTransaction()
        {
            _outbox = null;
            _preparedContext = default;
            _activeToken = 0UL;
            _activeOperatorIndex = -1;
            _currentOutcomeWriteCount = 0;
            _outcomeWriteFault = false;
        }

        private static bool TryValidateRequirements(
            CompiledStateGraph graph,
            OperatorDescriptor descriptor,
            out CoCoDiagnostic diagnostic)
        {
            for (int requirementIndex = 0;
                 requirementIndex < descriptor.Requires.Count;
                 requirementIndex++)
            {
                OperationSectionRequirement requirement =
                    descriptor.Requires[requirementIndex];
                bool found = false;
                for (int provideIndex = 0;
                     provideIndex < graph.OperationProvides.Count;
                     provideIndex++)
                {
                    if (Matches(graph.OperationProvides.Provides[provideIndex], requirement))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.MissingOperatorBinding,
                        "Operator Requires must be an exact subset of the compiled Operation Provides manifest.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryValidateCoverage(
            CompiledStateGraph graph,
            OperatorBinding[] bindings,
            out CoCoDiagnostic diagnostic)
        {
            for (int provideIndex = 0;
                 provideIndex < graph.OperationProvides.Count;
                 provideIndex++)
            {
                GraphOperationProvision provided =
                    graph.OperationProvides.Provides[provideIndex];
                bool covered = false;
                for (int operatorIndex = 0; operatorIndex < bindings.Length && !covered; operatorIndex++)
                {
                    IReadOnlyList<OperationSectionRequirement> requires =
                        bindings[operatorIndex].Descriptor.Requires;
                    for (int requirementIndex = 0;
                         requirementIndex < requires.Count;
                         requirementIndex++)
                    {
                        if (Matches(provided, requires[requirementIndex]))
                        {
                            covered = true;
                            break;
                        }
                    }
                }

                if (!covered)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.MissingOperatorBinding,
                        "Explicit Operators must cover every compiled Operation Provides Section.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryResolveOutcomes(
            ContextFrameLayout layout,
            OperatorDescriptor descriptor,
            HashSet<StateSlotId> owned,
            out OutcomeBinding[] outcomes,
            out CoCoDiagnostic diagnostic)
        {
            outcomes = new OutcomeBinding[descriptor.OutcomeCount];
            for (int outcomeIndex = 0; outcomeIndex < descriptor.OutcomeCount; outcomeIndex++)
            {
                OperatorOutcomeRequirement outcome =
                    descriptor.OutcomeRequirements[outcomeIndex];
                StateSlotDescriptor slot = FindSlot(layout, outcome.SlotId);
                StateBlockDescriptor block = slot == null
                    ? null
                    : FindBlock(layout, slot.WriterBlockId);
                if (slot == null ||
                    block == null ||
                    slot.ValueType != outcome.ValueType ||
                    slot.RestorePolicy == ContextRestorePolicy.Derived ||
                    block.Owner != StateBlockOwner.Operator ||
                    !owned.Add(outcome.SlotId) ||
                    !layout.TryResolveBlock(block.BlockId, out StateBlockHandle blockHandle))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OutcomeOwnershipConflict,
                        "Every Operator Outcome must uniquely own one writable Operator StateSlot.");
                    return false;
                }

                outcomes[outcomeIndex] = new OutcomeBinding(
                    outcome.SlotId,
                    outcome.ValueType,
                    blockHandle);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryValidateOutcomeCoverage(
            ContextFrameLayout layout,
            HashSet<StateSlotId> owned,
            out CoCoDiagnostic diagnostic)
        {
            for (int blockIndex = 0; blockIndex < layout.Blocks.Count; blockIndex++)
            {
                StateBlockDescriptor block = layout.Blocks[blockIndex];
                if (block.Owner != StateBlockOwner.Operator)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    StateSlotDescriptor slot = block.Slots[slotIndex];
                    if (slot.RestorePolicy != ContextRestorePolicy.Derived &&
                        !owned.Contains(slot.SlotId))
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.OutcomeOwnershipConflict,
                            "Every writable Operator StateSlot requires exactly one explicit Outcome owner.");
                        return false;
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool Matches(
            GraphOperationProvision provided,
            OperationSectionRequirement required)
        {
            return required.IsValid &&
                   provided.SectionId == required.SectionId &&
                   provided.Mode == required.Mode &&
                   provided.SectionType == required.SectionType &&
                   provided.Shape != null &&
                   provided.Shape.Equals(required.Shape);
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

        private static StateBlockDescriptor FindBlock(
            ContextFrameLayout layout,
            StateBlockId blockId)
        {
            for (int index = 0; index < layout.Blocks.Count; index++)
            {
                if (layout.Blocks[index].BlockId == blockId)
                {
                    return layout.Blocks[index];
                }
            }

            return null;
        }

        private static EntityId GetObjectEntityId(UnityEngine.Object value)
        {
#if UNITY_6000_5_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operator, code, message);

        private static CoCoDiagnostic LifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);

        /// <summary>保存一个已核验组件及它可以写入的结果槽。</summary>
        private sealed class OperatorBinding
        {
            public OperatorBinding(
                MonoBehaviour component,
                IOperator executable,
                OperatorDescriptor descriptor,
                OutcomeBinding[] outcomes)
            {
                Component = component;
                Operator = executable;
                Descriptor = descriptor;
                Outcomes = outcomes;
            }

            public MonoBehaviour Component { get; }
            public IOperator Operator { get; }
            public OperatorDescriptor Descriptor { get; }
            public OutcomeBinding[] Outcomes { get; }
        }

        /// <summary>定位 Operator 拥有的结果值类型、槽和写入块。</summary>
        private readonly struct OutcomeBinding
        {
            public OutcomeBinding(
                StateSlotId slotId,
                Type valueType,
                StateBlockHandle block)
            {
                SlotId = slotId;
                ValueType = valueType;
                Block = block;
            }

            public StateSlotId SlotId { get; }
            public Type ValueType { get; }
            public StateBlockHandle Block { get; }
        }

    }
}

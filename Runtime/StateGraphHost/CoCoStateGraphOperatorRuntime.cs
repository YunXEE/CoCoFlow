using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    internal sealed class CoCoStateGraphOperatorRuntime : ICoCoOperatorOutcomeSink, IDisposable
    {
        private readonly OperatorBinding[] _operators;
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoContextFrameLayout _contextLayout;
        private readonly ClaimResource[] _claimResources;
        private readonly int[] _committedClaimOwners;
        private readonly CoCoActivationId[] _committedClaimActivations;
        private readonly int[] _candidateClaimOwners;
        private readonly CoCoActivationId[] _candidateClaimActivations;
        private readonly bool[] _retainedClaimOwners;
        private CoCoPreparedContextCommit _preparedContext;
        private ulong _activeToken;
        private int _activeOperatorIndex = -1;
        private int _currentOutcomeWriteCount;
        private bool _outcomeWriteFault;
        private bool _worldMayBeDirty;
        private bool _isDisposed;

        private CoCoStateGraphOperatorRuntime(
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout contextLayout,
            OperatorBinding[] operators,
            ClaimResource[] claimResources)
        {
            _graphInstanceId = graphInstanceId;
            _contextLayout = contextLayout;
            _operators = operators;
            _claimResources = claimResources;
            _committedClaimOwners = new int[claimResources.Length];
            _committedClaimActivations = new CoCoActivationId[claimResources.Length];
            _candidateClaimOwners = new int[claimResources.Length];
            _candidateClaimActivations = new CoCoActivationId[claimResources.Length];
            _retainedClaimOwners = new bool[operators.Length];
            ClearOwners(_committedClaimOwners);
            ClearOwners(_candidateClaimOwners);
        }

        internal int Count => _operators.Length;
        internal bool WorldMayBeDirty => _worldMayBeDirty;

        internal bool TryCreateOutboxLanes(
            int ledgerCapacity,
            out ICoCoEventOutboxLane[] lanes,
            out CoCoDiagnostic diagnostic)
        {
            var requirements = new List<CoCoEventOutboxRequirement>();
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                IReadOnlyList<CoCoEventOutboxRequirement> emits =
                    _operators[operatorIndex].Descriptor.Emits;
                for (int emitIndex = 0; emitIndex < emits.Count; emitIndex++)
                {
                    CoCoEventOutboxRequirement requirement = emits[emitIndex];
                    int existingIndex = -1;
                    for (int laneIndex = 0; laneIndex < requirements.Count; laneIndex++)
                    {
                        CoCoEventOutboxRequirement existing = requirements[laneIndex];
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

            lanes = new ICoCoEventOutboxLane[requirements.Count];
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
            CoCoStateGraphHost host,
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout contextLayout,
            CoCoOperationSectionRegistry operationRegistry,
            IReadOnlyList<MonoBehaviour> components,
            out CoCoStateGraphOperatorRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            if (host == null || graph == null || !graphInstanceId.IsValid ||
                contextLayout == null || operationRegistry == null || components == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperatorDescriptor,
                    "Operator setup requires one Host, compiled Graph, Context layout, and explicit component list.");
                return false;
            }

            var bindings = new OperatorBinding[components.Count];
            var operatorIds = new HashSet<CoCoOperatorId>();
            var componentIds = new HashSet<int>();
            var ownedOutcomes = new HashSet<CoCoStateSlotId>();
            var resources = new List<ClaimResourceBuilder>();
            var sectionClaims = new Dictionary<CoCoOperationSectionId, CoCoOperatorClaimId>();
            for (int operatorIndex = 0; operatorIndex < components.Count; operatorIndex++)
            {
                MonoBehaviour component = components[operatorIndex];
                if (component == null ||
                    !componentIds.Add(component.GetInstanceID()) ||
                    !(component is ICoCoOperator executable) ||
                    !IsInsideHostBoundary(host, component))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "Every Host Operator entry must be one unique live component inside that Host boundary.");
                    return false;
                }

                CoCoOperatorDescriptor descriptor;
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
                    !TryResolveClaims(
                        descriptor,
                        operatorIndex,
                        operationRegistry,
                        resources,
                        sectionClaims,
                        out int[] claimResourceIndices,
                        out int[] claimSectionIndices,
                        out diagnostic))
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
                    outcomes,
                    claimResourceIndices,
                    claimSectionIndices);
            }

            if (!TryValidateCoverage(graph, bindings, out diagnostic) ||
                !TryValidateOutcomeCoverage(contextLayout, ownedOutcomes, out diagnostic))
            {
                return false;
            }

            var frozenResources = new ClaimResource[resources.Count];
            for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
            {
                frozenResources[resourceIndex] = resources[resourceIndex].Freeze();
            }

            runtime = new CoCoStateGraphOperatorRuntime(
                graphInstanceId,
                contextLayout,
                bindings,
                frozenResources);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryExecute(
            in CoCoStagedGraphStep stagedStep,
            in CoCoContextFrameReadView previousContext,
            in CoCoPreparedContextCommit preparedContext,
            ICoCoEventOutboxSink eventOutbox,
            ulong token,
            CoCoStateFlowTraceBuffer trace,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                _activeToken != 0UL ||
                token == 0UL ||
                !stagedStep.IsValid ||
                !previousContext.IsValid ||
                !preparedContext.IsValid ||
                eventOutbox == null)
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
            if (!TryArbitrateClaims(stagedStep.FinalizedOperationFrame, out diagnostic))
            {
                ClearActiveTransaction();
                return false;
            }

            // Materialize every normal loser before the first real callback. A later
            // callback failure must not prevent ClaimDenied from becoming observable.
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                if (binding.Eligible)
                {
                    continue;
                }

                CoCoOperatorOutcome denied = CoCoOperatorOutcome.Denied(Error(
                    CoCoDiagnosticCode.OperatorClaimConflict,
                    "Operator execution was skipped because it did not win all requested Claims."));
                trace?.Append(CoCoStateFlowTraceEntry.Operator(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    binding.Descriptor.OperatorId,
                    denied.Status));
            }

            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                if (!binding.Eligible)
                {
                    continue;
                }

                CoCoOperatorOutcome outcome;

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
                int outboxCountBefore = eventOutbox is CoCoStateGraphTransaction transaction
                    ? transaction.CandidateEventCount
                    : 0;
                _worldMayBeDirty = true;
                bool executed;
                try
                {
                    var context = new CoCoOperatorExecutionContext(
                        binding.Descriptor,
                        stagedStep.TickFrame,
                        previousContext,
                        stagedStep.FinalizedOperationFrame,
                        this,
                        new CoCoEventOutboxWriter(binding.Descriptor, eventOutbox, token),
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

                int eventWrites = eventOutbox is CoCoStateGraphTransaction outboxTransaction
                    ? outboxTransaction.CandidateEventCount - outboxCountBefore
                    : 0;
                if (!executed ||
                    !outcome.IsValid ||
                    _outcomeWriteFault ||
                    (eventOutbox is CoCoStateGraphTransaction failedOutbox &&
                     failedOutbox.HasOutboxFailure) ||
                    outcome.Status == CoCoOperatorOutcomeStatus.ClaimDenied ||
                    ((outcome.Status == CoCoOperatorOutcomeStatus.NoOp ||
                      outcome.Status == CoCoOperatorOutcomeStatus.Rejected) &&
                     (_currentOutcomeWriteCount != 0 || eventWrites != 0)))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorExecutionFailed,
                        "Operator execution failed, returned an invalid Outcome, or left writes behind for a non-writing Outcome.");
                    ClearActiveTransaction();
                    return false;
                }

                trace?.Append(CoCoStateFlowTraceEntry.Operator(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    binding.Descriptor.OperatorId,
                    outcome.Status));
                if (outcome.Status == CoCoOperatorOutcomeStatus.Rejected)
                {
                    diagnostic = outcome.Diagnostic.IsError
                        ? outcome.Diagnostic
                        : Error(
                            CoCoDiagnosticCode.OperatorExecutionFailed,
                            "Operator rejected the candidate transaction.");
                    ClearActiveTransaction();
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool IsOperatorActive(ulong token, CoCoOperatorId operatorId)
        {
            return !_isDisposed &&
                   token != 0UL &&
                   token == _activeToken &&
                   _activeOperatorIndex >= 0 &&
                   _activeOperatorIndex < _operators.Length &&
                   _operators[_activeOperatorIndex].Descriptor.OperatorId == operatorId;
        }

        bool ICoCoOperatorOutcomeSink.IsActive(ulong token, CoCoOperatorId operatorId) =>
            IsOperatorActive(token, operatorId);

        void ICoCoOperatorOutcomeSink.RejectWrite(
            ulong token,
            CoCoOperatorId operatorId)
        {
            if (_activeToken != 0UL)
            {
                _outcomeWriteFault = true;
            }
        }

        bool ICoCoOperatorOutcomeSink.TryWrite<TValue>(
            ulong token,
            CoCoOperatorId operatorId,
            CoCoStateSlotId slotId,
            in TValue value)
        {
            if (!IsOperatorActive(token, operatorId) ||
                !_contextLayout.TryResolveSlot(
                    slotId,
                    out CoCoStateSlot<TValue> slot))
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
                        out CoCoContextFrameWriter writer) ||
                    !writer.Write(slot, value))
                {
                    continue;
                }

                _currentOutcomeWriteCount++;
                return true;
            }

            return false;
        }

        internal void CommitClaimsNoFail()
        {
            for (int index = 0; index < _claimResources.Length; index++)
            {
                _committedClaimOwners[index] = _candidateClaimOwners[index];
                _committedClaimActivations[index] = _candidateClaimActivations[index];
            }

            ClearActiveTransaction();
        }

        internal void Cancel()
        {
            ClearOwners(_candidateClaimOwners);
            Array.Clear(_candidateClaimActivations, 0, _candidateClaimActivations.Length);
            ClearActiveTransaction();
        }

        internal void Suspend()
        {
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                bool ownsCommittedClaims = false;
                bool release = false;
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    if (_committedClaimOwners[resourceIndex] != operatorIndex)
                    {
                        continue;
                    }

                    ownsCommittedClaims = true;
                    if (binding.Descriptor.Claims[claimIndex].SuspendPolicy ==
                        CoCoOperatorClaimSuspendPolicy.Release)
                    {
                        release = true;
                    }
                }

                if (!ownsCommittedClaims || !release)
                {
                    continue;
                }

                // Claim ownership is all-or-none per Operator. A Release policy on
                // any member therefore releases the complete committed Claim set.
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    _committedClaimOwners[resourceIndex] = -1;
                    _committedClaimActivations[resourceIndex] = default;
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Cancel();
            ClearOwners(_committedClaimOwners);
            Array.Clear(_committedClaimActivations, 0, _committedClaimActivations.Length);
            _isDisposed = true;
        }

        private bool TryArbitrateClaims(
            in CoCoFinalizedOperationFrame operationFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateCommittedClaims(out diagnostic))
            {
                return false;
            }

            Array.Clear(_retainedClaimOwners, 0, _retainedClaimOwners.Length);
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                binding.Eligible = true;
                for (int claimIndex = 0; claimIndex < binding.Descriptor.Claims.Count; claimIndex++)
                {
                    CoCoOperatorClaimRequirement claim = binding.Descriptor.Claims[claimIndex];
                    if (!operationFrame.TryGetHeader(
                            binding.ClaimSectionIndices[claimIndex],
                            out CoCoOperationSectionEntryHeader header))
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidOperatorDescriptor,
                            "Claim arbitration could not resolve its finalized Operation Section.");
                        return false;
                    }

                    binding.ClaimActivations[claimIndex] = header.ActivationId;
                    if (!header.Enabled || !header.ActivationId.IsValid)
                    {
                        binding.Eligible = false;
                    }
                }
            }

            // Preserve a committed owner while every member of its all-or-none
            // Claim set still addresses the same enabled Activation. Any changed
            // or exited Activation releases the complete set before arbitration.
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                if (binding.Descriptor.Claims.Count == 0 ||
                    _committedClaimOwners[binding.ClaimResourceIndices[0]] != operatorIndex)
                {
                    continue;
                }

                bool retain = binding.Eligible;
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count && retain;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    retain = _committedClaimOwners[resourceIndex] == operatorIndex &&
                             _committedClaimActivations[resourceIndex] ==
                             binding.ClaimActivations[claimIndex];
                }

                _retainedClaimOwners[operatorIndex] = retain;
            }

            bool changed;
            do
            {
                ClearOwners(_candidateClaimOwners);
                Array.Clear(_candidateClaimActivations, 0, _candidateClaimActivations.Length);
                for (int operatorIndex = 0;
                     operatorIndex < _operators.Length;
                     operatorIndex++)
                {
                    if (!_retainedClaimOwners[operatorIndex])
                    {
                        continue;
                    }

                    OperatorBinding retained = _operators[operatorIndex];
                    for (int claimIndex = 0;
                         claimIndex < retained.Descriptor.Claims.Count;
                         claimIndex++)
                    {
                        int resourceIndex = retained.ClaimResourceIndices[claimIndex];
                        _candidateClaimOwners[resourceIndex] = operatorIndex;
                        _candidateClaimActivations[resourceIndex] =
                            retained.ClaimActivations[claimIndex];
                    }
                }

                for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
                {
                    OperatorBinding binding = _operators[operatorIndex];
                    if (!binding.Eligible || _retainedClaimOwners[operatorIndex])
                    {
                        continue;
                    }

                    for (int claimIndex = 0;
                         claimIndex < binding.Descriptor.Claims.Count;
                         claimIndex++)
                    {
                        int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                        int current = _candidateClaimOwners[resourceIndex];
                        if (current < 0 ||
                            (!_retainedClaimOwners[current] && IsHigherPriority(
                                operatorIndex,
                                claimIndex,
                                current,
                                FindClaimIndex(current, resourceIndex))))
                        {
                            _candidateClaimOwners[resourceIndex] = operatorIndex;
                            _candidateClaimActivations[resourceIndex] =
                                binding.ClaimActivations[claimIndex];
                        }
                    }
                }

                changed = false;
                for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
                {
                    OperatorBinding binding = _operators[operatorIndex];
                    if (!binding.Eligible || binding.Descriptor.Claims.Count == 0)
                    {
                        continue;
                    }

                    for (int claimIndex = 0;
                         claimIndex < binding.Descriptor.Claims.Count;
                         claimIndex++)
                    {
                        if (_candidateClaimOwners[binding.ClaimResourceIndices[claimIndex]] ==
                            operatorIndex)
                        {
                            continue;
                        }

                        binding.Eligible = false;
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateCommittedClaims(out CoCoDiagnostic diagnostic)
        {
            for (int resourceIndex = 0;
                 resourceIndex < _claimResources.Length;
                 resourceIndex++)
            {
                int owner = _committedClaimOwners[resourceIndex];
                CoCoActivationId activation = _committedClaimActivations[resourceIndex];
                if (owner < 0)
                {
                    if (activation.IsValid)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.OperatorClaimConflict,
                            "A committed Claim resource has an Activation without an owner.");
                        return false;
                    }

                    continue;
                }

                if (owner >= _operators.Length ||
                    !activation.IsValid ||
                    FindClaimIndex(owner, resourceIndex) < 0)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "Committed Claim ownership or Activation identity is inconsistent.");
                    return false;
                }
            }

            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                bool ownsAny = false;
                bool ownsAll = true;
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count;
                     claimIndex++)
                {
                    int owner = _committedClaimOwners[
                        binding.ClaimResourceIndices[claimIndex]];
                    ownsAny |= owner == operatorIndex;
                    ownsAll &= owner == operatorIndex;
                }

                if (ownsAny && !ownsAll)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "A multi-Claim Operator has partial committed ownership.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool IsHigherPriority(
            int candidateOperator,
            int candidateClaim,
            int currentOperator,
            int currentClaim)
        {
            CoCoOperatorClaimRequirement candidate =
                _operators[candidateOperator].Descriptor.Claims[candidateClaim];
            CoCoOperatorClaimRequirement current =
                _operators[currentOperator].Descriptor.Claims[currentClaim];
            if (candidate.Priority != current.Priority)
            {
                return candidate.Priority > current.Priority;
            }

            if (candidateOperator != currentOperator)
            {
                return candidateOperator < currentOperator;
            }

            CoCoOperatorId candidateId = _operators[candidateOperator].Descriptor.OperatorId;
            CoCoOperatorId currentId = _operators[currentOperator].Descriptor.OperatorId;
            return candidateId.High < currentId.High ||
                   (candidateId.High == currentId.High && candidateId.Low < currentId.Low);
        }

        private int FindClaimIndex(int operatorIndex, int resourceIndex)
        {
            int[] indices = _operators[operatorIndex].ClaimResourceIndices;
            for (int index = 0; index < indices.Length; index++)
            {
                if (indices[index] == resourceIndex)
                {
                    return index;
                }
            }

            return -1;
        }

        private CoCoOperatorClaimRequirement FindClaim(int operatorIndex, int resourceIndex)
        {
            int claimIndex = FindClaimIndex(operatorIndex, resourceIndex);
            return claimIndex >= 0
                ? _operators[operatorIndex].Descriptor.Claims[claimIndex]
                : default;
        }

        private void ClearActiveTransaction()
        {
            _preparedContext = default;
            _activeToken = 0UL;
            _activeOperatorIndex = -1;
            _currentOutcomeWriteCount = 0;
            _outcomeWriteFault = false;
        }

        private static bool TryValidateRequirements(
            CoCoCompiledStateGraph graph,
            CoCoOperatorDescriptor descriptor,
            out CoCoDiagnostic diagnostic)
        {
            for (int requirementIndex = 0;
                 requirementIndex < descriptor.Requires.Count;
                 requirementIndex++)
            {
                CoCoOperationSectionRequirement requirement =
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
            CoCoCompiledStateGraph graph,
            OperatorBinding[] bindings,
            out CoCoDiagnostic diagnostic)
        {
            for (int provideIndex = 0;
                 provideIndex < graph.OperationProvides.Count;
                 provideIndex++)
            {
                CoCoGraphOperationProvideRequirement provided =
                    graph.OperationProvides.Provides[provideIndex];
                bool covered = false;
                for (int operatorIndex = 0; operatorIndex < bindings.Length && !covered; operatorIndex++)
                {
                    IReadOnlyList<CoCoOperationSectionRequirement> requires =
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
            CoCoContextFrameLayout layout,
            CoCoOperatorDescriptor descriptor,
            HashSet<CoCoStateSlotId> owned,
            out OutcomeBinding[] outcomes,
            out CoCoDiagnostic diagnostic)
        {
            outcomes = new OutcomeBinding[descriptor.OutcomeCount];
            for (int outcomeIndex = 0; outcomeIndex < descriptor.OutcomeCount; outcomeIndex++)
            {
                CoCoOperatorOutcomeRequirement outcome =
                    descriptor.OutcomeRequirements[outcomeIndex];
                CoCoStateSlotDescriptor slot = FindSlot(layout, outcome.SlotId);
                CoCoStateBlockDescriptor block = slot == null
                    ? null
                    : FindBlock(layout, slot.WriterBlockId);
                if (slot == null ||
                    block == null ||
                    slot.ValueType != outcome.ValueType ||
                    slot.RestorePolicy == CoCoContextRestorePolicy.Derived ||
                    block.Owner != CoCoStateBlockOwner.Operator ||
                    !owned.Add(outcome.SlotId) ||
                    !layout.TryResolveBlock(block.BlockId, out CoCoStateBlockHandle blockHandle))
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
            CoCoContextFrameLayout layout,
            HashSet<CoCoStateSlotId> owned,
            out CoCoDiagnostic diagnostic)
        {
            for (int blockIndex = 0; blockIndex < layout.Blocks.Count; blockIndex++)
            {
                CoCoStateBlockDescriptor block = layout.Blocks[blockIndex];
                if (block.Owner != CoCoStateBlockOwner.Operator)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    CoCoStateSlotDescriptor slot = block.Slots[slotIndex];
                    if (slot.RestorePolicy != CoCoContextRestorePolicy.Derived &&
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

        private static bool TryResolveClaims(
            CoCoOperatorDescriptor descriptor,
            int operatorIndex,
            CoCoOperationSectionRegistry operationRegistry,
            List<ClaimResourceBuilder> resources,
            Dictionary<CoCoOperationSectionId, CoCoOperatorClaimId> sectionClaims,
            out int[] resourceIndices,
            out int[] sectionIndices,
            out CoCoDiagnostic diagnostic)
        {
            resourceIndices = new int[descriptor.Claims.Count];
            sectionIndices = new int[descriptor.Claims.Count];
            for (int claimIndex = 0; claimIndex < descriptor.Claims.Count; claimIndex++)
            {
                CoCoOperatorClaimRequirement claim = descriptor.Claims[claimIndex];
                if (!operationRegistry.TryResolveDenseIndex(
                        claim.Section,
                        out sectionIndices[claimIndex]))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "Claim Section could not resolve against the frozen Operation registry.");
                    return false;
                }

                if (sectionClaims.TryGetValue(
                        claim.Section.SectionId,
                        out CoCoOperatorClaimId existingClaimId) &&
                    existingClaimId != claim.ClaimId)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "One Operation Section cannot be arbitrated under multiple Claim identities.");
                    return false;
                }

                sectionClaims[claim.Section.SectionId] = claim.ClaimId;
                int resourceIndex = -1;
                for (int index = 0; index < resources.Count; index++)
                {
                    if (resources[index].ClaimId != claim.ClaimId)
                    {
                        continue;
                    }

                    if (resources[index].Section != claim.Section)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.OperatorClaimConflict,
                            "One Claim identity cannot map to conflicting Operation Sections.");
                        return false;
                    }

                    resourceIndex = index;
                    break;
                }

                if (resourceIndex < 0)
                {
                    resourceIndex = resources.Count;
                    resources.Add(new ClaimResourceBuilder(claim.ClaimId, claim.Section));
                }

                resources[resourceIndex].Add(operatorIndex);
                resourceIndices[claimIndex] = resourceIndex;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool Matches(
            CoCoGraphOperationProvideRequirement provided,
            CoCoOperationSectionRequirement required)
        {
            return required.IsValid &&
                   provided.SectionId == required.SectionId &&
                   provided.Mode == required.Mode &&
                   provided.SectionType == required.SectionType &&
                   provided.Shape != null &&
                   provided.Shape.Equals(required.Shape);
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

        private static CoCoStateBlockDescriptor FindBlock(
            CoCoContextFrameLayout layout,
            CoCoStateBlockId blockId)
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

        private static bool IsInsideHostBoundary(
            CoCoStateGraphHost host,
            MonoBehaviour component)
        {
            Transform current = component.transform;
            while (current != null)
            {
                CoCoStateGraphHost boundary = current.GetComponent<CoCoStateGraphHost>();
                if (boundary != null)
                {
                    return ReferenceEquals(boundary, host);
                }

                current = current.parent;
            }

            return false;
        }

        private static void ClearOwners(int[] owners)
        {
            for (int index = 0; index < owners.Length; index++)
            {
                owners[index] = -1;
            }
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operator, code, message);

        private sealed class OperatorBinding
        {
            public OperatorBinding(
                MonoBehaviour component,
                ICoCoOperator executable,
                CoCoOperatorDescriptor descriptor,
                OutcomeBinding[] outcomes,
                int[] claimResourceIndices,
                int[] claimSectionIndices)
            {
                Component = component;
                Operator = executable;
                Descriptor = descriptor;
                Outcomes = outcomes;
                ClaimResourceIndices = claimResourceIndices;
                ClaimSectionIndices = claimSectionIndices;
                ClaimActivations = new CoCoActivationId[claimResourceIndices.Length];
            }

            public MonoBehaviour Component { get; }
            public ICoCoOperator Operator { get; }
            public CoCoOperatorDescriptor Descriptor { get; }
            public OutcomeBinding[] Outcomes { get; }
            public int[] ClaimResourceIndices { get; }
            public int[] ClaimSectionIndices { get; }
            public CoCoActivationId[] ClaimActivations { get; }
            public bool Eligible { get; set; }
        }

        private readonly struct OutcomeBinding
        {
            public OutcomeBinding(
                CoCoStateSlotId slotId,
                Type valueType,
                CoCoStateBlockHandle block)
            {
                SlotId = slotId;
                ValueType = valueType;
                Block = block;
            }

            public CoCoStateSlotId SlotId { get; }
            public Type ValueType { get; }
            public CoCoStateBlockHandle Block { get; }
        }

        private readonly struct ClaimResource
        {
            public ClaimResource(
                CoCoOperatorClaimId claimId,
                CoCoOperationSectionRequirement section,
                int[] contenders)
            {
                ClaimId = claimId;
                Section = section;
                Contenders = contenders;
            }

            public CoCoOperatorClaimId ClaimId { get; }
            public CoCoOperationSectionRequirement Section { get; }
            public int[] Contenders { get; }
        }

        private sealed class ClaimResourceBuilder
        {
            private readonly List<int> _contenders = new List<int>();

            public ClaimResourceBuilder(
                CoCoOperatorClaimId claimId,
                CoCoOperationSectionRequirement section)
            {
                ClaimId = claimId;
                Section = section;
            }

            public CoCoOperatorClaimId ClaimId { get; }
            public CoCoOperationSectionRequirement Section { get; }

            public void Add(int operatorIndex) => _contenders.Add(operatorIndex);

            public ClaimResource Freeze() =>
                new ClaimResource(ClaimId, Section, _contenders.ToArray());
        }
    }
}

using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// Operator 运行前按整组争用规则选出所有者；变更随 Context 提交，读档时按恢复的激活号重建。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>维护一次 Host 运行中 Operator 的资源占用、候选所有者和恢复状态。</summary>
    internal sealed class HostOperatorClaims : IDisposable
    {
        /// <summary>在恢复时保留、重建或丢弃挂起造成的延迟释放。</summary>
        internal enum RestorePolicy
        {
            PreservePendingRelease = 0,
            RebuildForSuspended = 1,
            DiscardAbandonedFuture = 2
        }

        private readonly Binding[] _operators;
        private readonly ContextFrameLayout _contextLayout;
        private readonly ClaimResource[] _claimResources;
        private readonly int[] _committedClaimOwners;
        private readonly ActivationId[] _committedClaimActivations;
        private readonly int[] _candidateClaimOwners;
        private readonly ActivationId[] _candidateClaimActivations;
        private readonly int[] _preparedRestoreClaimOwners;
        private readonly ActivationId[] _preparedRestoreClaimActivations;
        private readonly bool[] _retainedClaimOwners;
        private bool _releaseClaimsOnNextArbitration;
        private bool _preparedReleaseClaimsOnNextArbitration;
        private ulong _preparedRestoreToken;
        private bool _isDisposed;

        private HostOperatorClaims(ContextFrameLayout layout, Binding[] operators, ClaimResource[] claimResources)
        {
            _contextLayout = layout;
            _operators = operators;
            _claimResources = claimResources;
            _committedClaimOwners = new int[claimResources.Length];
            _committedClaimActivations = new ActivationId[claimResources.Length];
            _candidateClaimOwners = new int[claimResources.Length];
            _candidateClaimActivations = new ActivationId[claimResources.Length];
            _preparedRestoreClaimOwners = new int[claimResources.Length];
            _preparedRestoreClaimActivations = new ActivationId[claimResources.Length];
            _retainedClaimOwners = new bool[operators.Length];
            ClearOwners(_committedClaimOwners);
            ClearOwners(_candidateClaimOwners);
            ClearOwners(_preparedRestoreClaimOwners);
        }

        internal bool IsEligible(int operatorIndex) => _operators[operatorIndex].Eligible;

        internal bool TryStage(in FinalizedOperationFrame operation, in PreparedContextCommit context,
            out CoCoDiagnostic diagnostic) =>
            TryArbitrateClaims(operation, out diagnostic) && TryWriteClaimCandidates(context, out diagnostic);

        internal void CommitClaimsNoFail()
        {
            for (int index = 0; index < _claimResources.Length; index++)
            {
                _committedClaimOwners[index] = _candidateClaimOwners[index];
                _committedClaimActivations[index] = _candidateClaimActivations[index];
            }

            _releaseClaimsOnNextArbitration = false;
        }

        internal void Cancel()
        {
            ClearOwners(_candidateClaimOwners);
            Array.Clear(_candidateClaimActivations, 0, _candidateClaimActivations.Length);
        }

        internal bool TryValidateRestore(
            in ContextRestoreReadView restore,
            Func<ContextRestoreReadView, ActivationId, bool> isRestoredActivation,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed || !restore.IsValid ||
                !_contextLayout.IsSameInstance(restore.Layout) || isRestoredActivation == null)
            {
                diagnostic = RestoreError(
                    "Claim restore validation requires one idle Runtime and an exact post-policy Context view.");
                return false;
            }

            for (int resourceIndex = 0; resourceIndex < _claimResources.Length; resourceIndex++)
            {
                if (!TryReadRestoreClaim(
                        restore,
                        isRestoredActivation,
                        resourceIndex,
                        out _,
                        out _,
                        out diagnostic))
                {
                    return false;
                }
            }

            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                Binding binding = _operators[operatorIndex];
                bool ownsAny = false;
                bool ownsAll = true;
                for (int claimIndex = 0;
                     claimIndex < binding.ClaimResourceIndices.Length;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    if (!TryReadRestoreClaim(
                            restore,
                            isRestoredActivation,
                            resourceIndex,
                            out int owner,
                            out _,
                            out diagnostic))
                    {
                        return false;
                    }

                    ownsAny |= owner == operatorIndex;
                    ownsAll &= owner == operatorIndex;
                }

                if (ownsAny && !ownsAll)
                {
                    diagnostic = RestoreError(
                        "Restored Claim ownership violates an Operator's all-or-none descriptor.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPrepareRestore(
            in ContextRestoreReadView restore,
            Func<ContextRestoreReadView, ActivationId, bool> isRestoredActivation,
            ulong token,
            RestorePolicy overlayPolicy,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (token == 0UL || _preparedRestoreToken != 0UL ||
                !TryValidateRestore(restore, isRestoredActivation, out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = RestoreError("Claim restore preparation requires one fresh token.");
                }

                return false;
            }

            for (int resourceIndex = 0; resourceIndex < _claimResources.Length; resourceIndex++)
            {
                if (!TryReadRestoreClaim(
                        restore,
                        isRestoredActivation,
                        resourceIndex,
                        out _preparedRestoreClaimOwners[resourceIndex],
                        out _preparedRestoreClaimActivations[resourceIndex],
                        out diagnostic))
                {
                    CancelPreparedRestore();
                    return false;
                }
            }

            switch (overlayPolicy)
            {
                case RestorePolicy.PreservePendingRelease:
                    _preparedReleaseClaimsOnNextArbitration =
                        _releaseClaimsOnNextArbitration;
                    break;
                case RestorePolicy.RebuildForSuspended:
                    _preparedReleaseClaimsOnNextArbitration =
                        HasPreparedReleaseClaimOwner();
                    break;
                case RestorePolicy.DiscardAbandonedFuture:
                    _preparedReleaseClaimsOnNextArbitration = false;
                    break;
                default:
                    CancelPreparedRestore();
                    diagnostic = RestoreError(
                        "Claim restore preparation received an invalid overlay policy.");
                    return false;
            }

            _preparedRestoreToken = token;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void CommitPreparedRestoreNoFail(ulong token)
        {
            if (token == 0UL || token != _preparedRestoreToken)
            {
                return;
            }

            for (int index = 0; index < _claimResources.Length; index++)
            {
                _committedClaimOwners[index] = _preparedRestoreClaimOwners[index];
                _committedClaimActivations[index] = _preparedRestoreClaimActivations[index];
            }

            _releaseClaimsOnNextArbitration = _preparedReleaseClaimsOnNextArbitration;
            CancelPreparedRestore();
        }

        internal bool IsPreparedRestoreTokenCurrent(ulong token) =>
            !_isDisposed &&
            token != 0UL &&
            token == _preparedRestoreToken;

        internal void CancelPreparedRestore()
        {
            _preparedRestoreToken = 0UL;
            _preparedReleaseClaimsOnNextArbitration = false;
            ClearOwners(_preparedRestoreClaimOwners);
            Array.Clear(
                _preparedRestoreClaimActivations,
                0,
                _preparedRestoreClaimActivations.Length);
        }

        internal void Suspend()
        {
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                Binding binding = _operators[operatorIndex];
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    if (_committedClaimOwners[resourceIndex] == operatorIndex &&
                        binding.Descriptor.Claims[claimIndex].SuspendPolicy ==
                        OperatorClaimSuspendPolicy.Release)
                    {
                        // ContextFrame remains the sole committed Claim authority while
                        // Suspended. Apply the release as a candidate overlay on the next
                        // arbitration, then persist it through the normal composite commit.
                        _releaseClaimsOnNextArbitration = true;
                        return;
                    }
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
            CancelPreparedRestore();
            ClearOwners(_committedClaimOwners);
            Array.Clear(_committedClaimActivations, 0, _committedClaimActivations.Length);
            _releaseClaimsOnNextArbitration = false;
            _isDisposed = true;
        }

        private bool TryArbitrateClaims(
            in FinalizedOperationFrame operationFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateCommittedClaims(out diagnostic))
            {
                return false;
            }

            Array.Clear(_retainedClaimOwners, 0, _retainedClaimOwners.Length);
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                Binding binding = _operators[operatorIndex];
                binding.Eligible = true;
                for (int claimIndex = 0; claimIndex < binding.Descriptor.Claims.Count; claimIndex++)
                {
                    OperatorClaimRequirement claim = binding.Descriptor.Claims[claimIndex];
                    if (!operationFrame.TryGetHeader(
                            binding.ClaimSectionIndices[claimIndex],
                            out OperationSectionEntryHeader header))
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
                Binding binding = _operators[operatorIndex];
                if (binding.Descriptor.Claims.Count == 0 ||
                    _committedClaimOwners[binding.ClaimResourceIndices[0]] != operatorIndex ||
                    _releaseClaimsOnNextArbitration && ShouldReleaseOnSuspend(binding))
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

                    Binding retained = _operators[operatorIndex];
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
                    Binding binding = _operators[operatorIndex];
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
                    Binding binding = _operators[operatorIndex];
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

        private bool TryWriteClaimCandidates(in PreparedContextCommit context, out CoCoDiagnostic diagnostic)
        {
            for (int resourceIndex = 0; resourceIndex < _claimResources.Length; resourceIndex++)
            {
                ClaimResource resource = _claimResources[resourceIndex];
                int ownerIndex = _candidateClaimOwners[resourceIndex];
                OperatorClaimState state;
                if (ownerIndex < 0)
                {
                    state = OperatorClaimState.Unheld(
                        resource.ClaimId,
                        resource.Section.SectionId);
                }
                else if (ownerIndex >= _operators.Length ||
                         !OperatorClaimState.TryCreateHeld(
                             resource.ClaimId,
                             resource.Section.SectionId,
                             _operators[ownerIndex].Descriptor.OperatorId,
                             _candidateClaimActivations[resourceIndex],
                             out state))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "Claim arbitration produced an invalid canonical ownership record.");
                    return false;
                }

                if (!context.TryGetWriter(
                        resource.Block,
                        out ContextFrameWriter writer) ||
                    !writer.Write(resource.Slot, state))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "Canonical Claim ownership could not be written to its Graph-owned State Slot.");
                    return false;
                }
            }

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
                ActivationId activation = _committedClaimActivations[resourceIndex];
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
                Binding binding = _operators[operatorIndex];
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

        private bool TryReadRestoreClaim(
            in ContextRestoreReadView restore,
            Func<ContextRestoreReadView, ActivationId, bool> isRestoredActivation,
            int resourceIndex,
            out int ownerIndex,
            out ActivationId activationId,
            out CoCoDiagnostic diagnostic)
        {
            ownerIndex = -1;
            activationId = default;
            if (resourceIndex < 0 || resourceIndex >= _claimResources.Length)
            {
                diagnostic = RestoreError("Restored Claim resource index is invalid.");
                return false;
            }

            ClaimResource resource = _claimResources[resourceIndex];
            if (!restore.TryRead(resource.Slot, out OperatorClaimState state) ||
                !state.IsValid ||
                state.ClaimId != resource.ClaimId ||
                state.SectionId != resource.Section.SectionId)
            {
                diagnostic = RestoreError(
                    "Restored Claim identity or Operation Section does not match its descriptor.");
                return false;
            }

            if (!state.IsHeld)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            ownerIndex = FindOperatorIndex(state.OwnerOperatorId);
            activationId = state.ActivationId;
            if (ownerIndex < 0 ||
                FindClaimIndex(ownerIndex, resourceIndex) < 0 ||
                !isRestoredActivation(restore, activationId))
            {
                ownerIndex = -1;
                activationId = default;
                diagnostic = RestoreError(
                    "Restored Claim owner or Activation is inconsistent with Graph authority.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool HasPreparedReleaseClaimOwner()
        {
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                Binding binding = _operators[operatorIndex];
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    if (_preparedRestoreClaimOwners[resourceIndex] == operatorIndex &&
                        binding.Descriptor.Claims[claimIndex].SuspendPolicy ==
                        OperatorClaimSuspendPolicy.Release)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private int FindOperatorIndex(OperatorId operatorId)
        {
            for (int index = 0; index < _operators.Length; index++)
            {
                if (_operators[index].Descriptor.OperatorId == operatorId)
                {
                    return index;
                }
            }

            return -1;
        }

        private bool IsHigherPriority(
            int candidateOperator,
            int candidateClaim,
            int currentOperator,
            int currentClaim)
        {
            OperatorClaimRequirement candidate =
                _operators[candidateOperator].Descriptor.Claims[candidateClaim];
            OperatorClaimRequirement current =
                _operators[currentOperator].Descriptor.Claims[currentClaim];
            if (candidate.Priority != current.Priority)
            {
                return candidate.Priority > current.Priority;
            }

            return candidateOperator < currentOperator;
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

        private static bool ShouldReleaseOnSuspend(Binding binding)
        {
            for (int index = 0; index < binding.Descriptor.Claims.Count; index++)
            {
                if (binding.Descriptor.Claims[index].SuspendPolicy ==
                    OperatorClaimSuspendPolicy.Release)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>按 Inspector 上的次序登记资源需求，在进入 Play 时冻结争用关系。</summary>
        internal sealed class Builder
        {
            private readonly ContextFrameLayout _layout;
            private readonly OperationSectionRegistry _registry;
            private readonly Dictionary<OperationSectionId, OperatorClaimId> _sectionClaims = new Dictionary<OperationSectionId, OperatorClaimId>();
            private readonly Dictionary<StateSlotId, OperatorClaimId> _slotClaims = new Dictionary<StateSlotId, OperatorClaimId>();
            private readonly List<ClaimResource> _resources = new List<ClaimResource>();
            private readonly List<Binding> _operators = new List<Binding>();

            internal Builder(ContextFrameLayout layout, OperationSectionRegistry registry)
            {
                _layout = layout;
                _registry = registry;
            }

            internal bool TryAdd(OperatorDescriptor descriptor, out CoCoDiagnostic diagnostic)
            {
                if (!TryResolveClaims(descriptor, _layout, _registry, _resources, _sectionClaims, _slotClaims,
                        out int[] resources, out int[] sections, out diagnostic))
                {
                    return false;
                }

                _operators.Add(new Binding(descriptor, resources, sections));
                return true;
            }

            internal bool TryFreeze(IReadOnlyList<StateSlotId> declaredSlots,
                out HostOperatorClaims claims, out CoCoDiagnostic diagnostic)
            {
                claims = null;
                if (!TryValidateClaimCoverage(declaredSlots, _resources, out diagnostic)) return false;
                claims = new HostOperatorClaims(_layout, _operators.ToArray(), _resources.ToArray());
                return true;
            }
        }

        private static bool TryResolveClaims(
            OperatorDescriptor descriptor,
            ContextFrameLayout contextLayout,
            OperationSectionRegistry operationRegistry,
            List<ClaimResource> resources,
            Dictionary<OperationSectionId, OperatorClaimId> sectionClaims,
            Dictionary<StateSlotId, OperatorClaimId> slotClaims,
            out int[] resourceIndices,
            out int[] sectionIndices,
            out CoCoDiagnostic diagnostic)
        {
            resourceIndices = new int[descriptor.Claims.Count];
            sectionIndices = new int[descriptor.Claims.Count];
            for (int claimIndex = 0; claimIndex < descriptor.Claims.Count; claimIndex++)
            {
                OperatorClaimRequirement claim = descriptor.Claims[claimIndex];
                if (!operationRegistry.TryResolveDenseIndex(
                        claim.Section,
                        out sectionIndices[claimIndex]) ||
                    !TryResolveClaimStateSlot(
                        contextLayout,
                        claim.StateSlotId,
                        out StateBlockHandle claimBlock,
                        out StateSlot<OperatorClaimState> claimSlot,
                        out OperatorClaimState defaultClaim) ||
                    defaultClaim.ClaimId != claim.ClaimId ||
                    defaultClaim.SectionId != claim.Section.SectionId ||
                    defaultClaim.IsHeld)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "Claim Section and trusted Slot default must exactly match the frozen descriptor.");
                    return false;
                }

                if (sectionClaims.TryGetValue(
                        claim.Section.SectionId,
                        out OperatorClaimId existingClaimId) &&
                    existingClaimId != claim.ClaimId)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "One Operation Section cannot be arbitrated under multiple Claim identities.");
                    return false;
                }

                sectionClaims[claim.Section.SectionId] = claim.ClaimId;
                if (slotClaims.TryGetValue(
                        claim.StateSlotId,
                        out OperatorClaimId existingSlotClaim) &&
                    existingSlotClaim != claim.ClaimId)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "One Claim State Slot cannot represent multiple Claim identities.");
                    return false;
                }

                slotClaims[claim.StateSlotId] = claim.ClaimId;
                int resourceIndex = -1;
                for (int index = 0; index < resources.Count; index++)
                {
                    if (resources[index].ClaimId != claim.ClaimId)
                    {
                        continue;
                    }

                    if (resources[index].Section != claim.Section ||
                        resources[index].StateSlotId != claim.StateSlotId)
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
                    resources.Add(new ClaimResource(
                        claim.ClaimId,
                        claim.Section,
                        claim.StateSlotId,
                        claimBlock,
                        claimSlot));
                }

                resourceIndices[claimIndex] = resourceIndex;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryValidateClaimCoverage(
            IReadOnlyList<StateSlotId> declaredSlots,
            List<ClaimResource> resources,
            out CoCoDiagnostic diagnostic)
        {
            if (declaredSlots.Count != resources.Count)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.OperatorClaimConflict,
                    "Graph-owned Claim State Slots must be consumed exactly once by Claim arbitration.");
                return false;
            }

            for (int slotIndex = 0; slotIndex < declaredSlots.Count; slotIndex++)
            {
                bool found = false;
                for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
                {
                    if (resources[resourceIndex].StateSlotId == declaredSlots[slotIndex])
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "A declared Claim State Slot has no matching Claim descriptor.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryResolveClaimStateSlot(
            ContextFrameLayout layout,
            StateSlotId slotId,
            out StateBlockHandle blockHandle,
            out StateSlot<OperatorClaimState> slotHandle,
            out OperatorClaimState defaultValue)
        {
            blockHandle = default;
            slotHandle = default;
            defaultValue = default;
            StateSlotDescriptor slot = FindSlot(layout, slotId);
            StateBlockDescriptor block = slot == null
                ? null
                : FindBlock(layout, slot.WriterBlockId);
            if (slot == null ||
                block == null ||
                slot.ValueType != typeof(OperatorClaimState) ||
                slot.RestorePolicy == ContextRestorePolicy.Derived ||
                block.Owner != StateBlockOwner.Graph ||
                !layout.TryResolveBlock(block.BlockId, out blockHandle) ||
                !layout.TryResolveSlot(slotId, out slotHandle))
            {
                return false;
            }

            defaultValue = StateFlowTypeRules.Read<OperatorClaimState>(
                slot.DefaultBytes,
                0);
            return defaultValue.IsValid;
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

        private static void ClearOwners(int[] owners)
        {
            for (int index = 0; index < owners.Length; index++)
            {
                owners[index] = -1;
            }
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operator, code, message);

        private static CoCoDiagnostic RestoreError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.InvalidClaimRestore,
                message);

        /// <summary>保存一个 Operator 的声明、解析后的资源槽和本次仲裁的激活号。</summary>
        private sealed class Binding
        {
            internal Binding(OperatorDescriptor descriptor, int[] resources, int[] sections)
            {
                Descriptor = descriptor;
                ClaimResourceIndices = resources;
                ClaimSectionIndices = sections;
                ClaimActivations = new ActivationId[resources.Length];
            }

            internal OperatorDescriptor Descriptor { get; }
            internal int[] ClaimResourceIndices { get; }
            internal int[] ClaimSectionIndices { get; }
            internal ActivationId[] ClaimActivations { get; }
            internal bool Eligible { get; set; }
        }

        /// <summary>关联争用号、Operation 和保存所有者的 Context 槽。</summary>
        private readonly struct ClaimResource
        {
            public ClaimResource(
                OperatorClaimId claimId,
                OperationSectionRequirement section,
                StateSlotId stateSlotId,
                StateBlockHandle block,
                StateSlot<OperatorClaimState> slot)
            {
                ClaimId = claimId;
                Section = section;
                StateSlotId = stateSlotId;
                Block = block;
                Slot = slot;
            }

            public OperatorClaimId ClaimId { get; }
            public OperationSectionRequirement Section { get; }
            public StateSlotId StateSlotId { get; }
            public StateBlockHandle Block { get; }
            public StateSlot<OperatorClaimState> Slot { get; }
        }

    }
}

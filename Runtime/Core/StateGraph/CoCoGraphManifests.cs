using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public sealed class CoCoIntentRequirement
    {
        internal CoCoIntentRequirement(ICoCoGraphIntentRegistration registration, int denseIndex)
        {
            IntentId = registration.IntentId;
            ValueType = registration.ValueType;
            ReducerType = registration.ReducerType;
            ReducerFactoryType = registration.FactoryType;
            ReducerFactorySemanticFingerprint = registration.FactorySemanticFingerprint;
            MaxContributions = registration.MaxContributions;
            DenseIndex = denseIndex;
        }

        public CoCoIntentId IntentId { get; }
        public Type ValueType { get; }
        public Type ReducerType { get; }
        public Type ReducerFactoryType { get; }
        public ulong ReducerFactorySemanticFingerprint { get; }
        public int MaxContributions { get; }
        public int DenseIndex { get; }
    }

    public sealed class CoCoIntentRequirementManifest
    {
        private readonly IReadOnlyList<CoCoIntentRequirement> _requirements;
        private readonly IReadOnlyList<CoCoCompiledEventToIntentDeclaration> _eventAdapterDeclarations;

        internal CoCoIntentRequirementManifest(
            CoCoFrameLayoutId layoutId,
            ICoCoGraphIntentRegistration[] registrations,
            ICoCoGraphEventToIntentDeclarationRegistration[] eventAdapterDeclarations)
        {
            LayoutId = layoutId;
            var requirements = new CoCoIntentRequirement[registrations.Length];
            for (int index = 0; index < registrations.Length; index++)
            {
                requirements[index] = new CoCoIntentRequirement(registrations[index], index);
            }

            _requirements = Array.AsReadOnly(requirements);
            var declarations = new CoCoCompiledEventToIntentDeclaration[eventAdapterDeclarations.Length];
            for (int index = 0; index < eventAdapterDeclarations.Length; index++)
            {
                declarations[index] =
                    new CoCoCompiledEventToIntentDeclaration(eventAdapterDeclarations[index]);
            }

            _eventAdapterDeclarations = Array.AsReadOnly(declarations);
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public int Count => _requirements.Count;
        public IReadOnlyList<CoCoIntentRequirement> Requirements => _requirements;
        public int AdapterCount => _eventAdapterDeclarations.Count;
        public IReadOnlyList<CoCoCompiledEventToIntentDeclaration> EventAdapterDeclarations =>
            _eventAdapterDeclarations;
    }

    public sealed class CoCoGraphOperationProvideRequirement
    {
        internal CoCoGraphOperationProvideRequirement(ICoCoGraphOperationRegistration registration)
        {
            SectionId = registration.SectionId;
            Mode = registration.Mode;
            SectionType = registration.SectionType;
            Shape = registration.Shape;
            ViewFactoryType = registration.FactoryType;
            ViewFactorySemanticFingerprint = registration.FactorySemanticFingerprint;
        }

        public CoCoOperationSectionId SectionId { get; }
        public CoCoOperationSectionMode Mode { get; }
        public Type SectionType { get; }
        public CoCoOperationSectionShape Shape { get; }
        public Type ViewFactoryType { get; }
        public ulong ViewFactorySemanticFingerprint { get; }
    }

    public sealed class CoCoGraphOperationProvidesManifest
    {
        private readonly IReadOnlyList<CoCoGraphOperationProvideRequirement> _provides;

        internal CoCoGraphOperationProvidesManifest(
            CoCoFrameLayoutId layoutId,
            ICoCoGraphOperationRegistration[] registrations)
        {
            LayoutId = layoutId;
            var provides = new CoCoGraphOperationProvideRequirement[registrations.Length];
            for (int index = 0; index < registrations.Length; index++)
            {
                provides[index] = new CoCoGraphOperationProvideRequirement(registrations[index]);
            }

            _provides = Array.AsReadOnly(provides);
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public int Count => _provides.Count;
        public IReadOnlyList<CoCoGraphOperationProvideRequirement> Provides => _provides;
    }

    public sealed class CoCoContextStateSlotRequirement
    {
        private readonly IReadOnlyList<CoCoStateSlotId> _derivedDependencies;

        internal CoCoContextStateSlotRequirement(ICoCoGraphStateSlotRegistration registration)
        {
            SlotId = registration.SlotId;
            WriterBlockId = registration.BlockId;
            ValueType = registration.ValueType;
            Projection = registration.Projection;
            RestorePolicy = registration.RestorePolicy;
            Codec = registration.Codec;
            DefaultValueFingerprint = registration.DefaultValueFingerprint;
            RebuilderType = registration.RebuilderType;
            RebuilderSemanticFingerprint = registration.RebuilderSemanticFingerprint;
            var dependencies = (CoCoStateSlotId[])registration.DerivedDependencies.Clone();
            _derivedDependencies = Array.AsReadOnly(dependencies);
        }

        public CoCoStateSlotId SlotId { get; }
        public CoCoStateBlockId WriterBlockId { get; }
        public Type ValueType { get; }
        public CoCoContextProjection Projection { get; }
        public CoCoContextRestorePolicy RestorePolicy { get; }
        public CoCoCodecDescriptor Codec { get; }
        public ulong DefaultValueFingerprint { get; }
        public Type RebuilderType { get; }
        public ulong RebuilderSemanticFingerprint { get; }
        public IReadOnlyList<CoCoStateSlotId> DerivedDependencies => _derivedDependencies;
    }

    public sealed class CoCoContextStateBlockRequirement
    {
        private readonly IReadOnlyList<CoCoContextStateSlotRequirement> _slots;

        internal CoCoContextStateBlockRequirement(
            CoCoGraphStateBlockRegistration block,
            ICoCoGraphStateSlotRegistration[] slots)
        {
            BlockId = block.BlockId;
            Owner = block.Owner;
            var requirements = new CoCoContextStateSlotRequirement[slots.Length];
            for (int index = 0; index < slots.Length; index++)
            {
                requirements[index] = new CoCoContextStateSlotRequirement(slots[index]);
            }

            _slots = Array.AsReadOnly(requirements);
        }

        public CoCoStateBlockId BlockId { get; }
        public CoCoStateBlockOwner Owner { get; }
        public IReadOnlyList<CoCoContextStateSlotRequirement> Slots => _slots;
    }

    public sealed class CoCoContextFrameStateRequirementManifest
    {
        private readonly IReadOnlyList<CoCoContextStateBlockRequirement> _blocks;

        internal CoCoContextFrameStateRequirementManifest(
            CoCoFrameLayoutId layoutId,
            uint layoutVersion,
            CoCoGraphStateBlockRegistration[] blocks,
            ICoCoGraphStateSlotRegistration[][] slotsByBlock)
        {
            LayoutId = layoutId;
            LayoutVersion = layoutVersion;
            var requirements = new CoCoContextStateBlockRequirement[blocks.Length];
            int slotCount = 0;
            for (int index = 0; index < blocks.Length; index++)
            {
                ICoCoGraphStateSlotRegistration[] blockSlots = slotsByBlock[index];
                requirements[index] = new CoCoContextStateBlockRequirement(blocks[index], blockSlots);
                slotCount += blockSlots.Length;
            }

            _blocks = Array.AsReadOnly(requirements);
            SlotCount = slotCount;
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public int BlockCount => _blocks.Count;
        public int SlotCount { get; }
        public IReadOnlyList<CoCoContextStateBlockRequirement> Blocks => _blocks;

        internal bool TryValidate(out CoCoDiagnostic diagnostic)
        {
            if (!LayoutId.IsValid || LayoutVersion == 0U)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "ContextFrame State Requirement identity is invalid.");
                return false;
            }

            var slots = new Dictionary<CoCoStateSlotId, CoCoContextStateSlotRequirement>();
            var orderedSlots = new List<CoCoContextStateSlotRequirement>(SlotCount);
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                CoCoContextStateBlockRequirement block = _blocks[blockIndex];
                if (!block.BlockId.IsValid ||
                    block.Owner == CoCoStateBlockOwner.None ||
                    !Enum.IsDefined(typeof(CoCoStateBlockOwner), block.Owner))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidStateBlock,
                        "ContextFrame StateBlock requirement is invalid.");
                    return false;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    CoCoContextStateSlotRequirement slot = block.Slots[slotIndex];
                    if (!IsBasicallyValid(slot) ||
                        slot.WriterBlockId != block.BlockId ||
                        slots.ContainsKey(slot.SlotId))
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidStateSlot,
                            "ContextFrame StateSlot requirement is invalid.");
                        return false;
                    }

                    slots.Add(slot.SlotId, slot);
                    orderedSlots.Add(slot);
                }
            }

            for (int slotIndex = 0; slotIndex < orderedSlots.Count; slotIndex++)
            {
                CoCoContextStateSlotRequirement slot = orderedSlots[slotIndex];
                var unique = new HashSet<CoCoStateSlotId>();
                for (int dependencyIndex = 0;
                     dependencyIndex < slot.DerivedDependencies.Count;
                     dependencyIndex++)
                {
                    CoCoStateSlotId dependencyId = slot.DerivedDependencies[dependencyIndex];
                    if (!dependencyId.IsValid ||
                        dependencyId == slot.SlotId ||
                        !unique.Add(dependencyId) ||
                        !slots.TryGetValue(dependencyId, out CoCoContextStateSlotRequirement dependency) ||
                        !HasProjectionClosure(slot, dependency))
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidRestoreMetadata,
                            "ContextFrame derived StateSlot dependencies are invalid.");
                        return false;
                    }
                }
            }

            var visitStates = new Dictionary<CoCoStateSlotId, byte>();
            for (int slotIndex = 0; slotIndex < orderedSlots.Count; slotIndex++)
            {
                CoCoContextStateSlotRequirement slot = orderedSlots[slotIndex];
                if (slot.RestorePolicy == CoCoContextRestorePolicy.Derived &&
                    !VisitDerived(slot, slots, visitStates))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.DerivedDependencyCycle,
                        "ContextFrame derived StateSlot dependencies contain a cycle.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool IsBasicallyValid(CoCoContextStateSlotRequirement slot)
        {
            const CoCoContextProjection knownProjection =
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable;
            bool isDerived = slot.RestorePolicy == CoCoContextRestorePolicy.Derived;
            return slot.SlotId.IsValid &&
                   slot.WriterBlockId.IsValid &&
                   slot.ValueType != null &&
                   CoCoStateFlowTypeRules.IsReferenceFreeValueType(slot.ValueType) &&
                   (slot.Projection & ~knownProjection) == 0 &&
                   slot.RestorePolicy != CoCoContextRestorePolicy.None &&
                   Enum.IsDefined(typeof(CoCoContextRestorePolicy), slot.RestorePolicy) &&
                   slot.Codec.IsValid &&
                   slot.DefaultValueFingerprint != 0UL &&
                   (isDerived
                       ? slot.RebuilderType != null &&
                         slot.RebuilderSemanticFingerprint != 0UL &&
                         slot.DerivedDependencies.Count > 0
                       : slot.RebuilderType == null &&
                         slot.RebuilderSemanticFingerprint == 0UL &&
                         slot.DerivedDependencies.Count == 0);
        }

        private static bool HasProjectionClosure(
            CoCoContextStateSlotRequirement slot,
            CoCoContextStateSlotRequirement dependency)
        {
            if (dependency.RestorePolicy == CoCoContextRestorePolicy.ResetToDefault)
            {
                return true;
            }

            return ((slot.Projection & CoCoContextProjection.Temporal) == 0 ||
                    (dependency.Projection & CoCoContextProjection.Temporal) != 0) &&
                   ((slot.Projection & CoCoContextProjection.Durable) == 0 ||
                    (dependency.Projection & CoCoContextProjection.Durable) != 0);
        }

        private static bool VisitDerived(
            CoCoContextStateSlotRequirement slot,
            Dictionary<CoCoStateSlotId, CoCoContextStateSlotRequirement> slots,
            Dictionary<CoCoStateSlotId, byte> visitStates)
        {
            if (visitStates.TryGetValue(slot.SlotId, out byte state))
            {
                return state == 2;
            }

            visitStates[slot.SlotId] = 1;
            for (int index = 0; index < slot.DerivedDependencies.Count; index++)
            {
                CoCoContextStateSlotRequirement dependency = slots[slot.DerivedDependencies[index]];
                if (dependency.RestorePolicy == CoCoContextRestorePolicy.Derived &&
                    !VisitDerived(dependency, slots, visitStates))
                {
                    return false;
                }
            }

            visitStates[slot.SlotId] = 2;
            return true;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Frame, code, message);
    }
}

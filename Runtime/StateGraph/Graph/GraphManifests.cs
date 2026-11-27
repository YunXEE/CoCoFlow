using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 编译器从各层状态汇总输入、输出和 Context 的要求，Host 依据这些清单匹配来源与执行者。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>描述运行一张图需要的输入类型及注册信息。</summary>
    public sealed class IntentRequirement
    {

        #region Public API

        public IntentId IntentId { get; }
        public Type ValueType { get; }
        public Type ReducerType { get; }
        public Type ReducerFactoryType { get; }
        public ulong ReducerFactorySemanticFingerprint { get; }
        public int MaxContributions { get; }
        public int DenseIndex { get; }

        #endregion

        #region Internal Logic

        internal IntentRequirement(IGraphIntentRegistration registration, int denseIndex)
        {
            IntentId = registration.IntentId;
            ValueType = registration.ValueType;
            ReducerType = registration.ReducerType;
            ReducerFactoryType = registration.FactoryType;
            ReducerFactorySemanticFingerprint = registration.FactorySemanticFingerprint;
            MaxContributions = registration.MaxContributions;
            DenseIndex = denseIndex;
        }

        #endregion
    }

    /// <summary>汇总图所需输入并记录对应的帧布局标识。</summary>
    public sealed class IntentRequirementManifest
    {
        private readonly IReadOnlyList<IntentRequirement> _requirements;
        private readonly IReadOnlyList<CompiledEventToIntentDeclaration> _eventAdapterDeclarations;

        public FrameLayoutId LayoutId { get; }
        public int Count => _requirements.Count;
        public IReadOnlyList<IntentRequirement> Requirements => _requirements;
        public int AdapterCount => _eventAdapterDeclarations.Count;
        public IReadOnlyList<CompiledEventToIntentDeclaration> EventAdapterDeclarations =>
            _eventAdapterDeclarations;

        #region Internal Logic

        internal IntentRequirementManifest(
            FrameLayoutId layoutId,
            IGraphIntentRegistration[] registrations,
            IGraphEventToIntentDeclarationRegistration[] eventAdapterDeclarations)
        {
            LayoutId = layoutId;
            var requirements = new IntentRequirement[registrations.Length];
            for (int index = 0; index < registrations.Length; index++)
            {
                requirements[index] = new IntentRequirement(registrations[index], index);
            }

            _requirements = Array.AsReadOnly(requirements);
            var declarations = new CompiledEventToIntentDeclaration[eventAdapterDeclarations.Length];
            for (int index = 0; index < eventAdapterDeclarations.Length; index++)
            {
                declarations[index] =
                    new CompiledEventToIntentDeclaration(eventAdapterDeclarations[index]);
            }

            _eventAdapterDeclarations = Array.AsReadOnly(declarations);
        }

        #endregion
    }

    /// <summary>描述图能够为执行者提供的命令接口和固定布局。</summary>
    public sealed class GraphOperationProvision
    {

        #region Public API

        public OperationId OperationId { get; }
        public Mode Mode { get; }
        public Type OperationType { get; }
        public OperationShape Shape { get; }
        public Type ViewFactoryType { get; }
        public ulong ViewFactorySemanticFingerprint { get; }

        #endregion

        #region Internal Logic

        internal GraphOperationProvision(IGraphOperationRegistration registration)
        {
            OperationId = registration.OperationId;
            Mode = registration.Mode;
            OperationType = registration.OperationType;
            Shape = registration.Shape;
            ViewFactoryType = registration.FactoryType;
            ViewFactorySemanticFingerprint = registration.FactorySemanticFingerprint;
        }

        #endregion
    }

    /// <summary>汇总图能产生的 Operation，供 Host 匹配实际执行者。</summary>
    public sealed class GraphOperationProvisionManifest
    {
        private readonly IReadOnlyList<GraphOperationProvision> _provides;

        public FrameLayoutId LayoutId { get; }
        public int Count => _provides.Count;
        public IReadOnlyList<GraphOperationProvision> Provides => _provides;

        #region Internal Logic

        internal GraphOperationProvisionManifest(
            FrameLayoutId layoutId,
            IGraphOperationRegistration[] registrations)
        {
            LayoutId = layoutId;
            var provides = new GraphOperationProvision[registrations.Length];
            for (int index = 0; index < registrations.Length; index++)
            {
                provides[index] = new GraphOperationProvision(registrations[index]);
            }

            _provides = Array.AsReadOnly(provides);
        }

        #endregion
    }

    /// <summary>保存图声明的槽类型、投影和恢复要求。</summary>
    public sealed class ContextStateSlotRequirement
    {
        private readonly IReadOnlyList<StateSlotId> _derivedDependencies;

        public StateSlotId SlotId { get; }
        public StateBlockId WriterBlockId { get; }
        public Type ValueType { get; }
        public ContextProjection Projection { get; }
        public ContextRestorePolicy RestorePolicy { get; }
        public CodecDescriptor Codec { get; }
        public ulong DefaultValueFingerprint { get; }
        public Type RebuilderType { get; }
        public ulong RebuilderSemanticFingerprint { get; }
        public IReadOnlyList<StateSlotId> DerivedDependencies => _derivedDependencies;

        #region Internal Logic

        internal ContextStateSlotRequirement(IGraphStateSlotRegistration registration)
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
            var dependencies = (StateSlotId[])registration.DerivedDependencies.Clone();
            _derivedDependencies = Array.AsReadOnly(dependencies);
        }

        #endregion
    }

    /// <summary>汇总图需要的一个 Context 数据块及其中的槽。</summary>
    public sealed class ContextStateBlockRequirement
    {
        private readonly IReadOnlyList<ContextStateSlotRequirement> _slots;

        public StateBlockId BlockId { get; }
        public StateBlockOwner Owner { get; }
        public IReadOnlyList<ContextStateSlotRequirement> Slots => _slots;

        #region Internal Logic

        internal ContextStateBlockRequirement(
            GraphStateBlockRegistration block,
            IGraphStateSlotRegistration[] slots)
        {
            BlockId = block.BlockId;
            Owner = block.Owner;
            var requirements = new ContextStateSlotRequirement[slots.Length];
            for (int index = 0; index < slots.Length; index++)
            {
                requirements[index] = new ContextStateSlotRequirement(slots[index]);
            }

            _slots = Array.AsReadOnly(requirements);
        }

        #endregion
    }

    /// <summary>给 Host 提供整张图的 Context 布局要求和稳定身份。</summary>
    public sealed class ContextFrameStateRequirementManifest
    {
        private readonly IReadOnlyList<ContextStateBlockRequirement> _blocks;

        public FrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public int BlockCount => _blocks.Count;
        public int SlotCount { get; }
        public IReadOnlyList<ContextStateBlockRequirement> Blocks => _blocks;

        #region Internal Logic

        internal ContextFrameStateRequirementManifest(
            FrameLayoutId layoutId,
            uint layoutVersion,
            GraphStateBlockRegistration[] blocks,
            IGraphStateSlotRegistration[][] slotsByBlock)
        {
            LayoutId = layoutId;
            LayoutVersion = layoutVersion;
            var requirements = new ContextStateBlockRequirement[blocks.Length];
            int slotCount = 0;
            for (int index = 0; index < blocks.Length; index++)
            {
                IGraphStateSlotRegistration[] blockSlots = slotsByBlock[index];
                requirements[index] = new ContextStateBlockRequirement(blocks[index], blockSlots);
                slotCount += blockSlots.Length;
            }

            _blocks = Array.AsReadOnly(requirements);
            SlotCount = slotCount;
        }

        internal bool TryValidate(out CoCoDiagnostic diagnostic)
        {
            if (!LayoutId.IsValid || LayoutVersion == 0U)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "ContextFrame State Requirement identity is invalid.");
                return false;
            }

            var slots = new Dictionary<StateSlotId, ContextStateSlotRequirement>();
            var orderedSlots = new List<ContextStateSlotRequirement>(SlotCount);
            for (int blockIndex = 0; blockIndex < _blocks.Count; blockIndex++)
            {
                ContextStateBlockRequirement block = _blocks[blockIndex];
                if (!block.BlockId.IsValid ||
                    block.Owner == StateBlockOwner.None ||
                    !Enum.IsDefined(typeof(StateBlockOwner), block.Owner))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidStateBlock,
                        "ContextFrame StateBlock requirement is invalid.");
                    return false;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    ContextStateSlotRequirement slot = block.Slots[slotIndex];
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
                ContextStateSlotRequirement slot = orderedSlots[slotIndex];
                var unique = new HashSet<StateSlotId>();
                for (int dependencyIndex = 0;
                     dependencyIndex < slot.DerivedDependencies.Count;
                     dependencyIndex++)
                {
                    StateSlotId dependencyId = slot.DerivedDependencies[dependencyIndex];
                    if (!dependencyId.IsValid ||
                        dependencyId == slot.SlotId ||
                        !unique.Add(dependencyId) ||
                        !slots.TryGetValue(dependencyId, out ContextStateSlotRequirement dependency) ||
                        !HasProjectionClosure(slot, dependency))
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidRestoreMetadata,
                            "ContextFrame derived StateSlot dependencies are invalid.");
                        return false;
                    }
                }
            }

            var visitStates = new Dictionary<StateSlotId, byte>();
            for (int slotIndex = 0; slotIndex < orderedSlots.Count; slotIndex++)
            {
                ContextStateSlotRequirement slot = orderedSlots[slotIndex];
                if (slot.RestorePolicy == ContextRestorePolicy.Derived &&
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

        private static bool IsBasicallyValid(ContextStateSlotRequirement slot)
        {
            const ContextProjection knownProjection =
                ContextProjection.Temporal | ContextProjection.Durable;
            bool isDerived = slot.RestorePolicy == ContextRestorePolicy.Derived;
            return slot.SlotId.IsValid &&
                   slot.WriterBlockId.IsValid &&
                   slot.ValueType != null &&
                   StateFlowTypeRules.IsReferenceFreeValueType(slot.ValueType) &&
                   (slot.Projection & ~knownProjection) == 0 &&
                   slot.RestorePolicy != ContextRestorePolicy.None &&
                   Enum.IsDefined(typeof(ContextRestorePolicy), slot.RestorePolicy) &&
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
            ContextStateSlotRequirement slot,
            ContextStateSlotRequirement dependency)
        {
            if (dependency.RestorePolicy == ContextRestorePolicy.ResetToDefault)
            {
                return true;
            }

            return ((slot.Projection & ContextProjection.Temporal) == 0 ||
                    (dependency.Projection & ContextProjection.Temporal) != 0) &&
                   ((slot.Projection & ContextProjection.Durable) == 0 ||
                    (dependency.Projection & ContextProjection.Durable) != 0);
        }

        private static bool VisitDerived(
            ContextStateSlotRequirement slot,
            Dictionary<StateSlotId, ContextStateSlotRequirement> slots,
            Dictionary<StateSlotId, byte> visitStates)
        {
            if (visitStates.TryGetValue(slot.SlotId, out byte state))
            {
                return state == 2;
            }

            visitStates[slot.SlotId] = 1;
            for (int index = 0; index < slot.DerivedDependencies.Count; index++)
            {
                ContextStateSlotRequirement dependency = slots[slot.DerivedDependencies[index]];
                if (dependency.RestorePolicy == ContextRestorePolicy.Derived &&
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

        #endregion
    }
}

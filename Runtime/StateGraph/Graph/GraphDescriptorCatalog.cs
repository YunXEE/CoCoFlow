using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;

// 项目登记状态及模块合同，编译器从冻结目录检查图的要求、冻结配置并计算目录指纹。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>在图目录中标识归并工厂及其语义版本，供 Host 延后实例化。</summary>
    public readonly struct IntentReducerFactoryToken<TIntent, TReducer, TFactory> :
        IEquatable<IntentReducerFactoryToken<TIntent, TReducer, TFactory>>
        where TReducer : unmanaged, IIntentReducer<TIntent>
        where TFactory : IIntentReducerFactory<TIntent, TReducer>
    {
        /// <summary>将归并工厂的标识与语义指纹绑定，供目录注册和实例装配核对。</summary>
        public IntentReducerFactoryToken(ulong semanticFingerprint)
        {
            SemanticFingerprint = semanticFingerprint;
        }

        public Type FactoryType => typeof(TFactory);
        public ulong SemanticFingerprint { get; }
        public bool IsValid => SemanticFingerprint != 0UL;

        /// <summary>按<c>SemanticFingerprint</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(IntentReducerFactoryToken<TIntent, TReducer, TFactory> other) =>
            SemanticFingerprint == other.SemanticFingerprint;

        /// <summary>仅当对象属于同类型并且<c>SemanticFingerprint</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is IntentReducerFactoryToken<TIntent, TReducer, TFactory> other && Equals(other);

        /// <summary>为哈希集合计算与<c>SemanticFingerprint</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => SemanticFingerprint.GetHashCode();
    }

    /// <summary>标识某个输出视图工厂与它必须满足的操作合同。</summary>
    internal readonly struct OperationViewFactoryToken<TOperation, TFactory> :
        IEquatable<OperationViewFactoryToken<TOperation, TFactory>>
        where TOperation : class, IOperation
        where TFactory : IOperationViewFactory<TOperation>
    {
        public OperationViewFactoryToken(ulong semanticFingerprint)
        {
            SemanticFingerprint = semanticFingerprint;
        }

        public Type FactoryType => typeof(TFactory);
        public ulong SemanticFingerprint { get; }
        public bool IsValid => SemanticFingerprint != 0UL;

        public bool Equals(OperationViewFactoryToken<TOperation, TFactory> other) =>
            SemanticFingerprint == other.SemanticFingerprint;

        public override bool Equals(object obj) =>
            obj is OperationViewFactoryToken<TOperation, TFactory> other && Equals(other);

        public override int GetHashCode() => SemanticFingerprint.GetHashCode();
    }

    /// <summary>在编译图中记录派生重建器的类型及语义版本。</summary>
    public readonly struct DerivedStateRebuilderToken<TValue, TRebuilder> :
        IEquatable<DerivedStateRebuilderToken<TValue, TRebuilder>>
        where TValue : unmanaged
        where TRebuilder : IDerivedStateRebuilder<TValue>
    {
        /// <summary>将派生值重建器的标识与语义指纹绑定，供恢复布局匹配。</summary>
        public DerivedStateRebuilderToken(ulong semanticFingerprint)
        {
            SemanticFingerprint = semanticFingerprint;
        }

        public Type RebuilderType => typeof(TRebuilder);
        public ulong SemanticFingerprint { get; }
        public bool IsValid => SemanticFingerprint != 0UL;

        /// <summary>按<c>SemanticFingerprint</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(DerivedStateRebuilderToken<TValue, TRebuilder> other) =>
            SemanticFingerprint == other.SemanticFingerprint;

        /// <summary>仅当对象属于同类型并且<c>SemanticFingerprint</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is DerivedStateRebuilderToken<TValue, TRebuilder> other && Equals(other);

        /// <summary>为哈希集合计算与<c>SemanticFingerprint</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => SemanticFingerprint.GetHashCode();
    }

    /// <summary>描述一个可实例化的 State、配置和记忆类型，以及声明的输入、输出和槽。</summary>
    public sealed class StateDescriptor
    {
        private readonly IReadOnlyList<IntentId> _intentRequirements;
        private readonly IReadOnlyList<OperationId> _operationProvides;
        private readonly IReadOnlyList<StateBlockId> _contextStateRequirements;

        public StateDescriptorId DescriptorId { get; }
        public uint Revision { get; }
        public Type LogicType { get; }
        public Type AuthoringConfigType { get; }
        public Type ConfigSchemaType { get; }
        public ulong ConfigSchemaFingerprint { get; }
        public Type ActivationMemoryType { get; }
        public bool ProvidesActionProgress { get; }
        public StateRuntimeRegistration RuntimeRegistration { get; }
        public IReadOnlyList<IntentId> IntentRequirements => _intentRequirements;
        public IReadOnlyList<OperationId> OperationProvides => _operationProvides;
        public IReadOnlyList<StateBlockId> ContextStateRequirements => _contextStateRequirements;

        #region Internal Logic

        internal StateDescriptor(IStateDescriptorRegistration registration)
        {
            DescriptorId = registration.DescriptorId;
            Revision = registration.Revision;
            LogicType = registration.RuntimeRegistration.LogicType;
            AuthoringConfigType = registration.AuthoringConfigType;
            ConfigSchemaType = registration.ConfigSchemaType;
            ConfigSchemaFingerprint = registration.ConfigSchemaFingerprint;
            ActivationMemoryType = registration.RuntimeRegistration.ActivationMemoryType;
            ProvidesActionProgress = registration.RuntimeRegistration.ProvidesActionProgress;
            RuntimeRegistration = registration.RuntimeRegistration;
            _intentRequirements = Array.AsReadOnly(registration.IntentRequirements);
            _operationProvides = Array.AsReadOnly(registration.OperationProvides);
            _contextStateRequirements = Array.AsReadOnly(registration.ContextStateRequirements);
        }

        #endregion
    }

    /// <summary>在编译前登记状态和依赖合同，校验唯一性后冻结目录。</summary>
    public sealed class GraphDescriptorCatalogBuilder
    {
        #region Public API

        public bool IsFrozen => _isFrozen;

        /// <summary>在冻结前登记节点描述符、Logic、记忆、配置及要件。重复身份或合同不匹配时返回 false 并给出诊断。</summary>
        public bool TryRegisterState<TLogic, TAuthoringConfig, TSchema, TMemory>(
            StateDescriptorId descriptorId,
            uint revision,
            IConfigFreezer<TAuthoringConfig, TSchema> configFreezer,
            StateRuntimeRegistration<TLogic, TSchema, TMemory> runtimeRegistration,
            IntentId[] intentRequirements,
            OperationId[] operationProvides,
            StateBlockId[] contextStateRequirements,
            out CoCoDiagnostic diagnostic)
            where TLogic : StateLogic
            where TAuthoringConfig : StateConfig
            where TSchema : struct, IFrozenConfigSchema
            where TMemory : StateActivationMemory
        {
            if (!CanRegister(descriptorId.IsValid && revision > 0U, out diagnostic) ||
                configFreezer == null ||
                runtimeRegistration == null)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.MissingDescriptor,
                        "A State descriptor requires a valid id, config freezer, and runtime registration token.");
                }

                return false;
            }

            if (_states.ContainsKey(descriptorId))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "State descriptor ids must be unique within a catalog.");
                return false;
            }

            Type[] authorTypes =
            {
                typeof(TLogic),
                typeof(TAuthoringConfig),
                typeof(TSchema),
                typeof(TMemory),
                configFreezer.GetType()
            };
            if (!GraphAuthorAssemblyValidator.TryValidate(authorTypes, out diagnostic))
            {
                return false;
            }

            if (!GraphConfigTypeValidator.TryValidate<TAuthoringConfig>(out diagnostic))
            {
                return false;
            }

            if (!TryCloneIds(intentRequirements, out IntentId[] intents) ||
                !TryCloneIds(operationProvides, out OperationId[] operations) ||
                !TryCloneIds(contextStateRequirements, out StateBlockId[] blocks))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ManifestConflict,
                    "State descriptor requirements must contain valid ids.");
                return false;
            }

            _states.Add(
                descriptorId,
                new StateDescriptorRegistration<TLogic, TAuthoringConfig, TSchema, TMemory>(
                    descriptorId,
                    revision,
                    configFreezer,
                    runtimeRegistration,
                    intents,
                    operations,
                    blocks));
            AddAuthorAssemblyRoots(authorTypes);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>在冻结前登记输入的稳定标识、载荷及归并合同。无效或重复项返回 false 和诊断。</summary>
        public bool TryRegisterIntent<TIntent, TReducer, TFactory>(
            IntentId intentId,
            int maxContributions,
            IntentReducerFactoryToken<TIntent, TReducer, TFactory> reducerFactoryToken,
            out CoCoDiagnostic diagnostic)
            where TReducer : unmanaged, IIntentReducer<TIntent>
            where TFactory : IIntentReducerFactory<TIntent, TReducer>
        {
            if (!CanRegister(intentId.IsValid, out diagnostic) ||
                maxContributions <= 0 ||
                !reducerFactoryToken.IsValid)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidIntentDescriptor,
                        "An Intent registration requires a valid id, positive capacity, and reducer factory token.");
                }

                return false;
            }

            if (_intents.ContainsKey(intentId))
            {
                IGraphIntentRegistration existing = _intents[intentId];
                bool sameMetadata = existing.ValueType == typeof(TIntent) &&
                                    existing.ReducerType == typeof(TReducer) &&
                                    existing.FactoryType == typeof(TFactory) &&
                                    existing.FactorySemanticFingerprint ==
                                    reducerFactoryToken.SemanticFingerprint &&
                                    existing.MaxContributions == maxContributions;
                diagnostic = Error(
                    sameMetadata
                        ? CoCoDiagnosticCode.DuplicateIdentifier
                        : CoCoDiagnosticCode.ManifestConflict,
                    sameMetadata
                        ? "Intent ids must be unique within a catalog."
                        : "An Intent id cannot map to conflicting Manifest metadata.");
                return false;
            }

            if (!GraphAuthorAssemblyValidator.TryValidate(
                    new[] { typeof(TIntent), typeof(TReducer), typeof(TFactory) },
                    out diagnostic))
            {
                return false;
            }

            _intents.Add(
                intentId,
                new GraphIntentRegistration<TIntent, TReducer>(
                    intentId,
                    maxContributions,
                    typeof(TFactory),
                    reducerFactoryToken.SemanticFingerprint));
            AddAuthorAssemblyRoots(new[] { typeof(TIntent), typeof(TReducer), typeof(TFactory) });
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>登记图所需的 Context 块及其所有者，标识冲突或目录已冻结时返回 false。</summary>
        public bool TryRegisterStateBlock(
            StateBlockId blockId,
            StateBlockOwner owner,
            out CoCoDiagnostic diagnostic)
        {
            if (!CanRegister(blockId.IsValid, out diagnostic) ||
                owner == StateBlockOwner.None ||
                !Enum.IsDefined(typeof(StateBlockOwner), owner))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidStateBlock,
                        "A Context StateBlock registration requires a valid id and owner.");
                }

                return false;
            }

            if (_blocks.ContainsKey(blockId))
            {
                bool sameMetadata = _blocks[blockId].Owner == owner;
                diagnostic = Error(
                    sameMetadata
                        ? CoCoDiagnosticCode.DuplicateIdentifier
                        : CoCoDiagnosticCode.ManifestConflict,
                    sameMetadata
                        ? "Context StateBlock ids must be unique within a catalog."
                        : "A Context StateBlock id cannot map to conflicting Manifest metadata.");
                return false;
            }

            _blocks.Add(blockId, new GraphStateBlockRegistration(blockId, owner));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>为图目录登记槽及其默认值、所有者与恢复策略，声明不能匹配时返回 false。</summary>
        public bool TryRegisterStateSlot<TValue>(
            StateBlockId blockId,
            StateSlotId slotId,
            ContextProjection projection,
            ContextRestorePolicy restorePolicy,
            TValue defaultValue,
            ulong defaultValueFingerprint,
            CodecDescriptor codec,
            StateSlotId[] derivedDependencies,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            if (!GraphAuthorAssemblyValidator.TryValidate(
                    new[] { typeof(TValue) },
                    out diagnostic))
            {
                return false;
            }

            if (!TryRegisterStateSlotCore(
                    new GraphStateSlotRegistration<TValue>(
                    blockId,
                    slotId,
                    projection,
                    restorePolicy,
                    defaultValue,
                    defaultValueFingerprint,
                    codec,
                    Clone(derivedDependencies),
                    null,
                    0UL),
                    out diagnostic))
            {
                return false;
            }

            AddAuthorAssemblyRoots(new[] { typeof(TValue) });
            return true;
        }

        /// <summary>登记由指定依赖和重建器产生的派生槽。无效依赖或重复声明时返回 false。</summary>
        public bool TryRegisterDerivedStateSlot<TValue, TRebuilder>(
            StateBlockId blockId,
            StateSlotId slotId,
            ContextProjection projection,
            TValue defaultValue,
            ulong defaultValueFingerprint,
            CodecDescriptor codec,
            StateSlotId[] derivedDependencies,
            DerivedStateRebuilderToken<TValue, TRebuilder> rebuilderToken,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
            where TRebuilder : IDerivedStateRebuilder<TValue>
        {
            if (!rebuilderToken.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "A derived StateSlot requires a valid rebuilder token.");
                return false;
            }

            if (!GraphAuthorAssemblyValidator.TryValidate(
                    new[] { typeof(TValue), typeof(TRebuilder) },
                    out diagnostic))
            {
                return false;
            }

            if (!TryRegisterStateSlotCore(
                    new GraphStateSlotRegistration<TValue>(
                    blockId,
                    slotId,
                    projection,
                    ContextRestorePolicy.Derived,
                    defaultValue,
                    defaultValueFingerprint,
                    codec,
                    Clone(derivedDependencies),
                    typeof(TRebuilder),
                    rebuilderToken.SemanticFingerprint),
                    out diagnostic))
            {
                return false;
            }

            AddAuthorAssemblyRoots(new[] { typeof(TValue), typeof(TRebuilder) });
            return true;
        }

        /// <summary>校验状态及其输入、输出与槽声明，成功返回带语义指纹的只读目录，失败返回诊断。</summary>
        public bool TryFreeze(
            out GraphDescriptorCatalog catalog,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                catalog = null;
                diagnostic = Error(CoCoDiagnosticCode.RegistryFrozen, "Descriptor Catalog may only freeze once.");
                return false;
            }

            foreach (IStateDescriptorRegistration state in _states.Values)
            {
                if (!RequirementsExist(
                        state.IntentRequirements,
                        state.OperationProvides,
                        state.ContextStateRequirements))
                {
                    catalog = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.MissingDescriptor,
                        "A State descriptor references an unregistered Manifest declaration.");
                    return false;
                }
            }

            foreach (IGraphStateSlotRegistration slot in _slots.Values)
            {
                if (!_blocks.ContainsKey(slot.BlockId))
                {
                    catalog = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidStateSlot,
                        "A Context StateSlot references an unregistered StateBlock.");
                    return false;
                }
            }

            catalog = new GraphDescriptorCatalog(
                Sorted(_states),
                Sorted(_intents),
                Sorted(_eventToIntentDeclarations),
                Sorted(_operations),
                Sorted(_blocks),
                Sorted(_slots),
                SortedAuthorAssemblyRootNames());
            _isFrozen = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion

        #region Internal Logic

        private readonly Dictionary<StateDescriptorId, IStateDescriptorRegistration> _states =
            new Dictionary<StateDescriptorId, IStateDescriptorRegistration>();
        private readonly Dictionary<IntentId, IGraphIntentRegistration> _intents =
            new Dictionary<IntentId, IGraphIntentRegistration>();
        private readonly Dictionary<EventToIntentDeclarationKey,
                IGraphEventToIntentDeclarationRegistration> _eventToIntentDeclarations =
            new Dictionary<EventToIntentDeclarationKey,
                IGraphEventToIntentDeclarationRegistration>();
        private readonly Dictionary<EventTypeId, IGraphEventToIntentDeclarationRegistration>
            _eventTypes =
                new Dictionary<EventTypeId, IGraphEventToIntentDeclarationRegistration>();
        private readonly Dictionary<OperationId, IGraphOperationRegistration> _operations =
            new Dictionary<OperationId, IGraphOperationRegistration>();
        private readonly Dictionary<StateBlockId, GraphStateBlockRegistration> _blocks =
            new Dictionary<StateBlockId, GraphStateBlockRegistration>();
        private readonly Dictionary<StateSlotId, IGraphStateSlotRegistration> _slots =
            new Dictionary<StateSlotId, IGraphStateSlotRegistration>();
        private readonly HashSet<string> _authorAssemblyRootNames =
            new HashSet<string>(StringComparer.Ordinal);
        private bool _isFrozen;

#if UNITY_INCLUDE_TESTS
        internal bool TryRegisterEventToIntentDeclaration<TEvent, TIntent>(
            EventDomainId domainId,
            EventTypeId eventTypeId,
            IntentId providedIntentId,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (!CanRegister(
                    domainId.IsValid && eventTypeId.IsValid && providedIntentId.IsValid,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidIdentifier,
                        "An Event-to-Intent declaration requires valid Domain, Event, and Intent ids.");
                }

                return false;
            }

            if (!_intents.TryGetValue(
                    providedIntentId,
                    out IGraphIntentRegistration intentRegistration))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "An Event-to-Intent declaration must reference an already registered Intent.");
                return false;
            }

            if (intentRegistration.ValueType != typeof(TIntent))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ManifestConflict,
                    "An Event-to-Intent declaration must match the registered Intent value type exactly.");
                return false;
            }

            EventToIntentDeclarationKey.TryCreate(
                eventTypeId,
                providedIntentId,
                out EventToIntentDeclarationKey key);
            if (_eventToIntentDeclarations.TryGetValue(
                    key,
                    out IGraphEventToIntentDeclarationRegistration existingDeclaration))
            {
                bool sameMetadata = existingDeclaration.EventDomainId == domainId &&
                                    existingDeclaration.EventPayloadType == typeof(TEvent) &&
                                    existingDeclaration.ProvidedIntentType == typeof(TIntent);
                diagnostic = Error(
                    sameMetadata
                        ? CoCoDiagnosticCode.DuplicateIdentifier
                        : CoCoDiagnosticCode.ManifestConflict,
                    sameMetadata
                        ? "Event-to-Intent declaration pairs must be unique within a catalog."
                        : "An Event-to-Intent declaration pair cannot map to conflicting metadata.");
                return false;
            }

            if (_eventTypes.TryGetValue(
                    eventTypeId,
                    out IGraphEventToIntentDeclarationRegistration existingEvent) &&
                (existingEvent.EventDomainId != domainId ||
                 existingEvent.EventPayloadType != typeof(TEvent)))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ManifestConflict,
                    "An EventTypeId must map to exactly one Domain and Event payload type.");
                return false;
            }

            Type[] authorTypes = { typeof(TEvent), typeof(TIntent) };
            if (!GraphAuthorAssemblyValidator.TryValidate(authorTypes, out diagnostic))
            {
                return false;
            }

            var registration = new GraphEventToIntentDeclarationRegistration(
                key,
                domainId,
                typeof(TEvent),
                typeof(TIntent));
            _eventToIntentDeclarations.Add(key, registration);
            if (!_eventTypes.ContainsKey(eventTypeId))
            {
                _eventTypes.Add(eventTypeId, registration);
            }

            AddAuthorAssemblyRoots(authorTypes);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
#endif

        internal bool TryRegisterOperation<TOperation, TFactory>(
            OperationId operationId,
            Mode mode,
            OperationViewFactoryToken<TOperation, TFactory> viewFactoryToken,
            out CoCoDiagnostic diagnostic)
            where TOperation : class, IOperation
            where TFactory : IOperationViewFactory<TOperation>
        {
            if (!CanRegister(true, out diagnostic))
            {
                return false;
            }

            if (!viewFactoryToken.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "An Operation Section registration requires a valid view factory token.");
                return false;
            }

            if (!OperationRequirement.TryCreate<TOperation>(
                    operationId,
                    mode,
                    out OperationRequirement requirement,
                    out CoCoDiagnostic requirementDiagnostic))
            {
                diagnostic = Error(requirementDiagnostic.Code, requirementDiagnostic.Message);
                return false;
            }

            Type operationType = requirement.OperationType;

            if (_operations.ContainsKey(operationId))
            {
                IGraphOperationRegistration existing = _operations[operationId];
                bool sameMetadata = existing.Mode == mode &&
                                    existing.OperationType == operationType &&
                                    Equals(existing.Shape, requirement.Shape) &&
                                    existing.FactoryType == typeof(TFactory) &&
                                    existing.FactorySemanticFingerprint ==
                                    viewFactoryToken.SemanticFingerprint;
                diagnostic = Error(
                    sameMetadata
                        ? CoCoDiagnosticCode.DuplicateIdentifier
                        : CoCoDiagnosticCode.ManifestConflict,
                    sameMetadata
                        ? "Operation Section ids must be unique within a catalog."
                        : "An Operation Section id cannot map to conflicting Manifest metadata.");
                return false;
            }

            if (!GraphAuthorAssemblyValidator.TryValidate(
                    new[] { typeof(TOperation), typeof(TFactory) },
                    out diagnostic))
            {
                return false;
            }

            _operations.Add(
                operationId,
                new GraphOperationRegistration<TOperation>(
                    requirement,
                    typeof(TFactory),
                    viewFactoryToken.SemanticFingerprint));
            AddAuthorAssemblyRoots(new[] { typeof(TOperation), typeof(TFactory) });
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryRegisterStateSlotCore(
            IGraphStateSlotRegistration registration,
            out CoCoDiagnostic diagnostic)
        {
            if (!CanRegister(registration.IsBasicallyValid, out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidStateSlot,
                        "A Context StateSlot registration contains invalid metadata.");
                }

                return false;
            }

            if (_slots.ContainsKey(registration.SlotId))
            {
                bool sameMetadata = HasSameManifestMetadata(_slots[registration.SlotId], registration);
                diagnostic = Error(
                    sameMetadata
                        ? CoCoDiagnosticCode.DuplicateIdentifier
                        : CoCoDiagnosticCode.ManifestConflict,
                    sameMetadata
                        ? "Context StateSlot ids must be unique within a catalog."
                        : "A Context StateSlot id cannot map to conflicting Manifest metadata.");
                return false;
            }

            _slots.Add(registration.SlotId, registration);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool CanRegister(bool valueIsValid, out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                diagnostic = Error(CoCoDiagnosticCode.RegistryFrozen, "Descriptor Catalog is frozen.");
                return false;
            }

            if (!valueIsValid)
            {
                diagnostic = CoCoDiagnostic.None;
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool RequirementsExist(
            IntentId[] intents,
            OperationId[] operations,
            StateBlockId[] blocks)
        {
            for (int index = 0; index < intents.Length; index++)
            {
                if (!_intents.ContainsKey(intents[index]))
                {
                    return false;
                }
            }

            for (int index = 0; index < operations.Length; index++)
            {
                if (!_operations.ContainsKey(operations[index]))
                {
                    return false;
                }
            }

            for (int index = 0; index < blocks.Length; index++)
            {
                if (!_blocks.ContainsKey(blocks[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryCloneIds<T>(T[] source, out T[] copy)
            where T : struct
        {
            if (source == null || source.Length == 0)
            {
                copy = Array.Empty<T>();
                return true;
            }

            var unique = new HashSet<T>();
            var canonical = new List<T>(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                object boxed = source[index];
                bool isValid = boxed is IntentId intent && intent.IsValid ||
                               boxed is OperationId operation && operation.IsValid ||
                               boxed is StateBlockId block && block.IsValid;
                if (!isValid)
                {
                    copy = null;
                    return false;
                }

                if (unique.Add(source[index]))
                {
                    canonical.Add(source[index]);
                }
            }

            copy = canonical.ToArray();
            Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(
                left.ToString(),
                right.ToString()));
            return true;
        }

        private static bool HasSameManifestMetadata(
            IGraphStateSlotRegistration left,
            IGraphStateSlotRegistration right)
        {
            if (left.BlockId != right.BlockId ||
                left.SlotId != right.SlotId ||
                left.ValueType != right.ValueType ||
                left.Projection != right.Projection ||
                left.RestorePolicy != right.RestorePolicy ||
                !left.Codec.Equals(right.Codec) ||
                left.DefaultValueFingerprint != right.DefaultValueFingerprint ||
                left.RebuilderType != right.RebuilderType ||
                left.RebuilderSemanticFingerprint != right.RebuilderSemanticFingerprint ||
                left.DerivedDependencies.Length != right.DerivedDependencies.Length)
            {
                return false;
            }

            for (int index = 0; index < left.DerivedDependencies.Length; index++)
            {
                if (left.DerivedDependencies[index] != right.DerivedDependencies[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static StateSlotId[] Clone(StateSlotId[] source) =>
            source == null ? Array.Empty<StateSlotId>() : (StateSlotId[])source.Clone();

        private void AddAuthorAssemblyRoots(Type[] types)
        {
            for (int index = 0; index < types.Length; index++)
            {
                string assemblyName = types[index].Assembly.GetName().Name;
                if (!string.IsNullOrEmpty(assemblyName))
                {
                    _authorAssemblyRootNames.Add(assemblyName);
                }
            }
        }

        private string[] SortedAuthorAssemblyRootNames()
        {
            var names = new string[_authorAssemblyRootNames.Count];
            _authorAssemblyRootNames.CopyTo(names);
            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }

        private static KeyValuePair<TKey, TValue>[] Sorted<TKey, TValue>(Dictionary<TKey, TValue> source)
            where TKey : struct
        {
            var entries = new KeyValuePair<TKey, TValue>[source.Count];
            int index = 0;
            foreach (KeyValuePair<TKey, TValue> entry in source)
            {
                entries[index++] = entry;
            }

            Array.Sort(entries, (left, right) =>
                StringComparer.Ordinal.Compare(left.Key.ToString(), right.Key.ToString()));
            return entries;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);

        #endregion

    }

    /// <summary>为图编译与 Host 装配提供稳定的类型目录和语义指纹。</summary>
    public sealed class GraphDescriptorCatalog
    {
        private readonly Dictionary<StateDescriptorId, StateDescriptor> _states;
        private readonly Dictionary<StateDescriptorId, IStateDescriptorRegistration> _stateRegistrations;
        private readonly Dictionary<IntentId, IGraphIntentRegistration> _intents;
        private readonly Dictionary<EventToIntentDeclarationKey,
            IGraphEventToIntentDeclarationRegistration> _eventToIntentDeclarations;
        private readonly Dictionary<OperationId, IGraphOperationRegistration> _operations;
        private readonly Dictionary<StateBlockId, GraphStateBlockRegistration> _blocks;
        private readonly Dictionary<StateSlotId, IGraphStateSlotRegistration> _slots;
        private readonly IReadOnlyList<StateDescriptor> _stateDescriptors;
        private readonly IReadOnlyList<string> _authorAssemblyRootNames;

        public ulong Fingerprint { get; }
        public int StateDescriptorCount => _states.Count;
        public bool IsFrozen => true;
        internal IReadOnlyList<StateDescriptor> StateDescriptors => _stateDescriptors;
        internal IReadOnlyList<string> AuthorAssemblyRootNames => _authorAssemblyRootNames;

        #region Public API

        /// <summary>按稳定标识读取已登记的状态描述符，找不到时返回 false。</summary>
        public bool TryGetStateDescriptor(
            StateDescriptorId descriptorId,
            out StateDescriptor descriptor) => _states.TryGetValue(descriptorId, out descriptor);

        /// <summary>根据节点描述符选择 freezer 并生成冻结快照，未注册或配置不合法时返回 false 和诊断。</summary>
        public bool TryFreezeStateConfig(
            StateDescriptorId descriptorId,
            StateConfig source,
            out FrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            if (!_states.TryGetValue(descriptorId, out StateDescriptor descriptor))
            {
                snapshot = null;
                diagnostic = Error(CoCoDiagnosticCode.MissingDescriptor, "State descriptor is not registered.");
                return false;
            }

            return _stateRegistrations[descriptorId].TryFreeze(source, out snapshot, out diagnostic);
        }

        #endregion

        #region Internal Logic

        internal GraphDescriptorCatalog(
            KeyValuePair<StateDescriptorId, IStateDescriptorRegistration>[] states,
            KeyValuePair<IntentId, IGraphIntentRegistration>[] intents,
            KeyValuePair<EventToIntentDeclarationKey,
                IGraphEventToIntentDeclarationRegistration>[] eventToIntentDeclarations,
            KeyValuePair<OperationId, IGraphOperationRegistration>[] operations,
            KeyValuePair<StateBlockId, GraphStateBlockRegistration>[] blocks,
            KeyValuePair<StateSlotId, IGraphStateSlotRegistration>[] slots,
            string[] authorAssemblyRootNames)
        {
            _states = new Dictionary<StateDescriptorId, StateDescriptor>(states.Length);
            _stateRegistrations = Copy(states);
            _intents = Copy(intents);
            _eventToIntentDeclarations = Copy(eventToIntentDeclarations);
            _operations = Copy(operations);
            _blocks = Copy(blocks);
            _slots = Copy(slots);
            _authorAssemblyRootNames = Array.AsReadOnly((string[])authorAssemblyRootNames.Clone());
            var stateDescriptors = new StateDescriptor[states.Length];

            ulong hash = GraphCatalogHash.OffsetBasis;
            for (int index = 0; index < states.Length; index++)
            {
                var descriptor = new StateDescriptor(states[index].Value);
                stateDescriptors[index] = descriptor;
                _states.Add(states[index].Key, descriptor);
                GraphCatalogHash.Add(ref hash, descriptor.DescriptorId.High);
                GraphCatalogHash.Add(ref hash, descriptor.DescriptorId.Low);
                GraphCatalogHash.Add(ref hash, descriptor.Revision);
                GraphCatalogHash.Add(ref hash, descriptor.LogicType);
                GraphCatalogHash.Add(ref hash, descriptor.AuthoringConfigType);
                GraphCatalogHash.Add(ref hash, descriptor.ConfigSchemaType);
                GraphCatalogHash.Add(ref hash, descriptor.ConfigSchemaFingerprint);
                GraphCatalogHash.Add(ref hash, descriptor.ActivationMemoryType);
                GraphCatalogHash.Add(ref hash, descriptor.ProvidesActionProgress ? 1UL : 0UL);
                GraphCatalogHash.Add(ref hash, states[index].Value.FreezerType);
                AddIds(ref hash, states[index].Value.IntentRequirements);
                AddIds(ref hash, states[index].Value.OperationProvides);
                AddIds(ref hash, states[index].Value.ContextStateRequirements);
            }

            for (int index = 0; index < intents.Length; index++)
            {
                IGraphIntentRegistration registration = intents[index].Value;
                GraphCatalogHash.Add(ref hash, registration.IntentId.High);
                GraphCatalogHash.Add(ref hash, registration.IntentId.Low);
                GraphCatalogHash.Add(ref hash, registration.ValueType);
                GraphCatalogHash.Add(ref hash, registration.ReducerType);
                GraphCatalogHash.Add(ref hash, registration.FactoryType);
                GraphCatalogHash.Add(ref hash, registration.FactorySemanticFingerprint);
                GraphCatalogHash.Add(ref hash, (ulong)registration.MaxContributions);
            }

            for (int index = 0; index < eventToIntentDeclarations.Length; index++)
            {
                IGraphEventToIntentDeclarationRegistration registration =
                    eventToIntentDeclarations[index].Value;
                GraphCatalogHash.Add(ref hash, registration.EventDomainId.Value);
                GraphCatalogHash.Add(ref hash, registration.EventTypeId.High);
                GraphCatalogHash.Add(ref hash, registration.EventTypeId.Low);
                GraphCatalogHash.Add(ref hash, registration.EventPayloadType);
                GraphCatalogHash.Add(ref hash, registration.ProvidedIntentId.High);
                GraphCatalogHash.Add(ref hash, registration.ProvidedIntentId.Low);
                GraphCatalogHash.Add(ref hash, registration.ProvidedIntentType);
            }

            for (int index = 0; index < operations.Length; index++)
            {
                IGraphOperationRegistration registration = operations[index].Value;
                GraphCatalogHash.Add(ref hash, registration.OperationId.High);
                GraphCatalogHash.Add(ref hash, registration.OperationId.Low);
                GraphCatalogHash.Add(ref hash, (ulong)registration.Mode);
                GraphCatalogHash.Add(ref hash, registration.OperationType);
                GraphCatalogHash.Add(ref hash, registration.Shape.ShapeFingerprint);
                GraphCatalogHash.Add(ref hash, registration.FactoryType);
                GraphCatalogHash.Add(ref hash, registration.FactorySemanticFingerprint);
            }

            for (int index = 0; index < blocks.Length; index++)
            {
                GraphStateBlockRegistration block = blocks[index].Value;
                GraphCatalogHash.Add(ref hash, block.BlockId.High);
                GraphCatalogHash.Add(ref hash, block.BlockId.Low);
                GraphCatalogHash.Add(ref hash, (ulong)block.Owner);
            }

            for (int index = 0; index < slots.Length; index++)
            {
                IGraphStateSlotRegistration slot = slots[index].Value;
                GraphCatalogHash.Add(ref hash, slot.BlockId.High);
                GraphCatalogHash.Add(ref hash, slot.BlockId.Low);
                GraphCatalogHash.Add(ref hash, slot.SlotId.High);
                GraphCatalogHash.Add(ref hash, slot.SlotId.Low);
                GraphCatalogHash.Add(ref hash, slot.ValueType);
                GraphCatalogHash.Add(ref hash, (ulong)slot.Projection);
                GraphCatalogHash.Add(ref hash, (ulong)slot.RestorePolicy);
                GraphCatalogHash.Add(ref hash, slot.Codec.CodecId.High);
                GraphCatalogHash.Add(ref hash, slot.Codec.CodecId.Low);
                GraphCatalogHash.Add(ref hash, slot.Codec.Version);
                GraphCatalogHash.Add(ref hash, slot.DefaultValueFingerprint);
                GraphCatalogHash.Add(ref hash, slot.RebuilderType);
                GraphCatalogHash.Add(ref hash, slot.RebuilderSemanticFingerprint);
                AddIds(ref hash, slot.DerivedDependencies);
            }

            _stateDescriptors = Array.AsReadOnly(stateDescriptors);
            Fingerprint = hash == 0UL ? GraphCatalogHash.OffsetBasis : hash;
        }

        internal bool TryGetIntent(IntentId id, out IGraphIntentRegistration registration) =>
            _intents.TryGetValue(id, out registration);

        internal bool TryGetEventToIntentDeclaration(
            EventToIntentDeclarationKey key,
            out IGraphEventToIntentDeclarationRegistration registration) =>
            _eventToIntentDeclarations.TryGetValue(key, out registration);

        internal bool TryGetOperation(
            OperationId id,
            out IGraphOperationRegistration registration) => _operations.TryGetValue(id, out registration);

        internal bool TryGetBlock(StateBlockId id, out GraphStateBlockRegistration registration) =>
            _blocks.TryGetValue(id, out registration);

        internal IGraphStateSlotRegistration[] GetSlots(StateBlockId blockId)
        {
            var matches = new List<IGraphStateSlotRegistration>();
            foreach (IGraphStateSlotRegistration slot in _slots.Values)
            {
                if (slot.BlockId == blockId)
                {
                    matches.Add(slot);
                }
            }

            matches.Sort((left, right) => StringComparer.Ordinal.Compare(
                left.SlotId.ToString(),
                right.SlotId.ToString()));
            return matches.ToArray();
        }

        internal bool AcceptsStateConfig(
            StateDescriptorId descriptorId,
            FrozenConfigSnapshot snapshot) =>
            _stateRegistrations.TryGetValue(descriptorId, out IStateDescriptorRegistration registration) &&
            registration.Accepts(snapshot);

        private static void AddIds<T>(ref ulong hash, T[] ids)
            where T : struct
        {
            GraphCatalogHash.Add(ref hash, (ulong)ids.Length);
            for (int index = 0; index < ids.Length; index++)
            {
                object boxed = ids[index];
                switch (boxed)
                {
                    case IntentId intent:
                        GraphCatalogHash.Add(ref hash, intent.High);
                        GraphCatalogHash.Add(ref hash, intent.Low);
                        break;
                    case OperationId operation:
                        GraphCatalogHash.Add(ref hash, operation.High);
                        GraphCatalogHash.Add(ref hash, operation.Low);
                        break;
                    case StateBlockId block:
                        GraphCatalogHash.Add(ref hash, block.High);
                        GraphCatalogHash.Add(ref hash, block.Low);
                        break;
                    case StateSlotId slot:
                        GraphCatalogHash.Add(ref hash, slot.High);
                        GraphCatalogHash.Add(ref hash, slot.Low);
                        break;
                }
            }
        }

        private static Dictionary<TKey, TValue> Copy<TKey, TValue>(KeyValuePair<TKey, TValue>[] entries)
        {
            var copy = new Dictionary<TKey, TValue>(entries.Length);
            for (int index = 0; index < entries.Length; index++)
            {
                copy.Add(entries[index].Key, entries[index].Value);
            }

            return copy;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);

        #endregion
    }

    /// <summary>在内部把描述符关联到对应的配置冻结与运行时类型信息。</summary>
    internal interface IStateDescriptorRegistration
    {
        StateDescriptorId DescriptorId { get; }
        uint Revision { get; }
        Type AuthoringConfigType { get; }
        Type ConfigSchemaType { get; }
        ulong ConfigSchemaFingerprint { get; }
        Type FreezerType { get; }
        StateRuntimeRegistration RuntimeRegistration { get; }
        IntentId[] IntentRequirements { get; }
        OperationId[] OperationProvides { get; }
        StateBlockId[] ContextStateRequirements { get; }
        bool Accepts(FrozenConfigSnapshot snapshot);
        bool TryFreeze(
            StateConfig source,
            out FrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>在已登记的具体 schema 和 Logic 类型下执行配置检查或冻结。</summary>
    internal sealed class StateDescriptorRegistration<TLogic, TAuthoringConfig, TSchema, TMemory> :
        IStateDescriptorRegistration
        where TLogic : StateLogic
        where TAuthoringConfig : StateConfig
        where TSchema : struct, IFrozenConfigSchema
        where TMemory : StateActivationMemory
    {
        private readonly IConfigFreezer<TAuthoringConfig, TSchema> _freezer;
        private readonly FrozenConfigSchema<TSchema> _schema;

        public StateDescriptorRegistration(
            StateDescriptorId descriptorId,
            uint revision,
            IConfigFreezer<TAuthoringConfig, TSchema> freezer,
            StateRuntimeRegistration<TLogic, TSchema, TMemory> runtimeRegistration,
            IntentId[] intentRequirements,
            OperationId[] operationProvides,
            StateBlockId[] contextStateRequirements)
        {
            DescriptorId = descriptorId;
            Revision = revision;
            _freezer = freezer;
            _schema = runtimeRegistration.ConfigSchema;
            RuntimeRegistration = runtimeRegistration;
            IntentRequirements = intentRequirements;
            OperationProvides = operationProvides;
            ContextStateRequirements = contextStateRequirements;
        }

        public StateDescriptorId DescriptorId { get; }
        public uint Revision { get; }
        public Type AuthoringConfigType => typeof(TAuthoringConfig);
        public Type ConfigSchemaType => typeof(TSchema);
        public ulong ConfigSchemaFingerprint => _schema.Fingerprint;
        public Type FreezerType => _freezer.GetType();
        public StateRuntimeRegistration RuntimeRegistration { get; }
        public IntentId[] IntentRequirements { get; }
        public OperationId[] OperationProvides { get; }
        public StateBlockId[] ContextStateRequirements { get; }

        public bool Accepts(FrozenConfigSnapshot snapshot) =>
            snapshot != null &&
            snapshot.MatchesSchema(_schema);

        public bool TryFreeze(
            StateConfig source,
            out FrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            snapshot = null;
            if (source == null || source.GetType() != typeof(TAuthoringConfig))
            {
                diagnostic = TypeMismatch("State authoring config does not match its descriptor.");
                return false;
            }

            FrozenConfigWriter<TSchema> writer = _schema.CreateWriter();
            try
            {
                if (!_freezer.TryFreeze(
                        (TAuthoringConfig)source,
                        writer,
                        out diagnostic))
                {
                    writer.SealWithoutSnapshot();
                    if (diagnostic.IsNone)
                    {
                        diagnostic = TypeMismatch("State config freezer failed without a diagnostic.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                writer.SealWithoutSnapshot();
                diagnostic = TypeMismatch("State config freezer threw while creating a frozen snapshot.");
                return false;
            }

            if (!diagnostic.IsNone)
            {
                writer.SealWithoutSnapshot();
                diagnostic = TypeMismatch("State config freezer succeeded with a diagnostic.");
                return false;
            }

            if (!writer.TrySeal(out snapshot, out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = TypeMismatch("State config freezer returned an invalid snapshot.");
                }

                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic TypeMismatch(string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.State, CoCoDiagnosticCode.InvalidFrozenConfig, message);
    }

    /// <summary>记录一种输入的载荷、容量和执行该归并的工厂标识。</summary>
    internal interface IGraphIntentRegistration
    {
        IntentId IntentId { get; }
        Type ValueType { get; }
        Type ReducerType { get; }
        Type FactoryType { get; }
        ulong FactorySemanticFingerprint { get; }
        int MaxContributions { get; }
    }

    /// <summary>把泛型输入合同关联到图目录中的稳定输入标识。</summary>
    internal sealed class GraphIntentRegistration<TIntent, TReducer> : IGraphIntentRegistration
        where TReducer : unmanaged, IIntentReducer<TIntent>
    {
        public GraphIntentRegistration(
            IntentId intentId,
            int maxContributions,
            Type factoryType,
            ulong factorySemanticFingerprint)
        {
            IntentId = intentId;
            MaxContributions = maxContributions;
            FactoryType = factoryType;
            FactorySemanticFingerprint = factorySemanticFingerprint;
        }

        public IntentId IntentId { get; }
        public Type ValueType => typeof(TIntent);
        public Type ReducerType => typeof(TReducer);
        public Type FactoryType { get; }
        public ulong FactorySemanticFingerprint { get; }
        public int MaxContributions { get; }
    }

    /// <summary>向编译器提供已登记操作的身份、模式和布局。</summary>
    internal interface IGraphOperationRegistration
    {
        OperationId OperationId { get; }
        Mode Mode { get; }
        Type OperationType { get; }
        OperationShape Shape { get; }
        Type FactoryType { get; }
        ulong FactorySemanticFingerprint { get; }
    }

    /// <summary>在编译目录中保存一个具体的操作接口及视图合同。</summary>
    internal sealed class GraphOperationRegistration<TOperation> : IGraphOperationRegistration
        where TOperation : class, IOperation
    {
        public GraphOperationRegistration(
            OperationRequirement requirement,
            Type factoryType,
            ulong factorySemanticFingerprint)
        {
            Requirement = requirement;
            FactoryType = factoryType;
            FactorySemanticFingerprint = factorySemanticFingerprint;
        }

        public OperationRequirement Requirement { get; }
        public OperationId OperationId => Requirement.OperationId;
        public Mode Mode => Requirement.Mode;
        public Type OperationType => Requirement.OperationType;
        public OperationShape Shape => Requirement.Shape;
        public Type FactoryType { get; }
        public ulong FactorySemanticFingerprint { get; }
    }

    /// <summary>关联图需要的数据块和其所有者。</summary>
    internal sealed class GraphStateBlockRegistration
    {
        public GraphStateBlockRegistration(StateBlockId blockId, StateBlockOwner owner)
        {
            BlockId = blockId;
            Owner = owner;
        }

        public StateBlockId BlockId { get; }
        public StateBlockOwner Owner { get; }
    }

    /// <summary>为编译器提供槽的值类型、默认值和恢复元数据。</summary>
    internal interface IGraphStateSlotRegistration
    {
        StateBlockId BlockId { get; }
        StateSlotId SlotId { get; }
        Type ValueType { get; }
        ContextProjection Projection { get; }
        ContextRestorePolicy RestorePolicy { get; }
        CodecDescriptor Codec { get; }
        StateSlotId[] DerivedDependencies { get; }
        ulong DefaultValueFingerprint { get; }
        Type RebuilderType { get; }
        ulong RebuilderSemanticFingerprint { get; }
        bool IsBasicallyValid { get; }
    }

    /// <summary>把具体值类型的槽申明加入图的 Context 要件。</summary>
    internal sealed class GraphStateSlotRegistration<TValue> : IGraphStateSlotRegistration
        where TValue : unmanaged
    {
        public GraphStateSlotRegistration(
            StateBlockId blockId,
            StateSlotId slotId,
            ContextProjection projection,
            ContextRestorePolicy restorePolicy,
            TValue defaultValue,
            ulong defaultValueFingerprint,
            CodecDescriptor codec,
            StateSlotId[] derivedDependencies,
            Type rebuilderType,
            ulong rebuilderSemanticFingerprint)
        {
            BlockId = blockId;
            SlotId = slotId;
            Projection = projection;
            RestorePolicy = restorePolicy;
            DefaultValueFingerprint = defaultValueFingerprint;
            Codec = codec;
            DerivedDependencies = derivedDependencies;
            RebuilderType = rebuilderType;
            RebuilderSemanticFingerprint = rebuilderSemanticFingerprint;
        }

        public StateBlockId BlockId { get; }
        public StateSlotId SlotId { get; }
        public Type ValueType => typeof(TValue);
        public ContextProjection Projection { get; }
        public ContextRestorePolicy RestorePolicy { get; }
        public CodecDescriptor Codec { get; }
        public StateSlotId[] DerivedDependencies { get; }
        public ulong DefaultValueFingerprint { get; }
        public Type RebuilderType { get; }
        public ulong RebuilderSemanticFingerprint { get; }
        public bool IsBasicallyValid
        {
            get
            {
                const ContextProjection knownProjection =
                    ContextProjection.Temporal | ContextProjection.Durable;
                return BlockId.IsValid &&
                       SlotId.IsValid &&
                       StateFlowTypeRules.IsReferenceFreeValueType(typeof(TValue)) &&
                       DefaultValueFingerprint != 0UL &&
                       Codec.IsValid &&
                       (Projection & ~knownProjection) == 0 &&
                       RestorePolicy != ContextRestorePolicy.None &&
                       Enum.IsDefined(typeof(ContextRestorePolicy), RestorePolicy) &&
                       (RestorePolicy == ContextRestorePolicy.Derived
                           ? RebuilderType != null &&
                             RebuilderSemanticFingerprint != 0UL &&
                             DerivedDependencies.Length > 0
                           : RebuilderType == null &&
                             RebuilderSemanticFingerprint == 0UL &&
                             DerivedDependencies.Length == 0);
            }
        }
    }

    /// <summary>判断注册的 Logic 和合同类型能否由纯玩法程序集提供。</summary>
    internal static class GraphAuthorAssemblyValidator
    {
        private static readonly string[] ForbiddenExactNames =
        {
            "CoCoFlow.Runtime.StateGraph"
        };

        private static readonly string[] ForbiddenPrefixes =
        {
            "Unity.",
            "UnityEngine",
            "UnityEditor",
            "CoCoFlow.Editor",
            "CoCoFlow.Runtime.Modules"
        };

        public static bool TryValidate(Type[] types, out CoCoDiagnostic diagnostic)
        {
            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                Type type = types[typeIndex];
                if (type == null)
                {
                    diagnostic = Error("A descriptor registration contains a null type.");
                    return false;
                }

                AssemblyName assemblyName = type.Assembly.GetName();
                if (IsForbidden(assemblyName.Name))
                {
                    diagnostic = Error("Graph authoring types must live in an engine-independent assembly.");
                    return false;
                }

                AssemblyName[] references = type.Assembly.GetReferencedAssemblies();
                for (int referenceIndex = 0; referenceIndex < references.Length; referenceIndex++)
                {
                    if (IsForbidden(references[referenceIndex].Name))
                    {
                        diagnostic = Error(
                            "Graph authoring assemblies cannot reference Unity, Editor, or Module assemblies.");
                        return false;
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool IsForbidden(string assemblyName)
        {
            for (int index = 0; index < ForbiddenExactNames.Length; index++)
            {
                if (string.Equals(
                        assemblyName,
                        ForbiddenExactNames[index],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            for (int index = 0; index < ForbiddenPrefixes.Length; index++)
            {
                if (assemblyName.StartsWith(ForbiddenPrefixes[index], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static CoCoDiagnostic Error(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.State,
                CoCoDiagnosticCode.InvalidAuthoringDependency,
                message);
    }

    /// <summary>验证图配置在冻结前可安全保存与复制的字段形状。</summary>
    internal static class GraphConfigTypeValidator
    {
        public static bool TryValidate<TAuthoringConfig>(out CoCoDiagnostic diagnostic)
        {
            Type authoringType = typeof(TAuthoringConfig);
            if (!authoringType.IsSerializable)
            {
                diagnostic = Error("Authoring config types must be marked Serializable.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic Error(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.State,
                CoCoDiagnosticCode.InvalidFrozenConfig,
                message);
    }

    /// <summary>把稳定类型名、数值及逐项声明组合为图目录指纹。</summary>
    internal static class GraphCatalogHash
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static void Add(ref ulong hash, ulong value)
        {
            for (int index = 0; index < 8; index++)
            {
                hash ^= (byte)(value >> (index * 8));
                hash *= Prime;
            }
        }

        public static void Add(ref ulong hash, Type type)
        {
            if (type == null)
            {
                Add(ref hash, 0UL);
                return;
            }

            Add(ref hash, GraphTypeIdentity.AssemblyName(type));
            Add(ref hash, GraphTypeIdentity.FullName(type));
        }

        private static void Add(ref ulong hash, string value)
        {
            Add(ref hash, unchecked((ulong)value.Length));
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                hash ^= (byte)character;
                hash *= Prime;
                hash ^= (byte)(character >> 8);
                hash *= Prime;
            }
        }
    }
}

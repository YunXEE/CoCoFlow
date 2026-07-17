using System;
using System.Collections.Generic;
using System.Reflection;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Identifies an Intent reducer factory without retaining or executing a factory instance.
    /// The semantic fingerprint distinguishes different bindings implemented by the same factory type.
    /// </summary>
    public readonly struct CoCoIntentReducerFactoryToken<TIntent, TReducer, TFactory> :
        IEquatable<CoCoIntentReducerFactoryToken<TIntent, TReducer, TFactory>>
        where TIntent : unmanaged
        where TReducer : unmanaged, ICoCoIntentReducer<TIntent>
        where TFactory : ICoCoIntentReducerFactory<TIntent, TReducer>
    {
        public CoCoIntentReducerFactoryToken(ulong semanticFingerprint)
        {
            SemanticFingerprint = semanticFingerprint;
        }

        public Type FactoryType => typeof(TFactory);
        public ulong SemanticFingerprint { get; }
        public bool IsValid => SemanticFingerprint != 0UL;

        public bool Equals(CoCoIntentReducerFactoryToken<TIntent, TReducer, TFactory> other) =>
            SemanticFingerprint == other.SemanticFingerprint;

        public override bool Equals(object obj) =>
            obj is CoCoIntentReducerFactoryToken<TIntent, TReducer, TFactory> other && Equals(other);

        public override int GetHashCode() => SemanticFingerprint.GetHashCode();
    }

    /// <summary>
    /// Identifies an Operation Section view factory without retaining or executing a factory instance.
    /// </summary>
    public readonly struct CoCoOperationSectionViewFactoryToken<TSection, TFactory> :
        IEquatable<CoCoOperationSectionViewFactoryToken<TSection, TFactory>>
        where TSection : class, ICoCoOperationSection
        where TFactory : ICoCoOperationSectionViewFactory<TSection>
    {
        public CoCoOperationSectionViewFactoryToken(ulong semanticFingerprint)
        {
            SemanticFingerprint = semanticFingerprint;
        }

        public Type FactoryType => typeof(TFactory);
        public ulong SemanticFingerprint { get; }
        public bool IsValid => SemanticFingerprint != 0UL;

        public bool Equals(CoCoOperationSectionViewFactoryToken<TSection, TFactory> other) =>
            SemanticFingerprint == other.SemanticFingerprint;

        public override bool Equals(object obj) =>
            obj is CoCoOperationSectionViewFactoryToken<TSection, TFactory> other && Equals(other);

        public override int GetHashCode() => SemanticFingerprint.GetHashCode();
    }

    /// <summary>
    /// Identifies a derived StateSlot rebuilder without retaining or executing a rebuilder instance.
    /// </summary>
    public readonly struct CoCoDerivedStateRebuilderToken<TValue, TRebuilder> :
        IEquatable<CoCoDerivedStateRebuilderToken<TValue, TRebuilder>>
        where TValue : unmanaged
        where TRebuilder : ICoCoDerivedStateRebuilder<TValue>
    {
        public CoCoDerivedStateRebuilderToken(ulong semanticFingerprint)
        {
            SemanticFingerprint = semanticFingerprint;
        }

        public Type RebuilderType => typeof(TRebuilder);
        public ulong SemanticFingerprint { get; }
        public bool IsValid => SemanticFingerprint != 0UL;

        public bool Equals(CoCoDerivedStateRebuilderToken<TValue, TRebuilder> other) =>
            SemanticFingerprint == other.SemanticFingerprint;

        public override bool Equals(object obj) =>
            obj is CoCoDerivedStateRebuilderToken<TValue, TRebuilder> other && Equals(other);

        public override int GetHashCode() => SemanticFingerprint.GetHashCode();
    }

    public sealed class CoCoStateDescriptor
    {
        private readonly IReadOnlyList<CoCoIntentId> _intentRequirements;
        private readonly IReadOnlyList<CoCoOperationSectionId> _operationProvides;
        private readonly IReadOnlyList<CoCoStateBlockId> _contextStateRequirements;

        internal CoCoStateDescriptor(ICoCoStateDescriptorRegistration registration)
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

        public CoCoStateDescriptorId DescriptorId { get; }
        public uint Revision { get; }
        public Type LogicType { get; }
        public Type AuthoringConfigType { get; }
        public Type ConfigSchemaType { get; }
        public ulong ConfigSchemaFingerprint { get; }
        public Type ActivationMemoryType { get; }
        public bool ProvidesActionProgress { get; }
        public CoCoStateRuntimeRegistration RuntimeRegistration { get; }
        public IReadOnlyList<CoCoIntentId> IntentRequirements => _intentRequirements;
        public IReadOnlyList<CoCoOperationSectionId> OperationProvides => _operationProvides;
        public IReadOnlyList<CoCoStateBlockId> ContextStateRequirements => _contextStateRequirements;
    }

    public sealed class CoCoConditionDescriptor
    {
        private readonly IReadOnlyList<CoCoIntentId> _intentRequirements;
        private readonly IReadOnlyList<CoCoStateBlockId> _contextStateRequirements;

        internal CoCoConditionDescriptor(ICoCoConditionDescriptorRegistration registration)
        {
            DescriptorId = registration.DescriptorId;
            Revision = registration.Revision;
            ConditionType = registration.RuntimeRegistration.ConditionType;
            AuthoringConfigType = registration.AuthoringConfigType;
            ConfigSchemaType = registration.ConfigSchemaType;
            ConfigSchemaFingerprint = registration.ConfigSchemaFingerprint;
            RuntimeRegistration = registration.RuntimeRegistration;
            _intentRequirements = Array.AsReadOnly(registration.IntentRequirements);
            _contextStateRequirements = Array.AsReadOnly(registration.ContextStateRequirements);
        }

        public CoCoConditionDescriptorId DescriptorId { get; }
        public uint Revision { get; }
        public Type ConditionType { get; }
        public Type AuthoringConfigType { get; }
        public Type ConfigSchemaType { get; }
        public ulong ConfigSchemaFingerprint { get; }
        public CoCoConditionRuntimeRegistration RuntimeRegistration { get; }
        public IReadOnlyList<CoCoIntentId> IntentRequirements => _intentRequirements;
        public IReadOnlyList<CoCoStateBlockId> ContextStateRequirements => _contextStateRequirements;
    }

    public sealed class CoCoGraphDescriptorCatalogBuilder
    {
        private readonly Dictionary<CoCoStateDescriptorId, ICoCoStateDescriptorRegistration> _states =
            new Dictionary<CoCoStateDescriptorId, ICoCoStateDescriptorRegistration>();
        private readonly Dictionary<CoCoConditionDescriptorId, ICoCoConditionDescriptorRegistration> _conditions =
            new Dictionary<CoCoConditionDescriptorId, ICoCoConditionDescriptorRegistration>();
        private readonly Dictionary<CoCoIntentId, ICoCoGraphIntentRegistration> _intents =
            new Dictionary<CoCoIntentId, ICoCoGraphIntentRegistration>();
        private readonly Dictionary<CoCoEventToIntentDeclarationKey,
                ICoCoGraphEventToIntentDeclarationRegistration> _eventToIntentDeclarations =
            new Dictionary<CoCoEventToIntentDeclarationKey,
                ICoCoGraphEventToIntentDeclarationRegistration>();
        private readonly Dictionary<CoCoEventTypeId, ICoCoGraphEventToIntentDeclarationRegistration>
            _eventTypes =
                new Dictionary<CoCoEventTypeId, ICoCoGraphEventToIntentDeclarationRegistration>();
        private readonly Dictionary<CoCoOperationSectionId, ICoCoGraphOperationRegistration> _operations =
            new Dictionary<CoCoOperationSectionId, ICoCoGraphOperationRegistration>();
        private readonly Dictionary<CoCoStateBlockId, CoCoGraphStateBlockRegistration> _blocks =
            new Dictionary<CoCoStateBlockId, CoCoGraphStateBlockRegistration>();
        private readonly Dictionary<CoCoStateSlotId, ICoCoGraphStateSlotRegistration> _slots =
            new Dictionary<CoCoStateSlotId, ICoCoGraphStateSlotRegistration>();
        private readonly HashSet<string> _authorAssemblyRootNames =
            new HashSet<string>(StringComparer.Ordinal);
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

        public bool TryRegisterState<TLogic, TAuthoringConfig, TSchema, TMemory>(
            CoCoStateDescriptorId descriptorId,
            uint revision,
            ICoCoConfigFreezer<TAuthoringConfig, TSchema> configFreezer,
            CoCoStateRuntimeRegistration<TLogic, TSchema, TMemory> runtimeRegistration,
            CoCoIntentId[] intentRequirements,
            CoCoOperationSectionId[] operationProvides,
            CoCoStateBlockId[] contextStateRequirements,
            out CoCoDiagnostic diagnostic)
            where TLogic : CoCoStateLogic
            where TAuthoringConfig : CoCoStateConfig
            where TSchema : struct, ICoCoFrozenConfigSchema
            where TMemory : CoCoActivationMemory
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
            if (!CoCoGraphAuthorAssemblyValidator.TryValidate(authorTypes, out diagnostic))
            {
                return false;
            }

            if (!CoCoGraphConfigTypeValidator.TryValidate<TAuthoringConfig>(out diagnostic))
            {
                return false;
            }

            if (!TryCloneIds(intentRequirements, out CoCoIntentId[] intents) ||
                !TryCloneIds(operationProvides, out CoCoOperationSectionId[] operations) ||
                !TryCloneIds(contextStateRequirements, out CoCoStateBlockId[] blocks))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ManifestConflict,
                    "State descriptor requirements must contain valid ids.");
                return false;
            }

            _states.Add(
                descriptorId,
                new CoCoStateDescriptorRegistration<TLogic, TAuthoringConfig, TSchema, TMemory>(
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

        public bool TryRegisterCondition<TCondition, TAuthoringConfig, TSchema>(
            CoCoConditionDescriptorId descriptorId,
            uint revision,
            ICoCoConfigFreezer<TAuthoringConfig, TSchema> configFreezer,
            CoCoConditionRuntimeRegistration<TCondition, TSchema> runtimeRegistration,
            CoCoIntentId[] intentRequirements,
            CoCoStateBlockId[] contextStateRequirements,
            out CoCoDiagnostic diagnostic)
            where TCondition : CoCoStateCondition
            where TAuthoringConfig : CoCoConditionConfig
            where TSchema : struct, ICoCoFrozenConfigSchema
        {
            if (!CanRegister(descriptorId.IsValid && revision > 0U, out diagnostic) ||
                configFreezer == null ||
                runtimeRegistration == null)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.MissingDescriptor,
                        "A Condition descriptor requires a valid id, config freezer, and runtime registration token.");
                }

                return false;
            }

            if (_conditions.ContainsKey(descriptorId))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "Condition descriptor ids must be unique within a catalog.");
                return false;
            }

            Type[] authorTypes =
            {
                typeof(TCondition),
                typeof(TAuthoringConfig),
                typeof(TSchema),
                configFreezer.GetType()
            };
            if (!CoCoGraphAuthorAssemblyValidator.TryValidate(authorTypes, out diagnostic))
            {
                return false;
            }

            if (!CoCoGraphConfigTypeValidator.TryValidate<TAuthoringConfig>(out diagnostic))
            {
                return false;
            }

            if (!TryCloneIds(intentRequirements, out CoCoIntentId[] intents) ||
                !TryCloneIds(contextStateRequirements, out CoCoStateBlockId[] blocks))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ManifestConflict,
                    "Condition descriptor requirements must contain valid ids.");
                return false;
            }

            _conditions.Add(
                descriptorId,
                new CoCoConditionDescriptorRegistration<TCondition, TAuthoringConfig, TSchema>(
                    descriptorId,
                    revision,
                    configFreezer,
                    runtimeRegistration,
                    intents,
                    blocks));
            AddAuthorAssemblyRoots(authorTypes);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryRegisterIntent<TIntent, TReducer, TFactory>(
            CoCoIntentId intentId,
            int maxContributions,
            CoCoIntentReducerFactoryToken<TIntent, TReducer, TFactory> reducerFactoryToken,
            out CoCoDiagnostic diagnostic)
            where TIntent : unmanaged
            where TReducer : unmanaged, ICoCoIntentReducer<TIntent>
            where TFactory : ICoCoIntentReducerFactory<TIntent, TReducer>
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
                ICoCoGraphIntentRegistration existing = _intents[intentId];
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

            if (!CoCoGraphAuthorAssemblyValidator.TryValidate(
                    new[] { typeof(TIntent), typeof(TReducer), typeof(TFactory) },
                    out diagnostic))
            {
                return false;
            }

            _intents.Add(
                intentId,
                new CoCoGraphIntentRegistration<TIntent, TReducer>(
                    intentId,
                    maxContributions,
                    typeof(TFactory),
                    reducerFactoryToken.SemanticFingerprint));
            AddAuthorAssemblyRoots(new[] { typeof(TIntent), typeof(TReducer), typeof(TFactory) });
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>
        /// Declares a graph-level Event payload that may be projected into a registered Intent.
        /// The declaration records static identity and exact payload types only; Pre4 owns the
        /// concrete Adapter instance and its binding coverage.
        /// </summary>
        public bool TryRegisterEventToIntentDeclaration<TEvent, TIntent>(
            CoCoEventDomainId domainId,
            CoCoEventTypeId eventTypeId,
            CoCoIntentId providedIntentId,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
            where TIntent : unmanaged
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
                    out ICoCoGraphIntentRegistration intentRegistration))
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

            CoCoEventToIntentDeclarationKey.TryCreate(
                eventTypeId,
                providedIntentId,
                out CoCoEventToIntentDeclarationKey key);
            if (_eventToIntentDeclarations.TryGetValue(
                    key,
                    out ICoCoGraphEventToIntentDeclarationRegistration existingDeclaration))
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
                    out ICoCoGraphEventToIntentDeclarationRegistration existingEvent) &&
                (existingEvent.EventDomainId != domainId ||
                 existingEvent.EventPayloadType != typeof(TEvent)))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ManifestConflict,
                    "An EventTypeId must map to exactly one Domain and Event payload type.");
                return false;
            }

            Type[] authorTypes = { typeof(TEvent), typeof(TIntent) };
            if (!CoCoGraphAuthorAssemblyValidator.TryValidate(authorTypes, out diagnostic))
            {
                return false;
            }

            var registration = new CoCoGraphEventToIntentDeclarationRegistration(
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

        public bool TryRegisterOperationSection<TSection, TFactory>(
            CoCoOperationSectionId sectionId,
            CoCoOperationSectionMode mode,
            CoCoOperationSectionViewFactoryToken<TSection, TFactory> viewFactoryToken,
            out CoCoDiagnostic diagnostic)
            where TSection : class, ICoCoOperationSection
            where TFactory : ICoCoOperationSectionViewFactory<TSection>
        {
            if (!CanRegister(true, out diagnostic))
            {
                return false;
            }

            if (!viewFactoryToken.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "An Operation Section registration requires a valid view factory token.");
                return false;
            }

            if (!CoCoOperationSectionRequirement.TryCreate<TSection>(
                    sectionId,
                    mode,
                    out CoCoOperationSectionRequirement requirement,
                    out CoCoDiagnostic requirementDiagnostic))
            {
                diagnostic = Error(requirementDiagnostic.Code, requirementDiagnostic.Message);
                return false;
            }

            Type sectionType = requirement.SectionType;

            if (_operations.ContainsKey(sectionId))
            {
                ICoCoGraphOperationRegistration existing = _operations[sectionId];
                bool sameMetadata = existing.Mode == mode &&
                                    existing.SectionType == sectionType &&
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

            if (!CoCoGraphAuthorAssemblyValidator.TryValidate(
                    new[] { typeof(TSection), typeof(TFactory) },
                    out diagnostic))
            {
                return false;
            }

            _operations.Add(
                sectionId,
                new CoCoGraphOperationRegistration<TSection>(
                    requirement,
                    typeof(TFactory),
                    viewFactoryToken.SemanticFingerprint));
            AddAuthorAssemblyRoots(new[] { typeof(TSection), typeof(TFactory) });
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryRegisterStateBlock(
            CoCoStateBlockId blockId,
            CoCoStateBlockOwner owner,
            out CoCoDiagnostic diagnostic)
        {
            if (!CanRegister(blockId.IsValid, out diagnostic) ||
                owner == CoCoStateBlockOwner.None ||
                !Enum.IsDefined(typeof(CoCoStateBlockOwner), owner))
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

            _blocks.Add(blockId, new CoCoGraphStateBlockRegistration(blockId, owner));
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryRegisterStateSlot<TValue>(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoContextProjection projection,
            CoCoContextRestorePolicy restorePolicy,
            TValue defaultValue,
            ulong defaultValueFingerprint,
            CoCoCodecDescriptor codec,
            CoCoStateSlotId[] derivedDependencies,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            if (!CoCoGraphAuthorAssemblyValidator.TryValidate(
                    new[] { typeof(TValue) },
                    out diagnostic))
            {
                return false;
            }

            if (!TryRegisterStateSlotCore(
                    new CoCoGraphStateSlotRegistration<TValue>(
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

        public bool TryRegisterDerivedStateSlot<TValue, TRebuilder>(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoContextProjection projection,
            TValue defaultValue,
            ulong defaultValueFingerprint,
            CoCoCodecDescriptor codec,
            CoCoStateSlotId[] derivedDependencies,
            CoCoDerivedStateRebuilderToken<TValue, TRebuilder> rebuilderToken,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
            where TRebuilder : ICoCoDerivedStateRebuilder<TValue>
        {
            if (!rebuilderToken.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "A derived StateSlot requires a valid rebuilder token.");
                return false;
            }

            if (!CoCoGraphAuthorAssemblyValidator.TryValidate(
                    new[] { typeof(TValue), typeof(TRebuilder) },
                    out diagnostic))
            {
                return false;
            }

            if (!TryRegisterStateSlotCore(
                    new CoCoGraphStateSlotRegistration<TValue>(
                    blockId,
                    slotId,
                    projection,
                    CoCoContextRestorePolicy.Derived,
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

        public bool TryFreeze(
            out CoCoGraphDescriptorCatalog catalog,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                catalog = null;
                diagnostic = Error(CoCoDiagnosticCode.RegistryFrozen, "Descriptor Catalog may only freeze once.");
                return false;
            }

            foreach (ICoCoStateDescriptorRegistration state in _states.Values)
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

            foreach (ICoCoConditionDescriptorRegistration condition in _conditions.Values)
            {
                if (!RequirementsExist(
                        condition.IntentRequirements,
                        Array.Empty<CoCoOperationSectionId>(),
                        condition.ContextStateRequirements))
                {
                    catalog = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.MissingDescriptor,
                        "A Condition descriptor references an unregistered Manifest declaration.");
                    return false;
                }
            }

            foreach (ICoCoGraphStateSlotRegistration slot in _slots.Values)
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

            catalog = new CoCoGraphDescriptorCatalog(
                Sorted(_states),
                Sorted(_conditions),
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

        private bool TryRegisterStateSlotCore(
            ICoCoGraphStateSlotRegistration registration,
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
            CoCoIntentId[] intents,
            CoCoOperationSectionId[] operations,
            CoCoStateBlockId[] blocks)
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
                bool isValid = boxed is CoCoIntentId intent && intent.IsValid ||
                               boxed is CoCoOperationSectionId operation && operation.IsValid ||
                               boxed is CoCoStateBlockId block && block.IsValid;
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
            ICoCoGraphStateSlotRegistration left,
            ICoCoGraphStateSlotRegistration right)
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

        private static CoCoStateSlotId[] Clone(CoCoStateSlotId[] source) =>
            source == null ? Array.Empty<CoCoStateSlotId>() : (CoCoStateSlotId[])source.Clone();

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
    }

    public sealed class CoCoGraphDescriptorCatalog
    {
        private readonly Dictionary<CoCoStateDescriptorId, CoCoStateDescriptor> _states;
        private readonly Dictionary<CoCoConditionDescriptorId, CoCoConditionDescriptor> _conditions;
        private readonly Dictionary<CoCoStateDescriptorId, ICoCoStateDescriptorRegistration> _stateRegistrations;
        private readonly Dictionary<CoCoConditionDescriptorId, ICoCoConditionDescriptorRegistration>
            _conditionRegistrations;
        private readonly Dictionary<CoCoIntentId, ICoCoGraphIntentRegistration> _intents;
        private readonly Dictionary<CoCoEventToIntentDeclarationKey,
            ICoCoGraphEventToIntentDeclarationRegistration> _eventToIntentDeclarations;
        private readonly Dictionary<CoCoOperationSectionId, ICoCoGraphOperationRegistration> _operations;
        private readonly Dictionary<CoCoStateBlockId, CoCoGraphStateBlockRegistration> _blocks;
        private readonly Dictionary<CoCoStateSlotId, ICoCoGraphStateSlotRegistration> _slots;
        private readonly IReadOnlyList<string> _authorAssemblyRootNames;

        internal CoCoGraphDescriptorCatalog(
            KeyValuePair<CoCoStateDescriptorId, ICoCoStateDescriptorRegistration>[] states,
            KeyValuePair<CoCoConditionDescriptorId, ICoCoConditionDescriptorRegistration>[] conditions,
            KeyValuePair<CoCoIntentId, ICoCoGraphIntentRegistration>[] intents,
            KeyValuePair<CoCoEventToIntentDeclarationKey,
                ICoCoGraphEventToIntentDeclarationRegistration>[] eventToIntentDeclarations,
            KeyValuePair<CoCoOperationSectionId, ICoCoGraphOperationRegistration>[] operations,
            KeyValuePair<CoCoStateBlockId, CoCoGraphStateBlockRegistration>[] blocks,
            KeyValuePair<CoCoStateSlotId, ICoCoGraphStateSlotRegistration>[] slots,
            string[] authorAssemblyRootNames)
        {
            _states = new Dictionary<CoCoStateDescriptorId, CoCoStateDescriptor>(states.Length);
            _conditions = new Dictionary<CoCoConditionDescriptorId, CoCoConditionDescriptor>(conditions.Length);
            _stateRegistrations = Copy(states);
            _conditionRegistrations = Copy(conditions);
            _intents = Copy(intents);
            _eventToIntentDeclarations = Copy(eventToIntentDeclarations);
            _operations = Copy(operations);
            _blocks = Copy(blocks);
            _slots = Copy(slots);
            _authorAssemblyRootNames = Array.AsReadOnly((string[])authorAssemblyRootNames.Clone());

            ulong hash = CoCoGraphCatalogHash.OffsetBasis;
            for (int index = 0; index < states.Length; index++)
            {
                var descriptor = new CoCoStateDescriptor(states[index].Value);
                _states.Add(states[index].Key, descriptor);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.DescriptorId.High);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.DescriptorId.Low);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.Revision);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.LogicType);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.AuthoringConfigType);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.ConfigSchemaType);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.ConfigSchemaFingerprint);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.ActivationMemoryType);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.ProvidesActionProgress ? 1UL : 0UL);
                CoCoGraphCatalogHash.Add(ref hash, states[index].Value.FreezerType);
                AddIds(ref hash, states[index].Value.IntentRequirements);
                AddIds(ref hash, states[index].Value.OperationProvides);
                AddIds(ref hash, states[index].Value.ContextStateRequirements);
            }

            for (int index = 0; index < conditions.Length; index++)
            {
                var descriptor = new CoCoConditionDescriptor(conditions[index].Value);
                _conditions.Add(conditions[index].Key, descriptor);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.DescriptorId.High);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.DescriptorId.Low);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.Revision);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.ConditionType);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.AuthoringConfigType);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.ConfigSchemaType);
                CoCoGraphCatalogHash.Add(ref hash, descriptor.ConfigSchemaFingerprint);
                CoCoGraphCatalogHash.Add(ref hash, conditions[index].Value.FreezerType);
                AddIds(ref hash, conditions[index].Value.IntentRequirements);
                AddIds(ref hash, conditions[index].Value.ContextStateRequirements);
            }

            for (int index = 0; index < intents.Length; index++)
            {
                ICoCoGraphIntentRegistration registration = intents[index].Value;
                CoCoGraphCatalogHash.Add(ref hash, registration.IntentId.High);
                CoCoGraphCatalogHash.Add(ref hash, registration.IntentId.Low);
                CoCoGraphCatalogHash.Add(ref hash, registration.ValueType);
                CoCoGraphCatalogHash.Add(ref hash, registration.ReducerType);
                CoCoGraphCatalogHash.Add(ref hash, registration.FactoryType);
                CoCoGraphCatalogHash.Add(ref hash, registration.FactorySemanticFingerprint);
                CoCoGraphCatalogHash.Add(ref hash, (ulong)registration.MaxContributions);
            }

            for (int index = 0; index < eventToIntentDeclarations.Length; index++)
            {
                ICoCoGraphEventToIntentDeclarationRegistration registration =
                    eventToIntentDeclarations[index].Value;
                CoCoGraphCatalogHash.Add(ref hash, registration.EventDomainId.Value);
                CoCoGraphCatalogHash.Add(ref hash, registration.EventTypeId.High);
                CoCoGraphCatalogHash.Add(ref hash, registration.EventTypeId.Low);
                CoCoGraphCatalogHash.Add(ref hash, registration.EventPayloadType);
                CoCoGraphCatalogHash.Add(ref hash, registration.ProvidedIntentId.High);
                CoCoGraphCatalogHash.Add(ref hash, registration.ProvidedIntentId.Low);
                CoCoGraphCatalogHash.Add(ref hash, registration.ProvidedIntentType);
            }

            for (int index = 0; index < operations.Length; index++)
            {
                ICoCoGraphOperationRegistration registration = operations[index].Value;
                CoCoGraphCatalogHash.Add(ref hash, registration.SectionId.High);
                CoCoGraphCatalogHash.Add(ref hash, registration.SectionId.Low);
                CoCoGraphCatalogHash.Add(ref hash, (ulong)registration.Mode);
                CoCoGraphCatalogHash.Add(ref hash, registration.SectionType);
                CoCoGraphCatalogHash.Add(ref hash, registration.Shape.ShapeFingerprint);
                CoCoGraphCatalogHash.Add(ref hash, registration.FactoryType);
                CoCoGraphCatalogHash.Add(ref hash, registration.FactorySemanticFingerprint);
            }

            for (int index = 0; index < blocks.Length; index++)
            {
                CoCoGraphStateBlockRegistration block = blocks[index].Value;
                CoCoGraphCatalogHash.Add(ref hash, block.BlockId.High);
                CoCoGraphCatalogHash.Add(ref hash, block.BlockId.Low);
                CoCoGraphCatalogHash.Add(ref hash, (ulong)block.Owner);
            }

            for (int index = 0; index < slots.Length; index++)
            {
                ICoCoGraphStateSlotRegistration slot = slots[index].Value;
                CoCoGraphCatalogHash.Add(ref hash, slot.BlockId.High);
                CoCoGraphCatalogHash.Add(ref hash, slot.BlockId.Low);
                CoCoGraphCatalogHash.Add(ref hash, slot.SlotId.High);
                CoCoGraphCatalogHash.Add(ref hash, slot.SlotId.Low);
                CoCoGraphCatalogHash.Add(ref hash, slot.ValueType);
                CoCoGraphCatalogHash.Add(ref hash, (ulong)slot.Projection);
                CoCoGraphCatalogHash.Add(ref hash, (ulong)slot.RestorePolicy);
                CoCoGraphCatalogHash.Add(ref hash, slot.Codec.CodecId.High);
                CoCoGraphCatalogHash.Add(ref hash, slot.Codec.CodecId.Low);
                CoCoGraphCatalogHash.Add(ref hash, slot.Codec.Version);
                CoCoGraphCatalogHash.Add(ref hash, slot.DefaultValueFingerprint);
                CoCoGraphCatalogHash.Add(ref hash, slot.RebuilderType);
                CoCoGraphCatalogHash.Add(ref hash, slot.RebuilderSemanticFingerprint);
                AddIds(ref hash, slot.DerivedDependencies);
            }

            Fingerprint = hash == 0UL ? CoCoGraphCatalogHash.OffsetBasis : hash;
        }

        public ulong Fingerprint { get; }
        public int StateDescriptorCount => _states.Count;
        public int ConditionDescriptorCount => _conditions.Count;
        public bool IsFrozen => true;
        internal IReadOnlyList<string> AuthorAssemblyRootNames => _authorAssemblyRootNames;

        public bool TryGetStateDescriptor(
            CoCoStateDescriptorId descriptorId,
            out CoCoStateDescriptor descriptor) => _states.TryGetValue(descriptorId, out descriptor);

        public bool TryGetConditionDescriptor(
            CoCoConditionDescriptorId descriptorId,
            out CoCoConditionDescriptor descriptor) => _conditions.TryGetValue(descriptorId, out descriptor);

        public bool TryFreezeStateConfig(
            CoCoStateDescriptorId descriptorId,
            CoCoStateConfig source,
            out CoCoFrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            if (!_states.TryGetValue(descriptorId, out CoCoStateDescriptor descriptor))
            {
                snapshot = null;
                diagnostic = Error(CoCoDiagnosticCode.MissingDescriptor, "State descriptor is not registered.");
                return false;
            }

            return _stateRegistrations[descriptorId].TryFreeze(source, out snapshot, out diagnostic);
        }

        public bool TryFreezeConditionConfig(
            CoCoConditionDescriptorId descriptorId,
            CoCoConditionConfig source,
            out CoCoFrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            if (!_conditions.TryGetValue(descriptorId, out CoCoConditionDescriptor descriptor))
            {
                snapshot = null;
                diagnostic = Error(CoCoDiagnosticCode.MissingDescriptor, "Condition descriptor is not registered.");
                return false;
            }

            return _conditionRegistrations[descriptorId].TryFreeze(source, out snapshot, out diagnostic);
        }

        internal bool TryGetIntent(CoCoIntentId id, out ICoCoGraphIntentRegistration registration) =>
            _intents.TryGetValue(id, out registration);

        internal bool TryGetEventToIntentDeclaration(
            CoCoEventToIntentDeclarationKey key,
            out ICoCoGraphEventToIntentDeclarationRegistration registration) =>
            _eventToIntentDeclarations.TryGetValue(key, out registration);

        internal bool TryGetOperation(
            CoCoOperationSectionId id,
            out ICoCoGraphOperationRegistration registration) => _operations.TryGetValue(id, out registration);

        internal bool TryGetBlock(CoCoStateBlockId id, out CoCoGraphStateBlockRegistration registration) =>
            _blocks.TryGetValue(id, out registration);

        internal ICoCoGraphStateSlotRegistration[] GetSlots(CoCoStateBlockId blockId)
        {
            var matches = new List<ICoCoGraphStateSlotRegistration>();
            foreach (ICoCoGraphStateSlotRegistration slot in _slots.Values)
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
            CoCoStateDescriptorId descriptorId,
            CoCoFrozenConfigSnapshot snapshot) =>
            _stateRegistrations.TryGetValue(descriptorId, out ICoCoStateDescriptorRegistration registration) &&
            registration.Accepts(snapshot);

        internal bool AcceptsConditionConfig(
            CoCoConditionDescriptorId descriptorId,
            CoCoFrozenConfigSnapshot snapshot) =>
            _conditionRegistrations.TryGetValue(
                descriptorId,
                out ICoCoConditionDescriptorRegistration registration) &&
            registration.Accepts(snapshot);

        private static void AddIds<T>(ref ulong hash, T[] ids)
            where T : struct
        {
            CoCoGraphCatalogHash.Add(ref hash, (ulong)ids.Length);
            for (int index = 0; index < ids.Length; index++)
            {
                object boxed = ids[index];
                switch (boxed)
                {
                    case CoCoIntentId intent:
                        CoCoGraphCatalogHash.Add(ref hash, intent.High);
                        CoCoGraphCatalogHash.Add(ref hash, intent.Low);
                        break;
                    case CoCoOperationSectionId operation:
                        CoCoGraphCatalogHash.Add(ref hash, operation.High);
                        CoCoGraphCatalogHash.Add(ref hash, operation.Low);
                        break;
                    case CoCoStateBlockId block:
                        CoCoGraphCatalogHash.Add(ref hash, block.High);
                        CoCoGraphCatalogHash.Add(ref hash, block.Low);
                        break;
                    case CoCoStateSlotId slot:
                        CoCoGraphCatalogHash.Add(ref hash, slot.High);
                        CoCoGraphCatalogHash.Add(ref hash, slot.Low);
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
    }

    internal interface ICoCoStateDescriptorRegistration
    {
        CoCoStateDescriptorId DescriptorId { get; }
        uint Revision { get; }
        Type AuthoringConfigType { get; }
        Type ConfigSchemaType { get; }
        ulong ConfigSchemaFingerprint { get; }
        Type FreezerType { get; }
        CoCoStateRuntimeRegistration RuntimeRegistration { get; }
        CoCoIntentId[] IntentRequirements { get; }
        CoCoOperationSectionId[] OperationProvides { get; }
        CoCoStateBlockId[] ContextStateRequirements { get; }
        bool Accepts(CoCoFrozenConfigSnapshot snapshot);
        bool TryFreeze(
            CoCoStateConfig source,
            out CoCoFrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic);
    }

    internal sealed class CoCoStateDescriptorRegistration<TLogic, TAuthoringConfig, TSchema, TMemory> :
        ICoCoStateDescriptorRegistration
        where TLogic : CoCoStateLogic
        where TAuthoringConfig : CoCoStateConfig
        where TSchema : struct, ICoCoFrozenConfigSchema
        where TMemory : CoCoActivationMemory
    {
        private readonly ICoCoConfigFreezer<TAuthoringConfig, TSchema> _freezer;
        private readonly CoCoFrozenConfigSchema<TSchema> _schema;

        public CoCoStateDescriptorRegistration(
            CoCoStateDescriptorId descriptorId,
            uint revision,
            ICoCoConfigFreezer<TAuthoringConfig, TSchema> freezer,
            CoCoStateRuntimeRegistration<TLogic, TSchema, TMemory> runtimeRegistration,
            CoCoIntentId[] intentRequirements,
            CoCoOperationSectionId[] operationProvides,
            CoCoStateBlockId[] contextStateRequirements)
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

        public CoCoStateDescriptorId DescriptorId { get; }
        public uint Revision { get; }
        public Type AuthoringConfigType => typeof(TAuthoringConfig);
        public Type ConfigSchemaType => typeof(TSchema);
        public ulong ConfigSchemaFingerprint => _schema.Fingerprint;
        public Type FreezerType => _freezer.GetType();
        public CoCoStateRuntimeRegistration RuntimeRegistration { get; }
        public CoCoIntentId[] IntentRequirements { get; }
        public CoCoOperationSectionId[] OperationProvides { get; }
        public CoCoStateBlockId[] ContextStateRequirements { get; }

        public bool Accepts(CoCoFrozenConfigSnapshot snapshot) =>
            snapshot != null &&
            snapshot.MatchesSchema(_schema);

        public bool TryFreeze(
            CoCoStateConfig source,
            out CoCoFrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            snapshot = null;
            if (source == null || source.GetType() != typeof(TAuthoringConfig))
            {
                diagnostic = TypeMismatch("State authoring config does not match its descriptor.");
                return false;
            }

            CoCoFrozenConfigWriter<TSchema> writer = _schema.CreateWriter();
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

    internal interface ICoCoConditionDescriptorRegistration
    {
        CoCoConditionDescriptorId DescriptorId { get; }
        uint Revision { get; }
        Type AuthoringConfigType { get; }
        Type ConfigSchemaType { get; }
        ulong ConfigSchemaFingerprint { get; }
        Type FreezerType { get; }
        CoCoConditionRuntimeRegistration RuntimeRegistration { get; }
        CoCoIntentId[] IntentRequirements { get; }
        CoCoStateBlockId[] ContextStateRequirements { get; }
        bool Accepts(CoCoFrozenConfigSnapshot snapshot);
        bool TryFreeze(
            CoCoConditionConfig source,
            out CoCoFrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic);
    }

    internal sealed class CoCoConditionDescriptorRegistration<TCondition, TAuthoringConfig, TSchema> :
        ICoCoConditionDescriptorRegistration
        where TCondition : CoCoStateCondition
        where TAuthoringConfig : CoCoConditionConfig
        where TSchema : struct, ICoCoFrozenConfigSchema
    {
        private readonly ICoCoConfigFreezer<TAuthoringConfig, TSchema> _freezer;
        private readonly CoCoFrozenConfigSchema<TSchema> _schema;

        public CoCoConditionDescriptorRegistration(
            CoCoConditionDescriptorId descriptorId,
            uint revision,
            ICoCoConfigFreezer<TAuthoringConfig, TSchema> freezer,
            CoCoConditionRuntimeRegistration<TCondition, TSchema> runtimeRegistration,
            CoCoIntentId[] intentRequirements,
            CoCoStateBlockId[] contextStateRequirements)
        {
            DescriptorId = descriptorId;
            Revision = revision;
            _freezer = freezer;
            _schema = runtimeRegistration.ConfigSchema;
            RuntimeRegistration = runtimeRegistration;
            IntentRequirements = intentRequirements;
            ContextStateRequirements = contextStateRequirements;
        }

        public CoCoConditionDescriptorId DescriptorId { get; }
        public uint Revision { get; }
        public Type AuthoringConfigType => typeof(TAuthoringConfig);
        public Type ConfigSchemaType => typeof(TSchema);
        public ulong ConfigSchemaFingerprint => _schema.Fingerprint;
        public Type FreezerType => _freezer.GetType();
        public CoCoConditionRuntimeRegistration RuntimeRegistration { get; }
        public CoCoIntentId[] IntentRequirements { get; }
        public CoCoStateBlockId[] ContextStateRequirements { get; }

        public bool Accepts(CoCoFrozenConfigSnapshot snapshot) =>
            snapshot != null &&
            snapshot.MatchesSchema(_schema);

        public bool TryFreeze(
            CoCoConditionConfig source,
            out CoCoFrozenConfigSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            snapshot = null;
            if (source == null || source.GetType() != typeof(TAuthoringConfig))
            {
                diagnostic = TypeMismatch("Condition authoring config does not match its descriptor.");
                return false;
            }

            CoCoFrozenConfigWriter<TSchema> writer = _schema.CreateWriter();
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
                        diagnostic = TypeMismatch("Condition config freezer failed without a diagnostic.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                writer.SealWithoutSnapshot();
                diagnostic = TypeMismatch("Condition config freezer threw while creating a frozen snapshot.");
                return false;
            }

            if (!diagnostic.IsNone)
            {
                writer.SealWithoutSnapshot();
                diagnostic = TypeMismatch("Condition config freezer succeeded with a diagnostic.");
                return false;
            }

            if (!writer.TrySeal(out snapshot, out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = TypeMismatch("Condition config freezer returned an invalid snapshot.");
                }

                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic TypeMismatch(string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.State, CoCoDiagnosticCode.InvalidFrozenConfig, message);
    }

    internal interface ICoCoGraphIntentRegistration
    {
        CoCoIntentId IntentId { get; }
        Type ValueType { get; }
        Type ReducerType { get; }
        Type FactoryType { get; }
        ulong FactorySemanticFingerprint { get; }
        int MaxContributions { get; }
    }

    internal sealed class CoCoGraphIntentRegistration<TIntent, TReducer> : ICoCoGraphIntentRegistration
        where TIntent : unmanaged
        where TReducer : unmanaged, ICoCoIntentReducer<TIntent>
    {
        public CoCoGraphIntentRegistration(
            CoCoIntentId intentId,
            int maxContributions,
            Type factoryType,
            ulong factorySemanticFingerprint)
        {
            IntentId = intentId;
            MaxContributions = maxContributions;
            FactoryType = factoryType;
            FactorySemanticFingerprint = factorySemanticFingerprint;
        }

        public CoCoIntentId IntentId { get; }
        public Type ValueType => typeof(TIntent);
        public Type ReducerType => typeof(TReducer);
        public Type FactoryType { get; }
        public ulong FactorySemanticFingerprint { get; }
        public int MaxContributions { get; }
    }

    internal interface ICoCoGraphOperationRegistration
    {
        CoCoOperationSectionId SectionId { get; }
        CoCoOperationSectionMode Mode { get; }
        Type SectionType { get; }
        CoCoOperationSectionShape Shape { get; }
        Type FactoryType { get; }
        ulong FactorySemanticFingerprint { get; }
    }

    internal sealed class CoCoGraphOperationRegistration<TSection> : ICoCoGraphOperationRegistration
        where TSection : class, ICoCoOperationSection
    {
        public CoCoGraphOperationRegistration(
            CoCoOperationSectionRequirement requirement,
            Type factoryType,
            ulong factorySemanticFingerprint)
        {
            Requirement = requirement;
            FactoryType = factoryType;
            FactorySemanticFingerprint = factorySemanticFingerprint;
        }

        public CoCoOperationSectionRequirement Requirement { get; }
        public CoCoOperationSectionId SectionId => Requirement.SectionId;
        public CoCoOperationSectionMode Mode => Requirement.Mode;
        public Type SectionType => Requirement.SectionType;
        public CoCoOperationSectionShape Shape => Requirement.Shape;
        public Type FactoryType { get; }
        public ulong FactorySemanticFingerprint { get; }
    }

    internal sealed class CoCoGraphStateBlockRegistration
    {
        public CoCoGraphStateBlockRegistration(CoCoStateBlockId blockId, CoCoStateBlockOwner owner)
        {
            BlockId = blockId;
            Owner = owner;
        }

        public CoCoStateBlockId BlockId { get; }
        public CoCoStateBlockOwner Owner { get; }
    }

    internal interface ICoCoGraphStateSlotRegistration
    {
        CoCoStateBlockId BlockId { get; }
        CoCoStateSlotId SlotId { get; }
        Type ValueType { get; }
        CoCoContextProjection Projection { get; }
        CoCoContextRestorePolicy RestorePolicy { get; }
        CoCoCodecDescriptor Codec { get; }
        CoCoStateSlotId[] DerivedDependencies { get; }
        ulong DefaultValueFingerprint { get; }
        Type RebuilderType { get; }
        ulong RebuilderSemanticFingerprint { get; }
        bool IsBasicallyValid { get; }
    }

    internal sealed class CoCoGraphStateSlotRegistration<TValue> : ICoCoGraphStateSlotRegistration
        where TValue : unmanaged
    {
        public CoCoGraphStateSlotRegistration(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoContextProjection projection,
            CoCoContextRestorePolicy restorePolicy,
            TValue defaultValue,
            ulong defaultValueFingerprint,
            CoCoCodecDescriptor codec,
            CoCoStateSlotId[] derivedDependencies,
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

        public CoCoStateBlockId BlockId { get; }
        public CoCoStateSlotId SlotId { get; }
        public Type ValueType => typeof(TValue);
        public CoCoContextProjection Projection { get; }
        public CoCoContextRestorePolicy RestorePolicy { get; }
        public CoCoCodecDescriptor Codec { get; }
        public CoCoStateSlotId[] DerivedDependencies { get; }
        public ulong DefaultValueFingerprint { get; }
        public Type RebuilderType { get; }
        public ulong RebuilderSemanticFingerprint { get; }
        public bool IsBasicallyValid
        {
            get
            {
                const CoCoContextProjection knownProjection =
                    CoCoContextProjection.Temporal | CoCoContextProjection.Durable;
                return BlockId.IsValid &&
                       SlotId.IsValid &&
                       CoCoStateFlowTypeRules.IsReferenceFreeValueType(typeof(TValue)) &&
                       DefaultValueFingerprint != 0UL &&
                       Codec.IsValid &&
                       (Projection & ~knownProjection) == 0 &&
                       RestorePolicy != CoCoContextRestorePolicy.None &&
                       Enum.IsDefined(typeof(CoCoContextRestorePolicy), RestorePolicy) &&
                       (RestorePolicy == CoCoContextRestorePolicy.Derived
                           ? RebuilderType != null &&
                             RebuilderSemanticFingerprint != 0UL &&
                             DerivedDependencies.Length > 0
                           : RebuilderType == null &&
                             RebuilderSemanticFingerprint == 0UL &&
                             DerivedDependencies.Length == 0);
            }
        }
    }

    internal static class CoCoGraphAuthorAssemblyValidator
    {
        private static readonly string[] ForbiddenExactNames =
        {
            "CoCoFlow.Runtime.Core",
            "CoCoFlow.Runtime.Core.StateGraphAuthoring"
        };

        private static readonly string[] ForbiddenPrefixes =
        {
            "Unity.",
            "UnityEngine",
            "UnityEditor",
            "CoCoFlow.Editor",
            "CoCoFlow.Runtime.Gameplay",
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
                            "Graph authoring assemblies cannot reference Unity, Editor, Gameplay, or Module assemblies.");
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

    internal static class CoCoGraphConfigTypeValidator
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

    internal static class CoCoGraphCatalogHash
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

            Add(ref hash, type.Assembly.GetName().Name);
            Add(ref hash, type.FullName ?? string.Empty);
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

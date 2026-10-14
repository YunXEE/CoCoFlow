using System;
using System.Collections.Generic;
using System.Linq;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>Builds the standard catalog and binds module-declared Intents,
    /// Operations and graph state to each Host's explicit scene references.</summary>
    public sealed partial class CoCoStandardBindingProvider :
        ICoCoStateGraphProjectBindingProvider
    {
        private static readonly StatelessMemoryBinding MemoryBinding =
            new StatelessMemoryBinding();

        private readonly CoCoGraphDescriptorCatalog _catalog;
        private readonly Dictionary<CoCoIntentId, ICoCoStandardIntentRegistration> _intentRegistrations;
        internal IReadOnlyList<System.Reflection.Assembly> Assemblies { get; private set; }
        private readonly Dictionary<CoCoStateDescriptorId, Type> _stateTypes;
        private readonly Dictionary<CoCoOperationSectionId, ICoCoStandardOperatorRegistrar>
            _operationBinders;
        private readonly Dictionary<CoCoStateBlockId, ICoCoStandardOperatorRegistrar>
            _contextBinders;

        private CoCoStandardBindingProvider(
            CoCoGraphDescriptorCatalog catalog,
            Dictionary<CoCoStateDescriptorId, Type> stateTypes,
            Dictionary<CoCoOperationSectionId, ICoCoStandardOperatorRegistrar>
                operationBinders,
            Dictionary<CoCoStateBlockId, ICoCoStandardOperatorRegistrar>
                contextBinders,
            Dictionary<CoCoIntentId, ICoCoStandardIntentRegistration> intentRegistrations)
        {
            _catalog = catalog;
            _stateTypes = stateTypes;
            _operationBinders = operationBinders;
            _contextBinders = contextBinders;
            _intentRegistrations = intentRegistrations;
        }

        public CoCoGraphDescriptorCatalog Catalog => _catalog;

        /// <summary>
        /// Scans the assembly of the marker type (put the marker next to your
        /// state scripts, or call the scan overload directly) for
        /// CoCoStateAttribute classes and builds the standard catalog.
        /// Registered Intent payloads are wired automatically.
        /// </summary>
        public static CoCoStandardBindingProvider BuildForAssemblyOf<TMarker>()
        {
            return Build(new[] { typeof(TMarker).Assembly });
        }

        public static CoCoStandardBindingProvider Build(
            IReadOnlyList<System.Reflection.Assembly> assemblies) => Build(assemblies, null);

        internal static CoCoStandardBindingProvider Build(
            IReadOnlyList<System.Reflection.Assembly> assemblies,
            IReadOnlyDictionary<CoCoStateDescriptorId, Type> aliases)
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            var operationIdsByType =
                new Dictionary<Type, CoCoOperationSectionId>();
            var registrarsBySectionType =
                new Dictionary<Type, ICoCoStandardOperatorRegistrar>();
            var operationBinders =
                new Dictionary<CoCoOperationSectionId, ICoCoStandardOperatorRegistrar>();
            var contextBinders =
                new Dictionary<CoCoStateBlockId, ICoCoStandardOperatorRegistrar>();
            var seenRegistrars = new HashSet<Type>();

            // Operator modules carry their own registrations. Package module
            // assemblies are added to the caller's graph-author assemblies,
            // then sorted so discovery is independent of load order.
            var registrarAssemblies =
                new List<System.Reflection.Assembly>(assemblies);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.StartsWith("CoCoFlow.Runtime.Modules", StringComparison.Ordinal) &&
                    !registrarAssemblies.Contains(assembly))
                {
                    registrarAssemblies.Add(assembly);
                }
            }

            registrarAssemblies.Sort((left, right) => string.CompareOrdinal(
                left.FullName,
                right.FullName));

            var intentRegistrations = RegisterIntents(registrarAssemblies, builder);
            var intentsByType = intentRegistrations.Values.ToDictionary(item => item.IntentType);

            foreach (System.Reflection.Assembly assembly in registrarAssemblies)
            {
                foreach (Type type in GetLoadableTypes(assembly))
                {
                    if (type == null || !type.IsClass)
                    {
                        continue;
                    }

                    foreach (CoCoOperatorRegistrationAttribute registration in
                             type.GetCustomAttributes(
                                 typeof(CoCoOperatorRegistrationAttribute),
                                 false))
                    {
                        if (!seenRegistrars.Add(registration.RegistrarType))
                        {
                            continue;
                        }

                        var registrar = Activator.CreateInstance(
                            registration.RegistrarType) as
                            ICoCoStandardOperatorRegistrar;
                        if (registrar == null || !registrar.RegisterCatalog(builder))
                        {
                            throw new InvalidOperationException(
                                "Operator registration failed: " +
                                registration.RegistrarType.Name);
                        }

                        if (registrar.Operations == null ||
                            registrar.Operations.Count == 0 ||
                            registrar.ContextBlocks == null)
                        {
                            throw new InvalidOperationException(
                                "Operator registrar must declare Operations and Context blocks: " +
                                registration.RegistrarType.Name);
                        }

                        for (int operationIndex = 0;
                             operationIndex < registrar.Operations.Count;
                             operationIndex++)
                        {
                            CoCoStandardOperationRegistration operation =
                                registrar.Operations[operationIndex];
                            if (!operation.IsValid ||
                                operationIdsByType.ContainsKey(operation.SectionType) ||
                                operationBinders.ContainsKey(operation.SectionId))
                            {
                                throw new InvalidOperationException(
                                    "Operator Sections must have unique valid types and ids: " +
                                    registration.RegistrarType.Name);
                            }

                            operationIdsByType.Add(
                                operation.SectionType,
                                operation.SectionId);
                            registrarsBySectionType.Add(
                                operation.SectionType,
                                registrar);
                            operationBinders.Add(operation.SectionId, registrar);
                        }

                        for (int blockIndex = 0;
                             blockIndex < registrar.ContextBlocks.Count;
                             blockIndex++)
                        {
                            CoCoStateBlockId blockId =
                                registrar.ContextBlocks[blockIndex];
                            if (!blockId.IsValid || contextBinders.ContainsKey(blockId))
                            {
                                throw new InvalidOperationException(
                                    "Operator Context blocks must have unique valid ids: " +
                                    registration.RegistrarType.Name);
                            }

                            contextBinders.Add(blockId, registrar);
                        }
                    }
                }
            }

            int states = 0;
            var stateTypes = new Dictionary<CoCoStateDescriptorId, Type>();
            foreach (System.Reflection.Assembly assembly in assemblies)
            {
                foreach (Type type in GetLoadableTypes(assembly).SelectMany(type => aliases == null ?
                             new[] { type } : aliases.Values.Where(value => value == type)))
                {
                    if (type == null ||
                        !type.IsClass ||
                        type.IsAbstract ||
                        !typeof(CoCoStateLogic).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    CoCoStateAttribute attribute =
                        (CoCoStateAttribute)Attribute.GetCustomAttribute(
                            type,
                            typeof(CoCoStateAttribute));
                    if (attribute == null)
                    {
                        continue;
                    }

                    if (!StandardDescriptors.TryCreate(type, attribute.Name,
                            out CoCoStateDescriptorId descriptorId))
                    {
                        throw new InvalidOperationException(
                            "Standard descriptor id derivation failed for " +
                            type.FullName);
                    }

                    if (aliases != null)
                        descriptorId = aliases.First(pair => pair.Value == type && !stateTypes.ContainsKey(pair.Key)).Key;
                    var intentDeclarations = new List<CoCoIntentConsumeAttribute>(Array.ConvertAll(
                        type.GetCustomAttributes(typeof(CoCoIntentConsumeAttribute), false),
                        value => (CoCoIntentConsumeAttribute)value));
                    var operationDeclarations = new List<CoCoOperationProvideAttribute>(Array.ConvertAll(
                        type.GetCustomAttributes(typeof(CoCoOperationProvideAttribute), false),
                        value => (CoCoOperationProvideAttribute)value));
                    foreach (var property in CoCoStatePropertyBindings.Properties(type))
                    {
                        if (property.IsDefined(typeof(IntentAttribute), false))
                            intentDeclarations.Add(new CoCoIntentConsumeAttribute(property.PropertyType));
                        else operationDeclarations.Add(new CoCoOperationProvideAttribute(property.PropertyType));
                    }

                    var requiredIntents = new HashSet<CoCoIntentId>();
                    foreach (CoCoIntentConsumeAttribute consume in intentDeclarations)
                    {
                        if (!intentsByType.TryGetValue(consume.IntentType, out var registration))
                            throw new InvalidOperationException("No standard Intent registration for " +
                                consume.IntentType.Name + " in State " + type.Name + ".");
                        requiredIntents.Add(registration.IntentId);
                    }
                    CoCoIntentId[] consumedIntents = requiredIntents
                        .OrderBy(id => id.High).ThenBy(id => id.Low).ToArray();

                    var stateProvides = new HashSet<CoCoOperationSectionId>();
                    var stateContextBlocks = new HashSet<CoCoStateBlockId>
                    {
                        StandardGraphState.BlockFor(descriptorId),
                    };
                    foreach (CoCoOperationProvideAttribute provide in
                             operationDeclarations)
                    {
                        if (operationIdsByType.TryGetValue(
                                provide.SectionType,
                                out CoCoOperationSectionId providedId) &&
                            registrarsBySectionType.TryGetValue(
                                provide.SectionType,
                                out ICoCoStandardOperatorRegistrar owner))
                        {
                            stateProvides.Add(providedId);
                            for (int blockIndex = 0;
                                 blockIndex < owner.ContextBlocks.Count;
                                 blockIndex++)
                            {
                                stateContextBlocks.Add(owner.ContextBlocks[blockIndex]);
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                "State " + type.Name + " provides Operation " +
                                provide.SectionType.Name +
                                " but no Operator registers it.");
                        }
                    }

                    CoCoOperationSectionId[] providedSections =
                        ToSortedArray(stateProvides);
                    CoCoStateBlockId[] contextBlocks =
                        ToSortedArray(stateContextBlocks);
                    if (!TryRegisterStandardState(
                            builder,
                            type,
                            descriptorId,
                            consumedIntents,
                            providedSections,
                            contextBlocks))
                    {
                        throw new InvalidOperationException(
                            "Standard catalog state registration failed for " +
                            type.Name);
                    }

                    if (stateTypes.ContainsKey(descriptorId))
                    {
                        throw new InvalidOperationException(
                            "Standard State descriptor ids must be unique: " +
                            type.FullName);
                    }

                    stateTypes.Add(descriptorId, type);
                    states++;
                }
            }

            if (states == 0)
            {
                throw new InvalidOperationException(
                    "CoCoStandardBindingProvider found no CoCoState-attributed " +
                    "state logic classes in the scanned assemblies.");
            }

            if (!builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic freezeDiagnostic))
            {
                throw new InvalidOperationException(
                    "Standard catalog freeze failed: " + freezeDiagnostic.Message);
            }

            return new CoCoStandardBindingProvider(
                catalog,
                stateTypes,
                operationBinders,
                contextBinders, intentRegistrations) { Assemblies = assemblies.ToArray() };
        }

        private static Dictionary<CoCoIntentId, ICoCoStandardIntentRegistration> RegisterIntents(
            IEnumerable<System.Reflection.Assembly> assemblies, CoCoGraphDescriptorCatalogBuilder builder)
        {
            var registrations = new Dictionary<CoCoIntentId, ICoCoStandardIntentRegistration>();
            var payloadTypes = new HashSet<Type>();
            var registrarTypes = new HashSet<Type>();
            void Add(ICoCoStandardIntentRegistration registration)
            {
                if (registration == null || !registration.IntentId.IsValid ||
                    registration.IntentType == null || !payloadTypes.Add(registration.IntentType) ||
                    registrations.ContainsKey(registration.IntentId) || !registration.RegisterCatalog(builder))
                    throw new InvalidOperationException("Intent registrations require unique valid types and ids.");
                registrations.Add(registration.IntentId, registration);
            }
            Add(new CoCoStandardIntentRegistration<InputIntentData, RawInputPassThroughReducer,
                RawInputReducerFactory>(RawIntents.Player, 2, 1UL));
            foreach (var assembly in assemblies)
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type == null || !type.IsClass) continue;
                foreach (CoCoIntentRegistrationAttribute attribute in
                         type.GetCustomAttributes(typeof(CoCoIntentRegistrationAttribute), false))
                    if (registrarTypes.Add(attribute.RegistrationType))
                        Add(Activator.CreateInstance(attribute.RegistrationType) as ICoCoStandardIntentRegistration);
            }
            return registrations;
        }

        public bool TryConfigure(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryBindRequiredIntent(bindingBuilder, out diagnostic) ||
                !TryBindRequiredOperations(bindingBuilder, out diagnostic) ||
                !TryBindRequiredContext(bindingBuilder, out diagnostic))
            {
                return false;
            }

            return TryBindStandardFactories(bindingBuilder, out diagnostic);
        }

        private bool TryBindRequiredIntent(
            CoCoStateGraphHostBindingBuilder bindingBuilder, out CoCoDiagnostic diagnostic)
        {
            var requirements = bindingBuilder.Graph.IntentRequirements.Requirements;
            diagnostic = CoCoDiagnostic.None;
            if (requirements.Count == 0) return true;
            var bindings = new ICoCoStandardIntentSourceBinding[requirements.Count];
            for (int i = 0; i < requirements.Count; i++)
            {
                var requirement = requirements[i];
                if (!_intentRegistrations.TryGetValue(requirement.IntentId, out var registration) ||
                    registration.IntentType != requirement.ValueType)
                {
                    diagnostic = RegistryError(CoCoDiagnosticCode.MissingIntentReducer,
                        "Compiled Intent has no matching standard registration.");
                    return false;
                }
                if (!registration.TryRegisterHost(bindingBuilder, out bindings[i], out diagnostic)) return false;
            }
            if (!bindingBuilder.TryBeginIntentBindings(out diagnostic)) return false;
            var matched = new bool[bindings.Length];
            var sources = bindingBuilder.IntentSourceComponents;
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                int match = -1;
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!bindings[i].Accepts(sources[sourceIndex])) continue;
                    if (match >= 0)
                    {
                        diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                            "One source matches multiple required Intent types.");
                        return false;
                    }
                    match = i;
                }
                if (match < 0 || matched[match])
                {
                    diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                        "Intent sources must each match one distinct required payload type.");
                    return false;
                }
                if (!bindings[match].TryBind(bindingBuilder, sourceIndex, out diagnostic)) return false;
                matched[match] = true;
            }
            if (matched.Any(value => !value))
            {
                diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                    "Every required Intent needs exactly one explicit Host source.");
                return false;
            }
            return true;
        }

        private bool TryBindRequiredOperations(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            IReadOnlyList<CoCoGraphOperationProvision> operations =
                bindingBuilder.Graph.OperationProvides.Provides;
            for (int index = 0; index < operations.Count; index++)
            {
                CoCoOperationSectionId sectionId = operations[index].SectionId;
                if (!_operationBinders.TryGetValue(
                        sectionId,
                        out ICoCoStandardOperatorRegistrar registrar))
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.MissingOperationBinding,
                        "No standard Operator owns compiled Operation Section " +
                        sectionId + ".");
                    return false;
                }

                if (!registrar.TryBindOperation(
                        sectionId,
                        bindingBuilder,
                        out diagnostic))
                {
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryBindRequiredContext(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            IReadOnlyList<CoCoContextStateBlockRequirement> blocks =
                bindingBuilder.Graph.ContextStateRequirements.Blocks;
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                CoCoContextStateBlockRequirement block = blocks[blockIndex];
                if (TryFindStandardGraphState(
                        bindingBuilder,
                        block.BlockId,
                        out CoCoCompiledStateLayer graphLayer,
                        out CoCoCompiledState graphState))
                {
                    if (!TryBindStandardGraphBlock(
                            bindingBuilder,
                            block,
                            graphLayer,
                            graphState,
                            out diagnostic))
                    {
                        return false;
                    }

                    continue;
                }

                for (int slotIndex = 0;
                     slotIndex < block.Slots.Count;
                     slotIndex++)
                {
                    CoCoContextStateSlotRequirement slot = block.Slots[slotIndex];
                    if (!_contextBinders.TryGetValue(
                            block.BlockId,
                            out ICoCoStandardOperatorRegistrar registrar))
                    {
                        diagnostic = RegistryError(
                            CoCoDiagnosticCode.MissingDescriptor,
                            "No standard Operator owns compiled Context block " +
                            block.BlockId + ".");
                        return false;
                    }

                    if (!registrar.TryBindContextSlot(
                            block.BlockId,
                            slot.SlotId,
                            bindingBuilder,
                            out diagnostic))
                    {
                        return false;
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryFindStandardGraphState(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            CoCoStateBlockId blockId,
            out CoCoCompiledStateLayer ownerLayer,
            out CoCoCompiledState ownerState)
        {
            for (int layerIndex = 0;
                 layerIndex < bindingBuilder.Graph.Layers.Count;
                 layerIndex++)
            {
                CoCoCompiledStateLayer layer =
                    bindingBuilder.Graph.Layers[layerIndex];
                for (int stateIndex = 0;
                     stateIndex < layer.States.Count;
                     stateIndex++)
                {
                    CoCoCompiledState state = layer.States[stateIndex];
                    if (StandardGraphState.BlockFor(
                            state.Descriptor.DescriptorId) == blockId)
                    {
                        ownerLayer = layer;
                        ownerState = state;
                        return true;
                    }
                }
            }

            ownerLayer = null;
            ownerState = null;
            return false;
        }

        private static bool TryBindStandardGraphBlock(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            CoCoContextStateBlockRequirement block,
            CoCoCompiledStateLayer layer,
            CoCoCompiledState state,
            out CoCoDiagnostic diagnostic)
        {
            if (HasFields(state.Descriptor.LogicType))
            {
                object[] args = { bindingBuilder, layer, state, null };
                bool result = (bool)InvokeFields(nameof(BindFields), state.Descriptor.LogicType, args);
                diagnostic = (CoCoDiagnostic)args[3];
                return result;
            }
            CoCoStateSlotId slotId = StandardGraphState.SlotFor(
                state.Descriptor.DescriptorId);
            if (block.Slots.Count != 1 || block.Slots[0].SlotId != slotId)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.InvalidStateSlot,
                    "Standard graph-state block must contain its one matching State slot.");
                return false;
            }

            // The trusted default must equal the runtime's initial graph
            // authority for this state: the layer's initial state starts
            // active (activation 1, enter pending), every other state
            // starts inactive (no activation, zero clocks). A hardcoded
            // active record only matches single-state graphs.
            ulong initialActivation = InitialActivation(bindingBuilder.Graph, state);
            bool isInitialState = initialActivation != 0;
            CoCoGraphStateRecord<int> defaultRecord;
            if (isInitialState)
            {
                if (!CoCoActivationId.TryCreate(
                        initialActivation,
                        out CoCoActivationId activationId) ||
                    !CoCoGraphStateRecord<int>.TryCreate(
                        layer.LayerId,
                        state.StateId,
                        true,
                        activationId,
                        0d,
                        0d,
                        true,
                        StatelessMemory.Fingerprint,
                        0,
                        out defaultRecord))
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.InvalidStateSlot,
                        "Standard graph-state block could not build its initial-state default record.");
                    return false;
                }
            }
            else if (!CoCoGraphStateRecord<int>.TryCreateInactive(
                         layer.LayerId,
                         state.StateId,
                         StatelessMemory.Fingerprint,
                         0,
                         out defaultRecord))
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.InvalidStateSlot,
                    "Standard graph-state block could not build its inactive-state default record.");
                return false;
            }

            return bindingBuilder.TryBindGraphStateSlot<
                StatelessMemory,
                int,
                StatelessMemoryBinding>(
                layer.LayerId,
                state.StateId,
                block.BlockId,
                slotId,
                defaultRecord,
                StatelessMemory.Fingerprint,
                MemoryBinding,
                out diagnostic);
        }

        private static bool TryRegisterStandardState(
            CoCoGraphDescriptorCatalogBuilder builder,
            Type type,
            CoCoStateDescriptorId descriptorId,
            CoCoIntentId[] consumedIntents,
            CoCoOperationSectionId[] providedSections,
            CoCoStateBlockId[] contextBlocks)
        {
            if (HasFields(type))
                return (bool)InvokeFields(nameof(RegisterFields), type, builder, descriptorId,
                    consumedIntents, providedSections, contextBlocks);
            System.Reflection.MethodInfo helper = typeof(CoCoStandardBindingProvider)
                .GetMethod(
                    "RegisterStateTyped",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)
                ?.MakeGenericMethod(type);
            if (helper == null)
            {
                return false;
            }

            object result = helper.Invoke(
                null,
                new object[]
                {
                    builder,
                    descriptorId,
                    consumedIntents,
                    providedSections,
                    contextBlocks
                });
            return result is true;
        }

        private static bool RegisterStateTyped<TLogic>(
            CoCoGraphDescriptorCatalogBuilder builder,
            CoCoStateDescriptorId descriptorId,
            CoCoIntentId[] consumedIntents,
            CoCoOperationSectionId[] providedSections,
            CoCoStateBlockId[] contextBlocks)
            where TLogic : CoCoStateLogic, new()
        {
            CoCoStateBlockId graphBlockId =
                StandardGraphState.BlockFor(descriptorId);
            if (!builder.TryRegisterStateBlock(
                    graphBlockId,
                    CoCoStateBlockOwner.Graph,
                    out CoCoDiagnostic blockDiagnostic))
            {
                UnityEngine.Debug.LogError(
                    "[StandardBinding] StateBlock registration failed for " +
                    typeof(TLogic).Name + ": " + blockDiagnostic.Message);
                return false;
            }

            bool ok = builder.TryRegisterState<TLogic, EmptyStateConfig, EmptyConfigSchema, StatelessMemory>(
                descriptorId,
                1U,
                new EmptyStateConfig.Freezer(),
                new CoCoStateRuntimeRegistration<TLogic, EmptyConfigSchema, StatelessMemory>(
                    EmptySchemas.State),
                consumedIntents,
                providedSections,
                contextBlocks,
                out CoCoDiagnostic diagnostic);
            if (!ok)
            {
                UnityEngine.Debug.LogError(
                    "[StandardBinding] TryRegisterState<" + typeof(TLogic).Name +
                    "> failed: " + diagnostic.Message);
                return false;
            }

            if (!builder.TryRegisterStateSlot<CoCoGraphStateRecord<int>>(
                    graphBlockId,
                    StandardGraphState.SlotFor(descriptorId),
                    CoCoContextProjection.Temporal |
                    CoCoContextProjection.Durable,
                    CoCoContextRestorePolicy.Stored,
                    default(CoCoGraphStateRecord<int>),
                    StatelessMemory.Fingerprint,
                    default,
                    null,
                    out CoCoDiagnostic slotDiagnostic))
            {
                UnityEngine.Debug.LogError(
                    "[StandardBinding] StateSlot registration failed for " +
                    typeof(TLogic).Name + ": " + slotDiagnostic.Message);
                return false;
            }

            return true;
        }

        private bool TryBindStandardFactories(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            var bound = new HashSet<CoCoStateDescriptorId>();
            for (int layerIndex = 0;
                 layerIndex < bindingBuilder.Graph.Layers.Count;
                 layerIndex++)
            {
                CoCoCompiledStateLayer layer =
                    bindingBuilder.Graph.Layers[layerIndex];
                for (int stateIndex = 0;
                     stateIndex < layer.States.Count;
                     stateIndex++)
                {
                    CoCoStateDescriptorId descriptorId =
                        layer.States[stateIndex].Descriptor.DescriptorId;
                    if (!bound.Add(descriptorId))
                    {
                        continue;
                    }

                    if (!_stateTypes.TryGetValue(
                            descriptorId,
                            out Type logicType) ||
                        !TryCreateStandardFactory(
                            logicType,
                            out ICoCoStateRuntimeFactory factory))
                    {
                        diagnostic = RegistryError(
                            CoCoDiagnosticCode.MissingDescriptor,
                            "Standard factory creation failed for descriptor " +
                            descriptorId + ".");
                        return false;
                    }

                    if (!bindingBuilder.TryBindState(
                            descriptorId,
                            factory,
                            out diagnostic))
                    {
                        return false;
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryCreateStandardFactory(
            Type logicType,
            out ICoCoStateRuntimeFactory factory)
        {
            if (HasFields(logicType))
            {
                factory = (ICoCoStateRuntimeFactory)InvokeFields(nameof(FieldFactory), logicType);
                return true;
            }
            factory = (ICoCoStateRuntimeFactory)typeof(CoCoStandardBindingProvider)
                .GetMethod(
                    "CreateFactoryTyped",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)
                ?.MakeGenericMethod(logicType)
                ?.Invoke(null, null);
            return factory != null;
        }

        private static CoCoStateRuntimeFactory<TLogic, StatelessMemory>
            CreateFactoryTyped<TLogic>()
            where TLogic : CoCoStateLogic, ICoCoStateUpdate, new()
        {
            return new CoCoStateRuntimeFactory<TLogic, StatelessMemory>(
                context => new TLogic(),
                () => new StatelessMemory(),
                (source, destination) => { },
                memory => { },
                memory => StatelessMemory.Fingerprint);
        }

        private static Type[] GetLoadableTypes(
            System.Reflection.Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException loaded)
            {
                types = loaded.Types;
            }

            Array.Sort(types, CompareTypes);
            return types;
        }

        private static int CompareTypes(Type left, Type right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return string.CompareOrdinal(left.FullName, right.FullName);
        }

        private static CoCoOperationSectionId[] ToSortedArray(
            HashSet<CoCoOperationSectionId> source)
        {
            var values = new CoCoOperationSectionId[source.Count];
            source.CopyTo(values);
            Array.Sort(values, CompareOperationIds);
            return values;
        }

        private static CoCoStateBlockId[] ToSortedArray(
            HashSet<CoCoStateBlockId> source)
        {
            var values = new CoCoStateBlockId[source.Count];
            source.CopyTo(values);
            Array.Sort(values, CompareBlockIds);
            return values;
        }

        private static int CompareOperationIds(
            CoCoOperationSectionId left,
            CoCoOperationSectionId right)
        {
            int high = left.High.CompareTo(right.High);
            return high != 0 ? high : left.Low.CompareTo(right.Low);
        }

        private static int CompareBlockIds(
            CoCoStateBlockId left,
            CoCoStateBlockId right)
        {
            int high = left.High.CompareTo(right.High);
            return high != 0 ? high : left.Low.CompareTo(right.Low);
        }

        private static CoCoDiagnostic RegistryError(
            CoCoDiagnosticCode code,
            string message)
        {
            return CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Registry,
                code,
                message);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        private Dictionary<CoCoConditionDescriptorId, ICoCoConditionRuntimeFactory> _conditionFactories;
        private readonly Dictionary<CoCoOperationSectionId, CoCoStandardOperatorDefinition>
            _operationBinders;
        private readonly Dictionary<CoCoStateBlockId, CoCoStandardOperatorDefinition>
            _contextBinders;

        private CoCoStandardBindingProvider(
            CoCoGraphDescriptorCatalog catalog,
            Dictionary<CoCoStateDescriptorId, Type> stateTypes,
            Dictionary<CoCoOperationSectionId, CoCoStandardOperatorDefinition>
                operationBinders,
            Dictionary<CoCoStateBlockId, CoCoStandardOperatorDefinition>
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
            var operatorsBySectionType =
                new Dictionary<Type, CoCoStandardOperatorDefinition>();
            var operationBinders =
                new Dictionary<CoCoOperationSectionId, CoCoStandardOperatorDefinition>();
            var contextBinders =
                new Dictionary<CoCoStateBlockId, CoCoStandardOperatorDefinition>();

            // Operator modules carry their own registrations. Package module
            // assemblies are added to the caller's graph-author assemblies,
            // then sorted so discovery is independent of load order.
            var moduleAssemblies =
                new List<System.Reflection.Assembly>(assemblies);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.StartsWith("CoCoFlow.Runtime.Modules", StringComparison.Ordinal) &&
                    !moduleAssemblies.Contains(assembly))
                {
                    moduleAssemblies.Add(assembly);
                }
            }

            moduleAssemblies.Sort((left, right) => string.CompareOrdinal(
                left.FullName,
                right.FullName));

            var intentRegistrations = RegisterIntents(moduleAssemblies, builder);
            var intentsByType = intentRegistrations.Values.ToDictionary(item => item.IntentType);

            foreach (System.Reflection.Assembly assembly in moduleAssemblies)
            {
                foreach (Type type in GetLoadableTypes(assembly))
                {
                    if (type == null || !type.IsClass || !typeof(ICoCoOperator).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.Public |
                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!property.IsDefined(typeof(CoCoOperatorDefinitionAttribute), false)) continue;
                        if (property.PropertyType != typeof(CoCoStandardOperatorDefinition) || property.GetMethod == null ||
                            !(property.GetValue(null) is CoCoStandardOperatorDefinition definition) ||
                            definition.Descriptor.OperatorType != type)
                            throw new InvalidOperationException($"Invalid Operator definition {type.FullName}.{property.Name}.");

                        definition.RegisterCatalog(builder);
                        foreach (var operation in definition.Operations)
                        {
                            if (!operation.IsValid || operationIdsByType.ContainsKey(operation.SectionType) ||
                                operationBinders.ContainsKey(operation.SectionId))
                                throw new InvalidOperationException($"Duplicate or invalid Operation Section {operation.SectionType.FullName}.");
                            operationIdsByType.Add(operation.SectionType, operation.SectionId);
                            operatorsBySectionType.Add(operation.SectionType, definition);
                            operationBinders.Add(operation.SectionId, definition);
                        }

                        foreach (var block in definition.ContextBlocks)
                        {
                            if (!block.IsValid || contextBinders.ContainsKey(block))
                                throw new InvalidOperationException($"Duplicate or invalid Context block declared by {type.FullName}.");
                            contextBinders.Add(block, definition);
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
                        {
                            registration = CoCoStandardSingleIntent.For(consume.IntentType);
                            if (intentRegistrations.ContainsKey(registration.IntentId))
                                throw new InvalidOperationException("Duplicate Intent id for " + consume.IntentType.FullName + ".");
                            registration.RegisterCatalog(builder);
                            intentRegistrations.Add(registration.IntentId, registration);
                            intentsByType.Add(consume.IntentType, registration);
                        }
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
                            operatorsBySectionType.TryGetValue(
                                provide.SectionType,
                                out CoCoStandardOperatorDefinition owner))
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

            var conditionFactories = RegisterConditions(assemblies, builder);
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
                contextBinders, intentRegistrations)
                { Assemblies = assemblies.ToArray(), _conditionFactories = conditionFactories };
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

        internal IEnumerable<Type> InboxEventTypes(CoCoCompiledStateGraph graph)
        {
            var usedLogic = new HashSet<Type>();
            foreach (var layer in graph.Layers)
            foreach (var state in layer.States)
            {
                for (var logic = state.Descriptor.LogicType; logic != null && typeof(CoCoStateLogic).IsAssignableFrom(logic); logic = logic.BaseType)
                    usedLogic.Add(logic);
            }

            var result = new HashSet<Type>();
            foreach (var assembly in usedLogic.Select(type => type.Assembly).Distinct())
            foreach (var entry in assembly.GetCustomAttributes<CoCoStateInboxEventAttribute>())
                if (usedLogic.Contains(entry.Logic)) result.Add(entry.Payload);
            return result;
        }

        bool ICoCoStateGraphProjectBindingProvider.TryConfigure(CoCoStateGraphHostBindingBuilder host,
            out CoCoDiagnostic diagnostic) => TryConfigure(host, out diagnostic);

        internal bool TryConfigure(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            if (bindingBuilder.Graph.IntentRequirements.AdapterCount != 0)
            {
                diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                    "This graph contains Event-to-Intent declarations. Migrate State reads to Inbox and remove the old declarations.");
                return false;
            }
            if (!TryBindRequiredIntent(bindingBuilder, out diagnostic) ||
                !TryBindRequiredOperations(bindingBuilder, out diagnostic) ||
                !TryBindRequiredContext(bindingBuilder, out diagnostic))
            {
                return false;
            }

            return TryBindStandardFactories(bindingBuilder, out diagnostic) &&
                   TryBindStandardConditions(bindingBuilder, out diagnostic);
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
            var matched = new int[bindings.Length];
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
                            $"Intent source {sources[sourceIndex].GetType().FullName} matches both " +
                            $"{requirements[match].ValueType.FullName} and {requirements[i].ValueType.FullName}.");
                        return false;
                    }
                    match = i;
                }
                if (match < 0)
                {
                    diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                        $"Host Intent source at index {sourceIndex} matches no required payload type.");
                    return false;
                }
                if (++matched[match] > requirements[match].MaxContributions)
                {
                    diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                        $"Too many Host sources for Intent {requirements[match].ValueType.FullName}. " +
                        $"The declared source capacity is {requirements[match].MaxContributions}.");
                    return false;
                }
                if (!bindings[match].TryBind(bindingBuilder, sourceIndex, out diagnostic)) return false;
            }
            for (int index = 0; index < matched.Length; index++)
            {
                if (matched[index] != 0) continue;
                diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                    $"Host source is missing for Intent {requirements[index].ValueType.FullName}.");
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
                        out CoCoStandardOperatorDefinition registrar))
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
                            out CoCoStandardOperatorDefinition registrar))
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

            // AnyState owns a persistent Activation without joining the active
            // path or receiving Enter. Ordinary initial-path defaults stay unchanged.
            ulong initialActivation = InitialActivation(bindingBuilder.Graph, state);
            bool hasInitialActivation = initialActivation != 0;
            bool isOnActivePath = hasInitialActivation && !state.IsAnyState;
            CoCoGraphStateRecord<int> defaultRecord;
            if (hasInitialActivation)
            {
                if (!CoCoActivationId.TryCreate(
                        initialActivation,
                        out CoCoActivationId activationId) ||
                    !CoCoGraphStateRecord<int>.TryCreate(
                        layer.LayerId,
                        state.StateId,
                        isOnActivePath,
                        activationId,
                        0d,
                        0d,
                        isOnActivePath,
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
                    EmptySchemas.State, ProvidesActionProgress(typeof(TLogic))),
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
            // IL2CPP 的 MakeGenericMethod 不保证执行与 Mono 相同的约束检查。
            if (!typeof(ICoCoStateUpdate).IsAssignableFrom(logicType))
            {
                throw new ArgumentException(
                    "Standard State generic constraint requires ICoCoStateUpdate: " + logicType.FullName,
                    nameof(logicType));
            }

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

        private static bool ProvidesActionProgress(Type type) =>
            ((CoCoStateAttribute)Attribute.GetCustomAttribute(type, typeof(CoCoStateAttribute)))
                ?.ProvidesActionProgress ?? false;

        private static Dictionary<CoCoConditionDescriptorId, ICoCoConditionRuntimeFactory> RegisterConditions(
            IReadOnlyList<System.Reflection.Assembly> assemblies, CoCoGraphDescriptorCatalogBuilder builder)
        {
            var factories = new Dictionary<CoCoConditionDescriptorId, ICoCoConditionRuntimeFactory>();
            foreach (var type in assemblies.SelectMany(GetLoadableTypes))
            {
                if (type == null || !type.IsDefined(typeof(ConditionAttribute), false)) continue;
                if (type.IsAbstract || type.ContainsGenericParameters ||
                    !typeof(CoCoStateCondition).IsAssignableFrom(type) ||
                    !typeof(ICoCoStateConditionEvaluator).IsAssignableFrom(type) ||
                    type.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException("Invalid standard Condition: " + type.FullName);
                CoCoConditionDescriptorId.TryCreate(0x434F434F434F4E44UL,
                    StandardDescriptors.DeriveLow(type.FullName), out var id);
                var factory = (ICoCoConditionRuntimeFactory)typeof(CoCoStandardBindingProvider)
                    .GetMethod(nameof(RegisterCondition),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .MakeGenericMethod(type).Invoke(null, new object[] { builder, id });
                factories.Add(id, factory);
            }
            return factories;
        }

        private static ICoCoConditionRuntimeFactory RegisterCondition<TCondition>(
            CoCoGraphDescriptorCatalogBuilder builder, CoCoConditionDescriptorId id)
            where TCondition : CoCoStateCondition, ICoCoStateConditionEvaluator, new()
        {
            if (!builder.TryRegisterCondition(
                    id, 1, new CoCoEmptyConditionConfig.Freezer(),
                    new CoCoConditionRuntimeRegistration<TCondition, EmptyConfigSchema>(EmptySchemas.State),
                    Array.Empty<CoCoIntentId>(), Array.Empty<CoCoStateBlockId>(), out var diagnostic))
                throw new InvalidOperationException(diagnostic.Message);
            return new CoCoConditionRuntimeFactory<TCondition>(_ => new TCondition());
        }

        private bool TryBindStandardConditions(CoCoStateGraphHostBindingBuilder builder,
            out CoCoDiagnostic diagnostic)
        {
            var bound = new HashSet<CoCoConditionDescriptorId>();
            foreach (var layer in builder.Graph.Layers)
            foreach (var transition in layer.Transitions)
            foreach (var condition in transition.Conditions)
            {
                var id = condition.Descriptor.DescriptorId;
                if (!bound.Add(id)) continue;
                if (!_conditionFactories.TryGetValue(id, out var factory))
                {
                    diagnostic = RegistryError(CoCoDiagnosticCode.MissingDescriptor,
                        "No standard Condition factory for " + id);
                    return false;
                }
                if (!builder.TryBindCondition(id, factory, out diagnostic)) return false;
            }
            diagnostic = CoCoDiagnostic.None;
            return true;
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

using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Standard project binding provider for state logics declared with
    /// CoCoStateAttribute. Builds a catalog where every such state uses the
    /// engine-free empty config and stateless memory (explicit
    /// TryRegisterState remains available for config/memory states), binds
    /// the RawInputIntent lane with the built-in pass-through reducer, binds
    /// Host intent source slot 0 to the scene InputReader, and binds one
    /// default graph-state slot per compiled state using the graph's real
    /// ids. Install via CoCoFlowRuntimeBootstrap (automatic on play).
    /// </summary>
    public sealed class CoCoStandardBindingProvider :
        ICoCoStateGraphProjectBindingProvider
    {
        private static readonly StatelessMemoryBinding MemoryBinding =
            new StatelessMemoryBinding();

        private readonly CoCoGraphDescriptorCatalog _catalog;
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
                contextBinders)
        {
            _catalog = catalog;
            _stateTypes = stateTypes;
            _operationBinders = operationBinders;
            _contextBinders = contextBinders;
        }

        public CoCoGraphDescriptorCatalog Catalog => _catalog;

        /// <summary>
        /// Scans the assembly of the marker type (put the marker next to your
        /// state scripts, or call the scan overload directly) for
        /// CoCoStateAttribute classes and builds the standard catalog.
        /// RawInputIntent-consuming states are wired automatically.
        /// </summary>
        public static CoCoStandardBindingProvider BuildForAssemblyOf<TMarker>()
        {
            return Build(new[] { typeof(TMarker).Assembly });
        }

        public static CoCoStandardBindingProvider Build(
            IReadOnlyList<System.Reflection.Assembly> assemblies)
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            if (!builder.TryRegisterIntent(
                    RawIntents.Player,
                    2,
                    new CoCoIntentReducerFactoryToken<
                        RawInputIntent,
                        RawInputPassThroughReducer,
                        RawInputReducerFactory>(1UL),
                    out CoCoDiagnostic intentDiagnostic))
            {
                throw new InvalidOperationException(
                    "Standard catalog intent registration failed: " +
                    intentDiagnostic.Message);
            }

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
                foreach (Type type in GetLoadableTypes(assembly))
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

                    bool consumesRawInput = false;
                    foreach (CoCoIntentConsumeAttribute consume in
                             type.GetCustomAttributes(
                                 typeof(CoCoIntentConsumeAttribute),
                                 false))
                    {
                        if (consume.IntentType != typeof(RawInputIntent))
                        {
                            throw new InvalidOperationException(
                                "Standard binding does not register Intent type " +
                                consume.IntentType.Name + " for State " + type.Name + ".");
                        }

                        consumesRawInput = true;
                    }

                    var stateProvides = new HashSet<CoCoOperationSectionId>();
                    var stateContextBlocks = new HashSet<CoCoStateBlockId>
                    {
                        StandardGraphState.BlockFor(descriptorId),
                    };
                    foreach (CoCoOperationProvideAttribute provide in
                             type.GetCustomAttributes(
                                 typeof(CoCoOperationProvideAttribute),
                                 false))
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
                            consumesRawInput,
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
                contextBinders);
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

        private static bool TryBindRequiredIntent(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            IReadOnlyList<CoCoIntentRequirement> requirements =
                bindingBuilder.Graph.IntentRequirements.Requirements;
            if (requirements.Count == 0)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (requirements.Count != 1 ||
                requirements[0].IntentId != RawIntents.Player ||
                requirements[0].ValueType != typeof(RawInputIntent))
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.MissingIntentReducer,
                    "Standard binding supports only the package RawInputIntent lane.");
                return false;
            }

            if (!bindingBuilder.TryRegisterIntent<
                    RawInputIntent,
                    RawInputPassThroughReducer,
                    RawInputReducerFactory>(
                    RawIntents.Player,
                    new RawInputReducerFactory(),
                    1UL,
                    out CoCoIntentHandle<RawInputIntent> intent,
                    out diagnostic) ||
                !bindingBuilder.TryBeginIntentBindings(out diagnostic) ||
                !CoCoIntentSourceRequirement<RawInputIntent>.TryCreate(
                    intent,
                    1,
                    out CoCoIntentSourceRequirement<RawInputIntent> source) ||
                !bindingBuilder.TryBindIntentSource(0, source, out diagnostic))
            {
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
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
            bool isInitialState = layer.States.Count > layer.InitialStateIndex &&
                                  layer.States[layer.InitialStateIndex].StateId ==
                                          state.StateId;
            CoCoGraphStateRecord<int> defaultRecord;
            if (isInitialState)
            {
                if (!CoCoActivationId.TryCreate(
                        1UL,
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
            bool consumesRawInput,
            CoCoOperationSectionId[] providedSections,
            CoCoStateBlockId[] contextBlocks)
        {
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
                    consumesRawInput,
                    providedSections,
                    contextBlocks
                });
            return result is true;
        }

        private static bool RegisterStateTyped<TLogic>(
            CoCoGraphDescriptorCatalogBuilder builder,
            CoCoStateDescriptorId descriptorId,
            bool consumesRawInput,
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
                consumesRawInput ? new[] { RawIntents.Player } : null,
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

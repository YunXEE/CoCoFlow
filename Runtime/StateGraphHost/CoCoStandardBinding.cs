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

        private CoCoStandardBindingProvider(CoCoGraphDescriptorCatalog catalog)
        {
            _catalog = catalog;
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

            if (!builder.TryRegisterStateBlock(
                    StandardGraphState.BlockId,
                    CoCoStateBlockOwner.Graph,
                    out CoCoDiagnostic blockDiagnostic))
            {
                throw new InvalidOperationException(
                    "Standard catalog block registration failed: " +
                    blockDiagnostic.Message);
            }

            int states = 0;
            foreach (System.Reflection.Assembly assembly in assemblies)
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

                foreach (Type type in types)
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

                    bool consumesRawInput = Attribute.GetCustomAttribute(
                            type,
                            typeof(CoCoIntentConsumeAttribute)) != null;

                    if (!TryRegisterStandardState(
                            builder,
                            type,
                            descriptorId,
                            consumesRawInput))
                    {
                        throw new InvalidOperationException(
                            "Standard catalog state registration failed for " +
                            type.Name);
                    }

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

            return new CoCoStandardBindingProvider(catalog);
        }

        public bool TryConfigure(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            if (!bindingBuilder.TryRegisterIntent<
                    RawInputIntent,
                    RawInputPassThroughReducer,
                    RawInputReducerFactory>(
                    RawIntents.Player,
                    new RawInputReducerFactory(),
                    1UL,
                    out CoCoIntentHandle<RawInputIntent> intent,
                    out diagnostic) ||
                !bindingBuilder.TryBeginIntentBindings(out diagnostic))
            {
                return false;
            }

            if (!CoCoIntentSourceRequirement<RawInputIntent>.TryCreate(
                    intent,
                    1,
                    out CoCoIntentSourceRequirement<RawInputIntent> requirement) ||
                !bindingBuilder.TryBindIntentSource(0, requirement, out diagnostic))
            {
                return false;
            }

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
                    CoCoStateSlotId slotId = StandardGraphState.SlotFor(
                        state.Descriptor.DescriptorId);
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
                            out CoCoGraphStateRecord<int> defaultRecord) ||
                        !bindingBuilder.TryBindGraphStateSlot<
                                StatelessMemory,
                                int,
                                StatelessMemoryBinding>(
                            layer.LayerId,
                            state.StateId,
                            StandardGraphState.BlockId,
                            slotId,
                            defaultRecord,
                            StatelessMemory.Fingerprint,
                            MemoryBinding,
                            out diagnostic))
                    {
                        return false;
                    }
                }
            }

            return TryBindStandardFactories(bindingBuilder, out diagnostic);
        }

        private static bool TryRegisterStandardState(
            CoCoGraphDescriptorCatalogBuilder builder,
            Type type,
            CoCoStateDescriptorId descriptorId,
            bool consumesRawInput)
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
                    consumesRawInput
                });
            return result is true;
        }

        private static bool RegisterStateTyped<TLogic>(
            CoCoGraphDescriptorCatalogBuilder builder,
            CoCoStateDescriptorId descriptorId,
            bool consumesRawInput)
            where TLogic : CoCoStateLogic, new()
        {
            bool ok = builder.TryRegisterState<TLogic, EmptyStateConfig, EmptyConfigSchema, StatelessMemory>(
                descriptorId,
                1U,
                new EmptyStateConfig.Freezer(),
                new CoCoStateRuntimeRegistration<TLogic, EmptyConfigSchema, StatelessMemory>(
                    EmptySchemas.State),
                consumesRawInput ? new[] { RawIntents.Player } : null,
                null,
                new[] { StandardGraphState.BlockId },
                out CoCoDiagnostic diagnostic);
            if (ok)
            {
                _ = builder.TryRegisterStateSlot<CoCoGraphStateRecord<int>>(
                    StandardGraphState.BlockId,
                    StandardGraphState.SlotFor(descriptorId),
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
                    default(CoCoGraphStateRecord<int>),
                    StatelessMemory.Fingerprint,
                    default,
                    null,
                    out _);
            }
            else
            {
                UnityEngine.Debug.LogError(
                    "[StandardBinding] TryRegisterState<" + typeof(TLogic).Name +
                    "> failed: " + diagnostic.Message);
            }

            return ok;
        }

        private bool TryBindStandardFactories(
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            foreach (KeyValuePair<CoCoStateDescriptorId, Type> pair in
                     StandardDescriptors.Table)
            {
                if (!TryCreateStandardFactory(
                        pair.Value,
                        bindingBuilder,
                        out ICoCoStateRuntimeFactory factory))
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Registry,
                        CoCoDiagnosticCode.MissingDescriptor,
                        "Standard factory creation failed for " + pair.Value.Name);
                    return false;
                }

                if (!bindingBuilder.TryBindState(
                        pair.Key,
                        factory,
                        out diagnostic))
                {
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryCreateStandardFactory(
            Type logicType,
            CoCoStateGraphHostBindingBuilder bindingBuilder,
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
    }
}

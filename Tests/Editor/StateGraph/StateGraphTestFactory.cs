using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;

// 供编辑器夹具共同构造具有固定身份的标准目录、冻结配置、图层和转换。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>为 EditMode 的编译及创作测试统一提供有效目录、图源和冻结节点配置。</summary>
    internal static class StateGraphTestFactory
    {
        internal static GraphId GraphId => CreateGraphId(1UL);
        internal static LayerId LayerId => CreateLayerId(1UL);
        internal static StateDescriptorId StateDescriptorId => CreateStateDescriptorId(1UL);
        internal static StateDescriptorId AnyStateDescriptorId => CreateStateDescriptorId(2UL);
        internal static IntentId IntentId => CreateIntentId(1UL);
        internal static OperationId OperationId => CreateOperationSectionId(1UL);
        internal static StateBlockId StateBlockId => CreateStateBlockId(1UL);
        internal static StateSlotId StateSlotId => CreateStateSlotId(1UL);

        internal static StateId RootStateId => CreateStateId(10UL);
        internal static StateId FirstChildStateId => CreateStateId(20UL);
        internal static StateId SecondChildStateId => CreateStateId(30UL);
        internal static TransitionId FirstTransitionId => CreateTransitionId(50UL);
        internal static TransitionId SecondTransitionId => CreateTransitionId(60UL);

        internal static GraphDescriptorCatalog CreateCatalog(
            bool includeManifestRequirements,
            uint descriptorRevision = 1U,
            ulong intentFactorySemanticFingerprint = 101UL,
            ulong operationFactorySemanticFingerprint = 102UL,
            bool providesActionProgress = false,
            bool includeAnyState = false)
        {
            var builder = new GraphDescriptorCatalogBuilder();
            IntentId[] intents = null;
            OperationId[] operations = null;
            StateBlockId[] blocks = null;
            if (includeManifestRequirements)
            {
                Require(builder.TryRegisterIntent(
                    IntentId,
                    4,
                    new IntentReducerFactoryToken<
                        TestIntent,
                        TestIntentReducer,
                        TestIntentReducerFactory>(intentFactorySemanticFingerprint),
                    out CoCoDiagnostic intentDiagnostic), intentDiagnostic);
                Require(builder.TryRegisterOperation(
                    OperationId,
                    Mode.Continuous,
                    new OperationViewFactoryToken<
                        ITestOperationSection,
                        TestOperationSectionViewFactory>(operationFactorySemanticFingerprint),
                    out CoCoDiagnostic operationDiagnostic), operationDiagnostic);
                Require(builder.TryRegisterStateBlock(
                    StateBlockId,
                    StateBlockOwner.Graph,
                    out CoCoDiagnostic blockDiagnostic), blockDiagnostic);
                Require(builder.TryRegisterStateSlot(
                    StateBlockId,
                    StateSlotId,
                    ContextProjection.Temporal,
                    ContextRestorePolicy.Stored,
                    7,
                    7UL,
                    default,
                    null,
                    out CoCoDiagnostic slotDiagnostic), slotDiagnostic);
                intents = new[] { IntentId };
                operations = new[] { OperationId };
                blocks = new[] { StateBlockId };
            }

            Require(builder.TryRegisterState(
                StateDescriptorId,
                descriptorRevision,
                new TestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(
                    TestFrozenConfigSchemas.StateSchema,
                    providesActionProgress),
                intents,
                operations,
                blocks,
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic);
            if (includeAnyState)
            {
                Require(builder.TryRegisterState(
                    AnyStateDescriptorId,
                    descriptorRevision,
                    new TestStateConfigFreezer(),
                    new StateRuntimeRegistration<TestAnyStateLogic, TestStateConfigSchema, TestActivationMemory>(
                        TestFrozenConfigSchemas.StateSchema),
                    intents,
                    operations,
                    blocks,
                    out CoCoDiagnostic anyDiagnostic), anyDiagnostic);
            }
            Require(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic);
            return catalog;
        }

        internal static StateGraphSource CreateHierarchicalSource(
            ulong contentFingerprint = 1001UL,
            bool reverseInputOrder = true)
        {
            StateSource root = State(
                RootStateId,
                default,
                FirstChildStateId,
                10);
            StateSource firstChild = State(
                FirstChildStateId,
                RootStateId,
                default,
                20);
            StateSource secondChild = State(
                SecondChildStateId,
                RootStateId,
                default,
                30);
            StateSource[] states = reverseInputOrder
                ? new[] { secondChild, root, firstChild }
                : new[] { root, firstChild, secondChild };

            var firstTransition = new TransitionSource(
                FirstTransitionId,
                FirstChildStateId,
                SecondChildStateId,
                10,
                TransitionWindow.Always);
            var secondTransition = new TransitionSource(
                SecondTransitionId,
                SecondChildStateId,
                FirstChildStateId,
                5,
                TransitionWindow.Always);
            TransitionSource[] transitions = reverseInputOrder
                ? new[] { secondTransition, firstTransition }
                : new[] { firstTransition, secondTransition };
            return Source(
                new StateLayerSource(LayerId, RootStateId, states, transitions),
                contentFingerprint);
        }

        internal static StateGraphSource CreateTerminalSource(ulong contentFingerprint = 2001UL)
        {
            var layer = new StateLayerSource(
                LayerId,
                RootStateId,
                new[] { State(RootStateId, default, default, 10) },
                Array.Empty<TransitionSource>());
            return Source(layer, contentFingerprint);
        }

        internal static StateGraphSource Source(
            StateLayerSource layer,
            ulong contentFingerprint,
            uint schemaVersion = StateGraphCompiler.CurrentSchemaVersion,
            GraphId graphId = default)
        {
            return new StateGraphSource(
                schemaVersion,
                contentFingerprint,
                graphId.IsValid ? graphId : GraphId,
                new[] { layer },
                Array.Empty<EventToIntentDeclarationSource>());
        }

        internal static StateSource State(
            StateId stateId,
            StateId parentStateId,
            StateId initialChildStateId,
            int configValue,
            StateDescriptorId descriptorId = default)
        {
            return new StateSource(
                stateId,
                parentStateId,
                initialChildStateId,
                descriptorId.IsValid ? descriptorId : StateDescriptorId,
                StateConfig(configValue));
        }

        internal static FrozenConfigSnapshot StateConfig(int value)
        {
            FrozenConfigWriter<TestStateConfigSchema> writer =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            Require(writer.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                value,
                out CoCoDiagnostic writeDiagnostic), writeDiagnostic);
            Require(writer.TrySeal(
                out FrozenConfigSnapshot snapshot,
                out CoCoDiagnostic sealDiagnostic), sealDiagnostic);
            return snapshot;
        }

        internal static GraphId CreateGraphId(ulong low)
        {
            Require(GraphId.TryCreate(1UL, low, out GraphId id));
            return id;
        }

        internal static LayerId CreateLayerId(ulong low)
        {
            Require(LayerId.TryCreate(2UL, low, out LayerId id));
            return id;
        }

        internal static StateId CreateStateId(ulong low)
        {
            Require(StateId.TryCreate(3UL, low, out StateId id));
            return id;
        }

        internal static TransitionId CreateTransitionId(ulong low)
        {
            Require(TransitionId.TryCreate(4UL, low, out TransitionId id));
            return id;
        }

        internal static StateDescriptorId CreateStateDescriptorId(ulong low)
        {
            Require(StateDescriptorId.TryCreate(5UL, low, out StateDescriptorId id));
            return id;
        }

        internal static IntentId CreateIntentId(ulong low)
        {
            Require(IntentId.TryCreate(7UL, low, out IntentId id));
            return id;
        }

        internal static OperationId CreateOperationSectionId(ulong low)
        {
            Require(OperationId.TryCreate(
                8UL,
                low,
                out OperationId id));
            return id;
        }

        internal static StateBlockId CreateStateBlockId(ulong low)
        {
            Require(StateBlockId.TryCreate(9UL, low, out StateBlockId id));
            return id;
        }

        internal static StateSlotId CreateStateSlotId(ulong low)
        {
            Require(StateSlotId.TryCreate(10UL, low, out StateSlotId id));
            return id;
        }

        private static void Require(bool succeeded, CoCoDiagnostic diagnostic = default)
        {
            if (!succeeded)
            {
                throw new InvalidOperationException(
                    diagnostic.IsNone ? "StateGraph test fixture construction failed." : diagnostic.Message);
            }
        }
    }

}

using System;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    internal static class CoCoStateGraphTestFactory
    {
        internal static CoCoGraphId GraphId => CreateGraphId(1UL);
        internal static CoCoLayerId LayerId => CreateLayerId(1UL);
        internal static CoCoStateDescriptorId StateDescriptorId => CreateStateDescriptorId(1UL);
        internal static CoCoConditionDescriptorId ConditionDescriptorId => CreateConditionDescriptorId(1UL);
        internal static CoCoIntentId IntentId => CreateIntentId(1UL);
        internal static CoCoOperationSectionId OperationSectionId => CreateOperationSectionId(1UL);
        internal static CoCoStateBlockId StateBlockId => CreateStateBlockId(1UL);
        internal static CoCoStateSlotId StateSlotId => CreateStateSlotId(1UL);

        internal static CoCoStateId RootStateId => CreateStateId(10UL);
        internal static CoCoStateId FirstChildStateId => CreateStateId(20UL);
        internal static CoCoStateId SecondChildStateId => CreateStateId(30UL);
        internal static CoCoTransitionId FirstTransitionId => CreateTransitionId(50UL);
        internal static CoCoTransitionId SecondTransitionId => CreateTransitionId(60UL);

        internal static CoCoGraphDescriptorCatalog CreateCatalog(
            bool includeManifestRequirements,
            uint descriptorRevision = 1U,
            ulong intentFactorySemanticFingerprint = 101UL,
            ulong operationFactorySemanticFingerprint = 102UL)
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            CoCoIntentId[] intents = null;
            CoCoOperationSectionId[] operations = null;
            CoCoStateBlockId[] blocks = null;
            if (includeManifestRequirements)
            {
                Require(builder.TryRegisterIntent(
                    IntentId,
                    4,
                    new CoCoIntentReducerFactoryToken<
                        TestIntent,
                        TestIntentReducer,
                        TestIntentReducerFactory>(intentFactorySemanticFingerprint),
                    out CoCoDiagnostic intentDiagnostic), intentDiagnostic);
                Require(builder.TryRegisterOperationSection(
                    OperationSectionId,
                    CoCoOperationSectionMode.Continuous,
                    new CoCoOperationSectionViewFactoryToken<
                        ITestOperationSection,
                        TestOperationSectionViewFactory>(operationFactorySemanticFingerprint),
                    out CoCoDiagnostic operationDiagnostic), operationDiagnostic);
                Require(builder.TryRegisterStateBlock(
                    StateBlockId,
                    CoCoStateBlockOwner.Graph,
                    out CoCoDiagnostic blockDiagnostic), blockDiagnostic);
                Require(builder.TryRegisterStateSlot(
                    StateBlockId,
                    StateSlotId,
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
                    7,
                    7UL,
                    default,
                    null,
                    out CoCoDiagnostic slotDiagnostic), slotDiagnostic);
                intents = new[] { IntentId };
                operations = new[] { OperationSectionId };
                blocks = new[] { StateBlockId };
            }

            Require(builder.TryRegisterState(
                StateDescriptorId,
                descriptorRevision,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                intents,
                operations,
                blocks,
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic);
            Require(builder.TryRegisterCondition(
                ConditionDescriptorId,
                descriptorRevision,
                new TestConditionConfigFreezer(),
                new CoCoConditionRuntimeRegistration<
                    TestStateCondition,
                    TestConditionConfigSchema>(TestFrozenConfigSchemas.ConditionSchema),
                intents,
                operations,
                blocks,
                out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic);
            Require(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic);
            return catalog;
        }

        internal static CoCoStateGraphSource CreateHierarchicalSource(
            ulong contentFingerprint = 1001UL,
            bool reverseInputOrder = true)
        {
            CoCoStateSource root = State(
                RootStateId,
                default,
                FirstChildStateId,
                10);
            CoCoStateSource firstChild = State(
                FirstChildStateId,
                RootStateId,
                default,
                20);
            CoCoStateSource secondChild = State(
                SecondChildStateId,
                RootStateId,
                default,
                30);
            CoCoStateSource[] states = reverseInputOrder
                ? new[] { secondChild, root, firstChild }
                : new[] { root, firstChild, secondChild };

            var firstTransition = new CoCoTransitionSource(
                FirstTransitionId,
                FirstChildStateId,
                SecondChildStateId,
                10,
                CoCoTransitionWindow.Always,
                CoCoTransitionInterruptPolicy.RequireSourceCompletion,
                new[]
                {
                    new CoCoConditionSource(ConditionDescriptorId, ConditionConfig(5))
                });
            var secondTransition = new CoCoTransitionSource(
                SecondTransitionId,
                SecondChildStateId,
                FirstChildStateId,
                5,
                CoCoTransitionWindow.Always,
                CoCoTransitionInterruptPolicy.RequireSourceCompletion,
                Array.Empty<CoCoConditionSource>());
            CoCoTransitionSource[] transitions = reverseInputOrder
                ? new[] { secondTransition, firstTransition }
                : new[] { firstTransition, secondTransition };
            return Source(
                new CoCoStateLayerSource(LayerId, RootStateId, states, transitions),
                contentFingerprint);
        }

        internal static CoCoStateGraphSource CreateTerminalSource(ulong contentFingerprint = 2001UL)
        {
            var layer = new CoCoStateLayerSource(
                LayerId,
                RootStateId,
                new[] { State(RootStateId, default, default, 10) },
                Array.Empty<CoCoTransitionSource>());
            return Source(layer, contentFingerprint);
        }

        internal static CoCoStateGraphSource Source(
            CoCoStateLayerSource layer,
            ulong contentFingerprint,
            uint schemaVersion = CoCoStateGraphCompiler.CurrentSchemaVersion,
            CoCoGraphId graphId = default)
        {
            return new CoCoStateGraphSource(
                schemaVersion,
                contentFingerprint,
                graphId.IsValid ? graphId : GraphId,
                new[] { layer },
                Array.Empty<CoCoEventToIntentDeclarationSource>());
        }

        internal static CoCoStateSource State(
            CoCoStateId stateId,
            CoCoStateId parentStateId,
            CoCoStateId initialChildStateId,
            int configValue,
            CoCoStateDescriptorId descriptorId = default)
        {
            return new CoCoStateSource(
                stateId,
                parentStateId,
                initialChildStateId,
                descriptorId.IsValid ? descriptorId : StateDescriptorId,
                StateConfig(configValue));
        }

        internal static CoCoFrozenConfigSnapshot StateConfig(int value)
        {
            CoCoFrozenConfigWriter<TestStateConfigSchema> writer =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            Require(writer.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                value,
                out CoCoDiagnostic writeDiagnostic), writeDiagnostic);
            Require(writer.TrySeal(
                out CoCoFrozenConfigSnapshot snapshot,
                out CoCoDiagnostic sealDiagnostic), sealDiagnostic);
            return snapshot;
        }

        internal static CoCoFrozenConfigSnapshot ConditionConfig(int threshold)
        {
            CoCoFrozenConfigWriter<TestConditionConfigSchema> writer =
                TestFrozenConfigSchemas.ConditionSchema.CreateWriter();
            Require(writer.TryWrite(
                TestFrozenConfigSchemas.ConditionThreshold,
                threshold,
                out CoCoDiagnostic writeDiagnostic), writeDiagnostic);
            Require(writer.TrySeal(
                out CoCoFrozenConfigSnapshot snapshot,
                out CoCoDiagnostic sealDiagnostic), sealDiagnostic);
            return snapshot;
        }

        internal static CoCoGraphId CreateGraphId(ulong low)
        {
            Require(CoCoGraphId.TryCreate(1UL, low, out CoCoGraphId id));
            return id;
        }

        internal static CoCoLayerId CreateLayerId(ulong low)
        {
            Require(CoCoLayerId.TryCreate(2UL, low, out CoCoLayerId id));
            return id;
        }

        internal static CoCoStateId CreateStateId(ulong low)
        {
            Require(CoCoStateId.TryCreate(3UL, low, out CoCoStateId id));
            return id;
        }

        internal static CoCoTransitionId CreateTransitionId(ulong low)
        {
            Require(CoCoTransitionId.TryCreate(4UL, low, out CoCoTransitionId id));
            return id;
        }

        internal static CoCoStateDescriptorId CreateStateDescriptorId(ulong low)
        {
            Require(CoCoStateDescriptorId.TryCreate(5UL, low, out CoCoStateDescriptorId id));
            return id;
        }

        internal static CoCoConditionDescriptorId CreateConditionDescriptorId(ulong low)
        {
            Require(CoCoConditionDescriptorId.TryCreate(
                6UL,
                low,
                out CoCoConditionDescriptorId id));
            return id;
        }

        internal static CoCoIntentId CreateIntentId(ulong low)
        {
            Require(CoCoIntentId.TryCreate(7UL, low, out CoCoIntentId id));
            return id;
        }

        internal static CoCoOperationSectionId CreateOperationSectionId(ulong low)
        {
            Require(CoCoOperationSectionId.TryCreate(
                8UL,
                low,
                out CoCoOperationSectionId id));
            return id;
        }

        internal static CoCoStateBlockId CreateStateBlockId(ulong low)
        {
            Require(CoCoStateBlockId.TryCreate(9UL, low, out CoCoStateBlockId id));
            return id;
        }

        internal static CoCoStateSlotId CreateStateSlotId(ulong low)
        {
            Require(CoCoStateSlotId.TryCreate(10UL, low, out CoCoStateSlotId id));
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

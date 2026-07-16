using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoStateFlowContractTests
    {
        [Test]
        public void EmptyOperationRegistryFreezesAndProducesAValidEmptyFrame()
        {
            var builder = new CoCoOperationSectionRegistryBuilder();

            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(1UL, 1UL),
                out CoCoOperationSectionRegistry registry,
                out CoCoDiagnostic freezeDiagnostic),
                freezeDiagnostic.Message);
            Assert.IsTrue(registry.IsFrozen);
            Assert.AreEqual(0, registry.Count);
            Assert.AreEqual(0, registry.ByteSize);
            Assert.IsEmpty(registry.Sections);
            Assert.IsTrue(registry.TryValidateProvides(
                Array.Empty<CoCoOperationSectionRequirement>(),
                out CoCoDiagnostic providesDiagnostic),
                providesDiagnostic.Message);
            Assert.IsTrue(CoCoOperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(1UL),
                Array.Empty<CoCoOperationSectionRequirement>(),
                out CoCoOperationFrame frame,
                out CoCoDiagnostic createDiagnostic),
                createDiagnostic.Message);
            Assert.IsTrue(frame.TryBegin(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoOperationFrameWriter writer));
            Assert.IsTrue(writer.Seal());
            Assert.IsTrue(frame.IsSealed);
        }

        [Test]
        public void StateRolesRemainCallbackFree()
        {
            AssertCallbackFreeRole(typeof(CoCoStateLogic));
            AssertCallbackFreeRole(typeof(CoCoStateConfig));
            AssertCallbackFreeRole(typeof(CoCoActivationMemory));
        }

        [Test]
        public void FrameHeadersCarryIndependentKindsAndStableIdentity()
        {
            CoCoGraphInstanceId graphInstanceId = CreateGraphInstanceId(11UL);
            CoCoFrameLayoutId layoutId = CreateLayoutId(1UL, 2UL);
            CoCoTickFrame tickFrame = CreateTickFrame(7UL, 3UL, 5UL);

            Assert.IsTrue(CoCoStateFlowFrameHeader.TryCreate(
                graphInstanceId,
                layoutId,
                CoCoStateFlowFrameKind.Intent,
                tickFrame,
                out CoCoStateFlowFrameHeader intent));
            Assert.IsTrue(CoCoStateFlowFrameHeader.TryCreate(
                graphInstanceId,
                layoutId,
                CoCoStateFlowFrameKind.Operation,
                tickFrame,
                out CoCoStateFlowFrameHeader operation));
            Assert.IsTrue(CoCoStateFlowFrameHeader.TryCreate(
                graphInstanceId,
                layoutId,
                CoCoStateFlowFrameKind.Context,
                tickFrame,
                out CoCoStateFlowFrameHeader context));

            Assert.AreEqual(CoCoStateFlowFrameKind.Intent, intent.Identity.Kind);
            Assert.AreEqual(CoCoStateFlowFrameKind.Operation, operation.Identity.Kind);
            Assert.AreEqual(CoCoStateFlowFrameKind.Context, context.Identity.Kind);
            Assert.AreEqual(graphInstanceId, context.Identity.GraphInstanceId);
            Assert.AreEqual(tickFrame.TimelineEpoch, context.Identity.TimelineEpoch);
            Assert.AreEqual(tickFrame.Tick, context.Identity.Tick);
            Assert.AreEqual(tickFrame.ExecutionSequence, context.Identity.ExecutionSequence);
            Assert.AreEqual(layoutId, context.LayoutId);
            Assert.AreNotEqual(intent, operation);
            Assert.AreNotEqual(operation, context);
        }

        [Test]
        public void OperationSectionRegistryDeduplicatesExactInterfacesButKeepsSameShapeDistinct()
        {
            var builder = new CoCoOperationSectionRegistryBuilder();
            CoCoOperationSectionId movementId = CreateSectionId(80UL, 1UL);
            CoCoOperationSectionId alternateMovementId = CreateSectionId(80UL, 2UL);
            var movementFactory = new MovementViewFactory();

            Assert.IsTrue(builder.TryRegister(
                movementId,
                CoCoOperationSectionMode.Continuous,
                movementFactory,
                out CoCoOperationSectionRequirement movementRequirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryRegister(
                movementId,
                CoCoOperationSectionMode.Continuous,
                movementFactory,
                out CoCoOperationSectionRequirement duplicateRequirement,
                out diagnostic),
                diagnostic.Message);
            Assert.AreEqual(movementRequirement, duplicateRequirement);
            Assert.AreEqual(1, builder.Count);

            Assert.IsFalse(builder.TryRegister(
                movementId,
                CoCoOperationSectionMode.Continuous,
                new MovementViewFactory(),
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, diagnostic.Code);
            Assert.AreEqual(1, builder.Count);

            Assert.IsTrue(builder.TryRegister(
                alternateMovementId,
                CoCoOperationSectionMode.Continuous,
                new AlternateMovementViewFactory(),
                out CoCoOperationSectionRequirement alternateRequirement,
                out diagnostic),
                diagnostic.Message);
            Assert.AreNotEqual(movementRequirement.SectionType, alternateRequirement.SectionType);
            Assert.AreEqual(2, builder.Count);

            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(80UL, 3UL),
                out CoCoOperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.AreEqual(2, registry.Count);
            Assert.IsTrue(registry.TryResolve(
                movementRequirement,
                out CoCoOperationSectionHandle<IMovementSection> movementHandle));
            Assert.IsTrue(registry.TryResolve(
                alternateRequirement,
                out CoCoOperationSectionHandle<IAlternateMovementSection> alternateHandle));
            Assert.AreNotEqual(movementHandle.DenseIndex, alternateHandle.DenseIndex);

            Assert.IsFalse(builder.TryRegister(
                CreateSectionId(80UL, 4UL),
                CoCoOperationSectionMode.Continuous,
                movementFactory,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, diagnostic.Code);
        }

        [Test]
        public void OperationRequirementCarriesCompleteDeterministicShape()
        {
            Assert.IsTrue(CoCoOperationSectionRequirement.TryCreate<ICompositeShapeSection>(
                CreateSectionId(80UL, 10UL),
                CoCoOperationSectionMode.Continuous,
                out CoCoOperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(CoCoOperationSectionRequirement.TryCreate<ICompositeShapeTwinSection>(
                CreateSectionId(80UL, 11UL),
                CoCoOperationSectionMode.Continuous,
                out CoCoOperationSectionRequirement twin,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(CoCoOperationSectionRequirement.TryCreate<IDifferentCompositeShapeSection>(
                CreateSectionId(80UL, 12UL),
                CoCoOperationSectionMode.Continuous,
                out CoCoOperationSectionRequirement different,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(CoCoOperationSectionShape.TryCreate(
                typeof(ICompositeShapeSection),
                out CoCoOperationSectionShape toolShape,
                out diagnostic),
                diagnostic.Message);

            CoCoOperationSectionShape shape = requirement.Shape;
            Assert.IsNotNull(shape);
            Assert.IsTrue(shape.IsValid);
            Assert.AreEqual(14, shape.ByteSize);
            Assert.AreEqual(3, shape.FieldCount);
            Assert.AreNotEqual(0UL, shape.ShapeFingerprint);
            AssertShapeField(shape.Fields[0], 0, "Alpha", typeof(int), 0, 4);
            AssertShapeField(shape.Fields[1], 1, "Middle", typeof(long), 4, 8);
            AssertShapeField(shape.Fields[2], 2, "Zulu", typeof(short), 12, 2);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoOperationSectionFieldShape>)shape.Fields)[0] = shape.Fields[0]);

            Assert.AreNotSame(shape, twin.Shape);
            Assert.AreEqual(shape.ShapeFingerprint, twin.Shape.ShapeFingerprint);
            Assert.IsTrue(shape.Equals(twin.Shape));
            Assert.AreNotSame(shape, toolShape);
            Assert.IsTrue(shape.Equals(toolShape));
            Assert.AreNotEqual(shape.ShapeFingerprint, different.Shape.ShapeFingerprint);
            Assert.IsFalse(shape.Equals(different.Shape));

            FieldInfo fingerprintField = typeof(CoCoOperationSectionShape).GetField(
                "<ShapeFingerprint>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fingerprintField);
            fingerprintField.SetValue(different.Shape, shape.ShapeFingerprint);
            Assert.AreEqual(shape.ShapeFingerprint, different.Shape.ShapeFingerprint);
            Assert.IsFalse(
                shape.Equals(different.Shape),
                "Fingerprint equality is only a fast filter; every field remains authoritative.");
        }

        [Test]
        public void OperationRegistryReusesTheRequirementShapeLayout()
        {
            var builder = new CoCoOperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(80UL, 20UL),
                CoCoOperationSectionMode.Discrete,
                new CompositeShapeViewFactory(),
                out CoCoOperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(80UL, 21UL),
                out CoCoOperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);

            CoCoOperationSectionDescriptor descriptor = registry.Sections[0];
            Assert.AreSame(requirement.Shape, descriptor.Shape);
            Assert.AreEqual(requirement.Shape.ByteSize, descriptor.ByteSize);
            Assert.AreEqual(requirement.Shape.FieldCount, descriptor.Fields.Count);
            for (int index = 0; index < requirement.Shape.FieldCount; index++)
            {
                CoCoOperationSectionFieldShape expected = requirement.Shape.Fields[index];
                CoCoOperationSectionFieldDescriptor actual = descriptor.Fields[index];
                AssertShapeField(
                    expected,
                    actual.DenseIndex,
                    actual.Name,
                    actual.ValueType,
                    actual.ByteOffset,
                    actual.ByteSize);
            }
        }

        [Test]
        public void OperationHandlesRejectASeparateRegistryWithTheSameLayoutIdentity()
        {
            CoCoFrameLayoutId layoutId = CreateLayoutId(81UL, 1UL);
            CoCoOperationSectionId sectionId = CreateSectionId(81UL, 2UL);
            var firstBuilder = new CoCoOperationSectionRegistryBuilder();
            Assert.IsTrue(firstBuilder.TryRegister(
                sectionId,
                CoCoOperationSectionMode.Continuous,
                new MovementViewFactory(),
                out CoCoOperationSectionRequirement movementRequirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(firstBuilder.TryFreeze(
                layoutId,
                out CoCoOperationSectionRegistry firstRegistry,
                out diagnostic),
                diagnostic.Message);

            var secondBuilder = new CoCoOperationSectionRegistryBuilder();
            Assert.IsTrue(secondBuilder.TryRegister(
                sectionId,
                CoCoOperationSectionMode.Continuous,
                new AlternateMovementViewFactory(),
                out CoCoOperationSectionRequirement alternateRequirement,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(secondBuilder.TryFreeze(
                layoutId,
                out CoCoOperationSectionRegistry secondRegistry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(secondRegistry.TryResolve(
                alternateRequirement,
                out CoCoOperationSectionHandle<IAlternateMovementSection> foreignHandle));
            Assert.IsFalse(firstRegistry.TryResolveField(
                foreignHandle,
                0,
                out CoCoOperationSectionField<int> _));

            Assert.IsTrue(CoCoOperationFrame.TryCreate(
                firstRegistry,
                CreateGraphInstanceId(81UL),
                new[] { movementRequirement },
                out CoCoOperationFrame frame,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(frame.TryBegin(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoOperationFrameWriter writer));
            Assert.IsTrue(writer.Seal());
            Assert.IsFalse(frame.TryGet(
                foreignHandle,
                out CoCoOperationSectionEntry<IAlternateMovementSection> _));
        }

        [Test]
        public void OperationSectionRejectsInheritanceAndIllegalMemberShapes()
        {
            AssertInvalidSection<IInheritedMovementSection>();
            AssertInvalidSection<IWritableSection>();
            AssertInvalidSection<IMethodSection>();
            AssertInvalidSection<IEventSection>();
            AssertInvalidSection<IIndexerSection>();
            AssertInvalidSection<IReferenceSection>();
            AssertInvalidSection<IStringSection>();
            AssertInvalidSection<IRefReturnSection>();
        }

        [Test]
        public void OperationRequirementRejectsIncompleteShapesAtDeclarationTime()
        {
            CoCoOperationSectionId sectionId = CreateSectionId(89UL, 1UL);

            Assert.IsFalse(CoCoOperationSectionRequirement.TryCreate<IManifestOnlySection>(
                sectionId,
                CoCoOperationSectionMode.Continuous,
                out _,
                out CoCoDiagnostic runtimeDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidOperationSection, runtimeDiagnostic.Code);
            Assert.IsFalse(CoCoOperationSectionShape.TryCreate(
                typeof(IManifestOnlySection),
                out _,
                out CoCoDiagnostic toolDiagnostic));
            Assert.AreEqual(runtimeDiagnostic, toolDiagnostic);

            AssertInvalidSection<ICoCoOperationSection>();
            AssertInvalidSection<IInheritedMovementSection>();
            AssertInvalidSection<ManifestSectionClass>();
        }

        [Test]
        public void OperationFrameUsesContinuousZeroAndDiscreteActivationHeaders()
        {
            var builder = new CoCoOperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(90UL, 1UL),
                CoCoOperationSectionMode.Continuous,
                new MovementViewFactory(),
                out CoCoOperationSectionRequirement movementRequirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(90UL, 2UL),
                CoCoOperationSectionMode.Discrete,
                new AttackViewFactory(),
                out CoCoOperationSectionRequirement attackRequirement,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(90UL, 3UL),
                out CoCoOperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(registry.TryResolve(
                movementRequirement,
                out CoCoOperationSectionHandle<IMovementSection> movementHandle));
            Assert.IsTrue(registry.TryResolve(
                attackRequirement,
                out CoCoOperationSectionHandle<IAttackSection> attackHandle));
            Assert.IsTrue(registry.TryResolveField(
                movementHandle,
                0,
                out CoCoOperationSectionField<int> distanceField));
            Assert.IsTrue(registry.TryResolveField(
                attackHandle,
                0,
                out CoCoOperationSectionField<int> damageField));
            Assert.IsFalse(CoCoOperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(901UL),
                new[] { movementRequirement },
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.MissingOperationSection, diagnostic.Code);
            Assert.IsFalse(CoCoOperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(902UL),
                new[] { movementRequirement, movementRequirement },
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, diagnostic.Code);
            Assert.IsTrue(CoCoOperationSectionRequirement.TryCreate<IAlternateMovementSection>(
                CreateSectionId(90UL, 4UL),
                CoCoOperationSectionMode.Continuous,
                out CoCoOperationSectionRequirement unregistered,
                out diagnostic));
            Assert.IsFalse(CoCoOperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(903UL),
                new[] { movementRequirement, unregistered },
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.MissingOperationSection, diagnostic.Code);
            Assert.IsTrue(CoCoOperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(90UL),
                new[] { movementRequirement, attackRequirement },
                out CoCoOperationFrame frame,
                out diagnostic),
                diagnostic.Message);

            Assert.IsTrue(frame.TryBegin(CreateTickFrame(1UL, 1UL, 1UL), out CoCoOperationFrameWriter writer));
            Assert.IsTrue(writer.Write(distanceField, 6));
            Assert.IsTrue(writer.Write(damageField, 12));
            Assert.IsTrue(CoCoActivationId.TryCreate(5UL, out CoCoActivationId activationId));
            Assert.IsTrue(writer.EnableDiscrete(attackHandle, activationId, out CoCoOperationSequence sequence));
            Assert.AreEqual(1UL, sequence.Value);
            Assert.IsTrue(writer.Seal());
            Assert.IsTrue(frame.TryGet(
                movementHandle,
                out CoCoOperationSectionEntry<IMovementSection> movement));
            Assert.IsTrue(frame.TryGet(
                attackHandle,
                out CoCoOperationSectionEntry<IAttackSection> attack));
            Assert.IsTrue(movement.Header.Enabled);
            Assert.AreEqual(6, movement.View.Distance);
            Assert.IsTrue(attack.Header.Enabled);
            Assert.AreEqual(activationId, attack.Header.ActivationId);
            Assert.AreEqual(sequence, attack.Header.OperationSequence);
            Assert.AreEqual(12, attack.View.Damage);

            Assert.IsFalse(frame.TryBegin(CreateTickFrame(1UL, 1UL, 2UL), out _));

            IMovementSection reusedView = movement.View;
            Assert.IsTrue(frame.TryBegin(CreateTickFrame(2UL, 1UL, 2UL), out writer));
            Assert.IsTrue(writer.Seal());
            Assert.IsTrue(frame.TryGet(movementHandle, out movement));
            Assert.IsTrue(frame.TryGet(attackHandle, out attack));
            Assert.IsTrue(movement.Header.Enabled);
            Assert.AreEqual(0, movement.View.Distance);
            Assert.AreSame(reusedView, movement.View);
            Assert.IsFalse(attack.Header.Enabled);
            Assert.AreEqual(0, attack.View.Damage);

            Assert.IsTrue(frame.TryBegin(CreateTickFrame(3UL, 2UL, 3UL), out writer));
            Assert.IsTrue(writer.EnableDiscrete(attackHandle, activationId, out CoCoOperationSequence cancelled));
            Assert.AreEqual(1UL, cancelled.Value);
            Assert.IsTrue(writer.Cancel());

            Assert.IsTrue(frame.TryBegin(CreateTickFrame(3UL, 1UL, 4UL), out writer));
            Assert.IsTrue(writer.EnableDiscrete(attackHandle, activationId, out CoCoOperationSequence continued));
            Assert.AreEqual(2UL, continued.Value);
            Assert.IsTrue(writer.Seal());

            Assert.IsTrue(frame.TryBegin(CreateTickFrame(4UL, 2UL, 5UL), out writer));
            Assert.IsTrue(writer.EnableDiscrete(attackHandle, activationId, out CoCoOperationSequence newEpoch));
            Assert.AreEqual(1UL, newEpoch.Value);
            Assert.IsTrue(writer.Seal());
            Assert.IsFalse(frame.TryBegin(CreateTickFrame(5UL, 1UL, 6UL), out _));
        }

        [Test]
        public void OperationSectionCustomNestedUnmanagedValueRoundTripsWithoutGenericReflection()
        {
            var builder = new CoCoOperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(91UL, 1UL),
                CoCoOperationSectionMode.Continuous,
                new NestedValueViewFactory(),
                out CoCoOperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(91UL, 2UL),
                out CoCoOperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(registry.TryResolve(
                requirement,
                out CoCoOperationSectionHandle<INestedValueSection> handle));
            Assert.IsTrue(registry.TryResolveField(
                handle,
                0,
                out CoCoOperationSectionField<NestedValue> field));
            Assert.IsTrue(CoCoOperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(91UL),
                new[] { requirement },
                out CoCoOperationFrame frame,
                out diagnostic),
                diagnostic.Message);

            var value = new NestedValue(17, true, 9);
            Assert.IsTrue(frame.TryBegin(CreateTickFrame(1UL, 1UL, 1UL), out CoCoOperationFrameWriter writer));
            Assert.IsTrue(writer.Write(field, value));
            Assert.IsTrue(writer.Seal());
            Assert.IsTrue(frame.TryGet(handle, out CoCoOperationSectionEntry<INestedValueSection> entry));
            Assert.AreEqual(17, entry.View.Value.Count);
            Assert.IsTrue(entry.View.Value.Flags.Enabled);
            Assert.AreEqual(9, entry.View.Value.Flags.Code);
        }

        [Test]
        public void ContextLayoutFreezesBlockOwnershipProjectionAndSingleWriter()
        {
            var builder = new CoCoContextFrameLayoutBuilder();
            CoCoStateBlockId graphBlockId = CreateBlockId(1UL, 1UL);
            CoCoStateBlockId operatorBlockId = CreateBlockId(1UL, 2UL);
            CoCoStateSlotId healthSlotId = CreateSlotId(2UL, 1UL);
            CoCoStateSlotId speedSlotId = CreateSlotId(2UL, 2UL);
            CoCoCodecDescriptor codec = CreateCodecDescriptor(4UL, 1U);

            Assert.IsTrue(builder.TryAddBlock(
                graphBlockId,
                CoCoStateBlockOwner.Graph,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.None, diagnosticCode);
            Assert.IsTrue(builder.TryAddBlock(
                operatorBlockId,
                CoCoStateBlockOwner.Operator,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                graphBlockId,
                healthSlotId,
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable,
                CoCoContextRestorePolicy.Stored,
                100,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                operatorBlockId,
                speedSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.ResetToDefault,
                1.5f,
                default,
                null,
                out diagnosticCode));

            Assert.IsFalse(builder.TryAddSlot(
                operatorBlockId,
                healthSlotId,
                CoCoContextProjection.Durable,
                CoCoContextRestorePolicy.Stored,
                0,
                default,
                null,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, diagnosticCode);

            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(3UL, 1UL),
                1U,
                out CoCoContextFrameLayout layout,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.None, diagnosticCode);
            Assert.AreEqual(2, layout.Blocks.Count);
            Assert.AreEqual(CoCoStateBlockOwner.Graph, layout.Blocks[0].Owner);
            Assert.AreEqual(CoCoStateBlockOwner.Operator, layout.Blocks[1].Owner);

            CoCoStateSlotDescriptor health = layout.Slots[0];
            Assert.AreEqual(graphBlockId, health.WriterBlockId);
            Assert.AreEqual(
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable,
                health.Projection);
            Assert.AreEqual(CoCoContextRestorePolicy.Stored, health.RestorePolicy);
            Assert.AreEqual(codec, health.Codec);
            Assert.IsTrue(layout.TryResolveSlot(healthSlotId, out CoCoStateSlot<int> healthHandle));
            Assert.IsTrue(healthHandle.IsValid);
            Assert.IsFalse(layout.TryResolveSlot(healthSlotId, out CoCoStateSlot<float> wrongTypeHandle));
            Assert.IsFalse(wrongTypeHandle.IsValid);

            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(3UL), layout, 2);
            Assert.IsTrue(layout.TryResolveBlock(graphBlockId, out CoCoStateBlockHandle graphBlock));
            Assert.IsTrue(layout.TryResolveBlock(operatorBlockId, out CoCoStateBlockHandle operatorBlock));
            Assert.IsTrue(layout.TryResolveSlot(speedSlotId, out CoCoStateSlot<float> speedHandle));
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit prepared,
                out CoCoContextCommitStatus status));
            Assert.IsTrue(prepared.TryGetWriter(graphBlock, out CoCoContextFrameWriter graphWriter));
            Assert.IsTrue(prepared.TryGetWriter(operatorBlock, out CoCoContextFrameWriter operatorWriter));
            Assert.IsTrue(graphWriter.Write(healthHandle, 80));
            Assert.IsFalse(graphWriter.Write(speedHandle, 3f));
            Assert.IsTrue(operatorWriter.Write(speedHandle, 3f));
            Assert.IsFalse(operatorWriter.Write(healthHandle, 80));
            Assert.IsTrue(FinalizeAndCommit(prepared).Succeeded);

            Assert.IsFalse(builder.TryAddBlock(
                CreateBlockId(9UL, 9UL),
                CoCoStateBlockOwner.Actor,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, diagnosticCode);
        }

        [Test]
        public void DerivedRestoreDependenciesFreezeInTopologicalOrder()
        {
            var builder = new CoCoContextFrameLayoutBuilder();
            CoCoStateBlockId blockId = CreateBlockId(10UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(10UL, 1UL);
            CoCoStateSlotId firstDerivedSlotId = CreateSlotId(10UL, 2UL);
            CoCoStateSlotId secondDerivedSlotId = CreateSlotId(10UL, 3UL);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                CoCoContextProjection.Durable,
                CoCoContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                firstDerivedSlotId,
                CoCoContextProjection.Durable,
                0,
                default,
                new[] { storedSlotId },
                new AddOneRebuilder(storedSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                secondDerivedSlotId,
                CoCoContextProjection.Durable,
                0,
                default,
                new[] { firstDerivedSlotId },
                new AddOneRebuilder(firstDerivedSlotId),
                out diagnosticCode));

            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(10UL, 4UL),
                1U,
                out CoCoContextFrameLayout layout,
                out diagnosticCode));
            CollectionAssert.AreEqual(new[] { 1, 2 }, layout.DerivedOrder);
            Assert.IsTrue(layout.TryResolveSlot(
                firstDerivedSlotId,
                out CoCoStateSlot<int> firstDerivedSlot));
            Assert.IsTrue(layout.TryResolveSlot(
                secondDerivedSlotId,
                out CoCoStateSlot<int> secondDerivedSlot));
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(10UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            CoCoContextFrame frame = FinalizeAndCommit(prepared).Frame;
            Assert.AreEqual(2, frame.Read(firstDerivedSlot));
            Assert.AreEqual(3, frame.Read(secondDerivedSlot));
        }

        [Test]
        public void DerivedRestoreDependencyCyclesAreRejected()
        {
            var builder = new CoCoContextFrameLayoutBuilder();
            CoCoStateBlockId blockId = CreateBlockId(20UL, 1UL);
            CoCoStateSlotId leftSlotId = CreateSlotId(20UL, 1UL);
            CoCoStateSlotId rightSlotId = CreateSlotId(20UL, 2UL);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Graph,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                leftSlotId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { rightSlotId },
                new AddOneRebuilder(rightSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                rightSlotId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { leftSlotId },
                new AddOneRebuilder(leftSlotId),
                out diagnosticCode));

            Assert.IsFalse(builder.TryFreeze(
                CreateLayoutId(20UL, 3UL),
                1U,
                out CoCoContextFrameLayout layout,
                out diagnosticCode));
            Assert.IsNull(layout);
            Assert.AreEqual(CoCoDiagnosticCode.DerivedDependencyCycle, diagnosticCode);
        }

        [Test]
        public void ProjectionRequiresStoredAndDerivedDependencyClosureButAllowsResetDependencies()
        {
            CoCoStateBlockId blockId = CreateBlockId(21UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(21UL, 1UL);
            CoCoStateSlotId firstDerivedSlotId = CreateSlotId(21UL, 2UL);
            CoCoStateSlotId secondDerivedSlotId = CreateSlotId(21UL, 3UL);
            var invalidBuilder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(invalidBuilder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(invalidBuilder.TryAddSlot(
                blockId,
                storedSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(invalidBuilder.TryAddDerivedSlot(
                blockId,
                firstDerivedSlotId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                new AddOneRebuilder(storedSlotId),
                out diagnosticCode));
            Assert.IsTrue(invalidBuilder.TryAddDerivedSlot(
                blockId,
                secondDerivedSlotId,
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable,
                0,
                default,
                new[] { firstDerivedSlotId },
                new AddOneRebuilder(firstDerivedSlotId),
                out diagnosticCode));
            Assert.IsFalse(invalidBuilder.TryFreeze(
                CreateLayoutId(21UL, 1UL),
                1U,
                out _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);

            var resetBuilder = new CoCoContextFrameLayoutBuilder();
            CoCoStateSlotId resetSlotId = CreateSlotId(21UL, 4UL);
            CoCoStateSlotId resetDerivedSlotId = CreateSlotId(21UL, 5UL);
            Assert.IsTrue(resetBuilder.TryAddBlock(blockId, CoCoStateBlockOwner.Actor, out diagnosticCode));
            Assert.IsTrue(resetBuilder.TryAddSlot(
                blockId,
                resetSlotId,
                CoCoContextProjection.None,
                CoCoContextRestorePolicy.ResetToDefault,
                5,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(resetBuilder.TryAddDerivedSlot(
                blockId,
                resetDerivedSlotId,
                CoCoContextProjection.Durable,
                0,
                default,
                new[] { resetSlotId },
                new DoubleRebuilder(resetSlotId),
                out diagnosticCode));
            Assert.IsTrue(resetBuilder.TryFreeze(
                CreateLayoutId(21UL, 2UL),
                1U,
                out _,
                out diagnosticCode));
        }

        [Test]
        public void DerivedValuesFinalizeEveryCommitAndFailurePreservesAuthority()
        {
            CoCoStateBlockId blockId = CreateBlockId(22UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(22UL, 1UL);
            CoCoStateSlotId derivedSlotId = CreateSlotId(22UL, 2UL);
            var rebuilder = new ControllableDoubleRebuilder(storedSlotId);
            var builder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(blockId, CoCoStateBlockOwner.Actor, out _));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                1,
                default,
                null,
                out _));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                rebuilder,
                out _));
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(22UL, 1UL),
                1U,
                out CoCoContextFrameLayout layout,
                out _));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out CoCoStateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(storedSlotId, out CoCoStateSlot<int> storedSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out CoCoStateSlot<int> derivedSlot));
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(22UL), layout, 2);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit first,
                out _));
            Assert.IsTrue(first.TryGetWriter(block, out CoCoContextFrameWriter writer));
            Assert.IsTrue(writer.Write(storedSlot, 3));
            Assert.IsFalse(writer.Write(derivedSlot, 99));
            CoCoContextCommitResult firstResult = FinalizeAndCommit(first);
            Assert.AreEqual(6, firstResult.Frame.Read(derivedSlot));
            Assert.AreEqual(1, rebuilder.InvocationCount);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out CoCoPreparedContextCommit noOp,
                out _));
            CoCoContextCommitResult noOpResult = FinalizeAndCommit(noOp);
            Assert.AreEqual(2UL, noOpResult.Frame.Revision.Value);
            Assert.AreEqual(6, noOpResult.Frame.Read(derivedSlot));
            Assert.AreEqual(2, rebuilder.InvocationCount);

            rebuilder.ShouldFail = true;
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out CoCoPreparedContextCommit failed,
                out _));
            Assert.IsTrue(failed.TryGetWriter(block, out writer));
            Assert.IsTrue(writer.Write(storedSlot, 4));
            Assert.IsFalse(failed.TryFinalize(
                out _,
                out CoCoContextCommitStatus failedStatus));
            Assert.AreEqual(CoCoContextCommitStatus.DerivedRebuildFailed, failedStatus);
            Assert.AreEqual(2UL, arena.Current.Revision.Value);
            Assert.AreEqual(3, arena.Current.Read(storedSlot));
            Assert.AreEqual(3, rebuilder.InvocationCount);

            rebuilder.ShouldFail = false;
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out CoCoPreparedContextCommit retry,
                out _));
            Assert.IsTrue(retry.TryGetWriter(block, out writer));
            Assert.IsTrue(writer.Write(storedSlot, 4));
            CoCoContextCommitResult retryResult = FinalizeAndCommit(retry);
            Assert.AreEqual(8, retryResult.Frame.Read(derivedSlot));
            Assert.AreEqual(4, rebuilder.InvocationCount);

            rebuilder.ShouldThrow = true;
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(4UL, 1UL, 4UL),
                out CoCoPreparedContextCommit throwing,
                out _));
            Assert.Throws<InvalidOperationException>(() => throwing.TryFinalize(out _, out _));
            Assert.IsFalse(throwing.IsValid);
            Assert.AreEqual(3UL, arena.Current.Revision.Value);
            Assert.AreEqual(4, arena.Current.Read(storedSlot));
            Assert.AreEqual(5, rebuilder.InvocationCount);
        }

        [Test]
        public void ContextFramesAreGraphIsolatedAndRejectForeignLayoutHandles()
        {
            CoCoStateSlotId valueSlotId = CreateSlotId(30UL, 1UL);
            CoCoContextFrameLayout firstLayout = CreateStoredIntLayout(
                CreateLayoutId(30UL, 1UL),
                valueSlotId,
                17);
            CoCoContextFrameLayout secondLayout = CreateStoredIntLayout(
                CreateLayoutId(30UL, 2UL),
                CreateSlotId(30UL, 2UL),
                99);
            CoCoGraphInstanceId firstGraph = CreateGraphInstanceId(31UL);
            CoCoGraphInstanceId secondGraph = CreateGraphInstanceId(32UL);
            var firstArena = new CoCoContextFrameArena(firstGraph, firstLayout, 3);
            var secondArena = new CoCoContextFrameArena(secondGraph, firstLayout, 3);

            Assert.IsTrue(firstArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit firstPrepared,
                out CoCoContextCommitStatus status));
            CoCoContextFrame firstFrame = FinalizeAndCommit(firstPrepared).Frame;
            Assert.AreEqual(firstGraph, firstFrame.Header.Identity.GraphInstanceId);

            Assert.IsTrue(secondArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit secondPrepared,
                out status));
            CoCoContextFrame secondFrame = FinalizeAndCommit(secondPrepared).Frame;
            Assert.AreEqual(secondGraph, secondFrame.Header.Identity.GraphInstanceId);
            Assert.AreNotEqual(firstFrame.Header.Identity, secondFrame.Header.Identity);

            Assert.IsFalse(secondArena.TryPrepareRestore(
                firstFrame,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.GraphMismatch, status);

            Assert.IsTrue(firstLayout.TryResolveSlot(valueSlotId, out CoCoStateSlot<int> validHandle));
            Assert.AreEqual(17, firstFrame.Read(validHandle));
            Assert.IsTrue(secondLayout.TryResolveSlot(
                CreateSlotId(30UL, 2UL),
                out CoCoStateSlot<int> foreignHandle));
            Assert.Throws<InvalidOperationException>(() => firstFrame.Read(foreignHandle));
        }

        [Test]
        public void ContextLayoutVersionSchemaAndInstanceRejectCrossUse()
        {
            CoCoFrameLayoutId layoutId = CreateLayoutId(31UL, 1UL);
            CoCoStateBlockId blockId = CreateBlockId(31UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(31UL, 1UL);
            CoCoStateSlotId derivedSlotId = CreateSlotId(31UL, 2UL);
            CoCoCodecDescriptor codec = CreateCodecDescriptor(31UL, 1U);
            CoCoContextFrameLayout layout = CreateProjectionLayout(
                layoutId, blockId, storedSlotId, derivedSlotId, codec, 1U, 0);
            CoCoContextFrameLayout versionTwo = CreateProjectionLayout(
                layoutId, blockId, storedSlotId, derivedSlotId, codec, 2U, 0);
            CoCoContextFrameLayout separateInstance = CreateProjectionLayout(
                layoutId, blockId, storedSlotId, derivedSlotId, codec, 1U, 0);
            CoCoContextFrameLayout changedSchema = CreateProjectionLayout(
                layoutId, blockId, storedSlotId, derivedSlotId, codec, 1U, 99);
            CoCoGraphInstanceId graphInstanceId = CreateGraphInstanceId(31UL);
            var arena = new CoCoContextFrameArena(graphInstanceId, layout, 2);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit prepared,
                out CoCoContextCommitStatus status));
            CoCoContextFrame frame = FinalizeAndCommit(prepared).Frame;
            Assert.AreEqual(1U, frame.Header.LayoutVersion);
            Assert.AreEqual(layout.SchemaHash, frame.Header.LayoutSchemaHash);
            Assert.AreEqual(layout.SchemaHash, separateInstance.SchemaHash);
            Assert.AreNotEqual(layout.SchemaHash, changedSchema.SchemaHash);

            Assert.IsTrue(versionTwo.TryResolveSlot(
                storedSlotId,
                out CoCoStateSlot<int> versionTwoHandle));
            Assert.Throws<InvalidOperationException>(() => frame.Read(versionTwoHandle));
            var versionTwoArena = new CoCoContextFrameArena(graphInstanceId, versionTwo, 2);
            Assert.IsFalse(versionTwoArena.TryPrepareRestore(
                frame,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.LayoutMismatch, status);

            Assert.IsTrue(separateInstance.TryResolveSlot(
                storedSlotId,
                out CoCoStateSlot<int> separateHandle));
            Assert.Throws<InvalidOperationException>(() => frame.Read(separateHandle));
            var separateArena = new CoCoContextFrameArena(graphInstanceId, separateInstance, 2);
            Assert.IsFalse(separateArena.TryPrepareRestore(
                frame,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.LayoutMismatch, status);
        }

        [Test]
        public void NoOpCommitIncrementsRevisionAndCancelPreservesAuthority()
        {
            CoCoStateSlotId slotId = CreateSlotId(40UL, 1UL);
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(40UL, 1UL),
                slotId,
                42);
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(40UL), layout, 3);
            Assert.IsTrue(layout.TryResolveSlot(slotId, out CoCoStateSlot<int> slot));

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit firstPrepared,
                out CoCoContextCommitStatus status));
            CoCoContextCommitResult firstResult = FinalizeAndCommit(firstPrepared);
            Assert.IsTrue(firstResult.Succeeded);
            Assert.AreEqual(1UL, firstResult.Frame.Revision.Value);
            Assert.AreEqual(42, firstResult.Frame.Read(slot));

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out CoCoPreparedContextCommit noOpPrepared,
                out status));
            CoCoContextCommitResult noOpResult = FinalizeAndCommit(noOpPrepared);
            Assert.IsTrue(noOpResult.Succeeded);
            Assert.AreEqual(2UL, noOpResult.Frame.Revision.Value);
            Assert.AreEqual(42, noOpResult.Frame.Read(slot));

            CoCoContextFrame authoritative = arena.Current;
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out CoCoPreparedContextCommit cancelled,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, cancelled.Cancel());
            Assert.AreEqual(authoritative, arena.Current);
            Assert.AreEqual(2UL, arena.Current.Revision.Value);
            Assert.IsFalse(cancelled.TryFinalize(out _, out status));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidPreparation, status);
        }

        [Test]
        public void PreparedAndFinalizedTokensCannotCrossCancelTheirTypeState()
        {
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(42UL, 1UL),
                CreateSlotId(42UL, 1UL),
                1);
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(42UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryFinalize(
                out CoCoFinalizedContextCommit finalized,
                out _));

            Assert.IsFalse(prepared.IsValid);
            Assert.AreEqual(CoCoContextCommitStatus.InvalidPreparation, prepared.Cancel());
            Assert.IsTrue(finalized.IsValid);
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, finalized.Cancel());
            Assert.IsFalse(finalized.IsValid);
        }

        [Test]
        public void ContextArenaRejectsCallbackReentrancyAndTokenExhaustion()
        {
            CoCoStateBlockId blockId = CreateBlockId(43UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(43UL, 1UL);
            CoCoStateSlotId derivedSlotId = CreateSlotId(43UL, 2UL);
            var rebuilder = new ReentrantRebuilder(storedSlotId);
            var builder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(blockId, CoCoStateBlockOwner.Actor, out _));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                2,
                default,
                null,
                out _));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                rebuilder,
                out _));
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(43UL, 1UL),
                1U,
                out CoCoContextFrameLayout layout,
                out _));
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(43UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            rebuilder.Arena = arena;
            rebuilder.Token = ReadPrivateToken(arena);

            CoCoContextCommitResult result = FinalizeAndCommit(prepared);
            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(rebuilder.PrepareRejected);
            Assert.IsTrue(rebuilder.FinalizeRejected);
            Assert.IsTrue(rebuilder.CancelRejected);
            Assert.IsTrue(rebuilder.CommitRejected);

            var restoreArena = new CoCoContextFrameArena(CreateGraphInstanceId(43UL), layout, 2);
            rebuilder.Arena = restoreArena;
            rebuilder.Token = 0UL;
            Assert.IsTrue(restoreArena.TryPrepareRestore(
                result.Frame,
                CreateTickFrame(2UL, 2UL, 2UL),
                out CoCoFinalizedContextCommit restored,
                out _));
            Assert.IsTrue(rebuilder.PrepareRejected);
            Assert.IsTrue(rebuilder.FinalizeRejected);
            Assert.IsTrue(rebuilder.CancelRejected);
            Assert.IsTrue(rebuilder.CommitRejected);
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, restored.Cancel());

            var exhaustedArena = new CoCoContextFrameArena(CreateGraphInstanceId(44UL), layout, 2);
            FieldInfo tokenField = typeof(CoCoContextFrameArena).GetField(
                "_nextToken",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(tokenField);
            tokenField.SetValue(exhaustedArena, ulong.MaxValue - 1UL);
            Assert.IsTrue(exhaustedArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit lastPrepared,
                out _));
            Assert.AreEqual(ulong.MaxValue, ReadPrivateToken(exhaustedArena));
            Assert.IsTrue(lastPrepared.TryFinalize(
                out CoCoFinalizedContextCommit lastFinalized,
                out _));
            Assert.IsTrue(lastFinalized.Commit().Succeeded);
            Assert.IsFalse(lastPrepared.IsValid);
            Assert.IsFalse(lastFinalized.IsValid);
            Assert.IsFalse(exhaustedArena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out _,
                out CoCoContextCommitStatus exhaustedStatus));
            Assert.AreEqual(CoCoContextCommitStatus.RevisionExhausted, exhaustedStatus);
            Assert.IsTrue(exhaustedArena.HasCurrent);
            Assert.AreEqual(CoCoContextCommitStatus.InvalidPreparation, lastPrepared.Cancel());
            Assert.AreEqual(CoCoContextCommitStatus.InvalidPreparation, lastFinalized.Cancel());
        }

        [Test]
        public void CommitHarnessPublishesAndConsumesEventSequenceOnlyAfterSuccess()
        {
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(41UL, 1UL),
                CreateSlotId(41UL, 1UL),
                1);
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(41UL), layout, 2);
            var harness = new CommitEventHarness();

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit cancelled,
                out _));
            Assert.IsTrue(harness.Begin(cancelled));
            Assert.IsTrue(harness.StageEvent());
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, harness.Cancel());
            Assert.AreEqual(0, harness.PublishedCount);
            Assert.AreEqual(CoCoEventSequence.Zero, harness.LastSequence);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit noEventPrepared,
                out _));
            Assert.IsTrue(harness.Begin(noEventPrepared));
            Assert.IsTrue(harness.TryCommitAndPublish());
            Assert.AreEqual(0, harness.PublishedCount);
            Assert.AreEqual(CoCoEventSequence.Zero, harness.LastSequence);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            Assert.IsTrue(harness.Begin(prepared));
            Assert.IsTrue(harness.StageEvent());
            Assert.IsTrue(harness.TryCommitAndPublish());
            Assert.AreEqual(1, harness.PublishedCount);
            Assert.IsTrue(harness.LastSequence.IsValid);
            Assert.AreEqual(1UL, harness.LastSequence.Value);
        }

        [Test]
        public void RestoreRequiresNewEpochAndRecordsSourceIdentity()
        {
            CoCoFrameLayoutId layoutId = CreateLayoutId(50UL, 1UL);
            CoCoStateBlockId blockId = CreateBlockId(50UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(50UL, 1UL);
            CoCoStateSlotId resetSlotId = CreateSlotId(50UL, 2UL);
            CoCoStateSlotId derivedSlotId = CreateSlotId(50UL, 3UL);
            var builder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable,
                CoCoContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                resetSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.ResetToDefault,
                5,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                new DoubleRebuilder(storedSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(layoutId, 1U, out CoCoContextFrameLayout layout, out diagnosticCode));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out CoCoStateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(storedSlotId, out CoCoStateSlot<int> storedSlot));
            Assert.IsTrue(layout.TryResolveSlot(resetSlotId, out CoCoStateSlot<int> resetSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out CoCoStateSlot<int> derivedSlot));
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(50UL), layout, 3);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(9UL, 3UL, 4UL),
                out CoCoPreparedContextCommit initial,
                out CoCoContextCommitStatus status));
            Assert.IsTrue(initial.TryGetWriter(block, out CoCoContextFrameWriter writer));
            Assert.IsTrue(writer.Write(storedSlot, 7));
            Assert.IsTrue(writer.Write(resetSlot, 9));
            Assert.IsFalse(writer.Write(derivedSlot, 99));
            CoCoContextFrame source = FinalizeAndCommit(initial).Frame;
            Assert.IsTrue(source.Retain());
            Assert.AreEqual(7, source.Read(storedSlot));
            Assert.AreEqual(9, source.Read(resetSlot));
            Assert.AreEqual(14, source.Read(derivedSlot));

            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 3UL, 5UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);

            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 4UL, 5UL, 2UL, 2UL, 1UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);
            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 4UL, 5UL, 1UL, 1UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);
            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 4UL, 4UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);

            Assert.IsTrue(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 4UL, 5UL),
                out CoCoFinalizedContextCommit restore,
                out status));
            CoCoContextFrame restored = restore.Commit().Frame;
            Assert.AreEqual(2UL, restored.Revision.Value);
            Assert.AreEqual(new CoCoTimelineEpoch(4UL), restored.Header.Identity.TimelineEpoch);
            Assert.IsTrue(restored.Origin.IsRestore);
            Assert.AreEqual(source.Header.Identity.GraphInstanceId, restored.Origin.SourceGraphInstanceId);
            Assert.AreEqual(new CoCoTimelineEpoch(3UL), restored.Origin.SourceTimelineEpoch);
            Assert.AreEqual(new CoCoTimelineTick(9UL), restored.Origin.SourceTick);
            Assert.AreEqual(new CoCoContextRevision(1UL), restored.Origin.SourceRevision);
            Assert.AreEqual(7, restored.Read(storedSlot));
            Assert.AreEqual(5, restored.Read(resetSlot));
            Assert.AreEqual(14, restored.Read(derivedSlot));
            Assert.IsTrue(source.Release());
        }

        [Test]
        public void RestoreEpochMustAdvanceBeyondTheCurrentAuthority()
        {
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(51UL, 1UL),
                CreateSlotId(51UL, 1UL),
                1);
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(51UL), layout, 3);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(5UL, 5UL, 5UL),
                out CoCoPreparedContextCommit initial,
                out CoCoContextCommitStatus status));
            CoCoContextFrame source = FinalizeAndCommit(initial).Frame;
            Assert.IsTrue(source.Retain());

            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(2UL, 4UL, 6UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);

            Assert.IsTrue(arena.TryPrepareRestore(
                source,
                CreateTickFrame(2UL, 10UL, 6UL),
                out CoCoFinalizedContextCommit advanced,
                out status));
            Assert.IsTrue(advanced.Commit().Succeeded);
            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(3UL, 6UL, 7UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);
            Assert.IsTrue(source.Release());
        }

        [Test]
        public void RetainedFrameConsumesFixedArenaCapacityUntilReleased()
        {
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(60UL, 1UL),
                CreateSlotId(60UL, 1UL),
                1);
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(60UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit firstPrepared,
                out CoCoContextCommitStatus status));
            CoCoContextFrame first = FinalizeAndCommit(firstPrepared).Frame;
            Assert.IsFalse(first.Release());
            Assert.IsTrue(first.Retain());

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out CoCoPreparedContextCommit secondPrepared,
                out status));
            Assert.IsTrue(FinalizeAndCommit(secondPrepared).Succeeded);

            Assert.IsFalse(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out _,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.CapacityExhausted, status);
            Assert.IsTrue(first.Release());
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out CoCoPreparedContextCommit available,
                out status));
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, available.Cancel());
        }

        [Test]
        public void ArenaOwnershipAndMaximumExternalRetainsDoNotOverflowFrameLiveness()
        {
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(60UL, 2UL),
                CreateSlotId(60UL, 2UL),
                1);
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(602UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            CoCoContextFrame frame = FinalizeAndCommit(prepared).Frame;

            FieldInfo storageField = typeof(CoCoContextFrame).GetField(
                "_storage",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(storageField);
            object storage = storageField.GetValue(frame);
            Assert.NotNull(storage);
            FieldInfo retainCountField = storage.GetType().GetField(
                "_externalRetainCount",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(retainCountField);
            retainCountField.SetValue(storage, int.MaxValue - 1);

            Assert.IsTrue(frame.IsAlive);
            Assert.IsTrue(arena.HasCurrent);
            Assert.IsTrue(frame.Retain());
            Assert.IsTrue(frame.IsAlive);
            Assert.IsTrue(arena.HasCurrent);
            Assert.IsFalse(frame.Retain());
        }

        [Test]
        public void ReusedArenaStorageNeverRevivesAnExpiredFrameHandle()
        {
            CoCoStateSlotId slotId = CreateSlotId(61UL, 1UL);
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(61UL, 1UL),
                slotId,
                0);
            Assert.IsTrue(layout.TryResolveBlock(
                CreateBlockId(61UL, 1UL),
                out CoCoStateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(slotId, out CoCoStateSlot<int> slot));
            CoCoGraphInstanceId graphInstanceId = CreateGraphInstanceId(61UL);
            var arena = new CoCoContextFrameArena(graphInstanceId, layout, 2);

            CoCoContextFrame first = CommitStoredValue(arena, block, slot, 1UL, 1);
            CoCoContextFrame second = CommitStoredValue(arena, block, slot, 2UL, 2);
            Assert.IsFalse(first.IsAlive);
            Assert.AreEqual(2, second.Read(slot));

            CoCoContextFrame third = CommitStoredValue(arena, block, slot, 3UL, 3);
            Assert.IsFalse(first.IsAlive);
            Assert.IsFalse(first.Retain());
            Assert.IsFalse(first.Release());
            Assert.AreNotEqual(first, third);
            Assert.Throws<InvalidOperationException>(() => first.Read(slot));
            Assert.AreEqual(3, third.Read(slot));

            var registry = new CoCoContextCodecRegistry();
            Assert.IsTrue(registry.TryFreeze(out _));
            Assert.IsTrue(CoCoContextProjectionCodec.TryCreate(
                layout,
                registry,
                CoCoContextProjection.Temporal,
                out CoCoContextProjectionCodec codec,
                out _));
            var encoded = new byte[codec.MaxEncodedSize];
            Assert.IsFalse(codec.TryEncode(
                first,
                encoded,
                out _,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrameHandle, diagnosticCode);

            var restoreArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsFalse(restoreArena.TryPrepareRestore(
                first,
                CreateTickFrame(4UL, 4UL, 4UL),
                out _,
                out CoCoContextCommitStatus status));
            Assert.AreEqual(CoCoContextCommitStatus.LayoutMismatch, status);
        }

        [Test]
        public void ExplicitCodecContractRoundTripsExactValueAndVersion()
        {
            var codec = new Int32Codec(CreateCodecDescriptor(70UL, 3U));
            const int expected = unchecked((int)0x89abcdef);
            var bytes = new byte[codec.MaxEncodedSize];

            Assert.IsTrue(codec.Descriptor.IsValid);
            Assert.AreEqual(3U, codec.Descriptor.Version);
            Assert.IsTrue(codec.TryEncode(expected, bytes, out int bytesWritten));
            Assert.AreEqual(4, bytesWritten);
            Assert.IsTrue(codec.TryDecode(bytes, out int actual, out int bytesRead));
            Assert.AreEqual(4, bytesRead);
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void CodecRegistryAndProjectionRoundTripRestoreStoredAndDerivedState()
        {
            CoCoFrameLayoutId layoutId = CreateLayoutId(72UL, 1UL);
            CoCoStateBlockId blockId = CreateBlockId(72UL, 1UL);
            CoCoStateSlotId temporalSlotId = CreateSlotId(72UL, 1UL);
            CoCoStateSlotId durableSlotId = CreateSlotId(72UL, 2UL);
            CoCoStateSlotId derivedSlotId = CreateSlotId(72UL, 3UL);
            CoCoCodecDescriptor codecDescriptor = CreateCodecDescriptor(72UL, 1U);
            CoCoContextFrameLayout layout = CreateProjectionLayout(
                layoutId,
                blockId,
                temporalSlotId,
                durableSlotId,
                derivedSlotId,
                codecDescriptor,
                1U);
            var registry = new CoCoContextCodecRegistry();
            var codec = new Int32Codec(codecDescriptor);

            Assert.IsTrue(registry.TryRegister(codec, out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(registry.TryFreeze(out diagnosticCode));
            Assert.IsTrue(registry.TryResolve(
                codecDescriptor,
                out ICoCoContextValueCodec<int> resolved,
                out diagnosticCode));
            Assert.AreSame(codec, resolved);
            Assert.IsFalse(registry.TryResolve(
                CreateCodecDescriptor(720UL, 1U),
                out ICoCoContextValueCodec<int> _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.UnknownCodec, diagnosticCode);
            Assert.IsFalse(registry.TryResolve(
                new CoCoCodecDescriptor(codecDescriptor.CodecId, 2U),
                out ICoCoContextValueCodec<int> _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.UnsupportedCodecVersion, diagnosticCode);
            Assert.IsFalse(registry.TryResolve(
                codecDescriptor,
                out ICoCoContextValueCodec<float> _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateSlot, diagnosticCode);

            Assert.IsTrue(CoCoContextProjectionCodec.TryCreate(
                layout,
                registry,
                CoCoContextProjection.Temporal,
                out CoCoContextProjectionCodec temporalCodec,
                out diagnosticCode));
            Assert.IsTrue(CoCoContextProjectionCodec.TryCreate(
                layout,
                registry,
                CoCoContextProjection.Durable,
                out CoCoContextProjectionCodec durableCodec,
                out diagnosticCode));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out CoCoStateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(temporalSlotId, out CoCoStateSlot<int> temporalSlot));
            Assert.IsTrue(layout.TryResolveSlot(durableSlotId, out CoCoStateSlot<int> durableSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out CoCoStateSlot<int> derivedSlot));
            CoCoGraphInstanceId graphInstanceId = CreateGraphInstanceId(72UL);
            var sourceArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit sourceCommit,
                out _));
            Assert.IsTrue(sourceCommit.TryGetWriter(block, out CoCoContextFrameWriter writer));
            Assert.IsTrue(writer.Write(temporalSlot, 7));
            Assert.IsTrue(writer.Write(durableSlot, 9));
            CoCoContextFrame sourceFrame = FinalizeAndCommit(sourceCommit).Frame;

            var temporalBytes = new byte[temporalCodec.MaxEncodedSize];
            Assert.IsTrue(temporalCodec.TryEncode(
                sourceFrame,
                temporalBytes,
                out int temporalLength,
                out diagnosticCode));

            var oldFormat = new byte[temporalLength];
            Buffer.BlockCopy(temporalBytes, 0, oldFormat, 0, temporalLength);
            oldFormat[4] = 1;
            var wrongLayout = new byte[temporalLength];
            Buffer.BlockCopy(temporalBytes, 0, wrongLayout, 0, temporalLength);
            wrongLayout[12] ^= 1;
            var temporalArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                oldFormat,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out _,
                out _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                temporalBytes,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL, 2UL, 2UL, 1UL),
                out _,
                out _,
                out CoCoContextCommitStatus status,
                out diagnosticCode));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                temporalBytes,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL, 1UL, 1UL, 2UL),
                out _,
                out _,
                out status,
                out diagnosticCode));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                temporalBytes,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 1UL),
                out _,
                out _,
                out status,
                out diagnosticCode));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                wrongLayout,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out _,
                out _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrameLayout, diagnosticCode);

            var unknownCodec = new byte[temporalLength];
            Buffer.BlockCopy(temporalBytes, 0, unknownCodec, 0, temporalLength);
            unknownCodec[124] ^= 1;
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                unknownCodec,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out _,
                out _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.UnknownCodec, diagnosticCode);

            var unsupportedCodecVersion = new byte[temporalLength];
            Buffer.BlockCopy(temporalBytes, 0, unsupportedCodecVersion, 0, temporalLength);
            unsupportedCodecVersion[140] = 2;
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                unsupportedCodecVersion,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out _,
                out _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.UnsupportedCodecVersion, diagnosticCode);

            var decodeFailure = new byte[temporalLength];
            Buffer.BlockCopy(temporalBytes, 0, decodeFailure, 0, temporalLength);
            decodeFailure[152] = 3;
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                decodeFailure,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out _,
                out _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.UnknownCodec, diagnosticCode);

            Assert.IsTrue(temporalCodec.TryDecodeAndPrepareRestore(
                new ReadOnlySpan<byte>(temporalBytes, 0, temporalLength),
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out CoCoFinalizedContextCommit temporalRestore,
                out int temporalRead,
                out status,
                out diagnosticCode),
                $"{diagnosticCode}/{status}");
            CoCoContextFrame temporalFrame = temporalRestore.Commit().Frame;
            Assert.AreEqual(temporalLength, temporalRead);
            Assert.AreEqual(7, temporalFrame.Read(temporalSlot));
            Assert.AreEqual(2, temporalFrame.Read(durableSlot));
            Assert.AreEqual(14, temporalFrame.Read(derivedSlot));
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                new ReadOnlySpan<byte>(temporalBytes, 0, temporalLength),
                temporalArena,
                CreateTickFrame(3UL, 2UL, 3UL),
                out _,
                out _,
                out status,
                out diagnosticCode));
            Assert.AreEqual(CoCoContextCommitStatus.InvalidOrigin, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);

            var durableBytes = new byte[durableCodec.MaxEncodedSize];
            Assert.IsTrue(durableCodec.TryEncode(
                sourceFrame,
                durableBytes,
                out int durableLength,
                out diagnosticCode));
            var foreignGraphArena = new CoCoContextFrameArena(
                CreateGraphInstanceId(720UL),
                layout,
                2);
            Assert.IsFalse(durableCodec.TryDecodeAndPrepareRestore(
                new ReadOnlySpan<byte>(durableBytes, 0, durableLength),
                foreignGraphArena,
                CreateTickFrame(2UL, 3UL, 3UL),
                out _,
                out _,
                out status,
                out diagnosticCode));
            Assert.AreEqual(CoCoContextCommitStatus.GraphMismatch, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrameHandle, diagnosticCode);
            var durableArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(durableCodec.TryDecodeAndPrepareRestore(
                new ReadOnlySpan<byte>(durableBytes, 0, durableLength),
                durableArena,
                CreateTickFrame(2UL, 3UL, 3UL),
                out CoCoFinalizedContextCommit durableRestore,
                out int durableRead,
                out status,
                out diagnosticCode),
                $"{diagnosticCode}/{status}");
            CoCoContextFrame durableFrame = durableRestore.Commit().Frame;
            Assert.AreEqual(durableLength, durableRead);
            Assert.AreEqual(1, durableFrame.Read(temporalSlot));
            Assert.AreEqual(9, durableFrame.Read(durableSlot));
            Assert.AreEqual(2, durableFrame.Read(derivedSlot));
        }

        [Test]
        public void ProjectionCodecExceptionRollsBackCandidateAndRethrows()
        {
            CoCoFrameLayoutId layoutId = CreateLayoutId(73UL, 1UL);
            CoCoStateBlockId blockId = CreateBlockId(73UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(73UL, 1UL);
            CoCoStateSlotId derivedSlotId = CreateSlotId(73UL, 2UL);
            CoCoCodecDescriptor codecDescriptor = CreateCodecDescriptor(73UL, 1U);
            CoCoContextFrameLayout layout = CreateProjectionLayout(
                layoutId,
                blockId,
                storedSlotId,
                derivedSlotId,
                codecDescriptor,
                1U,
                4);
            var registry = new CoCoContextCodecRegistry();
            var codec = new ThrowOnDecodeInt32Codec(codecDescriptor);
            Assert.IsTrue(registry.TryRegister(
                codec,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(registry.TryFreeze(out diagnosticCode));
            Assert.IsTrue(CoCoContextProjectionCodec.TryCreate(
                layout,
                registry,
                CoCoContextProjection.Temporal,
                out CoCoContextProjectionCodec projectionCodec,
                out diagnosticCode));

            CoCoGraphInstanceId graphInstanceId = CreateGraphInstanceId(73UL);
            var sourceArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit sourceCommit,
                out _));
            CoCoContextFrame source = FinalizeAndCommit(sourceCommit).Frame;
            var encoded = new byte[projectionCodec.MaxEncodedSize];
            Assert.IsTrue(projectionCodec.TryEncode(
                source,
                encoded,
                out int encodedLength,
                out diagnosticCode));

            var restoreArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            codec.Arena = restoreArena;
            var decodeException = Assert.Throws<InvalidOperationException>(() =>
                projectionCodec.TryDecodeAndPrepareRestore(
                    new ReadOnlySpan<byte>(encoded, 0, encodedLength),
                    restoreArena,
                    CreateTickFrame(2UL, 2UL, 2UL),
                    out _,
                    out _,
                    out _,
                    out _));
            Assert.AreEqual("Test codec decode failure.", decodeException.Message);
            Assert.IsTrue(codec.PrepareRejected);
            Assert.IsTrue(codec.FinalizeRejected);
            Assert.IsTrue(codec.CancelRejected);
            Assert.IsTrue(codec.CommitRejected);
            Assert.IsTrue(restoreArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out CoCoPreparedContextCommit afterException,
                out _));
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, afterException.Cancel());
        }

        [Test]
        public void RestoreDerivedFailureAndExceptionRollBackDirectAndProjectionCandidates()
        {
            CoCoFrameLayoutId layoutId = CreateLayoutId(74UL, 1UL);
            CoCoStateBlockId blockId = CreateBlockId(74UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(74UL, 1UL);
            CoCoStateSlotId derivedSlotId = CreateSlotId(74UL, 2UL);
            var rebuilder = new ControllableDoubleRebuilder(storedSlotId);
            var builder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(blockId, CoCoStateBlockOwner.Actor, out _));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                3,
                default,
                null,
                out _));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                rebuilder,
                out _));
            Assert.IsTrue(builder.TryFreeze(layoutId, 1U, out CoCoContextFrameLayout layout, out _));
            var registry = new CoCoContextCodecRegistry();
            Assert.IsTrue(registry.TryFreeze(out _));
            Assert.IsTrue(CoCoContextProjectionCodec.TryCreate(
                layout,
                registry,
                CoCoContextProjection.Temporal,
                out CoCoContextProjectionCodec projectionCodec,
                out _));

            CoCoGraphInstanceId graphInstanceId = CreateGraphInstanceId(74UL);
            var sourceArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit sourceCommit,
                out _));
            CoCoContextFrame source = FinalizeAndCommit(sourceCommit).Frame;
            var encoded = new byte[projectionCodec.MaxEncodedSize];
            Assert.IsTrue(projectionCodec.TryEncode(source, encoded, out int encodedLength, out _));
            rebuilder.ShouldFail = true;

            var projectionFailureArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsFalse(projectionCodec.TryDecodeAndPrepareRestore(
                new ReadOnlySpan<byte>(encoded, 0, encodedLength),
                projectionFailureArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out _,
                out CoCoContextCommitStatus projectionFailureStatus,
                out CoCoDiagnosticCode projectionFailureDiagnostic));
            Assert.AreEqual(
                CoCoContextCommitStatus.DerivedRebuildFailed,
                projectionFailureStatus);
            Assert.AreEqual(
                CoCoDiagnosticCode.CommitPreparationFailed,
                projectionFailureDiagnostic);
            Assert.IsTrue(projectionFailureArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out CoCoPreparedContextCommit afterProjectionFailure,
                out _));
            Assert.AreEqual(
                CoCoContextCommitStatus.Cancelled,
                afterProjectionFailure.Cancel());

            var directFailureArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsFalse(directFailureArena.TryPrepareRestore(
                source,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out CoCoContextCommitStatus directFailureStatus));
            Assert.AreEqual(
                CoCoContextCommitStatus.DerivedRebuildFailed,
                directFailureStatus);
            Assert.IsTrue(directFailureArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out CoCoPreparedContextCommit afterDirectFailure,
                out _));
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, afterDirectFailure.Cancel());

            rebuilder.ShouldFail = false;
            rebuilder.ShouldThrow = true;

            var projectionArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            var projectionException = Assert.Throws<InvalidOperationException>(() =>
                projectionCodec.TryDecodeAndPrepareRestore(
                    new ReadOnlySpan<byte>(encoded, 0, encodedLength),
                    projectionArena,
                    CreateTickFrame(2UL, 2UL, 2UL),
                    out _,
                    out _,
                    out _,
                    out _));
            Assert.AreEqual("Test rebuilder failure.", projectionException.Message);
            Assert.IsTrue(projectionArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out CoCoPreparedContextCommit afterProjectionException,
                out _));
            Assert.AreEqual(
                CoCoContextCommitStatus.Cancelled,
                afterProjectionException.Cancel());

            var directArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            var directException = Assert.Throws<InvalidOperationException>(() =>
                directArena.TryPrepareRestore(
                    source,
                    CreateTickFrame(2UL, 2UL, 2UL),
                    out _,
                    out _));
            Assert.AreEqual("Test rebuilder failure.", directException.Message);
            Assert.IsTrue(directArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out CoCoPreparedContextCommit afterDirectException,
                out _));
            Assert.AreEqual(CoCoContextCommitStatus.Cancelled, afterDirectException.Cancel());
        }

        [Test]
        public void ContextReadAndCodecRoundTripAllocateNoManagedMemoryAfterWarmup()
        {
            CoCoStateSlotId slotId = CreateSlotId(71UL, 1UL);
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(71UL, 1UL),
                slotId,
                42);
            Assert.IsTrue(layout.TryResolveSlot(slotId, out CoCoStateSlot<int> slot));
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(71UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            CoCoContextFrame frame = FinalizeAndCommit(prepared).Frame;
            var codec = new Int32Codec(CreateCodecDescriptor(71UL, 1U));
            var bytes = new byte[codec.MaxEncodedSize];
            int checksum = 0;
            bool succeeded = true;

            for (int index = 0; index < 100; index++)
            {
                int value = frame.Read(slot);
                succeeded &= codec.TryEncode(value, bytes, out int written) && written == 4;
                succeeded &= codec.TryDecode(bytes, out int decoded, out int read) && read == 4;
                checksum += decoded;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                int value = frame.Read(slot);
                succeeded &= codec.TryEncode(value, bytes, out int written) && written == 4;
                succeeded &= codec.TryDecode(bytes, out int decoded, out int read) && read == 4;
                checksum += decoded;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(424200, checksum);
            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void ContextFrameRetainReleaseAllocatesNoManagedMemoryAfterWarmup()
        {
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(71UL, 2UL),
                CreateSlotId(71UL, 2UL),
                42);
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(712UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit prepared,
                out _));
            CoCoContextFrame frame = FinalizeAndCommit(prepared).Frame;
            bool succeeded = true;

            for (int index = 0; index < 100; index++)
            {
                succeeded &= frame.Retain();
                succeeded &= frame.Release();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= frame.Retain();
                succeeded &= frame.Release();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.IsTrue(frame.IsAlive);
            Assert.IsTrue(arena.HasCurrent);
            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void ContextFinalizeCommitAllocatesNoManagedMemoryAfterWarmup()
        {
            CoCoContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(74UL, 1UL),
                CreateSlotId(74UL, 1UL),
                42);
            var arena = new CoCoContextFrameArena(CreateGraphInstanceId(74UL), layout, 2);
            var ticks = new CoCoTickFrame[10100];
            for (int index = 0; index < ticks.Length; index++)
            {
                ulong frame = (ulong)index + 1UL;
                ticks[index] = CreateTickFrame(frame, 1UL, frame);
            }

            bool succeeded = true;
            for (int index = 0; index < 100; index++)
            {
                succeeded &= RunContextCommitCycle(arena, ticks[index]);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= RunContextCommitCycle(arena, ticks[index + 100]);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(10100UL, arena.Current.Revision.Value);
            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void ContextProjectionCodecHotPathAllocatesNoManagedMemoryAfterWarmup()
        {
            CoCoFrameLayoutId layoutId = CreateLayoutId(73UL, 1UL);
            CoCoStateBlockId blockId = CreateBlockId(73UL, 1UL);
            CoCoStateSlotId storedSlotId = CreateSlotId(73UL, 1UL);
            CoCoStateSlotId derivedSlotId = CreateSlotId(73UL, 2UL);
            CoCoCodecDescriptor codecDescriptor = CreateCodecDescriptor(73UL, 1U);
            CoCoContextFrameLayout layout = CreateProjectionLayout(
                layoutId,
                blockId,
                storedSlotId,
                derivedSlotId,
                codecDescriptor,
                1U,
                0);
            var registry = new CoCoContextCodecRegistry();
            Assert.IsTrue(registry.TryRegister(new Int32Codec(codecDescriptor), out _));
            Assert.IsTrue(registry.TryFreeze(out _));
            Assert.IsTrue(CoCoContextProjectionCodec.TryCreate(
                layout,
                registry,
                CoCoContextProjection.Temporal,
                out CoCoContextProjectionCodec projectionCodec,
                out _));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out CoCoStateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(storedSlotId, out CoCoStateSlot<int> storedSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out CoCoStateSlot<int> derivedSlot));
            CoCoGraphInstanceId graphInstanceId = CreateGraphInstanceId(73UL);
            var sourceArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out CoCoPreparedContextCommit sourceCommit,
                out _));
            Assert.IsTrue(sourceCommit.TryGetWriter(block, out CoCoContextFrameWriter writer));
            Assert.IsTrue(writer.Write(storedSlot, 42));
            CoCoContextFrame sourceFrame = FinalizeAndCommit(sourceCommit).Frame;
            var restoreArena = new CoCoContextFrameArena(graphInstanceId, layout, 2);
            var bytes = new byte[projectionCodec.MaxEncodedSize];
            var ticks = new CoCoTickFrame[10100];
            for (int index = 0; index < ticks.Length; index++)
            {
                ulong frame = (ulong)index + 2UL;
                ticks[index] = CreateTickFrame(frame, frame, frame);
            }

            bool succeeded = true;
            int checksum = 0;
            for (int index = 0; index < 100; index++)
            {
                succeeded &= RunProjectionCodecCycle(
                    projectionCodec,
                    sourceFrame,
                    bytes,
                    restoreArena,
                    ticks[index],
                    storedSlot,
                    derivedSlot,
                    ref checksum);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                succeeded &= RunProjectionCodecCycle(
                    projectionCodec,
                    sourceFrame,
                    bytes,
                    restoreArena,
                    ticks[index + 100],
                    storedSlot,
                    derivedSlot,
                    ref checksum);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(succeeded);
            Assert.AreEqual(1272600, checksum);
            Assert.AreEqual(0L, allocated);
        }

        private static bool RunProjectionCodecCycle(
            CoCoContextProjectionCodec codec,
            CoCoContextFrame source,
            byte[] destination,
            CoCoContextFrameArena restoreArena,
            CoCoTickFrame resumedTick,
            CoCoStateSlot<int> storedSlot,
            CoCoStateSlot<int> derivedSlot,
            ref int checksum)
        {
            if (!codec.TryEncode(source, destination, out int written, out _) ||
                !codec.TryDecodeAndPrepareRestore(
                    new ReadOnlySpan<byte>(destination, 0, written),
                    restoreArena,
                    resumedTick,
                    out CoCoFinalizedContextCommit finalized,
                    out int read,
                    out _,
                    out _) ||
                read != written)
            {
                return false;
            }

            CoCoContextCommitResult result = finalized.Commit();
            if (!result.Succeeded)
            {
                return false;
            }

            checksum += result.Frame.Read(storedSlot);
            checksum += result.Frame.Read(derivedSlot);
            return true;
        }

        private static bool RunContextCommitCycle(
            CoCoContextFrameArena arena,
            CoCoTickFrame tickFrame)
        {
            if (!arena.TryPrepare(
                    tickFrame,
                    out CoCoPreparedContextCommit prepared,
                    out _) ||
                !prepared.TryFinalize(
                    out CoCoFinalizedContextCommit finalized,
                    out _))
            {
                return false;
            }

            return finalized.Commit().Succeeded;
        }

        private static CoCoContextCommitResult FinalizeAndCommit(
            CoCoPreparedContextCommit prepared)
        {
            Assert.IsTrue(
                prepared.TryFinalize(
                    out CoCoFinalizedContextCommit finalized,
                    out CoCoContextCommitStatus status),
                status.ToString());
            return finalized.Commit();
        }

        private static CoCoContextFrame CommitStoredValue(
            CoCoContextFrameArena arena,
            CoCoStateBlockHandle block,
            CoCoStateSlot<int> slot,
            ulong frame,
            int value)
        {
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(frame, 1UL, frame),
                out CoCoPreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryGetWriter(block, out CoCoContextFrameWriter writer));
            Assert.IsTrue(writer.Write(slot, value));
            return FinalizeAndCommit(prepared).Frame;
        }

        private static CoCoContextFrameLayout CreateStoredIntLayout(
            CoCoFrameLayoutId layoutId,
            CoCoStateSlotId slotId,
            int defaultValue)
        {
            var builder = new CoCoContextFrameLayoutBuilder();
            CoCoStateBlockId blockId = CreateBlockId(layoutId.High, layoutId.Low);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                slotId,
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable,
                CoCoContextRestorePolicy.Stored,
                defaultValue,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(layoutId, 1U, out CoCoContextFrameLayout layout, out diagnosticCode));
            return layout;
        }

        private static CoCoContextFrameLayout CreateProjectionLayout(
            CoCoFrameLayoutId layoutId,
            CoCoStateBlockId blockId,
            CoCoStateSlotId storedSlotId,
            CoCoStateSlotId derivedSlotId,
            CoCoCodecDescriptor codec,
            uint version,
            int defaultValue)
        {
            var builder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable,
                CoCoContextRestorePolicy.Stored,
                defaultValue,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                CoCoContextProjection.Temporal | CoCoContextProjection.Durable,
                0,
                default,
                new[] { storedSlotId },
                new DoubleRebuilder(storedSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                layoutId,
                version,
                out CoCoContextFrameLayout layout,
                out diagnosticCode));
            return layout;
        }

        private static CoCoContextFrameLayout CreateProjectionLayout(
            CoCoFrameLayoutId layoutId,
            CoCoStateBlockId blockId,
            CoCoStateSlotId temporalSlotId,
            CoCoStateSlotId durableSlotId,
            CoCoStateSlotId derivedSlotId,
            CoCoCodecDescriptor codec,
            uint version)
        {
            var builder = new CoCoContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                temporalSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                1,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                durableSlotId,
                CoCoContextProjection.Durable,
                CoCoContextRestorePolicy.Stored,
                2,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                CoCoContextProjection.Temporal,
                0,
                default,
                new[] { temporalSlotId },
                new DoubleRebuilder(temporalSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                layoutId,
                version,
                out CoCoContextFrameLayout layout,
                out diagnosticCode));
            return layout;
        }

        private static CoCoGraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(CoCoGraphInstanceId.TryCreate(value, out CoCoGraphInstanceId id));
            return id;
        }

        private static CoCoFrameLayoutId CreateLayoutId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(high, low, out CoCoFrameLayoutId id));
            return id;
        }

        private static CoCoStateBlockId CreateBlockId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoStateBlockId.TryCreate(high, low, out CoCoStateBlockId id));
            return id;
        }

        private static CoCoStateSlotId CreateSlotId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoStateSlotId.TryCreate(high, low, out CoCoStateSlotId id));
            return id;
        }

        private static CoCoCodecDescriptor CreateCodecDescriptor(ulong value, uint version)
        {
            Assert.IsTrue(CoCoCodecId.TryCreate(0UL, value, out CoCoCodecId codecId));
            return new CoCoCodecDescriptor(codecId, version);
        }

        private static CoCoOperationSectionId CreateSectionId(ulong high, ulong low)
        {
            Assert.IsTrue(CoCoOperationSectionId.TryCreate(high, low, out CoCoOperationSectionId id));
            return id;
        }

        private static CoCoTickFrame CreateTickFrame(ulong tick, ulong epoch, ulong sequence)
        {
            return CreateTickFrame(tick, epoch, sequence, 1UL, 1UL, 1UL);
        }

        private static CoCoTickFrame CreateTickFrame(
            ulong tick,
            ulong epoch,
            ulong sequence,
            ulong timelineHigh,
            ulong timelineLow,
            ulong clockDomain)
        {
            Assert.IsTrue(CoCoTimelineId.TryCreate(
                timelineHigh,
                timelineLow,
                out CoCoTimelineId timelineId));
            Assert.IsTrue(CoCoTimelinePosition.TryCreate(tick * 0.016d, out CoCoTimelinePosition position));
            Assert.IsTrue(CoCoClockDomainId.TryCreate(clockDomain, out CoCoClockDomainId clockDomainId));
            Assert.IsTrue(CoCoTickFrame.TryCreate(
                0.016d,
                timelineId,
                position,
                new CoCoTimelineTick(tick),
                clockDomainId,
                new CoCoExecutionSequence(sequence),
                new CoCoTimelineEpoch(epoch),
                out CoCoTickFrame frame,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return frame;
        }

        private static ulong ReadPrivateToken(CoCoContextFrameArena arena)
        {
            FieldInfo tokenField = typeof(CoCoContextFrameArena).GetField(
                "_activeToken",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(tokenField);
            return (ulong)tokenField.GetValue(arena);
        }

        private static void AssertCallbackFreeRole(Type roleType)
        {
            Assert.IsTrue(roleType.IsClass, roleType.FullName);
            Assert.IsTrue(roleType.IsAbstract, roleType.FullName);
            Assert.AreEqual(typeof(object), roleType.BaseType, roleType.FullName);
            Assert.AreEqual(
                0,
                roleType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length,
                roleType.FullName);
        }

        private static void AssertInvalidSection<TSection>()
            where TSection : class, ICoCoOperationSection
        {
            Assert.IsFalse(CoCoOperationSectionRequirement.TryCreate<TSection>(
                CreateSectionId(99UL, 1UL),
                CoCoOperationSectionMode.Continuous,
                out _,
                out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidOperationSection, diagnostic.Code);
        }

        private static void AssertShapeField(
            CoCoOperationSectionFieldShape field,
            int denseIndex,
            string name,
            Type valueType,
            int byteOffset,
            int byteSize)
        {
            Assert.AreEqual(denseIndex, field.DenseIndex);
            Assert.AreEqual(name, field.Name);
            Assert.AreEqual(valueType, field.ValueType);
            Assert.AreEqual(byteOffset, field.ByteOffset);
            Assert.AreEqual(byteSize, field.ByteSize);
        }

        private interface IMovementSection : ICoCoOperationSection
        {
            int Distance { get; }
        }

        private interface IManifestOnlySection : ICoCoOperationSection
        {
        }

        private sealed class ManifestSectionClass : ICoCoOperationSection
        {
        }

        private interface IAlternateMovementSection : ICoCoOperationSection
        {
            int Distance { get; }
        }

        private interface ICompositeShapeSection : ICoCoOperationSection
        {
            short Zulu { get; }
            long Middle { get; }
            int Alpha { get; }
        }

        private interface ICompositeShapeTwinSection : ICoCoOperationSection
        {
            int Alpha { get; }
            short Zulu { get; }
            long Middle { get; }
        }

        private interface IDifferentCompositeShapeSection : ICoCoOperationSection
        {
            int Alpha { get; }
            int Middle { get; }
            short Zulu { get; }
        }

        private interface IAttackSection : ICoCoOperationSection
        {
            int Damage { get; }
        }

        private interface INestedValueSection : ICoCoOperationSection
        {
            NestedValue Value { get; }
        }

        private interface IInheritedMovementSection : IMovementSection
        {
            int VerticalDistance { get; }
        }

        private interface IWritableSection : ICoCoOperationSection
        {
            int Value { get; set; }
        }

        private interface IMethodSection : ICoCoOperationSection
        {
            int Value { get; }
            void Apply();
        }

        private interface IEventSection : ICoCoOperationSection
        {
            int Value { get; }
            event Action Changed;
        }

        private interface IIndexerSection : ICoCoOperationSection
        {
            int this[int index] { get; }
        }

        private interface IReferenceSection : ICoCoOperationSection
        {
            object Value { get; }
        }

        private interface IStringSection : ICoCoOperationSection
        {
            string Value { get; }
        }

        private interface IRefReturnSection : ICoCoOperationSection
        {
            ref int Value { get; }
        }

        private sealed class MovementView : IMovementSection
        {
            private readonly CoCoOperationSectionReader _reader;
            private readonly CoCoOperationSectionField<int> _distanceField;

            public MovementView(
                CoCoOperationSectionReader reader,
                CoCoOperationSectionField<int> distanceField)
            {
                _reader = reader;
                _distanceField = distanceField;
            }

            public int Distance => _reader.Read(_distanceField);
        }

        private sealed class AlternateMovementView : IAlternateMovementSection
        {
            public int Distance => 0;
        }

        private sealed class CompositeShapeView : ICompositeShapeSection
        {
            public short Zulu => 0;
            public long Middle => 0L;
            public int Alpha => 0;
        }

        private sealed class AttackView : IAttackSection
        {
            private readonly CoCoOperationSectionReader _reader;
            private readonly CoCoOperationSectionField<int> _damageField;

            public AttackView(
                CoCoOperationSectionReader reader,
                CoCoOperationSectionField<int> damageField)
            {
                _reader = reader;
                _damageField = damageField;
            }

            public int Damage => _reader.Read(_damageField);
        }

        private sealed class NestedValueView : INestedValueSection
        {
            private readonly CoCoOperationSectionReader _reader;
            private readonly CoCoOperationSectionField<NestedValue> _valueField;

            public NestedValueView(
                CoCoOperationSectionReader reader,
                CoCoOperationSectionField<NestedValue> valueField)
            {
                _reader = reader;
                _valueField = valueField;
            }

            public NestedValue Value => _reader.Read(_valueField);
        }

        private sealed class MovementViewFactory : ICoCoOperationSectionViewFactory<IMovementSection>
        {
            public IMovementSection Create(in CoCoOperationSectionViewContext<IMovementSection> context)
            {
                if (!context.TryGetField(0, out CoCoOperationSectionField<int> field))
                {
                    throw new InvalidOperationException("Movement field was not pre-resolved.");
                }

                return new MovementView(context.Reader, field);
            }
        }

        private sealed class AlternateMovementViewFactory :
            ICoCoOperationSectionViewFactory<IAlternateMovementSection>
        {
            public IAlternateMovementSection Create(
                in CoCoOperationSectionViewContext<IAlternateMovementSection> context)
            {
                return new AlternateMovementView();
            }
        }

        private sealed class CompositeShapeViewFactory :
            ICoCoOperationSectionViewFactory<ICompositeShapeSection>
        {
            public ICompositeShapeSection Create(
                in CoCoOperationSectionViewContext<ICompositeShapeSection> context)
            {
                return new CompositeShapeView();
            }
        }

        private sealed class AttackViewFactory : ICoCoOperationSectionViewFactory<IAttackSection>
        {
            public IAttackSection Create(in CoCoOperationSectionViewContext<IAttackSection> context)
            {
                if (!context.TryGetField(0, out CoCoOperationSectionField<int> field))
                {
                    throw new InvalidOperationException("Attack field was not pre-resolved.");
                }

                return new AttackView(context.Reader, field);
            }
        }

        private sealed class NestedValueViewFactory :
            ICoCoOperationSectionViewFactory<INestedValueSection>
        {
            public INestedValueSection Create(
                in CoCoOperationSectionViewContext<INestedValueSection> context)
            {
                if (!context.TryGetField(0, out CoCoOperationSectionField<NestedValue> field))
                {
                    throw new InvalidOperationException("Nested value field was not pre-resolved.");
                }

                return new NestedValueView(context.Reader, field);
            }
        }

        private readonly struct NestedValue
        {
            public NestedValue(int count, bool enabled, short code)
            {
                Count = count;
                Flags = new NestedFlags(enabled, code);
            }

            public int Count { get; }
            public NestedFlags Flags { get; }
        }

        private readonly struct NestedFlags
        {
            public NestedFlags(bool enabled, short code)
            {
                Enabled = enabled;
                Code = code;
            }

            public bool Enabled { get; }
            public short Code { get; }
        }

        private sealed class AddOneRebuilder : ICoCoDerivedStateRebuilder<int>
        {
            private readonly CoCoStateSlotId _dependency;

            public AddOneRebuilder(CoCoStateSlotId dependency)
            {
                _dependency = dependency;
            }

            public bool TryRebuild(in CoCoDerivedStateReadContext context, out int value)
            {
                if (!context.TryRead(_dependency, out int source))
                {
                    value = default;
                    return false;
                }

                value = source + 1;
                return true;
            }
        }

        private sealed class DoubleRebuilder : ICoCoDerivedStateRebuilder<int>
        {
            private readonly CoCoStateSlotId _dependency;

            public DoubleRebuilder(CoCoStateSlotId dependency)
            {
                _dependency = dependency;
            }

            public bool TryRebuild(in CoCoDerivedStateReadContext context, out int value)
            {
                if (!context.TryRead(_dependency, out int source))
                {
                    value = default;
                    return false;
                }

                value = source * 2;
                return true;
            }
        }

        private sealed class ControllableDoubleRebuilder : ICoCoDerivedStateRebuilder<int>
        {
            private readonly CoCoStateSlotId _dependency;

            public ControllableDoubleRebuilder(CoCoStateSlotId dependency)
            {
                _dependency = dependency;
            }

            public bool ShouldFail { get; set; }
            public bool ShouldThrow { get; set; }
            public int InvocationCount { get; private set; }

            public bool TryRebuild(in CoCoDerivedStateReadContext context, out int value)
            {
                InvocationCount++;
                if (ShouldThrow)
                {
                    throw new InvalidOperationException("Test rebuilder failure.");
                }

                if (ShouldFail || !context.TryRead(_dependency, out int source))
                {
                    value = default;
                    return false;
                }

                value = source * 2;
                return true;
            }
        }

        private sealed class ReentrantRebuilder : ICoCoDerivedStateRebuilder<int>
        {
            private readonly CoCoStateSlotId _dependency;

            public ReentrantRebuilder(CoCoStateSlotId dependency)
            {
                _dependency = dependency;
            }

            public CoCoContextFrameArena Arena { get; set; }
            public ulong Token { get; set; }
            public bool PrepareRejected { get; private set; }
            public bool FinalizeRejected { get; private set; }
            public bool CancelRejected { get; private set; }
            public bool CommitRejected { get; private set; }

            public bool TryRebuild(in CoCoDerivedStateReadContext context, out int value)
            {
                if (Arena != null)
                {
                    PrepareRejected = !Arena.TryPrepare(
                        CreateTickFrame(2UL, 2UL, 2UL),
                        out _,
                        out CoCoContextCommitStatus prepareStatus) &&
                        prepareStatus == CoCoContextCommitStatus.PreparationAlreadyActive;
                    FinalizeRejected = !Arena.TryFinalize(
                        Token,
                        out _,
                        out CoCoContextCommitStatus finalizeStatus) &&
                        finalizeStatus == CoCoContextCommitStatus.InvalidPreparation;
                    CancelRejected =
                        Arena.CancelPrepared(Token) == CoCoContextCommitStatus.InvalidPreparation &&
                        Arena.CancelFinalized(Token) == CoCoContextCommitStatus.InvalidPreparation;
                    CommitRejected =
                        Arena.Commit(Token).Status == CoCoContextCommitStatus.InvalidPreparation;
                }

                if (!context.TryRead(_dependency, out int source))
                {
                    value = default;
                    return false;
                }

                value = source * 2;
                return true;
            }
        }

        private sealed class Int32Codec : ICoCoContextValueCodec<int>
        {
            public Int32Codec(CoCoCodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CoCoCodecDescriptor Descriptor { get; }
            public int MaxEncodedSize => 4;

            public bool TryEncode(in int value, Span<byte> destination, out int bytesWritten)
            {
                if (destination.Length < MaxEncodedSize)
                {
                    bytesWritten = 0;
                    return false;
                }

                destination[0] = (byte)value;
                destination[1] = (byte)(value >> 8);
                destination[2] = (byte)(value >> 16);
                destination[3] = (byte)(value >> 24);
                bytesWritten = MaxEncodedSize;
                return true;
            }

            public bool TryDecode(ReadOnlySpan<byte> source, out int value, out int bytesRead)
            {
                if (source.Length < MaxEncodedSize)
                {
                    value = default;
                    bytesRead = 0;
                    return false;
                }

                value = source[0] |
                        source[1] << 8 |
                        source[2] << 16 |
                        source[3] << 24;
                bytesRead = MaxEncodedSize;
                return true;
            }
        }

        private sealed class ThrowOnDecodeInt32Codec : ICoCoContextValueCodec<int>
        {
            public ThrowOnDecodeInt32Codec(CoCoCodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CoCoCodecDescriptor Descriptor { get; }
            public int MaxEncodedSize => 4;
            public CoCoContextFrameArena Arena { get; set; }
            public bool PrepareRejected { get; private set; }
            public bool FinalizeRejected { get; private set; }
            public bool CancelRejected { get; private set; }
            public bool CommitRejected { get; private set; }

            public bool TryEncode(in int value, Span<byte> destination, out int bytesWritten)
            {
                if (destination.Length < MaxEncodedSize)
                {
                    bytesWritten = 0;
                    return false;
                }

                destination[0] = (byte)value;
                destination[1] = (byte)(value >> 8);
                destination[2] = (byte)(value >> 16);
                destination[3] = (byte)(value >> 24);
                bytesWritten = MaxEncodedSize;
                return true;
            }

            public bool TryDecode(ReadOnlySpan<byte> source, out int value, out int bytesRead)
            {
                if (Arena != null)
                {
                    ulong token = ReadPrivateToken(Arena);
                    PrepareRejected = !Arena.TryPrepare(
                        CreateTickFrame(2UL, 2UL, 2UL),
                        out _,
                        out CoCoContextCommitStatus prepareStatus) &&
                        prepareStatus == CoCoContextCommitStatus.PreparationAlreadyActive;
                    FinalizeRejected = !Arena.TryFinalize(
                        token,
                        out _,
                        out CoCoContextCommitStatus finalizeStatus) &&
                        finalizeStatus == CoCoContextCommitStatus.InvalidPreparation;
                    CancelRejected =
                        Arena.CancelPrepared(token) == CoCoContextCommitStatus.InvalidPreparation &&
                        Arena.CancelFinalized(token) == CoCoContextCommitStatus.InvalidPreparation;
                    CommitRejected =
                        Arena.Commit(token).Status == CoCoContextCommitStatus.InvalidPreparation;
                }

                throw new InvalidOperationException("Test codec decode failure.");
            }
        }

        private sealed class CommitEventHarness
        {
            private CoCoPreparedContextCommit _prepared;
            private bool _hasActivePreparation;
            private int _stagedCount;
            private ulong _lastSequence;

            public int PublishedCount { get; private set; }
            public CoCoEventSequence LastSequence
            {
                get
                {
                    return CoCoEventSequence.TryCreate(
                        _lastSequence,
                        out CoCoEventSequence sequence)
                        ? sequence
                        : CoCoEventSequence.Zero;
                }
            }

            public bool Begin(CoCoPreparedContextCommit prepared)
            {
                if (_hasActivePreparation || !prepared.IsValid)
                {
                    return false;
                }

                _prepared = prepared;
                _hasActivePreparation = true;
                _stagedCount = 0;
                return true;
            }

            public bool StageEvent()
            {
                if (!_hasActivePreparation)
                {
                    return false;
                }

                _stagedCount++;
                return true;
            }

            public CoCoContextCommitStatus Cancel()
            {
                if (!_hasActivePreparation)
                {
                    return CoCoContextCommitStatus.InvalidPreparation;
                }

                CoCoContextCommitStatus status = _prepared.Cancel();
                ClearPreparation();
                return status;
            }

            public bool TryCommitAndPublish()
            {
                if (!_hasActivePreparation)
                {
                    return false;
                }

                if ((ulong)_stagedCount > ulong.MaxValue - _lastSequence)
                {
                    _prepared.Cancel();
                    ClearPreparation();
                    return false;
                }

                if (!_prepared.TryFinalize(
                        out CoCoFinalizedContextCommit finalized,
                        out _))
                {
                    ClearPreparation();
                    return false;
                }

                CoCoContextCommitResult result = finalized.Commit();
                if (!result.Succeeded)
                {
                    ClearPreparation();
                    return false;
                }

                for (int index = 0; index < _stagedCount; index++)
                {
                    _lastSequence++;
                    PublishedCount++;
                }

                ClearPreparation();
                return true;
            }

            private void ClearPreparation()
            {
                _prepared = default;
                _hasActivePreparation = false;
                _stagedCount = 0;
            }
        }
    }
}

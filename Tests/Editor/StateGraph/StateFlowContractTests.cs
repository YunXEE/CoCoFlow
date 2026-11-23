using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateFlowContractTests
    {
        [Test]
        public void EmptyOperationRegistryFreezesAndProducesAValidEmptyFrame()
        {
            var builder = new OperationSectionRegistryBuilder();

            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(1UL, 1UL),
                out OperationSectionRegistry registry,
                out CoCoDiagnostic freezeDiagnostic),
                freezeDiagnostic.Message);
            Assert.IsTrue(registry.IsFrozen);
            Assert.AreEqual(0, registry.Count);
            Assert.AreEqual(0, registry.ByteSize);
            Assert.IsEmpty(registry.Sections);
            Assert.IsTrue(registry.TryValidateProvides(
                Array.Empty<OperationSectionRequirement>(),
                out CoCoDiagnostic providesDiagnostic),
                providesDiagnostic.Message);
            Assert.IsTrue(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(1UL),
                Array.Empty<OperationSectionRequirement>(),
                out OperationFrame frame,
                out CoCoDiagnostic createDiagnostic),
                createDiagnostic.Message);
            Assert.IsTrue(frame.TryBegin(
                CreateTickFrame(1UL, 1UL, 1UL),
                out OperationFrameWriter writer));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsTrue(finalized.IsValid);
            Assert.AreSame(registry, finalized.Registry);
            Assert.IsTrue(finalized.Commit());
            Assert.IsFalse(finalized.IsValid);
        }

        [Test]
        public void StateRolesRemainCallbackFree()
        {
            AssertCallbackFreeRole(typeof(StateLogic));
            AssertCallbackFreeRole(typeof(StateConfig));
            AssertCallbackFreeRole(typeof(ActivationMemory));
        }

        [Test]
        public void FrameHeadersCarryIndependentKindsAndStableIdentity()
        {
            GraphInstanceId graphInstanceId = CreateGraphInstanceId(11UL);
            FrameLayoutId layoutId = CreateLayoutId(1UL, 2UL);
            TickFrame tickFrame = CreateTickFrame(7UL, 3UL, 5UL);

            Assert.IsTrue(StateFlowFrameHeader.TryCreate(
                graphInstanceId,
                layoutId,
                StateFlowFrameKind.Intent,
                tickFrame,
                out StateFlowFrameHeader intent));
            Assert.IsTrue(StateFlowFrameHeader.TryCreate(
                graphInstanceId,
                layoutId,
                StateFlowFrameKind.Operation,
                tickFrame,
                out StateFlowFrameHeader operation));
            Assert.IsTrue(StateFlowFrameHeader.TryCreate(
                graphInstanceId,
                layoutId,
                StateFlowFrameKind.Context,
                tickFrame,
                out StateFlowFrameHeader context));

            Assert.AreEqual(StateFlowFrameKind.Intent, intent.Identity.Kind);
            Assert.AreEqual(StateFlowFrameKind.Operation, operation.Identity.Kind);
            Assert.AreEqual(StateFlowFrameKind.Context, context.Identity.Kind);
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
            var builder = new OperationSectionRegistryBuilder();
            OperationSectionId movementId = CreateSectionId(80UL, 1UL);
            OperationSectionId alternateMovementId = CreateSectionId(80UL, 2UL);
            var movementFactory = new MovementViewFactory();

            Assert.IsTrue(builder.TryRegister(
                movementId,
                OperationSectionMode.Continuous,
                movementFactory,
                out OperationSectionRequirement movementRequirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryRegister(
                movementId,
                OperationSectionMode.Continuous,
                movementFactory,
                out OperationSectionRequirement duplicateRequirement,
                out diagnostic),
                diagnostic.Message);
            Assert.AreEqual(movementRequirement, duplicateRequirement);
            Assert.AreEqual(1, builder.Count);

            Assert.IsFalse(builder.TryRegister(
                movementId,
                OperationSectionMode.Continuous,
                new MovementViewFactory(),
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, diagnostic.Code);
            Assert.AreEqual(1, builder.Count);

            Assert.IsTrue(builder.TryRegister(
                alternateMovementId,
                OperationSectionMode.Continuous,
                new AlternateMovementViewFactory(),
                out OperationSectionRequirement alternateRequirement,
                out diagnostic),
                diagnostic.Message);
            Assert.AreNotEqual(movementRequirement.SectionType, alternateRequirement.SectionType);
            Assert.AreEqual(2, builder.Count);

            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(80UL, 3UL),
                out OperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.AreEqual(2, registry.Count);
            Assert.IsTrue(registry.TryResolve(
                movementRequirement,
                out OperationSectionHandle<IMovementSection> movementHandle));
            Assert.IsTrue(registry.TryResolve(
                alternateRequirement,
                out OperationSectionHandle<IAlternateMovementSection> alternateHandle));
            Assert.AreNotEqual(movementHandle.DenseIndex, alternateHandle.DenseIndex);

            Assert.IsFalse(builder.TryRegister(
                CreateSectionId(80UL, 4UL),
                OperationSectionMode.Continuous,
                movementFactory,
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, diagnostic.Code);
        }

        [Test]
        public void OperationRequirementCarriesCompleteDeterministicShape()
        {
            Assert.IsTrue(OperationSectionRequirement.TryCreate<ICompositeShapeSection>(
                CreateSectionId(80UL, 10UL),
                OperationSectionMode.Continuous,
                out OperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(OperationSectionRequirement.TryCreate<ICompositeShapeTwinSection>(
                CreateSectionId(80UL, 11UL),
                OperationSectionMode.Continuous,
                out OperationSectionRequirement twin,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(OperationSectionRequirement.TryCreate<IDifferentCompositeShapeSection>(
                CreateSectionId(80UL, 12UL),
                OperationSectionMode.Continuous,
                out OperationSectionRequirement different,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(OperationSectionShape.TryCreate(
                typeof(ICompositeShapeSection),
                out OperationSectionShape toolShape,
                out diagnostic),
                diagnostic.Message);

            OperationSectionShape shape = requirement.Shape;
            Assert.IsNotNull(shape);
            Assert.IsTrue(shape.IsValid);
            Assert.AreEqual(14, shape.ByteSize);
            Assert.AreEqual(3, shape.FieldCount);
            Assert.AreNotEqual(0UL, shape.ShapeFingerprint);
            AssertShapeField(shape.Fields[0], 0, "Alpha", typeof(int), 0, 4);
            AssertShapeField(shape.Fields[1], 1, "Middle", typeof(long), 4, 8);
            AssertShapeField(shape.Fields[2], 2, "Zulu", typeof(short), 12, 2);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<OperationSectionFieldShape>)shape.Fields)[0] = shape.Fields[0]);

            Assert.AreNotSame(shape, twin.Shape);
            Assert.AreEqual(shape.ShapeFingerprint, twin.Shape.ShapeFingerprint);
            Assert.IsTrue(shape.Equals(twin.Shape));
            Assert.AreNotSame(shape, toolShape);
            Assert.IsTrue(shape.Equals(toolShape));
            Assert.AreNotEqual(shape.ShapeFingerprint, different.Shape.ShapeFingerprint);
            Assert.IsFalse(shape.Equals(different.Shape));

            FieldInfo fingerprintField = typeof(OperationSectionShape).GetField(
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
            var builder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(80UL, 20UL),
                OperationSectionMode.Discrete,
                new CompositeShapeViewFactory(),
                out OperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(80UL, 21UL),
                out OperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);

            OperationSectionDescriptor descriptor = registry.Sections[0];
            Assert.AreSame(requirement.Shape, descriptor.Shape);
            Assert.AreEqual(requirement.Shape.ByteSize, descriptor.ByteSize);
            Assert.AreEqual(requirement.Shape.FieldCount, descriptor.Fields.Count);
            for (int index = 0; index < requirement.Shape.FieldCount; index++)
            {
                OperationSectionFieldShape expected = requirement.Shape.Fields[index];
                OperationSectionFieldDescriptor actual = descriptor.Fields[index];
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
            FrameLayoutId layoutId = CreateLayoutId(81UL, 1UL);
            OperationSectionId sectionId = CreateSectionId(81UL, 2UL);
            var firstBuilder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(firstBuilder.TryRegister(
                sectionId,
                OperationSectionMode.Continuous,
                new MovementViewFactory(),
                out OperationSectionRequirement movementRequirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(firstBuilder.TryFreeze(
                layoutId,
                out OperationSectionRegistry firstRegistry,
                out diagnostic),
                diagnostic.Message);

            var secondBuilder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(secondBuilder.TryRegister(
                sectionId,
                OperationSectionMode.Continuous,
                new AlternateMovementViewFactory(),
                out OperationSectionRequirement alternateRequirement,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(secondBuilder.TryFreeze(
                layoutId,
                out OperationSectionRegistry secondRegistry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(secondRegistry.TryResolve(
                alternateRequirement,
                out OperationSectionHandle<IAlternateMovementSection> foreignHandle));
            Assert.IsFalse(firstRegistry.TryResolveField(
                foreignHandle,
                0,
                out OperationSectionField<int> _));

            Assert.IsTrue(OperationFrame.TryCreate(
                firstRegistry,
                CreateGraphInstanceId(81UL),
                new[] { movementRequirement },
                out OperationFrame frame,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(frame.TryBegin(
                CreateTickFrame(1UL, 1UL, 1UL),
                out OperationFrameWriter writer));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsFalse(finalized.TryGet(
                foreignHandle,
                out OperationSectionEntry<IAlternateMovementSection> _));
            Assert.IsTrue(finalized.Commit());
        }

        [Test]
        public void OperationSectionRejectsInheritanceAndIllegalMemberShapes()
        {
            AssertInvalidSection<IInheritedMovementSection>();
            AssertInvalidSection<IEventSection>();
            AssertInvalidSection<IIndexerSection>();
            AssertInvalidSection<IReferenceSection>();
            AssertInvalidSection<IStringSection>();
            AssertInvalidSection<IRefReturnSection>();
        }

        [Test]
        public void SectionSettersAndDataMethodsKeepTheSameFieldShape()
        {
            Assert.IsTrue(OperationSectionShape.TryCreate(typeof(IWritableSection), out var writable, out _));
            Assert.IsTrue(OperationSectionShape.TryCreate(typeof(IMethodSection), out var method, out _));
            Assert.AreEqual(1, writable.FieldCount);
            Assert.AreEqual(1, method.FieldCount);
            Assert.AreEqual(writable.Fields[0].Name, method.Fields[0].Name);
            Assert.AreEqual(writable.Fields[0].ValueType, method.Fields[0].ValueType);
        }

        [TestCase(OperationSectionMode.Continuous)]
        [TestCase(OperationSectionMode.Discrete)]
        public void CustomSectionViewWritesThroughTheExistingFrame(OperationSectionMode mode)
        {
            var builder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(CreateSectionId(91UL, 1UL), mode,
                new WritableViewFactory(), out var requirement, out _));
            Assert.IsTrue(builder.TryFreeze(CreateLayoutId(91UL, 2UL), out var registry, out _));
            Assert.IsTrue(OperationFrame.TryCreate(registry, CreateGraphInstanceId(91UL),
                new[] { requirement }, out var frame, out _));
            Assert.IsTrue(frame.TryBegin(CreateTickFrame(1UL, 1UL, 1UL), out var writer));
            var target = new SectionWriteTarget(registry, writer);
            var view = (IWritableSection)registry.CreateWriteView(typeof(IWritableSection), target);
            view.Value = 3;
            view.Value = 7;
            Assert.AreEqual(mode == OperationSectionMode.Discrete ? 2 : 0, target.Enables);
            Assert.IsTrue(registry.TryResolve(requirement, out OperationSectionHandle<IWritableSection> handle));
            Assert.IsTrue(writer.TryFinalize(out var finalized));
            Assert.IsTrue(finalized.TryGet(handle, out var entry));
            Assert.AreEqual(7, entry.View.Value);
            Assert.IsTrue(finalized.Commit());
        }

        private sealed class WritableView : IWritableSection
        {
            private readonly OperationSectionReader _access;
            private readonly OperationSectionField<int> _field;
            public WritableView(OperationSectionReader access, OperationSectionField<int> field)
            { _access = access; _field = field; }
            public int Value { get => _access.Read(_field); set => _access.Write(_field, value); }
        }

        private sealed class WritableViewFactory : IOperationSectionViewFactory<IWritableSection>
        {
            public IWritableSection Create(in OperationSectionViewContext<IWritableSection> context)
            {
                context.TryGetField(0, out OperationSectionField<int> field);
                return new WritableView(context.Reader, field);
            }
        }

        private sealed class SectionWriteTarget : IOperationSectionWriteTarget
        {
            private readonly OperationSectionRegistry _registry;
            private readonly OperationFrameWriter _writer;
            public int Enables;
            public SectionWriteTarget(OperationSectionRegistry registry, OperationFrameWriter writer)
            { _registry = registry; _writer = writer; }
            public void Write<T>(OperationSectionField<T> field, in T value) where T : unmanaged =>
                _writer.Write(field, value);
            public void Enable<T>() where T : class, IOperationSection
            {
                _registry.TryResolveTypedHandle(out OperationSectionHandle<T> handle);
                ActivationId.TryCreate(1UL, out var activation);
                Assert.IsTrue(_writer.EnableDiscrete(handle, activation));
                Enables++;
            }
        }

        [Test]
        public void OperationRequirementRejectsIncompleteShapesAtDeclarationTime()
        {
            OperationSectionId sectionId = CreateSectionId(89UL, 1UL);

            Assert.IsFalse(OperationSectionRequirement.TryCreate<IManifestOnlySection>(
                sectionId,
                OperationSectionMode.Continuous,
                out _,
                out CoCoDiagnostic runtimeDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidOperationSection, runtimeDiagnostic.Code);
            Assert.IsFalse(OperationSectionShape.TryCreate(
                typeof(IManifestOnlySection),
                out _,
                out CoCoDiagnostic toolDiagnostic));
            Assert.AreEqual(runtimeDiagnostic, toolDiagnostic);

            AssertInvalidSection<IOperationSection>();
            AssertInvalidSection<IInheritedMovementSection>();
            AssertInvalidSection<ManifestSectionClass>();
        }

        [Test]
        public void OperationFrameUsesContinuousZeroAndDiscreteActivationHeaders()
        {
            var builder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(90UL, 1UL),
                OperationSectionMode.Continuous,
                new MovementViewFactory(),
                out OperationSectionRequirement movementRequirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(90UL, 2UL),
                OperationSectionMode.Discrete,
                new AttackViewFactory(),
                out OperationSectionRequirement attackRequirement,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(90UL, 3UL),
                out OperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(registry.TryResolve(
                movementRequirement,
                out OperationSectionHandle<IMovementSection> movementHandle));
            Assert.IsTrue(registry.TryResolve(
                attackRequirement,
                out OperationSectionHandle<IAttackSection> attackHandle));
            Assert.IsTrue(registry.TryResolveField(
                movementHandle,
                0,
                out OperationSectionField<int> distanceField));
            Assert.IsTrue(registry.TryResolveField(
                attackHandle,
                0,
                out OperationSectionField<int> damageField));
            Assert.IsFalse(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(901UL),
                new[] { movementRequirement },
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.MissingOperationSection, diagnostic.Code);
            Assert.IsFalse(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(902UL),
                new[] { movementRequirement, movementRequirement },
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, diagnostic.Code);
            Assert.IsTrue(OperationSectionRequirement.TryCreate<IAlternateMovementSection>(
                CreateSectionId(90UL, 4UL),
                OperationSectionMode.Continuous,
                out OperationSectionRequirement unregistered,
                out diagnostic));
            Assert.IsFalse(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(903UL),
                new[] { movementRequirement, unregistered },
                out _,
                out diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.MissingOperationSection, diagnostic.Code);
            Assert.IsTrue(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(90UL),
                new[] { movementRequirement, attackRequirement },
                out OperationFrame frame,
                out diagnostic),
                diagnostic.Message);

            Assert.IsTrue(frame.TryBegin(CreateTickFrame(1UL, 1UL, 1UL), out OperationFrameWriter writer));
            Assert.IsTrue(writer.Write(distanceField, 6));
            Assert.IsTrue(writer.Write(damageField, 12));
            Assert.IsTrue(ActivationId.TryCreate(5UL, out ActivationId activationId));
            Assert.IsTrue(writer.EnableDiscrete(attackHandle, activationId));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsTrue(finalized.TryGet(
                movementHandle,
                out OperationSectionEntry<IMovementSection> movement));
            Assert.IsTrue(finalized.TryGet(
                attackHandle,
                out OperationSectionEntry<IAttackSection> attack));
            Assert.IsTrue(movement.Header.Enabled);
            Assert.AreEqual(6, movement.View.Distance);
            Assert.IsTrue(attack.Header.Enabled);
            Assert.AreEqual(activationId, attack.Header.ActivationId);
            Assert.AreEqual(1UL, attack.Header.OperationSequence.Value);
            Assert.AreEqual(12, attack.View.Damage);
            Assert.IsTrue(finalized.Commit());

            Assert.IsFalse(frame.TryBegin(CreateTickFrame(1UL, 1UL, 2UL), out _));

            IMovementSection reusedView = movement.View;
            Assert.IsTrue(frame.TryBegin(CreateTickFrame(2UL, 1UL, 2UL), out writer));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.TryGet(movementHandle, out movement));
            Assert.IsTrue(finalized.TryGet(attackHandle, out attack));
            Assert.IsTrue(movement.Header.Enabled);
            Assert.AreEqual(0, movement.View.Distance);
            Assert.AreSame(reusedView, movement.View);
            Assert.IsFalse(attack.Header.Enabled);
            Assert.AreEqual(0, attack.View.Damage);
            Assert.IsTrue(finalized.Commit());

            Assert.IsTrue(frame.TryBegin(CreateTickFrame(3UL, 2UL, 3UL), out writer));
            Assert.IsTrue(writer.EnableDiscrete(attackHandle, activationId));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.TryGet(attackHandle, out attack));
            Assert.AreEqual(1UL, attack.Header.OperationSequence.Value);
            Assert.IsTrue(finalized.Cancel());

            Assert.IsTrue(frame.TryBegin(CreateTickFrame(3UL, 1UL, 4UL), out writer));
            Assert.IsTrue(writer.EnableDiscrete(attackHandle, activationId));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.TryGet(attackHandle, out attack));
            Assert.AreEqual(2UL, attack.Header.OperationSequence.Value);
            Assert.IsTrue(finalized.Commit());

            Assert.IsTrue(frame.TryBegin(CreateTickFrame(4UL, 2UL, 5UL), out writer));
            Assert.IsTrue(writer.EnableDiscrete(attackHandle, activationId));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.TryGet(attackHandle, out attack));
            Assert.AreEqual(1UL, attack.Header.OperationSequence.Value);
            Assert.IsTrue(finalized.Commit());
            Assert.IsFalse(frame.TryBegin(CreateTickFrame(5UL, 1UL, 6UL), out _));
        }

        [Test]
        public void OperationFrameComposesContinuousFieldsIndependentlyByRank()
        {
            CreateContinuousPairOperationFrame(
                92UL,
                out OperationFrame frame,
                out OperationSectionHandle<IContinuousPairSection> handle,
                out OperationSectionField<int> horizontalField,
                out OperationSectionField<int> verticalField);
            Assert.IsTrue(OperationWriteRank.TryCreate(
                0,
                1,
                out OperationWriteRank parentRank));
            Assert.IsTrue(OperationWriteRank.TryCreate(
                0,
                2,
                out OperationWriteRank childRank));

            Assert.IsTrue(frame.TryBegin(
                CreateTickFrame(1UL, 1UL, 1UL),
                out OperationFrameWriter writer));
            Assert.IsTrue(writer.Write(parentRank, horizontalField, 1));
            Assert.IsTrue(writer.Write(childRank, verticalField, 20));
            Assert.IsTrue(writer.Write(parentRank, horizontalField, 2));
            Assert.IsTrue(writer.Write(parentRank, verticalField, 10));
            Assert.IsTrue(writer.Write(childRank, horizontalField, 3));
            Assert.IsTrue(writer.Write(childRank, horizontalField, 4));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsTrue(finalized.TryGet(
                handle,
                out OperationSectionEntry<IContinuousPairSection> entry));
            Assert.AreEqual(4, entry.View.Horizontal);
            Assert.AreEqual(20, entry.View.Vertical);
            Assert.IsTrue(finalized.Commit());
        }

        [Test]
        public void OperationFrameParentExitCannotOverwriteChildContribution()
        {
            CreateMovementOperationFrame(
                93UL,
                out OperationFrame frame,
                out OperationSectionHandle<IMovementSection> handle,
                out OperationSectionField<int> distanceField);
            Assert.IsTrue(OperationWriteRank.TryCreate(
                0,
                1,
                out OperationWriteRank parentRank));
            Assert.IsTrue(OperationWriteRank.TryCreate(
                0,
                2,
                out OperationWriteRank childRank));

            Assert.IsTrue(frame.TryBegin(
                CreateTickFrame(1UL, 1UL, 1UL),
                out OperationFrameWriter writer));
            Assert.IsTrue(writer.Write(childRank, distanceField, 8));
            Assert.IsTrue(
                writer.Write(parentRank, distanceField, -1),
                "A lower-ranked parent Exit contribution is accepted but cannot replace its child.");
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsTrue(finalized.TryGet(
                handle,
                out OperationSectionEntry<IMovementSection> entry));
            Assert.AreEqual(8, entry.View.Distance);
            Assert.IsTrue(finalized.Commit());
        }

        [Test]
        public void OperationFrameHigherLayerOverridesDeeperPathInLowerLayer()
        {
            CreateMovementOperationFrame(
                94UL,
                out OperationFrame frame,
                out OperationSectionHandle<IMovementSection> handle,
                out OperationSectionField<int> distanceField);
            Assert.IsTrue(OperationWriteRank.TryCreate(
                0,
                99,
                out OperationWriteRank deepLowerLayer));
            Assert.IsTrue(OperationWriteRank.TryCreate(
                1,
                0,
                out OperationWriteRank shallowHigherLayer));

            Assert.IsTrue(frame.TryBegin(
                CreateTickFrame(1UL, 1UL, 1UL),
                out OperationFrameWriter writer));
            Assert.IsTrue(writer.Write(deepLowerLayer, distanceField, 7));
            Assert.IsTrue(writer.Write(shallowHigherLayer, distanceField, 9));
            Assert.IsTrue(writer.Write(deepLowerLayer, distanceField, 11));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsTrue(finalized.TryGet(
                handle,
                out OperationSectionEntry<IMovementSection> entry));
            Assert.AreEqual(9, entry.View.Distance);
            Assert.IsTrue(finalized.Commit());
        }

        [Test]
        public void OperationFrameDiscreteWinnerGetsOneSequenceAtFinalizeAndCommit()
        {
            CreateAttackOperationFrame(
                95UL,
                out OperationFrame frame,
                out OperationSectionHandle<IAttackSection> handle);
            Assert.IsTrue(OperationWriteRank.TryCreate(
                0,
                0,
                out OperationWriteRank lowerRank));
            Assert.IsTrue(OperationWriteRank.TryCreate(
                0,
                1,
                out OperationWriteRank higherRank));
            Assert.IsTrue(ActivationId.TryCreate(1UL, out ActivationId firstActivation));
            Assert.IsTrue(ActivationId.TryCreate(2UL, out ActivationId secondActivation));
            Assert.IsTrue(ActivationId.TryCreate(3UL, out ActivationId finalActivation));
            TickFrame firstTick = CreateTickFrame(1UL, 1UL, 1UL);

            Assert.IsTrue(frame.TryBegin(firstTick, out OperationFrameWriter writer));
            Assert.IsTrue(writer.EnableDiscrete(lowerRank, handle, firstActivation));
            Assert.IsTrue(writer.EnableDiscrete(higherRank, handle, secondActivation));
            Assert.IsTrue(writer.EnableDiscrete(lowerRank, handle, firstActivation));
            Assert.IsTrue(writer.EnableDiscrete(higherRank, handle, finalActivation));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsTrue(finalized.TryGet(
                handle,
                out OperationSectionEntry<IAttackSection> entry));
            Assert.AreEqual(finalActivation, entry.Header.ActivationId);
            Assert.AreEqual(1UL, entry.Header.OperationSequence.Value);
            Assert.IsTrue(finalized.Cancel());

            Assert.IsTrue(frame.TryBegin(firstTick, out writer));
            Assert.IsTrue(writer.EnableDiscrete(higherRank, handle, finalActivation));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.TryGet(handle, out entry));
            Assert.AreEqual(1UL, entry.Header.OperationSequence.Value);
            Assert.IsTrue(finalized.Commit());

            Assert.IsTrue(frame.TryBegin(
                CreateTickFrame(2UL, 1UL, 2UL),
                out writer));
            Assert.IsTrue(writer.EnableDiscrete(lowerRank, handle, firstActivation));
            Assert.IsTrue(writer.EnableDiscrete(higherRank, handle, secondActivation));
            Assert.IsTrue(writer.EnableDiscrete(higherRank, handle, finalActivation));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.TryGet(handle, out entry));
            Assert.AreEqual(finalActivation, entry.Header.ActivationId);
            Assert.AreEqual(2UL, entry.Header.OperationSequence.Value);
            Assert.IsTrue(finalized.Commit());
        }

        [Test]
        public void OperationFrameFinalizeCancelRollsBackWhileCommitAdvancesTick()
        {
            CreateMovementOperationFrame(
                96UL,
                out OperationFrame frame,
                out _,
                out OperationSectionField<int> distanceField);
            TickFrame firstTick = CreateTickFrame(1UL, 1UL, 1UL);

            Assert.IsTrue(frame.TryBegin(firstTick, out OperationFrameWriter writer));
            Assert.IsTrue(writer.Write(distanceField, 1));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsFalse(writer.IsValid);
            Assert.IsFalse(frame.TryBegin(CreateTickFrame(2UL, 1UL, 2UL), out _));
            Assert.IsTrue(finalized.Cancel());
            Assert.IsFalse(finalized.IsValid);

            Assert.IsTrue(frame.TryBegin(firstTick, out writer));
            Assert.IsTrue(writer.Write(distanceField, 2));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.Commit());
            Assert.IsFalse(finalized.Commit());
            Assert.IsFalse(frame.TryBegin(firstTick, out _));

            TickFrame secondTick = CreateTickFrame(2UL, 1UL, 2UL);
            Assert.IsTrue(frame.TryBegin(secondTick, out writer));
            Assert.IsTrue(writer.Write(distanceField, 3));
            Assert.IsTrue(writer.Cancel());
            Assert.IsTrue(frame.TryBegin(secondTick, out writer));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.Cancel());
            Assert.IsTrue(frame.TryBegin(secondTick, out writer));
            Assert.IsTrue(writer.TryFinalize(out finalized));
            Assert.IsTrue(finalized.Commit());
        }

        [Test]
        public void OperationSectionCustomNestedUnmanagedValueRoundTripsWithoutGenericReflection()
        {
            var builder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(91UL, 1UL),
                OperationSectionMode.Continuous,
                new NestedValueViewFactory(),
                out OperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(91UL, 2UL),
                out OperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(registry.TryResolve(
                requirement,
                out OperationSectionHandle<INestedValueSection> handle));
            Assert.IsTrue(registry.TryResolveField(
                handle,
                0,
                out OperationSectionField<NestedValue> field));
            Assert.IsTrue(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(91UL),
                new[] { requirement },
                out OperationFrame frame,
                out diagnostic),
                diagnostic.Message);

            var value = new NestedValue(17, true, 9);
            Assert.IsTrue(frame.TryBegin(CreateTickFrame(1UL, 1UL, 1UL), out OperationFrameWriter writer));
            Assert.IsTrue(writer.Write(field, value));
            Assert.IsTrue(writer.TryFinalize(out FinalizedOperationFrame finalized));
            Assert.IsTrue(finalized.TryGet(handle, out OperationSectionEntry<INestedValueSection> entry));
            Assert.AreEqual(17, entry.View.Value.Count);
            Assert.IsTrue(entry.View.Value.Flags.Enabled);
            Assert.AreEqual(9, entry.View.Value.Flags.Code);
            Assert.IsTrue(finalized.Commit());
        }

        [Test]
        public void ContextLayoutFreezesBlockOwnershipProjectionAndSingleWriter()
        {
            var builder = new ContextFrameLayoutBuilder();
            StateBlockId graphBlockId = CreateBlockId(1UL, 1UL);
            StateBlockId operatorBlockId = CreateBlockId(1UL, 2UL);
            StateSlotId healthSlotId = CreateSlotId(2UL, 1UL);
            StateSlotId speedSlotId = CreateSlotId(2UL, 2UL);
            CodecDescriptor codec = CreateCodecDescriptor(4UL, 1U);

            Assert.IsTrue(builder.TryAddBlock(
                graphBlockId,
                StateBlockOwner.Graph,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.None, diagnosticCode);
            Assert.IsTrue(builder.TryAddBlock(
                operatorBlockId,
                StateBlockOwner.Operator,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                graphBlockId,
                healthSlotId,
                ContextProjection.Temporal | ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                100,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                operatorBlockId,
                speedSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.ResetToDefault,
                1.5f,
                default,
                null,
                out diagnosticCode));

            Assert.IsFalse(builder.TryAddSlot(
                operatorBlockId,
                healthSlotId,
                ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                0,
                default,
                null,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, diagnosticCode);

            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(3UL, 1UL),
                1U,
                out ContextFrameLayout layout,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.None, diagnosticCode);
            Assert.AreEqual(2, layout.Blocks.Count);
            Assert.AreEqual(StateBlockOwner.Graph, layout.Blocks[0].Owner);
            Assert.AreEqual(StateBlockOwner.Operator, layout.Blocks[1].Owner);

            StateSlotDescriptor health = layout.Slots[0];
            Assert.AreEqual(graphBlockId, health.WriterBlockId);
            Assert.AreEqual(
                ContextProjection.Temporal | ContextProjection.Durable,
                health.Projection);
            Assert.AreEqual(ContextRestorePolicy.Stored, health.RestorePolicy);
            Assert.AreEqual(codec, health.Codec);
            Assert.IsTrue(layout.TryResolveSlot(healthSlotId, out StateSlot<int> healthHandle));
            Assert.IsTrue(healthHandle.IsValid);
            Assert.IsFalse(layout.TryResolveSlot(healthSlotId, out StateSlot<float> wrongTypeHandle));
            Assert.IsFalse(wrongTypeHandle.IsValid);

            var arena = new ContextFrameArena(CreateGraphInstanceId(3UL), layout, 2);
            Assert.IsTrue(layout.TryResolveBlock(graphBlockId, out StateBlockHandle graphBlock));
            Assert.IsTrue(layout.TryResolveBlock(operatorBlockId, out StateBlockHandle operatorBlock));
            Assert.IsTrue(layout.TryResolveSlot(speedSlotId, out StateSlot<float> speedHandle));
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out ContextCommitStatus status));
            Assert.IsTrue(prepared.TryGetWriter(graphBlock, out ContextFrameWriter graphWriter));
            Assert.IsTrue(prepared.TryGetWriter(operatorBlock, out ContextFrameWriter operatorWriter));
            Assert.IsTrue(graphWriter.Write(healthHandle, 80));
            Assert.IsFalse(graphWriter.Write(speedHandle, 3f));
            Assert.IsTrue(operatorWriter.Write(speedHandle, 3f));
            Assert.IsFalse(operatorWriter.Write(healthHandle, 80));
            Assert.IsTrue(FinalizeAndCommit(prepared).Succeeded);

            Assert.IsFalse(builder.TryAddBlock(
                CreateBlockId(9UL, 9UL),
                StateBlockOwner.Actor,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.RegistryFrozen, diagnosticCode);
        }

        [Test]
        public void DerivedRestoreDependenciesFreezeInTopologicalOrder()
        {
            var builder = new ContextFrameLayoutBuilder();
            StateBlockId blockId = CreateBlockId(10UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(10UL, 1UL);
            StateSlotId firstDerivedSlotId = CreateSlotId(10UL, 2UL);
            StateSlotId secondDerivedSlotId = CreateSlotId(10UL, 3UL);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                firstDerivedSlotId,
                ContextProjection.Durable,
                0,
                default,
                new[] { storedSlotId },
                new AddOneRebuilder(storedSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                secondDerivedSlotId,
                ContextProjection.Durable,
                0,
                default,
                new[] { firstDerivedSlotId },
                new AddOneRebuilder(firstDerivedSlotId),
                out diagnosticCode));

            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(10UL, 4UL),
                1U,
                out ContextFrameLayout layout,
                out diagnosticCode));
            CollectionAssert.AreEqual(new[] { 1, 2 }, layout.DerivedOrder);
            Assert.IsTrue(layout.TryResolveSlot(
                firstDerivedSlotId,
                out StateSlot<int> firstDerivedSlot));
            Assert.IsTrue(layout.TryResolveSlot(
                secondDerivedSlotId,
                out StateSlot<int> secondDerivedSlot));
            var arena = new ContextFrameArena(CreateGraphInstanceId(10UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            ContextFrame frame = FinalizeAndCommit(prepared).Frame;
            Assert.AreEqual(2, frame.Read(firstDerivedSlot));
            Assert.AreEqual(3, frame.Read(secondDerivedSlot));
        }

        [Test]
        public void DerivedRestoreDependencyCyclesAreRejected()
        {
            var builder = new ContextFrameLayoutBuilder();
            StateBlockId blockId = CreateBlockId(20UL, 1UL);
            StateSlotId leftSlotId = CreateSlotId(20UL, 1UL);
            StateSlotId rightSlotId = CreateSlotId(20UL, 2UL);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Graph,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                leftSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { rightSlotId },
                new AddOneRebuilder(rightSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                rightSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { leftSlotId },
                new AddOneRebuilder(leftSlotId),
                out diagnosticCode));

            Assert.IsFalse(builder.TryFreeze(
                CreateLayoutId(20UL, 3UL),
                1U,
                out ContextFrameLayout layout,
                out diagnosticCode));
            Assert.IsNull(layout);
            Assert.AreEqual(CoCoDiagnosticCode.DerivedDependencyCycle, diagnosticCode);
        }

        [Test]
        public void ProjectionRequiresStoredAndDerivedDependencyClosureButAllowsResetDependencies()
        {
            StateBlockId blockId = CreateBlockId(21UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(21UL, 1UL);
            StateSlotId firstDerivedSlotId = CreateSlotId(21UL, 2UL);
            StateSlotId secondDerivedSlotId = CreateSlotId(21UL, 3UL);
            var invalidBuilder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(invalidBuilder.TryAddBlock(
                blockId,
                StateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(invalidBuilder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(invalidBuilder.TryAddDerivedSlot(
                blockId,
                firstDerivedSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                new AddOneRebuilder(storedSlotId),
                out diagnosticCode));
            Assert.IsTrue(invalidBuilder.TryAddDerivedSlot(
                blockId,
                secondDerivedSlotId,
                ContextProjection.Temporal | ContextProjection.Durable,
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

            var resetBuilder = new ContextFrameLayoutBuilder();
            StateSlotId resetSlotId = CreateSlotId(21UL, 4UL);
            StateSlotId resetDerivedSlotId = CreateSlotId(21UL, 5UL);
            Assert.IsTrue(resetBuilder.TryAddBlock(blockId, StateBlockOwner.Actor, out diagnosticCode));
            Assert.IsTrue(resetBuilder.TryAddSlot(
                blockId,
                resetSlotId,
                ContextProjection.None,
                ContextRestorePolicy.ResetToDefault,
                5,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(resetBuilder.TryAddDerivedSlot(
                blockId,
                resetDerivedSlotId,
                ContextProjection.Durable,
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
            StateBlockId blockId = CreateBlockId(22UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(22UL, 1UL);
            StateSlotId derivedSlotId = CreateSlotId(22UL, 2UL);
            var rebuilder = new ControllableDoubleRebuilder(storedSlotId);
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(blockId, StateBlockOwner.Actor, out _));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                1,
                default,
                null,
                out _));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                rebuilder,
                out _));
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(22UL, 1UL),
                1U,
                out ContextFrameLayout layout,
                out _));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(storedSlotId, out StateSlot<int> storedSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out StateSlot<int> derivedSlot));
            var arena = new ContextFrameArena(CreateGraphInstanceId(22UL), layout, 2);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit first,
                out _));
            Assert.IsTrue(first.TryGetWriter(block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(storedSlot, 3));
            Assert.IsFalse(writer.Write(derivedSlot, 99));
            ContextCommitResult firstResult = FinalizeAndCommit(first);
            Assert.AreEqual(6, firstResult.Frame.Read(derivedSlot));
            Assert.AreEqual(1, rebuilder.InvocationCount);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out PreparedContextCommit noOp,
                out _));
            ContextCommitResult noOpResult = FinalizeAndCommit(noOp);
            Assert.AreEqual(2UL, noOpResult.Frame.Revision.Value);
            Assert.AreEqual(6, noOpResult.Frame.Read(derivedSlot));
            Assert.AreEqual(2, rebuilder.InvocationCount);

            rebuilder.ShouldFail = true;
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out PreparedContextCommit failed,
                out _));
            Assert.IsTrue(failed.TryGetWriter(block, out writer));
            Assert.IsTrue(writer.Write(storedSlot, 4));
            Assert.IsFalse(failed.TryFinalize(
                out _,
                out ContextCommitStatus failedStatus));
            Assert.AreEqual(ContextCommitStatus.DerivedRebuildFailed, failedStatus);
            Assert.AreEqual(2UL, arena.Current.Revision.Value);
            Assert.AreEqual(3, arena.Current.Read(storedSlot));
            Assert.AreEqual(3, rebuilder.InvocationCount);

            rebuilder.ShouldFail = false;
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out PreparedContextCommit retry,
                out _));
            Assert.IsTrue(retry.TryGetWriter(block, out writer));
            Assert.IsTrue(writer.Write(storedSlot, 4));
            ContextCommitResult retryResult = FinalizeAndCommit(retry);
            Assert.AreEqual(8, retryResult.Frame.Read(derivedSlot));
            Assert.AreEqual(4, rebuilder.InvocationCount);

            rebuilder.ShouldThrow = true;
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(4UL, 1UL, 4UL),
                out PreparedContextCommit throwing,
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
            StateSlotId valueSlotId = CreateSlotId(30UL, 1UL);
            ContextFrameLayout firstLayout = CreateStoredIntLayout(
                CreateLayoutId(30UL, 1UL),
                valueSlotId,
                17);
            ContextFrameLayout secondLayout = CreateStoredIntLayout(
                CreateLayoutId(30UL, 2UL),
                CreateSlotId(30UL, 2UL),
                99);
            GraphInstanceId firstGraph = CreateGraphInstanceId(31UL);
            GraphInstanceId secondGraph = CreateGraphInstanceId(32UL);
            var firstArena = new ContextFrameArena(firstGraph, firstLayout, 3);
            var secondArena = new ContextFrameArena(secondGraph, firstLayout, 3);

            Assert.IsTrue(firstArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit firstPrepared,
                out ContextCommitStatus status));
            ContextFrame firstFrame = FinalizeAndCommit(firstPrepared).Frame;
            Assert.AreEqual(firstGraph, firstFrame.Header.Identity.GraphInstanceId);

            Assert.IsTrue(secondArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit secondPrepared,
                out status));
            ContextFrame secondFrame = FinalizeAndCommit(secondPrepared).Frame;
            Assert.AreEqual(secondGraph, secondFrame.Header.Identity.GraphInstanceId);
            Assert.AreNotEqual(firstFrame.Header.Identity, secondFrame.Header.Identity);

            Assert.IsFalse(secondArena.TryPrepareRestore(
                firstFrame,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.GraphMismatch, status);

            Assert.IsTrue(firstLayout.TryResolveSlot(valueSlotId, out StateSlot<int> validHandle));
            Assert.AreEqual(17, firstFrame.Read(validHandle));
            Assert.IsTrue(secondLayout.TryResolveSlot(
                CreateSlotId(30UL, 2UL),
                out StateSlot<int> foreignHandle));
            Assert.Throws<InvalidOperationException>(() => firstFrame.Read(foreignHandle));
        }

        [Test]
        public void ContextLayoutVersionSchemaAndInstanceRejectCrossUse()
        {
            FrameLayoutId layoutId = CreateLayoutId(31UL, 1UL);
            StateBlockId blockId = CreateBlockId(31UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(31UL, 1UL);
            StateSlotId derivedSlotId = CreateSlotId(31UL, 2UL);
            CodecDescriptor codec = CreateCodecDescriptor(31UL, 1U);
            ContextFrameLayout layout = CreateProjectionLayout(
                layoutId, blockId, storedSlotId, derivedSlotId, codec, 1U, 0);
            ContextFrameLayout versionTwo = CreateProjectionLayout(
                layoutId, blockId, storedSlotId, derivedSlotId, codec, 2U, 0);
            ContextFrameLayout separateInstance = CreateProjectionLayout(
                layoutId, blockId, storedSlotId, derivedSlotId, codec, 1U, 0);
            ContextFrameLayout changedSchema = CreateProjectionLayout(
                layoutId, blockId, storedSlotId, derivedSlotId, codec, 1U, 99);
            GraphInstanceId graphInstanceId = CreateGraphInstanceId(31UL);
            var arena = new ContextFrameArena(graphInstanceId, layout, 2);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out ContextCommitStatus status));
            ContextFrame frame = FinalizeAndCommit(prepared).Frame;
            Assert.AreEqual(1U, frame.Header.LayoutVersion);
            Assert.AreEqual(layout.SchemaHash, frame.Header.LayoutSchemaHash);
            Assert.AreEqual(layout.SchemaHash, separateInstance.SchemaHash);
            Assert.AreNotEqual(layout.SchemaHash, changedSchema.SchemaHash);

            Assert.IsTrue(versionTwo.TryResolveSlot(
                storedSlotId,
                out StateSlot<int> versionTwoHandle));
            Assert.Throws<InvalidOperationException>(() => frame.Read(versionTwoHandle));
            var versionTwoArena = new ContextFrameArena(graphInstanceId, versionTwo, 2);
            Assert.IsFalse(versionTwoArena.TryPrepareRestore(
                frame,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.LayoutMismatch, status);

            Assert.IsTrue(separateInstance.TryResolveSlot(
                storedSlotId,
                out StateSlot<int> separateHandle));
            Assert.Throws<InvalidOperationException>(() => frame.Read(separateHandle));
            var separateArena = new ContextFrameArena(graphInstanceId, separateInstance, 2);
            Assert.IsFalse(separateArena.TryPrepareRestore(
                frame,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.LayoutMismatch, status);
        }

        [Test]
        public void NoOpCommitIncrementsRevisionAndCancelPreservesAuthority()
        {
            StateSlotId slotId = CreateSlotId(40UL, 1UL);
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(40UL, 1UL),
                slotId,
                42);
            var arena = new ContextFrameArena(CreateGraphInstanceId(40UL), layout, 3);
            Assert.IsTrue(layout.TryResolveSlot(slotId, out StateSlot<int> slot));

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit firstPrepared,
                out ContextCommitStatus status));
            ContextCommitResult firstResult = FinalizeAndCommit(firstPrepared);
            Assert.IsTrue(firstResult.Succeeded);
            Assert.AreEqual(1UL, firstResult.Frame.Revision.Value);
            Assert.AreEqual(42, firstResult.Frame.Read(slot));

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out PreparedContextCommit noOpPrepared,
                out status));
            ContextCommitResult noOpResult = FinalizeAndCommit(noOpPrepared);
            Assert.IsTrue(noOpResult.Succeeded);
            Assert.AreEqual(2UL, noOpResult.Frame.Revision.Value);
            Assert.AreEqual(42, noOpResult.Frame.Read(slot));

            ContextFrame authoritative = arena.Current;
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out PreparedContextCommit cancelled,
                out status));
            Assert.AreEqual(ContextCommitStatus.Cancelled, cancelled.Cancel());
            Assert.AreEqual(authoritative, arena.Current);
            Assert.AreEqual(2UL, arena.Current.Revision.Value);
            Assert.IsFalse(cancelled.TryFinalize(out _, out status));
            Assert.AreEqual(ContextCommitStatus.InvalidPreparation, status);
        }

        [Test]
        public void PreparedAndFinalizedTokensCannotCrossCancelTheirTypeState()
        {
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(42UL, 1UL),
                CreateSlotId(42UL, 1UL),
                1);
            var arena = new ContextFrameArena(CreateGraphInstanceId(42UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryFinalize(
                out FinalizedContextCommit finalized,
                out _));

            Assert.IsFalse(prepared.IsValid);
            Assert.AreEqual(ContextCommitStatus.InvalidPreparation, prepared.Cancel());
            Assert.IsTrue(finalized.IsValid);
            Assert.AreEqual(ContextCommitStatus.Cancelled, finalized.Cancel());
            Assert.IsFalse(finalized.IsValid);
        }

        [Test]
        public void ContextArenaRejectsCallbackReentrancyAndTokenExhaustion()
        {
            StateBlockId blockId = CreateBlockId(43UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(43UL, 1UL);
            StateSlotId derivedSlotId = CreateSlotId(43UL, 2UL);
            var rebuilder = new ReentrantRebuilder(storedSlotId);
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(blockId, StateBlockOwner.Actor, out _));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                2,
                default,
                null,
                out _));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                rebuilder,
                out _));
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(43UL, 1UL),
                1U,
                out ContextFrameLayout layout,
                out _));
            var arena = new ContextFrameArena(CreateGraphInstanceId(43UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            rebuilder.Arena = arena;
            rebuilder.Token = ReadPrivateToken(arena);

            ContextCommitResult result = FinalizeAndCommit(prepared);
            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(rebuilder.PrepareRejected);
            Assert.IsTrue(rebuilder.FinalizeRejected);
            Assert.IsTrue(rebuilder.CancelRejected);
            Assert.IsTrue(rebuilder.CommitRejected);

            var restoreArena = new ContextFrameArena(CreateGraphInstanceId(43UL), layout, 2);
            rebuilder.Arena = restoreArena;
            rebuilder.Token = 0UL;
            Assert.IsTrue(restoreArena.TryPrepareRestore(
                result.Frame,
                CreateTickFrame(2UL, 2UL, 2UL),
                out FinalizedContextCommit restored,
                out _));
            Assert.IsTrue(rebuilder.PrepareRejected);
            Assert.IsTrue(rebuilder.FinalizeRejected);
            Assert.IsTrue(rebuilder.CancelRejected);
            Assert.IsTrue(rebuilder.CommitRejected);
            Assert.AreEqual(ContextCommitStatus.Cancelled, restored.Cancel());

            var exhaustedArena = new ContextFrameArena(CreateGraphInstanceId(44UL), layout, 2);
            FieldInfo tokenField = typeof(ContextFrameArena).GetField(
                "_nextToken",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(tokenField);
            tokenField.SetValue(exhaustedArena, ulong.MaxValue - 1UL);
            Assert.IsTrue(exhaustedArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit lastPrepared,
                out _));
            Assert.AreEqual(ulong.MaxValue, ReadPrivateToken(exhaustedArena));
            Assert.IsTrue(lastPrepared.TryFinalize(
                out FinalizedContextCommit lastFinalized,
                out _));
            Assert.IsTrue(lastFinalized.Commit().Succeeded);
            Assert.IsFalse(lastPrepared.IsValid);
            Assert.IsFalse(lastFinalized.IsValid);
            Assert.IsFalse(exhaustedArena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out _,
                out ContextCommitStatus exhaustedStatus));
            Assert.AreEqual(ContextCommitStatus.RevisionExhausted, exhaustedStatus);
            Assert.IsTrue(exhaustedArena.HasCurrent);
            Assert.AreEqual(ContextCommitStatus.InvalidPreparation, lastPrepared.Cancel());
            Assert.AreEqual(ContextCommitStatus.InvalidPreparation, lastFinalized.Cancel());
        }

        [Test]
        public void DerivedRebuilderDisposeIsDeferredUntilCallbackReturnsAndFailsFinalize()
        {
            StateBlockId blockId = CreateBlockId(441UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(441UL, 1UL);
            StateSlotId derivedSlotId = CreateSlotId(441UL, 2UL);
            var rebuilder = new DisposeArenaRebuilder(storedSlotId);
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(blockId, StateBlockOwner.Actor, out _));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                3,
                default,
                null,
                out _));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                rebuilder,
                out _));
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(441UL, 1UL),
                1U,
                out ContextFrameLayout layout,
                out _));
            var arena = new ContextFrameArena(CreateGraphInstanceId(441UL), layout, 2);
            rebuilder.Arena = arena;

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit firstPrepared,
                out _));
            ContextFrame authority = FinalizeAndCommit(firstPrepared).Frame;
            Assert.IsTrue(authority.Retain());

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out PreparedContextCommit disposingPrepared,
                out _));
            rebuilder.ShouldDispose = true;
            Assert.IsFalse(disposingPrepared.TryFinalize(
                out FinalizedContextCommit finalized,
                out ContextCommitStatus status));

            Assert.AreEqual(ContextCommitStatus.InvalidPreparation, status);
            Assert.IsFalse(finalized.IsValid);
            Assert.IsTrue(rebuilder.AuthorityStayedAliveInsideCallback);
            Assert.IsTrue(rebuilder.DisposeWasDeferredInsideCallback);
            Assert.IsTrue(arena.IsDisposed);
            Assert.IsFalse(arena.Current.IsAlive);
            Assert.IsTrue(authority.IsAlive);
            Assert.IsTrue(authority.Release());
            Assert.IsFalse(authority.IsAlive);
        }

        [Test]
        public void RestoreReadViewAppliesDirectPoliciesAndRejectsInvalidSlots()
        {
            FrameLayoutId layoutId = CreateLayoutId(49UL, 1UL);
            StateBlockId blockId = CreateBlockId(49UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(49UL, 1UL);
            StateSlotId resetSlotId = CreateSlotId(49UL, 2UL);
            StateSlotId derivedSlotId = CreateSlotId(49UL, 3UL);
            var rebuilder = new ControllableDoubleRebuilder(storedSlotId);
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                resetSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.ResetToDefault,
                5,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                rebuilder,
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                layoutId,
                1U,
                out ContextFrameLayout layout,
                out diagnosticCode));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(storedSlotId, out StateSlot<int> storedSlot));
            Assert.IsTrue(layout.TryResolveSlot(resetSlotId, out StateSlot<int> resetSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out StateSlot<int> derivedSlot));

            var arena = new ContextFrameArena(CreateGraphInstanceId(49UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryGetWriter(block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(storedSlot, 7));
            Assert.IsTrue(writer.Write(resetSlot, 9));
            ContextFrame source = FinalizeAndCommit(prepared).Frame;
            int rebuildCount = rebuilder.InvocationCount;

            var view = new ContextRestoreReadView(source, layout);
            Assert.IsTrue(view.IsValid);
            Assert.AreEqual(source.Header, view.Header);
            Assert.AreSame(layout, view.Layout);
            Assert.IsTrue(view.TryRead(storedSlot, out int stored));
            Assert.AreEqual(7, stored);
            Assert.IsTrue(view.TryRead(resetSlot, out int reset));
            Assert.AreEqual(5, reset);
            Assert.IsFalse(view.TryRead(derivedSlot, out int derived));
            Assert.AreEqual(default(int), derived);
            Assert.AreEqual(rebuildCount, rebuilder.InvocationCount);
            Assert.IsFalse(view.TryRead(default(StateSlot<int>), out _));

            ContextFrameLayout otherLayout = CreateStoredIntLayout(
                CreateLayoutId(49UL, 2UL),
                CreateSlotId(49UL, 4UL),
                11);
            Assert.IsTrue(otherLayout.TryResolveSlot(
                CreateSlotId(49UL, 4UL),
                out StateSlot<int> otherSlot));
            Assert.IsFalse(view.TryRead(otherSlot, out _));

            var mismatched = new ContextRestoreReadView(source, otherLayout);
            Assert.IsFalse(mismatched.IsValid);
            Assert.AreEqual(default(StateFlowFrameHeader), mismatched.Header);
            Assert.IsNull(mismatched.Layout);
            Assert.IsFalse(mismatched.TryRead(otherSlot, out _));
        }

        [Test]
        public void RestoreRequiresNewEpochAndRecordsSourceIdentity()
        {
            FrameLayoutId layoutId = CreateLayoutId(50UL, 1UL);
            StateBlockId blockId = CreateBlockId(50UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(50UL, 1UL);
            StateSlotId resetSlotId = CreateSlotId(50UL, 2UL);
            StateSlotId derivedSlotId = CreateSlotId(50UL, 3UL);
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Temporal | ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                1,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                resetSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.ResetToDefault,
                5,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                new DoubleRebuilder(storedSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(layoutId, 1U, out ContextFrameLayout layout, out diagnosticCode));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(storedSlotId, out StateSlot<int> storedSlot));
            Assert.IsTrue(layout.TryResolveSlot(resetSlotId, out StateSlot<int> resetSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out StateSlot<int> derivedSlot));
            var arena = new ContextFrameArena(CreateGraphInstanceId(50UL), layout, 3);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(9UL, 3UL, 4UL),
                out PreparedContextCommit initial,
                out ContextCommitStatus status));
            Assert.IsTrue(initial.TryGetWriter(block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(storedSlot, 7));
            Assert.IsTrue(writer.Write(resetSlot, 9));
            Assert.IsFalse(writer.Write(derivedSlot, 99));
            ContextFrame source = FinalizeAndCommit(initial).Frame;
            Assert.IsTrue(source.Retain());
            Assert.AreEqual(7, source.Read(storedSlot));
            Assert.AreEqual(9, source.Read(resetSlot));
            Assert.AreEqual(14, source.Read(derivedSlot));

            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 3UL, 5UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);

            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 4UL, 5UL, 2UL, 2UL, 1UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);
            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 4UL, 5UL, 1UL, 1UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);
            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 4UL, 4UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);

            Assert.IsTrue(arena.TryPrepareRestore(
                source,
                CreateTickFrame(10UL, 4UL, 5UL),
                out FinalizedContextCommit restore,
                out status));
            ContextFrame restored = restore.Commit().Frame;
            Assert.AreEqual(2UL, restored.Revision.Value);
            Assert.AreEqual(new TimelineEpoch(4UL), restored.Header.Identity.TimelineEpoch);
            Assert.IsTrue(restored.Origin.IsRestore);
            Assert.AreEqual(source.Header.Identity.GraphInstanceId, restored.Origin.SourceGraphInstanceId);
            Assert.AreEqual(new TimelineEpoch(3UL), restored.Origin.SourceTimelineEpoch);
            Assert.AreEqual(new TimelineTick(9UL), restored.Origin.SourceTick);
            Assert.AreEqual(new ContextRevision(1UL), restored.Origin.SourceRevision);
            Assert.AreEqual(7, restored.Read(storedSlot));
            Assert.AreEqual(5, restored.Read(resetSlot));
            Assert.AreEqual(14, restored.Read(derivedSlot));
            Assert.IsTrue(source.Release());
        }

        [Test]
        public void RestoreEpochMustAdvanceBeyondTheCurrentAuthority()
        {
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(51UL, 1UL),
                CreateSlotId(51UL, 1UL),
                1);
            var arena = new ContextFrameArena(CreateGraphInstanceId(51UL), layout, 3);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(5UL, 5UL, 5UL),
                out PreparedContextCommit initial,
                out ContextCommitStatus status));
            ContextFrame source = FinalizeAndCommit(initial).Frame;
            Assert.IsTrue(source.Retain());

            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(2UL, 4UL, 6UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);

            Assert.IsTrue(arena.TryPrepareRestore(
                source,
                CreateTickFrame(2UL, 10UL, 6UL),
                out FinalizedContextCommit advanced,
                out status));
            Assert.IsTrue(advanced.Commit().Succeeded);
            Assert.IsFalse(arena.TryPrepareRestore(
                source,
                CreateTickFrame(3UL, 6UL, 7UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);
            Assert.IsTrue(source.Release());
        }

        [Test]
        public void RetainedFrameConsumesFixedArenaCapacityUntilReleased()
        {
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(60UL, 1UL),
                CreateSlotId(60UL, 1UL),
                1);
            var arena = new ContextFrameArena(CreateGraphInstanceId(60UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit firstPrepared,
                out ContextCommitStatus status));
            ContextFrame first = FinalizeAndCommit(firstPrepared).Frame;
            Assert.IsFalse(first.Release());
            Assert.IsTrue(first.Retain());

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out PreparedContextCommit secondPrepared,
                out status));
            Assert.IsTrue(FinalizeAndCommit(secondPrepared).Succeeded);

            Assert.IsFalse(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.CapacityExhausted, status);
            Assert.IsTrue(first.Release());
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(3UL, 1UL, 3UL),
                out PreparedContextCommit available,
                out status));
            Assert.AreEqual(ContextCommitStatus.Cancelled, available.Cancel());
        }

        [Test]
        public void ArenaOwnershipAndMaximumExternalRetainsDoNotOverflowFrameLiveness()
        {
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(60UL, 2UL),
                CreateSlotId(60UL, 2UL),
                1);
            var arena = new ContextFrameArena(CreateGraphInstanceId(602UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            ContextFrame frame = FinalizeAndCommit(prepared).Frame;

            FieldInfo storageField = typeof(ContextFrame).GetField(
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
            StateSlotId slotId = CreateSlotId(61UL, 1UL);
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(61UL, 1UL),
                slotId,
                0);
            Assert.IsTrue(layout.TryResolveBlock(
                CreateBlockId(61UL, 1UL),
                out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(slotId, out StateSlot<int> slot));
            GraphInstanceId graphInstanceId = CreateGraphInstanceId(61UL);
            var arena = new ContextFrameArena(graphInstanceId, layout, 2);

            ContextFrame first = CommitStoredValue(arena, block, slot, 1UL, 1);
            ContextFrame second = CommitStoredValue(arena, block, slot, 2UL, 2);
            Assert.IsFalse(first.IsAlive);
            Assert.AreEqual(2, second.Read(slot));

            ContextFrame third = CommitStoredValue(arena, block, slot, 3UL, 3);
            Assert.IsFalse(first.IsAlive);
            Assert.IsFalse(first.Retain());
            Assert.IsFalse(first.Release());
            Assert.AreNotEqual(first, third);
            Assert.Throws<InvalidOperationException>(() => first.Read(slot));
            Assert.AreEqual(3, third.Read(slot));

            var registry = new ContextCodecRegistry();
            Assert.IsTrue(registry.TryFreeze(out _));
            Assert.IsTrue(ContextProjectionCodec.TryCreate(
                layout,
                registry,
                ContextProjection.Temporal,
                out ContextProjectionCodec codec,
                out _));
            var encoded = new byte[codec.MaxEncodedSize];
            Assert.IsFalse(codec.TryEncode(
                first,
                encoded,
                out _,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrameHandle, diagnosticCode);

            var restoreArena = new ContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsFalse(restoreArena.TryPrepareRestore(
                first,
                CreateTickFrame(4UL, 4UL, 4UL),
                out _,
                out ContextCommitStatus status));
            Assert.AreEqual(ContextCommitStatus.LayoutMismatch, status);
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
            FrameLayoutId layoutId = CreateLayoutId(72UL, 1UL);
            StateBlockId blockId = CreateBlockId(72UL, 1UL);
            StateSlotId temporalSlotId = CreateSlotId(72UL, 1UL);
            StateSlotId durableSlotId = CreateSlotId(72UL, 2UL);
            StateSlotId derivedSlotId = CreateSlotId(72UL, 3UL);
            CodecDescriptor codecDescriptor = CreateCodecDescriptor(72UL, 1U);
            ContextFrameLayout layout = CreateProjectionLayout(
                layoutId,
                blockId,
                temporalSlotId,
                durableSlotId,
                derivedSlotId,
                codecDescriptor,
                1U);
            var registry = new ContextCodecRegistry();
            var codec = new Int32Codec(codecDescriptor);

            Assert.IsTrue(registry.TryRegister(codec, out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(registry.TryFreeze(out diagnosticCode));
            Assert.IsTrue(registry.TryResolve(
                codecDescriptor,
                out IContextValueCodec<int> resolved,
                out diagnosticCode));
            Assert.AreSame(codec, resolved);
            Assert.IsFalse(registry.TryResolve(
                CreateCodecDescriptor(720UL, 1U),
                out IContextValueCodec<int> _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.UnknownCodec, diagnosticCode);
            Assert.IsFalse(registry.TryResolve(
                new CodecDescriptor(codecDescriptor.CodecId, 2U),
                out IContextValueCodec<int> _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.UnsupportedCodecVersion, diagnosticCode);
            Assert.IsFalse(registry.TryResolve(
                codecDescriptor,
                out IContextValueCodec<float> _,
                out diagnosticCode));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateSlot, diagnosticCode);

            Assert.IsTrue(ContextProjectionCodec.TryCreate(
                layout,
                registry,
                ContextProjection.Temporal,
                out ContextProjectionCodec temporalCodec,
                out diagnosticCode));
            Assert.IsTrue(ContextProjectionCodec.TryCreate(
                layout,
                registry,
                ContextProjection.Durable,
                out ContextProjectionCodec durableCodec,
                out diagnosticCode));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(temporalSlotId, out StateSlot<int> temporalSlot));
            Assert.IsTrue(layout.TryResolveSlot(durableSlotId, out StateSlot<int> durableSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out StateSlot<int> derivedSlot));
            GraphInstanceId graphInstanceId = CreateGraphInstanceId(72UL);
            var sourceArena = new ContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit sourceCommit,
                out _));
            Assert.IsTrue(sourceCommit.TryGetWriter(block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(temporalSlot, 7));
            Assert.IsTrue(writer.Write(durableSlot, 9));
            ContextFrame sourceFrame = FinalizeAndCommit(sourceCommit).Frame;

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
            var temporalArena = new ContextFrameArena(graphInstanceId, layout, 2);
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
                out ContextCommitStatus status,
                out diagnosticCode));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                temporalBytes,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 2UL, 1UL, 1UL, 2UL),
                out _,
                out _,
                out status,
                out diagnosticCode));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);
            Assert.IsFalse(temporalCodec.TryDecodeAndPrepareRestore(
                temporalBytes,
                temporalArena,
                CreateTickFrame(2UL, 2UL, 1UL),
                out _,
                out _,
                out status,
                out diagnosticCode));
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);
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
                out FinalizedContextCommit temporalRestore,
                out int temporalRead,
                out status,
                out diagnosticCode),
                $"{diagnosticCode}/{status}");
            ContextFrame temporalFrame = temporalRestore.Commit().Frame;
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
            Assert.AreEqual(ContextCommitStatus.InvalidOrigin, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidRestoreMetadata, diagnosticCode);

            var durableBytes = new byte[durableCodec.MaxEncodedSize];
            Assert.IsTrue(durableCodec.TryEncode(
                sourceFrame,
                durableBytes,
                out int durableLength,
                out diagnosticCode));
            var foreignGraphArena = new ContextFrameArena(
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
            Assert.AreEqual(ContextCommitStatus.GraphMismatch, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrameHandle, diagnosticCode);
            var durableArena = new ContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(durableCodec.TryDecodeAndPrepareRestore(
                new ReadOnlySpan<byte>(durableBytes, 0, durableLength),
                durableArena,
                CreateTickFrame(2UL, 3UL, 3UL),
                out FinalizedContextCommit durableRestore,
                out int durableRead,
                out status,
                out diagnosticCode),
                $"{diagnosticCode}/{status}");
            ContextFrame durableFrame = durableRestore.Commit().Frame;
            Assert.AreEqual(durableLength, durableRead);
            Assert.AreEqual(1, durableFrame.Read(temporalSlot));
            Assert.AreEqual(9, durableFrame.Read(durableSlot));
            Assert.AreEqual(2, durableFrame.Read(derivedSlot));
        }

        [Test]
        public void ProjectionCodecExceptionRollsBackCandidateAndRethrows()
        {
            FrameLayoutId layoutId = CreateLayoutId(73UL, 1UL);
            StateBlockId blockId = CreateBlockId(73UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(73UL, 1UL);
            StateSlotId derivedSlotId = CreateSlotId(73UL, 2UL);
            CodecDescriptor codecDescriptor = CreateCodecDescriptor(73UL, 1U);
            ContextFrameLayout layout = CreateProjectionLayout(
                layoutId,
                blockId,
                storedSlotId,
                derivedSlotId,
                codecDescriptor,
                1U,
                4);
            var registry = new ContextCodecRegistry();
            var codec = new ThrowOnDecodeInt32Codec(codecDescriptor);
            Assert.IsTrue(registry.TryRegister(
                codec,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(registry.TryFreeze(out diagnosticCode));
            Assert.IsTrue(ContextProjectionCodec.TryCreate(
                layout,
                registry,
                ContextProjection.Temporal,
                out ContextProjectionCodec projectionCodec,
                out diagnosticCode));

            GraphInstanceId graphInstanceId = CreateGraphInstanceId(73UL);
            var sourceArena = new ContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit sourceCommit,
                out _));
            ContextFrame source = FinalizeAndCommit(sourceCommit).Frame;
            var encoded = new byte[projectionCodec.MaxEncodedSize];
            Assert.IsTrue(projectionCodec.TryEncode(
                source,
                encoded,
                out int encodedLength,
                out diagnosticCode));

            var restoreArena = new ContextFrameArena(graphInstanceId, layout, 2);
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
                out PreparedContextCommit afterException,
                out _));
            Assert.AreEqual(ContextCommitStatus.Cancelled, afterException.Cancel());
        }

        [Test]
        public void CustomCodecDisposeIsDeferredUntilDecodeReturnsAndFailsRestore()
        {
            FrameLayoutId layoutId = CreateLayoutId(731UL, 1UL);
            StateBlockId blockId = CreateBlockId(731UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(731UL, 1UL);
            StateSlotId derivedSlotId = CreateSlotId(731UL, 2UL);
            CodecDescriptor codecDescriptor = CreateCodecDescriptor(731UL, 1U);
            ContextFrameLayout layout = CreateProjectionLayout(
                layoutId,
                blockId,
                storedSlotId,
                derivedSlotId,
                codecDescriptor,
                1U,
                4);
            var registry = new ContextCodecRegistry();
            var codec = new DisposeOnDecodeInt32Codec(codecDescriptor);
            Assert.IsTrue(registry.TryRegister(codec, out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(registry.TryFreeze(out diagnosticCode));
            Assert.IsTrue(ContextProjectionCodec.TryCreate(
                layout,
                registry,
                ContextProjection.Temporal,
                out ContextProjectionCodec projectionCodec,
                out diagnosticCode));

            GraphInstanceId graph = CreateGraphInstanceId(731UL);
            var sourceArena = new ContextFrameArena(graph, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit sourcePrepared,
                out _));
            ContextFrame source = FinalizeAndCommit(sourcePrepared).Frame;
            var encoded = new byte[projectionCodec.MaxEncodedSize];
            Assert.IsTrue(projectionCodec.TryEncode(
                source,
                encoded,
                out int encodedLength,
                out diagnosticCode));

            var restoreArena = new ContextFrameArena(graph, layout, 2);
            codec.Arena = restoreArena;
            Assert.IsFalse(projectionCodec.TryDecodeAndPrepareRestore(
                new ReadOnlySpan<byte>(encoded, 0, encodedLength),
                restoreArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out FinalizedContextCommit finalized,
                out int bytesRead,
                out ContextCommitStatus status,
                out diagnosticCode));

            Assert.IsFalse(finalized.IsValid);
            Assert.AreEqual(0, bytesRead);
            Assert.AreEqual(ContextCommitStatus.InvalidPreparation, status);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrameHandle, diagnosticCode);
            Assert.IsTrue(codec.DisposeWasDeferredInsideCallback);
            Assert.IsTrue(restoreArena.IsDisposed);
            Assert.IsFalse(restoreArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out status));
            Assert.AreEqual(ContextCommitStatus.InvalidPreparation, status);
        }

        [Test]
        public void RestoreDerivedFailureAndExceptionRollBackDirectAndProjectionCandidates()
        {
            FrameLayoutId layoutId = CreateLayoutId(74UL, 1UL);
            StateBlockId blockId = CreateBlockId(74UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(74UL, 1UL);
            StateSlotId derivedSlotId = CreateSlotId(74UL, 2UL);
            var rebuilder = new ControllableDoubleRebuilder(storedSlotId);
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(blockId, StateBlockOwner.Actor, out _));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                3,
                default,
                null,
                out _));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { storedSlotId },
                rebuilder,
                out _));
            Assert.IsTrue(builder.TryFreeze(layoutId, 1U, out ContextFrameLayout layout, out _));
            var registry = new ContextCodecRegistry();
            Assert.IsTrue(registry.TryFreeze(out _));
            Assert.IsTrue(ContextProjectionCodec.TryCreate(
                layout,
                registry,
                ContextProjection.Temporal,
                out ContextProjectionCodec projectionCodec,
                out _));

            GraphInstanceId graphInstanceId = CreateGraphInstanceId(74UL);
            var sourceArena = new ContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit sourceCommit,
                out _));
            ContextFrame source = FinalizeAndCommit(sourceCommit).Frame;
            var encoded = new byte[projectionCodec.MaxEncodedSize];
            Assert.IsTrue(projectionCodec.TryEncode(source, encoded, out int encodedLength, out _));
            rebuilder.ShouldFail = true;

            var projectionFailureArena = new ContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsFalse(projectionCodec.TryDecodeAndPrepareRestore(
                new ReadOnlySpan<byte>(encoded, 0, encodedLength),
                projectionFailureArena,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out _,
                out ContextCommitStatus projectionFailureStatus,
                out CoCoDiagnosticCode projectionFailureDiagnostic));
            Assert.AreEqual(
                ContextCommitStatus.DerivedRebuildFailed,
                projectionFailureStatus);
            Assert.AreEqual(
                CoCoDiagnosticCode.CommitPreparationFailed,
                projectionFailureDiagnostic);
            Assert.IsTrue(projectionFailureArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out PreparedContextCommit afterProjectionFailure,
                out _));
            Assert.AreEqual(
                ContextCommitStatus.Cancelled,
                afterProjectionFailure.Cancel());

            var directFailureArena = new ContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsFalse(directFailureArena.TryPrepareRestore(
                source,
                CreateTickFrame(2UL, 2UL, 2UL),
                out _,
                out ContextCommitStatus directFailureStatus));
            Assert.AreEqual(
                ContextCommitStatus.DerivedRebuildFailed,
                directFailureStatus);
            Assert.IsTrue(directFailureArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out PreparedContextCommit afterDirectFailure,
                out _));
            Assert.AreEqual(ContextCommitStatus.Cancelled, afterDirectFailure.Cancel());

            rebuilder.ShouldFail = false;
            rebuilder.ShouldThrow = true;

            var projectionArena = new ContextFrameArena(graphInstanceId, layout, 2);
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
                out PreparedContextCommit afterProjectionException,
                out _));
            Assert.AreEqual(
                ContextCommitStatus.Cancelled,
                afterProjectionException.Cancel());

            var directArena = new ContextFrameArena(graphInstanceId, layout, 2);
            var directException = Assert.Throws<InvalidOperationException>(() =>
                directArena.TryPrepareRestore(
                    source,
                    CreateTickFrame(2UL, 2UL, 2UL),
                    out _,
                    out _));
            Assert.AreEqual("Test rebuilder failure.", directException.Message);
            Assert.IsTrue(directArena.TryPrepare(
                CreateTickFrame(2UL, 2UL, 2UL),
                out PreparedContextCommit afterDirectException,
                out _));
            Assert.AreEqual(ContextCommitStatus.Cancelled, afterDirectException.Cancel());
        }

        [Test]
        public void ContextReadAndCodecRoundTripAllocateNoManagedMemoryAfterWarmup()
        {
            StateSlotId slotId = CreateSlotId(71UL, 1UL);
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(71UL, 1UL),
                slotId,
                42);
            Assert.IsTrue(layout.TryResolveSlot(slotId, out StateSlot<int> slot));
            var arena = new ContextFrameArena(CreateGraphInstanceId(71UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            ContextFrame frame = FinalizeAndCommit(prepared).Frame;
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
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(71UL, 2UL),
                CreateSlotId(71UL, 2UL),
                42);
            var arena = new ContextFrameArena(CreateGraphInstanceId(712UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            ContextFrame frame = FinalizeAndCommit(prepared).Frame;
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
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(74UL, 1UL),
                CreateSlotId(74UL, 1UL),
                42);
            var arena = new ContextFrameArena(CreateGraphInstanceId(74UL), layout, 2);
            var ticks = new TickFrame[10100];
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
            FrameLayoutId layoutId = CreateLayoutId(73UL, 1UL);
            StateBlockId blockId = CreateBlockId(73UL, 1UL);
            StateSlotId storedSlotId = CreateSlotId(73UL, 1UL);
            StateSlotId derivedSlotId = CreateSlotId(73UL, 2UL);
            CodecDescriptor codecDescriptor = CreateCodecDescriptor(73UL, 1U);
            ContextFrameLayout layout = CreateProjectionLayout(
                layoutId,
                blockId,
                storedSlotId,
                derivedSlotId,
                codecDescriptor,
                1U,
                0);
            var registry = new ContextCodecRegistry();
            Assert.IsTrue(registry.TryRegister(new Int32Codec(codecDescriptor), out _));
            Assert.IsTrue(registry.TryFreeze(out _));
            Assert.IsTrue(ContextProjectionCodec.TryCreate(
                layout,
                registry,
                ContextProjection.Temporal,
                out ContextProjectionCodec projectionCodec,
                out _));
            Assert.IsTrue(layout.TryResolveBlock(blockId, out StateBlockHandle block));
            Assert.IsTrue(layout.TryResolveSlot(storedSlotId, out StateSlot<int> storedSlot));
            Assert.IsTrue(layout.TryResolveSlot(derivedSlotId, out StateSlot<int> derivedSlot));
            GraphInstanceId graphInstanceId = CreateGraphInstanceId(73UL);
            var sourceArena = new ContextFrameArena(graphInstanceId, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit sourceCommit,
                out _));
            Assert.IsTrue(sourceCommit.TryGetWriter(block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(storedSlot, 42));
            ContextFrame sourceFrame = FinalizeAndCommit(sourceCommit).Frame;
            var restoreArena = new ContextFrameArena(graphInstanceId, layout, 2);
            var bytes = new byte[projectionCodec.MaxEncodedSize];
            var ticks = new TickFrame[10100];
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

        [Test]
        public void ArenaPreviousReadsLayoutDefaultsWithoutFabricatingCommittedIdentity()
        {
            StateSlotId slotId = CreateSlotId(75UL, 1UL);
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(75UL, 1UL),
                slotId,
                42);
            Assert.IsTrue(layout.TryResolveSlot(slotId, out StateSlot<int> slot));
            var arena = new ContextFrameArena(CreateGraphInstanceId(75UL), layout, 2);

            ContextFrameReadView defaults = arena.Previous;
            Assert.IsTrue(defaults.IsValid);
            Assert.IsFalse(defaults.HasCommittedFrame);
            Assert.IsFalse(defaults.Header.IsValid);
            Assert.IsFalse(defaults.Revision.IsValid);
            Assert.AreEqual(42, defaults.Read(slot));

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryFinalize(out FinalizedContextCommit finalized, out _));
            Assert.IsTrue(finalized.IsCommitReady);
            ContextFrame committed = finalized.CommitNoFailUnchecked();
            ContextFrameReadView previous = arena.Previous;
            Assert.IsTrue(previous.HasCommittedFrame);
            Assert.AreEqual(1UL, previous.Revision.Value);
            Assert.AreEqual(committed.Header, previous.Header);
            Assert.AreEqual(42, previous.Read(slot));
        }

        [Test]
        public void FinalizedContextCheckedCommitRejectsStaleProofAndUncheckedCoreSwapsValidProof()
        {
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(751UL, 1UL),
                CreateSlotId(751UL, 1UL),
                12);
            var arena = new ContextFrameArena(CreateGraphInstanceId(751UL), layout, 2);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit firstPrepared,
                out _));
            Assert.IsTrue(firstPrepared.TryFinalize(
                out FinalizedContextCommit staleProof,
                out _));
            Assert.IsTrue(staleProof.IsCommitReady);
            Assert.IsTrue(staleProof.Commit().Succeeded);
            Assert.IsFalse(staleProof.IsCommitReady);
            Assert.AreEqual(
                ContextCommitStatus.InvalidPreparation,
                staleProof.Commit().Status);

            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(2UL, 1UL, 2UL),
                out PreparedContextCommit secondPrepared,
                out _));
            Assert.IsTrue(secondPrepared.TryFinalize(
                out FinalizedContextCommit validProof,
                out _));
            Assert.IsTrue(validProof.IsCommitReady);

            ContextFrame committed = validProof.CommitNoFailUnchecked();

            Assert.AreEqual(committed, arena.Current);
            Assert.AreEqual(2UL, committed.Revision.Value);
            Assert.IsFalse(validProof.IsCommitReady);
        }

        [Test]
        public void RestoreValidationIsPureAndDoesNotReserveArenaCapacity()
        {
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(76UL, 1UL),
                CreateSlotId(76UL, 1UL),
                7);
            GraphInstanceId graph = CreateGraphInstanceId(76UL);
            var sourceArena = new ContextFrameArena(graph, layout, 2);
            Assert.IsTrue(sourceArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            ContextFrame source = FinalizeAndCommit(prepared).Frame;
            var targetArena = new ContextFrameArena(graph, layout, 2);

            Assert.IsTrue(targetArena.TryValidateRestore(
                source,
                CreateTickFrame(2UL, 2UL, 2UL),
                out ContextCommitStatus status),
                status.ToString());
            Assert.IsTrue(targetArena.HasAvailableCapacity);
            Assert.IsFalse(targetArena.HasCurrent);
            Assert.IsTrue(targetArena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit ordinary,
                out status),
                status.ToString());
            Assert.AreEqual(ContextCommitStatus.Cancelled, ordinary.Cancel());
        }

        [Test]
        public void ArenaDisposeReleasesOnlyArenaOwnership()
        {
            StateSlotId slotId = CreateSlotId(77UL, 1UL);
            ContextFrameLayout layout = CreateStoredIntLayout(
                CreateLayoutId(77UL, 1UL),
                slotId,
                11);
            Assert.IsTrue(layout.TryResolveSlot(slotId, out StateSlot<int> slot));
            var arena = new ContextFrameArena(CreateGraphInstanceId(77UL), layout, 2);
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(1UL, 1UL, 1UL),
                out PreparedContextCommit prepared,
                out _));
            ContextFrame retained = FinalizeAndCommit(prepared).Frame;
            Assert.IsTrue(retained.Retain());

            arena.Dispose();
            Assert.IsTrue(arena.IsDisposed);
            Assert.IsFalse(arena.HasCurrent);
            Assert.IsFalse(arena.Previous.IsValid);
            Assert.IsTrue(retained.IsAlive);
            Assert.AreEqual(11, retained.Read(slot));
            Assert.IsTrue(retained.Release());
            Assert.IsFalse(retained.IsAlive);
            arena.Dispose();
        }

        private static bool RunProjectionCodecCycle(
            ContextProjectionCodec codec,
            ContextFrame source,
            byte[] destination,
            ContextFrameArena restoreArena,
            TickFrame resumedTick,
            StateSlot<int> storedSlot,
            StateSlot<int> derivedSlot,
            ref int checksum)
        {
            if (!codec.TryEncode(source, destination, out int written, out _) ||
                !codec.TryDecodeAndPrepareRestore(
                    new ReadOnlySpan<byte>(destination, 0, written),
                    restoreArena,
                    resumedTick,
                    out FinalizedContextCommit finalized,
                    out int read,
                    out _,
                    out _) ||
                read != written)
            {
                return false;
            }

            ContextCommitResult result = finalized.Commit();
            if (!result.Succeeded)
            {
                return false;
            }

            checksum += result.Frame.Read(storedSlot);
            checksum += result.Frame.Read(derivedSlot);
            return true;
        }

        private static bool RunContextCommitCycle(
            ContextFrameArena arena,
            TickFrame tickFrame)
        {
            if (!arena.TryPrepare(
                    tickFrame,
                    out PreparedContextCommit prepared,
                    out _) ||
                !prepared.TryFinalize(
                    out FinalizedContextCommit finalized,
                    out _))
            {
                return false;
            }

            return finalized.Commit().Succeeded;
        }

        private static ContextCommitResult FinalizeAndCommit(
            PreparedContextCommit prepared)
        {
            Assert.IsTrue(
                prepared.TryFinalize(
                    out FinalizedContextCommit finalized,
                    out ContextCommitStatus status),
                status.ToString());
            return finalized.Commit();
        }

        private static ContextFrame CommitStoredValue(
            ContextFrameArena arena,
            StateBlockHandle block,
            StateSlot<int> slot,
            ulong frame,
            int value)
        {
            Assert.IsTrue(arena.TryPrepare(
                CreateTickFrame(frame, 1UL, frame),
                out PreparedContextCommit prepared,
                out _));
            Assert.IsTrue(prepared.TryGetWriter(block, out ContextFrameWriter writer));
            Assert.IsTrue(writer.Write(slot, value));
            return FinalizeAndCommit(prepared).Frame;
        }

        private static ContextFrameLayout CreateStoredIntLayout(
            FrameLayoutId layoutId,
            StateSlotId slotId,
            int defaultValue)
        {
            var builder = new ContextFrameLayoutBuilder();
            StateBlockId blockId = CreateBlockId(layoutId.High, layoutId.Low);
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                slotId,
                ContextProjection.Temporal | ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                defaultValue,
                default,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(layoutId, 1U, out ContextFrameLayout layout, out diagnosticCode));
            return layout;
        }

        private static ContextFrameLayout CreateProjectionLayout(
            FrameLayoutId layoutId,
            StateBlockId blockId,
            StateSlotId storedSlotId,
            StateSlotId derivedSlotId,
            CodecDescriptor codec,
            uint version,
            int defaultValue)
        {
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                storedSlotId,
                ContextProjection.Temporal | ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                defaultValue,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                ContextProjection.Temporal | ContextProjection.Durable,
                0,
                default,
                new[] { storedSlotId },
                new DoubleRebuilder(storedSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                layoutId,
                version,
                out ContextFrameLayout layout,
                out diagnosticCode));
            return layout;
        }

        private static ContextFrameLayout CreateProjectionLayout(
            FrameLayoutId layoutId,
            StateBlockId blockId,
            StateSlotId temporalSlotId,
            StateSlotId durableSlotId,
            StateSlotId derivedSlotId,
            CodecDescriptor codec,
            uint version)
        {
            var builder = new ContextFrameLayoutBuilder();
            Assert.IsTrue(builder.TryAddBlock(
                blockId,
                StateBlockOwner.Actor,
                out CoCoDiagnosticCode diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                temporalSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                1,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddSlot(
                blockId,
                durableSlotId,
                ContextProjection.Durable,
                ContextRestorePolicy.Stored,
                2,
                codec,
                null,
                out diagnosticCode));
            Assert.IsTrue(builder.TryAddDerivedSlot(
                blockId,
                derivedSlotId,
                ContextProjection.Temporal,
                0,
                default,
                new[] { temporalSlotId },
                new DoubleRebuilder(temporalSlotId),
                out diagnosticCode));
            Assert.IsTrue(builder.TryFreeze(
                layoutId,
                version,
                out ContextFrameLayout layout,
                out diagnosticCode));
            return layout;
        }

        private static GraphInstanceId CreateGraphInstanceId(ulong value)
        {
            Assert.IsTrue(GraphInstanceId.TryCreate(value, out GraphInstanceId id));
            return id;
        }

        private static FrameLayoutId CreateLayoutId(ulong high, ulong low)
        {
            Assert.IsTrue(FrameLayoutId.TryCreate(high, low, out FrameLayoutId id));
            return id;
        }

        private static StateBlockId CreateBlockId(ulong high, ulong low)
        {
            Assert.IsTrue(StateBlockId.TryCreate(high, low, out StateBlockId id));
            return id;
        }

        private static StateSlotId CreateSlotId(ulong high, ulong low)
        {
            Assert.IsTrue(StateSlotId.TryCreate(high, low, out StateSlotId id));
            return id;
        }

        private static CodecDescriptor CreateCodecDescriptor(ulong value, uint version)
        {
            Assert.IsTrue(CodecId.TryCreate(0UL, value, out CodecId codecId));
            return new CodecDescriptor(codecId, version);
        }

        private static OperationSectionId CreateSectionId(ulong high, ulong low)
        {
            Assert.IsTrue(OperationSectionId.TryCreate(high, low, out OperationSectionId id));
            return id;
        }

        private static TickFrame CreateTickFrame(ulong tick, ulong epoch, ulong sequence)
        {
            return CreateTickFrame(tick, epoch, sequence, 1UL, 1UL, 1UL);
        }

        private static TickFrame CreateTickFrame(
            ulong tick,
            ulong epoch,
            ulong sequence,
            ulong timelineHigh,
            ulong timelineLow,
            ulong clockDomain)
        {
            Assert.IsTrue(TimelineId.TryCreate(
                timelineHigh,
                timelineLow,
                out TimelineId timelineId));
            Assert.IsTrue(TimelinePosition.TryCreate(tick * 0.016d, out TimelinePosition position));
            Assert.IsTrue(ClockDomainId.TryCreate(clockDomain, out ClockDomainId clockDomainId));
            Assert.IsTrue(TickFrame.TryCreate(
                0.016d,
                timelineId,
                position,
                new TimelineTick(tick),
                clockDomainId,
                new ExecutionSequence(sequence),
                new TimelineEpoch(epoch),
                out TickFrame frame,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            return frame;
        }

        private static ulong ReadPrivateToken(ContextFrameArena arena)
        {
            FieldInfo tokenField = typeof(ContextFrameArena).GetField(
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

        private static void CreateContinuousPairOperationFrame(
            ulong seed,
            out OperationFrame frame,
            out OperationSectionHandle<IContinuousPairSection> handle,
            out OperationSectionField<int> horizontalField,
            out OperationSectionField<int> verticalField)
        {
            var builder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(seed, 1UL),
                OperationSectionMode.Continuous,
                new ContinuousPairViewFactory(),
                out OperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(seed, 2UL),
                out OperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(registry.TryResolve(requirement, out handle));
            Assert.IsTrue(registry.TryResolveField(handle, 0, out horizontalField));
            Assert.IsTrue(registry.TryResolveField(handle, 1, out verticalField));
            Assert.IsTrue(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(seed),
                new[] { requirement },
                out frame,
                out diagnostic),
                diagnostic.Message);
        }

        private static void CreateMovementOperationFrame(
            ulong seed,
            out OperationFrame frame,
            out OperationSectionHandle<IMovementSection> handle,
            out OperationSectionField<int> distanceField)
        {
            var builder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(seed, 1UL),
                OperationSectionMode.Continuous,
                new MovementViewFactory(),
                out OperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(seed, 2UL),
                out OperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(registry.TryResolve(requirement, out handle));
            Assert.IsTrue(registry.TryResolveField(handle, 0, out distanceField));
            Assert.IsTrue(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(seed),
                new[] { requirement },
                out frame,
                out diagnostic),
                diagnostic.Message);
        }

        private static void CreateAttackOperationFrame(
            ulong seed,
            out OperationFrame frame,
            out OperationSectionHandle<IAttackSection> handle)
        {
            var builder = new OperationSectionRegistryBuilder();
            Assert.IsTrue(builder.TryRegister(
                CreateSectionId(seed, 1UL),
                OperationSectionMode.Discrete,
                new AttackViewFactory(),
                out OperationSectionRequirement requirement,
                out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                CreateLayoutId(seed, 2UL),
                out OperationSectionRegistry registry,
                out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(registry.TryResolve(requirement, out handle));
            Assert.IsTrue(OperationFrame.TryCreate(
                registry,
                CreateGraphInstanceId(seed),
                new[] { requirement },
                out frame,
                out diagnostic),
                diagnostic.Message);
        }

        private static void AssertInvalidSection<TSection>()
            where TSection : class, IOperationSection
        {
            Assert.IsFalse(OperationSectionRequirement.TryCreate<TSection>(
                CreateSectionId(99UL, 1UL),
                OperationSectionMode.Continuous,
                out _,
                out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidOperationSection, diagnostic.Code);
        }

        private static void AssertShapeField(
            OperationSectionFieldShape field,
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

        private interface IMovementSection : IOperationSection
        {
            int Distance { get; }
        }

        private interface IContinuousPairSection : IOperationSection
        {
            int Horizontal { get; }
            int Vertical { get; }
        }

        private interface IManifestOnlySection : IOperationSection
        {
        }

        private sealed class ManifestSectionClass : IOperationSection
        {
        }

        private interface IAlternateMovementSection : IOperationSection
        {
            int Distance { get; }
        }

        private interface ICompositeShapeSection : IOperationSection
        {
            short Zulu { get; }
            long Middle { get; }
            int Alpha { get; }
        }

        private interface ICompositeShapeTwinSection : IOperationSection
        {
            int Alpha { get; }
            short Zulu { get; }
            long Middle { get; }
        }

        private interface IDifferentCompositeShapeSection : IOperationSection
        {
            int Alpha { get; }
            int Middle { get; }
            short Zulu { get; }
        }

        private interface IAttackSection : IOperationSection
        {
            int Damage { get; }
        }

        private interface INestedValueSection : IOperationSection
        {
            NestedValue Value { get; }
        }

        private interface IInheritedMovementSection : IMovementSection
        {
            int VerticalDistance { get; }
        }

        private interface IWritableSection : IOperationSection
        {
            int Value { get; set; }
        }

        private interface IMethodSection : IOperationSection
        {
            int Value { get; }
            void Apply();
        }

        private interface IEventSection : IOperationSection
        {
            int Value { get; }
            event Action Changed;
        }

        private interface IIndexerSection : IOperationSection
        {
            int this[int index] { get; }
        }

        private interface IReferenceSection : IOperationSection
        {
            object Value { get; }
        }

        private interface IStringSection : IOperationSection
        {
            string Value { get; }
        }

        private interface IRefReturnSection : IOperationSection
        {
            ref int Value { get; }
        }

        private sealed class MovementView : IMovementSection
        {
            private readonly OperationSectionReader _reader;
            private readonly OperationSectionField<int> _distanceField;

            public MovementView(
                OperationSectionReader reader,
                OperationSectionField<int> distanceField)
            {
                _reader = reader;
                _distanceField = distanceField;
            }

            public int Distance => _reader.Read(_distanceField);
        }

        private sealed class ContinuousPairView : IContinuousPairSection
        {
            private readonly OperationSectionReader _reader;
            private readonly OperationSectionField<int> _horizontalField;
            private readonly OperationSectionField<int> _verticalField;

            public ContinuousPairView(
                OperationSectionReader reader,
                OperationSectionField<int> horizontalField,
                OperationSectionField<int> verticalField)
            {
                _reader = reader;
                _horizontalField = horizontalField;
                _verticalField = verticalField;
            }

            public int Horizontal => _reader.Read(_horizontalField);
            public int Vertical => _reader.Read(_verticalField);
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
            private readonly OperationSectionReader _reader;
            private readonly OperationSectionField<int> _damageField;

            public AttackView(
                OperationSectionReader reader,
                OperationSectionField<int> damageField)
            {
                _reader = reader;
                _damageField = damageField;
            }

            public int Damage => _reader.Read(_damageField);
        }

        private sealed class NestedValueView : INestedValueSection
        {
            private readonly OperationSectionReader _reader;
            private readonly OperationSectionField<NestedValue> _valueField;

            public NestedValueView(
                OperationSectionReader reader,
                OperationSectionField<NestedValue> valueField)
            {
                _reader = reader;
                _valueField = valueField;
            }

            public NestedValue Value => _reader.Read(_valueField);
        }

        private sealed class MovementViewFactory : IOperationSectionViewFactory<IMovementSection>
        {
            public IMovementSection Create(in OperationSectionViewContext<IMovementSection> context)
            {
                if (!context.TryGetField(0, out OperationSectionField<int> field))
                {
                    throw new InvalidOperationException("Movement field was not pre-resolved.");
                }

                return new MovementView(context.Reader, field);
            }
        }

        private sealed class ContinuousPairViewFactory :
            IOperationSectionViewFactory<IContinuousPairSection>
        {
            public IContinuousPairSection Create(
                in OperationSectionViewContext<IContinuousPairSection> context)
            {
                if (!context.TryGetField(0, out OperationSectionField<int> horizontalField) ||
                    !context.TryGetField(1, out OperationSectionField<int> verticalField))
                {
                    throw new InvalidOperationException(
                        "Continuous pair fields were not pre-resolved.");
                }

                return new ContinuousPairView(
                    context.Reader,
                    horizontalField,
                    verticalField);
            }
        }

        private sealed class AlternateMovementViewFactory :
            IOperationSectionViewFactory<IAlternateMovementSection>
        {
            public IAlternateMovementSection Create(
                in OperationSectionViewContext<IAlternateMovementSection> context)
            {
                return new AlternateMovementView();
            }
        }

        private sealed class CompositeShapeViewFactory :
            IOperationSectionViewFactory<ICompositeShapeSection>
        {
            public ICompositeShapeSection Create(
                in OperationSectionViewContext<ICompositeShapeSection> context)
            {
                return new CompositeShapeView();
            }
        }

        private sealed class AttackViewFactory : IOperationSectionViewFactory<IAttackSection>
        {
            public IAttackSection Create(in OperationSectionViewContext<IAttackSection> context)
            {
                if (!context.TryGetField(0, out OperationSectionField<int> field))
                {
                    throw new InvalidOperationException("Attack field was not pre-resolved.");
                }

                return new AttackView(context.Reader, field);
            }
        }

        private sealed class NestedValueViewFactory :
            IOperationSectionViewFactory<INestedValueSection>
        {
            public INestedValueSection Create(
                in OperationSectionViewContext<INestedValueSection> context)
            {
                if (!context.TryGetField(0, out OperationSectionField<NestedValue> field))
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

        private sealed class AddOneRebuilder : IDerivedStateRebuilder<int>
        {
            private readonly StateSlotId _dependency;

            public AddOneRebuilder(StateSlotId dependency)
            {
                _dependency = dependency;
            }

            public bool TryRebuild(in DerivedStateReadContext context, out int value)
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

        private sealed class DoubleRebuilder : IDerivedStateRebuilder<int>
        {
            private readonly StateSlotId _dependency;

            public DoubleRebuilder(StateSlotId dependency)
            {
                _dependency = dependency;
            }

            public bool TryRebuild(in DerivedStateReadContext context, out int value)
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

        private sealed class ControllableDoubleRebuilder : IDerivedStateRebuilder<int>
        {
            private readonly StateSlotId _dependency;

            public ControllableDoubleRebuilder(StateSlotId dependency)
            {
                _dependency = dependency;
            }

            public bool ShouldFail { get; set; }
            public bool ShouldThrow { get; set; }
            public int InvocationCount { get; private set; }

            public bool TryRebuild(in DerivedStateReadContext context, out int value)
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

        private sealed class ReentrantRebuilder : IDerivedStateRebuilder<int>
        {
            private readonly StateSlotId _dependency;

            public ReentrantRebuilder(StateSlotId dependency)
            {
                _dependency = dependency;
            }

            public ContextFrameArena Arena { get; set; }
            public ulong Token { get; set; }
            public bool PrepareRejected { get; private set; }
            public bool FinalizeRejected { get; private set; }
            public bool CancelRejected { get; private set; }
            public bool CommitRejected { get; private set; }

            public bool TryRebuild(in DerivedStateReadContext context, out int value)
            {
                if (Arena != null)
                {
                    PrepareRejected = !Arena.TryPrepare(
                        CreateTickFrame(2UL, 2UL, 2UL),
                        out _,
                        out ContextCommitStatus prepareStatus) &&
                        prepareStatus == ContextCommitStatus.PreparationAlreadyActive;
                    FinalizeRejected = !Arena.TryFinalize(
                        Token,
                        out _,
                        out ContextCommitStatus finalizeStatus) &&
                        finalizeStatus == ContextCommitStatus.InvalidPreparation;
                    CancelRejected =
                        Arena.CancelPrepared(Token) == ContextCommitStatus.InvalidPreparation &&
                        Arena.CancelFinalized(Token) == ContextCommitStatus.InvalidPreparation;
                    CommitRejected =
                        Arena.Commit(Token).Status == ContextCommitStatus.InvalidPreparation;
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

        private sealed class DisposeArenaRebuilder : IDerivedStateRebuilder<int>
        {
            private readonly StateSlotId _dependency;

            public DisposeArenaRebuilder(StateSlotId dependency)
            {
                _dependency = dependency;
            }

            public ContextFrameArena Arena { get; set; }
            public bool ShouldDispose { get; set; }
            public bool AuthorityStayedAliveInsideCallback { get; private set; }
            public bool DisposeWasDeferredInsideCallback { get; private set; }

            public bool TryRebuild(in DerivedStateReadContext context, out int value)
            {
                if (!context.TryRead(_dependency, out int source))
                {
                    value = default;
                    return false;
                }

                if (ShouldDispose)
                {
                    Arena.Dispose();
                    AuthorityStayedAliveInsideCallback = Arena.HasCurrent;
                    DisposeWasDeferredInsideCallback = !Arena.IsDisposed;
                }

                value = source * 2;
                return true;
            }
        }

        private sealed class Int32Codec : IContextValueCodec<int>
        {
            public Int32Codec(CodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CodecDescriptor Descriptor { get; }
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

        private sealed class ThrowOnDecodeInt32Codec : IContextValueCodec<int>
        {
            public ThrowOnDecodeInt32Codec(CodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CodecDescriptor Descriptor { get; }
            public int MaxEncodedSize => 4;
            public ContextFrameArena Arena { get; set; }
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
                        out ContextCommitStatus prepareStatus) &&
                        prepareStatus == ContextCommitStatus.PreparationAlreadyActive;
                    FinalizeRejected = !Arena.TryFinalize(
                        token,
                        out _,
                        out ContextCommitStatus finalizeStatus) &&
                        finalizeStatus == ContextCommitStatus.InvalidPreparation;
                    CancelRejected =
                        Arena.CancelPrepared(token) == ContextCommitStatus.InvalidPreparation &&
                        Arena.CancelFinalized(token) == ContextCommitStatus.InvalidPreparation;
                    CommitRejected =
                        Arena.Commit(token).Status == ContextCommitStatus.InvalidPreparation;
                }

                throw new InvalidOperationException("Test codec decode failure.");
            }
        }

        private sealed class DisposeOnDecodeInt32Codec : IContextValueCodec<int>
        {
            public DisposeOnDecodeInt32Codec(CodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CodecDescriptor Descriptor { get; }
            public int MaxEncodedSize => 4;
            public ContextFrameArena Arena { get; set; }
            public bool DisposeWasDeferredInsideCallback { get; private set; }

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
                Arena.Dispose();
                DisposeWasDeferredInsideCallback = !Arena.IsDisposed;
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

    }
}

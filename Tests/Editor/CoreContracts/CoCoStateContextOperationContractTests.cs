using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.Tests
{
    public sealed class CoCoStateContextOperationContractTests
    {
        [Test]
        public void StateConfigAndMemoryAreCallbackFreeClassRoles()
        {
            AssertCallbackFreeRole(typeof(CoCoStateLogic));
            AssertCallbackFreeRole(typeof(CoCoStateConfig));
            AssertCallbackFreeRole(typeof(CoCoActivationMemory));
        }

        [Test]
        public void ContextFrameProvidesStronglyTypedReadOnlySectionAccess()
        {
            var section = new TestSection(42);
            ICoCoContextFrame frame = new TestContextFrame(new CoCoContextRevision(0UL), section);

            Assert.AreEqual(0UL, frame.Revision.Value);
            CoCoContextRequirement requirement = CoCoContextRequirement.For<ITestSection>();
            ITestSection resolved = frame.GetSection<ITestSection>(requirement);
            Assert.AreSame(section, resolved);
            Assert.AreEqual(42, resolved.Value);

            PropertyInfo revisionProperty = typeof(ICoCoContextFrame)
                .GetProperty(nameof(ICoCoContextFrame.Revision));
            Assert.IsNotNull(revisionProperty);
            Assert.IsTrue(revisionProperty.CanRead);
            Assert.IsFalse(revisionProperty.CanWrite);

            Assert.IsTrue(requirement.IsValid);
            Assert.AreEqual(typeof(ITestSection), requirement.SectionType);
            Assert.IsTrue(requirement.Matches<ITestSection>());
            Assert.IsFalse(requirement.Matches<TestSection>());
        }

        [Test]
        public void ContextRequirementRejectsConcreteAndRootSectionTypes()
        {
            CoCoContextRequirement concreteRequirement = CoCoContextRequirement.For<TestSection>();
            CoCoContextRequirement rootRequirement = CoCoContextRequirement.For<ICoCoContextSection>();

            Assert.IsFalse(concreteRequirement.IsValid);
            Assert.IsNull(concreteRequirement.SectionType);
            Assert.IsFalse(rootRequirement.IsValid);
            Assert.IsNull(rootRequirement.SectionType);
        }

        [Test]
        public void ContextRequirementRejectsMutableSectionInterfaces()
        {
            CoCoContextRequirement writableRequirement =
                CoCoContextRequirement.For<IWritableTestSection>();
            CoCoContextRequirement mutatingRequirement =
                CoCoContextRequirement.For<IMutatingTestSection>();
            CoCoContextRequirement referenceRequirement =
                CoCoContextRequirement.For<IReferenceTestSection>();
            CoCoContextRequirement callbackRequirement =
                CoCoContextRequirement.For<ICallbackTestSection>();
            CoCoContextRequirement refReturnRequirement =
                CoCoContextRequirement.For<IRefReturnTestSection>();
            CoCoContextRequirement nestedReferenceRequirement =
                CoCoContextRequirement.For<INestedReferenceTestSection>();
            CoCoContextRequirement inheritedWritableRequirement =
                CoCoContextRequirement.For<IInheritedWritableTestSection>();
            CoCoContextRequirement arrayRequirement =
                CoCoContextRequirement.For<IArrayTestSection>();
            CoCoContextRequirement listRequirement =
                CoCoContextRequirement.For<IListTestSection>();
            CoCoContextRequirement eventRequirement =
                CoCoContextRequirement.For<IEventTestSection>();

            Assert.IsFalse(writableRequirement.IsValid);
            Assert.IsFalse(mutatingRequirement.IsValid);
            Assert.IsFalse(referenceRequirement.IsValid);
            Assert.IsFalse(callbackRequirement.IsValid);
            Assert.IsFalse(refReturnRequirement.IsValid);
            Assert.IsFalse(nestedReferenceRequirement.IsValid);
            Assert.IsFalse(inheritedWritableRequirement.IsValid);
            Assert.IsFalse(arrayRequirement.IsValid);
            Assert.IsFalse(listRequirement.IsValid);
            Assert.IsFalse(eventRequirement.IsValid);
        }

        [Test]
        public void ContextRequirementRejectsParameterizedStaticAndImplementedMembers()
        {
            Assert.IsFalse(CoCoContextRequirement.For<IIndexerTestSection>().IsValid);
            Assert.IsFalse(CoCoContextRequirement.For<IStaticPropertyTestSection>().IsValid);
            Assert.IsFalse(CoCoContextRequirement.For<IDefaultPropertyTestSection>().IsValid);
            Assert.IsFalse(CoCoContextRequirement.For<IStaticFieldTestSection>().IsValid);
        }

        [Test]
        public void ContextRequirementRejectsHandleAndRefLikeFactTypes()
        {
            Assert.IsFalse(CoCoContextRequirement.For<IIntPtrTestSection>().IsValid);
            Assert.IsFalse(CoCoContextRequirement.For<IUIntPtrTestSection>().IsValid);
            Assert.IsFalse(CoCoContextRequirement.For<IRefLikeTestSection>().IsValid);
        }

        [Test]
        public void ContextRequirementAcceptsReferenceFreeValueFactsReturnedByValue()
        {
            Assert.IsTrue(CoCoContextRequirement.For<IInheritedReadOnlyTestSection>().IsValid);
            Assert.IsTrue(CoCoContextRequirement.For<INullableTestSection>().IsValid);
            Assert.IsTrue(CoCoContextRequirement.For<ICompositeValueTestSection>().IsValid);
            Assert.IsTrue(CoCoContextRequirement.For<IGenericValueTestSection>().IsValid);
        }

        [Test]
        public void ContextFrameExposesNoMutableOrConsumableSurface()
        {
            MemberInfo[] members = typeof(ICoCoContextFrame).GetMembers();

            Assert.IsFalse(Array.Exists(members, member =>
                member.Name.IndexOf("Writer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                member.Name.IndexOf("Source", StringComparison.OrdinalIgnoreCase) >= 0 ||
                member.Name.IndexOf("Consume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                member.Name.IndexOf("Mutable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                member.Name.IndexOf("TryGet", StringComparison.OrdinalIgnoreCase) >= 0));

            MethodInfo getSection = typeof(ICoCoContextFrame).GetMethod("GetSection");
            Assert.IsNotNull(getSection);
            Assert.IsTrue(getSection.IsGenericMethodDefinition);
            Assert.AreNotEqual(typeof(bool), getSection.ReturnType);
            ParameterInfo[] parameters = getSection.GetParameters();
            Assert.AreEqual(1, parameters.Length);
            Assert.AreEqual(typeof(CoCoContextRequirement), parameters[0].ParameterType);
        }

        [Test]
        public void StateSubmitsCommandsThroughSinkAndNoOpIsExplicit()
        {
            var sink = new RecordingCommandSink();
            var command = new TestCommand(42);
            CoCoOperationRequirement requirement = CoCoOperationRequirement.For<ITestPort>();

            sink.Submit(requirement, command);
            Assert.AreEqual(42, command.Value);
            Assert.AreEqual(99, sink.LastCommand.Value);
            Assert.AreEqual(requirement, sink.LastRequirement);

            Assert.IsTrue(requirement.IsValid);
            Assert.AreEqual(typeof(ITestPort), requirement.PortType);

            var noOp = new TestNoOpOperation();
            Assert.IsInstanceOf<ICoCoNoOpOperation>(noOp);
            Assert.IsInstanceOf<ITestPort>(noOp);
            Assert.IsTrue(requirement.PortType.IsInstanceOfType(noOp));
            Assert.IsFalse(typeof(ICoCoOperationCommand).IsAssignableFrom(noOp.GetType()));
        }

        [Test]
        public void OperationRequirementRejectsConcreteAndRootPortTypes()
        {
            CoCoOperationRequirement concreteRequirement =
                CoCoOperationRequirement.For<TestPortImplementation>();
            CoCoOperationRequirement rootRequirement =
                CoCoOperationRequirement.For<ICoCoOperationPort>();

            Assert.IsFalse(concreteRequirement.IsValid);
            Assert.IsNull(concreteRequirement.PortType);
            Assert.IsFalse(rootRequirement.IsValid);
            Assert.IsNull(rootRequirement.PortType);
        }

        [Test]
        public void OperationSubmissionHasNoSynchronousResultOrOptionalRequirement()
        {
            MethodInfo submit = typeof(ICoCoOperationCommandSink).GetMethod("Submit");
            Assert.IsNotNull(submit);
            Assert.AreEqual(typeof(void), submit.ReturnType);
            Assert.IsTrue(submit.IsGenericMethodDefinition);

            ParameterInfo[] parameters = submit.GetParameters();
            Assert.AreEqual(2, parameters.Length);
            Assert.AreEqual(typeof(CoCoOperationRequirement), parameters[0].ParameterType);

            Type commandType = submit.GetGenericArguments()[0];
            GenericParameterAttributes attributes = commandType.GenericParameterAttributes;
            Assert.AreNotEqual(
                0,
                attributes & GenericParameterAttributes.NotNullableValueTypeConstraint);
            Assert.Contains(typeof(ICoCoOperationCommand), commandType.GetGenericParameterConstraints());
            Assert.IsTrue(Array.Exists(
                commandType.GetCustomAttributes(false),
                attribute => attribute.GetType().FullName ==
                             "System.Runtime.CompilerServices.IsUnmanagedAttribute"));

            Assert.IsNull(typeof(CoCoOperationRequirement).GetProperty("Optional"));
            Assert.IsFalse(Array.Exists(
                typeof(CoCoOperationRequirement).GetMembers(),
                member => member.Name.IndexOf("Optional", StringComparison.OrdinalIgnoreCase) >= 0));
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

        private interface ITestSection : ICoCoContextSection
        {
            int Value { get; }
        }

        private interface IWritableTestSection : ICoCoContextSection
        {
            int Value { get; set; }
        }

        private interface IInheritedReadOnlyTestSection : ITestSection
        {
        }

        private interface IInheritedWritableTestSection : IWritableTestSection
        {
        }

        private interface IMutatingTestSection : ICoCoContextSection
        {
            void Mutate();
        }

        private interface IReferenceTestSection : ICoCoContextSection
        {
            object Value { get; }
        }

        private interface ICallbackTestSection : ICoCoContextSection
        {
            Action Callback { get; }
        }

        private interface IRefReturnTestSection : ICoCoContextSection
        {
            ref int Value { get; }
        }

        private interface INestedReferenceTestSection : ICoCoContextSection
        {
            NestedReferenceFact Value { get; }
        }

        private interface IArrayTestSection : ICoCoContextSection
        {
            int[] Values { get; }
        }

        private interface IListTestSection : ICoCoContextSection
        {
            List<int> Values { get; }
        }

        private interface IEventTestSection : ICoCoContextSection
        {
            event Action Changed;
        }

        private interface IIndexerTestSection : ICoCoContextSection
        {
            int this[int index] { get; }
        }

        private interface IStaticPropertyTestSection : ICoCoContextSection
        {
            static int Value => 1;
        }

        private interface IDefaultPropertyTestSection : ICoCoContextSection
        {
            int Value => 1;
        }

        private interface IStaticFieldTestSection : ICoCoContextSection
        {
            static int Value = 1;
        }

        private interface IIntPtrTestSection : ICoCoContextSection
        {
            IntPtr Value { get; }
        }

        private interface IUIntPtrTestSection : ICoCoContextSection
        {
            UIntPtr Value { get; }
        }

        private interface IRefLikeTestSection : ICoCoContextSection
        {
            PrimitiveRefLikeFact Value { get; }
        }

        private interface INullableTestSection : ICoCoContextSection
        {
            int? Value { get; }
        }

        private interface ICompositeValueTestSection : ICoCoContextSection
        {
            CompositeValueFact Value { get; }
        }

        private interface IGenericValueTestSection : ICoCoContextSection
        {
            GenericValueFact<int> Value { get; }
        }

        private struct NestedReferenceFact
        {
            public object Value;
        }

        private ref struct PrimitiveRefLikeFact
        {
            public int Value;
        }

        private struct CompositeValueFact
        {
            public int Count;
            public bool IsReady;
        }

        private struct GenericValueFact<T>
            where T : struct
        {
            public T Value;
        }

        private sealed class TestSection : ITestSection
        {
            public TestSection(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private sealed class TestContextFrame : ICoCoContextFrame
        {
            private readonly ICoCoContextSection _section;

            public TestContextFrame(CoCoContextRevision revision, ICoCoContextSection section)
            {
                Revision = revision;
                _section = section;
            }

            public CoCoContextRevision Revision { get; }

            public TSection GetSection<TSection>(CoCoContextRequirement requirement)
                where TSection : class, ICoCoContextSection
            {
                if (!requirement.Matches<TSection>())
                {
                    throw new InvalidOperationException("The Context requirement does not match the requested Section interface.");
                }

                return (TSection)_section;
            }
        }

        private interface ITestPort : ICoCoOperationPort
        {
        }

        private sealed class TestPortImplementation : ITestPort
        {
        }

        private struct TestCommand : ICoCoOperationCommand
        {
            public TestCommand(int value)
            {
                Value = value;
            }

            public int Value;
        }

        private sealed class TestNoOpOperation : ITestPort, ICoCoNoOpOperation
        {
        }

        private sealed class RecordingCommandSink : ICoCoOperationCommandSink
        {
            public TestCommand LastCommand { get; private set; }
            public CoCoOperationRequirement LastRequirement { get; private set; }

            public void Submit<TCommand>(CoCoOperationRequirement requirement, TCommand command)
                where TCommand : unmanaged, ICoCoOperationCommand
            {
                if (!requirement.IsValid)
                {
                    throw new InvalidOperationException("The Operation requirement must be valid.");
                }

                object commandCopy = command;
                if (commandCopy is TestCommand testCommand)
                {
                    testCommand.Value = 99;
                    LastCommand = testCommand;
                }

                LastRequirement = requirement;
            }
        }
    }
}

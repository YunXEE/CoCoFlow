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
            CoCoContextSectionRequirement requirement =
                CoCoContextSectionRequirement.For<ITestSection>();
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
        public void ContextSectionRequirementRejectsConcreteAndRootSectionTypes()
        {
            CoCoContextSectionRequirement concreteRequirement =
                CoCoContextSectionRequirement.For<TestSection>();
            CoCoContextSectionRequirement rootRequirement =
                CoCoContextSectionRequirement.For<ICoCoContextSection>();

            Assert.IsFalse(concreteRequirement.IsValid);
            Assert.IsNull(concreteRequirement.SectionType);
            Assert.IsFalse(rootRequirement.IsValid);
            Assert.IsNull(rootRequirement.SectionType);
        }

        [Test]
        public void ContextSectionRequirementRejectsMutableSectionInterfaces()
        {
            CoCoContextSectionRequirement writableRequirement =
                CoCoContextSectionRequirement.For<IWritableTestSection>();
            CoCoContextSectionRequirement mutatingRequirement =
                CoCoContextSectionRequirement.For<IMutatingTestSection>();
            CoCoContextSectionRequirement referenceRequirement =
                CoCoContextSectionRequirement.For<IReferenceTestSection>();
            CoCoContextSectionRequirement callbackRequirement =
                CoCoContextSectionRequirement.For<ICallbackTestSection>();
            CoCoContextSectionRequirement refReturnRequirement =
                CoCoContextSectionRequirement.For<IRefReturnTestSection>();
            CoCoContextSectionRequirement nestedReferenceRequirement =
                CoCoContextSectionRequirement.For<INestedReferenceTestSection>();
            CoCoContextSectionRequirement inheritedWritableRequirement =
                CoCoContextSectionRequirement.For<IInheritedWritableTestSection>();
            CoCoContextSectionRequirement arrayRequirement =
                CoCoContextSectionRequirement.For<IArrayTestSection>();
            CoCoContextSectionRequirement listRequirement =
                CoCoContextSectionRequirement.For<IListTestSection>();
            CoCoContextSectionRequirement eventRequirement =
                CoCoContextSectionRequirement.For<IEventTestSection>();

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
        public void ContextSectionRequirementRejectsParameterizedStaticAndImplementedMembers()
        {
            Assert.IsFalse(CoCoContextSectionRequirement.For<IIndexerTestSection>().IsValid);
            Assert.IsFalse(CoCoContextSectionRequirement.For<IStaticPropertyTestSection>().IsValid);
            Assert.IsFalse(CoCoContextSectionRequirement.For<IDefaultPropertyTestSection>().IsValid);
            Assert.IsFalse(CoCoContextSectionRequirement.For<IStaticFieldTestSection>().IsValid);
        }

        [Test]
        public void ContextSectionRequirementRejectsHandleAndRefLikeFactTypes()
        {
            Assert.IsFalse(CoCoContextSectionRequirement.For<IIntPtrTestSection>().IsValid);
            Assert.IsFalse(CoCoContextSectionRequirement.For<IUIntPtrTestSection>().IsValid);
            Assert.IsFalse(CoCoContextSectionRequirement.For<IRefLikeTestSection>().IsValid);
        }

        [Test]
        public void ContextSectionRequirementAcceptsReferenceFreeValueFactsReturnedByValue()
        {
            Assert.IsTrue(CoCoContextSectionRequirement.For<IInheritedReadOnlyTestSection>().IsValid);
            Assert.IsTrue(CoCoContextSectionRequirement.For<INullableTestSection>().IsValid);
            Assert.IsTrue(CoCoContextSectionRequirement.For<ICompositeValueTestSection>().IsValid);
            Assert.IsTrue(CoCoContextSectionRequirement.For<IGenericValueTestSection>().IsValid);
        }

        [Test]
        public void ContextSectionRequirementAllowsTopLevelStringButRejectsNestedStrings()
        {
            Assert.IsTrue(CoCoContextSectionRequirement.For<IStringTestSection>().IsValid);
            Assert.IsFalse(CoCoContextSectionRequirement.For<INestedStringTestSection>().IsValid);
            Assert.IsFalse(CoCoContextSectionRequirement.For<IPrivateNestedStringTestSection>().IsValid);
            Assert.IsFalse(CoCoContextSectionRequirement.For<IDeeplyNestedStringTestSection>().IsValid);
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
            Assert.AreEqual(typeof(CoCoContextSectionRequirement), parameters[0].ParameterType);
        }

        [Test]
        public void StateSubmitsCommandsThroughSinkAndNoOpIsExplicit()
        {
            var sink = new RecordingCommandSink();
            var command = new TestCommand(42);
            CoCoOperationPortRequirement requirement =
                CoCoOperationPortRequirement.For<ITestPort>();

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
        public void OperationPortRequirementRejectsConcreteAndRootPortTypes()
        {
            CoCoOperationPortRequirement concreteRequirement =
                CoCoOperationPortRequirement.For<TestPortImplementation>();
            CoCoOperationPortRequirement rootRequirement =
                CoCoOperationPortRequirement.For<ICoCoOperationPort>();

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
            Assert.AreEqual(typeof(CoCoOperationPortRequirement), parameters[0].ParameterType);

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

            Assert.IsNull(typeof(CoCoOperationPortRequirement).GetProperty("Optional"));
            Assert.IsFalse(Array.Exists(
                typeof(CoCoOperationPortRequirement).GetMembers(),
                member => member.Name.IndexOf("Optional", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void RequirementRenamesExposeNoLegacyAliases()
        {
            Assembly contractsAssembly = typeof(ICoCoContextFrame).Assembly;

            Assert.IsNull(contractsAssembly.GetType("CoCoFlow.Runtime.Core.CoCoContextRequirement"));
            Assert.IsNull(contractsAssembly.GetType("CoCoFlow.Runtime.Core.CoCoOperationRequirement"));
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

        private interface IStringTestSection : ICoCoContextSection
        {
            string Value { get; }
        }

        private interface INestedStringTestSection : ICoCoContextSection
        {
            NestedStringFact Value { get; }
        }

        private interface IPrivateNestedStringTestSection : ICoCoContextSection
        {
            PrivateNestedStringFact Value { get; }
        }

        private interface IDeeplyNestedStringTestSection : ICoCoContextSection
        {
            DeeplyNestedStringFact Value { get; }
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

        private struct NestedStringFact
        {
            public string Value;
        }

        private struct PrivateNestedStringFact
        {
            private readonly string _value;

            public PrivateNestedStringFact(string value)
            {
                _value = value;
            }

            public string Value => _value;
        }

        private struct DeeplyNestedStringFact
        {
            public NestedStringFact Value;
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

            public TSection GetSection<TSection>(CoCoContextSectionRequirement requirement)
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
            public CoCoOperationPortRequirement LastRequirement { get; private set; }

            public void Submit<TCommand>(CoCoOperationPortRequirement requirement, TCommand command)
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

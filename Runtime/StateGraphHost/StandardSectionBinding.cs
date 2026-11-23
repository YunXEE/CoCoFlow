using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.StateGraphHost
{
    /// <summary>
    /// One Operation Section consumed by an Operator.
    /// </summary>
    internal readonly struct StandardOperationRegistration
    {
        public StandardOperationRegistration(
            Type sectionType,
            OperationSectionId sectionId)
        {
            SectionType = sectionType ??
                          throw new ArgumentNullException(nameof(sectionType));
            SectionId = sectionId;
        }

        public Type SectionType { get; }
        public OperationSectionId SectionId { get; }
        public bool IsValid => SectionType != null &&
                               typeof(IOperationSection).IsAssignableFrom(SectionType) &&
                               SectionId.IsValid;
    }

    /// <summary>Declares the Sections, owned Context slots and event lanes of an Operator.</summary>
    public sealed class StandardOperatorBuilder
    {
        private readonly OperatorDescriptorBuilder _descriptor = new OperatorDescriptorBuilder();
        private readonly List<Operation> _operations = new List<Operation>();
        private readonly List<Outcome> _outcomes = new List<Outcome>();

        public OperationSectionRequirement Require<TSection>()
            where TSection : class, IOperationSection =>
            Require<TSection>(SectionId(typeof(TSection)), 1);

        public OperationSectionRequirement Require<TSection>(OperationSectionId id,
            ulong semanticFingerprint) where TSection : class, IOperationSection
        {
            var mode = GetMode(typeof(TSection));
            return Require<TSection, GeneratedOperationViewFactory<TSection>>(id, semanticFingerprint,
                new GeneratedOperationViewFactory<TSection>(), mode);
        }

        internal OperationSectionRequirement Require<TSection, TFactory>(OperationSectionId id,
            ulong semanticFingerprint, TFactory factory)
            where TSection : class, IOperationSection
            where TFactory : IOperationSectionViewFactory<TSection> =>
            Require<TSection, TFactory>(id, semanticFingerprint, factory, GetMode(typeof(TSection)));

        private OperationSectionRequirement Require<TSection, TFactory>(OperationSectionId id,
            ulong semanticFingerprint, TFactory factory, OperationSectionMode mode)
            where TSection : class, IOperationSection
            where TFactory : IOperationSectionViewFactory<TSection>
        {
            var section = typeof(TSection);
            Ensure(_descriptor.TryRequire<TSection>(id, mode, out var requirement, out var diagnostic), diagnostic);
            _operations.Add(new Operation(new StandardOperationRegistration(section, id),
                (GraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic error) =>
                    catalog.TryRegisterOperationSection(id, mode, new OperationSectionViewFactoryToken<TSection, TFactory>(semanticFingerprint), out error),
                (StateGraphHostBindingBuilder host, out CoCoDiagnostic error) =>
                    host.TryRegisterOperation<TSection>(id, mode, factory, semanticFingerprint, out _, out error)));
            return requirement;
        }

        public void OwnOutcome<TValue>(StateBlockId block, StateSlotId slot,
            ContextProjection projection, ContextRestorePolicy restorePolicy,
            TValue initialValue, ulong semanticFingerprint) where TValue : unmanaged
        {
            Ensure(_descriptor.TryOwnOutcome<TValue>(slot, out var diagnostic), diagnostic);
            _outcomes.Add(new Outcome(block, slot,
                (GraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic error) =>
                    catalog.TryRegisterStateSlot(block, slot, projection, restorePolicy,
                        initialValue, semanticFingerprint, default, null, out error),
                (StateGraphHostBindingBuilder host, out CoCoDiagnostic error) =>
                    host.TryBindContextSlot(block, slot, initialValue, semanticFingerprint, out error)));
        }

        public EventOutboxRequirement Emit<TEvent>(EventTypeId id, EventDomainId domain, int capacity)
            where TEvent : unmanaged
        {
            Ensure(_descriptor.TryEmit<TEvent>(id, domain, capacity, out var requirement, out var diagnostic), diagnostic);
            return requirement;
        }

        public StandardOperatorDefinition Freeze<TOperator>(OperatorId id) where TOperator : class, IOperator
        {
            Ensure(_descriptor.TryFreeze<TOperator>(id, out var descriptor, out var diagnostic), diagnostic);
            return new StandardOperatorDefinition(descriptor, _operations.ToArray(), _outcomes.ToArray());
        }

        private static OperationSectionMode GetMode(Type section)
        {
            var declaration = (OperationSectionAttribute)Attribute.GetCustomAttribute(section,
                typeof(OperationSectionAttribute));
            if (declaration == null || !OperationSectionContract.IsDefinedMode(declaration.Mode))
                throw new InvalidOperationException($"Operation Section {section.FullName} must declare Continuous or Discrete mode.");
            return declaration.Mode;
        }

        private static OperationSectionId SectionId(Type section)
        {
            OperationSectionId.TryCreate(0x434F434F4F505345UL, StandardTypeIdentity.Hash(section), out var id);
            return id;
        }

        private static void Ensure(bool succeeded, CoCoDiagnostic diagnostic)
        {
            if (!succeeded) throw new InvalidOperationException(diagnostic.Message);
        }

        internal delegate bool RegisterCatalog(GraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic diagnostic);
        internal delegate bool BindHost(StateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic);

        internal readonly struct Operation
        {
            internal Operation(StandardOperationRegistration registration, RegisterCatalog register, BindHost bind)
            {
                Registration = registration;
                Register = register;
                Bind = bind;
            }

            internal StandardOperationRegistration Registration { get; }
            internal RegisterCatalog Register { get; }
            internal BindHost Bind { get; }
        }

        internal readonly struct Outcome
        {
            internal Outcome(StateBlockId block, StateSlotId slot, RegisterCatalog register, BindHost bind)
            {
                Block = block;
                Slot = slot;
                Register = register;
                Bind = bind;
            }

            internal StateBlockId Block { get; }
            internal StateSlotId Slot { get; }
            internal RegisterCatalog Register { get; }
            internal BindHost Bind { get; }
        }
    }

    public sealed class StandardOperatorDefinition
    {
        private readonly StandardOperatorBuilder.Operation[] _operations;
        private readonly StandardOperatorBuilder.Outcome[] _outcomes;
        private readonly StandardOperationRegistration[] _operationsBySection;
        private readonly StateBlockId[] _blocks;

        internal StandardOperatorDefinition(OperatorDescriptor descriptor,
            StandardOperatorBuilder.Operation[] operations, StandardOperatorBuilder.Outcome[] outcomes)
        {
            Descriptor = descriptor;
            _operations = operations;
            _outcomes = outcomes;
            _operationsBySection = Array.ConvertAll(operations, operation => operation.Registration);
            var blocks = new HashSet<StateBlockId>();
            foreach (var outcome in outcomes) blocks.Add(outcome.Block);
            _blocks = new StateBlockId[blocks.Count];
            blocks.CopyTo(_blocks);
        }

        public OperatorDescriptor Descriptor { get; }
        internal IReadOnlyList<StandardOperationRegistration> Operations => _operationsBySection;
        internal IReadOnlyList<StateBlockId> ContextBlocks => _blocks;

        internal void RegisterCatalog(GraphDescriptorCatalogBuilder catalog)
        {
            foreach (var operation in _operations)
                if (!operation.Register(catalog, out var diagnostic)) throw new InvalidOperationException(diagnostic.Message);
            foreach (var block in _blocks)
                if (!catalog.TryRegisterStateBlock(block, StateBlockOwner.Operator, out var diagnostic))
                    throw new InvalidOperationException(diagnostic.Message);
            foreach (var outcome in _outcomes)
                if (!outcome.Register(catalog, out var diagnostic)) throw new InvalidOperationException(diagnostic.Message);
        }

        internal bool TryBindOperation(OperationSectionId section,
            StateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic)
        {
            foreach (var operation in _operations)
                if (operation.Registration.SectionId == section) return operation.Bind(host, out diagnostic);
            diagnostic = Missing($"Operator does not declare Operation Section {section}.");
            return false;
        }

        internal bool TryBindContextSlot(StateBlockId block, StateSlotId slot,
            StateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic)
        {
            foreach (var outcome in _outcomes)
                if (outcome.Block == block && outcome.Slot == slot) return outcome.Bind(host, out diagnostic);
            diagnostic = Missing($"Operator does not declare Context slot {slot} in block {block}.");
            return false;
        }

        private static CoCoDiagnostic Missing(string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, CoCoDiagnosticCode.MissingDescriptor, message);
    }
}

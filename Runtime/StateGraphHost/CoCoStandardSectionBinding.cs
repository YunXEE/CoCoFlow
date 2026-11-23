using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.StateGraphHost
{
    /// <summary>
    /// One Operation Section consumed by an Operator.
    /// </summary>
    internal readonly struct CoCoStandardOperationRegistration
    {
        public CoCoStandardOperationRegistration(
            Type sectionType,
            CoCoOperationSectionId sectionId)
        {
            SectionType = sectionType ??
                          throw new ArgumentNullException(nameof(sectionType));
            SectionId = sectionId;
        }

        public Type SectionType { get; }
        public CoCoOperationSectionId SectionId { get; }
        public bool IsValid => SectionType != null &&
                               typeof(ICoCoOperationSection).IsAssignableFrom(SectionType) &&
                               SectionId.IsValid;
    }

    /// <summary>Declares the Sections, owned Context slots and event lanes of an Operator.</summary>
    public sealed class CoCoStandardOperatorBuilder
    {
        private readonly CoCoOperatorDescriptorBuilder _descriptor = new CoCoOperatorDescriptorBuilder();
        private readonly List<Operation> _operations = new List<Operation>();
        private readonly List<Outcome> _outcomes = new List<Outcome>();

        public CoCoOperationSectionRequirement Require<TSection>()
            where TSection : class, ICoCoOperationSection =>
            Require<TSection>(SectionId(typeof(TSection)), 1);

        public CoCoOperationSectionRequirement Require<TSection>(CoCoOperationSectionId id,
            ulong semanticFingerprint) where TSection : class, ICoCoOperationSection
        {
            var mode = GetMode(typeof(TSection));
            return Require<TSection, CoCoGeneratedOperationViewFactory<TSection>>(id, semanticFingerprint,
                new CoCoGeneratedOperationViewFactory<TSection>(), mode);
        }

        internal CoCoOperationSectionRequirement Require<TSection, TFactory>(CoCoOperationSectionId id,
            ulong semanticFingerprint, TFactory factory)
            where TSection : class, ICoCoOperationSection
            where TFactory : ICoCoOperationSectionViewFactory<TSection> =>
            Require<TSection, TFactory>(id, semanticFingerprint, factory, GetMode(typeof(TSection)));

        private CoCoOperationSectionRequirement Require<TSection, TFactory>(CoCoOperationSectionId id,
            ulong semanticFingerprint, TFactory factory, CoCoOperationSectionMode mode)
            where TSection : class, ICoCoOperationSection
            where TFactory : ICoCoOperationSectionViewFactory<TSection>
        {
            var section = typeof(TSection);
            Ensure(_descriptor.TryRequire<TSection>(id, mode, out var requirement, out var diagnostic), diagnostic);
            _operations.Add(new Operation(new CoCoStandardOperationRegistration(section, id),
                (CoCoGraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic error) =>
                    catalog.TryRegisterOperationSection(id, mode, new CoCoOperationSectionViewFactoryToken<TSection, TFactory>(semanticFingerprint), out error),
                (CoCoStateGraphHostBindingBuilder host, out CoCoDiagnostic error) =>
                    host.TryRegisterOperation<TSection>(id, mode, factory, semanticFingerprint, out _, out error)));
            return requirement;
        }

        public void OwnOutcome<TValue>(CoCoStateBlockId block, CoCoStateSlotId slot,
            CoCoContextProjection projection, CoCoContextRestorePolicy restorePolicy,
            TValue initialValue, ulong semanticFingerprint) where TValue : unmanaged
        {
            Ensure(_descriptor.TryOwnOutcome<TValue>(slot, out var diagnostic), diagnostic);
            _outcomes.Add(new Outcome(block, slot,
                (CoCoGraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic error) =>
                    catalog.TryRegisterStateSlot(block, slot, projection, restorePolicy,
                        initialValue, semanticFingerprint, default, null, out error),
                (CoCoStateGraphHostBindingBuilder host, out CoCoDiagnostic error) =>
                    host.TryBindContextSlot(block, slot, initialValue, semanticFingerprint, out error)));
        }

        public CoCoEventOutboxRequirement Emit<TEvent>(CoCoEventTypeId id, CoCoEventDomainId domain, int capacity)
            where TEvent : unmanaged
        {
            Ensure(_descriptor.TryEmit<TEvent>(id, domain, capacity, out var requirement, out var diagnostic), diagnostic);
            return requirement;
        }

        public CoCoStandardOperatorDefinition Freeze<TOperator>(CoCoOperatorId id) where TOperator : class, ICoCoOperator
        {
            Ensure(_descriptor.TryFreeze<TOperator>(id, out var descriptor, out var diagnostic), diagnostic);
            return new CoCoStandardOperatorDefinition(descriptor, _operations.ToArray(), _outcomes.ToArray());
        }

        private static CoCoOperationSectionMode GetMode(Type section)
        {
            var declaration = (CoCoOperationSectionAttribute)Attribute.GetCustomAttribute(section,
                typeof(CoCoOperationSectionAttribute));
            if (declaration == null || !CoCoOperationSectionContract.IsDefinedMode(declaration.Mode))
                throw new InvalidOperationException($"Operation Section {section.FullName} must declare Continuous or Discrete mode.");
            return declaration.Mode;
        }

        private static CoCoOperationSectionId SectionId(Type section)
        {
            CoCoOperationSectionId.TryCreate(0x434F434F4F505345UL, CoCoStandardTypeIdentity.Hash(section), out var id);
            return id;
        }

        private static void Ensure(bool succeeded, CoCoDiagnostic diagnostic)
        {
            if (!succeeded) throw new InvalidOperationException(diagnostic.Message);
        }

        internal delegate bool RegisterCatalog(CoCoGraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic diagnostic);
        internal delegate bool BindHost(CoCoStateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic);

        internal readonly struct Operation
        {
            internal Operation(CoCoStandardOperationRegistration registration, RegisterCatalog register, BindHost bind)
            {
                Registration = registration;
                Register = register;
                Bind = bind;
            }

            internal CoCoStandardOperationRegistration Registration { get; }
            internal RegisterCatalog Register { get; }
            internal BindHost Bind { get; }
        }

        internal readonly struct Outcome
        {
            internal Outcome(CoCoStateBlockId block, CoCoStateSlotId slot, RegisterCatalog register, BindHost bind)
            {
                Block = block;
                Slot = slot;
                Register = register;
                Bind = bind;
            }

            internal CoCoStateBlockId Block { get; }
            internal CoCoStateSlotId Slot { get; }
            internal RegisterCatalog Register { get; }
            internal BindHost Bind { get; }
        }
    }

    public sealed class CoCoStandardOperatorDefinition
    {
        private readonly CoCoStandardOperatorBuilder.Operation[] _operations;
        private readonly CoCoStandardOperatorBuilder.Outcome[] _outcomes;
        private readonly CoCoStandardOperationRegistration[] _operationsBySection;
        private readonly CoCoStateBlockId[] _blocks;

        internal CoCoStandardOperatorDefinition(CoCoOperatorDescriptor descriptor,
            CoCoStandardOperatorBuilder.Operation[] operations, CoCoStandardOperatorBuilder.Outcome[] outcomes)
        {
            Descriptor = descriptor;
            _operations = operations;
            _outcomes = outcomes;
            _operationsBySection = Array.ConvertAll(operations, operation => operation.Registration);
            var blocks = new HashSet<CoCoStateBlockId>();
            foreach (var outcome in outcomes) blocks.Add(outcome.Block);
            _blocks = new CoCoStateBlockId[blocks.Count];
            blocks.CopyTo(_blocks);
        }

        public CoCoOperatorDescriptor Descriptor { get; }
        internal IReadOnlyList<CoCoStandardOperationRegistration> Operations => _operationsBySection;
        internal IReadOnlyList<CoCoStateBlockId> ContextBlocks => _blocks;

        internal void RegisterCatalog(CoCoGraphDescriptorCatalogBuilder catalog)
        {
            foreach (var operation in _operations)
                if (!operation.Register(catalog, out var diagnostic)) throw new InvalidOperationException(diagnostic.Message);
            foreach (var block in _blocks)
                if (!catalog.TryRegisterStateBlock(block, CoCoStateBlockOwner.Operator, out var diagnostic))
                    throw new InvalidOperationException(diagnostic.Message);
            foreach (var outcome in _outcomes)
                if (!outcome.Register(catalog, out var diagnostic)) throw new InvalidOperationException(diagnostic.Message);
        }

        internal bool TryBindOperation(CoCoOperationSectionId section,
            CoCoStateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic)
        {
            foreach (var operation in _operations)
                if (operation.Registration.SectionId == section) return operation.Bind(host, out diagnostic);
            diagnostic = Missing($"Operator does not declare Operation Section {section}.");
            return false;
        }

        internal bool TryBindContextSlot(CoCoStateBlockId block, CoCoStateSlotId slot,
            CoCoStateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic)
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

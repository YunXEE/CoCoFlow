using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Runtime.Modules.Locomotion
{
    /// <summary>
    /// Standard-path registration for the locomotion section and its
    /// Operator-owned slot. Strong-typed end to end; mounted on
    /// LocomotionOperator via CoCoOperatorRegistrationAttribute.
    /// </summary>
    public sealed class LocomotionSectionRegistrar :
        ICoCoStandardOperatorRegistrar
    {
        private static readonly CoCoStandardOperationRegistration[]
            OperationRegistrations =
            {
                new CoCoStandardOperationRegistration(
                    typeof(ILocomotionSection),
                    LocoContractIds.SectionId),
            };

        private static readonly CoCoStateBlockId[] ContextBlockRegistrations =
        {
            LocoContractIds.StateBlockId,
        };

        public IReadOnlyList<CoCoStandardOperationRegistration> Operations =>
            OperationRegistrations;

        public IReadOnlyList<CoCoStateBlockId> ContextBlocks =>
            ContextBlockRegistrations;

        public bool RegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder)
        {
            return builder.TryRegisterOperationSection<ILocomotionSection, LocomotionSectionViewFactory>(
                       LocoContractIds.SectionId,
                       CoCoOperationSectionMode.Continuous,
                       new CoCoOperationSectionViewFactoryToken<
                           ILocomotionSection,
                           LocomotionSectionViewFactory>(
                           LocoContractIds.SectionSemanticFingerprint),
                       out _) &&
                   builder.TryRegisterStateBlock(
                       LocoContractIds.StateBlockId,
                       CoCoStateBlockOwner.Operator,
                       out _) &&
                   builder.TryRegisterStateSlot<LocomotionState>(
                       LocoContractIds.StateBlockId,
                       LocoContractIds.StateSlotId,
                       CoCoContextProjection.Temporal |
                       CoCoContextProjection.Durable,
                       CoCoContextRestorePolicy.Stored,
                       default(LocomotionState),
                       1UL,
                       default,
                       null,
                       out _);
        }

        public bool TryBindOperation(
            CoCoOperationSectionId sectionId,
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            if (sectionId != LocoContractIds.SectionId)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingOperationBinding,
                    "Locomotion registrar does not own the requested Operation Section.");
                return false;
            }

            return bindingBuilder.TryRegisterOperation<ILocomotionSection>(
                LocoContractIds.SectionId,
                CoCoOperationSectionMode.Continuous,
                new LocomotionSectionViewFactory(),
                LocoContractIds.SectionSemanticFingerprint,
                out _,
                out diagnostic);
        }

        public bool TryBindContextSlot(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            if (blockId != LocoContractIds.StateBlockId ||
                slotId != LocoContractIds.StateSlotId)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "Locomotion registrar does not own the requested Context slot.");
                return false;
            }

            return bindingBuilder.TryBindContextSlot<LocomotionState>(
                LocoContractIds.StateBlockId,
                LocoContractIds.StateSlotId,
                default,
                1UL,
                out diagnostic);
        }

        private static CoCoDiagnostic Error(
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

using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Animation
{
    /// <summary>
    /// Standard-path registration for the animation parameter and trigger
    /// sections (no operator-owned slots — the Auto operator writes only
    /// engine presentation).
    /// </summary>
    public sealed class AnimSectionRegistrar :
        ICoCoStandardOperatorRegistrar
    {
        private static readonly CoCoStandardOperationRegistration[]
            OperationRegistrations =
            {
                new CoCoStandardOperationRegistration(
                    typeof(IAnimParameterOperationSection),
                    AnimContractIds.ParameterSectionId),
                new CoCoStandardOperationRegistration(
                    typeof(IAnimTriggerOperationSection),
                    AnimContractIds.TriggerSectionId),
            };

        public IReadOnlyList<CoCoStandardOperationRegistration> Operations =>
            OperationRegistrations;

        public IReadOnlyList<CoCoStateBlockId> ContextBlocks =>
            Array.Empty<CoCoStateBlockId>();

        public bool RegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder)
        {
            return builder.TryRegisterOperationSection<IAnimParameterOperationSection, AnimParameterOperationSectionViewFactory>(
                       AnimContractIds.ParameterSectionId,
                       CoCoOperationSectionMode.Continuous,
                       new CoCoOperationSectionViewFactoryToken<
                           IAnimParameterOperationSection,
                           AnimParameterOperationSectionViewFactory>(
                           AnimContractIds.ParameterSectionSemanticFingerprint),
                       out _) &&
                   builder.TryRegisterOperationSection<IAnimTriggerOperationSection, AnimTriggerOperationSectionViewFactory>(
                       AnimContractIds.TriggerSectionId,
                       CoCoOperationSectionMode.Discrete,
                       new CoCoOperationSectionViewFactoryToken<
                           IAnimTriggerOperationSection,
                           AnimTriggerOperationSectionViewFactory>(
                           AnimContractIds.TriggerSectionSemanticFingerprint),
                       out _);
        }

        public bool TryBindOperation(
            CoCoOperationSectionId sectionId,
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            if (sectionId == AnimContractIds.ParameterSectionId)
            {
                return bindingBuilder.TryRegisterOperation<IAnimParameterOperationSection>(
                    AnimContractIds.ParameterSectionId,
                    CoCoOperationSectionMode.Continuous,
                    new AnimParameterOperationSectionViewFactory(),
                    AnimContractIds.ParameterSectionSemanticFingerprint,
                    out _,
                    out diagnostic);
            }

            if (sectionId == AnimContractIds.TriggerSectionId)
            {
                return bindingBuilder.TryRegisterOperation<IAnimTriggerOperationSection>(
                    AnimContractIds.TriggerSectionId,
                    CoCoOperationSectionMode.Discrete,
                    new AnimTriggerOperationSectionViewFactory(),
                    AnimContractIds.TriggerSectionSemanticFingerprint,
                    out _,
                    out diagnostic);
            }

            diagnostic = Error(
                CoCoDiagnosticCode.MissingOperationBinding,
                "Animation registrar does not own the requested Operation Section.");
            return false;
        }

        public bool TryBindContextSlot(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = Error(
                CoCoDiagnosticCode.MissingDescriptor,
                "AnimAutoOperator does not own a Context slot.");
            return false;
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

using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Animation
{
    /// <summary>
    /// Standard-path registration for the animation parameter and trigger
    /// sections plus the operator-owned Animator snapshot slot.
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

        private static readonly CoCoStateBlockId[] ContextBlockRegistrations =
        {
            AnimContractIds.SnapshotBlockId,
        };

        public IReadOnlyList<CoCoStateBlockId> ContextBlocks =>
            ContextBlockRegistrations;

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
                       out _) &&
                   builder.TryRegisterStateBlock(
                       AnimContractIds.SnapshotBlockId,
                       CoCoStateBlockOwner.Operator,
                       out _) &&
                   builder.TryRegisterStateSlot<AnimSnapshotState>(
                       AnimContractIds.SnapshotBlockId,
                       AnimContractIds.SnapshotSlotId,
                       CoCoContextProjection.Temporal |
                       CoCoContextProjection.Durable,
                       CoCoContextRestorePolicy.Stored,
                       default(AnimSnapshotState),
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
            if (blockId != AnimContractIds.SnapshotBlockId ||
                slotId != AnimContractIds.SnapshotSlotId)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "Animation registrar does not own the requested Context slot.");
                return false;
            }

            return bindingBuilder.TryBindContextSlot<AnimSnapshotState>(
                AnimContractIds.SnapshotBlockId,
                AnimContractIds.SnapshotSlotId,
                default(AnimSnapshotState),
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

using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Camera.Contracts;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CoCoFlow/Camera/Camera Operator")]
    [CoCoOperatorRegistration(typeof(CameraOperationRegistrar))]
    public sealed class CameraOperator : MonoBehaviour, ICoCoPersistenceRestoreOperator
    {
        [SerializeField] private CameraDirector director;
        private static readonly CoCoOperationSectionRequirement Requirement;
        private static readonly CoCoOperatorDescriptor CameraDescriptor;
        public CoCoOperatorDescriptor Descriptor => CameraDescriptor;

        static CameraOperator()
        {
            var builder = new CoCoOperatorDescriptorBuilder();
            if (!builder.TryRequire<CameraOperation>(CamContractIds.OperationId,
                    CoCoOperationSectionMode.Discrete, out Requirement, out var diagnostic) ||
                !builder.TryOwnOutcome<CamSnapshotState>(CamContractIds.SnapshotSlotId, out diagnostic) ||
                !builder.TryFreeze<CameraOperator>(CamContractIds.OperatorId, out CameraDescriptor, out diagnostic))
                throw new InvalidOperationException(diagnostic.Message);
        }

        public bool TryExecute(in CoCoOperatorExecutionContext context, out CoCoOperatorOutcome outcome)
        {
            if (director == null || !context.IsValid ||
                !context.TryGet(Requirement, out CoCoOperationSectionEntry<CameraOperation> request))
            {
                outcome = CoCoOperatorOutcome.Rejected(Error("Camera execution requires a Director and valid request."));
                return false;
            }
            var operation = request.View;
            if (request.Header.Enabled && !director.ApplyRequest(operation.RigId, operation.Active,
                    operation.Priority, operation.CameraId, operation.SchedulingEnabled, out var requestDiagnostic))
            {
                outcome = CoCoOperatorOutcome.Rejected(requestDiagnostic);
                return false;
            }
            // Capture engine facts even on Ticks that contain no operation request.
            if (!director.GetState(out var snapshot, out var diagnostic))
            {
                outcome = CoCoOperatorOutcome.Rejected(diagnostic);
                return false;
            }
            if (!context.TryWriteOutcome(CamContractIds.SnapshotSlotId, snapshot))
            {
                outcome = CoCoOperatorOutcome.Rejected(Error("Camera snapshot write was rejected."));
                return false;
            }
            outcome = CoCoOperatorOutcome.Success;
            return true;
        }

        public bool TryRestore(in CoCoContextRestoreBindingContext context, out CoCoDiagnostic diagnostic)
        {
            if (director == null || !context.IsValid ||
                !context.Reader.TryRead(CamContractIds.SnapshotSlotId, out CamSnapshotState snapshot))
            {
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.MissingDescriptor, "Camera restore requires a Director and saved snapshot.");
                return false;
            }
            return director.SetState(in snapshot, out diagnostic);
        }

        private static CoCoDiagnostic Error(string message) => CoCoDiagnostic.Error(
            CoCoDiagnosticDomain.Operator, CoCoDiagnosticCode.OperatorExecutionFailed, message);
    }


    public sealed class CameraOperationRegistrar : ICoCoStandardOperatorRegistrar
    {
        public IReadOnlyList<CoCoStandardOperationRegistration> Operations { get; } =
            new[] { new CoCoStandardOperationRegistration(typeof(CameraOperation), CamContractIds.OperationId) };
        public IReadOnlyList<CoCoStateBlockId> ContextBlocks { get; } =
            new[] { CamContractIds.SnapshotBlockId };

        public bool RegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder) =>
            builder.TryRegisterOperationSection(
                CamContractIds.OperationId, CoCoOperationSectionMode.Discrete,
                new CoCoOperationSectionViewFactoryToken<CameraOperation, CameraOperationViewFactory>(
                    CamContractIds.Fingerprint), out _) &&
            builder.TryRegisterStateBlock(CamContractIds.SnapshotBlockId, CoCoStateBlockOwner.Operator, out _) &&
            builder.TryRegisterStateSlot<CamSnapshotState>(CamContractIds.SnapshotBlockId,
                CamContractIds.SnapshotSlotId, CoCoContextProjection.Durable, CoCoContextRestorePolicy.Stored,
                default, CamContractIds.Fingerprint, default, null, out _);

        public bool TryBindOperation(CoCoOperationSectionId sectionId,
            CoCoStateGraphHostBindingBuilder bindingBuilder, out CoCoDiagnostic diagnostic)
        {
            if (sectionId != CamContractIds.OperationId)
                return Reject(out diagnostic);
            return bindingBuilder.TryRegisterOperation(CamContractIds.OperationId,
                CoCoOperationSectionMode.Discrete, new CameraOperationViewFactory(),
                CamContractIds.Fingerprint, out _, out diagnostic);
        }

        public bool TryBindContextSlot(CoCoStateBlockId blockId, CoCoStateSlotId slotId,
            CoCoStateGraphHostBindingBuilder bindingBuilder, out CoCoDiagnostic diagnostic)
        {
            if (blockId != CamContractIds.SnapshotBlockId || slotId != CamContractIds.SnapshotSlotId)
                return Reject(out diagnostic);
            return bindingBuilder.TryBindContextSlot<CamSnapshotState>(blockId, slotId,
                default, CamContractIds.Fingerprint, out diagnostic);
        }

        private static bool Reject(out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry,
                CoCoDiagnosticCode.MissingDescriptor, "Camera registrar does not own the requested binding.");
            return false;
        }
    }
}

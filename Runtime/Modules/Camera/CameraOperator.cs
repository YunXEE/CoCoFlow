using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Camera.Contracts;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CoCoFlow/Camera/Camera Operator")]
    public sealed class CameraOperator : MonoBehaviour, ICoCoPersistenceRestoreOperator
    {
        [SerializeField] private CameraDirector director;
        private static readonly CoCoOperationSectionRequirement Requirement;
        [CoCoOperatorDefinition]
        private static CoCoStandardOperatorDefinition Definition { get; }
        public CoCoOperatorDescriptor Descriptor => Definition.Descriptor;

        static CameraOperator()
        {
            var builder = new CoCoStandardOperatorBuilder();
            Requirement = builder.Require<CameraOperation>(CamContractIds.OperationId, CamContractIds.Fingerprint);
            builder.OwnOutcome(CamContractIds.SnapshotBlockId, CamContractIds.SnapshotSlotId,
                CoCoContextProjection.Durable, CoCoContextRestorePolicy.Stored,
                default(CamSnapshotState), CamContractIds.Fingerprint);
            Definition = builder.Freeze<CameraOperator>(CamContractIds.OperatorId);
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
}

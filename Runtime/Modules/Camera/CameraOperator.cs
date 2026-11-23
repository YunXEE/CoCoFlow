using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using CoCoFlow.Runtime.Camera.Contracts;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CoCoFlow/Camera/Camera Operator")]
    public sealed class CameraOperator : MonoBehaviour, IPersistenceRestoreOperator
    {
        [SerializeField] private CameraDirector director;
        private static readonly OperationSectionRequirement Requirement;
        [OperatorDefinition]
        private static StandardOperatorDefinition Definition { get; }
        public OperatorDescriptor Descriptor => Definition.Descriptor;

        static CameraOperator()
        {
            var builder = new StandardOperatorBuilder();
            Requirement = builder.Require<CameraOperation>(CamContractIds.OperationId, CamContractIds.Fingerprint);
            builder.OwnOutcome(CamContractIds.SnapshotBlockId, CamContractIds.SnapshotSlotId,
                ContextProjection.Durable, ContextRestorePolicy.Stored,
                default(CamSnapshotState), CamContractIds.Fingerprint);
            Definition = builder.Freeze<CameraOperator>(CamContractIds.OperatorId);
        }

        public bool TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome)
        {
            if (director == null || !context.IsValid ||
                !context.TryGet(Requirement, out OperationSectionEntry<CameraOperation> request))
            {
                outcome = OperatorOutcome.Rejected(Error("Camera execution requires a Director and valid request."));
                return false;
            }
            var operation = request.View;
            if (request.Header.Enabled && !director.ApplyRequest(operation.RigId, operation.Active,
                    operation.Priority, operation.CameraId, operation.SchedulingEnabled, out var requestDiagnostic))
            {
                outcome = OperatorOutcome.Rejected(requestDiagnostic);
                return false;
            }
            // Capture engine facts even on Ticks that contain no operation request.
            if (!director.GetState(out var snapshot, out var diagnostic))
            {
                outcome = OperatorOutcome.Rejected(diagnostic);
                return false;
            }
            if (!context.TryWriteOutcome(CamContractIds.SnapshotSlotId, snapshot))
            {
                outcome = OperatorOutcome.Rejected(Error("Camera snapshot write was rejected."));
                return false;
            }
            outcome = OperatorOutcome.Success;
            return true;
        }

        public bool TryRestore(in ContextRestoreBindingContext context, out CoCoDiagnostic diagnostic)
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

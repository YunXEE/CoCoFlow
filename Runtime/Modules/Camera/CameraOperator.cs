using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Camera.Contracts;
using UnityEngine;

// Host 将 State 写入的离散请求交给摄像机执行者。启用的请求应用到 Director 并捕获机位快照。
namespace CoCoFlow.Runtime.Modules.Camera
{
    /// <summary>消费本步已启用的离散机位请求，并将摄像机现场捕获到可恢复的 Context。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CoCoFlow/Camera/Camera Operator")]
    public sealed class CameraOperator : MonoBehaviour, IPersistenceRestoreOperator
    {
        [SerializeField] private CameraDirector director;
        private static readonly OperationRequirement Requirement;
        [OperatorDefinition]
        private static StandardOperatorDefinition Definition { get; }
        public OperatorDescriptor Descriptor => Definition.Descriptor;

        #region Public API

        /// <summary>只在本步离散机位请求已启用时应用到 Director，并把可恢复状态写回候选。无法取得合同或写入时返回 false。</summary>
        public bool TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome)
        {
            if (director == null || !context.IsValid ||
                !context.TryGet(Requirement, out OperationEntry<CameraOperation> request))
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

        /// <summary>从恢复读取器取得摄像机快照并应用到场景机位，读取或应用失败时返回诊断。</summary>
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

        #endregion

        #region Internal Logic

        static CameraOperator()
        {
            var builder = new StandardOperatorBuilder();
            Requirement = builder.Require<CameraOperation>(CamContractIds.OperationId, CamContractIds.Fingerprint);
            builder.OwnOutcome(CamContractIds.SnapshotBlockId, CamContractIds.SnapshotSlotId,
                ContextProjection.Durable, ContextRestorePolicy.Stored,
                default(CamSnapshotState), CamContractIds.Fingerprint);
            Definition = builder.Freeze<CameraOperator>(CamContractIds.OperatorId);
        }

        private static CoCoDiagnostic Error(string message) => CoCoDiagnostic.Error(
            CoCoDiagnosticDomain.Operator, CoCoDiagnosticCode.OperatorExecutionFailed, message);

        #endregion
    }
}

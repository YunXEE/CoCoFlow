using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public class CameraRig : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string rigId;

        [Header("Targets")]
        [SerializeField] private Transform rootTarget;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Transform lookAtTarget;
        [SerializeField] private Transform aimTarget;
        [SerializeField] private Transform spectateTarget;

        public string RigId => rigId;
        public Transform FollowTarget => followTarget != null ? followTarget : RootTarget;
        public Transform LookAtTarget => lookAtTarget != null ? lookAtTarget : FollowTarget;

        private Transform RootTarget => rootTarget != null ? rootTarget : transform;
        private Transform AimTarget => aimTarget != null ? aimTarget : LookAtTarget;
        private Transform SpectateTarget => spectateTarget != null ? spectateTarget : LookAtTarget;

        public Transform ResolveTarget(CameraTargetRole targetRole)
        {
            switch (targetRole)
            {
                case CameraTargetRole.SubjectRoot:
                    return RootTarget;
                case CameraTargetRole.SubjectFollow:
                    return FollowTarget;
                case CameraTargetRole.SubjectLookAt:
                    return LookAtTarget;
                case CameraTargetRole.SubjectAim:
                    return AimTarget;
                case CameraTargetRole.SubjectSpectate:
                    return SpectateTarget;
                default:
                    return null;
            }
        }

        public void SetTargets(
            Transform root,
            Transform follow = null,
            Transform lookAt = null,
            Transform aim = null,
            Transform spectate = null)
        {
            rootTarget = root;
            followTarget = follow;
            lookAtTarget = lookAt;
            aimTarget = aim;
            spectateTarget = spectate;
        }

        private void Reset()
        {
            rootTarget = transform;
            followTarget = transform;
            lookAtTarget = transform;
            aimTarget = transform;
            spectateTarget = transform;
        }
    }
}

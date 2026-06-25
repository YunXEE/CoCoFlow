using System;
using Unity.Cinemachine;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [Serializable]
    public class CameraProfileEntry
    {
        [SerializeField] private string profileId = CameraProfileKeys.Default;
        [SerializeField] private CinemachineCamera camera;
        [SerializeField] private int standbyPriority;
        [SerializeField] private int activePriority = 20;
        [SerializeField] private CameraTargetRole followTarget = CameraTargetRole.SubjectFollow;
        [SerializeField] private CameraTargetRole lookAtTarget = CameraTargetRole.SubjectLookAt;

        public CameraProfileEntry() { }

        public CameraProfileEntry(
            string profileId,
            CinemachineCamera camera,
            int standbyPriority = 0,
            int activePriority = 20,
            CameraTargetRole followTarget = CameraTargetRole.SubjectFollow,
            CameraTargetRole lookAtTarget = CameraTargetRole.SubjectLookAt)
        {
            this.profileId = string.IsNullOrWhiteSpace(profileId)
                ? CameraProfileKeys.Default
                : profileId;
            this.camera = camera;
            this.standbyPriority = standbyPriority;
            this.activePriority = activePriority;
            this.followTarget = followTarget;
            this.lookAtTarget = lookAtTarget;
        }

        public string ProfileId => string.IsNullOrWhiteSpace(profileId)
            ? CameraProfileKeys.Default
            : profileId;
        public CinemachineCamera Camera => camera;
        public int StandbyPriority => standbyPriority;
        public int ActivePriority => activePriority;
        public bool IsUsable => camera != null;

        public void ApplyStandbyPriority()
        {
            if (camera == null) return;
            camera.Priority = standbyPriority;
        }

        public void ApplyActive(
            CameraRig subjectRig,
            Transform focusTarget)
        {
            if (camera == null) return;

            camera.Follow = ResolveTarget(followTarget, subjectRig, focusTarget);
            camera.LookAt = ResolveTarget(lookAtTarget, subjectRig, focusTarget);
            camera.Priority = activePriority;
        }

        private static Transform ResolveTarget(
            CameraTargetRole targetRole,
            CameraRig subjectRig,
            Transform focusTarget)
        {
            if (targetRole == CameraTargetRole.RequestFocus)
            {
                return focusTarget != null
                    ? focusTarget
                    : subjectRig != null ? subjectRig.LookAtTarget : null;
            }

            return subjectRig != null ? subjectRig.ResolveTarget(targetRole) : null;
        }
    }
}

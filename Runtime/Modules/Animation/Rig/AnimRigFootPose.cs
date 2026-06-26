using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation.Rig
{
    public readonly struct AnimRigFootPose
    {
        public AnimRigFootPose(
            bool hasGround,
            Vector3 probeOrigin,
            Vector3 groundPoint,
            Vector3 groundNormal,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float weight,
            float footVelocity)
        {
            HasGround = hasGround;
            ProbeOrigin = probeOrigin;
            GroundPoint = groundPoint;
            GroundNormal = groundNormal;
            TargetPosition = targetPosition;
            TargetRotation = targetRotation;
            Weight = Mathf.Clamp01(weight);
            FootVelocity = Mathf.Max(0f, footVelocity);
        }

        public bool HasGround { get; }
        public Vector3 ProbeOrigin { get; }
        public Vector3 GroundPoint { get; }
        public Vector3 GroundNormal { get; }
        public Vector3 TargetPosition { get; }
        public Quaternion TargetRotation { get; }
        public float Weight { get; }
        public float FootVelocity { get; }
    }
}

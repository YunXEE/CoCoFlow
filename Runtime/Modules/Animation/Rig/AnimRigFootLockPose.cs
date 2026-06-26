using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation.Rig
{
    public readonly struct AnimRigFootLockPose
    {
        public AnimRigFootLockPose(
            Vector3 position,
            Quaternion rotation,
            float weight)
        {
            Position = position;
            Rotation = rotation;
            Weight = Mathf.Clamp01(weight);
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Weight { get; }
    }
}

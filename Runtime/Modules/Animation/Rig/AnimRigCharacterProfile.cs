using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation.Rig
{
    [CreateAssetMenu(
        fileName = "AnimRigCharacterProfile",
        menuName = "CoCoFlow/Animation/Rig/Anim Rig Character Profile")]
    public class AnimRigCharacterProfile : ScriptableObject
    {
        public const string DefaultLeftFootPlantCurve = "CoCoFlow_LeftFootPlant";
        public const string DefaultRightFootPlantCurve = "CoCoFlow_RightFootPlant";

        [Header("Foot Probe")]
        [SerializeField] private LayerMask groundLayer = ~0;
        [SerializeField] private float footProbeUpOffset = 0.35f;
        [SerializeField] private float footProbeDistance = 1.2f;
        [SerializeField] private float footOffset = 0.03f;
        [SerializeField] private bool alignToGroundNormal = true;
        [SerializeField] private float maxSlopeAngle = 55f;

        [Header("Blend")]
        [SerializeField] private float footBlendSpeed = 10f;
        [SerializeField] private float footLockBlendSpeed = 12f;

        [Header("Foot Lock")]
        [SerializeField] private float automaticPlantVelocity = 0.08f;
        [SerializeField] private float automaticReleaseVelocity = 0.35f;
        [SerializeField] private float lockReleaseDistance = 0.35f;
        [SerializeField] private float lockReleaseAngle = 35f;
        [SerializeField] private float groundLossReleaseTime = 0.08f;
        [SerializeField] private float teleportReleaseDistance = 1.5f;

        [Header("Animation Curves")]
        [SerializeField] private string leftFootPlantCurve = DefaultLeftFootPlantCurve;
        [SerializeField] private string rightFootPlantCurve = DefaultRightFootPlantCurve;
        [SerializeField] [Range(0f, 1f)] private float plantEnterThreshold = 0.65f;
        [SerializeField] [Range(0f, 1f)] private float plantExitThreshold = 0.35f;

        public LayerMask GroundLayer => groundLayer;
        public float FootProbeUpOffset => footProbeUpOffset;
        public float FootProbeDistance => footProbeDistance;
        public float FootOffset => footOffset;
        public bool AlignToGroundNormal => alignToGroundNormal;
        public float MaxSlopeAngle => maxSlopeAngle;
        public float FootBlendSpeed => footBlendSpeed;
        public float FootLockBlendSpeed => footLockBlendSpeed;
        public float AutomaticPlantVelocity => automaticPlantVelocity;
        public float AutomaticReleaseVelocity => automaticReleaseVelocity;
        public float LockReleaseDistance => lockReleaseDistance;
        public float LockReleaseAngle => lockReleaseAngle;
        public float GroundLossReleaseTime => groundLossReleaseTime;
        public float TeleportReleaseDistance => teleportReleaseDistance;
        public string LeftFootPlantCurve => leftFootPlantCurve;
        public string RightFootPlantCurve => rightFootPlantCurve;
        public float PlantEnterThreshold => plantEnterThreshold;
        public float PlantExitThreshold => plantExitThreshold;

        public static AnimRigCharacterProfile CreateRuntimeDefault()
        {
            var profile = CreateInstance<AnimRigCharacterProfile>();
            profile.name = "Runtime Anim Rig Character Profile";
            profile.hideFlags = HideFlags.DontSave;
            profile.ClampValues();
            return profile;
        }

        private void OnValidate()
        {
            ClampValues();
        }

        private void ClampValues()
        {
            footProbeUpOffset = Mathf.Max(0f, footProbeUpOffset);
            footProbeDistance = Mathf.Max(0.01f, footProbeDistance);
            footOffset = Mathf.Max(0f, footOffset);
            maxSlopeAngle = Mathf.Clamp(maxSlopeAngle, 0f, 89f);
            footBlendSpeed = Mathf.Max(0f, footBlendSpeed);
            footLockBlendSpeed = Mathf.Max(0f, footLockBlendSpeed);
            automaticPlantVelocity = Mathf.Max(0f, automaticPlantVelocity);
            automaticReleaseVelocity = Mathf.Max(automaticPlantVelocity, automaticReleaseVelocity);
            lockReleaseDistance = Mathf.Max(0.01f, lockReleaseDistance);
            lockReleaseAngle = Mathf.Clamp(lockReleaseAngle, 0f, 180f);
            groundLossReleaseTime = Mathf.Max(0f, groundLossReleaseTime);
            teleportReleaseDistance = Mathf.Max(0.01f, teleportReleaseDistance);
            leftFootPlantCurve = string.IsNullOrWhiteSpace(leftFootPlantCurve)
                ? DefaultLeftFootPlantCurve
                : leftFootPlantCurve;
            rightFootPlantCurve = string.IsNullOrWhiteSpace(rightFootPlantCurve)
                ? DefaultRightFootPlantCurve
                : rightFootPlantCurve;
            plantEnterThreshold = Mathf.Clamp01(plantEnterThreshold);
            plantExitThreshold = Mathf.Min(Mathf.Clamp01(plantExitThreshold), plantEnterThreshold);
        }
    }
}

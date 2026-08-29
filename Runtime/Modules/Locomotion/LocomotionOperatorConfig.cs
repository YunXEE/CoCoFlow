using CoCoFlow.Runtime.Modules.Locomotion;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Locomotion
{
    /// <summary>
    /// Authored locomotion configuration (CL serialized fields, verbatim).
    /// </summary>
    [CreateAssetMenu(
        menuName = "CoCoFlow/Locomotion/Locomotion Operator Config",
        fileName = "LocomotionOperatorConfig")]
    public sealed class LocomotionOperatorConfig : ScriptableObject
    {
        [Header("Gravity & Ground")]
        [Tooltip("Master gravity toggle. Per-state hover uses the Section's UseGravity field.")]
        public bool isUsingGravity = true;

        public float gravity = -9.81f;
        public float baseGravityMultiplier = 2f;

        public LayerMask groundLayer = ~0;
        public float groundCheckRadius = 0.2f;
        public Vector3 groundCheckOffset = new Vector3(0f, 0.1f, 0f);

        [Header("Rotation")]
        public float rotationSmoothTime = 0.1f;

        public LocoConfig ToConfig() => new LocoConfig
        {
            Gravity = gravity,
            BaseGravityMultiplier = baseGravityMultiplier,
            IsUsingGravity = isUsingGravity,
            GroundLayer = groundLayer,
            GroundCheckRadius = groundCheckRadius,
            GroundCheckOffset = groundCheckOffset,
            RotationSmoothTime = rotationSmoothTime,
        };
    }
}

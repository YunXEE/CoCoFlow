using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation.Rig
{
    [DisallowMultipleComponent]
    public class AnimRigFootDriver : MonoBehaviour
    {
        [Header("Feet")]
        [SerializeField] private AnimRigFootBinding leftFoot = new AnimRigFootBinding(AnimRigFootSlot.Left);
        [SerializeField] private AnimRigFootBinding rightFoot = new AnimRigFootBinding(AnimRigFootSlot.Right);

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        private AnimRigFootPose _leftPose;
        private AnimRigFootPose _rightPose;
        private AnimRigFootPose _leftAppliedPose;
        private AnimRigFootPose _rightAppliedPose;
        private Vector3 _leftPreviousProbePosition;
        private Vector3 _rightPreviousProbePosition;
        private bool _hasLeftPreviousProbePosition;
        private bool _hasRightPreviousProbePosition;
        private readonly AnimRigFootLockRuntime _leftLock = new AnimRigFootLockRuntime();
        private readonly AnimRigFootLockRuntime _rightLock = new AnimRigFootLockRuntime();
        private Vector3 _lastRootPosition;
        private bool _hasLastRootPosition;

        public AnimRigFootBinding LeftFoot => leftFoot;
        public AnimRigFootBinding RightFoot => rightFoot;
        public AnimRigFootPose LeftPose => _leftPose;
        public AnimRigFootPose RightPose => _rightPose;
        public AnimRigFootPose LeftAppliedPose => _leftAppliedPose;
        public AnimRigFootPose RightAppliedPose => _rightAppliedPose;
        public AnimRigFootLockState LeftFootLockState => _leftLock.State;
        public AnimRigFootLockState RightFootLockState => _rightLock.State;

        public AnimRigFootPose GetFootPose(AnimRigFootSlot slot)
        {
            return slot == AnimRigFootSlot.Left ? _leftPose : _rightPose;
        }

        public AnimRigFootPose GetAppliedPose(AnimRigFootSlot slot)
        {
            return slot == AnimRigFootSlot.Left ? _leftAppliedPose : _rightAppliedPose;
        }

        public AnimRigFootLockState GetFootLockState(AnimRigFootSlot slot)
        {
            return GetLockRuntime(slot).State;
        }

        public bool PlantFoot(AnimRigFootSlot slot)
        {
            return PlantFoot(GetLockRuntime(slot), GetFootPose(slot));
        }

        public void ReleaseFoot(AnimRigFootSlot slot)
        {
            var runtime = GetLockRuntime(slot);
            if (runtime.State == AnimRigFootLockState.Released) return;

            runtime.State = AnimRigFootLockState.Releasing;
            runtime.GroundLossTime = 0f;
        }

        public void ReleaseAllFeet()
        {
            ReleaseFoot(AnimRigFootSlot.Left);
            ReleaseFoot(AnimRigFootSlot.Right);
        }

        public void SampleFeet(
            AnimRigCharacterProfile profile,
            bool isEnabled,
            float deltaTime)
        {
            if (profile == null) return;

            _leftPose = SampleFoot(
                leftFoot,
                _leftPose,
                ref _leftPreviousProbePosition,
                ref _hasLeftPreviousProbePosition,
                profile,
                isEnabled,
                deltaTime);
            _rightPose = SampleFoot(
                rightFoot,
                _rightPose,
                ref _rightPreviousProbePosition,
                ref _hasRightPreviousProbePosition,
                profile,
                isEnabled,
                deltaTime);
        }

        public void UpdateFootLocks(
            AnimRigFootLockMode mode,
            AnimRigCharacterProfile profile,
            Transform root,
            float deltaTime,
            float leftPlantSignal = 0f,
            float rightPlantSignal = 0f)
        {
            if (profile == null) return;

            float rootDelta = CalculateRootDelta(root);
            if (mode == AnimRigFootLockMode.Off)
            {
                ReleaseAllFeet();
                ClearAnimationDrivenSuppression();
            }

            UpdateFootLock(
                _leftLock,
                mode,
                _leftPose,
                profile,
                rootDelta,
                deltaTime,
                leftPlantSignal);
            UpdateFootLock(
                _rightLock,
                mode,
                _rightPose,
                profile,
                rootDelta,
                deltaTime,
                rightPlantSignal);
        }

        public void ApplyFootTargets(bool isEnabled)
        {
            _leftAppliedPose = ApplyFootTarget(leftFoot, _leftPose, isEnabled);
            _rightAppliedPose = ApplyFootTarget(rightFoot, _rightPose, isEnabled);
        }

        private AnimRigFootPose SampleFoot(
            AnimRigFootBinding binding,
            AnimRigFootPose previousPose,
            ref Vector3 previousProbePosition,
            ref bool hasPreviousProbePosition,
            AnimRigCharacterProfile profile,
            bool isEnabled,
            float deltaTime)
        {
            if (binding == null)
            {
                return previousPose;
            }

            var probeTransform = binding.ProbeTransform;
            var baseRotation = ResolveBaseRotation(binding, previousPose);
            var fallbackPosition = ResolveFallbackPosition(binding, previousPose);
            var probeBasePosition = probeTransform != null ? probeTransform.position : fallbackPosition;
            var probeOrigin = probeBasePosition + Vector3.up * profile.FootProbeUpOffset;
            var footVelocity = CalculateVelocity(
                probeBasePosition,
                ref previousProbePosition,
                ref hasPreviousProbePosition,
                deltaTime);

            var targetWeight = 0f;
            var hasGround = false;
            var groundPoint = previousPose.GroundPoint;
            var groundNormal = previousPose.GroundNormal == Vector3.zero ? Vector3.up : previousPose.GroundNormal;
            var targetPosition = fallbackPosition;
            var targetRotation = baseRotation;

            if (isEnabled &&
                Physics.Raycast(
                    probeOrigin,
                    Vector3.down,
                    out var hit,
                    profile.FootProbeDistance + profile.FootProbeUpOffset,
                    profile.GroundLayer,
                    QueryTriggerInteraction.Ignore))
            {
                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
                if (slopeAngle <= profile.MaxSlopeAngle)
                {
                    hasGround = true;
                    groundPoint = hit.point;
                    groundNormal = hit.normal;
                    targetPosition = hit.point + hit.normal * profile.FootOffset;
                    targetRotation = profile.AlignToGroundNormal
                        ? Quaternion.FromToRotation(baseRotation * Vector3.up, hit.normal) * baseRotation
                        : baseRotation;
                    targetWeight = 1f;
                }
            }

            float weight = MoveWeight(
                previousPose.Weight,
                targetWeight,
                profile.FootBlendSpeed,
                deltaTime);
            return new AnimRigFootPose(
                hasGround,
                probeOrigin,
                groundPoint,
                groundNormal,
                targetPosition,
                targetRotation,
                weight,
                footVelocity);
        }

        private AnimRigFootPose ApplyFootTarget(
            AnimRigFootBinding binding,
            AnimRigFootPose pose,
            bool isEnabled)
        {
            if (binding == null)
            {
                return pose;
            }

            var finalPosition = pose.TargetPosition;
            var finalRotation = pose.TargetRotation;
            var finalWeight = isEnabled ? pose.Weight : 0f;

            if (isEnabled &&
                TryGetLockPose(binding.Slot, out var lockPose))
            {
                finalPosition = Vector3.Lerp(finalPosition, lockPose.Position, lockPose.Weight);
                finalRotation = Quaternion.Slerp(finalRotation, lockPose.Rotation, lockPose.Weight);
                finalWeight = Mathf.Max(finalWeight, lockPose.Weight);
            }

            if (binding.HasTarget && finalWeight > 0f)
            {
                binding.IkTarget.position = finalPosition;
                binding.IkTarget.rotation = finalRotation;
            }

            return new AnimRigFootPose(
                pose.HasGround,
                pose.ProbeOrigin,
                pose.GroundPoint,
                pose.GroundNormal,
                finalPosition,
                finalRotation,
                finalWeight,
                pose.FootVelocity);
        }

        private bool PlantFoot(
            AnimRigFootLockRuntime runtime,
            AnimRigFootPose pose)
        {
            if (!pose.HasGround)
            {
                return false;
            }

            runtime.State = AnimRigFootLockState.Planting;
            runtime.LockPosition = pose.TargetPosition;
            runtime.LockRotation = pose.TargetRotation;
            runtime.GroundLossTime = 0f;
            return true;
        }

        private bool TryGetLockPose(
            AnimRigFootSlot slot,
            out AnimRigFootLockPose pose)
        {
            var runtime = GetLockRuntime(slot);
            if (runtime.State == AnimRigFootLockState.Released || runtime.Weight <= 0f)
            {
                pose = default;
                return false;
            }

            pose = new AnimRigFootLockPose(
                runtime.LockPosition,
                runtime.LockRotation,
                runtime.Weight);
            return true;
        }

        private void UpdateFootLock(
            AnimRigFootLockRuntime runtime,
            AnimRigFootLockMode mode,
            AnimRigFootPose pose,
            AnimRigCharacterProfile profile,
            float rootDelta,
            float deltaTime,
            float plantSignal)
        {
            if (mode == AnimRigFootLockMode.AnimationDriven)
            {
                ApplyAnimationDrivenRequests(runtime, pose, profile, plantSignal);
            }
            else if (mode == AnimRigFootLockMode.Automatic)
            {
                ApplyAutomaticRequests(runtime, pose, profile);
            }

            if (runtime.State == AnimRigFootLockState.Planting ||
                runtime.State == AnimRigFootLockState.Locked)
            {
                if (ShouldRelease(runtime, pose, profile, rootDelta, deltaTime))
                {
                    runtime.State = AnimRigFootLockState.Releasing;
                    if (mode == AnimRigFootLockMode.AnimationDriven)
                    {
                        runtime.SuppressAnimationDrivenPlant = true;
                    }
                }
            }

            AdvanceLockBlend(runtime, profile, deltaTime);
        }

        private void ApplyAnimationDrivenRequests(
            AnimRigFootLockRuntime runtime,
            AnimRigFootPose pose,
            AnimRigCharacterProfile profile,
            float plantSignal)
        {
            float normalizedSignal = Mathf.Clamp01(plantSignal);
            if (normalizedSignal <= profile.PlantExitThreshold)
            {
                runtime.SuppressAnimationDrivenPlant = false;
                if (runtime.State == AnimRigFootLockState.Planting ||
                    runtime.State == AnimRigFootLockState.Locked)
                {
                    runtime.State = AnimRigFootLockState.Releasing;
                }

                return;
            }

            if (runtime.SuppressAnimationDrivenPlant) return;

            if (normalizedSignal >= profile.PlantEnterThreshold &&
                runtime.State == AnimRigFootLockState.Released)
            {
                PlantFoot(runtime, pose);
            }
        }

        private void ApplyAutomaticRequests(
            AnimRigFootLockRuntime runtime,
            AnimRigFootPose pose,
            AnimRigCharacterProfile profile)
        {
            if (runtime.State == AnimRigFootLockState.Released &&
                pose.HasGround &&
                pose.FootVelocity <= profile.AutomaticPlantVelocity)
            {
                PlantFoot(runtime, pose);
                return;
            }

            if ((runtime.State == AnimRigFootLockState.Planting ||
                 runtime.State == AnimRigFootLockState.Locked) &&
                pose.FootVelocity >= profile.AutomaticReleaseVelocity)
            {
                runtime.State = AnimRigFootLockState.Releasing;
            }
        }

        private bool ShouldRelease(
            AnimRigFootLockRuntime runtime,
            AnimRigFootPose pose,
            AnimRigCharacterProfile profile,
            float rootDelta,
            float deltaTime)
        {
            if (rootDelta > profile.TeleportReleaseDistance)
            {
                return true;
            }

            if (!pose.HasGround)
            {
                runtime.GroundLossTime += Mathf.Max(0f, deltaTime);
                return runtime.GroundLossTime >= profile.GroundLossReleaseTime;
            }

            runtime.GroundLossTime = 0f;

            if (Vector3.Distance(pose.TargetPosition, runtime.LockPosition) >
                profile.LockReleaseDistance)
            {
                return true;
            }

            return Quaternion.Angle(pose.TargetRotation, runtime.LockRotation) >
                   profile.LockReleaseAngle;
        }

        private static void AdvanceLockBlend(
            AnimRigFootLockRuntime runtime,
            AnimRigCharacterProfile profile,
            float deltaTime)
        {
            float targetWeight =
                runtime.State == AnimRigFootLockState.Planting ||
                runtime.State == AnimRigFootLockState.Locked
                    ? 1f
                    : 0f;

            runtime.Weight = profile.FootLockBlendSpeed <= 0f
                ? targetWeight
                : Mathf.MoveTowards(
                    runtime.Weight,
                    targetWeight,
                    profile.FootLockBlendSpeed * Mathf.Max(0f, deltaTime));

            if (runtime.State == AnimRigFootLockState.Planting &&
                runtime.Weight >= 0.999f)
            {
                runtime.Weight = 1f;
                runtime.State = AnimRigFootLockState.Locked;
            }
            else if (runtime.State == AnimRigFootLockState.Releasing &&
                     runtime.Weight <= 0.001f)
            {
                runtime.Weight = 0f;
                runtime.State = AnimRigFootLockState.Released;
                runtime.GroundLossTime = 0f;
            }
        }

        private float CalculateRootDelta(Transform root)
        {
            if (root == null)
            {
                _hasLastRootPosition = false;
                return 0f;
            }

            if (!_hasLastRootPosition)
            {
                _lastRootPosition = root.position;
                _hasLastRootPosition = true;
                return 0f;
            }

            float delta = Vector3.Distance(root.position, _lastRootPosition);
            _lastRootPosition = root.position;
            return delta;
        }

        private AnimRigFootLockRuntime GetLockRuntime(AnimRigFootSlot slot)
        {
            return slot == AnimRigFootSlot.Left ? _leftLock : _rightLock;
        }

        private void ClearAnimationDrivenSuppression()
        {
            _leftLock.SuppressAnimationDrivenPlant = false;
            _rightLock.SuppressAnimationDrivenPlant = false;
        }

        private static float CalculateVelocity(
            Vector3 position,
            ref Vector3 previousPosition,
            ref bool hasPreviousPosition,
            float deltaTime)
        {
            if (!hasPreviousPosition)
            {
                previousPosition = position;
                hasPreviousPosition = true;
                return 0f;
            }

            float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
            float velocity = Vector3.Distance(position, previousPosition) / safeDeltaTime;
            previousPosition = position;
            return velocity;
        }

        private static float MoveWeight(
            float current,
            float target,
            float speed,
            float deltaTime)
        {
            if (speed <= 0f)
            {
                return target;
            }

            return Mathf.MoveTowards(
                Mathf.Clamp01(current),
                Mathf.Clamp01(target),
                speed * Mathf.Max(0f, deltaTime));
        }

        private static Vector3 ResolveFallbackPosition(
            AnimRigFootBinding binding,
            AnimRigFootPose previousPose)
        {
            if (binding.IkTarget != null) return binding.IkTarget.position;
            if (binding.FootBone != null) return binding.FootBone.position;
            if (binding.RaycastOrigin != null) return binding.RaycastOrigin.position;
            return previousPose.TargetPosition;
        }

        private static Quaternion ResolveBaseRotation(
            AnimRigFootBinding binding,
            AnimRigFootPose previousPose)
        {
            if (binding.FootBone != null) return binding.FootBone.rotation;
            if (binding.IkTarget != null) return binding.IkTarget.rotation;
            return previousPose.TargetRotation == default
                ? Quaternion.identity
                : previousPose.TargetRotation;
        }

        private void Reset()
        {
            leftFoot = new AnimRigFootBinding(AnimRigFootSlot.Left);
            rightFoot = new AnimRigFootBinding(AnimRigFootSlot.Right);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            DrawFootGizmos(_leftAppliedPose, Color.cyan);
            DrawFootGizmos(_rightAppliedPose, Color.magenta);
            DrawLockGizmo(_leftLock, Color.yellow);
            DrawLockGizmo(_rightLock, Color.red);
        }

        private static void DrawFootGizmos(
            AnimRigFootPose pose,
            Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(pose.ProbeOrigin, pose.GroundPoint);
            Gizmos.DrawWireSphere(pose.TargetPosition, 0.04f);

            if (!pose.HasGround) return;

            Gizmos.color = Color.green;
            Gizmos.DrawLine(pose.GroundPoint, pose.GroundPoint + pose.GroundNormal * 0.25f);
        }

        private static void DrawLockGizmo(
            AnimRigFootLockRuntime runtime,
            Color color)
        {
            if (runtime.State == AnimRigFootLockState.Released ||
                runtime.Weight <= 0f)
            {
                return;
            }

            Gizmos.color = color;
            Gizmos.DrawWireSphere(runtime.LockPosition, 0.06f);
            Gizmos.DrawLine(
                runtime.LockPosition,
                runtime.LockPosition + runtime.LockRotation * Vector3.forward * 0.2f);
        }

        private sealed class AnimRigFootLockRuntime
        {
            public AnimRigFootLockRuntime()
            {
                State = AnimRigFootLockState.Released;
                LockRotation = Quaternion.identity;
            }

            public AnimRigFootLockState State;
            public Vector3 LockPosition;
            public Quaternion LockRotation;
            public float Weight;
            public float GroundLossTime;
            public bool SuppressAnimationDrivenPlant;
        }
    }
}

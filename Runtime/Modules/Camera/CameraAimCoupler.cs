using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public class CameraAimCoupler : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private MonoBehaviour inputStateProvider;
        [SerializeField] private Vector2 lookSensitivity = new Vector2(180f, 120f);
        [SerializeField] private Vector2 pitchRange = new Vector2(-70f, 70f);

        [Header("Sync")]
        [SerializeField] private Transform syncTarget;
        [SerializeField] private bool coupled;

        private IInputStateProvider _inputStateProvider;
        private float _yaw;
        private float _pitch;

        public bool Coupled => coupled;
        public Transform SyncTarget => syncTarget;
        public Vector2 LookSensitivity => lookSensitivity;
        public Vector2 PitchRange => pitchRange;

        public void SetCoupled(bool value)
        {
            coupled = value;
        }

        public void SetSyncTarget(Transform target)
        {
            syncTarget = target;
        }

        public void SetInputStateProvider(MonoBehaviour provider)
        {
            inputStateProvider = provider;
            _inputStateProvider = null;
            ResolveInputStateProvider();
        }

        public void SetLookAngles(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = ClampPitch(pitch);
            ApplyAimRotation();
        }

        private void Awake()
        {
            ResolveInputStateProvider();
        }

        private void Update()
        {
            SampleInput(Time.deltaTime);
        }

        private void SampleInput(float deltaTime)
        {
            var source = ResolveInputStateProvider();
            if (source == null) return;

            var look = source.LookInput;
            _yaw += look.x * lookSensitivity.x * deltaTime;
            _pitch = ClampPitch(_pitch - look.y * lookSensitivity.y * deltaTime);

            ApplyAimRotation();
        }

        private void ApplyAimRotation()
        {
            transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            if (!coupled || syncTarget == null) return;

            if (IsAncestor(syncTarget, transform))
            {
                SyncAncestorRotation();
            }
            else
            {
                syncTarget.rotation = transform.rotation;
            }
        }

        private void SyncAncestorRotation()
        {
            var aimWorldRotation = transform.rotation;
            float yawDelta = GetHorizontalYawDelta(syncTarget, aimWorldRotation);
            if (!Mathf.Approximately(yawDelta, 0f))
            {
                syncTarget.rotation =
                    Quaternion.AngleAxis(yawDelta, syncTarget.up) * syncTarget.rotation;
            }

            transform.rotation = aimWorldRotation;
            CacheLookAnglesFromLocalRotation();
        }

        private IInputStateProvider ResolveInputStateProvider()
        {
            if (_inputStateProvider != null) return _inputStateProvider;

            if (inputStateProvider is IInputStateProvider provider)
            {
                _inputStateProvider = provider;
            }

            return _inputStateProvider;
        }

        private float ClampPitch(float value)
        {
            return Mathf.Clamp(value, pitchRange.x, pitchRange.y);
        }

        private void CacheLookAnglesFromLocalRotation()
        {
            var euler = transform.localRotation.eulerAngles;
            _yaw = NormalizeAngle(euler.y);
            _pitch = ClampPitch(NormalizeAngle(euler.x));
        }

        private static float GetHorizontalYawDelta(
            Transform target,
            Quaternion aimWorldRotation)
        {
            var up = target.up;
            var currentForward = Vector3.ProjectOnPlane(target.forward, up);
            var aimForward = Vector3.ProjectOnPlane(aimWorldRotation * Vector3.forward, up);
            if (currentForward.sqrMagnitude <= Mathf.Epsilon ||
                aimForward.sqrMagnitude <= Mathf.Epsilon)
            {
                return 0f;
            }

            return Vector3.SignedAngle(currentForward, aimForward, up);
        }

        private static bool IsAncestor(Transform candidate, Transform child)
        {
            var parent = child.parent;
            while (parent != null)
            {
                if (parent == candidate) return true;

                parent = parent.parent;
            }

            return false;
        }

        private static float NormalizeAngle(float value)
        {
            while (value > 180f)
            {
                value -= 360f;
            }

            while (value < -180f)
            {
                value += 360f;
            }

            return value;
        }

        private void OnValidate()
        {
            if (pitchRange.x > pitchRange.y)
            {
                (pitchRange.x, pitchRange.y) = (pitchRange.y, pitchRange.x);
            }

            _pitch = ClampPitch(_pitch);
        }
    }
}

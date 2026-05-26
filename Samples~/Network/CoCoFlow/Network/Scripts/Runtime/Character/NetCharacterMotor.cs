using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network.Character
{
    /// <summary>
    /// NetPlayer 专用网络移动器。
    /// 将移动模拟收束到 Fusion Tick 内，避免再委托给 CharacterLocomotion.Update()。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public class NetCharacterMotor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _rotationSmoothTime = 0.1f;
        [SerializeField] private bool _inputIsWorldSpace = true;

        [Header("Gravity")]
        [SerializeField] private bool _useGravity = true;
        [SerializeField] private float _gravity = -9.81f;
        [SerializeField] private float _gravityMultiplier = 2f;
        [SerializeField] private LayerMask _groundLayer;
        [SerializeField] private float _groundCheckRadius = 0.2f;
        [SerializeField] private Vector3 _groundCheckOffset = new Vector3(0f, 0.1f, 0f);

        [Header("Network Smoothing")]
        [SerializeField] private float _proxyInterpolationSpeed = 15f;
        [SerializeField] private float _predictionCorrectionSpeed = 20f;
        [SerializeField] private float _snapDistance = 3f;

        [Header("Compatibility")]
        [SerializeField] private bool _disableCharacterLocomotion = true;

        private CharacterController _characterController;
        private CharacterLocomotion _legacyLocomotion;
        private Transform _cachedTransform;
        private Camera _cachedMainCamera;
        private float _verticalVelocity;
        private float _currentRotationVelocity;

        public Vector3 CurrentVelocity { get; private set; }
        public bool IsGrounded { get; private set; }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _legacyLocomotion = GetComponent<CharacterLocomotion>();
            _cachedTransform = transform;
            _cachedMainCamera = Camera.main;

            if (_disableCharacterLocomotion && _legacyLocomotion != null)
            {
                _legacyLocomotion.enabled = false;
            }
        }

        public void Simulate(Vector2 inputDir, float deltaTime)
        {
            if (_characterController == null || deltaTime <= 0f) return;

            if (inputDir.sqrMagnitude > 1f)
                inputDir.Normalize();

            CheckGrounded();
            ApplyGravity(deltaTime);

            var worldDir = ConvertToWorldDirection(inputDir);
            var horizontalVelocity = worldDir * _moveSpeed;
            var velocity = horizontalVelocity + Vector3.up * _verticalVelocity;

            _characterController.Move(velocity * deltaTime);
            CurrentVelocity = _characterController.velocity;

            if (worldDir.sqrMagnitude > 0.01f)
                RotateTowards(worldDir);

            CheckGrounded();
        }

        public void InterpolateTo(Vector3 targetPosition, Vector3 targetVelocity, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            var t = Mathf.Clamp01(_proxyInterpolationSpeed * deltaTime);
            _cachedTransform.position = Vector3.Lerp(_cachedTransform.position, targetPosition, t);
            CurrentVelocity = targetVelocity;
        }

        public void CorrectPrediction(Vector3 targetPosition, Vector3 targetVelocity, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            var distance = Vector3.Distance(_cachedTransform.position, targetPosition);
            if (distance > _snapDistance)
            {
                Warp(targetPosition);
            }
            else if (distance > 0.01f)
            {
                var t = Mathf.Clamp01(_predictionCorrectionSpeed * deltaTime);
                _cachedTransform.position = Vector3.Lerp(_cachedTransform.position, targetPosition, t);
            }

            CurrentVelocity = targetVelocity;
        }

        public void Warp(Vector3 position)
        {
            if (_characterController != null)
            {
                var wasEnabled = _characterController.enabled;
                _characterController.enabled = false;
                _cachedTransform.position = position;
                _characterController.enabled = wasEnabled;
            }
            else
            {
                _cachedTransform.position = position;
            }
        }

        private void ApplyGravity(float deltaTime)
        {
            if (!_useGravity)
            {
                _verticalVelocity = 0f;
                return;
            }

            if (IsGrounded && _verticalVelocity < 0.01f)
            {
                _verticalVelocity = -2f;
            }
            else
            {
                _verticalVelocity += _gravity * _gravityMultiplier * deltaTime;
            }
        }

        private void CheckGrounded()
        {
            var mask = _groundLayer.value == 0 ? Physics.DefaultRaycastLayers : _groundLayer.value;
            var checkPosition = _cachedTransform.position + _groundCheckOffset;
            IsGrounded = Physics.CheckSphere(
                checkPosition,
                _groundCheckRadius,
                mask,
                QueryTriggerInteraction.Ignore);
        }

        private Vector3 ConvertToWorldDirection(Vector2 inputDir)
        {
            if (inputDir.sqrMagnitude < 0.01f) return Vector3.zero;

            if (_inputIsWorldSpace)
                return new Vector3(inputDir.x, 0f, inputDir.y).normalized;

            var mainCam = _cachedMainCamera;
            if (mainCam == null)
                mainCam = _cachedMainCamera = Camera.main;

            if (mainCam == null)
                return new Vector3(inputDir.x, 0f, inputDir.y).normalized;

            var camForward = mainCam.transform.forward;
            var camRight = mainCam.transform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            return (camForward * inputDir.y + camRight * inputDir.x).normalized;
        }

        private void RotateTowards(Vector3 worldDir)
        {
            var targetAngle = Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg;
            var angle = Mathf.SmoothDampAngle(
                _cachedTransform.eulerAngles.y,
                targetAngle,
                ref _currentRotationVelocity,
                _rotationSmoothTime);
            _cachedTransform.rotation = Quaternion.Euler(0f, angle, 0f);
        }
    }
}

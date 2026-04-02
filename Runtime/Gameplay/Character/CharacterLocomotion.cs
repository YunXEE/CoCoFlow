using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    [RequireComponent(typeof(CharacterController))]
    public class CharacterLocomotion : MonoBehaviour
    {
        [Header("Components")]
        private CharacterController _characterController;

        [Header("Gravity & Ground")]
        [SerializeField] private bool isUsingGravity = true; //是否启用自动重力计算
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float baseGravityMultiplier = 2f;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private float groundCheckRadius = 0.2f;
        [SerializeField] private Vector3 groundCheckOffset = new Vector3(0, 0.1f, 0);
        
        [Header("Rotation")]
        [SerializeField] private float rotationSmoothTime = 0.1f;
        private float _currentRotationVelocity;
        
        // 核心状态
        public bool IsGrounded { get; private set; }
        public Vector3 CurrentVelocity => _characterController.velocity;
        
        private Vector3 _targetMovementVelocity;     // 普通水平速度（收到速度倍率影响）
        private Vector3 _forcedMovementVelocity;     // 强制水平速度（不受速度倍率影响）
        private float _currentSpeedMultiplier = 1f;  // 水平速度倍率 
        private float _currentGravityScale = 1f;     // 重力缩放倍率 
        
        private float _verticalVelocity;             // 垂直速度

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
        }

        private void Update()
        {
            CheckGrounded();
            HandleGravity();
            ApplyMovement();
        }

        #region Public API
        
        // ================= 重力处理开关 ==============
        public void SetGravityEnable(bool enable)
        {
            isUsingGravity = enable;
            if (!enable) 
            {
                _verticalVelocity = 0f;
            }
        }

        // ================= 水平移动 =================

        // 普通水平移动
        public void SetMovementVelocity(Vector3 velocity)
        {
            _targetMovementVelocity = velocity;
        }

        // 强制水平移动
        public void SetForcedVelocity(Vector3 velocity)
        {
            _forcedMovementVelocity = velocity;
        }

        // 添加速度倍率
        public void AddSpeedMultiplier(float multiplier)
        {
            _currentSpeedMultiplier *= multiplier;
        }

        // ================= 垂直移动与重力 =================

        // 普通跳跃
        public void Jump(float jumpForce)
        {
            if (IsGrounded) _verticalVelocity = jumpForce;
        }

        // 强制升空/弹飞 
        public void Launch(float launchForce)
        {
            _verticalVelocity = launchForce;
        }

        // 设置重力缩放
        public void SetGravityScale(float scale)
        {
            _currentGravityScale = scale;
        }

        // ================= 旋转 =================

        public void SetRotation(Vector3 lookDirection)
        {
            if (lookDirection.sqrMagnitude < 0.01f) return;
            var targetAngle = Mathf.Atan2(lookDirection.x, lookDirection.z) * Mathf.Rad2Deg;
            var angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _currentRotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        public void SetRotationInstant(Vector3 lookDirection)
        {
            if (lookDirection.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.LookRotation(lookDirection);
        }

        #endregion

        #region Internal Logic 

        private void CheckGrounded()
        {
            var checkPosition = transform.position + groundCheckOffset;
            IsGrounded = Physics.CheckSphere(checkPosition, groundCheckRadius, groundLayer);
        }

        private void HandleGravity()
        {
            if (!isUsingGravity) return;
            
            if (IsGrounded && _verticalVelocity < 0.01f)
            {
                _verticalVelocity = -2f; // 吸附地面
            }
            else
            {
                _verticalVelocity += gravity * baseGravityMultiplier * _currentGravityScale * Time.deltaTime;
            }
        }

        private void ApplyMovement()
        {
            var finalHorizontalVelocity = (_forcedMovementVelocity.sqrMagnitude > 0.001f) 
                ? _forcedMovementVelocity 
                : _targetMovementVelocity * _currentSpeedMultiplier;
            
            var finalVelocity = finalHorizontalVelocity + new Vector3(0, _verticalVelocity, 0);
            _characterController.Move(finalVelocity * Time.deltaTime);
            
            _targetMovementVelocity = Vector3.zero; 
            _forcedMovementVelocity = Vector3.zero; 
            _currentSpeedMultiplier = 1f; 
            _currentGravityScale = 1f;
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position + groundCheckOffset, groundCheckRadius);
        }
    }
}
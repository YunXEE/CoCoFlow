using System;
using Fusion;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Network.Character
{
    /// <summary>
    /// 基于 Fusion Tick 的角色运动驱动，通过反射委托调用 CharacterLocomotion API。
    /// 所有物理计算均在 FixedUpdateNetwork 中执行，不使用 Unity Update/FixedUpdate。
    /// </summary>
    public class NetCharacterMotion : NetworkBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _rotationSpeed = 10f;
        [SerializeField] private float _interpolationSpeed = 15f;

        // 通过 NetCharacter 注入的反射委托，指向 CharacterLocomotion.SetMovementVelocity / SetRotation
        private Action<Vector3> _setMovementVelocity;
        private Action<Vector3> _setRotation;

        private Transform _cachedTransform;
        private Camera _cachedMainCamera; // 缓存 Camera.main 避免每 FUN tick 的 FindGameObjectsWithTag 分配
        private Vector3 _lastInterpPosition;
        private Vector3 _lastInterpVelocity;

        #region Unity Lifecycle

        private void Awake()
        {
            _cachedTransform = transform;
            _cachedMainCamera = Camera.main;
        }

        #endregion

        #region Public API

        /// <summary>
        /// 由 NetCharacter.Spawned() 调用，注入 Locomotion 方法委托。
        /// </summary>
        public void Initialize(Action<Vector3> setMovementVelocity, Action<Vector3> setRotation)
        {
            _setMovementVelocity = setMovementVelocity;
            _setRotation = setRotation;
        }

        /// <summary>
        /// 权威端处理移动输入：转换为世界空间方向，驱动 Locomotion。
        /// 在 FixedUpdateNetwork 中调用。
        /// </summary>
        public void ProcessMovement(Vector2 inputDir)
        {
            if (_setMovementVelocity == null) return;

            var worldDir = ConvertToWorldDirection(inputDir);
            var velocity = worldDir * _moveSpeed;
            _setMovementVelocity.Invoke(velocity);

            if (inputDir.sqrMagnitude > 0.01f)
            {
                _setRotation.Invoke(worldDir);
            }
        }

        /// <summary>
        /// 输入权威端的本地预测处理，与 ProcessMovement 逻辑一致但仅影响本地。
        /// 在 FixedUpdateNetwork 中调用。
        /// </summary>
        public void ProcessMovementPrediction(Vector2 inputDir)
        {
            if (_setMovementVelocity == null) return;

            var worldDir = ConvertToWorldDirection(inputDir);
            var velocity = worldDir * _moveSpeed;
            _setMovementVelocity.Invoke(velocity);

            if (inputDir.sqrMagnitude > 0.01f)
            {
                _setRotation.Invoke(worldDir);
            }
        }

        /// <summary>
        /// 代理端（非权威非输入端）插值到目标位置和速度。
        /// 在 Render 中调用。
        /// </summary>
        public void InterpolateTo(Vector3 targetPosition, Vector3 targetVelocity)
        {
            _lastInterpPosition = Vector3.Lerp(
                _cachedTransform.position,
                targetPosition,
                _interpolationSpeed * Runner.DeltaTime
            );

            _lastInterpVelocity = targetVelocity;
            _cachedTransform.position = _lastInterpPosition;
        }

        #endregion

        #region Internal Logic

        /// <summary>
        /// 将 2D 输入方向转换为相对于摄像机朝向的世界空间方向。
        /// </summary>
        private Vector3 ConvertToWorldDirection(Vector2 inputDir)
        {
            if (inputDir.sqrMagnitude < 0.01f) return Vector3.zero;

            var mainCam = _cachedMainCamera;
            if (mainCam == null)
            {
                return new Vector3(inputDir.x, 0f, inputDir.y).normalized;
            }

            var camForward = mainCam.transform.forward;
            var camRight = mainCam.transform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            return (camForward * inputDir.y + camRight * inputDir.x).normalized;
        }

        #endregion
    }
}

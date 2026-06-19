using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using Unity.Cinemachine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [RequireComponent(typeof(CameraManager))]
    public class CamLockOnHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CinemachineCamera lockOnCamera;
        [SerializeField] private Transform playerTarget;
        [Tooltip("参考视角的 Transform，通常是主相机的 Transform，用于判断目标是否在屏幕前方")]
        [SerializeField] private Transform mainCameraTransform;

        [Header("Search Settings")]
        [Tooltip("索敌半径")]
        [SerializeField] private float searchRadius = 15f;
        [Tooltip("索敌最大角度（FOV的一半），例如 60 表示前方 120 度扇形范围内")]
        [SerializeField] private float maxSearchAngle = 60f;
        [Tooltip("可锁定目标的 Layer")]
        [SerializeField] private LayerMask targetLayer;
        [Tooltip("障碍物的 Layer，用于防穿墙锁定")]
        [SerializeField] private LayerMask obstacleLayer;

        [Header("Input Action Binding")]
        [Tooltip("触发锁定切换的 Action 名（来自 InputActionAsset）。留空则不绑定。")]
        [SerializeField] private string lockOnActionName = "LockOn";

        private CameraManager _cameraManager;

        private readonly Collider[] _searchResults = new Collider[20];
        private IInputEventSource _inputEvents;
        private IDisposable _inputEventsWait;

        #region Public API

        public CamLockOnObject CurrentTarget { get; private set; }

        public void ToggleLockOn()
        {
            if (CurrentTarget != null)
            {
                ClearLock();
            }
            else
            {
                TryLockTarget();
            }
        }

        public void ClearLock()
        {
            if (CurrentTarget != null)
            {
                CurrentTarget.OnUnlocked();
                CurrentTarget = null;
            }

            lockOnCamera.LookAt = null;
            _cameraManager.SwitchCameraState(CameraState.FreeLook);
        }

        #endregion

        #region Internal Logic

        private void Awake()
        {
            _cameraManager = GetComponent<CameraManager>();
            if (mainCameraTransform == null && UnityEngine.Camera.main != null)
            {
                mainCameraTransform = UnityEngine.Camera.main.transform;
            }

            // 异步绑定输入事件
            _inputEventsWait = CoCoServices.WaitFor<IInputEventSource>(svc =>
            {
                _inputEvents = svc;
                if (!string.IsNullOrEmpty(lockOnActionName))
                    _inputEvents.OnActionPerformed += OnInputActionPerformed;
            });
        }

        private void OnDestroy()
        {
            _inputEventsWait?.Dispose();
            if (_inputEvents != null && !string.IsNullOrEmpty(lockOnActionName))
            {
                _inputEvents.OnActionPerformed -= OnInputActionPerformed;
            }
        }

        private void OnInputActionPerformed(string actionName)
        {
            if (actionName == lockOnActionName) ToggleLockOn();
        }

        private void TryLockTarget()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(playerTarget.position, searchRadius, _searchResults, targetLayer);
            if (hitCount == 0) return;

            CamLockOnObject bestTarget = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                var target = _searchResults[i].GetComponent<CamLockOnObject>();
                if (target == null) continue;

                Vector3 directionToTarget = target.lockPoint.position - mainCameraTransform.position;
                float distance = directionToTarget.magnitude;

                float angle = Vector3.Angle(mainCameraTransform.forward, directionToTarget);
                if (angle > maxSearchAngle) continue;

                if (Physics.Raycast(mainCameraTransform.position, directionToTarget.normalized, distance, obstacleLayer))
                {
                    continue;
                }

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    bestTarget = target;
                }
            }

            if (bestTarget != null)
            {
                CurrentTarget = bestTarget;
                CurrentTarget.OnLocked();
                lockOnCamera.LookAt = CurrentTarget.lockPoint;
                _cameraManager.SwitchCameraState(CameraState.LockOn);
            }
            else
            {
                Debug.Log("[LockOnCameraHandler] 未找到可用目标");
            }
        }

        private void Update()
        {
            if (CurrentTarget != null)
            {
                if (!CurrentTarget.gameObject.activeInHierarchy ||
                    Vector3.Distance(playerTarget.position, CurrentTarget.transform.position) > searchRadius * 1.5f)
                {
                    ClearLock();
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (playerTarget != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(playerTarget.position, searchRadius);
            }
        }

        #endregion
    }
}

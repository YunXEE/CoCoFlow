using CoCoFlow.Runtime.Core;
using UnityEngine;
using Unity.Cinemachine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [RequireComponent(typeof(CameraManager))]
    public class CamOrbitHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CinemachineCamera freeLookCamera;
        [Tooltip("需要获取玩家的 Transform 来向下打射线检测坡度")]
        [SerializeField] private Transform playerTarget;

        [Header("Look Settings")]
        [SerializeField] private float lookSpeedX = 2f;
        [SerializeField] private float lookSpeedY = 0.15f;
        [SerializeField] private bool invertY;
        [SerializeField] private bool invertX;

        [Header("Zoom Settings")]
        [SerializeField] private float zoomSpeed = 1f;
        [SerializeField] private float minZoomRadius = 1.5f;
        [SerializeField] private float maxZoomRadius = 6f;
        [SerializeField] private float zoomSmoothTime = 0.1f;

        [Header("Slope Auto Adjust")]
        [Tooltip("是否开启坡度自动微调")]
        [SerializeField] private bool enableAutoAdjust = true;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private float autoAdjustDelay = 1.5f;
        [SerializeField] private float adjustSmoothTime = 0.5f;

        [Tooltip("默认的俯视角度，例如 10 度")]
        [SerializeField] private float defaultYAngle = 10f;
        [Tooltip("坡度对角度的影响系数，例如 40 度")]
        [SerializeField] private float slopeMultiplier = 40f;
        [Tooltip("向下打射线的起始点要往前偏移一点，才能提前侦测下坡")]
        [SerializeField] private float lookAheadDistance = 0.5f;

        [Tooltip("限制相机的最小角度（仰视）和最大角度（俯视）")]
        [SerializeField] private float minAngle = -20f;
        [SerializeField] private float maxAngle = 60f;

        private CameraManager cameraManager;
        private CinemachineOrbitalFollow orbitalFollow;

        // 通过抽象访问输入
        private IInputStateProvider _input;

        private float currentZoomLevel;
        private float zoomVelocity;

        private float lastManualInputTime;
        private float adjustVelocityY;

        private void Awake()
        {
            cameraManager = GetComponent<CameraManager>();

            if (freeLookCamera != null)
            {
                orbitalFollow = freeLookCamera.GetComponent<CinemachineOrbitalFollow>();
            }

            // 异步获取，兼容 InputReader 在场景里晚于本组件加载的情况
            CoCoServices.WaitFor<IInputStateProvider>(svc => _input = svc);

            InitializeZoomData();
            lastManualInputTime = Time.time;
        }

        private void InitializeZoomData()
        {
            if (orbitalFollow == null) return;
            currentZoomLevel = orbitalFollow.Radius;
        }

        private void LateUpdate()
        {
            if (_input == null) return;
            if (cameraManager.CurrentState != CameraState.FreeLook || orbitalFollow == null) return;

            HandleRotationAndSlope();
            HandleZoom();
        }

        private void HandleRotationAndSlope()
        {
            bool hasManualLookInput = _input.LookInput.sqrMagnitude > 0.01f;

            if (hasManualLookInput)
            {
                lastManualInputTime = Time.time;

                float deltaX = _input.LookInput.x * lookSpeedX * (invertX ? -1 : 1);
                orbitalFollow.HorizontalAxis.Value += deltaX;

                float deltaY = _input.LookInput.y * lookSpeedY * (invertY ? 1 : -1);
                orbitalFollow.VerticalAxis.Value += deltaY;
            }
            else if (enableAutoAdjust && Time.time - lastManualInputTime > autoAdjustDelay)
            {
                if (_input.MoveInput.sqrMagnitude > 0.01f)
                {
                    ExecuteSlopeAdjustment();
                }
            }
        }

        private void ExecuteSlopeAdjustment()
        {
            if (playerTarget == null) return;

            Vector3 horizontalForward = new Vector3(playerTarget.forward.x, 0, playerTarget.forward.z).normalized;
            Vector3 rayStart = playerTarget.position + Vector3.up * 0.5f + horizontalForward * lookAheadDistance;

            Debug.DrawRay(rayStart, Vector3.down * 2f, Color.red);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2f, groundLayer))
            {
                float slopeDot = Vector3.Dot(horizontalForward, hit.normal);
                float targetAngle = defaultYAngle + (slopeDot * slopeMultiplier);
                targetAngle = Mathf.Clamp(targetAngle, minAngle, maxAngle);

                float currentAngle = orbitalFollow.VerticalAxis.Value;
                orbitalFollow.VerticalAxis.Value = Mathf.SmoothDamp(currentAngle, targetAngle, ref adjustVelocityY, adjustSmoothTime);
            }
            else
            {
                float currentAngle = orbitalFollow.VerticalAxis.Value;
                orbitalFollow.VerticalAxis.Value = Mathf.SmoothDamp(currentAngle, defaultYAngle, ref adjustVelocityY, adjustSmoothTime * 2f);
            }
        }

        private void HandleZoom()
        {
            float scrollInput = _input.ZoomInput.y;

            if (Mathf.Abs(scrollInput) > 0.01f)
            {
                float scrollDir = Mathf.Sign(scrollInput);
                currentZoomLevel -= scrollDir * zoomSpeed;
                currentZoomLevel = Mathf.Clamp(currentZoomLevel, minZoomRadius, maxZoomRadius);
            }

            orbitalFollow.Radius = Mathf.SmoothDamp(
                orbitalFollow.Radius,
                currentZoomLevel,
                ref zoomVelocity,
                zoomSmoothTime
            );
        }
    }
}

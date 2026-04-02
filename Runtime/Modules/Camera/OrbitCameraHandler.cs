using CoCoFlow.Runtime.Modules.Input;
using UnityEngine;
using Unity.Cinemachine; 

namespace CoCoFlow.Runtime.Modules.Camera
{
    [RequireComponent(typeof(CameraManager))]
    public class OrbitCameraHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CinemachineCamera freeLookCamera; 
        [SerializeField] private PlayerInputReader inputReader;
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
        
        private float[] originalOrbits; 
        private float currentZoomLevel;
        private float zoomVelocity;

        private float lastManualInputTime;
        private float adjustVelocityY; 

        private void Awake()
        {
            cameraManager = GetComponent<CameraManager>();
            
            // 获取轨道跟随模块
            if (freeLookCamera != null)
            {
                orbitalFollow = freeLookCamera.GetComponent<CinemachineOrbitalFollow>();
            }
            
            InitializeZoomData();
            lastManualInputTime = Time.time;
        }

        private void InitializeZoomData()
        {
            if (orbitalFollow == null) return;
            
            // 【CM3 优化】新版直接获取全局 Radius 即可，不需要再管三个具体的轨道了！
            currentZoomLevel = orbitalFollow.Radius; 
        }

        private void LateUpdate()
        {
            if (cameraManager.CurrentState != CameraState.FreeLook || orbitalFollow == null) return;

            HandleRotationAndSlope();
            HandleZoom(); 
        }

        private void HandleRotationAndSlope()
        {
            bool hasManualLookInput = inputReader.LookInput.sqrMagnitude > 0.01f;

            if (hasManualLookInput)
            {
                lastManualInputTime = Time.time;

                // 新版 API：直接修改 HorizontalAxis 和 VerticalAxis 的 Value
                float deltaX = inputReader.LookInput.x * lookSpeedX * (invertX ? -1 : 1);
                orbitalFollow.HorizontalAxis.Value += deltaX;

                float deltaY = inputReader.LookInput.y * lookSpeedY * (invertY ? 1 : -1);
                orbitalFollow.VerticalAxis.Value += deltaY;
            }
            else if (enableAutoAdjust && Time.time - lastManualInputTime > autoAdjustDelay)
            {
                if (inputReader.MoveInput.sqrMagnitude > 0.01f)
                {
                    ExecuteSlopeAdjustment();
                }
            }
        }

        private void ExecuteSlopeAdjustment()
        {
            if (playerTarget == null) return;

            // 核心优化 1：提取水平面的前方向量。防止玩家模型在爬坡时前倾导致点乘计算错误。
            Vector3 horizontalForward = new Vector3(playerTarget.forward.x, 0, playerTarget.forward.z).normalized;
    
            // 核心优化 2：射线起始点稍微往前偏移 (Look Ahead)，提前感知下坡
            Vector3 rayStart = playerTarget.position + Vector3.up * 0.5f + horizontalForward * lookAheadDistance;

            // 画一条 Debug 线方便你在编辑器里观察射线有没有打对位置
            Debug.DrawRay(rayStart, Vector3.down * 2f, Color.red);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2f, groundLayer))
            {
                // 计算点乘。
                // 上坡时法线向后倾，点乘为负；下坡时法线向前倾，点乘为正。
                float slopeDot = Vector3.Dot(horizontalForward, hit.normal);
        
                // 核心优化 3：基于角度计算。下坡时 targetAngle 变大（相机抬高俯视），上坡时 targetAngle 变小（相机降低仰视）
                float targetAngle = defaultYAngle + (slopeDot * slopeMultiplier); 
                targetAngle = Mathf.Clamp(targetAngle, minAngle, maxAngle);

                float currentAngle = orbitalFollow.VerticalAxis.Value;
                orbitalFollow.VerticalAxis.Value = Mathf.SmoothDamp(currentAngle, targetAngle, ref adjustVelocityY, adjustSmoothTime);
            }
            else
            {
                // 射线没打到地面（比如跳跃或下落中），缓慢恢复默认角度
                float currentAngle = orbitalFollow.VerticalAxis.Value;
                orbitalFollow.VerticalAxis.Value = Mathf.SmoothDamp(currentAngle, defaultYAngle, ref adjustVelocityY, adjustSmoothTime * 2f);
            }
        }

        private void HandleZoom()
        {
            float scrollInput = inputReader.ZoomInput.y;

            if (Mathf.Abs(scrollInput) > 0.01f)
            {
                // 【极其关键】将 120 / -120 强行归一化为 1 或 -1
                float scrollDir = Mathf.Sign(scrollInput); 

                // 滚轮向上 (scrollDir 为 1) -> 视角拉近 (减小半径)
                // 滚轮向下 (scrollDir 为 -1) -> 视角拉远 (增大半径)
                currentZoomLevel -= scrollDir * zoomSpeed;
                
                // 限制缩放范围
                currentZoomLevel = Mathf.Clamp(currentZoomLevel, minZoomRadius, maxZoomRadius);
            }

            // 【CM3 优化】直接对 orbitalFollow.Radius 进行平滑插值！
            orbitalFollow.Radius = Mathf.SmoothDamp(
                orbitalFollow.Radius, 
                currentZoomLevel, 
                ref zoomVelocity, 
                zoomSmoothTime
            );
        }
    }
}
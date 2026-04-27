using UnityEngine;
using Unity.Cinemachine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public enum CameraState
    {
        FreeLook,   // 自由视角（探索）
        LockOn,     // 锁定视角（战斗）
        Cinematic   // 过场动画（CG）
    }

    public class CameraManager : MonoBehaviour
    {
        [Header("Cinemachine 3 Cameras")]
        [SerializeField] private CinemachineCamera freeLookCamera;
        [SerializeField] private CinemachineCamera lockOnCamera;

        public CameraState CurrentState { get; private set; }

        private void Awake()
        {
            if (freeLookCamera == null)
            {
                Debug.LogError("[CoCoFlow] CameraManager 缺少 freeLookCamera！");
            }
        }

        private void Start()
        {
            SwitchCameraState(CameraState.FreeLook);
        }

        public void SwitchCameraState(CameraState newState)
        {
            CurrentState = newState;

            // 重置所有优先级
            if (freeLookCamera != null) freeLookCamera.Priority = 10;
            if (lockOnCamera != null) lockOnCamera.Priority = 10;

            switch (newState)
            {
                case CameraState.FreeLook:
                    if (freeLookCamera != null) freeLookCamera.Priority = 20;
                    break;
                case CameraState.LockOn:
                    if (lockOnCamera != null) lockOnCamera.Priority = 20;
                    break;
                case CameraState.Cinematic:
                    break;
            }

            Debug.Log($"[CameraManager] 视角切换至: {newState}");
        }
    }
}

using UnityEngine;
using Unity.Cinemachine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [RequireComponent(typeof(CameraManager))]
    public class LockOnCameraHandler : MonoBehaviour
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

        private CameraManager cameraManager;
        public LockOnObject CurrentTarget { get; private set; }

        private Collider[] searchResults = new Collider[20];

        private void Awake()
        {
            cameraManager = GetComponent<CameraManager>();
            if (mainCameraTransform == null && UnityEngine.Camera.main != null)
            {
                mainCameraTransform = UnityEngine.Camera.main.transform;
            }
        }

        // 这个方法可以被你的 PlayerInputReader 通过事件触发
        // 比如：inputReader.OnLockOnPerformed += ToggleLockOn;
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

        private void TryLockTarget()
        {
            // 1. 获取范围内的所有碰撞体 (非分配模式，避免 GC)
            int hitCount = Physics.OverlapSphereNonAlloc(playerTarget.position, searchRadius, searchResults, targetLayer);
            if (hitCount == 0) return;

            LockOnObject bestTarget = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                var target = searchResults[i].GetComponent<LockOnObject>();
                if (target == null) continue;

                Vector3 directionToTarget = target.lockPoint.position - mainCameraTransform.position;
                float distance = directionToTarget.magnitude;

                // 2. 角度过滤：只锁定屏幕前方的敌人
                float angle = Vector3.Angle(mainCameraTransform.forward, directionToTarget);
                if (angle > maxSearchAngle) continue;

                // 3. 视线检测：检查玩家和敌人之间有没有墙壁
                if (Physics.Raycast(mainCameraTransform.position, directionToTarget.normalized, distance, obstacleLayer))
                {
                    continue; // 被墙挡住了
                }

                // 4. 排序算法：这里使用最简单的“距离最近”，商业中也可加入“角度最小（最靠近屏幕中心）”的权重
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    bestTarget = target;
                }
            }

            // 5. 执行锁定
            if (bestTarget != null)
            {
                CurrentTarget = bestTarget;
                CurrentTarget.OnLocked();

                // 配置 CM3 锁定相机的 LookAt 为敌人
                lockOnCamera.LookAt = CurrentTarget.lockPoint;
                
                // 切换状态
                cameraManager.SwitchCameraState(CameraState.LockOn);
            }
            else
            {
                // 如果没找到目标，一般会执行“镜头回正”逻辑 (Reset Camera)
                Debug.Log("[TargetLockController] 未找到可用目标");
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
            cameraManager.SwitchCameraState(CameraState.FreeLook);
        }

        private void Update()
        {
            // 锁定状态下的安全检查
            if (CurrentTarget != null)
            {
                // 如果敌人跑出过远距离，或者敌人死亡（gameObject被销毁/禁用），自动断开
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
    }
}
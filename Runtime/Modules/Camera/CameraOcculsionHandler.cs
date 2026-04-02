using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public class CameraOcclusionHandler : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("玩家的中心观察点，通常在胸口或头部")]
        [SerializeField] private Transform playerTarget;
        [Tooltip("真正渲染画面的主摄像机")]
        [SerializeField] private UnityEngine.Camera mainCamera;

        [Header("Occlusion Settings")]
        [Tooltip("哪些层的物体会被遮挡透明化（比如 Environment, Obstacles）")]
        [SerializeField] private LayerMask occlusionLayer;
        [Tooltip("射线粗细，建议比胶囊体稍微宽一点")]
        [SerializeField] private float castRadius = 0.5f;
        [Tooltip("射线检测频率（秒），不需要每帧检测，0.1秒一次即可节省性能")]
        [SerializeField] private float checkInterval = 0.1f;

        // 避免 GC 的非分配射线检测数组
        private RaycastHit[] hits = new RaycastHit[10];
        
        // 记录当前正在被透视的物体
        private HashSet<OccludableObject> currentOccludedObjects = new HashSet<OccludableObject>();
        private HashSet<OccludableObject> previousOccludedObjects = new HashSet<OccludableObject>();

        private float timer;

        private void LateUpdate()
        {
            if (playerTarget == null || mainCamera == null) return;

            timer += Time.deltaTime;
            if (timer >= checkInterval)
            {
                timer = 0f;
                CheckOcclusion();
            }
        }

        private void CheckOcclusion()
        {
            // 交换当前和前一次的集合，准备记录新一轮
            var temp = previousOccludedObjects;
            previousOccludedObjects = currentOccludedObjects;
            currentOccludedObjects = temp;
            currentOccludedObjects.Clear();

            Vector3 camPos = mainCamera.transform.position;
            Vector3 targetPos = playerTarget.position;
            Vector3 direction = targetPos - camPos;
            float distance = direction.magnitude;

            // 稍微缩短检测距离，防止把玩家自己给隐形了
            int hitCount = Physics.SphereCastNonAlloc(camPos, castRadius, direction.normalized, hits, distance - 0.5f, occlusionLayer);

            for (int i = 0; i < hitCount; i++)
            {
                // 尝试获取可透视组件（商业项目中，环境物件最好在预制体上挂好这个组件，避免这里使用 GetComponent 带来开销。
                // 如果为了极致性能，可以使用全局 Dictionary<Collider, OccludableObject> 来做映射映射查找）
                if (hits[i].collider.TryGetComponent<OccludableObject>(out var occludable))
                {
                    currentOccludedObjects.Add(occludable);
                    
                    // 如果是新被遮挡的，让它变透明
                    if (!previousOccludedObjects.Contains(occludable))
                    {
                        occludable.FadeOut();
                    }
                }
            }

            // 遍历前一次的集合，如果不在当前的集合中，说明不再遮挡，恢复实体
            foreach (var occludable in previousOccludedObjects)
            {
                if (!currentOccludedObjects.Contains(occludable))
                {
                    occludable.FadeIn();
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (playerTarget != null && mainCamera != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(mainCamera.transform.position, castRadius);
            }
        }
    }
}
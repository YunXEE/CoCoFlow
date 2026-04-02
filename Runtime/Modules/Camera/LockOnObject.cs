using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public class LockOnObject : MonoBehaviour
    {
        [Tooltip("锁定时的瞄准准星应该挂载的 Transform，比如敌人的胸口")]
        public Transform lockPoint;

        private void Reset()
        {
            // 如果没手动指定，默认用自己的 Transform
            if (lockPoint == null) lockPoint = transform;
        }

        // 预留接口：当被锁定时，可以在这里显示UI准星
        public void OnLocked()
        {
            // Debug.Log($"{gameObject.name} 被锁定了！");
        }

        public void OnUnlocked()
        {
            // Debug.Log($"{gameObject.name} 解除锁定！");
        }
    }
}
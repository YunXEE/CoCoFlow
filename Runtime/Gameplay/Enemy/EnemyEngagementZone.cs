using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Enemy
{
    /// <summary>
    /// 战斗接敌区域 —— 挂载在场景中带有 BoxCollider 的 GameObject 上，
    /// 用于判定玩家是否进入该区域，触发敌人的战斗状态切换。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class EnemyEngagementZone : MonoBehaviour
    {
        private BoxCollider _zoneCollider;

        private void Awake()
        {
            _zoneCollider = GetComponent<BoxCollider>();
        }

        #region Public API

        /// <summary>判断指定位置是否在战斗区域内</summary>
        /// <param name="position">世界坐标</param>
        /// <returns>true 表示在区域内</returns>
        public bool IsPositionInsideZone(Vector3 position)
        {
            if (_zoneCollider == null)
            {
                _zoneCollider = GetComponent<BoxCollider>();
                if (_zoneCollider == null) return false;
            }

            return _zoneCollider.bounds.Contains(position);
        }

        #endregion

        #region Internal Logic

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_zoneCollider == null)
            {
                _zoneCollider = GetComponent<BoxCollider>();
                if (_zoneCollider == null) return;
            }

            Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
            Gizmos.DrawWireCube(
                _zoneCollider.bounds.center,
                _zoneCollider.bounds.size
            );
        }
#endif

        #endregion
    }
}

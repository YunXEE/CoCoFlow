using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace CoCoFlow.Runtime.Gameplay.Enemy.Sensors
{
    /// <summary>
    /// 敌人视觉传感器 —— 使用 Physics.OverlapSphereNonAlloc + Physics.Raycast + FOV 过滤检测目标。
    /// 继承自 EnemySensorBase，由基类的 UniTask 异步轮询循环驱动，
    /// 检测到目标后写入 Blackboard（CurrentTarget / IsTargetVisible / LastKnownPosition）。
    /// </summary>
    public class EnemySensorVision : EnemySensorBase
    {
        // ──────────────────────────── 缓存引用 ────────────────────────────
        private Transform _cachedTransform;
        private Transform _playerTarget;
        private int _playerLayerMask;
        private int _combinedLayerMask;

        // ──────────────────────────── 预分配缓冲区（零 GC） ────────────────────────────
        private readonly Collider[] _hitBuffer = new Collider[32];

        // ──────────────────────────── Unity 生命周期 ────────────────────────────

        private void Awake()
        {
            _cachedTransform = transform;
            _playerLayerMask = LayerMask.GetMask("Player");
            if (_playerLayerMask == -1) _playerLayerMask = 1 << 6; // Fallback to layer 6 if "Player" not found
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Controller != null && Controller.Config != null)
            {
                _combinedLayerMask = _playerLayerMask | Controller.Config.ObstacleLayerMask;
            }
            else
            {
                _combinedLayerMask = _playerLayerMask;
            }
        }

        // ──────────────────────────── 调试可视化 ────────────────────────────

        private void OnDrawGizmosSelected()
        {
            if (Controller == null || Controller.Config == null) return;

            // Aggro 半径球体
            Gizmos.color = new Color(0, 1, 0, 0.1f);
            Gizmos.DrawWireSphere(transform.position, Controller.Config.AggroRadius);

            // FOV 锥形可视化
            float halfFov = Controller.Config.FieldOfView * 0.5f;
            Vector3 forward = transform.forward;
            Quaternion leftRay = Quaternion.AngleAxis(-halfFov, Vector3.up);
            Quaternion rightRay = Quaternion.AngleAxis(halfFov, Vector3.up);
            Vector3 leftDir = leftRay * forward * Controller.Config.AggroRadius;
            Vector3 rightDir = rightRay * forward * Controller.Config.AggroRadius;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, leftDir);
            Gizmos.DrawRay(transform.position, rightDir);
        }

        #region Internal Logic

        /// <summary>
        /// 执行一帧视觉检测 —— 先检查当前目标是否仍可见，
        /// 若无可对附近所有 Player 物体做 OverlapSphere → FOV过滤 → Raycast穿墙检测。
        /// </summary>
        protected override async UniTask PerformDetectionAsync(CancellationToken ct)
        {
            // 提前退出：取消令牌已触发
            if (ct.IsCancellationRequested) return;

            EnemyController.EnemyBlackboard bb = Controller.Blackboard;

            // ── 情况 A：已有当前目标，验证是否仍然可见 ──
            if (bb.currentTarget != null)
            {
                Vector3 dirToTarget = bb.currentTarget.position - _cachedTransform.position;
                float distToTarget = dirToTarget.magnitude;

                if (distToTarget <= Controller.Config.AggroRadius)
                {
                    // 检查 FOV
                    float angle = Vector3.Angle(_cachedTransform.forward, dirToTarget.normalized);
                    if (angle < Controller.Config.FieldOfView * 0.5f)
                    {
                        // 射线检测穿墙遮挡
                        if (Physics.Raycast(_cachedTransform.position, dirToTarget.normalized, out RaycastHit hit,
                                Controller.Config.AggroRadius, _combinedLayerMask))
                        {
                            if (hit.transform == bb.currentTarget)
                            {
                                // 目标可见 → 更新最后已知位置，保持锁定
                                bb.lastKnownPosition = bb.currentTarget.position;
                                bb.isTargetVisible = true;
                                return;
                            }
                        }
                    }
                }

                // 被遮挡、超出范围或超出视野 → 目标丢失视线（但不一定清除 CurrentTarget，取决于逻辑要求）
                // 在本框架中，一旦不可见，SubState_Chase 会切入 SubState_Investigate
                bb.isTargetVisible = false;
                OnTargetLost();
                // 注意：这里保留 CurrentTarget，让状态机决定何时清除它（通常在 Investigate 失败后）
                return;
            }

            // ── 情况 B：无当前目标，扫描周围玩家 ──
            int hitCount = Physics.OverlapSphereNonAlloc(
                _cachedTransform.position,
                Controller.Config.AggroRadius,
                _hitBuffer,
                _playerLayerMask);

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _hitBuffer[i];
                if (col == null) continue;

                Vector3 dirToTarget = (col.transform.position - _cachedTransform.position).normalized;
                float angle = Vector3.Angle(_cachedTransform.forward, dirToTarget);

                // FOV 半角过滤
                if (angle < Controller.Config.FieldOfView * 0.5f)
                {
                    // 射线检测：确认无遮挡
                    if (Physics.Raycast(_cachedTransform.position, dirToTarget, out RaycastHit hit,
                            Controller.Config.AggroRadius, _combinedLayerMask))
                    {
                        if (hit.transform == col.transform)
                        {
                            // 发现目标！
                            bb.currentTarget = col.transform;
                            bb.isTargetVisible = true;
                            bb.lastKnownPosition = col.transform.position;
                            OnTargetDetected(col.transform);
                            break;
                        }
                    }
                }
            }

            // 委托给 UniTask 调度器，避免阻塞主线程
            // （检测逻辑本身是同步的，但保留 async 以支持未来扩展）
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        #endregion
    }
}

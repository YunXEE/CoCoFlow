using UnityEngine;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

namespace CoCoFlow.Runtime.Gameplay.Enemy.Sensors
{
    /// <summary>
    /// 敌人传感器抽象基类 —— 提供基于 UniTask 的异步轮询循环。
    /// 子类实现 PerformDetectionAsync 以执行具体的检测逻辑（视觉、听觉等），
    /// 并通过 OnTargetDetected / OnTargetLost 钩子响应检测结果。
    /// </summary>
    public abstract class EnemySensorBase : MonoBehaviour
    {
        protected EnemyController Controller;

        private CancellationTokenSource _cts;
        private float _interval;

        protected virtual void OnEnable()
        {
            Controller = GetComponentInParent<EnemyController>();
            if (Controller == null)
            {
                CoCoLog.Error($"EnemySensorBase: 在 {gameObject.name} 或其父物体上找不到 EnemyController！传感器将停止工作。");
                enabled = false;
                return;
            }
            _interval = Controller.Config.SensorPollInterval;
            _cts = new CancellationTokenSource();
            PollLoop(_cts.Token).Forget();
        }

        protected virtual void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        #region Public API

        #endregion

        #region Internal Logic

        /// <summary>
        /// 执行一次检测逻辑，由轮询循环在每帧间隔后调用。
        /// 子类在此实现具体的感知逻辑（射线检测、重叠球等）。
        /// </summary>
        /// <param name="ct">取消令牌，检测逻辑应检查此令牌以支持提前退出</param>
        protected abstract UniTask PerformDetectionAsync(CancellationToken ct);

        /// <summary>
        /// 目标被首次检测到时触发。子类重写以写入 Blackboard 或切换状态。
        /// </summary>
        /// <param name="target">检测到的目标 Transform</param>
        protected virtual void OnTargetDetected(Transform target) { }

        /// <summary>
        /// 目标丢失时触发。子类重写以清空 Blackboard 或切换到搜索/巡逻状态。
        /// </summary>
        protected virtual void OnTargetLost() { }

        /// <summary>
        /// 异步轮询主循环 —— 以 SensorPollInterval 为间隔持续执行检测，
        /// 通过 CancellationToken 实现干净地停止与销毁。
        /// </summary>
        private async UniTaskVoid PollLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await PerformDetectionAsync(ct);
                await UniTask.Delay(TimeSpan.FromSeconds(_interval), cancellationToken: ct);
            }
        }

        #endregion
    }
}

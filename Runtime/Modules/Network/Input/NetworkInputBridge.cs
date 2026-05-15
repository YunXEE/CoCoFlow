using Fusion;
using UnityEngine;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Network.Input
{
    /// <summary>
    /// 输入桥接器 — 连接本地 InputReader 与 Fusion 网络输入系统。
    /// 每帧 Accumulate 输入，在 Fusion OnInput 回调中原子消费，解决 Unity 帧率
    /// （≥60fps）与 Fusion Tick（默认30Hz）之间的输入丢失问题。
    /// 通过实现 IInputStateProvider 覆盖 InputReader 的 CoCoServices 注册，
    /// 使网络模式下所有下游消费者自动获取网络化输入。
    /// </summary>
    [DefaultExecutionOrder(-90)] // 在 InputReader (-100) 之后，Fusion OnInput 之前
    public class NetworkInputBridge : MonoBehaviour, IInputStateProvider
    {
        [SerializeField] private bool _accumulateLookInput;

        private IInputStateProvider _localInput;
        private IInputEventSource _inputEvents;
        private NetPlayerInput _accumulatedInput;

        private void Awake()
        {
            CoCoServices.WaitFor<IInputStateProvider>(svc => _localInput = svc);
            CoCoServices.WaitFor<IInputEventSource>(svc =>
            {
                _inputEvents = svc;
                _inputEvents.OnActionPerformed += HandleAction;
            });

            // 覆盖 IInputStateProvider 注册，使网络模式下消费者自动获取网络化输入
            CoCoServices.Register<IInputStateProvider>(this);
        }

        private void OnDestroy()
        {
            // 清理事件订阅，避免 EventSource 持有已销毁对象的引用
            if (_inputEvents != null)
            {
                _inputEvents.OnActionPerformed -= HandleAction;
            }

            // CoCoServices 的 Register 是基于实例的，Destroy 时自动失效
        }

        /// <summary>
        /// 每帧累积输入。Fusion OnInput 回调通过 ConsumeAndReset() 原子消费。
        /// </summary>
        private void Update()
        {
            if (_localInput == null) return;

            _accumulatedInput.MoveDirection += _localInput.MoveInput;

            if (_accumulateLookInput)
            {
                _accumulatedInput.LookDirection += _localInput.LookInput;
            }
        }

        #region Public API

        /// <summary>
        /// 原子地返回并清空累积的输入，由 NetManager.OnInput 调用。
        /// </summary>
        public NetPlayerInput ConsumeAndReset()
        {
            var input = _accumulatedInput;
            _accumulatedInput = default;
            return input;
        }

        public Vector2 MoveInput => _localInput?.MoveInput ?? Vector2.zero;
        public Vector2 LookInput => _localInput?.LookInput ?? Vector2.zero;
        public Vector2 ZoomInput => Vector2.zero;

        #endregion

        #region Internal Logic

        /// <summary>
        /// 处理离散动作事件。使用 OR 累积确保 Fusion Tick 之间
        /// 的按键按下不会被漏掉（离散动作不会每帧持续触发）。
        /// </summary>
        private void HandleAction(string actionName)
        {
            switch (actionName)
            {
                case "Jump":
                    _accumulatedInput.Jump = true;
                    break;
                case "Interact":
                    _accumulatedInput.Interact = true;
                    break;
            }
        }

        #endregion
    }
}

using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlow.Runtime.Modules.Input
{
    public enum InputMapType
    {
        // 定义所有的Action Maps
        Player,
        UI,
        None
    }

    [DefaultExecutionOrder(-100)] // 保证早于 Camera/UI 的 Awake 完成注册，确保依赖输入的组件能正确获取服务
    public class InputReader : MonoBehaviour,
        IInputStateProvider, IInputEventSource, IInputModeController
    {
        private struct BufferedInput
        {
            public string ActionName;
            public float Timestamp;

            public void Clear()
            {
                ActionName = string.Empty;
                Timestamp = 0f;
            }

            public bool IsValid(float currentTime, float bufferWindow)
            {
                return !string.IsNullOrEmpty(ActionName) && (currentTime - Timestamp) <= bufferWindow;
            }
        }

        // --- 序列化字段与生命周期 ---

        [Header("Input Configuration")]
        [Tooltip("Add Input Asset here")]
        public InputActionAsset inputAsset;

        [Header("Input Buffering")]
        [Tooltip("Open Input Buffering")]
        [SerializeField] private bool isUsingInputBuffering = true;
        [Tooltip("Action Keeping Time")]
        [SerializeField] private float inputBufferTime = 0.2f;

        [SerializeField] private InputMapType defaultMapType = InputMapType.Player;

        private BufferedInput _currentBuffer;

        // 实现 IInputStateProvider
        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public Vector2 ZoomInput { get; private set; }

        private InputActionAsset _runtimeAsset;
        private InputActionMap _currentMap;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _zoomAction;

        // Events (实现 IInputEventSource)
        public event Action<string> OnActionPerformed;
        public event Action<string> OnActionCanceled;

        private void Awake()
        {
            if (inputAsset == null)
            {
                Debug.LogWarning("[InputReader] 无可用输入配置");
                return;
            }

            // 实例化 Asset 避免修改原始资源
            _runtimeAsset = Instantiate(inputAsset);

            SwitchActionMap(defaultMapType);
            _currentBuffer.Clear();

            // 注册到 Core ServiceLocator —— 一次性把三个接口都登记
            CoCoServices.Register<IInputStateProvider>(this);
            CoCoServices.Register<IInputEventSource>(this);
            CoCoServices.Register<IInputModeController>(this);
        }

        private void Update()
        {
            // 每帧轮询持续性输入（如摇杆/鼠标移动）
            if (_moveAction != null) MoveInput = _moveAction.ReadValue<Vector2>();
            if (_lookAction != null) LookInput = _lookAction.ReadValue<Vector2>();
            if (_zoomAction != null) ZoomInput = _zoomAction.ReadValue<Vector2>();

            // 检查缓冲区超时，自动清除过期输入
            if (isUsingInputBuffering &&
                !string.IsNullOrEmpty(_currentBuffer.ActionName) &&
                !_currentBuffer.IsValid(Time.time, inputBufferTime))
            {
                _currentBuffer.Clear();
            }
        }

        private void OnEnable() => _currentMap?.Enable();

        private void OnDisable()
        {
            _currentMap?.Disable();
            ClearBuffer();

            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            ZoomInput = Vector2.zero;
        }

        private void OnDestroy()
        {
            // 务必注销服务，防止 ServiceLocator 持有已销毁对象的引用
            CoCoServices.Unregister<IInputStateProvider>(this);
            CoCoServices.Unregister<IInputEventSource>(this);
            CoCoServices.Unregister<IInputModeController>(this);

            if (_currentMap != null)
            {
                UnbindCurrentMapActions();
            }

            if (_runtimeAsset != null)
            {
                Destroy(_runtimeAsset);
            }
        }

        #region Public API
        // ============================================================
        //  IInputModeController & IInputEventSource 实现
        // ============================================================

        /// <summary>
        /// 切换当前活跃的 Action Map（字符串版本，用于解耦）
        /// </summary>
        public void SwitchActionMap(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                Debug.LogError("[InputReader] SwitchActionMap 收到空 mapName");
                return;
            }

            if (Enum.TryParse<InputMapType>(mapName, ignoreCase: false, out var t))
            {
                SwitchActionMap(t);
            }
            else
            {
                Debug.LogError($"[InputReader] 未知 ActionMap 名: {mapName}");
            }
        }

        /// <summary>
        /// 切换当前活跃的 Action Map（强类型版本）
        /// </summary>
        public void SwitchActionMap(InputMapType newMapType)
        {
            if (_runtimeAsset == null) return;

            var newMapTypeName = newMapType.ToString();

            if (_currentMap != null && _currentMap.name == newMapTypeName) return;

            if (_currentMap != null)
            {
                UnbindCurrentMapActions();
                _currentMap.Disable();
                _currentBuffer.Clear();

                MoveInput = Vector2.zero;
                LookInput = Vector2.zero;
                ZoomInput = Vector2.zero;
            }

            if (newMapType == InputMapType.None)
            {
                _currentMap = null;
                Debug.Log("[InputReader] 已切断所有输入 (None 状态)");
                return;
            }

            _currentMap = _runtimeAsset.FindActionMap(newMapTypeName);
            if (_currentMap == null)
            {
                Debug.LogError($"[InputReader] 找不到名为 {newMapTypeName} 的 Action Map！");
                return;
            }

            BindCurrentMapActions();

            if (this.isActiveAndEnabled)
            {
                _currentMap.Enable();
            }

            CoCoLog.Log($"成功切换到 Action Map: {newMapTypeName}");
            Debug.Log($"[InputReader] 成功切换到 Action Map: {newMapTypeName}");
        }

        /// <summary>
        /// 尝试消耗缓冲区中的输入（如果匹配且未超时）
        /// </summary>
        public bool TryConsumeBufferedAction(string targetActionName)
        {
            if (!isUsingInputBuffering) return false;

            if (_currentBuffer.ActionName == targetActionName &&
                _currentBuffer.IsValid(Time.time, inputBufferTime))
            {
                _currentBuffer.Clear();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 清除当前所有缓冲输入
        /// </summary>
        public void ClearBuffer() => _currentBuffer.Clear();

        #endregion

        #region Internal Logic

        private void BindCurrentMapActions()
        {
            _moveAction = null;
            _lookAction = null;
            _zoomAction = null;

            foreach (var action in _currentMap.actions)
            {
                // Vector2 类型通常用于持续性输入，通过 ReadValue 读取
                if (action.expectedControlType == "Vector2")
                {
                    switch (action.name)
                    {
                        case "Move": _moveAction = action; break;
                        case "Look": _lookAction = action; break;
                        case "Zoom": _zoomAction = action; break;
                    }
                }
                else
                {
                    // 其他类型（如 Button）通过事件驱动
                    action.performed += HandleActionPerformed;
                    action.canceled += HandleActionCanceled;
                }
            }
        }

        private void UnbindCurrentMapActions()
        {
            if (_currentMap == null) return;

            foreach (var action in _currentMap.actions)
            {
                if (action.expectedControlType != "Vector2")
                {
                    action.performed -= HandleActionPerformed;
                    action.canceled -= HandleActionCanceled;
                }
            }
        }

        private void HandleActionPerformed(InputAction.CallbackContext ctx)
        {
            var actionName = ctx.action.name;

            if (isUsingInputBuffering)
            {
                _currentBuffer.ActionName = actionName;
                _currentBuffer.Timestamp = Time.time;
            }

            OnActionPerformed?.Invoke(actionName);
        }

        private void HandleActionCanceled(InputAction.CallbackContext ctx)
            => OnActionCanceled?.Invoke(ctx.action.name);

        #endregion
    }
}



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

    public class PlayerInputReader : MonoBehaviour
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

            // 检查当前缓冲是否有效
            public bool IsValid(float currentTime, float bufferWindow)
            {
                return !string.IsNullOrEmpty(ActionName) && (currentTime - Timestamp) <= bufferWindow;
            }
        }

        //Events
        public event Action<string> OnActionPerformed;
        public event Action<string> OnActionCanceled;

        [Header("Input Configuration")]
        [Tooltip("Add Input Asset here")]
        public InputActionAsset inputAsset;

        [Header("Input Buffering")]
        [Tooltip("Open Input Buffering")]
        [SerializeField]private bool isUsingInputBuffering = true;
        [Tooltip("Action Keeping Time")]
        [SerializeField]private float inputBufferTime = 0.2f;

        private BufferedInput _currentBuffer;

        [SerializeField]private InputMapType defaultMapType = InputMapType.Player;

        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public Vector2 ZoomInput { get; private set; }

        private InputActionAsset _runtimeAsset;
        private InputActionMap _currentMap;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _zoomAction;

        private void Awake()
        {
            if (inputAsset == null)
            {
                Debug.LogWarning("无可用输入配置");
                return;
            }

            _runtimeAsset = Instantiate(inputAsset);

            SwitchActionMap(defaultMapType);
            _currentBuffer.Clear();
        }

        private void Update()
        {
            if (_moveAction != null) MoveInput = _moveAction.ReadValue<Vector2>();
            if (_lookAction != null) LookInput = _lookAction.ReadValue<Vector2>();
            if (_zoomAction != null) ZoomInput = _zoomAction.ReadValue<Vector2>();

            if (isUsingInputBuffering &&
                !string.IsNullOrEmpty(_currentBuffer.ActionName) &&
                !_currentBuffer.IsValid(Time.time, inputBufferTime))
            {
                _currentBuffer.Clear();
            }
        }

        public void SwitchActionMap(InputMapType newMapType)
        {
            if (_runtimeAsset == null) return;

            var newMapTypeName = newMapType.ToString();

            // 重复调用则跳过检查
            if (_currentMap != null && _currentMap.name == newMapTypeName)
            {
                return;
            }

            // 解绑Map
            if (_currentMap != null)
            {
                UnbindCurrentMapActions();
                _currentMap.Disable();
                _currentBuffer.Clear();

                MoveInput = Vector2.zero;
                LookInput = Vector2.zero;
                ZoomInput = Vector2.zero;
            }

            // 切断输入
            if (newMapType == InputMapType.None)
            {
                _currentMap = null;
                Debug.Log("[InputReader] 已切断所有输入 (None 状态)");
                return;
            }

            // 查找新的 Map
            _currentMap = _runtimeAsset.FindActionMap(newMapTypeName);
            if (_currentMap == null)
            {
                Debug.LogError($"找不到名为 {newMapTypeName} 的 Action Map！");
                return;
            }

            // 重新绑定 Action
            BindCurrentMapActions();

            if (this.isActiveAndEnabled)
            {
                _currentMap.Enable();
            }


            CoCoLog.Log($"成功切换到 Action Map: {newMapTypeName}");
            Debug.Log($"[InputReader] 成功切换到 Action Map: {newMapTypeName}");
        }

        private void BindCurrentMapActions()
        {
            _moveAction = null;
            _lookAction = null;
            _zoomAction = null;

            foreach (var action in _currentMap.actions)
            {
                if (action.expectedControlType == "Vector2")
                {
                    switch (action.name)
                    {
                        case  "Move":
                            _moveAction = action;
                            break;
                        case   "Look":
                            _lookAction = action;
                            break;
                        case   "Zoom":
                            _zoomAction = action;
                            break;
                    }
                }
                else
                {
                    // 只有离散的 Button 按钮，才会绑定事件
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

        // 手动清除Buffer
        public void ClearBuffer()
        {
            _currentBuffer.Clear();
        }

        private void OnEnable()
        {
            _currentMap?.Enable();
        }
        private void OnDisable()
        {
            _currentMap?.Disable();
            ClearBuffer();

            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            ZoomInput = Vector2.zero;
        }

        private void HandleActionPerformed(InputAction.CallbackContext ctx)
        {
            var actionName = ctx.action.name;

            // 记录最新缓冲按键
            if (isUsingInputBuffering)
            {
                _currentBuffer.ActionName = actionName;
                _currentBuffer.Timestamp = Time.time;
            }

            // 抛出事件
            OnActionPerformed?.Invoke(actionName);
        }
        private void HandleActionCanceled(InputAction.CallbackContext ctx) => OnActionCanceled?.Invoke(ctx.action.name);

        private void OnDestroy()
        {
            if (_currentMap != null)
            {
                UnbindCurrentMapActions();
            }

            if (_runtimeAsset != null)
            {
                Destroy(_runtimeAsset);
            }

        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace CoCoFlow.Runtime.Modules.UI.Widgets
{
    // 定义按钮的内置行为类型
    public enum UIButtonActionType
    {
        None,
        OpenPanel,           // 打开新面板
        CloseCurrentPanel,   // 关闭当前面板
        CustomGameLogic      // 预留给具体的游戏逻辑（如恢复时间、退出游戏）
    }

    [RequireComponent(typeof(Button))]
    public class UIWidgetButton : UIWidgetBase
    {
        [Header("Action Routing")]
        [SerializeField] private UIButtonActionType actionType = UIButtonActionType.CustomGameLogic;
        [SerializeField] private string targetPanelAddress;

        [Header("Custom Logic")]
        [SerializeField] private UnityEvent onCustomClick;

        private Button _button;

        protected override void Awake()
        {
            base.Awake();
            _button = GetComponent<Button>();
            if (_button == null)
            {
                Debug.LogError($"[UI 框架规范错误] Widget {gameObject.name} 未找到 Button 组件！", gameObject);
                return;
            }

            _button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(HandleClick);
        }

        #region Public API
        public override void ResetState()
        {
            // 按钮专属的状态重置逻辑（如果有的话）
        }
        #endregion

        #region Internal Logic
        private void HandleClick()
        {
            if (!IsInteractable) return;

            switch (actionType)
            {
                case UIButtonActionType.OpenPanel:
                    if (!string.IsNullOrEmpty(targetPanelAddress))
                    {
                        UIManager uiManager = UIManager.Instance;
                        if (uiManager == null)
                        {
                            Debug.LogWarning($"[UI 框架规范错误] Widget {gameObject.name} 执行 OpenPanel 时未找到 UIManager！", gameObject);
                            return;
                        }

                        uiManager.OpenPanel(targetPanelAddress);
                    }
                    break;

                case UIButtonActionType.CloseCurrentPanel:
                    UIManager currentManager = UIManager.Instance;
                    if (currentManager == null)
                    {
                        Debug.LogWarning($"[UI 框架规范错误] Widget {gameObject.name} 执行 CloseCurrentPanel 时未找到 UIManager！", gameObject);
                        return;
                    }

                    currentManager.CloseCurrentPanel();
                    break;

                case UIButtonActionType.CustomGameLogic:
                    onCustomClick?.Invoke();
                    break;
            }
        }
        #endregion
    }
}

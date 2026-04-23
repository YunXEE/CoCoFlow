using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using CoCoFlow.Runtime.Core;

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
        public UIButtonActionType actionType = UIButtonActionType.CustomGameLogic;

        // 仅在 actionType == OpenPanel 时，在 Inspector 中显示
        public string targetPanelAddress;

        // 仅在 actionType == CustomGameLogic 时，在 Inspector 中显示
        [Header("Custom Logic")]
        public UnityEvent onCustomClick;

        private Button _button;

        protected override void Awake()
        {
            base.Awake();
            _button = GetComponent<Button>();
            _button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            if (!IsInteractable) return;

            // 路由分配
            switch (actionType)
            {
                case UIButtonActionType.OpenPanel:
                    if (!string.IsNullOrEmpty(targetPanelAddress))
                    {
                        // 假设 UIManager 是单例，或者你通过 EventAgent 发送全局事件
                        UIManager.Instance.OpenPanel(targetPanelAddress);
                    }
                    break;

                case UIButtonActionType.CloseCurrentPanel:
                    UIManager.Instance.CloseCurrentPanel();
                    break;

                case UIButtonActionType.CustomGameLogic:
                    // 将控制权交还给 Inspector 里的 UnityEvent，用于挂载比如 GameMode.ResumeGame()
                    onCustomClick?.Invoke();
                    break;
            }
        }

        public override void ResetState()
        {
            // 按钮专属的状态重置逻辑
        }
    }
}

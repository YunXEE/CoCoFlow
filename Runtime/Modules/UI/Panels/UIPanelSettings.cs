using UnityEngine;
// 如果你的 UIPanelBase 不在 Panels 命名空间，请确保 using 了正确的命名空间
// using CoCoFlow.Runtime.Modules.UI;

namespace CoCoFlow.Runtime.Modules.UI.Panels
{
    public class UIPanelSettings : UIPanelBase
    {
        protected override void Awake()
        {
            // 必须保留，UIPanelBase 需要在这里获取 CanvasGroup 和 RectTransform
            base.Awake();
        }

        protected override void OnBeforeShow()
        {
            base.OnBeforeShow();
            // 仅仅打印一下，方便你在控制台看生命周期流转
            Debug.Log("[CoCoFrame] Settings 面板: 准备入场...");
        }

        protected override void OnAfterHide()
        {
            base.OnAfterHide();
            Debug.Log("[CoCoFrame] Settings 面板: 已经退场并销毁！");
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CocoFlow.Runtime.Modules.UI
{
    public class UIManager : MonoBehaviour
    {
        // 场景中预设的各层级挂载点
        [SerializeField] private Transform hudRoot;
        [SerializeField] private Transform panelRoot;
        [SerializeField] private Transform popupRoot;

        // 面板注册表：存放所有 Prefab 或已实例化的面板
        private Dictionary<string, UIPanelBase> panelRegistry = new Dictionary<string, UIPanelBase>();
        
        // 核心跳转容器：栈
        private Stack<UIPanelBase> panelStack = new Stack<UIPanelBase>();

        public void RegisterPanel(UIPanelBase panel)
        {
            if (!panelRegistry.ContainsKey(panel.PanelId))
            {
                panelRegistry.Add(panel.PanelId, panel);
            }
        }

        /// <summary>
        /// 压栈：打开新面板
        /// </summary>
        public void PushPanel(string panelId)
        {
            if (!panelRegistry.TryGetValue(panelId, out UIPanelBase newPanel))
            {
                Debug.LogError($"[UIManager] 找不到面板: {panelId}");
                return;
            }

            // 处理当前栈顶面板
            if (panelStack.Count > 0)
            {
                var topPanel = panelStack.Peek();
                if (newPanel.HideLowerPanels)
                {
                    // 将它暂停/隐藏，但不销毁
                    //topPanel.canvasGroup.interactable = false;
                    // 这里可以调用一个不带动画的瞬隐，或者播放一个退到背景的动画
                }
            }

            // 新面板入栈并展示
            panelStack.Push(newPanel);
            
            // 自动挂载到对应的层级节点
            Transform targetRoot = panelRoot;
            if (newPanel.Layer == UILayer.HUD) targetRoot = hudRoot;
            else if (newPanel.Layer == UILayer.Popup) targetRoot = popupRoot;
            
            newPanel.transform.SetParent(targetRoot, false);
            newPanel.transform.SetAsLastSibling(); // 保证在最上层显示
            
            newPanel.Show();
        }

        /// <summary>
        /// 出栈：关闭当前面板，返回上一页
        /// </summary>
        public void PopPanel()
        {
            if (panelStack.Count <= 0) return;

            var currentPanel = panelStack.Pop();
            currentPanel.Hide();

            // 恢复下一层面板
            if (panelStack.Count > 0)
            {
                var lowerPanel = panelStack.Peek();
                if (currentPanel.HideLowerPanels)
                {
                    // 恢复其交互和显示
                    //lowerPanel.canvasGroup.interactable = true;
                }
            }
        }
    }
}
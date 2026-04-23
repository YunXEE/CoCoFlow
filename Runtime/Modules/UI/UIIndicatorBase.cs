using UnityEngine;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.UI
{
    /// <summary>
    /// 指示器基类：用于场景血条、体力条、交互提示词等不参与栈管理的独立 UI
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIIndicatorBase : MonoBehaviour
    {
        protected CanvasGroup CanvasGroup;
        protected EventAgent EventAgent = new EventAgent();

        // 供 Editor 窗口抓取状态使用
        public string LastReceivedDataLog { get; protected set; } = "None";

        protected virtual void Awake()
        {
            CanvasGroup = GetComponent<CanvasGroup>();
        }

        protected virtual void OnEnable()
        {
            // 每次激活时重新绑定事件，兼容未来的对象池
            BindEvents();
        }

        protected virtual void OnDisable()
        {
            // 隐藏或回收时，清理事件，防止后台继续消耗性能
            EventAgent.UnsubscribeAll();
            LastReceivedDataLog = "Disabled";
        }

        protected virtual void OnDestroy()
        {
            EventAgent.UnsubscribeAll();
        }

        /// <summary>
        /// 子类必须实现此方法来订阅自己关心的事件
        /// </summary>
        protected abstract void BindEvents();

        // 提供给子类快速更新 Editor 监控日志的工具
        protected void LogEventData(string dataStr)
        {
#if UNITY_EDITOR
            LastReceivedDataLog = $"[{System.DateTime.Now:HH:mm:ss}] {dataStr}";
#endif
        }
    }
}

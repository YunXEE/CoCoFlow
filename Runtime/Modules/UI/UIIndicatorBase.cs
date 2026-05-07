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
        private CanvasGroup _canvasGroup;
        private readonly EventAgent _eventAgent = new EventAgent();

        public string LastReceivedDataLog { get; private set; } = "None";

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        protected virtual void OnEnable()
        {
            BindEvents();
        }

        protected virtual void OnDisable()
        {
            _eventAgent.UnsubscribeAll();
            LastReceivedDataLog = "Disabled";
        }

        protected virtual void OnDestroy()
        {
            _eventAgent.UnsubscribeAll();
        }

        #region Internal Logic
        /// <summary>
        /// 子类必须实现此方法来订阅自己关心的事件
        /// </summary>
        protected abstract void BindEvents();

        protected void LogEventData(string dataStr)
        {
#if UNITY_EDITOR
            LastReceivedDataLog = $"[{System.DateTime.Now:HH:mm:ss}] {dataStr}";
#endif
        }
        #endregion
    }
}

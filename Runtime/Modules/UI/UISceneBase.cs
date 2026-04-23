using UnityEngine;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.UI
{
    /// <summary>
    /// 世界 3D UI 容器基类（不参与 Stack 栈管理）。
    /// 通常挂载在 Canvas (World Space) 下。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UISceneBase : MonoBehaviour
    {
        protected CanvasGroup CanvasGroup;
        protected EventAgent EventAgent = new EventAgent();

        // 供 Editor 监控使用
        public string SceneUIName => gameObject.name;
        public bool IsActive => gameObject.activeInHierarchy;

        protected virtual void Awake()
        {
            CanvasGroup = GetComponent<CanvasGroup>();
        }

        protected virtual void OnEnable()
        {
            BindEvents();
        }

        protected virtual void OnDisable()
        {
            EventAgent.UnsubscribeAll();
        }

        protected virtual void OnDestroy()
        {
            EventAgent.UnsubscribeAll();
        }

        /// <summary>
        /// 绑定该 World UI 关心的事件
        /// </summary>
        protected abstract void BindEvents();

    }
}

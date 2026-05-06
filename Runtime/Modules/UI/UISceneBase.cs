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
        private CanvasGroup _canvasGroup;
        private readonly EventAgent _eventAgent = new EventAgent();

        public string SceneUIName => gameObject.name;
        public bool IsActive => gameObject.activeInHierarchy;

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
        }

        protected virtual void OnDestroy()
        {
            _eventAgent.UnsubscribeAll();
        }

        #region Internal Logic
        /// <summary>
        /// 绑定该 World UI 关心的事件，由子类实现
        /// </summary>
        protected abstract void BindEvents();
        #endregion
    }
}

using UnityEngine;

namespace CoCoFlow.Runtime.Modules.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIWidgetBase : MonoBehaviour
    {
        private CanvasGroup _canvasGroup;

        public UIPanelBase OwnerPanel { get; private set; }
        public UISceneBase OwnerSceneUI { get; private set; }

        public bool IsInteractable => _canvasGroup != null && _canvasGroup.interactable;

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();

            // 自动向上查找宿主，确保 Widget 处于合规的层级
            OwnerPanel = GetComponentInParent<UIPanelBase>(true);
            OwnerSceneUI = GetComponentInParent<UISceneBase>(true);

            if (OwnerPanel == null && OwnerSceneUI == null)
            {
                Debug.LogError($"[UI 框架规范错误] Widget {gameObject.name} 必须挂载在 UIPanelBase 或 UISceneBase 节点下！", gameObject);
            }
        }

        protected virtual void OnEnable()
        {
            ResetState();
        }

        #region Public API
        /// <summary>
        /// 设置组件的交互状态，并自动处理 Alpha 表现
        /// </summary>
        public virtual void SetInteractable(bool isInteractable)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.interactable = isInteractable;
            _canvasGroup.blocksRaycasts = isInteractable;
            _canvasGroup.alpha = isInteractable ? 1f : 0.5f;
        }

        /// <summary>
        /// 重置组件状态（如清空输入框、恢复按钮默认状态）
        /// </summary>
        public abstract void ResetState();
        #endregion
    }
}

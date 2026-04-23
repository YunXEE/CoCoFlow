using UnityEngine;

namespace CoCoFlow.Runtime.Modules.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIWidgetBase : MonoBehaviour
    {
        protected CanvasGroup CanvasGroup;

        // 核心：Widget 的宿主（要么是 Panel，要么是 SceneUI）
        public UIPanelBase OwnerPanel { get; private set; }
        public UISceneBase OwnerSceneUI { get; private set; }

        public bool IsInteractable => CanvasGroup.interactable;

        protected virtual void Awake()
        {
            CanvasGroup = GetComponent<CanvasGroup>();

            // 1. 强制约束：自动向上查找宿主
            OwnerPanel = GetComponentInParent<UIPanelBase>(true);
            OwnerSceneUI = GetComponentInParent<UISceneBase>(true);

            if (OwnerPanel == null && OwnerSceneUI == null)
            {
                Debug.LogError($"[UI 框架规范错误] Widget {gameObject.name} 必须放在 UIPanelBase 或 UISceneBase 下面！", gameObject);
            }
        }

        // 生命周期挂钩：当宿主显示/隐藏时，可能需要重置 Widget 状态
        protected virtual void OnEnable() { ResetState(); }

        public virtual void SetInteractable(bool interactable)
        {
            if (CanvasGroup == null) return;
            CanvasGroup.interactable = interactable;
            CanvasGroup.blocksRaycasts = interactable;
            CanvasGroup.alpha = interactable ? 1f : 0.5f;
        }

        public abstract void ResetState();
    }
}

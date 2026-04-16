using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks; // 引入 UniTask
using CoCoFlow.Runtime.Core;
using UnityEngine.Serialization; // 引入你的事件总线

namespace CoCoFlow.Runtime.Modules.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        [FormerlySerializedAs("PanelAddress")] [Header("UI Routing")]
        public string panelAddress; // 替换 PanelId 为 Addressable 地址
        public UILayer layer = UILayer.Panel;
        public bool hideLowerPanels = true;

        [Header("DOTween Animation Config")]
        public float animDuration = 0.3f;
        public AnimationCurve showCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationCurve hideCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        protected CanvasGroup CanvasGroup;
        protected RectTransform RectTransform;

        // 核心注入：专属事件代理，面板销毁时自动退订！
        protected EventAgent EventAgent = new EventAgent();

        protected virtual void Awake()
        {
            CanvasGroup = GetComponent<CanvasGroup>();
            RectTransform = GetComponent<RectTransform>();

            CanvasGroup.alpha = 0f;
            CanvasGroup.interactable = false;
            CanvasGroup.blocksRaycasts = false;
        }

        // ================== 生命周期改写为 Async ==================

        public virtual async UniTask ShowAsync()
        {
            gameObject.SetActive(true);
            CanvasGroup.blocksRaycasts = true;

            OnBeforeShow();

            transform.DOKill();
            CanvasGroup.DOKill();
            transform.localScale = Vector3.one * 0.8f;

            // 使用 UniTask.WhenAll 并发等待 DOTween 动画结束，极其优雅
            await UniTask.WhenAll(
                transform.DOScale(Vector3.one, animDuration).SetEase(showCurve).ToUniTask(),
                CanvasGroup.DOFade(1f, animDuration * 0.8f).ToUniTask()
            );

            CanvasGroup.interactable = true;
        }

        public virtual async UniTask HideAsync()
        {
            CanvasGroup.interactable = false;
            CanvasGroup.blocksRaycasts = false;

            transform.DOKill();
            CanvasGroup.DOKill();

            await UniTask.WhenAll(
                transform.DOScale(Vector3.one * 0.8f, animDuration).SetEase(hideCurve).ToUniTask(),
                CanvasGroup.DOFade(0f, animDuration).ToUniTask()
            );

            OnAfterHide();
            gameObject.SetActive(false);
        }

        protected virtual void OnBeforeShow() { }
        protected virtual void OnAfterHide() { }

        // 终极保险：销毁时清理事件
        protected virtual void OnDestroy()
        {
            EventAgent.UnsubscribeAll();
        }
        /// <summary>
        /// 供外部（如 UIManager）动态控制面板的交互状态
        /// </summary>
        public void SetInteractable(bool isInteractable)
        {
            if (CanvasGroup != null)
            {
                CanvasGroup.interactable = isInteractable;
            }
        }
    }
}

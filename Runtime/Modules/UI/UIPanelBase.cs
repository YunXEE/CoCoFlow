using UnityEngine;
using DG.Tweening;
using System;

namespace CoCoFlow.Runtime.Modules.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        [Header("UI Routing")]
        public string PanelId;
        public UILayer Layer = UILayer.Panel;
        [Tooltip("打开时是否暂停/隐藏下层的面板？")]
        public bool HideLowerPanels = true;

        [Header("DOTween Animation Config")]
        public float animDuration = 0.3f;
        [Tooltip("入场动画贝塞尔曲线 (推荐使用过冲曲线实现弹性)")]
        public AnimationCurve showCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("退场动画贝塞尔曲线")]
        public AnimationCurve hideCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        protected CanvasGroup canvasGroup;
        protected RectTransform rectTransform;

        protected virtual void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            rectTransform = GetComponent<RectTransform>();
            
            // 初始化状态：隐藏并关闭交互
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        // ================== 生命周期 API ==================

        /// <summary>
        /// 面板被压入栈，准备展示
        /// </summary>
        public virtual void Show(Action onComplete = null)
        {
            gameObject.SetActive(true);
            canvasGroup.blocksRaycasts = true;

            // 你的自定义初始化逻辑 (比如清空列表，加载数据)
            OnBeforeShow();

            // 停止之前的动画，防止冲突
            transform.DOKill();
            canvasGroup.DOKill();

            // DOTween 弹性入场范例：缩放 + 透明度渐变
            transform.localScale = Vector3.one * 0.8f;
            
            Sequence seq = DOTween.Sequence();
            seq.Join(transform.DOScale(Vector3.one, animDuration).SetEase(showCurve));
            seq.Join(canvasGroup.DOFade(1f, animDuration * 0.8f)); // 透明度比缩放稍快一点完成
            
            seq.OnComplete(() => 
            {
                canvasGroup.interactable = true;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// 面板被弹出栈，或者被上层面板遮挡
        /// </summary>
        public virtual void Hide(Action onComplete = null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            transform.DOKill();
            canvasGroup.DOKill();

            Sequence seq = DOTween.Sequence();
            seq.Join(transform.DOScale(Vector3.one * 0.8f, animDuration).SetEase(hideCurve));
            seq.Join(canvasGroup.DOFade(0f, animDuration));
            
            seq.OnComplete(() => 
            {
                OnAfterHide();
                gameObject.SetActive(false);
                onComplete?.Invoke();
            });
        }

        // 供子类重写的钩子函数
        protected virtual void OnBeforeShow() { }
        protected virtual void OnAfterHide() { }
    }
}
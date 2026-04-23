using System;
using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks; // 引入 UniTask
using CoCoFlow.Runtime.Core;
using UnityEngine.Serialization; // 引入你的事件总线

namespace CoCoFlow.Runtime.Modules.UI
{
    [Flags]
    public enum UIPanelConfig
    {
        None = 0,
        PauseGame = 1 << 0,       // 呼出时是否暂停游戏时间
        TakeInputFocus = 1 << 1,  // 是否剥夺玩家控制权，切换到 UI 输入 ActionMap
        HideLowerPanels = 1 << 2, // 是否隐藏底层的面板（你原来的 hideLowerPanels）
        ShowCursor = 1 << 3       // 呼出时是否解锁并显示鼠标（3D 冒险游戏刚需！）
    }

    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        [Header("UI Routing & Config")]
        public string panelAddress;
        public UILayer layer = UILayer.Panel;

        [Tooltip("在 Inspector 中自由组合这个面板的属性")]
        public UIPanelConfig config = UIPanelConfig.TakeInputFocus | UIPanelConfig.HideLowerPanels | UIPanelConfig.ShowCursor;

        [Header("DOTween Animation Config")]
        public float animDuration = 0.3f;
        public AnimationCurve showCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationCurve hideCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        protected CanvasGroup CanvasGroup;
        protected RectTransform RectTransform;
        protected EventAgent EventAgent = new EventAgent();

        protected virtual void Awake()
        {
            CanvasGroup = GetComponent<CanvasGroup>();
            RectTransform = GetComponent<RectTransform>();

            CanvasGroup.alpha = 0f;
            CanvasGroup.interactable = false;
            CanvasGroup.blocksRaycasts = false;
        }

        public virtual async UniTask ShowAsync()
        {
            gameObject.SetActive(true);
            CanvasGroup.blocksRaycasts = true;

            OnBeforeShow();

            transform.DOKill();
            CanvasGroup.DOKill();
            transform.localScale = Vector3.one * 0.8f;

            // 完美的防卡死动画等待
            await UniTask.WhenAll(
                transform.DOScale(Vector3.one, animDuration).SetEase(showCurve).SetUpdate(true).AwaitForComplete(),
                CanvasGroup.DOFade(1f, animDuration * 0.8f).SetUpdate(true).AwaitForComplete()
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
                transform.DOScale(Vector3.one * 0.8f, animDuration).SetEase(hideCurve).SetUpdate(true).AwaitForComplete(),
                CanvasGroup.DOFade(0f, animDuration).SetUpdate(true).AwaitForComplete()
            );

            OnAfterHide();
            gameObject.SetActive(false);
        }

        protected virtual void OnBeforeShow() { }
        protected virtual void OnAfterHide() { }

        protected virtual void OnDestroy()
        {
            EventAgent.UnsubscribeAll();
        }

        public void SetInteractable(bool isInteractable)
        {
            if (CanvasGroup != null) CanvasGroup.interactable = isInteractable;
        }
    }
}

using System;
using CoCoFlow.Runtime.Content;
using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks; // 引入 UniTask
using CoCoFlow.Runtime.Core;

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
        [SerializeField] private UILayer layer = UILayer.Panel;
        [SerializeField] private UIPanelConfig config = UIPanelConfig.TakeInputFocus | UIPanelConfig.HideLowerPanels | UIPanelConfig.ShowCursor;

        [Header("DOTween Animation Config")]
        [SerializeField] private float animDuration = 0.3f;
        [SerializeField] private AnimationCurve showCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private AnimationCurve hideCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;
        private readonly CoCoEventAgent _eventAgent = new CoCoEventAgent();
        private ContentId _sourceContentId;
        private bool _hasSourceOwnership;

        public ContentId SourceContentId => _sourceContentId;
        public UILayer Layer => layer;
        public UIPanelConfig Config => config;

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rectTransform = GetComponent<RectTransform>();

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        protected virtual void OnDestroy()
        {
            _eventAgent.UnsubscribeAll();
        }

        #region Public API
        /// <summary>
        /// 异步显示面板，并播放入场动画
        /// </summary>
        public virtual async UniTask ShowAsync()
        {
            gameObject.SetActive(true);
            _canvasGroup.blocksRaycasts = true;

            OnBeforeShow();

            transform.DOKill();
            _canvasGroup.DOKill();
            transform.localScale = Vector3.one * 0.8f;

            await UniTask.WhenAll(
                transform.DOScale(Vector3.one, animDuration).SetEase(showCurve).SetUpdate(true).AwaitForComplete(),
                _canvasGroup.DOFade(1f, animDuration * 0.8f).SetUpdate(true).AwaitForComplete()
            );

            _canvasGroup.interactable = true;
        }

        /// <summary>
        /// 异步隐藏面板，并播放离场动画
        /// </summary>
        public virtual async UniTask HideAsync()
        {
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            transform.DOKill();
            _canvasGroup.DOKill();

            await UniTask.WhenAll(
                transform.DOScale(Vector3.one * 0.8f, animDuration).SetEase(hideCurve).SetUpdate(true).AwaitForComplete(),
                _canvasGroup.DOFade(0f, animDuration).SetUpdate(true).AwaitForComplete()
            );

            OnAfterHide();
            gameObject.SetActive(false);
        }

        public void SetInteractable(bool isInteractable)
        {
            if (_canvasGroup != null) _canvasGroup.interactable = isInteractable;
        }

        internal void BindSourceOwnership(
            ContentId sourceContentId,
            ContentScope sourceScope,
            ContentLease<GameObject> sourceLease)
        {
            if (_hasSourceOwnership)
            {
                throw new InvalidOperationException("Panel source ownership is already bound.");
            }

            if (sourceScope == null) throw new ArgumentNullException(nameof(sourceScope));
            if (sourceLease == null) throw new ArgumentNullException(nameof(sourceLease));
            if (!sourceContentId.IsValid) throw new ArgumentException(
                "Panel source Content Id must be valid.",
                nameof(sourceContentId));
            if (sourceLease.Id != sourceContentId)
            {
                throw new ArgumentException(
                    "Panel source lease identity does not match the requested Content Id.",
                    nameof(sourceLease));
            }
            if (sourceLease.OwnerId != sourceScope.OwnerId)
            {
                throw new ArgumentException(
                    "Panel source Scope and Lease must have the same Content Owner Id.",
                    nameof(sourceLease));
            }

            var ownership = gameObject.AddComponent<UIPanelSourceOwnership>();
            ownership.Bind(sourceScope, sourceLease);
            _sourceContentId = sourceContentId;
            _hasSourceOwnership = true;
        }
        #endregion

        #region Internal Logic
        protected virtual void OnBeforeShow() { }
        protected virtual void OnAfterHide() { }
        #endregion
    }
}

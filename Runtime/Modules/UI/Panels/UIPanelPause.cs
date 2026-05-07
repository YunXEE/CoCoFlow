using CoCoFlow.Runtime.Core;
using UnityEngine;
using TMPro;

namespace CoCoFlow.Runtime.Modules.UI.Panels
{
    public class UIPanelPause : UIPanelBase
    {
        [Header("Pause Menu Setup")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private string customTitle = "PAUSED"; // 支持 Inspector 动态修改标题

        protected override void Awake()
        {
            base.Awake();

            if (titleText != null)
            {
                titleText.text = customTitle;
            }
        }

        protected override void OnBeforeShow()
        {
            base.OnBeforeShow();
            // 在这里处理暂停菜单独有的进场表现，例如：
            // - 播放一声沉闷的“嗡”音效
            // - 触发屏幕后处理的模糊效果 (通过 EventBus 广播)
        }

        protected override void OnAfterHide()
        {
            base.OnAfterHide();
            // 恢复后处理效果等
        }

        /// <summary>
        /// 这是唯一保留的纯业务逻辑。
        /// 供 Inspector 中 Quit 按钮的 UIButtonWidget (CustomGameLogic 模式) 调用。
        /// </summary>
        public void QuitGame()
        {
            Debug.Log("[CoCoFrame] Application Quit Requested.");
            CoCoLog.Log("非常好啊非常好");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

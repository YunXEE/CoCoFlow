using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Cysharp.Threading.Tasks;
using CoCoFlow.Runtime.Modules.Input;

namespace CoCoFlow.Runtime.Modules.UI
{
    public enum UILayer
    {
        Scene,      // 场景UI (如怪物血条，世界坐标转屏幕坐标)
        HUD,        // 常驻界面 (玩家血条，快捷栏，摇杆，永远在底层)
        Panel,      // 常规面板 (背包，设置，全屏/半屏，会遮挡HUD)
        Popup,      // 弹窗 (确认框，警告框)
        Top         // 顶层 (Loading界面，系统级断线提示)
    }

    public class UIManager : MonoBehaviour
    {
        // --- 新增：单例模式，方便 Widget 路由和全局调用 ---
        public static UIManager Instance { get; private set; }

        [Header("Root Transforms")]
        [SerializeField] private Transform hudRoot;
        [SerializeField] private Transform panelRoot;
        [SerializeField] private Transform popupRoot;

        [Header("Input Integration")]
        [SerializeField] private PlayerInputReader inputReader;
        public string pauseActionName = "Pause";
        public string cancelActionName = "Cancel";
        public string pausePanelAddress = "UI_PausePanel";

        private readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();
        private readonly Stack<UIPanelBase> _panelStack = new Stack<UIPanelBase>();
        private bool _isTransitioning;

        // 当前记录的暂停锁数量（防止多面板冲突）
        private int _pauseLockCount = 0;
        private int _cursorLockCount = 0;

        // --- 新增：初始化单例 ---
        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void OnEnable()
        {
            if (inputReader != null) inputReader.OnActionPerformed += HandleUIInput;
        }

        private void OnDisable()
        {
            if (inputReader != null) inputReader.OnActionPerformed -= HandleUIInput;
        }

        private void HandleUIInput(string actionName)
        {
            if (_isTransitioning) return;

            if (actionName == pauseActionName && _panelStack.Count == 0)
            {
                PushPanelAsync(pausePanelAddress).Forget();
            }
            else if (actionName == cancelActionName && _panelStack.Count > 0)
            {
                PopPanelAsync().Forget();
            }
        }

        // ================= 提供给 Inspector (如按钮 UnityEvent) 调用的同步包装器 =================
        public void OpenPanel(string address) => PushPanelAsync(address).Forget();
        public void CloseCurrentPanel() => PopPanelAsync().Forget();

        // --- 新增：关闭所有面板（例如直接从多层子菜单返回游戏）---
        public void CloseAllPanels() => PopAllPanelsAsync().Forget();

        // --- 新增：切换面板状态（如果当前最上层是它，就关掉；如果不是，就打开）---
        public void TogglePanel(string address)
        {
            if (_panelStack.Count > 0 && _panelStack.Peek().panelAddress == address)
            {
                CloseCurrentPanel();
            }
            else
            {
                OpenPanel(address);
            }
        }
        // =========================================================================================

        public async UniTask PushPanelAsync(string address)
        {
            if (_isTransitioning) return;
            _isTransitioning = true;

            try
            {
                if (!_prefabCache.TryGetValue(address, out GameObject prefab))
                {
                    prefab = await Addressables.LoadAssetAsync<GameObject>(address).ToUniTask();
                    _prefabCache.Add(address, prefab);
                }

                GameObject panelObj = Instantiate(prefab);
                UIPanelBase newPanel = panelObj.GetComponent<UIPanelBase>();

                Transform targetRoot = newPanel.layer switch
                {
                    UILayer.HUD => hudRoot,
                    UILayer.Popup => popupRoot,
                    _ => panelRoot
                };
                panelObj.transform.SetParent(targetRoot, false);
                panelObj.transform.SetAsLastSibling();

                // --- 1. 根据新面板的 Config 处理游戏状态 ---
                ApplyPanelConfigOnPush(newPanel.config);

                // --- 2. 处理栈逻辑 ---
                if (_panelStack.Count > 0)
                {
                    var topPanel = _panelStack.Peek();
                    if (newPanel.config.HasFlag(UIPanelConfig.HideLowerPanels))
                        topPanel.SetInteractable(false);
                }

                _panelStack.Push(newPanel);
                await newPanel.ShowAsync();
            }
            finally
            {
                _isTransitioning = false; // 无论如何，强制解锁
            }
        }

        public async UniTask PopPanelAsync()
        {
            if (_isTransitioning || _panelStack.Count == 0) return;
            _isTransitioning = true;

            try
            {
                var currentPanel = _panelStack.Pop();

                await currentPanel.HideAsync();

                // 提取出它的 Config 用于恢复状态，然后销毁
                var config = currentPanel.config;
                if (currentPanel != null && currentPanel.gameObject != null)
                {
                    Destroy(currentPanel.gameObject);
                }

                // --- 1. 恢复底层面板 ---
                if (_panelStack.Count > 0)
                {
                    var lowerPanel = _panelStack.Peek();
                    if (config.HasFlag(UIPanelConfig.HideLowerPanels) && lowerPanel != null)
                    {
                        lowerPanel.SetInteractable(true);
                    }
                }

                // --- 2. 根据刚刚关掉的面板的 Config，恢复游戏状态 ---
                ApplyPanelConfigOnPop(config);
            }
            finally
            {
                _isTransitioning = false; // 强制解锁
            }
        }

        // --- 新增：内部异步退出所有面板的流 ---
        private async UniTask PopAllPanelsAsync()
        {
            if (_isTransitioning) return;

            // 只要栈里还有面板，就一个个退栈。由于 PopPanelAsync 内部会正确处理 _pauseLockCount 和恢复底层面板，所以不会破坏状态。
            while (_panelStack.Count > 0)
            {
                await PopPanelAsync();
            }
        }

        // --- 状态处理分离，保持代码整洁 ---
        private void ApplyPanelConfigOnPush(UIPanelConfig config)
        {
            if (config.HasFlag(UIPanelConfig.PauseGame))
            {
                _pauseLockCount++;
                Time.timeScale = 0f;
            }

            if (config.HasFlag(UIPanelConfig.ShowCursor))
            {
                _cursorLockCount++;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // 只要不是 0，且新面板要求输入，就切给 UI
            if (config.HasFlag(UIPanelConfig.TakeInputFocus) && inputReader != null)
            {
                inputReader.SwitchActionMap(InputMapType.UI);
            }
        }

        private void ApplyPanelConfigOnPop(UIPanelConfig config)
        {
            if (config.HasFlag(UIPanelConfig.PauseGame))
            {
                _pauseLockCount--;
                if (_pauseLockCount <= 0)
                {
                    _pauseLockCount = 0;
                    Time.timeScale = 1f;
                }
            }

            if (config.HasFlag(UIPanelConfig.ShowCursor))
            {
                _cursorLockCount--;
                if (_cursorLockCount <= 0)
                {
                    _cursorLockCount = 0;
                    Cursor.lockState = CursorLockMode.Locked; // 3D 游戏恢复隐藏鼠标
                    Cursor.visible = false;
                }
            }

            // 如果栈空了，把操作还给玩家
            if (_panelStack.Count == 0 && inputReader != null)
            {
                inputReader.SwitchActionMap(InputMapType.Player);
            }
        }
    }
}

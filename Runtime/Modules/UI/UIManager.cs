using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Cysharp.Threading.Tasks;
using CoCoFlow.Runtime.Core;

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
        public static UIManager Instance { get; private set; }

        [Header("Root Transforms")]
        [SerializeField] private Transform hudRoot;
        [SerializeField] private Transform panelRoot;
        [SerializeField] private Transform popupRoot;

        [Header("Input Integration")]
        public string pauseActionName = "Pause";
        public string cancelActionName = "Cancel";
        public string pausePanelAddress = "UI_PausePanel";

        // 抽象引用（不再依赖具体的 PlayerInputReader）
        private IInputEventSource _inputEvents;
        private IInputModeController _inputMode;

        private readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();
        private readonly Stack<UIPanelBase> _panelStack = new Stack<UIPanelBase>();
        private bool _isTransitioning;

        private int _pauseLockCount = 0;
        private int _cursorLockCount = 0;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            // 异步取依赖，避免 Awake 顺序问题
            CoCoServices.WaitFor<IInputEventSource>(svc =>
            {
                _inputEvents = svc;
                _inputEvents.OnActionPerformed += HandleUIInput;
            });

            CoCoServices.WaitFor<IInputModeController>(svc => _inputMode = svc);
        }

        private void OnDestroy()
        {
            if (_inputEvents != null) _inputEvents.OnActionPerformed -= HandleUIInput;
            if (Instance == this) Instance = null;
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
        public void CloseAllPanels() => PopAllPanelsAsync().Forget();

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

                ApplyPanelConfigOnPush(newPanel.config);

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
                _isTransitioning = false;
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

                var config = currentPanel.config;
                if (currentPanel != null && currentPanel.gameObject != null)
                {
                    Destroy(currentPanel.gameObject);
                }

                if (_panelStack.Count > 0)
                {
                    var lowerPanel = _panelStack.Peek();
                    if (config.HasFlag(UIPanelConfig.HideLowerPanels) && lowerPanel != null)
                    {
                        lowerPanel.SetInteractable(true);
                    }
                }

                ApplyPanelConfigOnPop(config);
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        private async UniTask PopAllPanelsAsync()
        {
            if (_isTransitioning) return;

            while (_panelStack.Count > 0)
            {
                await PopPanelAsync();
            }
        }

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

            if (config.HasFlag(UIPanelConfig.TakeInputFocus))
            {
                _inputMode?.SwitchActionMap(InputMapNames.UI);
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
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            if (_panelStack.Count == 0)
            {
                _inputMode?.SwitchActionMap(InputMapNames.Player);
            }
        }
    }
}

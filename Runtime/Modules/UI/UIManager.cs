using System;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Content;
using UnityEngine;
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
        [Header("Root Transforms")]
        [SerializeField] private Transform hudRoot;
        [SerializeField] private Transform panelRoot;
        [SerializeField] private Transform popupRoot;

        [Header("Content")]
        [SerializeField] private CoCoContentHost contentHost;

        [Header("Input Integration")]
        [SerializeField] private string pauseActionName = "Pause";
        [SerializeField] private string cancelActionName = "Cancel";
        [SerializeField] private ContentReference pausePanelSource;

        private IInputEventSource _inputEvents;
        private IInputModeController _inputMode;
        private IDisposable _inputEventsWait;
        private IDisposable _inputModeWait;

        private readonly Stack<UIPanelBase> _panelStack = new Stack<UIPanelBase>();
        private readonly CancellationTokenSource _destroyCts = new CancellationTokenSource();
        private ContentScope _pendingPanelScope;
        private ulong _panelOwnerSequence;
        private bool _isTransitioning;
        private bool _isDestroyed;

        private int _pauseLockCount;
        private int _cursorLockCount;

        #region Public API

        public static UIManager Instance { get; private set; }

        public void OpenPanel(ContentReference panelSource) => PushPanelAsync(panelSource).Forget();
        public void CloseCurrentPanel() => PopPanelAsync().Forget();
        public void CloseAllPanels() => PopAllPanelsAsync().Forget();

        public void TogglePanel(ContentReference panelSource)
        {
            if (_panelStack.Count > 0 &&
                _panelStack.Peek() != null &&
                _panelStack.Peek().SourceContentId.Equals(panelSource.Id))
            {
                CloseCurrentPanel();
            }
            else
            {
                OpenPanel(panelSource);
            }
        }

        #endregion

        #region Internal Logic

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            _inputEventsWait = CoCoServices.WaitFor<IInputEventSource>(svc =>
            {
                if (_isDestroyed) return;
                _inputEvents = svc;
                _inputEvents.OnActionPerformed += HandleUIInput;
            });

            _inputModeWait = CoCoServices.WaitFor<IInputModeController>(svc =>
            {
                if (!_isDestroyed) _inputMode = svc;
            });
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _destroyCts.Cancel();
            _inputEventsWait?.Dispose();
            _inputModeWait?.Dispose();
            if (_inputEvents != null) _inputEvents.OnActionPerformed -= HandleUIInput;
            _pendingPanelScope?.Dispose();
            _pendingPanelScope = null;
            DestroyOpenPanels();
            _destroyCts.Dispose();
            if (Instance == this) Instance = null;
        }

        private async UniTask PushPanelAsync(ContentReference panelSource)
        {
            if (_isTransitioning || _isDestroyed) return;
            if (!panelSource.IsValid || panelSource.Kind != ContentKind.PrefabSource)
            {
                CoCoLog.Error("[UIManager] OpenPanel requires a valid Prefab Source ContentReference.");
                return;
            }

            if (contentHost == null)
            {
                CoCoLog.Error("[UIManager] A CoCoContentHost reference is required.");
                return;
            }

            _isTransitioning = true;
            ContentScope pendingScope = null;
            ContentLease<GameObject> sourceLease = null;
            GameObject panelObject = null;
            UIPanelBase newPanel = null;
            UIPanelBase lowerPanel = null;
            UIPanelConfig panelConfig = UIPanelConfig.None;
            bool ownershipBound = false;
            bool configApplied = false;
            bool lowerPanelDisabled = false;
            bool panelPushed = false;

            try
            {
                if (!TryCreatePanelOwnerId(out var ownerId))
                {
                    CoCoLog.Error("[UIManager] Unable to create a panel Content Owner Id.");
                    return;
                }

                if (!contentHost.TryCreateScope(ownerId, out pendingScope, out var scopeDiagnostic))
                {
                    CoCoLog.Error($"[UIManager] Unable to create panel Content Scope: {scopeDiagnostic}");
                    return;
                }

                _pendingPanelScope = pendingScope;
                ContentAcquireResult<GameObject> acquireResult =
                    await pendingScope.AcquirePrefabSourceAsync(panelSource, _destroyCts.Token);
                if (!acquireResult.Succeeded)
                {
                    if (!acquireResult.Cancelled)
                    {
                        CoCoLog.Error(
                            $"[UIManager] Failed to acquire panel {panelSource.Id}: " +
                            acquireResult.Diagnostic);
                    }

                    return;
                }

                sourceLease = acquireResult.Lease;
                GameObject prefab = sourceLease?.Value;
                if (_isDestroyed || prefab == null) return;

                // 从 prefab 读取 Layer 来确定目标根节点，避免 Instantiate 后再 SetParent
                UIPanelBase prefabPanel = prefab.GetComponent<UIPanelBase>();
                if (prefabPanel == null)
                {
                    CoCoLog.Error($"[UIManager] Panel {panelSource.Id} has no UIPanelBase component.");
                    return;
                }

                Transform targetRoot = prefabPanel.Layer switch
                {
                    UILayer.HUD => hudRoot,
                    UILayer.Popup => popupRoot,
                    _ => panelRoot
                };

                panelObject = Instantiate(prefab, targetRoot, false);
                newPanel = panelObject.GetComponent<UIPanelBase>();
                if (newPanel == null)
                {
                    CoCoLog.Error($"[UIManager] Instantiated panel {panelSource.Id} has no UIPanelBase component.");
                    return;
                }

                newPanel.BindSourceOwnership(panelSource.Id, pendingScope, sourceLease);
                ownershipBound = true;
                _pendingPanelScope = null;
                pendingScope = null;
                sourceLease = null;
                panelObject.transform.SetAsLastSibling();

                panelConfig = newPanel.Config;
                ApplyPanelConfigOnPush(panelConfig);
                configApplied = true;

                if (_panelStack.Count > 0)
                {
                    lowerPanel = _panelStack.Peek();
                    if (panelConfig.HasFlag(UIPanelConfig.HideLowerPanels) && lowerPanel != null)
                    {
                        lowerPanel.SetInteractable(false);
                        lowerPanelDisabled = true;
                    }
                }

                _panelStack.Push(newPanel);
                panelPushed = true;
                await newPanel.ShowAsync();
            }
            catch (Exception ex)
            {
                CoCoLog.Error($"[UIManager] Failed to open panel {panelSource.Id}: {ex}");

                if (panelPushed && _panelStack.Count > 0 && ReferenceEquals(_panelStack.Peek(), newPanel))
                {
                    _panelStack.Pop();
                    panelPushed = false;
                }

                if (lowerPanelDisabled && lowerPanel != null)
                {
                    lowerPanel.SetInteractable(true);
                    lowerPanelDisabled = false;
                }

                if (configApplied)
                {
                    ApplyPanelConfigOnPop(panelConfig);
                    configApplied = false;
                }
            }
            finally
            {
                if (ReferenceEquals(_pendingPanelScope, pendingScope))
                {
                    _pendingPanelScope = null;
                }

                if (!ownershipBound)
                {
                    sourceLease?.Dispose();
                    pendingScope?.Dispose();
                    if (panelObject != null)
                    {
                        Destroy(panelObject);
                    }
                }

                if (ownershipBound && !panelPushed && panelObject != null)
                {
                    Destroy(panelObject);
                }

                if (!_isDestroyed) _isTransitioning = false;
            }
        }

        private async UniTask PopPanelAsync()
        {
            if (_isTransitioning || _panelStack.Count == 0) return;
            _isTransitioning = true;

            try
            {
                var currentPanel = _panelStack.Pop();
                var config = currentPanel != null ? currentPanel.Config : UIPanelConfig.None;
                try
                {
                    if (currentPanel != null)
                    {
                        await currentPanel.HideAsync();
                    }
                }
                catch (Exception ex)
                {
                    CoCoLog.Error($"[UIManager] Failed to hide panel: {ex}");
                }
                finally
                {
                    if (currentPanel != null && currentPanel.gameObject != null)
                    {
                        Destroy(currentPanel.gameObject);
                    }
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

        private void HandleUIInput(string actionName)
        {
            if (_isTransitioning || _isDestroyed) return;

            if (actionName == pauseActionName && _panelStack.Count == 0)
            {
                PushPanelAsync(pausePanelSource).Forget();
            }
            else if (actionName == cancelActionName && _panelStack.Count > 0)
            {
                PopPanelAsync().Forget();
            }
        }

        private void DestroyOpenPanels()
        {
            while (_panelStack.Count > 0)
            {
                var panel = _panelStack.Pop();
                if (panel == null) continue;

                var panelObject = panel.gameObject;
                if (panelObject == null) continue;

                if (Application.isPlaying)
                    Destroy(panelObject);
                else
                    DestroyImmediate(panelObject);
            }

            _pauseLockCount = 0;
            _cursorLockCount = 0;
            Time.timeScale = 1f;
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

        private bool TryCreatePanelOwnerId(out ContentOwnerId ownerId)
        {
            _panelOwnerSequence++;
            return ContentOwnerId.TryCreate(
                $"ui.manager.{GetInstanceID()}.panel.{_panelOwnerSequence}",
                out ownerId);
        }
        #endregion
    }
}

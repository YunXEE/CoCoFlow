using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlow.Runtime.Modules.Input
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput))]
    public sealed class InputRuntime : MonoBehaviour,
        IInputStateProvider,
        IInputEventSource,
        IInputModeController
    {
        [Header("Input Authority")]
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private InputGlyphCatalog glyphCatalog;

        [Header("Binding Overrides")]
        [SerializeField] private MonoBehaviour bindingOverrideStore;
        [SerializeField] private string bindingOverrideStorageKey =
            "cocoflow.input.binding-overrides";

        [Header("Legacy Presentation Compatibility")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference zoomAction;

        private Action<string> _legacyActionPerformed;
        private Action<string> _legacyActionCanceled;
        private string _legacyBufferedAction = string.Empty;
        private string _currentControlScheme = string.Empty;
        private string _currentDeviceLayout = string.Empty;
        private bool _isSubscribed;

        public PlayerInput PlayerInput => playerInput;
        public InputActionAsset Actions => playerInput != null ? playerInput.actions : null;
        public InputGlyphCatalog GlyphCatalog => glyphCatalog;
        public string CurrentControlScheme => _currentControlScheme;
        public string CurrentDeviceLayout => _currentDeviceLayout;
        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public Vector2 ZoomInput { get; private set; }

        public event Action<InputActionEvent> ActionChanged;
        public event Action PromptChanged;
        public event Action InputFenced;

        event Action<string> IInputEventSource.OnActionPerformed
        {
            add => _legacyActionPerformed += value;
            remove => _legacyActionPerformed -= value;
        }

        event Action<string> IInputEventSource.OnActionCanceled
        {
            add => _legacyActionCanceled += value;
            remove => _legacyActionCanceled -= value;
        }

        private void Reset()
        {
            playerInput = GetComponent<PlayerInput>();
        }

        private void Awake()
        {
            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
            }

            LoadBindingOverrides();
            UpdatePresentationAuthority(true);

            CoCoServices.Register<IInputStateProvider>(this);
            CoCoServices.Register<IInputEventSource>(this);
            CoCoServices.Register<IInputModeController>(this);
        }

        private void OnEnable()
        {
            SubscribeActions();
            UpdatePresentationAuthority(true);
        }

        private void Update()
        {
            SampleLegacyContinuousValues();
            UpdatePresentationAuthority(false);
        }

        private void OnDisable()
        {
            UnsubscribeActions();
            FenceInput();
        }

        private void OnDestroy()
        {
            UnsubscribeActions();
            CoCoServices.Unregister<IInputStateProvider>(this);
            CoCoServices.Unregister<IInputEventSource>(this);
            CoCoServices.Unregister<IInputModeController>(this);
        }

        public bool TryReadValue<TValue>(
            InputActionReference actionReference,
            out TValue value)
            where TValue : struct
        {
            if (!TryResolveAction(actionReference, out InputAction action) ||
                !action.enabled)
            {
                value = default;
                return false;
            }

            value = action.ReadValue<TValue>();
            return true;
        }

        public bool TryResolveAction(
            InputActionReference actionReference,
            out InputAction action)
        {
            action = null;
            InputAction referencedAction = actionReference != null
                ? actionReference.action
                : null;
            return referencedAction != null &&
                   TryResolveAction(referencedAction.id, out action);
        }

        public bool TryResolveAction(Guid actionId, out InputAction action)
        {
            action = null;
            InputActionAsset actions = Actions;
            if (actions == null || actionId == Guid.Empty)
            {
                return false;
            }

            action = actions.FindAction(actionId.ToString(), false);
            return action != null;
        }

        public bool TryGetPrompt(
            InputActionReference actionReference,
            int bindingIndex,
            out InputPromptSnapshot snapshot)
        {
            snapshot = default;
            if (!TryResolveAction(actionReference, out InputAction action) ||
                bindingIndex < 0 ||
                bindingIndex >= action.bindings.Count)
            {
                return false;
            }

            string bindingDisplay = action.GetBindingDisplayString(
                bindingIndex,
                out string deviceLayout,
                out string controlPath);
            Sprite glyph = null;
            glyphCatalog?.TryResolve(deviceLayout, controlPath, out glyph);

            snapshot = new InputPromptSnapshot(
                action.id,
                bindingIndex,
                bindingDisplay,
                deviceLayout,
                controlPath,
                glyph);
            return snapshot.IsValid;
        }

        public void SwitchActionMap(InputActionMap actionMap)
        {
            if (playerInput == null || actionMap == null)
            {
                return;
            }

            FenceInput();
            playerInput.SwitchCurrentActionMap(actionMap.name);
            UpdatePresentationAuthority(true);
        }

        public void FenceInput()
        {
            _legacyBufferedAction = string.Empty;
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            ZoomInput = Vector2.zero;
            InputFenced?.Invoke();
        }

        public string CaptureBindingOverrides()
        {
            return Actions != null
                ? Actions.SaveBindingOverridesAsJson()
                : string.Empty;
        }

        public bool TryCommitBindingOverrides(
            string previousOverrideJson,
            out string error)
        {
            error = string.Empty;
            InputActionAsset actions = Actions;
            IInputBindingOverrideStore store =
                bindingOverrideStore as IInputBindingOverrideStore;
            if (actions == null)
            {
                error = "InputRuntime has no PlayerInput actions.";
                return false;
            }

            if (store == null)
            {
                RestoreBindingOverrides(previousOverrideJson);
                error =
                    "InputRuntime requires an IInputBindingOverrideStore to commit a rebind.";
                return false;
            }

            try
            {
                string nextOverrideJson =
                    actions.SaveBindingOverridesAsJson();
                if (!store.TrySave(
                        bindingOverrideStorageKey,
                        nextOverrideJson))
                {
                    RestoreBindingOverrides(previousOverrideJson);
                    error =
                        "The binding override store rejected the new override JSON.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                RestoreBindingOverrides(previousOverrideJson);
                error =
                    "The binding override store failed: " +
                    exception.Message;
                return false;
            }

            NotifyBindingsChanged();
            return true;
        }

        public void RestoreBindingOverrides(string overrideJson)
        {
            InputActionAsset actions = Actions;
            if (actions == null)
            {
                return;
            }

            actions.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(overrideJson))
            {
                actions.LoadBindingOverridesFromJson(overrideJson, false);
            }

            NotifyBindingsChanged();
        }

        public void NotifyBindingsChanged()
        {
            FenceInput();
            UpdatePresentationAuthority(true);
        }

        bool IInputEventSource.TryConsumeBufferedAction(string actionName)
        {
            if (string.IsNullOrEmpty(actionName) ||
                !string.Equals(
                    _legacyBufferedAction,
                    actionName,
                    StringComparison.Ordinal))
            {
                return false;
            }

            _legacyBufferedAction = string.Empty;
            return true;
        }

        void IInputModeController.SwitchActionMap(string mapName)
        {
            if (playerInput == null || string.IsNullOrEmpty(mapName))
            {
                return;
            }

            FenceInput();
            playerInput.SwitchCurrentActionMap(mapName);
            UpdatePresentationAuthority(true);
        }

        void IInputModeController.ClearBuffer()
        {
            FenceInput();
        }

        private void LoadBindingOverrides()
        {
            IInputBindingOverrideStore store =
                bindingOverrideStore as IInputBindingOverrideStore;
            if (store == null || Actions == null)
            {
                return;
            }

            try
            {
                if (!store.TryLoad(
                        bindingOverrideStorageKey,
                        out string overrideJson) ||
                    string.IsNullOrEmpty(overrideJson))
                {
                    return;
                }

                Actions.LoadBindingOverridesFromJson(overrideJson, false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[InputRuntime] Binding override load failed: {exception.Message}",
                    this);
            }
        }

        private void SubscribeActions()
        {
            if (_isSubscribed || Actions == null)
            {
                return;
            }

            foreach (InputActionMap map in Actions.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    action.performed += OnActionPerformed;
                    action.canceled += OnActionCanceled;
                }
            }

            _isSubscribed = true;
        }

        private void UnsubscribeActions()
        {
            if (!_isSubscribed || Actions == null)
            {
                return;
            }

            foreach (InputActionMap map in Actions.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    action.performed -= OnActionPerformed;
                    action.canceled -= OnActionCanceled;
                }
            }

            _isSubscribed = false;
        }

        private void OnActionPerformed(InputAction.CallbackContext context)
        {
            _legacyBufferedAction = context.action.name;
            _legacyActionPerformed?.Invoke(context.action.name);
            ActionChanged?.Invoke(
                new InputActionEvent(context.action, InputActionPhase.Performed));
        }

        private void OnActionCanceled(InputAction.CallbackContext context)
        {
            _legacyActionCanceled?.Invoke(context.action.name);
            ActionChanged?.Invoke(
                new InputActionEvent(context.action, InputActionPhase.Canceled));
        }

        private void SampleLegacyContinuousValues()
        {
            MoveInput = TryReadValue(moveAction, out Vector2 move)
                ? move
                : Vector2.zero;
            LookInput = TryReadValue(lookAction, out Vector2 look)
                ? look
                : Vector2.zero;
            ZoomInput = TryReadValue(zoomAction, out Vector2 zoom)
                ? zoom
                : Vector2.zero;
        }

        private void UpdatePresentationAuthority(bool force)
        {
            string nextControlScheme = playerInput != null
                ? playerInput.currentControlScheme ?? string.Empty
                : string.Empty;
            string nextDeviceLayout =
                playerInput != null && playerInput.devices.Count > 0
                    ? playerInput.devices[0].layout ?? string.Empty
                    : string.Empty;
            if (!force &&
                string.Equals(
                    _currentControlScheme,
                    nextControlScheme,
                    StringComparison.Ordinal) &&
                string.Equals(
                    _currentDeviceLayout,
                    nextDeviceLayout,
                    StringComparison.Ordinal))
            {
                return;
            }

            _currentControlScheme = nextControlScheme;
            _currentDeviceLayout = nextDeviceLayout;
            PromptChanged?.Invoke();
        }

        private void OnValidate()
        {
            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
            }

            if (bindingOverrideStore != null &&
                !(bindingOverrideStore is IInputBindingOverrideStore))
            {
                Debug.LogError(
                    "[InputRuntime] Binding Override Store must implement " +
                    nameof(IInputBindingOverrideStore) + ".",
                    this);
            }
        }
    }
}

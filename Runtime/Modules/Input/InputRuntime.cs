using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(
    "CoCoFlow.Tests.Runtime.Input")]

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
        private string _lastUsedDeviceLayout = string.Empty;
        private InputActionAsset _subscribedActions;
        private readonly List<InputAction> _neutralGatedActions =
            new List<InputAction>(8);
        private int _controlledTransitionDepth;
        private int _bindingResolutionDepth;
        private bool _runtimeInitialized;
        private bool _bindingOverrideLoadAttempted;
        private bool _hasStarted;

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

            CoCoServices.Register<IInputStateProvider>(this);
            CoCoServices.Register<IInputEventSource>(this);
            CoCoServices.Register<IInputModeController>(this);
        }

        private void OnEnable()
        {
            InputSystem.onActionChange += OnGlobalActionChange;
            TryInitializeRuntime();
        }

        private void Start()
        {
            _hasStarted = true;
            TryInitializeRuntime();
            TryLoadBindingOverrides();
        }

        private void Update()
        {
            TryInitializeRuntime();
            TryLoadBindingOverrides();
            if (!_runtimeInitialized)
            {
                return;
            }

            ReconcileActionSubscriptions(true);
            FinalizeAbandonedBindingResolution();
            ReleaseNeutralActions();
            SampleLegacyContinuousValues();
            UpdatePresentationAuthority(false);
        }

        private void OnDisable()
        {
            InputSystem.onActionChange -= OnGlobalActionChange;
            UnsubscribeActions();
            _neutralGatedActions.Clear();
            _bindingResolutionDepth = 0;
            _runtimeInitialized = false;
            FenceInput();
        }

        private void OnDestroy()
        {
            InputSystem.onActionChange -= OnGlobalActionChange;
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
            if (!isActiveAndEnabled ||
                !_runtimeInitialized ||
                !TryResolveAction(actionReference, out InputAction action) ||
                !action.enabled ||
                ShouldSuppress(action))
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
            out InputPromptSnapshot snapshot)
        {
            snapshot = default;
            if (!TryResolveAction(actionReference, out InputAction action) ||
                !TrySelectPromptBinding(action, out int bindingIndex))
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

            SwitchActionMap(actionMap.name);
        }

        internal void DisableActionForTransition(InputAction action)
        {
            if (action == null)
            {
                return;
            }

            FenceInput();
            _controlledTransitionDepth++;
            try
            {
                action.Disable();
            }
            finally
            {
                _controlledTransitionDepth--;
                FenceInput();
            }
        }

        internal void RestoreActionAfterTransition(
            InputAction action,
            bool shouldEnable)
        {
            if (action == null)
            {
                return;
            }

            _controlledTransitionDepth++;
            try
            {
                if (shouldEnable && !action.enabled)
                {
                    action.Enable();
                }
            }
            finally
            {
                _controlledTransitionDepth--;
                RefreshNeutralGate(action);
                FenceInput();
                UpdatePresentationAuthority(true);
            }
        }

        public void FenceInput()
        {
            _legacyBufferedAction = string.Empty;
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            ZoomInput = Vector2.zero;
            PublishBoundaryEvent(InputFenced, nameof(InputFenced));
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
            RefreshNeutralGates(Actions);
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

            SwitchActionMap(mapName);
        }

        void IInputModeController.ClearBuffer()
        {
            FenceInput();
        }

        private void TryInitializeRuntime()
        {
            if (_runtimeInitialized ||
                playerInput == null ||
                !playerInput.inputIsActive)
            {
                return;
            }

            InputActionAsset actions = playerInput.actions;
            if (actions == null)
            {
                return;
            }

            _runtimeInitialized = true;
            ReconcileActionSubscriptions(false);
            TryLoadBindingOverrides();
            RefreshNeutralGates(_subscribedActions);
            UpdatePresentationAuthority(true);
        }

        private void TryLoadBindingOverrides()
        {
            if (!_hasStarted ||
                !_runtimeInitialized ||
                _bindingOverrideLoadAttempted)
            {
                return;
            }

            InputActionAsset actions = _subscribedActions;
            if (actions == null)
            {
                return;
            }

            IInputBindingOverrideStore store =
                bindingOverrideStore as IInputBindingOverrideStore;
            _bindingOverrideLoadAttempted = true;
            if (store == null)
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

                actions.LoadBindingOverridesFromJson(overrideJson, false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[InputRuntime] Binding override load failed: {exception.Message}",
                    this);
            }
        }

        private void ReconcileActionSubscriptions(bool notify)
        {
            InputActionAsset next = Actions;
            if (ReferenceEquals(_subscribedActions, next))
            {
                return;
            }

            UnsubscribeActions();
            _neutralGatedActions.Clear();
            _subscribedActions = next;
            if (_subscribedActions != null)
            {
                foreach (InputActionMap map in _subscribedActions.actionMaps)
                {
                    foreach (InputAction action in map.actions)
                    {
                        action.performed += OnActionPerformed;
                        action.canceled += OnActionCanceled;
                    }
                }

                RefreshNeutralGates(_subscribedActions);
            }

            if (!notify)
            {
                return;
            }

            _lastUsedDeviceLayout = string.Empty;
            FenceInput();
            UpdatePresentationAuthority(true);
        }

        private void UnsubscribeActions()
        {
            InputActionAsset subscribed = _subscribedActions;
            _subscribedActions = null;
            if (subscribed == null)
            {
                return;
            }

            foreach (InputActionMap map in subscribed.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    action.performed -= OnActionPerformed;
                    action.canceled -= OnActionCanceled;
                }
            }
        }

        private void OnActionPerformed(InputAction.CallbackContext context)
        {
            if (ShouldSuppress(context.action))
            {
                return;
            }

            UpdateLastUsedDevice(context.control?.device);
            _legacyBufferedAction = context.action.name;
            _legacyActionPerformed?.Invoke(context.action.name);
            ActionChanged?.Invoke(
                new InputActionEvent(context.action, InputActionPhase.Performed));
        }

        private void OnActionCanceled(InputAction.CallbackContext context)
        {
            if (ShouldSuppress(context.action))
            {
                return;
            }

            UpdateLastUsedDevice(context.control?.device);
            _legacyActionCanceled?.Invoke(context.action.name);
            ActionChanged?.Invoke(
                new InputActionEvent(context.action, InputActionPhase.Canceled));
        }

        private void SwitchActionMap(string mapName)
        {
            FenceInput();
            _controlledTransitionDepth++;
            try
            {
                playerInput.SwitchCurrentActionMap(mapName);
            }
            finally
            {
                _controlledTransitionDepth--;
                RefreshNeutralGates(Actions);
                FenceInput();
                UpdatePresentationAuthority(true);
            }
        }

        private void OnGlobalActionChange(
            object actionOrMap,
            InputActionChange change)
        {
            if (!_runtimeInitialized)
            {
                TryInitializeRuntime();
                if (!_runtimeInitialized)
                {
                    return;
                }
            }

            if (_controlledTransitionDepth > 0 ||
                !TryGetOwningAsset(actionOrMap, out InputActionAsset asset) ||
                (!ReferenceEquals(asset, _subscribedActions) &&
                 !ReferenceEquals(asset, Actions)))
            {
                return;
            }

            switch (change)
            {
                case InputActionChange.BoundControlsAboutToChange:
                    _bindingResolutionDepth++;
                    FenceInput();
                    return;

                case InputActionChange.BoundControlsChanged:
                    RefreshNeutralGatesForChange(actionOrMap);
                    if (_bindingResolutionDepth > 0)
                    {
                        _bindingResolutionDepth--;
                    }

                    FenceInput();
                    if (_bindingResolutionDepth == 0)
                    {
                        UpdatePresentationAuthority(true);
                    }

                    return;

                case InputActionChange.ActionDisabled:
                case InputActionChange.ActionMapDisabled:
                    FenceInput();
                    return;

                case InputActionChange.ActionEnabled:
                    RefreshNeutralGate(actionOrMap as InputAction);
                    FenceInput();
                    return;

                case InputActionChange.ActionMapEnabled:
                    RefreshNeutralGates(actionOrMap as InputActionMap);
                    FenceInput();
                    return;
            }
        }

        private bool ShouldSuppress(InputAction action)
        {
            return _controlledTransitionDepth > 0 ||
                   _bindingResolutionDepth > 0 ||
                   (action != null && _neutralGatedActions.Contains(action));
        }

        private void FinalizeAbandonedBindingResolution()
        {
            if (_bindingResolutionDepth <= 0)
            {
                return;
            }

            _bindingResolutionDepth = 0;
            RefreshNeutralGates(Actions);
            FenceInput();
            UpdatePresentationAuthority(true);
        }

        private void RefreshNeutralGatesForChange(object actionOrMap)
        {
            switch (actionOrMap)
            {
                case InputAction action:
                    RefreshNeutralGate(action);
                    return;

                case InputActionMap map:
                    RefreshNeutralGates(map);
                    return;

                case InputActionAsset asset:
                    RefreshNeutralGates(asset);
                    return;

                default:
                    RefreshNeutralGates(Actions);
                    return;
            }
        }

        private void RefreshNeutralGates(InputActionAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            foreach (InputActionMap map in asset.actionMaps)
            {
                RefreshNeutralGates(map);
            }
        }

        private void RefreshNeutralGates(InputActionMap map)
        {
            if (map == null)
            {
                return;
            }

            foreach (InputAction action in map.actions)
            {
                RefreshNeutralGate(action);
            }
        }

        private void RefreshNeutralGate(InputAction action)
        {
            if (action == null)
            {
                return;
            }

            bool shouldGate = action.enabled && HasActuatedControl(action);
            int existingIndex = _neutralGatedActions.IndexOf(action);
            if (shouldGate)
            {
                if (existingIndex < 0)
                {
                    _neutralGatedActions.Add(action);
                }

                return;
            }

            if (existingIndex >= 0)
            {
                _neutralGatedActions.RemoveAt(existingIndex);
            }
        }

        internal void ReleaseNeutralActions()
        {
            for (int index = _neutralGatedActions.Count - 1;
                 index >= 0;
                 index--)
            {
                InputAction action = _neutralGatedActions[index];
                if (action == null ||
                    !action.enabled ||
                    !HasActuatedControl(action))
                {
                    _neutralGatedActions.RemoveAt(index);
                }
            }
        }

        private static bool HasActuatedControl(InputAction action)
        {
            for (int index = 0; index < action.controls.Count; index++)
            {
                InputControl control = action.controls[index];
                if (control != null && !control.CheckStateIsAtDefault())
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetOwningAsset(
            object actionOrMap,
            out InputActionAsset asset)
        {
            if (actionOrMap is InputAction action)
            {
                asset = action.actionMap?.asset;
                return asset != null;
            }

            if (actionOrMap is InputActionMap map)
            {
                asset = map.asset;
                return asset != null;
            }

            asset = actionOrMap as InputActionAsset;
            return asset != null;
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
            string nextDeviceLayout = ResolvePresentationDeviceLayout();
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
            PublishBoundaryEvent(PromptChanged, nameof(PromptChanged));
        }

        private void PublishBoundaryEvent(Action subscribers, string eventName)
        {
            if (subscribers == null)
            {
                return;
            }

            foreach (Delegate subscriber in subscribers.GetInvocationList())
            {
                try
                {
                    ((Action)subscriber).Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(
                        new InvalidOperationException(
                            $"[InputRuntime] {eventName} subscriber failed.",
                            exception),
                        this);
                }
            }
        }

        private bool TrySelectPromptBinding(
            InputAction action,
            out int bindingIndex)
        {
            bindingIndex = -1;
            if (action == null)
            {
                return false;
            }

            string bindingGroup = string.Empty;
            InputActionAsset actions = Actions;
            if (actions != null && !string.IsNullOrEmpty(_currentControlScheme))
            {
                InputControlScheme? scheme =
                    actions.FindControlScheme(_currentControlScheme);
                if (scheme.HasValue)
                {
                    bindingGroup = scheme.Value.bindingGroup ?? string.Empty;
                }
            }

            InputBinding groupMask = string.IsNullOrEmpty(bindingGroup)
                ? default
                : InputBinding.MaskByGroup(bindingGroup);
            int bestScore = -1;
            for (int index = 0; index < action.bindings.Count; index++)
            {
                InputBinding binding = action.bindings[index];
                if (binding.isPartOfComposite ||
                    string.IsNullOrEmpty(binding.effectivePath))
                {
                    continue;
                }

                string display = action.GetBindingDisplayString(
                    index,
                    out string deviceLayout,
                    out _);
                if (string.IsNullOrEmpty(display))
                {
                    continue;
                }

                bool groupMatches =
                    !string.IsNullOrEmpty(bindingGroup) &&
                    groupMask.Matches(binding);
                bool layoutMatches =
                    LayoutMatches(_currentDeviceLayout, deviceLayout);
                int score = groupMatches
                    ? layoutMatches ? 3 : 2
                    : layoutMatches ? 1 : 0;
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bindingIndex = index;
                if (bestScore == 3)
                {
                    break;
                }
            }

            return bindingIndex >= 0;
        }

        private void UpdateLastUsedDevice(InputDevice device)
        {
            if (device == null || playerInput == null)
            {
                return;
            }

            bool paired = false;
            for (int index = 0; index < playerInput.devices.Count; index++)
            {
                if (ReferenceEquals(playerInput.devices[index], device))
                {
                    paired = true;
                    break;
                }
            }

            string layout = paired ? device.layout ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(layout) ||
                string.Equals(
                    _lastUsedDeviceLayout,
                    layout,
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastUsedDeviceLayout = layout;
            UpdatePresentationAuthority(false);
        }

        private string ResolvePresentationDeviceLayout()
        {
            if (playerInput == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(_lastUsedDeviceLayout))
            {
                for (int index = 0; index < playerInput.devices.Count; index++)
                {
                    string pairedLayout =
                        playerInput.devices[index]?.layout ?? string.Empty;
                    if (LayoutMatches(
                            pairedLayout,
                            _lastUsedDeviceLayout) ||
                        LayoutMatches(
                            _lastUsedDeviceLayout,
                            pairedLayout))
                    {
                        return _lastUsedDeviceLayout;
                    }
                }
            }

            return playerInput.devices.Count > 0
                ? playerInput.devices[0]?.layout ?? string.Empty
                : string.Empty;
        }

        private static bool LayoutMatches(
            string currentLayout,
            string bindingLayout)
        {
            if (string.IsNullOrEmpty(currentLayout) ||
                string.IsNullOrEmpty(bindingLayout))
            {
                return false;
            }

            return string.Equals(
                       currentLayout,
                       bindingLayout,
                       StringComparison.Ordinal) ||
                   InputSystem.IsFirstLayoutBasedOnSecond(
                       currentLayout,
                       bindingLayout);
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

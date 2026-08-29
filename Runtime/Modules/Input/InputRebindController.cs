using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlow.Runtime.Modules.Input
{
    [DisallowMultipleComponent]
    public sealed class InputRebindController : MonoBehaviour
    {
        [SerializeField] private InputReader inputReader;

        private InputActionRebindingExtensions.RebindingOperation _operation;
        private InputAction _action;
        private string _previousOverrideJson;
        private bool _wasEnabled;

        public bool IsRebinding => _operation != null;
        public string LastError { get; private set; } = string.Empty;

        public event Action<bool> RebindCompleted;

        private void Reset()
        {
            inputReader = GetComponent<InputReader>();
        }

        private void OnDisable()
        {
            _operation?.Cancel();
        }

        public bool TryBegin(
            InputActionReference actionReference,
            Guid bindingId,
            out string error)
        {
            InputAction referencedAction = actionReference != null
                ? actionReference.action
                : null;
            if (referencedAction == null)
            {
                error = "The rebind Action reference is missing.";
                return false;
            }

            return TryBegin(referencedAction.id, bindingId, out error);
        }

        public bool TryBegin(
            Guid actionId,
            Guid bindingId,
            out string error)
        {
            error = string.Empty;
            if (_operation != null)
            {
                error = "A rebind is already active.";
                return false;
            }

            if (inputReader == null ||
                !inputReader.TryResolveAction(actionId, out InputAction action))
            {
                error = "InputReader could not resolve the requested action.";
                return false;
            }

            int bindingIndex = -1;
            for (int index = 0; index < action.bindings.Count; index++)
            {
                if (action.bindings[index].id == bindingId)
                {
                    bindingIndex = index;
                    break;
                }
            }

            if (bindingId == Guid.Empty || bindingIndex < 0)
            {
                error = "InputReader could not resolve the stable Binding ID.";
                return false;
            }

            _action = action;
            _wasEnabled = action.enabled;
            _previousOverrideJson = inputReader.CaptureBindingOverrides();
            inputReader.DisableActionForTransition(action);

            try
            {
                _operation = action
                    .PerformInteractiveRebinding(bindingIndex)
                    .OnCancel(HandleCanceled)
                    .OnComplete(HandleCompleted);
                _operation.Start();
                LastError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                RestorePreviousBinding();
                error = exception.Message;
                LastError = error;
                DisposeOperation();
                return false;
            }
        }

        public void Cancel()
        {
            _operation?.Cancel();
        }

        private void HandleCanceled(
            InputActionRebindingExtensions.RebindingOperation operation)
        {
            RestorePreviousBinding();
            LastError = string.Empty;
            DisposeOperation();
            RebindCompleted?.Invoke(false);
        }

        private void HandleCompleted(
            InputActionRebindingExtensions.RebindingOperation operation)
        {
            bool committed = false;
            string error = string.Empty;
            try
            {
                RestoreActionEnablement();
                committed = inputReader != null &&
                            inputReader.TryCommitBindingOverrides(
                                _previousOverrideJson,
                                out error);
            }
            catch (Exception exception)
            {
                RestorePreviousBinding();
                error = exception.Message;
            }
            finally
            {
                if (!committed)
                {
                    RestoreActionEnablement();
                }

                LastError = committed ? string.Empty : error;
                DisposeOperation();
                RebindCompleted?.Invoke(committed);
            }
        }

        private void RestorePreviousBinding()
        {
            inputReader?.RestoreBindingOverrides(_previousOverrideJson);
            RestoreActionEnablement();
        }

        private void RestoreActionEnablement()
        {
            inputReader?.RestoreActionAfterTransition(
                _action,
                _wasEnabled);
        }

        private void DisposeOperation()
        {
            _operation?.Dispose();
            _operation = null;
            _action = null;
            _previousOverrideJson = string.Empty;
            _wasEnabled = false;
        }
    }
}

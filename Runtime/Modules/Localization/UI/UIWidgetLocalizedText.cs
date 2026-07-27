using System;
using System.Collections;
using CoCoFlow.Runtime.Modules.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CoCoFlow.Runtime.Modules.Localization.UI
{
    [DisallowMultipleComponent]
    public sealed class UIWidgetLocalizedText : UIWidgetBase
    {
        [SerializeField] private TMP_Text targetText;
        [SerializeField] private LocalizedString localizedString =
            new LocalizedString();
        [SerializeField, TextArea] private string fallbackText = string.Empty;

        private bool _isSubscribed;
        private bool _isPresentationSuppressed;
        private Coroutine _loadObservation;

        public LocalizationDiagnostic LastDiagnostic { get; private set; }

        public event Action<LocalizationDiagnostic> DiagnosticChanged;

        protected override void OnEnable()
        {
            if (!_isPresentationSuppressed)
            {
                Subscribe();
            }

            base.OnEnable();
        }

        private void OnDisable()
        {
            StopLoadObservation();
            Unsubscribe();
        }

        public override void ResetState()
        {
            if (_isPresentationSuppressed)
            {
                if (targetText != null)
                {
                    targetText.text = string.Empty;
                }

                SetDiagnostic(LocalizationDiagnosticCode.None, string.Empty);
                return;
            }

            if (targetText == null)
            {
                SetDiagnostic(
                    LocalizationDiagnosticCode.MissingTextTarget,
                    "UIWidgetLocalizedText requires a TMP_Text target.");
                return;
            }

            if (localizedString == null)
            {
                targetText.text = fallbackText;
                SetDiagnostic(
                    LocalizationDiagnosticCode.MissingLocalizedString,
                    "UIWidgetLocalizedText requires a LocalizedString reference.");
                return;
            }

            if (localizedString.IsEmpty)
            {
                targetText.text = fallbackText;
                SetDiagnostic(
                    LocalizationDiagnosticCode.InvalidTableOrEntry,
                    "The LocalizedString Table or Entry reference is empty.");
                return;
            }

            try
            {
                localizedString.RefreshString();
                ObserveCurrentLoad();
            }
            catch (Exception exception)
            {
                targetText.text = fallbackText;
                SetDiagnostic(
                    LocalizationDiagnosticCode.LoadFailed,
                    exception.Message);
            }
        }

        public void SetArguments(params object[] arguments)
        {
            if (localizedString == null)
            {
                return;
            }

            _isPresentationSuppressed = false;
            localizedString.Arguments = arguments ?? Array.Empty<object>();
            if (!_isSubscribed && isActiveAndEnabled)
            {
                Subscribe();
            }

            if (!_isSubscribed)
            {
                return;
            }

            try
            {
                localizedString.RefreshString();
                ObserveCurrentLoad();
            }
            catch (Exception exception)
            {
                if (targetText != null)
                {
                    targetText.text = fallbackText;
                }

                SetDiagnostic(
                    LocalizationDiagnosticCode.LoadFailed,
                    exception.Message);
            }
        }

        public void SetFallback(string value)
        {
            fallbackText = value ?? string.Empty;
            if (targetText != null && LastDiagnostic.IsError)
            {
                targetText.text = fallbackText;
            }
        }

        public void ClearPresentation()
        {
            _isPresentationSuppressed = true;
            StopLoadObservation();
            Unsubscribe();
            if (localizedString != null)
            {
                localizedString.Arguments = Array.Empty<object>();
            }

            fallbackText = string.Empty;
            if (targetText != null)
            {
                targetText.text = string.Empty;
            }

            SetDiagnostic(LocalizationDiagnosticCode.None, string.Empty);
        }

        private void Subscribe()
        {
            if (_isSubscribed || localizedString == null)
            {
                return;
            }

            localizedString.StringChanged += ApplyLocalizedValue;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed || localizedString == null)
            {
                return;
            }

            localizedString.StringChanged -= ApplyLocalizedValue;
            _isSubscribed = false;
        }

        private void ApplyLocalizedValue(string value)
        {
            if (_isPresentationSuppressed)
            {
                return;
            }

            if (targetText == null)
            {
                SetDiagnostic(
                    LocalizationDiagnosticCode.MissingTextTarget,
                    "The localized value arrived without a TMP_Text target.");
                return;
            }

            if (string.IsNullOrEmpty(value))
            {
                targetText.text = fallbackText;
                SetDiagnostic(
                    LocalizationDiagnosticCode.EmptyResult,
                    "The localized string resolved to an empty value.");
                return;
            }

            targetText.text = value;
            SetDiagnostic(LocalizationDiagnosticCode.None, string.Empty);
        }

        private void ObserveCurrentLoad()
        {
            StopLoadObservation();
            if (localizedString == null ||
                _isPresentationSuppressed ||
                !localizedString.CurrentLoadingOperationHandle.IsValid())
            {
                return;
            }

            _loadObservation = StartCoroutine(ObserveCurrentLoadRoutine());
        }

        private IEnumerator ObserveCurrentLoadRoutine()
        {
            var operation = localizedString.CurrentLoadingOperationHandle;
            yield return operation;
            _loadObservation = null;

            if (!isActiveAndEnabled ||
                _isPresentationSuppressed ||
                !operation.IsValid() ||
                operation.Status != AsyncOperationStatus.Failed)
            {
                yield break;
            }

            if (targetText == null)
            {
                SetDiagnostic(
                    LocalizationDiagnosticCode.MissingTextTarget,
                    "The localized load failed without a TMP_Text target.");
                yield break;
            }

            targetText.text = fallbackText;
            SetDiagnostic(
                LocalizationDiagnosticCode.LoadFailed,
                operation.OperationException?.Message ??
                "The localized string loading operation failed.");
        }

        private void StopLoadObservation()
        {
            if (_loadObservation == null)
            {
                return;
            }

            StopCoroutine(_loadObservation);
            _loadObservation = null;
        }

        private void SetDiagnostic(
            LocalizationDiagnosticCode code,
            string message)
        {
            var next = new LocalizationDiagnostic(code, message);
            if (next == LastDiagnostic)
            {
                return;
            }

            LastDiagnostic = next;
            DiagnosticChanged?.Invoke(next);
        }
    }
}

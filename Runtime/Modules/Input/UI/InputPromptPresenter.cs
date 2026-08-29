using CoCoFlow.Runtime.Modules.Localization.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoCoFlow.Runtime.Modules.Input.UI
{
    [DisallowMultipleComponent]
    public sealed class InputPromptPresenter : MonoBehaviour
    {
        [SerializeField] private InputReader inputReader;
        [SerializeField] private InputActionReference action;
        [SerializeField] private UIWidgetLocalizedText localizedText;
        [SerializeField] private Image glyphImage;

        public InputPromptSnapshot Current { get; private set; }

        private void OnEnable()
        {
            if (inputReader != null)
            {
                inputReader.PromptChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (inputReader != null)
            {
                inputReader.PromptChanged -= Refresh;
            }
        }

        public void Refresh()
        {
            if (inputReader == null ||
                !inputReader.TryGetPrompt(
                    action,
                    out InputPromptSnapshot snapshot))
            {
                Current = default;
                if (glyphImage != null)
                {
                    glyphImage.sprite = null;
                    glyphImage.enabled = false;
                }

                localizedText?.ClearPresentation();
                return;
            }

            Current = snapshot;
            if (glyphImage != null)
            {
                glyphImage.sprite = snapshot.Glyph;
                glyphImage.enabled = snapshot.HasGlyph;
            }

            if (localizedText != null)
            {
                localizedText.SetFallback(snapshot.BindingDisplay);
                localizedText.SetArguments(
                    new { binding = snapshot.BindingDisplay });
            }
        }
    }
}

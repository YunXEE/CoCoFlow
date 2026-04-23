using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CoCoFlow.Runtime.Modules.UI.Widgets
{
    public class UIWidgetSlider : UIWidgetBase
    {
        [SerializeField] private Slider slider;
        [SerializeField] private TextMeshProUGUI valueText; // 可选的数值显示

        public Action<float> OnValueChanged;

        protected override void Awake()
        {
            base.Awake();
            if (slider == null) slider = GetComponentInChildren<Slider>();

            slider.onValueChanged.AddListener(HandleSliderChange);
        }

        public void InitValue(float value, float min = 0f, float max = 1f)
        {
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            UpdateText(value);
        }

        private void HandleSliderChange(float newValue)
        {
            UpdateText(newValue);
            OnValueChanged?.Invoke(newValue);
        }

        private void UpdateText(float val)
        {
            if (valueText != null)
            {
                valueText.text = Mathf.RoundToInt(val * 100).ToString();
            }
        }

        public override void ResetState()
        {
            // 如果需要重置默认值的逻辑写在这里
        }

        private void OnDestroy()
        {
            slider.onValueChanged.RemoveListener(HandleSliderChange);
        }
    }
}

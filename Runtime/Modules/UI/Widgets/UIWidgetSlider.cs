using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CoCoFlow.Runtime.Modules.UI.Widgets
{
    public class UIWidgetSlider : UIWidgetBase
    {
        [SerializeField] private Slider slider;
        [SerializeField] private TextMeshProUGUI valueText;

        [Tooltip("数值显示的格式化字符串，例如 '{0}' 或 '{0:F2}'")]
        [SerializeField] private string textFormat = "{0}";

        [Tooltip("是否将 0-1 的值转换为 0-100 显示")]
        [SerializeField] private bool displayAsPercentage = true;

        public event Action<float> OnValueChanged;

        protected override void Awake()
        {
            base.Awake();
            if (slider == null) slider = GetComponentInChildren<Slider>();

            slider.onValueChanged.AddListener(HandleSliderChange);
        }

        private void OnDestroy()
        {
            if (slider != null) slider.onValueChanged.RemoveListener(HandleSliderChange);
        }

        #region Public API
        /// <summary>
        /// 初始化滑动条的数值和范围
        /// </summary>
        public void InitValue(float value, float min = 0f, float max = 1f)
        {
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            UpdateText(value);
        }

        public override void ResetState()
        {
            // 滑动条重置逻辑可根据业务需求实现，通常保持当前值或恢复初始值
        }
        #endregion

        #region Internal Logic
        private void HandleSliderChange(float newValue)
        {
            UpdateText(newValue);
            OnValueChanged?.Invoke(newValue);
        }

        private void UpdateText(float val)
        {
            if (valueText == null) return;

            float displayVal = displayAsPercentage ? val * 100f : val;
            valueText.text = string.Format(textFormat, displayVal);
        }
        #endregion
    }
}


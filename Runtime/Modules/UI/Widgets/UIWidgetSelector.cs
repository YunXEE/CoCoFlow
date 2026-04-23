using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CoCoFlow.Runtime.Modules.UI.Widgets
{
    public class UIWidgetSelector : UIWidgetBase
    {
        [SerializeField] private Button leftArrowBtn;
        [SerializeField] private Button rightArrowBtn;
        [SerializeField] private TextMeshProUGUI optionText;

        public Action<int, string> OnOptionChanged; // 返回 Index 和 对应的字符串

        private List<string> _options = new List<string>();
        private int _currentIndex = 0;

        protected override void Awake()
        {
            base.Awake();
            leftArrowBtn.onClick.AddListener(SelectPrevious);
            rightArrowBtn.onClick.AddListener(SelectNext);
        }

        public void InitOptions(List<string> options, int defaultIndex = 0)
        {
            _options = options;
            _currentIndex = Mathf.Clamp(defaultIndex, 0, _options.Count - 1);
            UpdateUI();
        }

        private void SelectPrevious()
        {
            if (_options.Count == 0) return;
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _options.Count - 1; // 循环切换
            UpdateUI();
            OnOptionChanged?.Invoke(_currentIndex, _options[_currentIndex]);
        }

        private void SelectNext()
        {
            if (_options.Count == 0) return;
            _currentIndex++;
            if (_currentIndex >= _options.Count) _currentIndex = 0; // 循环切换
            UpdateUI();
            OnOptionChanged?.Invoke(_currentIndex, _options[_currentIndex]);
        }

        private void UpdateUI()
        {
            if (_options.Count > 0)
            {
                optionText.text = _options[_currentIndex];
            }
        }

        public override void ResetState()
        {
            _currentIndex = 0;
            UpdateUI();
        }

        private void OnDestroy()
        {
            leftArrowBtn.onClick.RemoveAllListeners();
            rightArrowBtn.onClick.RemoveAllListeners();
        }
    }
}

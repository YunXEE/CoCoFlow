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

        public event Action<int, string> OnOptionChanged;

        private List<string> _options = new List<string>();
        private int _currentIndex;

        protected override void Awake()
        {
            base.Awake();
            if (leftArrowBtn != null) leftArrowBtn.onClick.AddListener(SelectPrevious);
            if (rightArrowBtn != null) rightArrowBtn.onClick.AddListener(SelectNext);
        }

        private void OnDestroy()
        {
            if (leftArrowBtn != null) leftArrowBtn.onClick.RemoveListener(SelectPrevious);
            if (rightArrowBtn != null) rightArrowBtn.onClick.RemoveListener(SelectNext);
        }

        #region Public API
        /// <summary>
        /// 初始化选项列表
        /// </summary>
        public void InitOptions(List<string> options, int defaultIndex = 0)
        {
            _options = options ?? new List<string>();
            _currentIndex = _options.Count > 0 ? Mathf.Clamp(defaultIndex, 0, _options.Count - 1) : 0;
            UpdateUI();
        }

        /// <summary>
        /// 手动设置当前选中的索引
        /// </summary>
        public void SetIndex(int index, bool triggerEvent = true)
        {
            if (_options == null || _options.Count == 0) return;

            _currentIndex = Mathf.Clamp(index, 0, _options.Count - 1);
            UpdateUI();

            if (triggerEvent)
            {
                OnOptionChanged?.Invoke(_currentIndex, _options[_currentIndex]);
            }
        }

        public override void ResetState()
        {
            if (_options is not { Count: > 0}) return;
            _currentIndex = 0;
            UpdateUI();

        }
        #endregion

        #region Internal Logic
        private void SelectPrevious()
        {
            if (_options == null || _options.Count == 0) return;

            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _options.Count - 1;

            UpdateUI();
            OnOptionChanged?.Invoke(_currentIndex, _options[_currentIndex]);
        }

        private void SelectNext()
        {
            if (_options == null || _options.Count == 0) return;

            _currentIndex++;
            if (_currentIndex >= _options.Count) _currentIndex = 0;

            UpdateUI();
            OnOptionChanged?.Invoke(_currentIndex, _options[_currentIndex]);
        }

        private void UpdateUI()
        {
            if (optionText == null) return;

            if (_options is { Count: > 0} && _currentIndex  >= 0 && _currentIndex < _options.Count)
            {
                optionText.text = _options[_currentIndex];
            }
            else
            {
                optionText.text = string.Empty;
            }
        }
        #endregion
    }
}


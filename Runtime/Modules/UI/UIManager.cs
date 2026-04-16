using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Modules.UI
{
    public class UIManager : MonoBehaviour
    {
        [SerializeField] private Transform hudRoot;
        [SerializeField] private Transform panelRoot;
        [SerializeField] private Transform popupRoot;

        // 缓存池：记录已经从 Addressable 加载过的 Prefab
        private readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();
        private readonly Stack<UIPanelBase> _panelStack = new Stack<UIPanelBase>();

        private bool _isTransitioning; // 防狂按锁

        public async UniTask PushPanelAsync(string address)
        {
            if (_isTransitioning) return;
            _isTransitioning = true;

            // 1. 处理底层面板掩藏逻辑 (利用 await 等待旧面板退场完毕)
            if (_panelStack.Count > 0)
            {
                var topPanel = _panelStack.Peek();
                if (topPanel.hideLowerPanels)
                {
                    topPanel.SetInteractable(false);
                    // 如果需要，这里可以 await topPanel.PlayFadeOutAnimAsync();
                }
            }

            // 2. 动态加载 Prefab (Addressables)
            if (!_prefabCache.TryGetValue(address, out GameObject prefab))
            {
                prefab = await Addressables.LoadAssetAsync<GameObject>(address).ToUniTask();
                _prefabCache.Add(address, prefab);
            }

            // 3. 实例化与挂载层级
            GameObject panelObj = Instantiate(prefab);
            UIPanelBase newPanel = panelObj.GetComponent<UIPanelBase>();

            Transform targetRoot = newPanel.layer switch
            {
                UILayer.HUD => hudRoot,
                UILayer.Popup => popupRoot,
                _ => panelRoot
            };

            panelObj.transform.SetParent(targetRoot, false);
            panelObj.transform.SetAsLastSibling();

            // 4. 压栈并播放动画
            _panelStack.Push(newPanel);
            await newPanel.ShowAsync();

            _isTransitioning = false;
        }

        public async UniTask PopPanelAsync()
        {
            if (_isTransitioning || _panelStack.Count == 0) return;
            _isTransitioning = true;

            var currentPanel = _panelStack.Pop();

            // 等待退出动画播完
            await currentPanel.HideAsync();

            // 彻底销毁实例
            Destroy(currentPanel.gameObject);

            // 恢复下一层面板
            if (_panelStack.Count > 0)
            {
                var lowerPanel = _panelStack.Peek();
                if (currentPanel.hideLowerPanels)
                {
                    lowerPanel.SetInteractable(true);
                }
            }

            _isTransitioning = false;
        }
    }
}

using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network.Bootstrap
{
    /// <summary>
    /// Fusion 网络骨架的最小启动入口。
    /// 用于在测试场景最早阶段清理 CoCoServices，避免关闭 Domain Reload 时残留旧网络服务。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [RequireComponent(typeof(CoCoStateMachineController))]
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private bool _clearServicesOnAwake = true;
        [SerializeField] private bool _dontDestroyOnLoad = true;

        private static GameBootstrap _instance;
        private CoCoStateMachineController _stateMachine;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            if (_clearServicesOnAwake)
                CoCoServices.ClearAll();

            _stateMachine = GetComponent<CoCoStateMachineController>();
            CoCoLog.Log("[GameBootstrap] CoCoFlow Core 已初始化。");
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        #region Public API

        public CoCoStateMachineController StateMachine => _stateMachine;

        #endregion
    }
}

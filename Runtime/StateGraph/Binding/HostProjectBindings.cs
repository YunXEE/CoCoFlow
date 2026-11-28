using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>保存当前运行会话的项目 Provider，供各 Host 启动时读取。</summary>
    public static class HostProjectBindings
    {
        #region Public API

        /// <summary>本次运行会话已安装项目绑定时为真。</summary>
        public static bool IsInstalled => _provider != null;

        /// <summary>安装一个具有冻结目录的项目 Provider，供随后启动的 Host 使用。</summary>
        /// <param name="provider">启动图实例时使用的项目绑定。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>安装成功返回真，目录未冻结或已有 Provider 时返回假。</returns>
        public static bool TryInstall(HostBindingProvider provider, out CoCoDiagnostic diagnostic) =>
            TryInstall((IHostBindingProvider)provider, out diagnostic);

        #endregion

        #region Internal Logic

        private static IHostBindingProvider _provider;

        internal static bool TryInstall(
            IHostBindingProvider provider,
            out CoCoDiagnostic diagnostic)
        {
            if (provider == null || provider.Catalog == null || !provider.Catalog.IsFrozen)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "StateGraph project bindings require one provider with a frozen descriptor Catalog.");
                return false;
            }

            if (_provider != null)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "StateGraph project bindings are immutable after the first installation.");
                return false;
            }

            _provider = provider;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static IHostBindingProvider Provider => _provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAtSubsystemRegistration()
        {
            _provider = null;
            InstanceIdAllocator.Reset();
        }

        internal static void ResetForTests()
        {
            ResetAtSubsystemRegistration();
        }

        private static CoCoDiagnostic RegistryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);

        #endregion
    }
}

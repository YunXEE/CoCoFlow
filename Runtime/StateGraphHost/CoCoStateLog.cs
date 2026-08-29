using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Installs the engine-side sink for CoCoStateLog: state scripts in
    /// engine-free assemblies call CoCoStateLog.Print, which forwards into
    /// the package CoCoLog pipeline (level + EventBus) and Debug.Log.
    /// </summary>
    internal static class CoCoStateLogInstaller
    {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            CoCoStateLog.Install(message =>
            {
                CoCoLog.Warning(message.ToString());
            });
        }
    }
}

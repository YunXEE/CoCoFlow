using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraphHost
{
    internal static class CoCoCoreUnityIntegration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializePlaySession()
        {
            CoCoServices.ClearAll();
            Install();
        }

        internal static void Install()
        {
            CoCoLog.WarningSink = Debug.LogWarning;
            CoCoLog.ErrorSink = Debug.LogError;
            CoCoEventBus.IsDestroyedListener = listener =>
                listener is Object unityObject && unityObject == null;
        }
    }
}

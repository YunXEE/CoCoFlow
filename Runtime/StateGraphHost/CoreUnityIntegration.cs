using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    internal static class CoreUnityIntegration
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

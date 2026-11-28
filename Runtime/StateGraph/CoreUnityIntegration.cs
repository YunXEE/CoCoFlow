using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>在 Unity 初始化时安装日志输出和已销毁监听器的判定。</summary>
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

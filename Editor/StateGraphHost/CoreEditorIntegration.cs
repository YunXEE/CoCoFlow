using CoCoFlow.Runtime.StateGraph;
using UnityEditor;

namespace CoCoFlow.Editor.StateGraphHost
{
    internal static class CoreEditorIntegration
    {
        [InitializeOnLoadMethod]
        private static void Initialize() => CoreUnityIntegration.Install();
    }
}

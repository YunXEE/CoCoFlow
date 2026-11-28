using CoCoFlow.Runtime.StateGraph;
using UnityEditor;

namespace CoCoFlow.Editor.StateGraph
{
    internal static class CoreEditorIntegration
    {
        [InitializeOnLoadMethod]
        private static void Initialize() => CoreUnityIntegration.Install();
    }
}

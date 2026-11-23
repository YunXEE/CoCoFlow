using CoCoFlow.Runtime.StateGraphHost;
using UnityEditor;

namespace CoCoFlow.Editor.StateGraphHost
{
    internal static class CoCoCoreEditorIntegration
    {
        [InitializeOnLoadMethod]
        private static void Initialize() => CoCoCoreUnityIntegration.Install();
    }
}

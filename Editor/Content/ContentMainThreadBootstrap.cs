using CoCoFlow.Runtime.Content;
using UnityEditor;

namespace CoCoFlow.Editor.Content
{
    [InitializeOnLoad]
    internal static class ContentMainThreadBootstrap
    {
        static ContentMainThreadBootstrap()
        {
            ContentMainThreadGuard.CaptureCurrentThread();
        }
    }
}

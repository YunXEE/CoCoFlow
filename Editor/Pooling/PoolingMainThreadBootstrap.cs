using CoCoFlow.Runtime.Pooling;
using UnityEditor;

namespace CoCoFlow.Editor.Pooling
{
    [InitializeOnLoad]
    internal static class PoolingMainThreadBootstrap
    {
        static PoolingMainThreadBootstrap()
        {
            PoolingMainThreadGuard.CaptureCurrentThread();
        }
    }
}

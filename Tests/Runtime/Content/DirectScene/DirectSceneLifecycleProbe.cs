using UnityEngine;

namespace CoCoFlow.Runtime.Content.Tests.DirectScene
{
    public sealed class DirectSceneLifecycleProbe : MonoBehaviour
    {
        internal static int AwakeCount { get; private set; }
        internal static int EnableCount { get; private set; }

        internal static void ResetCounts()
        {
            AwakeCount = 0;
            EnableCount = 0;
        }

        private void Awake()
        {
            AwakeCount++;
        }

        private void OnEnable()
        {
            EnableCount++;
        }
    }
}

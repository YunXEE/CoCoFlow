using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    [DisallowMultipleComponent]
    internal sealed class PoolInstanceSentinel : MonoBehaviour
    {
        private PoolEntry owner;
        private long instanceSequence;

        internal void Initialize(PoolEntry owner, long instanceSequence)
        {
            this.owner = owner;
            this.instanceSequence = instanceSequence;
        }

        internal void Detach()
        {
            owner = null;
            instanceSequence = 0;
        }

        private void OnDestroy()
        {
            PoolEntry callback = owner;
            long sequence = instanceSequence;
            owner = null;
            instanceSequence = 0;
            callback?.OnSentinelDestroyed(sequence);
        }
    }
}

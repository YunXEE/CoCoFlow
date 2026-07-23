using System;
using UnityEngine.Pool;

namespace CoCoFlow.Runtime.Pooling
{
    internal sealed class UnityObjectPoolAdapter : IDisposable
    {
        private readonly PoolEntry entry;
        private readonly ObjectPool<PoolInstanceRecord> pool;
        private readonly int maxRetained;
        private bool disposed;

        internal UnityObjectPoolAdapter(
            PoolEntry entry,
            int defaultCapacity,
            int maxRetained)
        {
            this.entry = entry;
            this.maxRetained = maxRetained;
            pool = new ObjectPool<PoolInstanceRecord>(
                entry.CreateInstance,
                entry.OnTakenFromUnityPool,
                entry.OnReleasedToUnityPool,
                entry.OnDestroyedByUnityPool,
                false,
                Math.Max(1, defaultCapacity),
                Math.Max(1, maxRetained));
        }

        internal PoolInstanceRecord Get()
        {
            if (disposed) throw new ObjectDisposedException(nameof(UnityObjectPoolAdapter));

            while (true)
            {
                PoolInstanceRecord record = pool.Get();
                if (record == null)
                {
                    continue;
                }

                if (!entry.ContainsRecord(record))
                {
                    continue;
                }

                if (record.GameObject != null)
                {
                    return record;
                }

                entry.OnInvalidRecordTakenFromUnityPool(record);
            }
        }

        internal void Release(PoolInstanceRecord record)
        {
            if (record == null || !entry.ContainsRecord(record)) return;

            if (disposed || maxRetained == 0)
            {
                entry.OnDestroyedByUnityPool(record);
                return;
            }

            pool.Release(record);
        }

        internal void Clear()
        {
            if (disposed) return;
            pool.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;

            disposed = true;
            pool.Dispose();
        }
    }
}

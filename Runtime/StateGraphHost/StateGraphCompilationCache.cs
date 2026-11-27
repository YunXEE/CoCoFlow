using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.StateGraph
{
    internal readonly struct StateGraphCompilationCacheKey :
        IEquatable<StateGraphCompilationCacheKey>
    {
        public StateGraphCompilationCacheKey(
            GraphId graphId,
            ulong contentFingerprint,
            ulong catalogFingerprint,
            uint compilerSchemaVersion)
        {
            GraphId = graphId;
            ContentFingerprint = contentFingerprint;
            CatalogFingerprint = catalogFingerprint;
            CompilerSchemaVersion = compilerSchemaVersion;
        }

        public GraphId GraphId { get; }
        public ulong ContentFingerprint { get; }
        public ulong CatalogFingerprint { get; }
        public uint CompilerSchemaVersion { get; }

        public bool Equals(StateGraphCompilationCacheKey other)
        {
            return GraphId == other.GraphId &&
                   ContentFingerprint == other.ContentFingerprint &&
                   CatalogFingerprint == other.CatalogFingerprint &&
                   CompilerSchemaVersion == other.CompilerSchemaVersion;
        }

        public override bool Equals(object obj) =>
            obj is StateGraphCompilationCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = GraphId.GetHashCode();
                hashCode = (hashCode * 397) ^ ContentFingerprint.GetHashCode();
                hashCode = (hashCode * 397) ^ CatalogFingerprint.GetHashCode();
                hashCode = (hashCode * 397) ^ CompilerSchemaVersion.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(
            StateGraphCompilationCacheKey left,
            StateGraphCompilationCacheKey right) => left.Equals(right);

        public static bool operator !=(
            StateGraphCompilationCacheKey left,
            StateGraphCompilationCacheKey right) => !left.Equals(right);
    }

    internal interface IStateGraphCompilationCache
    {
        StateGraphAssetCompileResult GetOrAdd(
            StateGraphCompilationCacheKey key,
            Func<StateGraphAssetCompileResult> valueFactory);

        void Clear();
    }

    internal sealed class StateGraphCompilationCache : IStateGraphCompilationCache
    {
        private readonly ConcurrentDictionary<
            StateGraphCompilationCacheKey,
            Lazy<StateGraphAssetCompileResult>> entries =
            new ConcurrentDictionary<
                StateGraphCompilationCacheKey,
                Lazy<StateGraphAssetCompileResult>>();

        internal static StateGraphCompilationCache Shared { get; } =
            new StateGraphCompilationCache();

        public StateGraphAssetCompileResult GetOrAdd(
            StateGraphCompilationCacheKey key,
            Func<StateGraphAssetCompileResult> valueFactory)
        {
            if (valueFactory == null)
            {
                throw new ArgumentNullException(nameof(valueFactory));
            }

            Lazy<StateGraphAssetCompileResult> candidate =
                new Lazy<StateGraphAssetCompileResult>(
                    valueFactory,
                    LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<StateGraphAssetCompileResult> stored = entries.GetOrAdd(key, candidate);
            try
            {
                return stored.Value;
            }
            catch
            {
                TryRemoveIfSame(key, stored);
                throw;
            }
        }

        public void Clear() => entries.Clear();

        internal void TryRemoveIfSame(
            StateGraphCompilationCacheKey key,
            Lazy<StateGraphAssetCompileResult> stored)
        {
            // ConcurrentDictionary's KeyValuePair removal compares both key and value atomically.
            // A waiter observing an older faulted Lazy must not evict a replacement published by
            // another caller after the first waiter removed that faulted entry.
            var entry = new KeyValuePair<
                StateGraphCompilationCacheKey,
                Lazy<StateGraphAssetCompileResult>>(key, stored);
            ((ICollection<KeyValuePair<
                StateGraphCompilationCacheKey,
                Lazy<StateGraphAssetCompileResult>>>)entries).Remove(entry);
        }
    }
}

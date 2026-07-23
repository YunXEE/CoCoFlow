using System;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Runtime.Modules.Map
{
    #region Public API

    /// <summary>
    /// Published after one requester's additive-scene demand owns a live lease.
    /// This notification does not grant release authority.
    /// </summary>
    public struct MapChunkLoadedEvent
    {
        public ContentOwnerId RequesterId;
        public ContentId SceneId;
    }

    #endregion

    public class MapResourceManager : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private CoCoContentHost contentHost;

        private readonly Dictionary<ContentOwnerId, RequesterDemands> _requesters =
            new Dictionary<ContentOwnerId, RequesterDemands>();
        private readonly CancellationTokenSource _destroyCts = new CancellationTokenSource();
        private uint _demandGeneration;
        private bool _isDestroyed;

        #region Public API

        public void DemandScene(ContentOwnerId requesterId, ContentReference sceneSource)
        {
            if (_isDestroyed) return;
            if (!requesterId.IsValid)
            {
                CoCoLog.Error("[MapResourceManager] DemandScene requires a valid Content Owner Id.");
                return;
            }

            if (!sceneSource.IsValid || sceneSource.Kind != ContentKind.AdditiveScene)
            {
                CoCoLog.Error("[MapResourceManager] DemandScene requires a valid Additive Scene ContentReference.");
                return;
            }

            if (contentHost == null)
            {
                CoCoLog.Error("[MapResourceManager] A CoCoContentHost reference is required.");
                return;
            }

            if (!_requesters.TryGetValue(requesterId, out var requester))
            {
                if (!contentHost.TryCreateScope(requesterId, out var scope, out var diagnostic))
                {
                    CoCoLog.Error(
                        $"[MapResourceManager] Unable to create requester Content Scope: {diagnostic}");
                    return;
                }

                requester = new RequesterDemands(scope);
                _requesters.Add(requesterId, requester);
            }

            if (requester.Scenes.TryGetValue(sceneSource.Id, out var existing))
            {
                if (!existing.Source.Equals(sceneSource))
                {
                    CoCoLog.Error(
                        $"[MapResourceManager] Requester {requesterId} already demands " +
                        $"{sceneSource.Id} through a different ContentReference.");
                }

                return;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_destroyCts.Token);
            var demand = new SceneDemand(sceneSource, cancellation, NextDemandGeneration());
            requester.Scenes.Add(sceneSource.Id, demand);
            AcquireSceneAsync(requesterId, requester, demand).Forget();
        }

        public void ReleaseScene(ContentOwnerId requesterId, ContentId sceneId)
        {
            if (!_requesters.TryGetValue(requesterId, out var requester) ||
                !requester.Scenes.TryGetValue(sceneId, out var demand))
            {
                return;
            }

            requester.Scenes.Remove(sceneId);
            demand.CancelAndRelease();
            RemoveRequesterWhenEmpty(requesterId, requester);
        }

        #endregion

        #region Internal Logic

        private void OnDestroy()
        {
            _isDestroyed = true;
            _destroyCts.Cancel();

            foreach (var requester in _requesters.Values)
            {
                requester.Dispose();
            }

            _requesters.Clear();
            _destroyCts.Dispose();
        }

        private async UniTask AcquireSceneAsync(
            ContentOwnerId requesterId,
            RequesterDemands requester,
            SceneDemand demand)
        {
            ContentAcquireResult<Scene> result;
            try
            {
                result = await requester.Scope.AcquireAdditiveSceneAsync(
                    demand.Source,
                    demand.Cancellation.Token);
            }
            catch (Exception ex)
            {
                CoCoLog.Error(
                    $"[MapResourceManager] Unexpected scene acquisition failure for " +
                    $"{demand.Source.Id}: {ex}");
                RemoveFailedDemand(requesterId, requester, demand);
                return;
            }

            if (!IsCurrentDemand(requesterId, requester, demand))
            {
                result.Lease?.Dispose();
                return;
            }

            demand.DisposeCancellation();
            if (!result.Succeeded)
            {
                requester.Scenes.Remove(demand.Source.Id);
                if (!result.Cancelled)
                {
                    CoCoLog.Error(
                        $"[MapResourceManager] Failed to acquire scene {demand.Source.Id}: " +
                        result.Diagnostic);
                }

                RemoveRequesterWhenEmpty(requesterId, requester);
                return;
            }

            demand.Lease = result.Lease;
            var loadedEvent = new MapChunkLoadedEvent
            {
                RequesterId = requesterId,
                SceneId = demand.Source.Id
            };
            CoCoEventBus.Publish(ref loadedEvent);
        }

        private void RemoveFailedDemand(
            ContentOwnerId requesterId,
            RequesterDemands requester,
            SceneDemand demand)
        {
            if (!IsCurrentDemand(requesterId, requester, demand)) return;

            requester.Scenes.Remove(demand.Source.Id);
            demand.CancelAndRelease();
            RemoveRequesterWhenEmpty(requesterId, requester);
        }

        private bool IsCurrentDemand(
            ContentOwnerId requesterId,
            RequesterDemands requester,
            SceneDemand demand)
        {
            return !_isDestroyed &&
                   _requesters.TryGetValue(requesterId, out var currentRequester) &&
                   ReferenceEquals(currentRequester, requester) &&
                   requester.Scenes.TryGetValue(demand.Source.Id, out var currentDemand) &&
                   ReferenceEquals(currentDemand, demand) &&
                   currentDemand.Generation == demand.Generation;
        }

        private void RemoveRequesterWhenEmpty(
            ContentOwnerId requesterId,
            RequesterDemands requester)
        {
            if (requester.Scenes.Count != 0) return;
            if (!_requesters.TryGetValue(requesterId, out var current) ||
                !ReferenceEquals(current, requester))
            {
                return;
            }

            _requesters.Remove(requesterId);
            requester.Dispose();
        }

        private uint NextDemandGeneration()
        {
            _demandGeneration++;
            if (_demandGeneration == 0) _demandGeneration = 1;
            return _demandGeneration;
        }

        private sealed class RequesterDemands : IDisposable
        {
            private bool _isDisposed;

            public RequesterDemands(ContentScope scope)
            {
                Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            }

            public ContentScope Scope { get; }
            public Dictionary<ContentId, SceneDemand> Scenes { get; } =
                new Dictionary<ContentId, SceneDemand>();

            public void Dispose()
            {
                if (_isDisposed) return;
                _isDisposed = true;

                foreach (var demand in Scenes.Values)
                {
                    demand.CancelAndRelease();
                }

                Scenes.Clear();
                Scope.Dispose();
            }
        }

        private sealed class SceneDemand
        {
            public SceneDemand(
                ContentReference source,
                CancellationTokenSource cancellation,
                uint generation)
            {
                Source = source;
                Cancellation = cancellation;
                Generation = generation;
            }

            public ContentReference Source { get; }
            public CancellationTokenSource Cancellation { get; private set; }
            public uint Generation { get; }
            public ContentLease<Scene> Lease { get; set; }

            public void DisposeCancellation()
            {
                Cancellation?.Dispose();
                Cancellation = null;
            }

            public void CancelAndRelease()
            {
                if (Cancellation != null)
                {
                    Cancellation.Cancel();
                    Cancellation.Dispose();
                    Cancellation = null;
                }

                Lease?.Dispose();
                Lease = null;
            }
        }

        #endregion
    }
}

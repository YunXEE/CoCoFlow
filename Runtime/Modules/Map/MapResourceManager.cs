using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    #region Public API

    public struct MapChunkLoadEvent
    {
        public string ChunkAddress;
    }

    public struct MapChunkUnloadEvent
    {
        public string ChunkAddress;
    }

    /// <summary>
    /// 地图区块加载完成事件 — 在 Addressables 场景加载成功后发布，
    /// 用于触发 NavMesh 烘焙等后处理逻辑。
    /// </summary>
    public struct MapChunkLoadedEvent
    {
        public string ChunkAddress;
    }

    #endregion

    public class MapResourceManager : MonoBehaviour
    {
        private readonly HashSet<string> _desiredLoadedChunks = new HashSet<string>();
        private readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> _loadingChunks =
            new Dictionary<string, AsyncOperationHandle<SceneInstance>>();
        private readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> _loadedChunks =
            new Dictionary<string, AsyncOperationHandle<SceneInstance>>();
        private readonly HashSet<string> _unloadingChunks = new HashSet<string>();
        private readonly CancellationTokenSource _destroyCts = new CancellationTokenSource();

        // 声明事件代理
        private readonly EventAgent _eventAgent = new EventAgent();

        #region Internal Logic

        private void OnEnable()
        {
            _eventAgent.Subscribe<MapChunkLoadEvent>(OnLoadChunkReceived);
            _eventAgent.Subscribe<MapChunkUnloadEvent>(OnUnloadChunkReceived);
        }

        private void OnDisable()
        {
            _eventAgent.UnsubscribeAll();
        }

        private void OnDestroy()
        {
            _eventAgent.UnsubscribeAll();
            _destroyCts.Cancel();
            UnloadAllTrackedChunks();
            _destroyCts.Dispose();
        }

        private void OnLoadChunkReceived(ref MapChunkLoadEvent evt)
        {
            LoadChunkAsync(evt.ChunkAddress).Forget();
        }

        private void OnUnloadChunkReceived(ref MapChunkUnloadEvent evt)
        {
            UnloadChunkAsync(evt.ChunkAddress).Forget();
        }

        private async UniTask LoadChunkAsync(string chunkAddress)
        {
            if (string.IsNullOrWhiteSpace(chunkAddress)) return;

            _desiredLoadedChunks.Add(chunkAddress);
            if (_loadedChunks.ContainsKey(chunkAddress) ||
                _loadingChunks.ContainsKey(chunkAddress) ||
                _unloadingChunks.Contains(chunkAddress))
            {
                return;
            }

            var handle = Addressables.LoadSceneAsync(chunkAddress, LoadSceneMode.Additive);
            _loadingChunks[chunkAddress] = handle;

            try
            {
                await handle.ToUniTask(cancellationToken: _destroyCts.Token);
            }
            catch (System.OperationCanceledException)
            {
                if (handle.IsValid())
                {
                    _ = Addressables.UnloadSceneAsync(handle);
                }
                return;
            }
            catch (System.Exception ex)
            {
                CoCoLog.Error($"[MapResourceManager] 加载地图区块 {chunkAddress} 失败: {ex}");
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return;
            }
            finally
            {
                _loadingChunks.Remove(chunkAddress);
            }

            if (_destroyCts.IsCancellationRequested || !_desiredLoadedChunks.Contains(chunkAddress))
            {
                await UnloadSceneHandleAsync(chunkAddress, handle);
                return;
            }

            _loadedChunks[chunkAddress] = handle;

            var loadedEvent = new MapChunkLoadedEvent { ChunkAddress = chunkAddress };
            CoCoEventBus.Publish(ref loadedEvent);
        }

        private async UniTask UnloadChunkAsync(string chunkAddress)
        {
            if (string.IsNullOrWhiteSpace(chunkAddress)) return;

            _desiredLoadedChunks.Remove(chunkAddress);
            if (_loadingChunks.ContainsKey(chunkAddress) || _unloadingChunks.Contains(chunkAddress))
            {
                return;
            }

            if (_loadedChunks.TryGetValue(chunkAddress, out var handle))
            {
                _loadedChunks.Remove(chunkAddress);
                await UnloadSceneHandleAsync(chunkAddress, handle);
            }
        }

        private async UniTask UnloadSceneHandleAsync(string chunkAddress, AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid()) return;

            _unloadingChunks.Add(chunkAddress);

            try
            {
                await Addressables.UnloadSceneAsync(handle).ToUniTask();
            }
            catch (System.Exception ex)
            {
                CoCoLog.Error($"[MapResourceManager] 卸载地图区块 {chunkAddress} 失败: {ex}");
            }
            finally
            {
                _unloadingChunks.Remove(chunkAddress);
            }

            if (!_destroyCts.IsCancellationRequested && _desiredLoadedChunks.Contains(chunkAddress))
            {
                LoadChunkAsync(chunkAddress).Forget();
            }
        }

        private void UnloadAllTrackedChunks()
        {
            _desiredLoadedChunks.Clear();

            foreach (var handle in _loadingChunks.Values)
            {
                if (handle.IsValid())
                {
                    _ = Addressables.UnloadSceneAsync(handle);
                }
            }

            foreach (var handle in _loadedChunks.Values)
            {
                if (handle.IsValid())
                {
                    _ = Addressables.UnloadSceneAsync(handle);
                }
            }

            _loadingChunks.Clear();
            _loadedChunks.Clear();
            _unloadingChunks.Clear();
        }

        #endregion
    }
}

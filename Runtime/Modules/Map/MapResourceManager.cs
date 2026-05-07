using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    public struct MapChunkLoadEvent
    {
        public string ChunkAddress;
    }

    public struct MapChunkUnloadEvent
    {
        public string ChunkAddress;
    }

    public class MapResourceManager : MonoBehaviour
    {
        private readonly Dictionary<string, SceneInstance> _loadedChunks = new Dictionary<string, SceneInstance>();

        // 声明事件代理
        private readonly EventAgent _eventAgent = new EventAgent();

        private void OnEnable()
        {
            _eventAgent.Subscribe<MapChunkLoadEvent>(OnLoadChunkReceived);
            _eventAgent.Subscribe<MapChunkUnloadEvent>(OnUnloadChunkReceived);
        }

        private void OnDisable()
        {
            _eventAgent.UnsubscribeAll();
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
            if (_loadedChunks.ContainsKey(chunkAddress)) return;

            var sceneInstance = await Addressables.LoadSceneAsync(chunkAddress, LoadSceneMode.Additive).ToUniTask();
            _loadedChunks.Add(chunkAddress, sceneInstance);
        }

        private async UniTask UnloadChunkAsync(string chunkAddress)
        {
            if (_loadedChunks.TryGetValue(chunkAddress, out var instance))
            {
                await Addressables.UnloadSceneAsync(instance).ToUniTask();
                _loadedChunks.Remove(chunkAddress);
            }
        }
    }
}

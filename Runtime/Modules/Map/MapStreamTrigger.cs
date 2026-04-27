using UnityEngine;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    [RequireComponent(typeof(BoxCollider))]
    public class MapStreamTrigger : MonoBehaviour
    {
        [Header("玩家进入该区域时：")]
        public List<string> scenesToLoadOnEnter;
        public List<string> scenesToUnloadOnEnter;

        public float triggerCooldown = 2.0f;
        private float _lastTriggerTime;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") && Time.time - _lastTriggerTime > triggerCooldown)
            {
                _lastTriggerTime = Time.time;
                ExecuteStreaming();
            }
        }

        private void ExecuteStreaming()
        {
            foreach (var scene in scenesToLoadOnEnter)
            {
                CoCoEventBus.Publish(new MapChunkLoadEvent { ChunkAddress = scene });
            }

            foreach (var scene in scenesToUnloadOnEnter)
            {
                CoCoEventBus.Publish(new MapChunkUnloadEvent { ChunkAddress = scene });
            }
        }
    }
}

using System.Collections.Generic;
using CoCoFlow.Runtime.Content;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [RequireComponent(typeof(BoxCollider))]
    public class MapStreamTrigger : MonoBehaviour
    {
        [Header("Content Ownership")]
        [SerializeField] private MapResourceManager resourceManager;
        [SerializeField] private ContentOwnerId requesterId;

        [Header("Player Enter Demands")]
        [SerializeField] private List<ContentReference> scenesToLoadOnEnter =
            new List<ContentReference>();
        [SerializeField] private List<ContentId> sceneIdsToReleaseOnEnter =
            new List<ContentId>();

        [SerializeField] private float triggerCooldown = 2.0f;
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
            if (resourceManager == null)
            {
                Debug.LogError("[MapStreamTrigger] A target MapResourceManager is required.", this);
                return;
            }

            if (!requesterId.IsValid)
            {
                Debug.LogError("[MapStreamTrigger] A valid Content Owner Id is required.", this);
                return;
            }

            foreach (var scene in scenesToLoadOnEnter)
            {
                resourceManager.DemandScene(requesterId, scene);
            }

            foreach (var sceneId in sceneIdsToReleaseOnEnter)
            {
                resourceManager.ReleaseScene(requesterId, sceneId);
            }
        }
    }
}

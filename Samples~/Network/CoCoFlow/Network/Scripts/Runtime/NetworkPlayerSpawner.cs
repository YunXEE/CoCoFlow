using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Addon.Network.Events;
using Fusion;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network
{
    /// <summary>
    /// Fusion 玩家对象生成器。
    /// Host/Shared Master 负责为每个 PlayerRef 生成 NetworkObject，并绑定 Runner.PlayerObject。
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class NetworkPlayerSpawner : MonoBehaviour
    {
        [Header("Player Prefab")]
        [SerializeField] private NetworkObject _playerPrefab;

        [Header("Spawn Points")]
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private Vector3 _fallbackSpawnOrigin;
        [SerializeField] private float _fallbackSpawnSpacing = 2f;

        [Header("Scene Timing")]
        [SerializeField] private bool _waitForSceneReady;
        [SerializeField] private bool _spawnExistingPlayersOnSceneReady = true;

        private readonly EventAgent _eventAgent = new EventAgent();
        private readonly HashSet<PlayerRef> _pendingPlayers = new HashSet<PlayerRef>();
        private readonly Dictionary<PlayerRef, NetworkObject> _spawnedObjects = new Dictionary<PlayerRef, NetworkObject>();

        private INetworkRunnerProvider _runnerProvider;
        private IDisposable _runnerProviderWait;
        private bool _isSceneReady;

        #region Public API

        public void SetPlayerPrefab(NetworkObject playerPrefab)
        {
            _playerPrefab = playerPrefab;
        }

        public void SpawnMissingPlayers()
        {
            var runner = GetRunner();
            if (runner == null || !CanSpawnPlayers()) return;

            foreach (var player in runner.ActivePlayers)
                QueueOrSpawn(player);
        }

        public bool TrySpawnPlayer(PlayerRef player)
        {
            return SpawnPlayer(player) != null;
        }

        public void DespawnPlayer(PlayerRef player)
        {
            var runner = GetRunner();
            if (runner == null || !CanSpawnPlayers()) return;

            var playerObject = runner.GetPlayerObject(player);
            if (playerObject == null && _spawnedObjects.TryGetValue(player, out var trackedObject))
                playerObject = trackedObject;

            if (playerObject == null) return;

            runner.SetPlayerObject(player, null);
            runner.Despawn(playerObject);
            _spawnedObjects.Remove(player);

            var despawned = new NetPlayerObjectDespawnedEvent { Player = player, Object = playerObject };
            CoCoEventBus.Publish(ref despawned);

            var legacy = new NetCharacterDestroyedEvent { Player = player };
            CoCoEventBus.Publish(ref legacy);
        }

        #endregion

        #region Internal Logic

        private void Awake()
        {
            _runnerProviderWait = CoCoServices.WaitFor<INetworkRunnerProvider>(provider =>
            {
                _runnerProvider = provider;
                TrySpawnPendingPlayers();
            });
        }

        private void OnEnable()
        {
            _eventAgent.Subscribe<NetPlayerJoinedEvent>(OnPlayerJoined);
            _eventAgent.Subscribe<NetPlayerLeftEvent>(OnPlayerLeft);
            _eventAgent.Subscribe<NetSceneReadyEvent>(OnSceneReady);
            _eventAgent.Subscribe<NetShutdownEvent>(OnShutdown);
        }

        private void OnDisable()
        {
            _eventAgent.UnsubscribeAll();
        }

        private void OnDestroy()
        {
            _runnerProviderWait?.Dispose();
            _eventAgent.UnsubscribeAll();
        }

        private void OnPlayerJoined(ref NetPlayerJoinedEvent evt)
        {
            QueueOrSpawn(evt.Player);
        }

        private void OnPlayerLeft(ref NetPlayerLeftEvent evt)
        {
            _pendingPlayers.Remove(evt.Player);
            DespawnPlayer(evt.Player);
        }

        private void OnSceneReady(ref NetSceneReadyEvent evt)
        {
            _isSceneReady = true;

            if (_spawnExistingPlayersOnSceneReady)
                SpawnMissingPlayers();

            TrySpawnPendingPlayers();
        }

        private void OnShutdown(ref NetShutdownEvent evt)
        {
            _pendingPlayers.Clear();
            _spawnedObjects.Clear();
            _isSceneReady = false;
        }

        private void QueueOrSpawn(PlayerRef player)
        {
            if (!player.IsRealPlayer) return;
            if (!CanSpawnPlayers()) return;

            if (_waitForSceneReady && !_isSceneReady)
            {
                _pendingPlayers.Add(player);
                return;
            }

            SpawnPlayer(player);
        }

        private void TrySpawnPendingPlayers()
        {
            if (!CanSpawnPlayers()) return;
            if (_waitForSceneReady && !_isSceneReady) return;
            if (_pendingPlayers.Count == 0) return;

            var players = new List<PlayerRef>(_pendingPlayers);
            _pendingPlayers.Clear();

            foreach (var player in players)
                SpawnPlayer(player);
        }

        private NetworkObject SpawnPlayer(PlayerRef player)
        {
            var runner = GetRunner();
            if (runner == null || !CanSpawnPlayers()) return null;

            if (_playerPrefab == null)
            {
                CoCoLog.Error("[NetworkPlayerSpawner] 未配置 Player Prefab，无法生成玩家对象。");
                return null;
            }

            var existingObject = runner.GetPlayerObject(player);
            if (existingObject != null)
            {
                _spawnedObjects[player] = existingObject;
                return existingObject;
            }

            GetSpawnPose(player, out var position, out var rotation);
            var spawnedObject = runner.Spawn(_playerPrefab, position, rotation, player);
            if (spawnedObject == null)
            {
                CoCoLog.Error($"[NetworkPlayerSpawner] 玩家 {player} 生成失败，请确认 Prefab 已注册到 Fusion Prefab Table。");
                return null;
            }

            runner.SetPlayerObject(player, spawnedObject);
            _spawnedObjects[player] = spawnedObject;

            var spawned = new NetPlayerObjectSpawnedEvent { Player = player, Object = spawnedObject };
            CoCoEventBus.Publish(ref spawned);

            var legacy = new NetCharacterSpawnedEvent { Player = player, Object = spawnedObject };
            CoCoEventBus.Publish(ref legacy);

            return spawnedObject;
        }

        private void GetSpawnPose(PlayerRef player, out Vector3 position, out Quaternion rotation)
        {
            var spawnPoint = GetSpawnPoint(player);
            if (spawnPoint != null)
            {
                position = spawnPoint.position;
                rotation = spawnPoint.rotation;
                return;
            }

            var index = player.GetHashCode() & 0x7fffffff;
            var x = index % 4;
            var z = index / 4 % 4;
            position = _fallbackSpawnOrigin + new Vector3(x * _fallbackSpawnSpacing, 0f, z * _fallbackSpawnSpacing);
            rotation = Quaternion.identity;
        }

        private Transform GetSpawnPoint(PlayerRef player)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
                return null;

            var startIndex = (player.GetHashCode() & 0x7fffffff) % _spawnPoints.Length;
            for (var i = 0; i < _spawnPoints.Length; i++)
            {
                var candidate = _spawnPoints[(startIndex + i) % _spawnPoints.Length];
                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        private bool CanSpawnPlayers()
        {
            return _runnerProvider != null && _runnerProvider.CanSpawnPlayerObjects;
        }

        private NetworkRunner GetRunner()
        {
            return _runnerProvider?.Runner;
        }

        #endregion
    }
}

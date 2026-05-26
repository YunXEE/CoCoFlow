#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Input;
using CoCoFlow.Runtime.Addon.Network.Bootstrap;
using CoCoFlow.Runtime.Addon.Network.Character;
using CoCoFlow.Runtime.Addon.Network.Input;
using CoCoFlow.Runtime.Addon.Network.UI;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Editor.Addon.Network
{
    /// <summary>
    /// 生成网络作业验证所需的最小场景和玩家预制体。
    /// 输出路径固定到 Assets/CoCoFlow/Network，便于从 Add-on 导入后继续迁移。
    /// </summary>
    public static class NetworkTestScaffoldBuilder
    {
        private const string RootDirectory = "Assets/CoCoFlow/Network";
        private const string PrefabDirectory = RootDirectory + "/Prefabs";
        private const string PlayerPrefabPath = PrefabDirectory + "/NetPlayer.prefab";
        private const string SceneDirectory = RootDirectory + "/Scenes";
        private const string ScenePath = SceneDirectory + "/NetworkTestArena.unity";
        private const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";
        private const string FusionPrefabLabel = "FusionPrefab";
        private const string FusionConfigPath = "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion";

        [MenuItem("CoCoFlow/Network/Rebuild Test Scaffold")]
        public static void RebuildTestScaffold()
        {
            EnsureFolder(RootDirectory);
            EnsureFolder(PrefabDirectory);
            EnsureFolder(SceneDirectory);

            var playerPrefab = BuildPlayerPrefab();
            var sceneIndex = BuildScene(playerPrefab);
            RebuildFusionPrefabTable();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[NetworkTestScaffoldBuilder] NetworkTestArena 与 NetPlayer 已生成，Game Scene Build Index={sceneIndex}。");
        }

        private static NetworkObject BuildPlayerPrefab()
        {
            var root = new GameObject("NetPlayer");
            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>();

            var characterController = root.AddComponent<CharacterController>();
            characterController.height = 1.8f;
            characterController.radius = 0.28f;
            characterController.center = new Vector3(0f, 0.9f, 0f);

            root.AddComponent<NetCharacterMotor>();
            root.AddComponent<NetCharacter>();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);

            AddLabel(PlayerPrefabPath, FusionPrefabLabel);
            return prefab.GetComponent<NetworkObject>();
        }

        private static int BuildScene(NetworkObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootstrap = new GameObject("GameBootstrap");
            bootstrap.AddComponent<GameBootstrap>();
            var bootstrapStateMachine = bootstrap.GetComponent<CoCoStateMachineController>();
            if (bootstrapStateMachine == null)
                bootstrapStateMachine = bootstrap.AddComponent<CoCoStateMachineController>();
            var bootstrapStateObject = new GameObject("State_Ready");
            bootstrapStateObject.transform.SetParent(bootstrap.transform);
            var bootstrapReadyState = bootstrapStateObject.AddComponent<GameBootstrapReadyState>();
            SetObjectField(bootstrapStateMachine, "defaultCoCoState", bootstrapReadyState);

            var input = new GameObject("Input");
            var inputReader = input.AddComponent<InputReader>();
            inputReader.inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);

            var network = new GameObject("Network");
            network.AddComponent<NetManager>();
            network.AddComponent<NetworkInputBridge>();
            var spawner = network.AddComponent<NetworkPlayerSpawner>();
            spawner.SetPlayerPrefab(playerPrefab);
            SetObjectField(spawner, "_playerPrefab", playerPrefab);
            EditorUtility.SetDirty(spawner);

            var spawnRoot = new GameObject("SpawnPoints");
            var spawnPoints = new List<Transform>();
            for (var i = 0; i < 4; i++)
            {
                var point = new GameObject($"SpawnPoint_{i + 1}");
                point.transform.SetParent(spawnRoot.transform);
                point.transform.position = new Vector3((i % 2) * 3f, 0f, (i / 2) * 3f);
                spawnPoints.Add(point.transform);
            }
            SetObjectArray(spawner, "_spawnPoints", spawnPoints);

            var hud = new GameObject("NetworkTestHud");
            var networkTestHud = hud.AddComponent<NetworkTestHud>();

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.localScale = new Vector3(2f, 1f, 2f);

            var cameraObj = new GameObject("Main Camera");
            cameraObj.tag = "MainCamera";
            cameraObj.transform.position = new Vector3(4f, 6f, -8f);
            cameraObj.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
            cameraObj.AddComponent<Camera>();
            cameraObj.AddComponent<AudioListener>();

            var lightObj = new GameObject("Directional Light");
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            EditorSceneManager.SaveScene(scene, ScenePath);
            var sceneIndex = AddSceneToBuildSettings(ScenePath);
            SetIntField(networkTestHud, "_gameSceneBuildIndex", sceneIndex);
            EditorUtility.SetDirty(networkTestHud);
            EditorSceneManager.SaveScene(scene, ScenePath);

            return sceneIndex;
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void AddLabel(string assetPath, string label)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null) return;

            var labels = AssetDatabase.GetLabels(asset).ToList();
            if (!labels.Contains(label))
            {
                labels.Add(label);
                AssetDatabase.SetLabels(asset, labels.ToArray());
            }
        }

        private static void SetObjectField(Object target, string propertyName, Object value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) return;

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetIntField(Object target, string propertyName, int value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) return;

            property.intValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetObjectArray(Object target, string propertyName, IReadOnlyList<Transform> values)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) return;

            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static int AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var existingIndex = scenes.FindIndex(scene => scene.path == scenePath);
            if (existingIndex >= 0)
                return existingIndex;

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return scenes.Count - 1;
        }

        private static void RebuildFusionPrefabTable()
        {
            NetworkProjectConfigUtilities.RebuildPrefabTable();

            if (AssetDatabase.LoadAssetAtPath<Object>(FusionConfigPath) == null) return;

            AssetDatabase.ImportAsset(FusionConfigPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
#endif

# Network Add-on

这是一个可选 Photon Fusion 网络 Add-on，用于迁移参考。它通过 Unity Package Manager 的 `Samples~` 机制分发，不属于 CoCoFlow 主包默认编译内容；需要时请在 Package Manager 的 Samples 面板导入到宿主项目。

## 定位

- 跑通 Host / Client 房间流程。
- 生成并绑定 Fusion `PlayerObject`。
- 通过 CoCoFlow `CoCoServices` 与 `CoCoEventBus` 暴露网络状态。
- 提供最小 Lobby UI 连接：Create Room、Join Room、Start Game、Connection Status。

Shared Mode 只保留入口，不作为首轮验收目标。

## 依赖

宿主项目需要先具备：

- Unity 6000+
- CoCoFlow 0.3.1
- Photon Fusion 2.x
- UniTask
- TextMeshPro
- Unity Input System

Fusion 不是 CoCoFlow 主包依赖。导入本 Add-on 前，请先在宿主项目安装 Photon Fusion SDK，并配置可用的 Photon AppId。

## 导入方式

1. 打开 Unity Package Manager。
2. 选择 `CoCoFlow` 包。
3. 展开 `Samples`。
4. 点击 `Add-on: Network` 的 `Import`。
5. Unity 会先把 Add-on 放到 `Assets/Samples/CoCoFlow/0.3.1/Network/` 下。
6. Add-on 内部带有独立根目录：`CoCoFlow/Network/`。如果这是正式作业项目，建议把这个目录整体移动到 `Assets/CoCoFlow/Network/`，后续业务代码再放到 `Assets/Game/` 或 `Assets/Scripts/`。

导入后 Add-on 会编译成：

- `CoCoFlow.Runtime.Addon.Network`
- `CoCoFlow.Editor.Addon.Network`
- `CoCoFlow.Runtime.Addon.Network.Tests`

## Fusion 配置

在宿主项目中确认以下配置：

1. 创建或打开 `NetworkProjectConfig`。
2. 在 `Assemblies To Weave` 加入：

   ```text
   CoCoFlow.Runtime.Addon.Network
   ```

3. 配置 Photon AppId。
4. 将 `NetPlayer.prefab` 注册到 Fusion Prefab Table。
5. 确认 `NetPlayerInput` 被 Weaver 正确处理；配置完成后 `InputDataWordCount` 不应为 0。

如果没有把 Add-on assembly 加入 Weaver，带 `[Networked]` 或 `[Rpc]` 的脚本会在运行时失败。

## 最小测试场景

创建一个测试场景，例如 `NetworkTestArena`，并加入 Build Profiles 的 Scene List。

导入后也可以直接执行菜单：

```text
CoCoFlow/Network/Rebuild Test Scaffold
```

该工具会在宿主项目中生成：

```text
Assets/CoCoFlow/Network/
  Prefabs/NetPlayer.prefab
  Scenes/NetworkTestArena.unity
```

场景里至少需要：

- `GameBootstrap`：初始化 `CoCoServices`、`CoCoEventBus`、基础状态机。
- `InputReader`：提供 CoCoFlow 输入服务。
- `NetManager`：负责 Fusion Host / Client / Shared 启动。
- `NetworkInputBridge`：和 `NetManager` 放在同一个 GameObject 上。
- `NetworkPlayerSpawner`：配置 `Player Prefab` 和可选出生点。
- Lobby UI：绑定 `NetLobbyPanel` 的按钮、输入框、状态文本和玩家数量文本。

`NetManager.StartGame` 需要有效的场景索引。请在 `NetLobbyPanel` 的 `Game Scene Build Index` 填入 Build Profiles 中的游戏场景索引；未配置时按钮会提示需要先配置场景，不会只在 Host 本地假显示开始。

## NetPlayer 预制体

创建空 GameObject，命名为 `NetPlayer`，添加 `NetPlayerPrefabSetup`，然后在 Inspector 中执行 Reset。它会补齐：

- `NetworkObject`
- `NetworkTransform`
- `CharacterController`
- `NetCharacterMotor`
- `CharacterLifeCycle`
- `Animator`
- `NetworkMecanimAnimator`
- `NetCharacter`
- `NetStateSyncHandler`
- `NetCharacterLifecycle`
- `NetAnimatorSync`

创建 prefab 后，把它拖到 `NetworkPlayerSpawner.Player Prefab`，并注册到 Fusion Prefab Table。

## 手动验收

推荐验证顺序：

1. Editor Play 作为 Host，点击 `Create Room`。
2. macOS build 或第二个客户端启动，输入相同房间名，点击 `Join Room`。
3. 观察两端 UI 显示已连接、玩家数量正确。
4. Host 端确认每个 `PlayerRef` 都有 `Runner.GetPlayerObject(player)`。
5. 所有客户端都能看到 2-3 个玩家对象。
6. Host 点击 `Start Game` 后，客户端跟随进入同一 Fusion 网络场景。
7. Client 断开后，Host despawn 对应玩家对象，UI 状态更新。

## 已知限制

- 这是可选 Add-on，不是 CoCoFlow 正式 Network 模块。
- Host / Client 是主验收路径；Shared Mode 只是最小入口。
- Lobby UI 只做最小可用，不包含房间列表交互。
- 自动化测试主要是 smoke test，不能替代多客户端手动验收。
- Adventure 示例代码不属于迁移范围。

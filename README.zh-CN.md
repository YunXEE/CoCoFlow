# CoCoFlow

[English](README.md) | [简体中文](README.zh-CN.md)

CoCoFlow 是一个面向 Unity 的模块化游戏开发框架，围绕 Context 驱动的 gameplay、显式 State Layer、可复用组件、持久化、编辑器工具和可选 samples 构建。

> **版本**: 0.3.9 · **Unity**: 6000+

## 包范围

CoCoFlow 提供一套 runtime 基础设施，用于把 gameplay 代码组织在明确的 Context 契约和状态机拓扑之上。这个包关注可复用的框架表面，不提供完整游戏功能。

当前包包含：

- Core services、事件总线、Context 契约和 State Layer runtime。
- Character、Enemy、Item gameplay 基础能力。
- Input、Camera、UI、Animation、Map、Rendering、Persistence 模块。
- Setup、状态图查看、持久化存档槽位、Catalog 编辑等编辑器工具。
- Player、Enemy、Chest、Rigging、Network 相关可选 samples。

## Runtime 拓扑

```text
CoCoFlow
│
├── Runtime
│   ├── Core
│   │   ├── CoCoServices
│   │   ├── CoCoEventBus
│   │   ├── ICoCoContext / ICoCoContextProvider<TContext>
│   │   ├── CoCoStateController / CoCoStateLayer / CoCoStateBase
│   │   └── CoCoStateDefinition
│   │
│   ├── Modules
│   │   ├── Input
│   │   ├── Camera
│   │   ├── UI
│   │   ├── Animation
│   │   ├── Map
│   │   ├── Rendering
│   │   └── Persistence
│   │
│   └── Gameplay
│       ├── Character
│       ├── Enemy
│       └── Item
│
└── Editor
    ├── Core
    ├── AssetPipeline
    ├── Modules
    └── Gameplay
```

## 核心概念

| 概念 | 说明 |
|---|---|
| Context | 可持久化的类型化 gameplay 数据契约，通过 `ICoCoContextProvider<TContext>` 暴露。 |
| State Layer | 由一个 `CoCoStateController` 拥有的命名状态机平面。多个 layer 可以按显式顺序执行。 |
| State Definition | 状态声明的元数据，用于描述 Context 读写、外部操作依赖和状态跳转。 |
| Event Bus | 类型化事件分发系统，可配合 event envelope 做跨系统通信。 |
| Persistence Context | 场景实体快照路径，用于还原由 Context 驱动的状态机实体。 |
| Persistence Container | 基于 Catalog 的 runtime 数据路径，用于 inventory、quest、event、fact、reward 和 tag。 |

## 模块

| 模块 | 状态 | 摘要 |
|---|---|---|
| Core | 稳定基础 | Service locator、事件总线、Context 契约、State Layer controller、state definition 和日志。 |
| Input | 可用基础 | Input reader 和 input intent 契约。 |
| Camera | 活跃基础 | 本地第三人称 Cinemachine rig 调度、玩家内置相机、AimCore 耦合、观战优先级和 cutscene 接管边界。 |
| UI | 可用基础 | View/controller 抽象和 panel stack 管理。 |
| Animation | 活跃基础 | Animator 辅助、animation event state machine behaviour、编辑器注入工具，以及 Rig 脚部 IK / 脚步锁定 operation 组件。 |
| Map | 可用基础 | Map manager 和 chunk loading 支持。 |
| Rendering | 工具层 | Rendering quality 辅助能力。 |
| Persistence | 活跃模块 | 版本化存档文档、临时文件 JSON 写入、Context 快照、Container 数据、Catalog 编辑器和存档槽位工具。 |
| Gameplay.Character | 活跃基础 | Character context provider、input driver、lifecycle writer、locomotion 和 navigation motor。 |
| Gameplay.Enemy | 活跃基础 | Enemy brain、spline navigation source、vision query 和 engagement zone。 |
| Gameplay.Item | 活跃基础 | Item context、item context provider、input driver 和 item lifecycle writer。 |

## Persistence

Persistence 在每个存档文档中维护两个 section：

- `contextSection`：通过 `PersistenceContext` 捕获的场景实体 Context 快照。
- `containerSection`：通过 `PersistenceContainerStore` 捕获的 runtime container 数据。

这个模块提供手动存读档入口、存档槽位 metadata、schema migration 入口、临时文件替换写入、Catalog 编辑器，以及用于 container command 的 bridge。

更多设置、数据流和示例用法见 [Module: Persistence](Docs/Module-Persistence.md)。

## Camera

Camera 是只服务本地表现层的第三人称相机模块。它不同步玩法状态，也不重写 Cinemachine 3 的镜头算法。当前模型收束为 Director/Rig：`CameraDirector` 按 active + priority 调度一组 `CameraRig`，每个 `CameraRig` 持有 Free/Aim/Lock/Spectate/Focus/Custom 等 Cinemachine virtual cameras，并把当前相机暴露给 Director。

TPS Aim 通过玩家内部 AimCore 上的 `CameraAimCoupler` 处理。State Layer 显式切换 rig mode 和 coupled 开关；每台 Cinemachine camera 的 Follow/LookAt/ThirdPersonFollow target 仍在 Inspector 里直接配置。

详细拓扑、Scene 组装、AimCore 设置、观战优先级、联机绑定和 cutscene 交接见 [Module-Camera](Docs/Module-Camera.md)。

## Animation

Animation 包含轻量 Animator/SMB 工具层，以及 0.3.9 新增的 Rig 子能力：脚部 IK target 驱动和脚步锁定。Rig 和 `CharacterLocomotion` 一样是 operation component：State Layer 脚本只在需要的状态显式启用 Foot IK，`AnimRigCharacterController` 作为门面 API，`AnimRigFootDriver` 负责更新 target transforms、动画曲线驱动的脚步锁定状态和调试 gizmos。

详细拓扑、Prefab 接线和 State Layer 接入方式见 [Module-Animation](Docs/Module-Animation.md)。

## 依赖

| Package | Version | 用途 |
|---|---:|---|
| Addressables | 2.9.1 | 包 runtime/editor 工作流 |
| Input System | 1.18.0 | Input 模块 |
| Newtonsoft Json | 3.2.2 | Persistence |
| Cinemachine | 3.1.6 | Camera 模块 |
| AI Navigation | 2.0.0 | Character navigation 和 Enemy samples |
| Mathematics | 1.3.3 | Enemy 和 spline 相关工作流 |
| Splines | 2.6.0 | Enemy spline 支持 |
| Animation Rigging | 项目自行安装 | 将 Animation Rig target 接到 Unity constraints 的项目或 sample |

项目可以按 sample 或业务模块需求自行安装可选第三方包。它们不属于 core runtime 的内置表面。

## 安装

可以通过 Unity Package Manager 使用 Git URL 安装，也可以把包放入 Unity 项目的 `Packages/` 目录。

安装后：

1. 打开 `CoCoFlow/Setup/Setup Assistant`。
2. 检查必需 package dependencies。
3. 按需安装可选 samples。
4. 使用 `CoCoFlow/State/State Graph Viewer` 查看 `CoCoStateController`。
5. 使用 `CoCoFlow/Persistence/Catalog Editor` 编辑 persistence catalog asset。

## Samples

| Sample | 导入路径 | 用途 |
|---|---|---|
| Player Samples | `Assets/CoCoFlow/Player` | 演示带有 `CharacterContextProvider`、locomotion 和显式 State Layers 的 player prefab。 |
| Enemy Samples | `Assets/CoCoFlow/Enemy` | 演示 enemy context、brain、spline navigation、state scripts 和 prefab 接线。 |
| Chest Samples | `Assets/CoCoFlow/Chest` | 演示 chest prefab 和 runtime container store 下的 Persistence Context / Container 双路径。 |
| Network Samples | `Assets/CoCoFlow/Network` | 记录 network adapter 边界，并提供 container event bridge 和本地 camera rig binding samples，不引入 network package 依赖。 |
| Rigging Samples | `Assets/CoCoFlow/Rigging` | 演示从 State Layer 风格脚本调用 Animation 模块的脚部 IK 和脚步锁定 operation。 |

Samples 是集成参考，不是完整游戏模板。

## 编辑器工具

| 菜单 | 用途 |
|---|---|
| `CoCoFlow/Setup/Setup Assistant` | 依赖状态检查和可选 sample setup。 |
| `CoCoFlow/State/State Graph Viewer` | 只读查看 controller、layer、state、Context 使用、operation 和 transition。 |
| `CoCoFlow/Persistence/Save Editor` | 本地测试用的手动 save/load slot 工具。 |
| `CoCoFlow/Persistence/Catalog Editor` | Persistence catalog definitions 的分页编辑器。 |
| `CoCoFlow/Persistence/Validate Selected Catalog` | Catalog ID 和引用校验。 |

## 文档

- [Context / Network Boundary](Docs/ContextNetworkBoundary.md)
- [Module: Animation](Docs/Module-Animation.md)
- [Module: Camera](Docs/Module-Camera.md)
- [Module: Persistence](Docs/Module-Persistence.md)
- [Network Context Sync Plan](Samples~/Network%20Samples/CoCoFlow/Network/Docs/ContextSyncPlan.md)

## License

MIT

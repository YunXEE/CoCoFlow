# CoCoFlow

模块化 Unity 游戏开发框架。

> **版本**: 0.3.5 · **Unity**: 6000+

---

## 当前骨架

CoCoFlow 0.3.5 的 gameplay 主线已经收紧到一套可预测的骨架：

- 一个角色或实体使用一份权威 `ICoCoContext`。
- 本地角色默认由 `CharacterContextProvider` 持有 `CharacterContext`。
- `CharacterContextProvider` 可以显式接入多个 `ICharacterContextSource`，按 `Priority` 从低到高写入；相同 Priority 按 Inspector List 声明顺序写入；disabled 或 inactive source 会被跳过。
- 一个状态系统使用一个 `CoCoStateController`。
- `CoCoStateController` 持有一组显式声明的 `CoCoStateLayer`。每个 Layer 是一个独立状态面，按 `Order` 从上到下执行。
- `CoCoStateBase` 通过 `DefineState` 声明 Context 读写、外部操作依赖和可能的状态跳转。
- State Graph Viewer 是只读拓扑查看器，用于查看 Controller、State Layer、State、Context、Operation 和 transition 关系。

这个版本不再依赖自动扫描子状态或嵌套 controller。所有可运行状态必须显式放进对应的 `CoCoStateLayer`。

---

## 架构拓扑

```text
CoCoFlow v0.3.5
│
├── CoCoFlow.Runtime
│   ├── Core
│   │   ├── CoCoServices
│   │   ├── CoCoEventBus
│   │   ├── CoCoStateController / CoCoStateLayer / CoCoStateBase
│   │   ├── CoCoStateDefinition
│   │   └── CoCoLog
│   │
│   ├── Modules
│   │   ├── Input        - InputReader + CoCoInputIntent
│   │   ├── Camera       - ThirdPersonCamera / CameraRig / CameraController
│   │   ├── UI           - UIController / UIViewManager / panel stack
│   │   ├── Animation    - AnimHandler / AnimEventSmb / SMB injector support
│   │   ├── Map          - MapManager / chunk loading
│   │   ├── Rendering    - URP quality helpers
│   │   └── Persistence  - SaveManager / JSON save data
│   │
│   └── Gameplay
│       ├── Character    - CharacterContextProvider / CharacterContext / CharacterInputDriver
│       │                  CharacterLifeCycle / CharacterNavigationMotor / CharacterLocomotion
│       ├── Enemy        - EnemyBrain / EnemySpline / EnemyVisionQuery / EngagementZone
│       └── Item         - ItemContext / ItemInputDriver / item lifecycle facts
│
└── CoCoFlow.Editor
    ├── Core             - State Graph Viewer
    ├── AssetPipeline
    ├── Animation        - AnimEventSmb editor / injector
    ├── Persistence
    └── UI
```

---

## 模块状态

| 模块 | 状态 | 说明 |
|------|------|------|
| Core | 稳定骨架 | ServiceLocator、EventBus、Context、State Layer controller 已收紧。 |
| Input | 基本可用 | `InputReader` 输出 Core 级输入事实，不直接绑定 Character。 |
| Camera | 基本可用 | 第三人称相机和 Cinemachine 绑定仍保持轻量。 |
| UI | 基本可用 | 面板栈和 View 生命周期可用。 |
| Animation | 薄封装 | 当前只包含 Animator 操作封装、SMB 帧事件和注入工具；不包含 Animation Rigging / IK runtime。 |
| Gameplay | 开发中 | Character、Enemy、Item 的 Context 主线和 samples 已建立，战斗/技能/完整 NPC 行为仍应在业务层扩展。 |
| Editor | 开发中 | Setup Assistant 和 State Graph Viewer 已可用于当前骨架。 |
| Network Samples | 文档样本 | 只固定 Fusion 接入边界，不提供可编译 runtime。 |
| Enemy Samples | 可选样本 | Enemy Brain/Spline/State/Prefab 接入示例。 |
| Player Samples | 可选样本 | Player State Layers/Context/Locomotion 接入示例。 |

---

## Animation Rigging / IK 策略

当前建议是先在业务项目里继续开发 Animation Rigging 和手部 IK，再吸纳进包。

原因很简单：包里的 Animation 模块现在只有 `AnimHandler`、`AnimEventSmb` 和编辑器注入工具，还没有形成稳定的 rig contract。手部 IK 虽然已经在业务项目完成，但它需要经过真实角色、武器、交互物、动画层和状态层的压力测试，确认哪些东西是通用骨架，哪些只是业务 prefab 约定。

后续适合吸纳进包的条件：

- 需要调用的外部组件契约稳定，例如 `CharacterRigDriver` 或 `ICharacterRigOperation`。
- 需要读写的 Context facts 明确，例如 hand target、grip weight、aim weight、interaction anchor。
- 缺少 Animation Rigging package 或 rig 组件时能安全降级。
- 不把业务项目的具体骨骼命名、武器结构、prefab 路径硬编码进 Runtime。
- 最好以可选 add-on 或 sample 形式进入，而不是直接塞进 Core/Character 主线。

在这些条件满足前，业务项目先行更合适；包里只保留能稳定复用的薄工具。

---

## 依赖

| Package | 版本 | 是否必须 |
|---------|------|----------|
| Addressables | 2.9.1+ | 必须 |
| Input System | 1.18.0+ | 必须 |
| Newtonsoft Json | 3.2.2+ | Persistence 必须 |
| Cinemachine | 3.1.6+ | Camera 模块必须 |
| AI Navigation | 2.0.0+ | CharacterNavigationMotor / Enemy Samples 必须 |
| Splines | 2.6.0+ | EnemySpline / Enemy Samples 必须 |
| Mathematics | 1.3.3+ | Splines / Enemy 必须 |
| UniTask | 2.5.11 | 由 Setup Assistant 通过 Git URL 安装 |
| DOTween | 最新 | 可选，依赖 DOTween 的模块需要手动安装 |
| Photon Fusion | 2.x | 仅 Network Samples 设计目标 |
| Animation Rigging | 1.x/2.x | 当前不属于包依赖，建议先留在业务项目 |

导入后可执行 `CoCoFlow/Setup/Setup Assistant` 检查依赖、配置 scripting defines，并安装 Network / Enemy / Player Samples。

---

## Samples

| Sample | 默认安装位置 | 当前用途 |
|--------|--------------|----------|
| Network Samples | `Assets/CoCoFlow/Network` | Fusion input bridge、Context snapshot、EventEnvelope 的设计文档。 |
| Enemy Samples | `Assets/CoCoFlow/Enemy` | `P_Enemy_00` 演示 `CharacterContextProvider`、`EnemyBrain`、`EnemySpline`、`Main` State Layer。 |
| Player Samples | `Assets/CoCoFlow/Player` | `P_Player_00` 演示 `Main`、`FullBody`、`UpperLayer` 三个 State Layer 和 Player 状态声明。 |

Samples 是接线参考，不是完整游戏功能。Player sample 不包含完整 camera、combat、weapon、IK 或输入 action 配置；Enemy sample 需要可用 NavMesh 才能完整验证巡逻和追击移动。

---

## 安装

1. 将本仓库克隆到 Unity 项目的 `Packages/` 目录下，或通过 Package Manager Git URL 安装。
2. 安装必需依赖。
3. 打开 `CoCoFlow/Setup/Setup Assistant` 检查依赖和可选 samples。
4. 在需要查看状态拓扑时打开 `CoCoFlow/State/State Graph Viewer`，指定目标 `CoCoStateController`。

---

## 架构文档

- [Context / Network Boundary](Docs/ContextNetworkBoundary.md) - Context、Intent、State、EventEnvelope、Persistence ID 与网络 adapter 边界。
- `Samples~/Network Samples/CoCoFlow/Network/Docs/ContextSyncPlan.md` - Network Samples 的后续实现计划。

---

## 许可证

MIT

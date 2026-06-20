# CoCoFlow

模块化 Unity 游戏开发框架。

> **版本**: 0.3.3 · **Unity**: 6000+

---

## 架构拓扑

```
CoCoFlow v0.3.3
│
├── CoCoFlow.Runtime (运行时)
│   ├── ◆ Core (核心层)
│   │   ├── CoCoServices    — 类型化服务定位器 (Register / Get / WaitFor)
│   │   ├── EventBus        — 类型安全的事件总线 (Pub/Sub + 动态反射)
│   │   ├── StateMachine    — 通用有限状态机引擎 (含 TransitionPredicate)
│   │   └── CoCoLog         — 统一日志工具
│   │
│   ├── ◇ Modules (功能模块层)
│   │   ├── Input           — InputReader + ActionMap 切换 + 输入缓冲
│   │   ├── Camera          — ThirdPersonCamera / CameraRig / CameraController
│   │   ├── UI              — UIController / UIViewManager / 面板栈管理
│   │   ├── Animation       — AnimationManager / MotionController
│   │   ├── Map             — MapManager / Chunk 分块加载
│   │   ├── Rendering       — URP 画质分级 / LOD Bias / 渲染管线
│   │   └── Persistence     — SaveManager / SaveData (JSON 存档)
│   │
│   └── ◇ Gameplay (游戏玩法层)
│       ├── Character       — CharacterLifecycle (生命值 / 伤害 / 死亡 / 复活)
│       └── ...              — 待扩展
│
└── CoCoFlow.Editor (编辑器工具链)
    ├── Core                — 编辑器核心
    ├── AssetPipeline       — 资源管线工具
    ├── Animation           — 动画编辑器工具
    ├── Persistence         — 存档数据编辑器工具
    └── UI                  — UI 编辑器工具

Samples~
├── Network Samples/CoCoFlow/Network
│                        — Fusion 网络兼容层设计样本（InputReader 桥 + Context 同步）
├── Enemy Samples/CoCoFlow/Enemy Samples
│                        — Enemy 基础层示例状态、配置和 prefab
└── Player Samples/CoCoFlow/Player Samples
                         — Player 示例 CCS 状态和 prefab
```

---

## 模块开发进度

| 模块 | 状态 | 完成度 | 说明 |
|------|------|--------|------|
| **Core** | ✅ 稳定 | 95% | ServiceLocator / EventBus / StateMachine 已定型 |
| **Input** | ✅ 基本完成 | 90% | ActionMap 切换、输入缓冲、多设备支持 |
| **Camera** | ✅ 基本完成 | 85% | 第三人称相机、Cinemachine 虚拟相机绑定 |
| **UI** | ✅ 基本完成 | 85% | 面板栈管理、View 生命周期 |
| **Animation** | ✅ 基本完成 | 80% | MotionController + DOTween 程序化动画 |
| **Map** | ✅ 基本完成 | 80% | Chunk 分块加载、异步加载 |
| **Rendering** | 🟡 大部分完成 | 70% | URP 画质分级 + LOD Bias，后处理 / 纹理流送开发中 |
| **Persistence** | 🟡 大部分完成 | 65% | JSON 存档 + SaveManager，加密 / 迁移开发中 |
| **Gameplay** | 🟡 开发中 | 35% | Character / Enemy 基础层已建立，战斗 / 技能待定 |
| **Editor** | 🟡 大部分完成 | 70% | 各模块编辑器工具链已建立，AssetPipeline 开发中 |
| **Network Samples** | 可选 Samples | Fusion 技术栈下的 Input/Context 同步兼容层设计 |
| **Enemy Samples** | 可选 Samples | 基础 Enemy 状态脚本、意图/配置 SO 和 `P_Enemy_00` prefab |
| **Player Samples** | 可选 Samples | 常见 Player CCS 状态脚本和 `P_Player_00` prefab |

---

## 依赖

| Package | 版本 | 是否必须 |
|---------|------|----------|
| UniTask | 2.5.11 | ⚠️ 由 Setup Assistant 通过 Git URL 安装 |
| Addressables | 2.9.1+ | ✅ 必须 |
| Input System | 1.18.0+ | ✅ 必须 |
| Newtonsoft Json | 3.2.2+ | ✅ Persistence 必须 |
| DOTween | 最新 | ⚠️ 需手动安装 |
| Cinemachine | 3.1.6+ | ✅ Camera 模块必须 |
| Splines | 2.6.0+ | ✅ Enemy 模块必须 |
| Mathematics | 1.3.3+ | ✅ Splines/Enemy 必须 |
| Photon Fusion | 2.x | ⚠️ 仅 Network Samples 需要 |

> **注意:**
> - DOTween 需手动安装
> - 导入后可执行 `CoCoFlow/Setup/Setup Assistant` 查看依赖状态、一键配置 UniTask/Newtonsoft/宏，并选择安装 Network / Enemy / Player samples
> - Setup Assistant 使用 UniTask Git URL 固定版本，不再依赖 OpenUPM
> - Network Samples 位于 `Samples~/Network Samples/CoCoFlow/Network`，Setup Assistant 默认导入到 `Assets/CoCoFlow/Network`
> - Enemy Samples 位于 `Samples~/Enemy Samples/CoCoFlow/Enemy Samples`，Setup Assistant 默认导入到 `Assets/CoCoFlow/Enemy`
> - Player Samples 位于 `Samples~/Player Samples/CoCoFlow/Player Samples`，Setup Assistant 默认导入到 `Assets/CoCoFlow/Player`

---

## 安装

1. 将本仓库克隆到你的 Unity 项目的 `Packages/` 目录下（或通过package manager git url安装）
2. 安装上述依赖
3. 开始使用

---

## 快速开始

请参照：https://cypress-abrosaurus-007.notion.site/CoCoFlow-Guideline-35a9a23f605d80668f5cf0c232389ba4

## 架构文档

- [Context / Network Boundary](Docs/ContextNetworkBoundary.md) — Context、Intent、StateMachine、EventEnvelope、Persistence ID 与网络 adapter 边界。

---

## 许可证

MIT

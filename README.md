# CoCoFlow

模块化 Unity 游戏开发框架。

> **版本**: 0.3.1 · **Unity**: 6000+

---

## 架构拓扑

```
CoCoFlow v0.3.1
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
└── Network/CoCoFlow/Network
                         — Photon Fusion 可选 Add-on（需手动导入）
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
| **Gameplay** | 🟡 开发中 | 30% | CharacterLifecycle 已有，战斗 / 技能 / AI 待定 |
| **Editor** | 🟡 大部分完成 | 70% | 各模块编辑器工具链已建立，AssetPipeline 开发中 |
| **Network Add-on** | 可选 Add-on | Photon Fusion Host/Client、Lobby、PlayerObject 生成；不属于主包默认编译模块 |

---

## 依赖

| Package | 版本 | 是否必须 |
|---------|------|----------|
| UniTask | 2.5.10+ | ✅ 必须 |
| Addressables | 2.9.1+ | ✅ 必须 |
| Input System | 1.18.0+ | ✅ 必须 |
| DOTween | 最新 | ⚠️ 需手动安装 |
| Cinemachine | 3.x | ⚠️ 仅 Camera 模块需要 |
| Photon Fusion | 2.x | ⚠️ 仅 Network Add-on 需要 |

> **注意:**
> - DOTween 需手动安装（Asset Store 或通过名称安装: `com.unity.dotween`）
> - Photon Fusion 网络骨架位于 `Samples~/Network/CoCoFlow/Network`，需要时在 Package Manager 的 Samples 面板导入 `Add-on: Network`

---

## 安装

1. 将本仓库克隆到你的 Unity 项目的 `Packages/` 目录下（或通过 `git submodule` 引入）
2. 安装上述依赖
3. 开始使用

```bash
cd YourUnityProject/Packages
git clone https://github.com/YunXEE/CoCoFlow.git com.yunxee.cocoflow
```

---

## 快速开始

请参照：https://cypress-abrosaurus-007.notion.site/CoCoFlow-Guideline-35a9a23f605d80668f5cf0c232389ba4

---

## 许可证

MIT

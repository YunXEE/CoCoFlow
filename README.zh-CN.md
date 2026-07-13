# CoCoFlow

[English](README.md) | [简体中文](README.zh-CN.md)

> **版本**：0.4.0-pre.1 · **Unity**：6000+
>
> 当前版本是 Pre1 Core 契约阶段，只冻结架构边界和值语义，不代表 0.4
> Runtime 和编辑工作流已经完成。

CoCoFlow 是面向 Unity 6 的 Layered HFSM（分层层次有限状态机）框架，围绕
Context 驱动决策、显式受控的 Operation，以及由宿主推进的确定性 Tick 构建。
0.4 面向新的单机 3D 冒险与动作项目。

## Pre1 冻结什么

Pre1 确立后续所有预发布版本必须遵守的依赖方向：

```text
手动绑定的 Sources
  -> Frozen Context Frame N
  -> 相互独立的 Layered HFSM Layers
  -> 已声明的 Operation 准入口
  -> Operation 回写
  -> Frozen Context Frame N + 1
```

本阶段冻结的 Core 表面包括：

- 彼此独立的 graph、layer、state、transition、graph-instance、activation
  和 timeline identity；
- execution sequence、timeline tick/position、clock domain 和 tick frame
  值契约；
- 显式 runtime lifecycle state；
- 结构化 diagnostic domain、code、severity 和 record；
- 不暴露 `MonoBehaviour`、`GameObject`、Animator 或 Playable 类型的纯 C#
  StateLogic 角色与依赖声明。

Core 规则：

- StateLogic 只读取一份冻结的 Context Frame，不能回写 Context。
- Context Section 只允许 public abstract instance property，并且必须是无参数 getter。
  Indexer、default/static member、field、callback、Unity Object、引用型 collection、
  native handle 与 stack-only value 都会被拒绝；事实只允许 immutable string 或不含
  引用的 value。读取必须携带匹配 Requirement，不暴露 mutable root、Source、Writer
  或具体 Provider 类型。
- 每个 Layer 独立拥有一个 HFSM，并计算出一条以末端 State 结束的活跃路径。
- Layer 按显式优先级执行；一条活跃路径完成当前生命周期阶段后，才处理下一
  个 Layer。
- Unity callback 只是宿主输入，不等于 CoCo 时钟。Variable、Fixed、Manual
  driver 产生的 CoCo Tick 数量可以与 Unity callback 数量不同。
- 零或负 delta 非法。Suspend 不产生 Tick，因此也没有 Frozen Frame 采样。
- Runtime 在第一次 Running 前允许 `Created → Disposed`；已经 Running 或 Suspended
  的实例必须先 Stop 再 Dispose。
- Operation 是唯一受认可的副作用边界，其回写只能在后续 Frozen Frame 可见。
  StateLogic 通过已声明的 Port Requirement 按值提交 unmanaged Command，因此 Submit
  不携带托管引用、delegate、共享结果或同步玩法返回通道。Pre5 还必须在派发前校验
  Port/Command 匹配，并拒绝 native handle、裸指针或函数指针 Command Shape。
- Pre1 冻结的是框架提供的 StateLogic 角色，不把任意项目 C# 代码伪装成 CLR 安全沙箱；
  State 作者程序集与依赖限制由 StateGraph Compiler/作者验证对应 Pre 落实。

完整 Frame 与 adapter 规则见
[Context / Network Boundary](Docs/ContextNetworkBoundary.md)。

## 仓库过渡状态

现有 0.3.9 CCS Runtime 暂时保留在仓库中，让 Pre1 可以单独冻结契约，而不把
Runtime 重写混进同一个改动。它将在 Pre4 被替换，不是 0.4 的兼容承诺、API
基线或迁移层。

具体来说：

- 现有 `CoCoStateController`、`CoCoStateLayer`、`CoCoStateBase` 及其 Unity
  生命周期行为只属于旧实现证据；
- 0.3.9 项目继续锁定 0.3.9 revision，0.4 不提供双 Runtime；
- Pre1 不发布 Samples，也不提供 Add-on 导入表面；
- 0.3.9 的只读状态图检查工具已在 Pre1 移除；GraphAsset 编译和图编辑
  会在对应的 Pre 中实现。

## 包边界

```text
Runtime/Core/Contracts   与引擎隔离的冻结契约
Runtime/Core             过渡期 0.3.9 CCS Runtime，保留至 Pre4
Runtime/Gameplay         过渡期 gameplay 实现
Runtime/Modules          过渡期表现与服务模块
Editor                   依赖/setup 与旧模块工具
Tests                    契约、架构和过渡期回归测试
```

Core Contracts assembly 不得依赖 Gameplay、表现模块、Editor、项目代码、Animator
或 Playables。上层模块可以依赖 Core contracts，Core 不得反向依赖上层模块。

## Pre1 不实现什么

Pre1 明确不实现：

- Context V2 组合和 runtime source 解析；
- `StateGraphAsset`、Graph 编译、Transition 编辑或 Runtime 执行；
- clock scheduler、变速、transition queue 或 runtime snapshot；
- Operation ownership/claim 仲裁与回写实现；
- temporal rewind；
- 基于 Playable 的动画、自有动画 Runtime、Combo 编辑或 Root Motion 所有权；
- starter content、gameplay 模板或 golden-path 项目；替代 Samples 与
  Adventure Starter 由 Pre15/Pre16 负责。

这些能力属于后续预发布版本，并且必须建立在本阶段冻结的契约之上。

## 依赖

Pre1 不调整 dependency 集合，因为过渡期 0.3.9 模块仍需要它们参与编译。

| Package | Version | 当前使用者 |
|---|---:|---|
| Addressables | 2.9.1 | Map 和 UI runtime 工作流 |
| Input System | 1.18.0 | Input 模块 |
| Newtonsoft Json | 3.2.2 | Persistence 模块 |
| Cinemachine | 3.1.6 | Camera 模块 |
| AI Navigation | 2.0.0 | Character 与 Enemy navigation |
| Mathematics | 1.3.3 | Enemy/spline assemblies |
| Splines | 2.6.0 | Enemy spline 支持 |

依赖精简应由替换对应模块的 Pre 负责，不属于 Core 契约冻结。

## 安装与验证

可以通过 Unity Package Manager 使用 Git revision 安装，也可以把包放入 Unity
项目的 `Packages/` 目录。应锁定明确的 prerelease tag 或 commit，不要把持续
变化的开发分支当作生产依赖。

当前阶段的 `CoCoFlow/Setup/Setup Assistant` 只负责依赖与 support define 状态，
不安装项目内容。

本仓库是 UPM package，不是完整 Unity Project，因此 release gate 必须使用干净
的 Unity 6 宿主工程完成包导入，并执行 EditMode 和 PlayMode 测试。

## 文档

- [Context / Network Boundary](Docs/ContextNetworkBoundary.md)
- [Module: Animation](Docs/Module-Animation.md)
- [Module: Camera](Docs/Module-Camera.md)
- [Module: Persistence](Docs/Module-Persistence.md)
- [Changelog](CHANGELOG.md)

除非明确标记为已冻结的 0.4 契约，模块文档描述的都是过渡期实现。

## 版本约定

- 集成分支：`dev/0.4.0`
- 工作分支：`pre/NN-topic`
- UPM 预发布版本：`0.4.0-pre.N`
- 0.3.9 是历史 Runtime 线，0.4 不内置迁移 Runtime。

## License

MIT

# CoCoFlow

> **版本**：0.4.0 · **Unity**：6000+
>
> [English](README.md)

CoCoFlow 是面向 Unity 6 的状态流框架，核心包括类型化输入、分层
StateGraph、事务式 Context 提交、Temporal 恢复和显式 Runtime ownership。
0.4.0 将当前 Runtime 直接收口为可用版本：停止继续扩张功能，并准确说明包内现有边界。

## “成熟”的定义

本次发布中的 **成熟** 表示：公共 Runtime API 稳定、模块已在实际项目中完成过其职责，
并且已知边界能够被准确说明。它**不表示**架构最新、效能最高、Editor 工具完整、
零缺陷或通过商店级认证。

| 模块 | 状态 | 准确说明 |
|---|---|---|
| Core Engine | **成熟** | Contracts、StateFlow、StateGraph、StateGraphAuthoring Runtime、StateGraphHost；0.4 原生核心。 |
| Camera | **成熟** | 起源于 0.3.9，当前 Rig、priority、mode API 可稳定使用。 |
| Persistence | **成熟** | 起源于 0.3.9，现已支持 schema v2、Container 与 StateGraph ContextFrame 持久化。 |
| UI | **成熟** | 起源于 0.3.9，Panel、Widget、Input、Content ownership API 可用且稳定，但不是高效能 UI 框架。 |
| Map | **不成熟** | 当前实现可用，但公共 API、配置和序列化结构暂不保证兼容。 |
| Pooling | **不成熟** | 当前实现可用，但公共 API、配置和序列化结构暂不保证兼容。 |
| 其他模块 | **暂不评级** | 本次不作成熟或不成熟判断。 |

Core Engine 的成熟声明不包含 StateGraph Editor，也不包含
`Runtime/Core/*.cs` 下旧 EventBus、Services 和 Context 设施。

## Core Flow

```text
Raw input / typed events
        ↓
Mailbox + Intent 仲裁
        ↓
分层 StateGraph Step
        ↓
Finalized OperationFrame
        ↓
Operators + staged Context candidate
        ↓
原子提交
        ├─ committed ContextFrame
        ├─ Event Outbox + Trace
        └─ Temporal / persistence projection
```

每个 Actor 独占自己的 Runtime 状态。StateLogic 读取不可变输入，只写已声明的
Operation Section。Host 在启动前校验 binding，每次只暂存一个候选 Tick；整个 Actor
事务要么一起提交，要么继续保留上一份权威状态。跨对象副作用只从已提交的 Event
Outbox 离开，不通过直接 State callback 传播。

Temporal Restore 是同会话、精确 Layout 的恢复。持久化存档使用独立的 Persistence
Schema；两者不是同一种 Wire Format。

## 安装

在 Unity Package Manager 中使用 Git URL：

```text
https://github.com/YunXEE/CoCoFlow.git#v0.4.0
```

或在 `Packages/manifest.json` 中加入：

```json
{
  "dependencies": {
    "com.yunxee.cocoflow": "https://github.com/YunXEE/CoCoFlow.git#v0.4.0"
  }
}
```

通过 **CoCoFlow > Utility Panel**（CoCoFlow Utility 窗口）检查可选集成依赖和项目设置。
部分集成程序集只有在外部包和对应 support define 存在时才会编译，详见
[依赖矩阵](Docs/DependencyMatrix.md)。

## 文档

- [文档入口](Documentation~/index.md)
- [StateGraph Asset 与 Compiler](Docs/StateGraphCompiler.md)
- [StateGraph Runtime 与 Host](Docs/StateGraphRuntime.md)
- [State Flow / Event 边界](Docs/ContextNetworkBoundary.md)
- [Temporal Rewind](Docs/TemporalRewind.md)
- [Camera](Docs/Module-Camera.md)
- [Persistence](Docs/Module-Persistence.md)
- [UI](Docs/Module-UI.md)
- [Map](Docs/Module-Map.md)
- [Pooling](Docs/ObjectPooling.md)
- [Changelog](CHANGELOG.md)

## 发布策略

CoCoFlow 0.4.x 采用小步迭代。成熟的 Runtime surface 按稳定 API 对待。Map 与 Pooling
可能在 0.4.x 调整 API、配置和序列化结构。未评级模块应依据实际实现和模块文档判断，
不能从本表推导成熟度。

## License

MIT

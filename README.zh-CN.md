![CoCoFlow](.github/assets/cocoflow-banner.svg)

> **版本**：0.4.3 · **Unity**：6000+
>
> [English](README.md)

CoCoFlow 是面向 Unity 6 的状态流框架，核心包括类型化输入、分层
StateGraph、事务式 Context 提交、Temporal 恢复和显式 Runtime ownership。

## 0.4.3 新内容

- Host 上挂载 Adapter 接收外部结构体事件，Logic 通过
  `context.Inbox.TryGetBatch<TEvent>` 读取当前 Tick 冻结的邮件批次。
  一个 Host 支持多个 Adapter 和事件类型，事件与业务 Intent 分别进入 Logic。
- 用 `context.Outbox.Publish(in payload)` 发送结构体事件；Tick 成功提交后，
  Host 才调用普通 EventBus 发布。
- 在 `[CoCoState]` 上声明 `ProvidesActionProgress = true`，普通与 Config/Memory
  两条注册路径都可使用进度窗口。独立 `[Condition]` 类自动发现并绑定工厂，
  无需项目手写 Binding。

直接邮件仅在 Running 时接收；每类型队列满后丢弃新邮件，不触发 Host Fault。
被拒绝的 Temporal 调用保留待处理邮件。完整 API 与生命周期见
[State 邮箱与绑定](Docs/StateGraphRuntime.md#direct-state-mailbox-d043-040711121415)。

## “成熟”的定义

本次发布中的 **成熟** 表示：公共 Runtime API 稳定、模块已在实际项目中完成过其职责，
并且已知边界能够被准确说明；各版本更新记录中明确列出的变更优先于兼容预期。
它**不表示**架构最新、效能最高、Editor 工具完整、
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
Host-local Intent sources       External struct events
          ↓                               ↓
      IntentFrame                  Adapter → Inbox batch
          └──────────────┬────────────────┘
               Layered StateGraph Step
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

每个 Actor 独占自己的 Runtime 状态。StateLogic 读取不可变的 Tick 输入，更新激活
Memory，写入已声明的 Operation Section，并暂存待发送事件。Host 在启动前校验 binding，每次只暂存一个候选 Tick；整个
Actor 事务要么一起提交，要么继续保留上一份权威状态。跨对象副作用只从已提交的 Event
Outbox 离开，不通过直接 State callback 传播。

Temporal Restore 是同会话、精确 Layout 的恢复。持久化存档使用独立的 Persistence
Schema；两者不是同一种 Wire Format。

## 安装

在 Unity Package Manager 中使用 Git URL：

```text
https://github.com/YunXEE/CoCoFlow.git#v0.4.3
```

或在 `Packages/manifest.json` 中加入：

```json
{
  "dependencies": {
    "com.yunxee.cocoflow": "https://github.com/YunXEE/CoCoFlow.git#v0.4.3"
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
- [Input](Docs/Module-Input.md)
- [Camera](Docs/Module-Camera.md)
- [Persistence](Docs/Module-Persistence.md)
- [UI](Docs/Module-UI.md)
- [Map](Docs/Module-Map.md)
- [Pooling](Docs/ObjectPooling.md)
- [Changelog](CHANGELOG.md)

## 发布策略

CoCoFlow 0.4.x 采用小步迭代。成熟的 Runtime surface 按稳定 API 对待，破坏性变更会在
对应版本中明确列出。Map 与 Pooling 可能在 0.4.x 调整 API、配置和序列化结构。
未评级模块应依据实际实现和模块文档判断，
不能从本表推导成熟度。

## License

MIT

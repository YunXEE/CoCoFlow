# CoCoFlow

[English](README.md) | [简体中文](README.zh-CN.md)

> **版本**：0.4.0-pre.2 · **Unity**：6000+
>
> Pre2 冻结 State Flow Frame、OperationFrame Section、ContextFrame Restore
> 与 Actor Mailbox 契约；它还不是完整的 0.4 Runtime、StateGraph 编辑工作流、
> 倒放系统或 Persistence V2。

CoCoFlow 是面向 Unity 6、新单机 3D 冒险与动作项目的 State Flow + Layered
HFSM 框架。0.4 将输入意图、状态图决策、副作用执行、Actor 已提交状态和跨 Object
消息分开处理，不再把这些职责混入一个可变 Context。

## State Flow

每个被接受的 CoCoTick 只沿一个方向执行：

```text
Input / AI / Network + sealed EventInbox
  -> Event-to-Intent Adapters
  -> freeze IntentFrame
  + Previous ContextFrame
  -> StateGraph
  -> OperationFrame Sections
  -> Operators
  -> Outcomes + EventOutbox candidates
  -> commit ContextFrame
  -> assign EventSequence and publish EventOutbox
```

跨 Object gameplay 输入通过 Actor 信箱加入这条 Flow：

```text
EventPacket<TEvent>
  -> CoCoEventBus
  -> one EventRouter per EventDomain
  -> target Actor EventInbox
  -> next accepted CoCoTick
  -> Event-to-Intent Adapter
  -> IntentFrame
```

EventBus 是公路，EventEnvelope 是快递单，EventRouter 是分拣中心，Actor
EventInbox 是门口信箱，Event-to-Intent Adapter 负责把来信翻译成本 Actor 的
Intent。StateGraph 永远不读取 raw EventBus callback、Envelope、Router 或 Mailbox。

## 冻结词义

| 词条 | 含义 |
|---|---|
| `IntentFrame` | 一个 CoCoTick 的不可变输入；只采样、仲裁并冻结一次，不持久化，也不进入倒放历史。 |
| `OperationFrame` | StateGraph 产生的完整执行指南；只有它公开 Section 契约。 |
| `ContextFrame` | 单个 Actor 在 Tick 边界的完整已提交逻辑状态，是 Restore 以及后续 Temporal/Durable 投影的权威输入。 |
| `EventInbox` | 一个 GraphRuntimeInstance 的待处理跨 Object gameplay 输入，不是事实存储。 |
| `EventOutbox` | Operator 执行期间产生的跨 Object 输出候选，只有 ContextFrame Commit 成功后才发布。 |
| `EventAgent` | 只负责 EventBus 订阅生命周期；不路由、不排队、不拥有也不持久化消息。 |

`IntentFrame`、`OperationFrame` 和 `ContextFrame` 不是别名，不能互相替代。
Inbox、raw Envelope、当前 IntentFrame 和尚未发布的 Outbox 候选都不会混入
ContextFrame。

## OperationFrame Section

Operator 通过一个或多个只读 OperationFrame Section Interface 声明本 Tick 需要的
执行数据，StateGraph 必须交付这些要求的去重并集。

- Section Interface 只允许继承框架 Marker；Section 之间的继承会被拒绝。扩展能力
  必须通过组合表达。
- 同一个 Interface Identity 被多次要求时只占一个 Layout Entry。
- 两个 Interface 即使字段完全相同，只要身份不同，就仍然是两个 Section。
- Section 是本 Tick 的执行承诺，不是 Actor 状态，也不是可变 Callback 表面。
- 离散执行继续使用结构化数据，并显式表达 Enabled、Activation 与 Sequence；
  StateGraph 不建立平行的 Command Queue。
- Layout、Descriptor、Binding、Priority 和 Reducer 必须在 Running 前固定；Tick 热路径
  禁止运行时反射、字符串 Key 查找和稳态分配。

Pre2 只提供契约与显式测试 Layout；Pre3 负责自动汇总 Graph Requirement 和生成
Compiled Layout，Pre5 负责正式 Operator Runtime。

## ContextFrame 与 Restore

`ContextFrame` 是单个 Actor 的完整已提交逻辑状态，不是世界快照，也不是 Unity
场景 Object Graph。固定的 StateBlock/Slot Layout 必须包含从该 Commit Boundary
继续运行所需的 Graph、Activation、Transition Progress、Actor 数值和可控 Operator
进度，或包含能够确定重建它们的数据。

Descriptor 元数据包含两个独立维度：

- Projection Flags 分别标记 **Temporal**、**Durable**，同一 Slot 可以同时拥有两项。
- Restore Policy 独立选择 **Stored**、**ResetToDefault** 或 **Derived**。
- Derived Slot 必须声明依赖，由已恢复输入确定重建，不形成第二个权威值。

Restore 永远落在一个完成的 Commit Boundary。它不会恢复 Inbox、IntentFrame、
EventAgent 订阅、未发布 Event、执行一半的 Operator、其他 Actor，或已经交付给
其他 Actor 的后果。

Pre2 验证 Descriptor 与 Codec Spike；Pre6 负责 Temporal 存储和倒放，Pre13 负责
Durable Save Document、Migration、Container、世界事实和生成实体重建。

## Actor Mailbox 规则

Gameplay 消息使用一个原子值：

```text
EventPacket<TEvent> = EventEnvelope + immutable typed payload
```

每个 GraphRuntimeInstance 独占一个 EventInbox；每个 EventDomain 只有一个中央
Router，EventDomain 与 ClockDomain 是不同身份。Targeted 消息按当前
GraphInstanceId 路由。Broadcast 只投递给显式声明对应 Event-to-Intent Adapter 的
Actor，并且默认不回送 Source Actor。

Inbox 使用固定容量、预分配双缓冲。Router Callback 只能校验、去重、路由和入队。
Step 开始时封存本 Tick 可见批次；Step 期间到达的消息最早在下一次被接受的 Tick
可见。一条消息最多投影到一个 IntentFrame；需要持续存在的含义必须提交成
ContextFrame State。

- Suspend 只允许在固定容量内继续积压。
- Rewind/Restore 拒绝新的 gameplay 消息并记录诊断。
- Reliable 溢出返回 Host Fault 结果；Unreliable 溢出拒绝最新消息并增加诊断计数。
- Stop/Dispose 清空队列和去重状态。
- 音效、VFX、日志等纯表现事件可以继续使用普通 EventBus，不进入 gameplay Inbox。

中央 Router、真实 EventBus 订阅生命周期、StableEntityId 解析、Host Fault 状态切换
和幽灵订阅测试属于 Pre4。

## Commit 与时间边界

- 每个被接受的 `CoCoTickFrame` 都使用有限正 Delta。
- Pause/Suspend 不产生 Tick，不采样 Intent，也不产生新 Frame。
- Rewind 不使用负 Delta。Pre6 从旧 ContextFrame Restore，建立新 TimelineEpoch，
  然后继续正向 Tick。
- StateGraph 只读取当前 IntentFrame 和 Previous ContextFrame，不能观察本 Tick
  执行中产生的 Outcome。
- ContextFrame Commit 是唯一对外可观察的 gameplay 边界。
- Commit 失败、Cancel、Restore 或 Rewind 时，零 Outbox Event、零最终 EventSequence
  消耗、零跨 Actor 副作用。

正式 Outcome 聚合、ContextFrame Commit 和 EventOutbox Publish 属于 Pre5。Pre2
仅通过纯契约 Harness 冻结和验证协议。

## 仓库与包边界

0.3.9 CCS Runtime 暂时保留，用于编译和历史回归证据。它的可变 Context Provider、
MonoBehaviour State、Unity Callback 调度和当前模块 API 都不是 0.4 契约，也不是迁移
层。现有 0.3.9 项目应继续锁定 0.3.9 Revision。

```text
Runtime/Core/Contracts   与引擎隔离的 0.4 契约
Runtime/Core/StateFlow   与引擎隔离的 0.4 Frame、Section、Intent 与 Mailbox 契约
Runtime/Core/*.cs        过渡期 0.3.9 Runtime 与后续 Pre 集成
Runtime/Gameplay         过渡期 gameplay 实现
Runtime/Modules          过渡期表现与服务模块
Editor                   dependency/setup 与过渡期模块工具
Tests                    契约、架构与过渡期回归测试
```

Core Contract 与 State Flow 表面不得依赖 Gameplay、表现模块、Editor、项目代码、
Animator、Playable、特定网络框架或持久化后端。StateLogic/Layer API 不得暴露
EventBus、EventAgent、EventEnvelope、EventRouter 或 EventInbox 依赖。

Pre1 仍是 identity、time、lifecycle、diagnostic 与纯 StateLogic 契约的历史发布。
当 Pre1 的候选 Context Flow 与本文冲突时，以 Pre2 State Flow 为权威。

## 后续 0.4 工作

- **Pre3**：StateGraph Asset/Compiler、Intent Requirement、Graph Operation Provides、
  ContextFrame State Requirement 与 Compiled Layout 生成。
- **Pre4**：`CoCoStateGraphHost`、Clock/Driver、EventRouter、EventAgent 订阅、Actor
  Inbox 注册与生命周期集成。
- **Pre5**：Operator Binding/Claim、Outcome 聚合、ContextFrame Commit 与
  EventOutbox Publish。
- **Pre6**：Temporal Ring Buffer、Rewind/Resume 与 TimelineEpoch 切换。
- **Pre11**：Playable Animation V2、Animation Operator、Combo Timing 与 Root Motion
  所有权。
- **Pre13**：Persistence V2、Durable Projection、Migration、Container 与世界事实。
- **Pre15/Pre16**：替代 Samples、Golden Path、技术文档以及完整跨模块性能/生命周期
  认证。

## 依赖

Pre2 不调整依赖集合，因为过渡期 0.3.9 模块仍需要这些依赖参与编译。

| Package | Version | 当前使用者 |
|---|---:|---|
| Addressables | 2.9.1 | Map 和 UI 过渡期工作流 |
| Input System | 1.18.0 | Input 模块 |
| Newtonsoft Json | 3.2.2 | Persistence 过渡期模块 |
| Cinemachine | 3.1.6 | Camera 过渡期模块 |
| AI Navigation | 2.0.0 | Character 与 Enemy Navigation |
| Mathematics | 1.3.3 | Enemy/Spline Assembly |
| Splines | 2.6.0 | Enemy Spline 支持 |

依赖精简由替换对应模块的 Pre 负责。

## 安装与验证

可以通过 Unity Package Manager 使用明确的 Git Revision 安装，也可以把包放入
Unity 项目的 `Packages/` 目录。不要把持续变化的开发分支当作生产依赖。

本仓库是 UPM Package，不是完整 Unity Project。发布门禁必须在干净的 Unity 6
宿主工程中完成包导入、Core/State Flow EditMode 测试、相关 PlayMode/AOT 检查与
Unity Package Validation Suite。`CoCoFlow/Setup/Setup Assistant` 仍只负责依赖与
Support Define，不安装项目内容。

## 文档

- [State Flow / Network Boundary](Docs/ContextNetworkBoundary.md)
- [Module: Animation](Docs/Module-Animation.md)
- [Module: Camera](Docs/Module-Camera.md)
- [Module: Persistence](Docs/Module-Persistence.md)
- [Changelog](CHANGELOG.md)

除非明确标注为 0.4 权威契约，模块文档描述的都是过渡期实现。

## 版本约定

- 集成分支：`dev/0.4.0`
- 工作分支：`pre/NN-topic`
- UPM 预发布版本：`0.4.0-pre.N`
- 0.3.9 是历史 Runtime 线；0.4 不承诺自动迁移或双 Runtime。

## License

MIT

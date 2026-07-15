# CoCoFlow State Flow / Event Boundary

> Contract status: `0.4.0-pre.2` · Updated 2026-07-15
>
> This is the authoritative Pre2 data-flow and cross-Object communication
> boundary. It freezes contracts and pure test harnesses; later Pres implement
> the Compiler, Unity Host, Router, Operator Runtime, rewind, and persistence.

## 目标

CoCoFlow 0.4 将一个 Actor 的 gameplay 处理收束成单向 State Flow：外部输入先变成
冻结 Intent，StateGraph 只负责解释和生成执行指南，Operator 执行世界副作用，最后
在 Commit Barrier 产生完整 Actor 状态。跨 Object 通信不能绕过这条 Flow 直接修改
StateGraph 或 ContextFrame。

本文中的 `ContextFrame` 是 Tick 结束后的完整 Actor 已提交逻辑状态。它不是 Pre1
候选设计中的“StateGraph 前冻结事实面”，也不是世界级 Snapshot 或 Unity Object
Graph。

## 1. 权威 State Flow

```text
Actor A
StateGraph -> OperationFrame -> Operators
  -> Outcomes + EventOutbox candidates
  -> Commit ContextFrame A
  -> successful commit assigns EventSequence and publishes EventOutbox

Cross Object
EventPacket<TEvent>
  -> CoCoEventBus
  -> EventRouter for one EventDomain
  -> Actor B Incoming EventInbox

Actor B next accepted CoCoTick
Input / AI / Network + sealed EventInbox
  -> Event-to-Intent Adapters
  -> IntentFrame B
  + Previous ContextFrame B
  -> StateGraph
  -> OperationFrame Sections
  -> Operators
  -> Outcomes + EventOutbox candidates
  -> Commit ContextFrame B
```

比喻上，EventBus 是公路，EventEnvelope 是快递单，EventRouter 是分拣中心，Actor
EventInbox 是门口信箱，Event-to-Intent Adapter 把来信翻译成本 Actor 的 Intent。
StateGraph 永远只读取已经翻译并冻结的 IntentFrame。

## 2. Frame 职责与所有权

### IntentFrame

`IntentFrame` 是一个 CoCoTick 的唯一输入面：

- Input、AI、Network、Host Sampling 和 sealed EventInbox Adapter 都只能提供候选；
- 候选按 Running 前固定的 Priority/Reducer 仲裁；
- 每个 Source 每 Tick 最多采样一次，Frame 只冻结一次；
- 同 Tick 的所有 Layer 读取相同 Frame Identity 和值；
- 无候选时仍产生合法空 IntentFrame；
- 不保存 raw EventPacket、Envelope、Source 对象或 Unity 输入 API；
- 不进入 ContextFrame、Temporal History 或 Persistence；
- Pause/Suspend 不产生 Tick，因此不采样、不仲裁，也不生成新 IntentFrame。

跨 Source 冲突只依赖显式 Priority/Reducer，不依赖 EventBus 到达顺序。同 Source 的
离散输入使用 SourceEventSequence 保持顺序。

### OperationFrame

`OperationFrame` 是 StateGraph 为当前 Tick 生成的完整执行指南，也是唯一公开 Section
契约的 Frame：

- Operator 通过 Section Interface 声明自己需要的执行数据；
- Graph Operation Provides 必须覆盖全部 Operator Requirement；
- 多个 Operator 要求同一 Interface 时按 Interface Identity 去重；
- 字段形状相同但 Identity 不同的 Interface 永远是不同 Section；
- Section Interface 只允许继承框架 Marker，Section-to-Section 继承非法；
- Section 在 Operator 执行期间只读，不能作为 Actor 持久状态；
- 离散执行使用结构化 Section，并明确 Enabled、ActivationId 和 Sequence；
- 无输入时产生合法 Disabled/Zero Section，而不是建立第二套 Command Queue。

Pre2 冻结 Descriptor、Registry、固定 Layout 输入和显式测试构造入口。Pre3 负责从
Graph 与 Operator 声明自动汇总、验证并编译 Layout；Pre5 负责实际执行 Operator。

### ContextFrame

`ContextFrame` 是一个 GraphRuntimeInstance 在 Commit Barrier 上的完整已提交逻辑
状态：

- 包含 GraphInstanceId、TimelineEpoch、Tick、Revision 和 Layout 兼容身份；
- 使用固定 StateBlock/Slot Layout，而不是公开 Root Context 或 Operation Section；
- StateBlock Owner 固定为 Graph、Operator 或 Actor；每个 Slot 只有一个 Writer，但可以有
  多个 Reader；
- 包含继续运行所需的 active state、transition progress、Activation、Actor 连续值和
  参与恢复的可控 Operator 进度，或包含能够唯一重建它们的值；
- 不包含 Inbox、IntentFrame、raw Envelope、Source、执行中局部变量、未发布 Outbox
  或 Unity Object 引用图；
- 不同 GraphRuntimeInstance 不共享可变 StateBlock；
- 包括无操作 Tick 在内，每次成功 Commit 都递增 Revision；
- Restore 只接受已提交 ContextFrame 或合法投影，在新 TimelineEpoch 形成新 Revision，
  并记录 Source GraphInstanceId、TimelineEpoch、Tick 与 Revision。

ContextFrame 只承诺单 Actor 逻辑恢复。它不恢复世界、其他 Actor，或撤销已经交付给
其他 Actor 的 Event 后果。

## 3. Operation、Outcome 与 Commit Barrier

Operator 是允许接触 Unity 对象和项目服务的执行边界。StateGraph 不调用具体 Unity
API，而是交付 OperationFrame；Operator 读取对应 Section，执行后产生 Outcome 与
可选 EventOutbox Candidate。

Commit 顺序固定：

```text
validate OperationFrame, bindings, Layout and capacities
  -> reserve ContextFrame arena and prepare an infallible commit path
  -> execute Operators
  -> collect Outcomes and Outbox candidates
  -> finalize and commit the prepared ContextFrame
  -> assign final EventSequence
  -> publish EventOutbox
```

ContextFrame Commit 是当前 Tick 唯一对外可观察的 gameplay 边界：

- Commit 成功产生一个完整的新 ContextFrame；
- 所有可能失败的验证与容量预留都发生在 Operator 执行前；合法的 prepared commit 在
  正常执行路径中不得失败；
- StateGraph 不能在当前 Tick 读取执行中的 Outcome；
- Outbox Candidate 在 Commit 前不可见；
- Commit 失败、Cancel、Restore 或 Rewind 时，旧 ContextFrame 继续权威；
- 失败路径不发布 Event、不消耗最终 EventSequence，也不产生跨 Actor 副作用。

Pre2 只定义并通过测试 Harness 验证协议。Outcome 聚合、正式 Commit Runtime 和
EventOutbox Publish 原子化属于 Pre5。

## 4. EventPacket 与路由身份

0.4 gameplay 消息使用一个原子值：

```text
EventPacket<TEvent> = EventEnvelope + immutable typed payload
```

Envelope 至少表达：

- EventTypeId
- EventDomainId
- SourceGraphInstanceId
- 可空 TargetGraphInstanceId；空值只用于 DeclaredBroadcast
- SourceTimelineEpoch
- SourceTick
- SourceEventSequence
- DeliveryMode：Targeted 或 DeclaredBroadcast
- Reliability
- 可选 StableEntityId、ActivationId 和 CorrelationId

本地 gameplay 热路径不使用字符串 Payload。当前 0.3.9
`payloadTypeId/payload` 只属于网络、日志或 Codec 边界。旧
`PublishWithEnvelope` 将 Payload 与 Envelope 分开发送，不能作为 0.4 Actor 路由
协议。

身份与路由规则：

- 每个 GraphRuntimeInstance 只有一个 EventInbox；
- 一个 EventDomain 只有一个中央 EventRouter；
- EventDomain 与 ClockDomain 分离；
- Targeted 消息按当前 TargetGraphInstanceId 做 O(1) 路由；
- StableEntityId 只用于存档、跨加载、网络和诊断，进入本地 Router 前必须解析为当前
  GraphInstanceId；
- DeclaredBroadcast 只投递给同 EventDomain 内显式声明对应 Adapter 的 Actor；
- Broadcast 默认不回送 Source Actor；
- 未声明广播、错误 Target、未知 Domain、旧 Epoch 和 Duplicate 都被拒绝并产生结构化
  诊断。
- 同一 SourceGraphInstanceId、SourceTimelineEpoch 与 SourceEventSequence 不得换用另一
  EventTypeId；这种 Sequence 跨类型复用属于协议错误。

去重 Key 为：

```text
SourceGraphInstanceId
+ SourceTimelineEpoch
+ SourceEventSequence
+ EventTypeId
```

同一个 `(SourceGraphInstanceId, SourceTimelineEpoch)` 的 Sequence 单调。Intent 候选
固定按高 Priority、小注册序号、SourceGraphInstanceId、SourceTimelineEpoch、
SourceEventSequence 排序；Reducer 不能把 EventBus 到达顺序当成玩法规则。

## 5. Actor EventInbox

Inbox 使用 Running 前固定容量的预分配双缓冲：

```text
Incoming buffer
  -- seal at Step start --> current Tick sealed batch

messages arriving after seal
  -> Incoming buffer for the next accepted Tick
```

Router Callback 只能校验、路由、去重和入队；不能调用 StateGraph、Operator、Commit
或修改 ContextFrame。Event-to-Intent Adapter 只读取 sealed batch 并产生 typed Intent
候选；StateGraph 看不到 raw Envelope，也没有 ACK、Dequeue 或 Consume 通道。

一条消息最多进入一个 IntentFrame。需要持续存在的请求必须由 StateGraph/Operator
处理后提交成 ContextFrame Pending State；Inbox 本身不是事实存储。

容量与生命周期规则：

- Capacity、Reliability Policy、Broadcast Manifest 和 Adapter 集合在 Running 前固定，
  运行中不能扩容或热替换；
- 普通 Suspend 在固定容量内继续积压，Resume 后下一次 Tick 交付；
- Rewind/Restore 停止接收 gameplay Event，拒绝新消息并记录诊断；
- Resume 建立新 TimelineEpoch，旧 Inbox Batch、旧 Packet 和旧去重窗口失效；
- Reliable 溢出报告结构化 Host Fault，Pre4 Host 必须暂停；
- Unreliable 溢出拒绝最新消息并递增诊断计数；
- Stop/Dispose 注销路由、清空 Inbox 和去重窗口；
- 音效、VFX、日志等可丢表现 Event 继续使用普通 EventBus，不进入 gameplay Inbox。

Pre2 只冻结消息身份、双缓冲、容量、投影和失败语义。中央 EventRouter、EventAgent
订阅、Host 注册、StableEntityId 解析和无幽灵订阅验证属于 Pre4。

## 6. Projection Flags 与 Restore Policy

ContextFrame 是完整内存状态。Descriptor 使用两组正交元数据，而不是三套平行事实面：

- Projection Flags 独立包含 `Temporal` 与 `Durable`；前者进入 Actor 时间历史，后者进入
  跨会话持久化投影，同一 Slot 可以同时拥有两项；
- Restore Policy 独立选择 `Stored`、`ResetToDefault` 或 `Derived`；
- `Derived` 必须声明依赖，由已恢复 Slot 确定重建，不单独保存为权威值；
- Derived 缺少依赖或出现不兼容 Layout/Codec 时，Restore 必须确定失败并诊断。

Ring Buffer 只保存 ContextFrame，不保存 IntentFrame、Inbox 或未发布 Outbox。需要跨
存档存在的“事件”必须先转化成 Actor Pending State 或世界事实。

Pre2 验证 Descriptor 与版本化 Codec Spike；Pre6 实现 Temporal Ring Buffer、Rewind
和 TimelineEpoch 切换；Pre13 实现 Durable Projection、Migration、Container 和世界
事实恢复。

## 7. Tick、Unity 与外部 Driver

- 一个 Unity Update 最多触发一个 CoCoTick；是否触发以及 Delta 由 Clock/Driver 决定；
- `CoCoTickFrame` 只接受有限正 Delta；
- Pause/Suspend 等价于零 Tick，不创建 Delta 为零的 Frame；
- 倒放不使用负 Delta，而是 Restore 旧 ContextFrame 后切换 Epoch；
- Unity Callback、Fusion Tick 或 Manual Driver 都只能作为 Host/Driver 输入；
- Animator/SMB Callback 不能立即调用 StateGraph 或修改当前 Frame；它只能进入表现
  路径，或经 Event/Intent 边界供后续 Tick 消化。

具体 `CoCoStateGraphHost`、Unity 生命周期、Clock/Driver、Binding Inspector 与 Router
装配属于 Pre4。Playable Animation、可控播放进度与 Root Motion 归 Pre11。

## 8. Network Adapter Boundary

Core 不依赖具体网络框架。网络 Adapter 只能：

- 将远端 Input/Authority 候选投影为 Intent Source；
- 将跨对象离散输入构造成合法 EventPacket，进入 Router/Inbox；
- 在完整 ContextFrame Commit Boundary 采集或应用 Actor 状态；
- 使用稳定 Graph/State/Instance/Activation/Timeline 身份；
- 将 Correction 安排到合法 Restore 或下一次正向 Tick，不能回调当前 StateLogic；
- 把 Camera、Animator 和其他本地表现排除在权威 gameplay ContextFrame 之外。

网络层不得为每个 gameplay State 复制一套网络 State，也不能直接驱动 Layer、Operator
或 ContextFrame Writer。

## 9. 架构依赖门禁

StateLogic 和 Layer 的程序集/API 表面不得引用：

- CoCoEventBus
- EventAgent
- EventEnvelope/EventPacket
- EventRouter
- EventInbox/EventOutbox
- Unity Object、Animator 或 Playable 类型

它们只读取当前 IntentFrame 与 Previous ContextFrame，并只产生 StateGraph 内部决策
和 OperationFrame 数据。固定 Layout 的读取、Intent 仲裁、Mailbox 投影和 Commit
协议不得在热路径依赖反射、字符串查找或稳态分配。

## 10. Pre 边界

- **Pre2**：Frame/Section/Descriptor/Registry、Intent 仲裁、Mailbox 协议、Restore
  元数据、Codec Spike 与纯契约测试。
- **Pre3**：GraphAsset/Compiler，汇总 Intent Requirement、Graph Operation Provides
  与 ContextFrame State Requirement，生成 Compiled Layout。
- **Pre4**：Host、Clock/Driver、EventRouter、EventAgent 订阅、Inbox 注册和生命周期。
- **Pre5**：Operator、Outcome、ContextFrame Commit 与 EventOutbox Publish。
- **Pre6**：Temporal Ring Buffer、Rewind/Resume 与 TimelineEpoch。
- **Pre13**：Persistence V2、Durable Projection、Migration、Container 与世界事实。
- **Pre16**：跨模块架构、性能、Mailbox、Suspend、Rewind 与幽灵订阅完整门禁。

## 11. Pre1 与旧 CCS Runtime

Pre1 冻结的 Graph/Runtime/Timeline Identity、正 Delta、Lifecycle、Diagnostic 和纯
StateLogic 方向继续有效。Pre1 文档中的 ContextRuntime/Source Merge/Frozen Context
候选 Flow 已被 Pre2 State Flow 取代，只保留在历史 Changelog 中。

仓库中的 0.3.9 CCS Runtime 暂时保留至对应 Pre 完成替换，只用于过渡期编译和历史
回归。其 MonoBehaviour State、可变 Context、Unity `Update/FixedUpdate` 驱动和旧
Operation/Event API 都不是 0.4 兼容承诺。0.3.9 项目应继续锁定旧 Revision；0.4
不提供双 Runtime 或自动迁移层。

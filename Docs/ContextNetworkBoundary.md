# CoCoFlow State Flow / Event Boundary

> State Flow contract: `0.4.0-pre.2` · StateGraph runtime integration:
> `0.4.0-pre.6` · Updated 2026-07-19
>
> This is the authoritative Pre2 data-flow and cross-Object communication
> boundary. Pre3 implements Asset/Compiler and Pre4 implements the staged
> StateGraph Runtime, Unity Host, ingress Router, Inbox, and Clock. Pre5 completes
> Operator execution and the composite Context commit that publishes output.
> Pre6 adds Host-owned Temporal projection history and public same-session
> Preview/Restore orchestration without changing this one-way Tick boundary.

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
CoCoEventPacket<TEvent>
  -> Host Gateway
  -> internal EventRouter for one EventDomain
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

比喻上，Host Gateway 是收发口，EventEnvelope 是快递单，EventRouter 是跨 Actor
分拣中心，Actor EventInbox 是门口信箱，Event-to-Intent Adapter 把来信翻译成本
Actor 的 Intent。Actor 本地事件直接从 Gateway 进入自己的 Inbox，不绕 Router；
StateGraph 永远只读取已经翻译并冻结的 IntentFrame。

## 2. Frame 职责与所有权

### IntentFrame

`IntentFrame` 是一个 CoCoTick 的唯一输入面：

- Input、AI、Network、Host Sampling 和 sealed EventInbox Adapter 都只能提供候选；
- 候选按 Running 前固定的 Priority/Reducer 仲裁；
- 每个 Source 每 Tick 最多采样一次，Frame 只冻结一次；
- 每个 GraphRuntimeInstance 通过 setup-only Factory 创建并独占 Reducer 实例，Actor
  之间不共享 Reducer 可变状态；
- Freeze 前对各 Reducer 的值状态建立 Checkpoint；任一 Reducer 失败或生命周期中断时，
  所有 Reducer 与部分 IntentFrame 一起回滚；
- 同 Tick 的所有 Layer 读取相同 Frame Identity 和值；
- 无候选时仍产生合法空 IntentFrame；
- 不保存 raw EventPacket、Envelope、Source 对象或 Unity 输入 API；
- 不进入 ContextFrame、Temporal History 或 Persistence；
- Pause/Suspend 不产生 Tick，因此不采样、不仲裁，也不生成新 IntentFrame。
- 开始下一轮 Collection、Cancel、Timeline Reset 与 Dispose 都立即使旧 IntentFrame
  失效；Source、Adapter 或 Reducer 抛异常时先回滚 Collection 再继续抛出；
- Cancel 同时回滚 Inbox Projection Claim，并禁止同一个 Tick 再次 Begin；
- 用户 Source/Adapter/Reducer Callback 不能重入 Begin、Sample、Project、Freeze 或
  Cancel。Callback 中请求 Dispose 会延后到 Callback 退出并最终优先执行。

跨 Source 冲突只依赖显式 Priority/Reducer，不依赖 EventBus 到达顺序。同 Source 的
离散输入使用 SourceEventSequence 保持顺序。

### OperationFrame

`OperationFrame` 是 StateGraph 为当前 Tick 生成的完整执行指南，也是唯一公开 Section
契约的 Frame：

- Operator 通过 Section Interface 声明自己需要的执行数据；
- 去重后的 Operator Requirement 并集必须与 Graph Operation Provides 精确双向匹配；
- 多个 Operator 要求同一 Interface 时按 Interface Identity 去重；
- 字段形状相同但 Identity 不同的 Interface 永远是不同 Section；
- Section Interface 只允许继承框架 Marker，Section-to-Section 继承非法；
- Pre3 的 Provides Manifest 必须包含完整 Section Shape：总字节数、字段数，以及每个
  字段的 dense index、ordinal name、unmanaged type、byte offset 与 size；
- Section 在 Operator 执行期间只读，不能作为 Actor 持久状态；
- 离散执行使用结构化 Section，并明确 Enabled、ActivationId 和 Sequence；
- 无输入时产生合法 Disabled/Zero Section，而不是建立第二套 Command Queue。

Pre4 StateGraph 通过携带固定合成等级的受限 Writer 产生 Section：Layer 列表越靠后
等级越高，同一 Layer 中子 State 高于父 State。同等级的后一次生命周期调用覆盖前一次，
但调用顺序不能反转等级，因此 Leaf→Parent Exit 不会让父 State 覆盖子 State。
Continuous Section 按字段合成；Discrete Section 只为最终胜出的贡献分配一次 Sequence。

OperationFrame 使用独立的两阶段协议：

```text
TryBegin -> Write -> TryFinalize -> FinalizedFrame -> Commit / Cancel
```

`TryFinalize` 只冻结候选数据，不更新 LastTick，也不消耗 OperationSequence。Pre4 把
Finalized OperationFrame 与候选 ActiveLeaf、Memory 和 Clock 一起放入单次使用的 staged
Tick；该 Tick 接受或取消前不能开始下一 Step。Pre5 只通过与 Context、Claim、
Clock 和 Sequence 共享的复合提交屏障接受它。取消会保持旧 Path、Memory、Clock、
Context Revision 与序列权威不变。

Pre2 冻结 Descriptor、Registry、固定 Layout 输入和显式测试构造入口。Pre3 已从
Graph 声明自动汇总、验证并编译 Provides Manifest 与不可变 Graph Lookup。Catalog
和 Registry 使用同一 Shape Validator；运行 binding 必须逐字段精确比较，不能只相信
Shape fingerprint。Pre5 在 Host 的显式序列化顺序中执行 Operator。Compiler 的具体
Schema 与诊断见
[StateGraph Asset and Compiler](StateGraphCompiler.md)。

### ContextFrame

`ContextFrame` 是一个 GraphRuntimeInstance 在 Commit Barrier 上的完整已提交逻辑
状态的 generation-scoped 只读 Handle。Arena 内部 Storage Cell 可以复用，但 Handle
捕获的 Generation 不可复用：

- 包含 GraphInstanceId、TimelineEpoch、Tick、Revision 和 Layout 兼容身份；
- 使用固定 StateBlock/Slot Layout，而不是公开 Root Context 或 Operation Section；
- StateBlock Owner 固定为 Graph、Operator 或 Actor；Graph state、Graph auxiliary、
  canonical Claim、Operator Outcome、Actor Binding 与 Derived rebuilder 必须把每个 Slot
  精确分类并覆盖一次，但可以有多个 Reader；
- 包含继续运行所需的 active state、transition progress、Activation、Actor 连续值和
  参与恢复的可控 Operator 进度，或包含能够唯一重建它们的值；
- 不包含 Inbox、IntentFrame、raw Envelope、Source、执行中局部变量、未发布 Outbox
  或 Unity Object 引用图；
- 不同 GraphRuntimeInstance 不共享可变 StateBlock；
- 首 Tick 使用 Layout 中的真实 Default 建立 `CoCoContextFrameReadView`，不构造伪
  Tick 0 或 Revision 0 Frame；首次成功 Commit 产生 Revision 1；
- Retain 一个存活 Frame 会阻止对应 Cell 被复用；Release 后 Cell 可以服务新的
  Generation，但所有旧 Handle 永久失效，不会因 Cell 复用重新变为可读；
- 包括无操作 Tick 在内，每次成功 Commit 都递增 Revision；
- Restore 只接受已提交 ContextFrame 或合法投影，在新 TimelineEpoch 形成新 Revision，
  并记录 Source GraphInstanceId、TimelineEpoch、Tick 与 Revision。

ContextFrame 是唯一可以携带、Retain 与 Restore 的 Actor 提交记录。Graph
Path/Memory/Activation、ActorClock 与 Claim 的 live cache 只能是它的镜像，或由它唯一
重建，不能形成第二份权威。ContextFrame 只承诺单 Actor 逻辑恢复；它不恢复世界、其他
Actor，或撤销已经交付给其他 Actor 的 Event 后果。

Layout 的实际 Default 由可信 Project Provider 提供；semantic fingerprint 是 Provider
对该值与 Manifest 语义一致性的声明 token，不是框架从 `defaultValue` 重算的 canonical
hash。Runtime 初始化后、公开前会捕获一次初始 Graph State 并与这些 Default 比较，
但不产生 Revision。

## 3. Operation、Outcome 与 Commit Barrier

Operator 是允许接触 Unity 对象和项目服务的执行边界。StateGraph 不调用具体 Unity
API，而是交付 OperationFrame；Operator 读取对应 Section，执行后产生 Outcome 与
可选 EventOutbox Candidate。

Commit 顺序固定：

```text
Preview -> Context Prepare -> Intent Collect/Freeze -> Graph Stage + Trace
  -> Graph-owned State/Value Capture -> Claim Arbitration/Claim Capture
  -> Operators/Outcomes/Outbox -> Actor-owned Capture -> Derived Finalize
  -> Composite Preflight -> Temporal Projection Capture
  -> no-fail Commit + Temporal Publish -> complete EventOutbox Publish
```

ContextFrame Commit 是当前 Tick 唯一已提交的 gameplay 逻辑权威边界：

- Commit 成功产生一个完整的新 ContextFrame；
- 所有可预检的验证与容量预留都发生在 Operator 执行前；Context 无空 Cell 是可
  重试结果，不推进 Tick 或 Inbox；Prepared Token 只提供 Writer 与 `TryFinalize`；
- 任何真实 Operator Callback 前一次性计算 Claim。Discrete Claim 绑定
  `Enabled + ActivationId`；一个 Operator 的多个 Claim all-or-none，按 Priority、Host
  列表顺序、OperatorId 稳定仲裁；每个 Claim 指向一个 Graph-owned Claim State Slot，
  同 ClaimId 的竞争者必须指向同一 Slot；正常败者得到 `ClaimDenied` 而不 Fault，
  canonical Slot 仍由仲裁器写一次；
- Outcome Writer 绑定 OperatorId、Transaction Token 与该 Operator 的 Slot 白名单，
  Callback 退出后立即失效。它只能写 Manifest 内非 Derived、Operator-owned 且唯一
  owner 的 Slot；后序 Operator 仍只读上一个 committed Context；
- Writer 拒绝 Derived Slot。Finalize 在每个成功 Tick（包括 no-op Tick）从权威输入按
  确定拓扑重建所有 Derived；只有 Finalized Token 可以 Commit；
- Graph-owned 捕获在 Claim 与任何真实 Operator Callback 前完成；Actor Binding 在
  Operators 后捕获 Actor-owned Slot。两者与后序 Operator 都只能读取 Previous Context，
  不能读取本 Tick candidate；
- Derived Rebuilder 返回失败或抛异常时先放弃 Candidate，旧 ContextFrame 继续权威；
  Host 在完成取消后将抛出路径收敛为结构化 Fault Diagnostic；
- 启用 Temporal 时，从 finalized candidate 编码 projection staging 仍属于可失败段。
  Codec 失败会取消整个 Tick；权威屏障后发布已准备 Ring entry 不再失败；
- StateGraph 不能在当前 Tick 读取执行中的 Outcome；
- Outbox Candidate 在 Commit 前不可见；
- Commit 失败、Cancel、Preview 或 Restore 时，旧 ContextFrame 继续权威；
- 失败路径同时取消 Pre4 staged Tick，不发布 Event、不消耗最终 EventSequence 或
  OperationSequence，也不产生跨 Actor 副作用；
- no-fail barrier 内禁止 Callback、分配、容量申请与可失败 Mutation；它一次性交换
  Context 权威，提交 OperationSequence、Graph Path/Memory/Activation、ActorClock、Claim、
  连续 EventSequence range 与 Intent Tick；
- 真实 Operator Callback 之后失败时，旧 Context 保持权威，Host Fault 并标记
  `RequiresWorldCorrection`；不伪造 Unity 世界回滚。

EventOutbox 使用 typed preallocated lanes 和全局 order ledger。Finalize 只预检容量、
metadata 与 Sequence overflow，不消耗 Sequence。Commit 后按 Host Operator 顺序、再按每个
Operator append 顺序发布；同一 GraphInstance/Epoch 的所有 EventType 共用连续
Sequence。Subscriber 异常继续隔离；基础设施异常记录 Fault 并继续发布剩余已提交
Packet，不回收 Sequence、不自动重发、不回滚已送达 Event。

Trace 先按 compiled order 记录 Source/Window 合法且 Conditions 全通过的 Transition
Candidate，再为 Winner 单独记录一条。Frame Reference 只保存 identity、精确 Layout
metadata、Revision 与 `HasCommittedFrame`，不 Retain Context Handle；首 Tick Previous
引用精确 Layout Default 且 `HasCommittedFrame=false`，不伪造 Revision 0。失败事务以
Cancelled 结束，不得出现 Commit、Sequence 或 Published。

## 4. EventPacket 与路由身份

0.4 gameplay 消息使用一个原子值：

```text
CoCoEventPacket<TEvent> = CoCoActorEventEnvelope + immutable typed payload
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

- 一个 Graph 的全部 Event declaration 必须属于同一 EventDomain；没有 Event
  declaration 的 Host 不创建 EventInbox 或 Router；
- 每个有 Event declaration 的 GraphRuntimeInstance 只有一个 EventInbox；
- 每个 EventDomain 惰性创建一个 internal EventRouter；
- EventDomain 与 ClockDomain 分离；
- 本 Actor local event 从 Host Gateway 直接进入自己的 Inbox，不经过 Router；
- Targeted 消息按当前 TargetGraphInstanceId 做 O(1) 路由；
- StableEntityId 只用于存档、跨加载、网络和诊断，进入本地 Router 前必须解析为当前
  GraphInstanceId；
- DeclaredBroadcast 只投递给同 EventDomain 内显式声明对应 Adapter 的 Actor；
- Broadcast 默认不回送 Source Actor；
- 未声明广播、错误 Target、未知 Domain、旧 Epoch 和 Duplicate 都被拒绝并产生结构化
  诊断。
- 同一 SourceGraphInstanceId、SourceTimelineEpoch 与 SourceEventSequence 不得换用另一
  EventTypeId；这种 Sequence 跨类型复用属于协议错误。

Pre3 在 Graph 级编译 `EventTypeId + ProvidedIntentId` 静态 declaration，并将 Event
Domain、Payload Type 和 Provided Intent Type 保存到 Intent Requirement Manifest。
这些 declaration 只证明静态类型、Intent Shape 与 `MaxContributions` 容量下界成立；
不包含 Adapter 实例、priority、projection capacity、broadcast、Inbox 或 reliability。
Pre4 在 Host Start 前对实际 Adapter 执行 missing/extra/duplicate/type-exact coverage；
任一不匹配使 Host 留在 Created，零 Router 注册、零 callback、零 Tick。同一个
EventType 投影到多个 Intent 时只建立一条 typed Inbox Lane，再依声明运行多个 Adapter。

去重 Key 为：

```text
SourceGraphInstanceId
+ SourceTimelineEpoch
+ SourceEventSequence
+ EventTypeId
```

同一个 `(SourceGraphInstanceId, SourceTimelineEpoch)` 的 Sequence 单调。Intent 候选
固定按高 Priority、小注册序号、SourceGraphInstanceId、SourceTimelineEpoch、
SourceEventSequence 排序；Reducer 不能把 Router 到达顺序当成玩法规则。

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
- Inbox 进入 Running 前必须绑定存活、Bindings 已冻结的 Intent Runtime。Inbox typed
  lanes 必须与 Runtime 去重后的 Adapter Manifest 按 EventDomain、EventType 和 Payload
  Type 双向精确匹配；每条 Lane Capacity 不得超过对应 Adapter 的最小 Projection
  Capacity。绑定、Start、Tick Seal、Suspend 与 Resume 时 Runtime 还必须处于 idle；
  Collecting 期间到达的消息不能通过再次 Seal 进入当前 IntentFrame；Start 失败时 Inbox
  保持 Created；
- 普通 Suspend 保留 Router 注册并在固定容量内继续积压，Resume 后下一次 Tick 交付；
- Begin Temporal Preview 立即清除 queue、sealed batch 与 dedup window；后续
  gameplay Event 被 drop 并计数，不留到 Resume；
- Preview Cancel 保持原 TimelineEpoch，但不复活 Begin 时已清除的 backlog；
- Confirm 成功切换新 TimelineEpoch 后，旧 Inbox Batch、旧 Packet 和旧去重窗口全部
  失效，只接受属于新 Epoch 的新输入；
- 普通 Suspend/Resume 不是 Rewind，保持当前 TimelineEpoch 与容量内合法积压；
- Reliable 溢出在安全边界锁存 Host Fault；Fault 门禁拒绝新的 gameplay 输入与普通
  Resume；
- Unreliable 溢出拒绝最新消息并递增诊断计数；
- Stop/Dispose 注销路由、清空 Inbox 和去重窗口；
- 绑定的 Intent Runtime Dispose 时，Running Inbox 停止并清空；Created Inbox 只解除
  绑定，以便替代 Runtime 重新绑定。后续 Enqueue/Seal/生命周期入口必须拒绝失效绑定；
- Callback 内请求的 Inbox Stop/Dispose 延迟到 Callback 退出后执行，先取消当前
  Collection 并回滚 Projection Claim；失效 sealed batch 不得继续贡献；
- 音效、VFX、日志等可丢表现 Event 继续使用普通 EventBus，不进入 gameplay Inbox。

Host 完成全部启动检查后才最后注册 Router；Stop/Dispose 首先注销。一个 Domain 的
最后 Host 离开时，Router 释放其 internal EventAgent subscription。Router callback
只接收原子 `CoCoEventPacket<TEvent>` 并校验、入队，不调用 Runtime、Operator 或 Context；
它不使用旧 `PublishWithEnvelope`。Pre5 只在复合提交成功后通过 Host internal
outbound seam 发布 EventOutbox；发布期间的 Destroy/Stop/Dispose 请求在完成整个
committed list 后再收尾。

## 6. Projection Flags 与 Restore Policy

ContextFrame 是完整内存状态。Descriptor 使用两组正交元数据，而不是三套平行事实面：

- Projection Flags 独立包含 `Temporal` 与 `Durable`；前者进入 Actor 时间历史，后者把
  Slot 标记为 Pre13 Durable Projection 的候选，同一 Slot 可以同时拥有两项；
- Restore Policy 独立选择 `Stored`、`ResetToDefault` 或 `Derived`；
- `Derived` 必须声明依赖，在每次 Commit Finalize 和 Restore Finalize 中由已恢复 Slot
  确定重建，不允许 Writer 直接写入，也不单独保存为权威值；
- 某 Projection 包含 Derived Slot 时，也必须包含其全部传递 Stored/Derived 依赖；
  ResetToDefault 依赖可以确定恢复，因此豁免。Layout Freeze 主验证这项闭包，Codec
  创建时再次防御性验证；
- Derived 缺少依赖或出现不兼容 Layout/Codec 时，Restore 必须确定失败并诊断。

Temporal Ring 不保存或 Retain 完整 ContextFrame。每个 Host 独占一个预分配、
固定条目容量的 Ring：

- 只编码 `Temporal + Stored` Slot 的 exact-layout payload；
- `Temporal + ResetToDefault` 不存值，Restore 时取 Layout default；
- `Temporal + Derived` 不存结果，Restore 时从闭包完整依赖重建；
- 未标记 Temporal 的 Stored Slot 也取 Layout default，不与 Rewind 前 current 值混合；
- Entry 另存不可变 GraphInstance、TickFrame、Revision 和 Origin 元数据；
- Capacity 包含 current，首次成功 Commit 后 Count 为 1；0 关闭 History，启用时至少为
  2，容量 1 在启动时拒绝；满后覆盖 oldest，Running 期间不扩容或热换。

Capacity 0 不要求 Restore Binding：错误类型、已销毁或 Host 边界外的 assignment
会被忽略；合法且位于本 Host 内的 Binding 可仅为非 Temporal 脏失败后的通用
World Correction 保留。

捕获源是已 Finalize Context candidate，但捕获在 authority swap 之前完成。
Codec/capture 失败会令整个 Tick 失败，旧 Context/Graph/Clock/Claim/History 不变，
零 Outbox 与零最终 Sequence。权威成功交换后，Ring publish/overwrite 必须 no-fail。
普通 ContextFrame `Retain`/`Release` 契约继续有效，只是 Temporal Ring 不使用它。

Preview 只移动非权威游标并调用 Host 的唯一同步
`ICoCoContextRestoreBinding`。它不使用负 Delta，不运行 State Enter/Exit、
Condition、Transition、Operator、Actor capture、Event 或 Trace。Cancel 仅在本次
会话至少成功完成一次 Preview 投射后通过同一 Binding 重新投射 current authority；
Begin 后直接 Cancel 不调用 Binding。两条路径都不交换逻辑权威或切换 Epoch。

Confirm 先在屏障外完整验证与准备 Context、Graph Path/Memory、Clock 和 Claim，
再只调用一次 Binding。Unity 投射成功后，no-fail barrier 原子交换逻辑权威，
丢弃所选点之后的 future，并把新 Epoch restore commit 记为新 branch head。Restore 保持
Source TimelineId 与 ClockDomainId，ExecutionSequence 严格推进，TimelineEpoch 严格大于
Source 与 Current Epoch。下一次被接受的正 Delta Tick 才恢复正常计算。

没有早先 Preview 投射且 callback 尚未开始时，Binding preflight 失败只拒绝请求，
Host 保持健康。一旦 callback 已开始，或会话仍有成功 Preview 投射，Binding 拒绝、
抛异常、被销毁或可能局部修改 Unity 时，旧逻辑权威继续有效，Host Fault 且
`RequiresWorldCorrection=true`。Correction 从最后逻辑权威经同一 Binding 重新投射
Unity，只在成功后清除对应的可恢复 Fault。Temporal payload 只是
same-session、exact-layout 内部表示，不是稳定 Wire Identity 或跨会话存档格式。
Pre13 负责 Durable Save Document、StableEntityId 解析、Migration、Container 和世界事实。

## 7. Tick、Unity 与外部 Driver

- 一个 Unity Update 或 FixedUpdate 最多触发一个 CoCoTick；是否触发以及 Delta 由
  Host 内部 Clock/Driver 决定；Manual 每次调用都是独立 Tick，不使用 accumulator 或
  catch-up；
- `CoCoTickFrame` 只接受有限正 Delta；
- Actor TimeScale 同样必须有限且大于零；Pause/Suspend 等价于零 Tick，不创建 Delta
  为零的 Frame；
- 倒放不使用负 Delta；Preview 只投射历史，Confirm 在新 Epoch 执行一次正式 Restore；
- Unity Callback、Fusion Tick 或 Manual Driver 都只能作为 Host/Driver 输入；
- Animator/SMB Callback 不能立即调用 StateGraph 或修改当前 Frame；它只能进入表现
  路径，或经 Event/Intent 边界供后续 Tick 消化。

Host 启动先完成 Compile、Provider Configure/Freeze 与 Transaction Preflight；只有 Graph
producer、必需的 Actor Binding、启用 Temporal History 时的 Restore Binding、
Operator/Outcome/Claim、Temporal 容量与 Outbox 容量全部合法，才创建 Clock、
Runtime、执行 Start 和初始 Graph/default validation，最后公开 Host 字段并注册 Router。
因此配置错误不会触发 Logic/Condition/Memory factory、Reset、Fingerprint、Graph capture、
Operator 或 Actor callback，Host 保持 `Created`。

`CoCoStateGraphHost` 仍是框架唯一 public MonoBehaviour；Asset 是唯一必填项，其他
Driver、AutoStart、TimeScale、Temporal history 与诊断容量都是同一 Host 的设置。
Restore Binding 是一个显式项目组件引用，不通过扫描发现。Runtime、Clock、Inbox、
Router、Logic、Condition、Memory 与 Temporal Ring 都是内部普通对象。Playable
Animation、可控播放进度与 Root Motion 归 Pre11。

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

Pre3 Editor Analyze 与 Player build preflight 会从 Catalog 记录的全部作者类型 root
遍历完整已解析程序集依赖闭包。每个可达自定义程序集都必须有 asmdef 且
`noEngineReferences:true`；命中 Unity、Editor、legacy Core、StateGraphAuthoring、
Gameplay、Modules，或遇到无法证明安全的自定义 precompiled dependency 时失败关闭。
Runtime 的 direct-reference guard 只是快速防线，不代替闭包验证；纯 Compiler 本身不
扫描程序集。

它们只读取当前 IntentFrame 与 Previous ContextFrame，并只产生 StateGraph 内部决策
和 OperationFrame 数据。固定 Layout 的读取、Intent 仲裁、Mailbox 投影和 Commit
协议不得在热路径依赖反射、字符串查找或稳态分配。

## 10. Pre 边界

- **Pre2**：Frame/Section/Descriptor/Registry、Intent 仲裁、Mailbox 协议、Restore
  元数据、Codec Spike 与纯契约测试。
- **Pre3**：已交付 GraphAsset/Compiler、框架规范化 FrozenConfig、Event-to-Intent
  静态 declaration、包含完整 Shape 的 Graph Operation Provides、ContextFrame State
  Requirement、不可变 compiled lookup，以及 Editor/build-time 完整依赖闭包验证。
- **Pre4**：已交付纯 StateGraph Runtime、每 Actor 独占状态、Host、Clock/Driver、实际
  Event-to-Intent Adapter coverage/binding、internal EventRouter、EventAgent 订阅、
  Inbox 注册、staged Tick 与生命周期。
- **Pre5**：已交付 Host 显式 Operator 列表、Graph/Claim/Operator/Actor/Derived producer
  ownership、ContextFrame 复合 Commit、committed EventOutbox Publish、immutable Trace、
  完整 Actor 纯 Restore validation 与内部无 callback apply seam。
- **Pre6**：已交付 Host-owned Temporal projection Ring、public Preview/Restore/Cancel/Correction
  编排、Mailbox 阻断与新 TimelineEpoch branch head。
- **Pre11**：Animator/Playable/SMB 替代与视觉倒放映射。
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

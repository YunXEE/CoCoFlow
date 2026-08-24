# CoCoFlow

[English](README.md) | [简体中文](README.zh-CN.md)

> **版本**：0.4.0-rc.0 · **Unity**：6000+
>
> Pre15 冻结 0.4 公共 API 面，退出旧版 Mono FSM 与输入 Bridge 运行时，
> 将项目 Gameplay 迁入 Sample 边界，并在 Unity 6000.3/6000.5 上完成
> 依赖组合与 Player 验证。

CoCoFlow 是面向 Unity 6、新单机 3D 冒险与动作项目的 State Flow + Layered
HFSM 框架。0.4 将输入意图、状态图决策、副作用执行、Actor 已提交状态和跨 Object
消息分开处理，不再把这些职责混入一个可变 Context。

## State Flow

每个被接受的 CoCoTick 只沿一个方向执行：

```text
Input / AI + sealed EventInbox
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
CoCoEventPacket<TEvent>
  -> Actor Host Gateway
  -> one internal EventRouter per EventDomain
  -> target Actor EventInbox
  -> next accepted CoCoTick
  -> Event-to-Intent Adapter
  -> IntentFrame
```

Host Gateway 是 Actor 的统一收发口，EventEnvelope 是快递单，EventRouter 是跨 Actor
分拣中心，EventInbox 是门口信箱。本地事件直接进入同一个 Host 的 Inbox；只有跨
Actor Targeted 与声明式 Broadcast packet 才经过 Router。Event-to-Intent Adapter
负责把 sealed message 翻译成本 Actor 的 Intent。StateGraph 永远不读取 raw callback、
Envelope、Router 或 Mailbox。

## 冻结词义

| 词条 | 含义 |
|---|---|
| `IntentFrame` | 一个 CoCoTick 的不可变输入；只采样、仲裁并冻结一次，不持久化，也不进入倒放历史。 |
| `OperationFrame` | StateGraph 产生的完整执行指南；只有它公开 Section 契约。 |
| `ContextFrame` | 指向单个 Actor 在 Tick 边界完整已提交逻辑状态的 generation-scoped 只读 Handle；它是直接 Retain/Restore 的权威输入与 Temporal/Durable 投影的语义源，Temporal Ring 不 Retain 该 Handle。 |
| `Temporal projection` | 从已 Finalize 且成功的 Context candidate 捕获、由 Host 独占的固定容量历史 payload；只含投影的 Stored 字节与不可变源元数据，不是被 Retain 的 ContextFrame。 |
| `EventInbox` | 一个 GraphRuntimeInstance 的待处理跨 Object gameplay 输入，不是事实存储。 |
| `EventOutbox` | Operator 执行期间产生的跨 Object 输出候选，只有 ContextFrame Commit 成功后才发布。 |
| `CoCoEventAgent` | 只负责 EventBus 订阅生命周期；不路由、不排队、不拥有也不持久化消息。 |

`IntentFrame`、`OperationFrame` 和 `ContextFrame` 不是别名，不能互相替代。
Inbox、raw Envelope、当前 IntentFrame 和尚未发布的 Outbox 候选都不会混入
ContextFrame。

## StateGraph Asset 与编译

`CoCoStateGraphAsset` 是唯一序列化创作真相。它保存带稳定 ID 的 Graph、Layer、递归
State 和由 Layer 单一持有的 Transition。rename、move、reorder、save/reload 与 Config
编辑不改变既有 ID；整 Asset、Layer 或 State 子树复制会为新副本重建对应 ID，并重映射
其内部引用，复制出的 Config 数据不会继续与源对象共享。但 Layer 列表顺序具有运行
语义：前低后高，因此 reorder 会改变内容 fingerprint 和合成结果，却不改变 Layer ID。

Host 运行前，Unity-facing snapshot 边界先深度冻结 Asset，再把纯数据交给
`CoCoStateGraphCompiler`。成功编译只产生一份不可变 `CoCoCompiledStateGraph`，其中
包含层级/邻接查找表，以及三类 Manifest：

- Intent Requirement；
- Graph Operation Provides；
- ContextFrame State Requirement。

Intent Manifest 同时携带 Graph 的 immutable Event-to-Intent 静态 declaration。Pre3
校验 Event Domain、Payload Type、Provided Intent Type、contribution capacity 下界，
以及每 Graph 只能有一个 EventDomain。Pre4 实例化声明的 Adapter，并在 runtime
binding coverage 不是精确匹配时拒绝 Host Start。Adapter 的权威执行顺序来自 Asset
declaration list，并由 compiled manifest 保留；binding Provider 只能满足声明，不能
改变这一语义顺序。

Config Freezer 只能写入框架拥有的 typed Schema。字段快照由框架封口、防御复制并计算
fingerprint；Snapshot 的不可变性不依赖作者自律。

编译和验证不会构造或执行用户 StateLogic、Condition。任何 Error 都阻止产生 compiled
result；unreachable State 等 Warning 不阻止。普通 Transition 环与无出口终止 State
合法；层级环、缺失目标、重复 ID 和跨 Layer 边为 Error。

同一 Asset 内容 fingerprint 与 catalog 会返回同一个缓存结果；成功和失败结果都会缓存，
但只有成功结果包含共享 compiled graph。每个 Host 的可变 runtime state 不存入共享对象，
完整 Schema、身份、诊断、线程与延期边界见
[StateGraph Asset 与 Compiler](Docs/StateGraphCompiler.md)。

预发布序列化 Schema 仍为 v1，但 Pre4 在原位重新定义它，不承诺迁移实验性 Pre3 Asset。
Completion 与 InterruptPolicy 被删除，normalized timing 改为 Activation-scoped
ActionProgress。

Pre7 Editor 每次只编辑一个 Asset、选中的 Layer 与一个递归 State scope。Transition
两端只能是同一 Layer 的 leaf State，作者字段只有 Conditions、一个 Window 与同源唯一
Priority；Completion 与 Interrupt 都不是作者字段。每次拓扑修改都经过支持 Undo 的作者
操作，删除 State 子树会同时删除全部 incident Transition。删除 initial State 时，如有
surviving sibling，用户必须显式选一个有效替代；如无 survivor，可以显式确认清空引用并
留下 compiler-invalid 草稿。Pre7 Copy/Paste 只限同一 Asset，新副本使用新 ID 且只保留
子树内部 Transition；跨 Asset 与跨 Editor session clipboard 延后。

State 位置是按稳定 State ID 保存、独立版本化的表现数据，不进入 runtime schema v1、
compiler snapshot、content fingerprint 或 compilation-cache key。selection、breadcrumb、
foldout、pan/zoom、search 与 diagnostic location 是只保证跨 Domain Reload 的 session
state。Editor 以稳定 identity 确定性枚举 internal Catalog，只叠加已有 Intent、Graph
Operation 与 ContextFrame State 三类 Manifest。Simple preset 创建一个 Layer 与两个
generic leaf State，并连接一条 same-Layer Transition；Combo 创建通用
`Step1 -> Step2 -> Step3 -> Step4 -> Exit` 拓扑。两者都不创建 gameplay logic、
animation timing、Sample 或第四类 Manifest。详见
[StateGraph Editor 与 Runtime Debugger](Docs/StateGraphEditor.md)。

## StateGraph Runtime 与 Host

每个 Actor 最少只需要一个框架组件和一个 Asset；存在 Actor-owned Slot 时，再显式引用
一个项目 Actor Binding：

```text
Actor GameObject
├─ CoCoStateGraphHost        必需
│  └─ StateGraphAsset       必需
├─ Intent Source scripts     可选；由 Host 以显式顺序引用
├─ Event Adapter scripts     按 Manifest 要求；由 Host 以显式顺序引用
├─ Operator scripts          可选；由 Host 以显式顺序引用
├─ Actor Context binding     仅有 Actor-owned Slot 时必需
└─ Context Restore binding   启用 Temporal history 时必需
```

Runtime、Clock、Inbox、Router、Logic、Condition 与 Memory 都不是组件。Host 不扫描
旧 Controller、Context Provider、子物体或场景。多个 Host 可以共享不可变 compiled
graph，但各自独占 StateLogic/Condition 实例、双 Memory Bank、active leaf、Clock、
Inbox、staged Tick 与锁存 Fault。

Host 拥有用户确认的具体场景组件引用及其顺序；Project Provider 继续拥有冻结 Catalog、
State/Condition factory、通用 Intent/Adapter binding、Operation/Context type、Codec、
default 与 AOT-safe construction 的类型权威。Editor 建议仅供参考，用户确认前不写入；
Running 时配置只读。Host 不通过场景扫描发现这些引用。

任何 Clock 或 Runtime factory 运行前，Transaction Preflight 必须精确覆盖 Graph state、
Graph value、Claim、Operator、Actor 与 Derived Context Slot，并验证 Operator、Claim、
Actor Binding 与 Outbox 配置。无效设置令 Host 保持 `Created`，不运行 callback，也不
产生 Router 可见状态。Actor Binding 是 Host 的单一显式引用，不通过场景扫描发现。

`Start` 只确定初始叶子，不运行 callback。每 Layer 首 Tick 的新路径先 Parent→Child
运行可选 Enter，再 Root→Leaf 运行必选 Update。叶子 Update 可以请求零到多个预声明
出边；全部 Update 结束后，Runtime 统一计算 window、Condition，并选唯一最高
Priority。Transition 两端必须是叶子，同一源叶子的出边 Priority 必须唯一。

Transition Tick 始终保留源路径为有效路径，因此可以同 Tick Update + Exit；成功提交的
目标在下一 Tick Enter + Update。Enter 与 Exit 是独立可选阶段，Update 每 Tick 必跑，
每 Layer 每 Tick 最多一个 Winner。系统没有 Completion 状态，ActionProgress 到 `1`
也不会自动退出。

同一 Activation 内，ActionProgress 必须有限且单调非递减；重复当前值是合法停滞。
任何下降都会取消候选 Tick、保留上一份已提交权威状态并锁存 Fault。事务回滚只是恢复
上一份已提交权威状态，绝不允许 ActionProgress 倒退。

Layer 与 path depth 给 Operation 写入固定等级：高 Layer 覆盖低 Layer，子 State 覆盖
父 State。Continuous Section 按字段合成；Discrete Section 只为最终赢家消耗一次
Sequence。Operation Finalize 只生成单次使用的 staged Tick，不改变权威状态。Pre5
的复合提交屏障是 Host 接受 staged Tick 的唯一路径；在此之前，新 Path、
Memory、Clock、Context Revision、Claim、OperationSequence 与 EventSequence 都不可见。

Host 的显式 Operator 列表同时定义确定性执行顺序，去重后的 Requirement 并集必须
精确覆盖 Graph Operation-provides Manifest。任何真实 Operator Callback 前先按 Priority、
Host 顺序与 Operator ID 完成 Claim 仲裁。败者得到 `ClaimDenied`，不进入 Callback，
也不能写 Context 或 Outbox；普通竞争不会令整个 Tick Fault。

Transition window、self-loop、rollback、生命周期、Fault 与事件路由的完整语义见
[StateGraph Runtime 与 Host](Docs/StateGraphRuntime.md)。

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

Pre3 保存每个 provided Section 的完整不可变 Shape：总字节数，以及每个字段的 dense
index、ordinal name、unmanaged type、byte offset 与 size。Catalog 和 Registry 共用
同一 Shape Validator，runtime binding 必须逐字段比较完整 Shape，不能把 fingerprint
当作正确性证明。

Pre4 按固定 Layer/path 等级写入，并在不消耗 Sequence 或 LastTick 的情况下 Finalize
OperationFrame candidate：

```text
TryBegin -> Write -> TryFinalize -> FinalizedFrame -> Commit / Cancel
```

Pre2 提供 Section 契约与显式测试 Layout，Pre3 编译自动汇总的 Graph Provides
Manifest，Pre4 产生 finalized staged frame，Pre5 执行匹配的 Operator，并且只在
对应 Context candidate 成功 Finalize 后才接受该 Frame。

## ContextFrame 与 Restore

`ContextFrame` 是单个 Actor 的完整已提交逻辑状态，不是世界快照，也不是 Unity
场景 Object Graph。固定的 StateBlock/Slot Layout 必须包含从该 Commit Boundary
继续运行所需的 Graph、Activation、Transition Progress、Actor 数值和可控 Operator
进度，或包含能够确定重建它们的数据。它是唯一可以携带、Retain 与 Restore 的 Actor
提交记录；Graph、Clock 与 Claim 的 live cache 只能是镜像，或由它唯一重建。

每个直接 Slot 恰有一个 producer：Graph-owned 数据来自 Graph state record 或 Graph
value producer，Graph-owned Claim Slot 由 Claim 仲裁写入，Operator-owned 数据来自唯一
Operator Outcome，Actor-owned 数据来自 Host 的单一 Actor Binding；Derived Slot 仍只由
既有 rebuilder 产生。

Descriptor 元数据包含两个独立维度：

- Projection Flags 分别标记 **Temporal**、**Durable**，同一 Slot 可以同时拥有两项。
- Restore Policy 独立选择 **Stored**、**ResetToDefault** 或 **Derived**。
- Derived Slot 必须声明依赖，在 Finalize 中由 Stored/Default 输入确定重建；它不可直接
  写入，也不形成第二个权威值。
- 投影包含 Derived Slot 时，必须同时包含重建它所需的全部传递 Stored/Derived 依赖；
  ResetToDefault 依赖可以确定重建，因此无需进入投影。

Project Provider 提供实际 Layout default，并以 semantic fingerprint 声明它与 Manifest
兼容。该 token 不是框架从提供值重算的 canonical hash。Runtime Start 后会捕获一次初始
Graph 状态并与这些 default 比较，但不会因此创建 committed Revision。

`ContextFrame` 是 Arena Storage Cell 上带 Generation 的 Handle，不是可复用 Cell 本身。
Retain 存活 Frame 会阻止对应 Cell 被复用；该 Generation 被释放且 Cell 复用后，所有旧
Handle 永久失效，不能观察或操作新 Generation。

Commit 使用显式的两阶段权威边界：

```text
TryPrepare -> Writer -> TryFinalize -> Finalized Commit -> Commit
```

Writer 只能写入权威 Stored/ResetToDefault 输入。Finalize 在每个成功 Tick（包括 no-op
Tick）按确定依赖顺序重建所有 Derived Slot。Finalize 失败会放弃候选，Previous
ContextFrame 继续保持权威。

Restore 永远落在一个完成的 Commit Boundary。它不会恢复 Inbox、IntentFrame、
CoCoEventAgent 订阅、未发布 Event、执行一半的 Operator、其他 Actor，或已经交付给
其他 Actor 的后果。

普通 `ContextFrame.Retain()`/`Release()` 仍可用于 generation-scoped 长期读取。
Temporal history 刻意不 Retain 这些 Handle：每个启用的 Host 独占一个预分配的
exact-layout projection ring。只编码 `Temporal + Stored`；ResetToDefault 从 Layout
default 获得，Derived 从闭包完整的依赖重建，未标记 Temporal 的 Stored 也回到
Layout default。

每次成功 Context commit 都在权威交换前从 finalized candidate 捕获。
捕获失败会取消整个 Tick 并保留旧权威；复合屏障后发布已准备历史条目不再
失败。Capacity 统计包含 current 的 committed entry，0 表示关闭；启用时至少需要
2 个 entry，以同时容纳 current authority 与一个更旧 commit。满 Ring 覆盖 oldest。
Capacity 0 不要求 Restore Binding：错误类型、已销毁或 Host 边界外的 assignment
会被忽略；合法且位于本 Host 内的 Binding 可仅为非 Temporal 脏失败后的世界
Correction 保留。

Temporal Preview 与 Runtime lifecycle 正交，只移动非权威历史游标并调用唯一
显式同步 `ICoCoContextRestoreBinding`；它不使用负 Delta，也不运行 State、
Condition、Transition、Operator、Event 或 Trace。Cancel 仅在本次会话至少成功完成
一次 Preview 投射后重新投射未变的 current authority；Begin 后直接 Cancel 不调用
Binding。Confirm 只调用一次同一 Binding，然后原子交换 Context、Graph、Clock 与
Claim，丢弃被放弃的 future，并在同时新于 Source 与 Current 的 TimelineEpoch 记录
新 branch head。下一次被接受的 Tick 才继续正 Delta 正向运行。

没有早先 Preview 投射且 callback 尚未开始时，Binding preflight 失败只拒绝请求，
Host 保持健康。一旦 callback 已开始，或会话仍有成功 Preview 投射，Binding 拒绝、
抛异常、被销毁或可能部分改动 Unity 时，旧逻辑权威保持不变，Host 锁存 Fault 并
设置 `RequiresWorldCorrection`。`TryCorrectWorld` 通过同一 Binding 重新投射最后权威，
然后只清除对应的可恢复 Fault。完整 Host API 与失败语义见
[Temporal Rewind](Docs/TemporalRewind.md)。Pre13 负责 Durable Save、Migration、世界事实与
生成实体重建。

## Actor Mailbox 规则

Gameplay 消息使用一个原子值：

```text
CoCoEventPacket<TEvent> = CoCoActorEventEnvelope + immutable typed payload
```

有 Event declaration 的 GraphRuntimeInstance 独占一个 EventInbox，而且全部 Event
必须属于同一 EventDomain；没有 declaration 时不创建 Inbox 或 Router。每个
EventDomain 惰性创建一个 internal Router，并与 ClockDomain 分离。本 Actor local
input 直接进入 Host Gateway；跨 Actor Targeted 消息按当前 GraphInstanceId 路由。
Broadcast 只投递给显式声明对应 Adapter 的 Actor，默认不回送 Source Actor。

当一个 Event 由多个已声明 Adapter 投影时，执行顺序严格采用 compiled manifest 保留的
Asset declaration-list 顺序。项目 binding Provider 提供精确实现，但不能重排该顺序。

Inbox 只有在绑定到存活且 Bindings 已冻结的 Intent Runtime 后才能进入 Running。Typed
Lane 必须与该 Runtime 去重后的 Adapter Manifest 精确匹配，包括 EventDomain、
EventType 和 Payload Type；每条 Lane 的 Capacity 不得超过对应 Adapter 声明的最小
Projection Capacity。每个 GraphRuntimeInstance 独占自己的 Reducer 实例，Actor 之间
绝不共享 Reducer 可变状态。

Inbox 绑定、Start、Tick Seal、Suspend 或 Resume 时 Intent Runtime 必须处于 idle，
避免 Collection 开始后重新 Seal 的消息进入当前 IntentFrame。Freeze 会先为 Reducer 状态建立
Checkpoint；Reduction 失败时 Reducer 与部分 Frame 一起回滚。用户 Callback 中请求的
Inbox Stop/Dispose 会延迟到 Callback 退出后执行并终止当前 Collection，失效的 sealed
Batch 不能继续产生 Contribution。

Inbox 使用固定容量、预分配双缓冲。Router Callback 只能校验、去重、路由和入队。
Step 开始时封存本 Tick 可见批次；Step 期间到达的消息最早在下一次被接受的 Tick
可见。一条消息最多投影到一个 IntentFrame；需要持续存在的含义必须提交成
ContextFrame State。

- Suspend 保留 Router 注册，并只允许在固定容量内继续积压。
- Begin Temporal Preview 立即清除 queue、sealed batch 与 dedup 状态；Preview
  期间到达的 gameplay message 直接 drop 并累计，不排队到恢复后。
- Cancel 保持旧 Epoch，但不复活已清除 backlog；Confirm 后只接受属于新 Epoch
  的新消息。
- 普通 Suspend/Resume 不是 Rewind，仍保留合法固定容量 backlog。
- Reliable 溢出在安全边界锁存 Host Fault；Fault 拒绝新 gameplay input 与普通 Resume。
  Unreliable 溢出拒绝最新消息并增加诊断计数。
- Stop/Dispose 清空队列和去重状态。
- 新一轮 Intent Collection、Cancel、Timeline Reset 与 Dispose 都会立即使上一份可读
  IntentFrame 失效。Source、Adapter 或 Reducer 抛异常时先 Cancel Collection 再继续
  抛出；用户 Callback 不得重入 Collection/Freeze 操作。
- Cancel 会回滚 Inbox Projection Claim，并禁止同一个 Tick 再次 Begin。
- 绑定的 Intent Runtime Dispose 时，Running Inbox 会停止并清空；Created Inbox 只解除
  绑定，以便挂接替代 Runtime。
- 音效、VFX、日志等纯表现事件可以继续使用普通 EventBus，不进入 gameplay Inbox。

Host 在所有 Binding 与 Operator coverage 检查成功后才最后注册 Router；
Stop/Dispose 首先注销。Domain 最后一个 Host 离开时释放 internal CoCoEventAgent
subscription。EventOutbox 条目在复合提交前只是预分配候选；提交时才为同一
GraphInstance/Epoch 分配连续 EventSequence 区间。发布保留 Host Operator 顺序与每个
Operator 的 append 顺序，且 Callback 只能观察完整的新权威。

合法的 Runtime 实例生命周期边为 `Created -> Running`、`Running <-> Suspended`、
`Running/Suspended -> Stopped` 与 `Created/Stopped -> Disposed`。`Created` 不可 Stop，
Host 的公开 `TryDispose` 只接受 `Created` 或 `Stopped`；Runtime `Dispose()` 与 Unity
销毁则是不可拒绝的清理，会先在内部把存活实例经由 `Stopped` 拆除且不伪造 Exit。
停止后的 Host 再次 Start 会创建全新 Runtime 实例。生命周期调用不可重入启动过程或
正在推进的 Tick；这两个阶段发生 Unity 销毁时，会阻止实例发布或在权威状态交换前取消
未决候选。

## Commit 与时间边界

- 每个被接受的 `CoCoTickFrame` 都使用有限正 Delta。
- Actor TimeScale 同样必须有限且大于零。Pause/Suspend 不产生 Tick，不采样 Intent，
  也不产生新 Frame。
- Unity Update/FixedUpdate 每帧最多接受一个 CoCoTick；Manual 每次调用都是独立 Tick，
  不使用 accumulator 或 catch-up。
- Rewind 不使用负 Delta。Preview 只选择并投射历史 candidate；Confirm 在新
  TimelineEpoch 只 Restore 一次，然后继续正 Delta 正向 Tick。
- 健康 Suspended Host 的内部 Debug Step 接受一个有限正 Delta，在 Update、FixedUpdate
  或 Manual Driver 下执行恰好一个普通 Tick。它可以提交 Context、history、Trace 与
  Outbox Event，且只在成功时回到 Suspended；它不是 Rewind 或中性 Preview。
- StateGraph 只读取当前 IntentFrame 和 Previous ContextFrame，不能观察本 Tick
  执行中产生的 Outcome。
- 首 Tick 通过 `CoCoContextFrameReadView` 读取 Layout 默认值，不伪造 Tick 0 或
  Revision 0 Frame；首次成功提交为 Revision 1。
- ContextFrame Commit 是唯一已提交的 gameplay 逻辑权威边界。
- Commit 失败、Cancel、Preview 或 Restore 时，零 Outbox Event、零最终 EventSequence
  消耗、零跨 Actor 副作用。
- 真实 Operator Callback 修改 Unity 对象后再失败时，框架不伪造世界回滚；旧
  Context 继续是权威，Host Fault，`RequiresWorldCorrection` 保持为真，
  直到显式 Correction 成功重新投射 current authority，或新 Host 实例启动。

Outcome 只能写入 Pre3 Manifest 中已声明、非 Derived、Operator-owned 且唯一 owner
的 Context Slot。Trace 按 compiled order 记录通过条件的 Transition Candidate，并把
Winner 另记一条。不可变 Frame Reference 只含 identity、精确 Layout metadata、Revision
与是否存在 committed Frame，不 Retain Frame Handle；Trace 不保存 payload、Unity Object、
mutable Frame 或 diagnostic string。Trace 是 opt-in，capacity 默认为 0，Running 时不可
改变。

Runtime Debugger 的内部不可变 Snapshot 与 Trace 分离。它回答当前已提交的 Host/
lifecycle/fault、Context revision、Tick/Clock/Epoch、各 Layer active path 与 committed
Transition，不暴露 candidate Tick、payload、Inbox、Envelope、retained Context Handle
或反射私有字段；失败或取消的 Tick 不能替换 committed Snapshot 权威。

## Content、Object Pool 与 Map Region Fidelity

Content 通过显式 Scope 与引用类型 `ContentLease` 管理已加载的 Asset、Prefab Source
和 Additive Scene。Pooling 建立在这条边界之上：一个已 Prepare 的 Pool Entry 在拥有
任何物理 GameObject 实例期间只保留一份 Prefab Source Lease；readonly、
generation-safe 的 `PooledHandle` 只授予 consumer 一代 rental 权威。

Prepare 与 Prewarm 是异步操作，Entry Ready 后 Rent 为同步操作。`PrewarmCount`
只是预备目标，`MaxRetained` 只限制 idle retention；burst 可以超过二者，Return
overflow 会销毁实例。不存在 hard active cap、自动 Trim、LRU 或隐藏的
Addressables 路径。

可选 Temporal sidecar 会在单个 Host 的历史仍能把实体投射为 present 时 quarantine
同一物理实例。它只保存纯 identity/presence value，不是 multi-Actor 或 whole-world
rollback。现有 UI 与 Enemy consumer 不会被自动迁移。

Map 现在把 Region 定义为逻辑 Fidelity 单元，把 Chunk 定义为 Region 所拥有的优化
分区。schema-v1 `CoCoRegionProfile` 具有稳定资产身份、固定的
`off / represented / background / enterable / full` Tier ID，以及可编辑的
Participant-by-Tier Enabled/Mode/configuration 矩阵；并通过显式、AOT-safe Catalog
接纳命名空间化自定义 Capability 与 Participant。Demand Owner 持有
`RegionDemandLease`，Coverage 可以是 `All` 或显式 Chunk 集合；Region-global Node
合并全部 live demand，每个 Chunk 只合并覆盖自己的 demand。

Transition 会复用未改变的 `(Region, optional Chunk, participant slot)` Node，只为
fingerprint 变化的 Node Prepare Candidate。Residency、Services、Simulation、
Presentation 按确定顺序提交并逆序清理。Required 失败保持事务原子性，Optional
失败显示为 `OptionalDegraded`，Commit Fault 与 Blocked Cleanup 都保持显式。
由 Capability 触发的跨 Region Dependency Rule 会持有独立 Target Lease，并在 Source
Transition 提交前等待 Target Ready。

内置 Map Participant 的 Additive Scene Lease 只能由 Content 持有。受管理 Chunk
Scene cold-start 时只有一个 metadata-only Anchor Root，其余 managed Root 初始
inactive。已提交 Map Node 可以拥有 Pool Scope；Temporal decorator 链固定为
`Map -> optional Pool -> project restore binding`，Preview 不会加载 Scene、Prepare
Pool 或提交 Fidelity Tier。Barrier 只把每个 Region 的最终 Demand Resolution 排队到
`LateUpdate`，启动前会拒绝 Decorator Cycle；Disable、Destroy 与显式 Shutdown
收束到同一个事务式终止任务。Editor Monitor 只读取内部不可变 Ownership Snapshot，
不会暴露原始 Content 或 Pool 权威。完整契约以及从 `MapResourceManager`/
`MapStreamTrigger` 迁移的 breaking 说明见
[Map Region Fidelity](Docs/Module-Map.md)。

## 仓库与包边界

旧 0.3.9 Mono 状态运行时与输入 Bridge 已在 Pre15 线移除，StateGraph/StateFlow
是唯一状态运行时。少量过渡期 Core 设施（EventBus、日志、服务）与过渡期模块
（Camera、Persistence、UI）保留，均非 0.4 契约或迁移层。现有 0.3.9 项目应继续
锁定 0.3.9 Revision。

```text
Runtime/Core/Contracts   与引擎隔离的 0.4 契约
Runtime/Core/StateFlow   与引擎隔离的 0.4 Frame、Section、Intent 与 Mailbox 契约
Runtime/Core/StateGraph  与引擎隔离的 Compiler、Runtime、Clock 与 staged Tick
Runtime/Core/StateGraphAuthoring  Unity StateGraph Asset、snapshot 与 compilation cache
Runtime/StateGraphHost   Unity Host 与 internal Gateway/Router 集成
Runtime/Content          Unity-facing Content 获取、所有权、Direct 后端与 diagnostics
Runtime/Content/Addressables  可选条件编译的 Addressables 后端
Runtime/Pooling          Content-backed GameObject 实例所有权与 diagnostics
Runtime/Pooling/Temporal 可选的 Host-scoped pooled Temporal entity retention
Runtime/Animation        与引擎隔离的 Animation Operation 与 feedback 契约
Runtime/Modules/Animation  Animator Controller Operator、SMB bridge 与可选 adapter
Runtime/Modules/Map      事务式 Region fidelity、demand、participant 与 adapter
Runtime/Core/*.cs        过渡期 Core 设施（EventBus/日志/服务）
Samples~/Gameplay        可选 Character、Enemy、Item 实现的源码交接区
Runtime/Modules          其他过渡期表现与服务模块
Editor/StateGraph        受限图创作与 diagnostics
Editor/StateGraphHost    Host Inspector 与 committed runtime debugger
Editor/Content           Content Reference 创作与 Runtime Ownership Monitor
Editor/Pooling           Pool Host 创作与 Runtime Ownership Monitor
Editor/Modules/Map       Region Profile/Binding 创作、validation 与 Runtime Monitor
Editor/Modules/Animation Controller mapping validation 与 SMB 创作工具
Editor                   dependency/setup 与过渡期模块工具
Tests                    契约、架构与过渡期回归测试
```

Core Contract 与 State Flow 表面不得依赖 Gameplay、表现模块、Editor、项目代码、
Animator、Playable、特定网络框架或持久化后端。StateLogic/Layer API 不得暴露
EventBus、CoCoEventAgent、EventEnvelope、EventRouter 或 EventInbox 依赖。

对于注册进 StateGraph 的作者代码，Editor Analyze 与 Player build preflight 会遍历
完整的已解析程序集依赖闭包。所有可达自定义程序集都必须是与引擎隔离的 asmdef；
命中禁止依赖或无法证明安全的依赖时，build-time gate 会失败关闭。

Pre1 仍是 identity、time、lifecycle、diagnostic 与纯 StateLogic 契约的历史发布。
当 Pre1 的候选 Context Flow 与本文冲突时，以 Pre2 State Flow 为权威。

## 后续 0.4 工作

- **Pooling 扩展**：generic non-GameObject pool、hard active/total cap、
  自动 Trim/LRU、hot profile mutation，以及 world/durable rollback。
- **Map 扩展**：项目自有 distance/adjacency 策略、自动 fidelity budget/downgrade、
  Map-state replay 与 whole-world rollback。
- **Animation 扩展**：待 bounded replay gate 通过后再实现 Animator 精确重放；
  generic Playable、内置 IK 与直接写世界 Transform 的 Root Motion 不属于 Pre11。
- **Pre12**：最终 UI navigation、focus、transition 与 authoring 契约。
- **Pre13**：Persistence V2、Durable Projection、Migration、Container 与世界事实。
- **Pre16**：production gameplay State、替代 Sample 与完整跨模块性能和
  生命周期认证。
- **Pre17**：不改变功能边界的最终视觉与 XML 文档 polish。

## 依赖

Addressables 仍不属于包体硬依赖。只使用 Direct Content/Pooling/Map 的项目不需要
安装 Addressables；需要可选后端时，从 `CoCoFlow/Setup/Setup Assistant` 显式安装。
Addressable Map Binding 还必须由项目实现 `IRegionAddressableSceneResolver` 并接到
`CoCoMapHost`，同时为 Editor 注册等价 Resolver Provider；只安装 Content Backend
不会自动定义 Address 到唯一 Scene 的映射。

| Package | Version | 当前使用者 |
|---|---:|---|
| Input System | 1.18.0 | Input 模块 |
| Localization | 1.5.9 | Localization Core 与可选 UI V2 提示扩展 |
| Newtonsoft Json | 3.2.2 | Persistence 过渡期模块 |
| Cinemachine | 3.1.6 | Camera 过渡期模块 |
| AI Navigation | 2.0.0 | 无直接消费者（保留依赖） |
| Mathematics | 1.3.3 | Samples~ Gameplay（Enemy）程序集 |
| Splines | 2.6.0 | Samples~ Gameplay（Enemy） |

可选依赖：

| Package | 推荐版本 | 当前使用者 |
|---|---:|---|
| Addressables | `[2.9.1,3.0.0)` | 仅 `CoCoFlow.Runtime.Content.Addressables` |
| DOTween | 项目自有 | 可选 Animation modulation 与 UI |
| UniTask | `2.5.11` Git revision | 可选 Animation playback waiter 与异步模块 |

UniTask 仍由 Setup Assistant 管理。启用 `COCOFLOW_UNITASK_SUPPORT` 后编译
Content、Pooling、Pooling Temporal、UI、Map 与可选 Animation waiter。
可选 Animation modulation adapter 使用 `COCOFLOW_DOTWEEN_SUPPORT`，并且只推进
自身拥有的 tween；Addressables adapter 还需要对应 package version define。
`UIWidgetLocalizedText` 与 `InputPromptPresenter` 沿用 UI V2 契约，只有
`COCOFLOW_UNITASK_SUPPORT`、`COCOFLOW_DOTWEEN_SUPPORT` 和
`UNITASK_DOTWEEN_SUPPORT` 同时启用时才编译；Localization Core 与 Input Core
不依赖这些可选集成。Setup Assistant 会分开报告这些 Surface，不安装独立 Pool 或
Animation 包。

## 安装与验证

可以通过 Unity Package Manager 使用明确的 Git Revision 安装，也可以把包放入
Unity 项目的 `Packages/` 目录。不要把持续变化的开发分支当作生产依赖。

本仓库是 UPM Package，不是完整 Unity Project。发布门禁必须在干净的 Unity 6
宿主工程中完成包导入、Core/State Flow/StateGraph Runtime EditMode 测试、Host
PlayMode 测试、相关 IL2CPP/High Stripping 检查与 Unity Package Validation Suite。
`CoCoFlow/Setup/Setup Assistant` 仍只负责依赖
与 Support Define，不安装项目内容。

Pre15 的验证证据记录在 Changelog 中。Package Validation Suite 与平台 Player
Build 结果必须和定向聚焦测试分开报告。

## 文档

- [State Flow / Event Boundary](Docs/ContextNetworkBoundary.md)
- [StateGraph Asset 与 Compiler](Docs/StateGraphCompiler.md)
- [StateGraph Runtime 与 Host](Docs/StateGraphRuntime.md)
- [StateGraph Editor 与 Runtime Debugger](Docs/StateGraphEditor.md)
- [Temporal Rewind](Docs/TemporalRewind.md)
- [Content 获取与所有权](Docs/ContentOwnership.md)
- [Object Pooling 与实例所有权](Docs/ObjectPooling.md)
- [Module: UI](Docs/Module-UI.md)
- [Module: Input](Docs/Module-Input.md)
- [Module: Localization](Docs/Module-Localization.md)
- [Project Scaffold](Docs/ProjectScaffold.md)
- [Map Region Fidelity](Docs/Module-Map.md)
- [Module: Animation](Docs/Module-Animation.md)
- [Module: Camera](Docs/Module-Camera.md)
- [Module: Persistence](Docs/Module-Persistence.md)
- [Changelog](CHANGELOG.md)

除非明确标注为 0.4 权威契约，模块文档描述的都是过渡期实现；Animation 模块文档
描述的是 Pre11 的 0.4 表面。

## 版本约定

- 集成分支：`dev/0.4.0`
- 工作分支：`pre/NN-topic`
- UPM 候选版本：`0.4.0-rc.N`
- 0.3.9 是历史 Runtime 线；0.4 不承诺自动迁移或双 Runtime。

## License

MIT

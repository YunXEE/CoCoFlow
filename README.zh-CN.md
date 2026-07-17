# CoCoFlow

[English](README.md) | [简体中文](README.zh-CN.md)

> **版本**：0.4.0-pre.4 · **Unity**：6000+
>
> Pre4 加入每 Actor 独占的 StateGraph Runtime、唯一 Unity Host、确定性生命周期与
> Transition 计算、staged OperationFrame、Clock、Inbox 和内部事件路由。Operator
> 执行与 Context commit 归 Pre5。

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
| `ContextFrame` | 指向单个 Actor 在 Tick 边界完整已提交逻辑状态的 generation-scoped 只读 Handle；捕获的 Storage Generation 存活期间，它是 Restore 以及后续 Temporal/Durable 投影的权威输入。 |
| `EventInbox` | 一个 GraphRuntimeInstance 的待处理跨 Object gameplay 输入，不是事实存储。 |
| `EventOutbox` | Operator 执行期间产生的跨 Object 输出候选，只有 ContextFrame Commit 成功后才发布。 |
| `EventAgent` | 只负责 EventBus 订阅生命周期；不路由、不排队、不拥有也不持久化消息。 |

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

Intent Manifest 同时携带 Graph 的 canonical Event-to-Intent 静态 declaration。Pre3
校验 Event Domain、Payload Type、Provided Intent Type、contribution capacity 下界，
以及每 Graph 只能有一个 EventDomain。Pre4 实例化声明的 Adapter，并在 runtime
binding coverage 不是精确匹配时拒绝 Host Start。

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

## StateGraph Runtime 与 Host

每个 Actor 只需要一个组件和一个 Asset：

```text
Actor GameObject
├─ CoCoStateGraphHost        必需
│  └─ StateGraphAsset       必需
└─ Operator scripts          可选；Pre5 增加 Host 显式引用列表
```

Runtime、Clock、Inbox、Router、Logic、Condition 与 Memory 都不是组件。Host 不扫描
旧 Controller、Context Provider、子物体或场景。多个 Host 可以共享不可变 compiled
graph，但各自独占 StateLogic/Condition 实例、双 Memory Bank、active leaf、Clock、
Inbox、staged Tick 与锁存 Fault。

`Start` 只确定初始叶子，不运行 callback。每 Layer 首 Tick 的新路径先 Parent→Child
运行可选 Enter，再 Root→Leaf 运行必选 Update。叶子 Update 可以请求零到多个预声明
出边；全部 Update 结束后，Runtime 统一计算 window、Condition，并选唯一最高
Priority。Transition 两端必须是叶子，同一源叶子的出边 Priority 必须唯一。

Transition Tick 始终保留源路径为有效路径，因此可以同 Tick Update + Exit；成功提交的
目标在下一 Tick Enter + Update。Enter 与 Exit 是独立可选阶段，Update 每 Tick 必跑，
每 Layer 每 Tick 最多一个 Winner。系统没有 Completion 状态，ActionProgress 到 `1`
也不会自动退出。

Layer 与 path depth 给 Operation 写入固定等级：高 Layer 覆盖低 Layer，子 State 覆盖
父 State。Continuous Section 按字段合成；Discrete Section 只为最终赢家消耗一次
Sequence。Operation Finalize 只生成单次使用的 staged Tick，不改变权威状态。Pre5
必须先成功提交 Context，新的 Path、Memory、Clock、Sequence 与 EventOutbox 才可见。

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
Manifest，Pre4 产生 finalized staged frame；Pre5 负责正式 Operator Runtime，并且只有
对应 Context commit 成功后才接受该 Frame。

## ContextFrame 与 Restore

`ContextFrame` 是单个 Actor 的完整已提交逻辑状态，不是世界快照，也不是 Unity
场景 Object Graph。固定的 StateBlock/Slot Layout 必须包含从该 Commit Boundary
继续运行所需的 Graph、Activation、Transition Progress、Actor 数值和可控 Operator
进度，或包含能够确定重建它们的数据。

Descriptor 元数据包含两个独立维度：

- Projection Flags 分别标记 **Temporal**、**Durable**，同一 Slot 可以同时拥有两项。
- Restore Policy 独立选择 **Stored**、**ResetToDefault** 或 **Derived**。
- Derived Slot 必须声明依赖，在 Finalize 中由 Stored/Default 输入确定重建；它不可直接
  写入，也不形成第二个权威值。
- 投影包含 Derived Slot 时，必须同时包含重建它所需的全部传递 Stored/Derived 依赖；
  ResetToDefault 依赖可以确定重建，因此无需进入投影。

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
EventAgent 订阅、未发布 Event、执行一半的 Operator、其他 Actor，或已经交付给
其他 Actor 的后果。

Restore 必须保持 Source 的 Timeline 与 ClockDomain、推进 ExecutionSequence，并建立
同时新于 Source Epoch 与 Actor 当前权威 Epoch 的 TimelineEpoch。Pre2 只验证
Descriptor 和 internal、same-session、exact-layout Codec Spike；该 Spike 不是跨会话
存档格式或稳定 Wire Identity。Pre6 负责 Temporal 存储和倒放，Pre13 负责
Durable Save Document、StableEntityId 到 Runtime 的解析、Migration、Container、世界
事实和生成实体重建。

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
- Rewind/Restore 拒绝新的 gameplay 消息并记录诊断。
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

Host 在所有 binding 检查成功后才最后注册 Router；Stop/Dispose 首先注销。Domain
最后一个 Host 离开时释放 internal EventAgent subscription。Pre4 只开放入站能力；
Pre5 Context commit 成功前，Host outbound seam 不得发布 EventOutbox。

## Commit 与时间边界

- 每个被接受的 `CoCoTickFrame` 都使用有限正 Delta。
- Actor TimeScale 同样必须有限且大于零。Pause/Suspend 不产生 Tick，不采样 Intent，
  也不产生新 Frame。
- Unity Update/FixedUpdate 每帧最多接受一个 CoCoTick；Manual 每次调用都是独立 Tick，
  不使用 accumulator 或 catch-up。
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
Runtime/Core/StateGraph  与引擎隔离的 Compiler、Runtime、Clock 与 staged Tick
Runtime/Core/StateGraphAuthoring  Unity StateGraph Asset、snapshot 与 compilation cache
Runtime/StateGraphHost   Unity Host 与 internal Gateway/Router 集成
Runtime/Core/*.cs        过渡期 0.3.9 Runtime 与后续 Pre 集成
Runtime/Gameplay         过渡期 gameplay 实现
Runtime/Modules          过渡期表现与服务模块
Editor/StateGraph        Editor-only 身份操作与 diagnostic navigation
Editor                   dependency/setup 与过渡期模块工具
Tests                    契约、架构与过渡期回归测试
```

Core Contract 与 State Flow 表面不得依赖 Gameplay、表现模块、Editor、项目代码、
Animator、Playable、特定网络框架或持久化后端。StateLogic/Layer API 不得暴露
EventBus、EventAgent、EventEnvelope、EventRouter 或 EventInbox 依赖。

对于注册进 StateGraph 的作者代码，Editor Analyze 与 Player build preflight 会遍历
完整的已解析程序集依赖闭包。所有可达自定义程序集都必须是与引擎隔离的 asmdef；
命中禁止依赖或无法证明安全的依赖时，build-time gate 会失败关闭。

Pre1 仍是 identity、time、lifecycle、diagnostic 与纯 StateLogic 契约的历史发布。
当 Pre1 的候选 Context Flow 与本文冲突时，以 Pre2 State Flow 为权威。

## 后续 0.4 工作

- **Pre5**：Host 显式 Operator 引用、Operator Binding/执行/Claim、Outcome 聚合、
  ContextFrame Commit 与 committed EventOutbox Publish。
- **Pre6**：Temporal Ring Buffer、Restore、Rewind 与新 TimelineEpoch。
- **Pre11**：Playable Animation V2、Animation Operator、Combo Timing 与 Root Motion
  所有权。
- **Pre13**：Persistence V2、Durable Projection、Migration、Container 与世界事实。
- **Pre15/Pre16**：替代 Samples、Golden Path、技术文档以及完整跨模块性能/生命周期
  认证。

## 依赖

Pre4 不调整依赖集合，因为过渡期 0.3.9 模块仍需要这些依赖参与编译。

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
宿主工程中完成包导入、Core/State Flow/StateGraph Runtime EditMode 测试、Host
PlayMode 测试、相关 IL2CPP/High Stripping 检查与 Unity Package Validation Suite。
`CoCoFlow/Setup/Setup Assistant` 仍只负责依赖
与 Support Define，不安装项目内容。

## 文档

- [State Flow / Network Boundary](Docs/ContextNetworkBoundary.md)
- [StateGraph Asset 与 Compiler](Docs/StateGraphCompiler.md)
- [StateGraph Runtime 与 Host](Docs/StateGraphRuntime.md)
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

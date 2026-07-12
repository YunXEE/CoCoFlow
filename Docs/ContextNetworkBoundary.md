# CoCoFlow Context / Network Boundary

> Contract status: `0.4.0-pre.1` · Updated 2026-07-13
>
> This document defines the 0.4 dependency and frame boundary. Context V2,
> StateGraph runtime, schedulers, Operations, network adapters, and snapshot
> implementations arrive in later Pres.

## 目标

CoCoFlow 0.4 把 gameplay 计算收束成一个单向循环：Source 产出事实，Context 在
Tick 边界冻结，独立 Layer 只读解释冻结帧，Operation 执行副作用并为下一帧写回。
StateGraph 不拥有输入、Unity 生命周期、Animator、网络或持久化实现。

## 权威数据流

```text
Manually Bound Sources
  -> Context Composition
  -> Frozen Context Frame N
  -> StateGraph
       -> Independent Layer A (Layered HFSM)
       -> Independent Layer B (Layered HFSM)
       -> Independent Layer ...
  -> Declared Operation Entry Points
  -> Operation Execution / Write-back
  -> Context Composition
  -> Frozen Context Frame N + 1
```

网络、回放和 Timeline 不创建第二条执行链。它们和输入、AI 一样，只能作为
Source 或 host driver 接入这条循环。

## 1. Source Boundary

Source 是 Frozen Context Frame 形成前的事实来源，例如输入、AI 感知、网络快照、
Timeline 指令或项目业务组件。

冻结规则：

- Source 必须由用户显式绑定，不允许 runtime 动态接入或全局扫描。
- Source 必须来自 StateGraph 宿主所在对象或其父子层级上的脚本。
- 每个 Source 显式声明 priority；相同 priority 使用绑定列表的声明顺序。
- Source 只覆盖自己声明的 section/field，其余数据自动透传。
- Context 通过组合承接 Operation 所需的只读接口，不通过继承某个具体 Source
  类型来获得能力。
- Source 不直接切换 State，不持有 Layer，不调用 StateLogic。

具体 Source API 和 Context V2 builder 不在 Pre1 实现；上述绑定与合并语义已经
冻结。

## 2. Frozen Context Frame

StateGraph 的每次 Step 只接收一份冻结帧。冻结帧在整个 Step 内不可变：

- StateLogic 只能读取，不能回写 Context。
- 同一 Tick 内 Operation 产生的结果不会被后续 Layer 偷看到。
- Context 没有更新时，StateGraph 仍读取上一份合法冻结帧并照常解释。
- 没有任何合法 Context 时，StateGraph 拒绝启动；这属于启动诊断，不是运行中
  的 StateLogic 异常。
- Frozen Frame 是数据边界，不是 Unity `Update` 的别名。

这一规则保证 Layer 顺序只影响 State/Operation 调度顺序，不会制造同 Tick 的
隐式数据竞争。

## 3. Independent Layers

StateGraph 驱动一组相互正交的 Layer。每个 Layer：

- 独立维护一个 HFSM；
- 在任意时刻至多拥有一条 active path；
- active path 从 Root State 延伸到没有 active child State 的 Leaf State；
- 在同一个生命周期阶段内按父到子处理 active path；退出/撤销阶段按确定性的
  反向顺序收束；
- 具有唯一显式 priority。

StateGraph 按 priority 从高到低完成一个 Layer 的当前阶段，再处理下一个 Layer。
不同 Layer 不是继承关系，也不共享可变 State 实例。

Layer 之间禁止通信、直接调用或提交针对另一个 Layer 的 transition。
多个 Layer 的协调只能通过读取同一份 Frozen Context 完成，与 Animator
Layer 读取同一组 Parameters 的原则一致。GraphAsset 中的跨 Layer 引用是
编译/验证错误，不是 Runtime 调度功能。

## 4. StateLogic Boundary

StateLogic 是解释冻结 Context 的纯 C# 逻辑：

- 不继承 `MonoBehaviour` 或 `ScriptableObject`；
- 不持有 `GameObject`、`Component`、Animator 或 Playable 对象；
- 不访问 Unity callback；
- 不修改 Context；
- 只通过已声明的 Operation 类别和准入口表达副作用需求。

StateLogic 声明“需要什么”，Context 通过组合实现对应只读能力，Operation registry
提供允许调用的执行入口。StateLogic 不绑定具体项目组件，也不负责运行时搜索。

Section Requirement 只接受非根、只有 getter 的 Section interface。getter 的事实
只能是 immutable string 或递归不含托管引用的 value，不允许 ref return、callback、
collection、Unity Object 或其他 mutable reference。StateLogic 每次读取都必须携带
与目标接口匹配的 Requirement；具体 Context 实现、Source、Writer 和 mutable root
不属于读取准入口。

缺少声明为必需的 Context 能力或 Operation 绑定时，Graph instance 必须在启动前
给出结构化诊断并拒绝启动，不能等到某个 State 执行后再抛空引用。

## 5. Operation Boundary

Operation 是允许接触世界和产生副作用的层，例如 Locomotion、Navigation、
Lifecycle、Animation presentation 或项目自定义 gameplay operation。

Operation 可以：

- 读取当前 Frozen Context Frame；
- 接收 StateLogic 通过准入口提交的声明式请求；
- 修改 Unity 对象或调用项目服务；
- 把结果写入自己拥有的 Source/section，供下一次 Context composition 使用。

Operation 不可以：

- 回调正在执行的 StateLogic；
- 在当前 Step 内修改 Frozen Context Frame；
- 绕过 StateGraph 直接改写某个 Layer 的 active path；
- 依赖未声明的全局查找结果。

Pre1 的命令准入口必须携带已声明的 Port Requirement，并按值接收实现
`ICoCoOperationCommand` 的 unmanaged struct。这样 Command 本身不能携带 callback
或共享引用结果，`Submit` 也没有同步返回值；世界结果只能通过后续 Frozen Context
Frame 重新进入 StateLogic。

Operation 的 Claim/Ownership、冲突仲裁、执行阶段和 write-back API 属于后续 Pre，
Pre1 只冻结这条依赖方向。

## 6. Tick、Suspend 与 Unity Host

CoCo Tick 与 Unity callback 可不一一对应。Host 可以由 Variable、Fixed 或 Manual
driver 推进 StateGraph，从而支持独立频率、变速、测试和回放。

- `CoCoTickFrame` 的 delta 必须是有限正数；零或负 delta 都非法。
- 倒退不能通过负 delta 实现；必须从 Snapshot 恢复后再正向组织 Tick。
- Suspend 期间 host 不提交 Tick，因此没有 StateLogic、Operation 或采样。
- 对 StateGraph 而言，`GameObject.SetActive(false)` 与手动 Suspend 都表现为 host
  停止供给 Tick；它们都不等于终态 `Disposed`。
- `Disposed` 必须走显式、不可逆的 runtime lifecycle 通道。

调度器和 Unity adapter 的具体恢复/重建策略属于 Runtime Pre。

## 7. Network Adapter Boundary

Core 不依赖任何具体网络框架。网络实现只能放在项目 adapter：

- 把 remote input、authority facts 或 snapshot 数据转换为已绑定 Source；
- 在权威端使用与本地玩法相同的正向 Tick 规则；
- 使用稳定的 graph/state/instance/activation/timeline identity 描述运行对象；
- 在完整 Tick 边界采集或应用 snapshot，不读取中间 Layer 状态；
- remote correction 在下一次 composition 生效，不回调当前 StateLogic；
- 本地 Camera、Animator 和其他 presentation 不作为权威 gameplay snapshot。

网络 adapter 不为每个 gameplay State 派生网络 State，也不能直接驱动某个 Layer
的 State 实例。离散消息若会改变 gameplay，必须先落到带 Tick/Sequence/Epoch
的 Transient Context Fact，再由 State 在后续 Frozen Frame 中读取事实并产生
自身的同 Layer outgoing Transition Candidate。

## 8. Persistence 与 Rewind Boundary

Persistence 和 temporal rewind 都消费稳定边界：

- Persistence 保存 durable Context facts，不保存一帧 Intent 或 Unity object 引用。
- Runtime snapshot 在完整 Tick 结束后采集 Graph/Context/Operation 所需状态。
- Rewind 从选定 Snapshot 恢复新的 timeline epoch，然后以有限正 delta
  继续正向 Step。
- Snapshot schema、恢复顺序和 temporal ownership 属于后续 Pre。

## Pre1 与旧 CCS Runtime

仓库中的 0.3.9 CCS Runtime 暂时保留到 Pre4，只用于维持过渡期编译和历史回归。
它的 `MonoBehaviour` State、Unity `Update/FixedUpdate` 驱动、可变 Context 访问和
现有 Layer order 都不是本文件定义的 0.4 契约。

0.3.9 项目应继续锁定旧 revision。0.4 不提供双 Runtime 或自动迁移层。

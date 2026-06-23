# CoCoFlow Context / Network Boundary

编写日期：2026-06-19

## 目标

CoCoFlow 的 gameplay 边界以 Context、Intent、State、Event、Persistence Identity 为核心。网络层不理解具体状态类、Animator、IK 或本地 Controller；网络 adapter 只搬运 Intent、Context snapshot 和 EventEnvelope。

## 信息流

```text
Input / AI / Timeline / Network / Replay
  -> CoCoStateController
  -> Context.Intent / Context facts
  -> CoCoStateBase lifecycle(context)
  -> Arms: Motor / Combat / Interaction
  -> Context facts + CoCoEventBus events
  -> Presenter / Network Adapter / Persistence
```

关键规则：

- `InputReader` 只表示输入端，不绑定 Character、Vehicle、UI 或具体 gameplay。
- Character、Vehicle、Item 等对象在各自 gameplay 层把输入或交互请求翻译成自己的 Intent。
- State 读取生命周期参数里的 `ICoCoContext`，不再为常规业务到处查找具体 Controller。
- 常规状态切换由 `CoCoStateController` 或其评估逻辑根据 Context 推进；旧状态直接 `ChangeState` 只作为兼容路径保留。
- Event 表示离散事实，重要 Event 必须对应可恢复的 Context 状态变化。

## Core Contracts

Core 提供以下边界：

- `ICoCoContext`：Context 标记接口。
- `ICoCoContextProvider<TContext>`：组件提供当前 Context。
- `ICoCoIntent`：Intent 标记接口。
- `ICoCoIntentSource<TIntent>`：输入、AI、网络、回放等意图来源。
- `ICoCoStableEntityIdProvider`：存档系统提供稳定实体 ID。
- `CoCoLifecycleContext`：实体生命周期事实。
- `CoCoEntityIdentity`：稳定 ID、运行时 ID、owner、类型和 prefab key。
- `CoCoEntityContext`：通用实体 Context，包含 identity、lifecycle、semantic/action state 和 event sequence。
- `CoCoEventEnvelope`：网络 adapter 可搬运的事件信封。

Core 不依赖 Fusion，也不依赖具体 gameplay 模块。

## Character Boundary

`CharacterContext : CoCoEntityContext` 是玩家、敌人、友方 NPC、Timeline 角色和网络代理角色的共同主线。

包含：

- `CharacterIntent`：Move、Look、Jump、Attack、Interact、UseSkill、目标点和目标实体。
- `CharacterMotionContext`：位置、旋转、速度、grounded。
- `CharacterResourceContext`：生命值和死亡判断。
- `CharacterPerceptionContext`：当前目标、目标可见性、最后已知位置。
- `CharacterNavigationContext`：`CharacterContext.Navigation` 下的导航和移动请求事实，记录控制权、目标点、期望速度、路线进度和 warp 请求。

玩家不是单独的 Core 类型。玩家输入通过 `CharacterInputDriver` 把 `CoCoInputIntent` 写入 `CharacterContext.Intent`。敌人行为树、Sensor、Spline 巡逻、Timeline、Network adapter 后续也应写同一个 `CharacterContext`。`CharacterInputDriver` 是 Character 的意图写入层，不持有状态切换权威。

`CharacterContextProvider` 是本地默认 `CharacterContext` 宿主。它可以显式接入多个 `ICharacterContextSource`，在状态 tick 前按 priority 写入同一份 Context。业务场景需要额外事实时，继承 `CharacterContext` 并通过 `CharacterContextProvider<TContext>` 提供派生 Context，例如特殊动画参数或业务标记；消费侧仍通过 `ICoCoContextProvider<CharacterContext>` 接入。

`CharacterLifeCycle` 是兼容门面和生命周期写入组件：旧代码仍可调用 `TakeDamage/Heal/Revive`，但它只从 provider 读取 Context，并把权威事实写入 `CharacterContext.Resources` 和 `CharacterContext.Lifecycle`。它不再作为 `CharacterContext` provider。

默认死亡规则：

```text
Health == 0
  -> SemanticStateId = CharacterSemanticState.Dead
  -> Lifecycle.State = Disabled
```

复活回到 `Lifecycle.State = Active`。真正销毁才进入 `Destroyed`。

## Item Boundary

`ItemContext : CoCoEntityContext` 是宝箱、门、可拾取物、任务节点等可交互物的共同主线。宝箱只是 prefab/config，不创建 `ChestContext` 或 `ChestState`。

包含：

- `ItemIntent`：openRequested、unlockRequested、useRequested、actorId。
- `ItemSemanticState`：Inactive、Available、Locked、Opening、Opened、Consumed。
- `ItemInventoryPayload`：可选物品载荷。

`ItemContextProvider` 提供 `ItemContext`，同时暴露 `RequestOpen/RequestUnlock/RequestUse` 作为最薄的 Item Intent 写入口。`ItemInputDriver` 可从 `ICoCoIntentSource<ItemIntent>` 采样，或被交互系统、网络 adapter、回放系统直接调用 `RequestOpen/RequestUnlock/RequestUse`。它们都不发布事件，也不切状态；Item 状态控制器读取 Context 后推进 Item 状态，并在状态事实落定后发布事件。

默认规则：

```text
Available / Locked / Opening / Opened -> Lifecycle.State = Active
Consumed -> Lifecycle.State = Consumed
```

## Input Boundary

`InputReader` 属于输入模块，只输出 Core 级输入事实：

- `IInputStateProvider`
- `IInputEventSource`
- `IInputModeController`
- `ICoCoIntentSource<CoCoInputIntent>`

它不依赖 Character，不调用状态控制器，不理解 Player/Vehicle/Item。角色侧的 `CharacterInputDriver` 才负责将 `CoCoInputIntent` 翻译为 `CharacterIntent`。

离散输入通过 `PerformedSequence` 表示新事件，避免一个按钮事件在多个 frame 中重复触发。

## Event Boundary

本地事件仍使用 `CoCoEventBus.Publish<T>` 发布强类型事件。

网络相关事件使用 `CoCoEventEnvelope` 描述：

- `EventTypeId`
- `SourceEntityId`
- `TargetEntityId`
- `Sequence`
- `Tick`
- `Reliable`
- `PayloadTypeId`
- `Payload`

当一个事件既要服务本地系统又要服务网络桥时，使用：

```text
CoCoEventBus.PublishWithEnvelope(ref typedEvent, ref envelope)
```

本地表现系统可订阅强类型事件；网络 adapter 可订阅 `CoCoEventEnvelope`。

## Identity Boundary

必须区分三种 ID：

- `StableEntityId`：跨存档、跨会话，适合静态场景实体，例如宝箱、门、任务触发器。
- `RuntimeInstanceId`：单局运行时实例，适合玩家、动态敌人、掉落物。网络模式下应由 Host 或 StateAuthority 分配。
- `EventSequenceId`：单个 source 的事件序号，用于去重、排序和回放。

`SavableEntityBase.UniqueID` 通过 `ICoCoStableEntityIdProvider.StableEntityId` 暴露给 Core identity。Persistence 不分配 RuntimeInstanceId，也不驱动实时 gameplay。

## Network Adapter Boundary

Core 不包含 Fusion 依赖。Fusion 或其他网络实现应作为可选 adapter：

- 从网络输入构造 Intent。
- 在 authority 侧推进 Context。
- 将 identity、lifecycle、semantic/action state、motion facts、resources、event sequence 等关键事实写入 snapshot。
- 将可靠离散事件桥接为 `CoCoEventEnvelope`。
- 对 remote proxy 做 snapshot interpolation 或 correction。

不为每个 gameplay state 创建网络版本，例如 `NetEnemyStateChase` 或 `NetItemStateOpened`。网络围绕 Context snapshot 和 EventEnvelope 接入。

## 当前迁移边界

- 旧 Enemy 状态仍可通过旧 API 编译运行。
- 新状态优先使用 `Enter(ICoCoContext)`、`OnStateUpdate(ICoCoContext)`、`OnStateFixedUpdate(ICoCoContext)`、`Exit(ICoCoContext)`。
- 新 Character / Item 逻辑应通过 Context 和 Intent 接入，不再依赖具体 PlayerController、EnemyController 或 Chest 命名。

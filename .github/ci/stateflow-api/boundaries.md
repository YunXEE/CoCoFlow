# StateFlow 程序集与 API 边界

状态、图合同和场景组件共用 C# 命名空间 `CoCoFlow.Runtime.StateGraph`。asmdef 按实际使用的类型引用。

| 程序集 | 包路径 | 用途 |
| --- | --- | --- |
| CoCoFlow.Runtime.Core | Runtime/Core | 服务、事件、日志与诊断 |
| CoCoFlow.Runtime.StateGraph.Contracts | Runtime/StateGraph/Contracts | 纯 C# State、Intent、Operation 和只读帧 |
| CoCoFlow.Runtime.Input.Contracts | Runtime/Input/Contracts | 输入动作、原始记录与一次采样的值 |
| CoCoFlow.Runtime.StateGraph | Runtime/StateGraph | Unity 图资产、Host、来源及 Operator 绑定 |

0.4.10 原纯程序集的 GUID 随 Contracts 保留，原 Host 程序集的 GUID 随 Unity 侧保留。按程序集名引用的项目需要更新 asmdef 并重编译。原先导入 `CoCoFlow.Runtime.StateGraphHost` 的场景代码现在导入 `CoCoFlow.Runtime.StateGraph`。

StateLogic 和 Operator 从框架传入的 `PreviousContext` 读取布局默认值或最近一次成功提交的值。Host 在一次手动步进中协调图、Operator 和提交。纯图编译器、资产编译器、时钟和帧提交入口均由框架调用。

## 标准玩法接入

| 玩法写法 | 框架处理 |
| --- | --- |
| 在 Host 的 Intent Sources 配置 `IIntentFrameSource<T>.TrySample(in TickFrame, out T)`，State 用 `[Intent]` 声明同一 `T`。 | 标准目录从 State 声明发现载荷，Host 匹配源并注入冻结的 Intent。单来源从类型派生 ID。需要合并多来源的模块可用 `IntentRegistrationAttribute` 和 `HostIntentRegistration` 声明 reducer 与容量。 |
| Operation 接口标记 `[Operation(Mode.Continuous)]` 或 `Discrete`。State 用 `[Operation]` 声明接口，通过普通属性写命令。Operator 用 `HostOperatorBuilder.Require<TOperation>()` 消费同一接口。 | Roslyn 生成属性 View，目录和 Host 从 Operator 上的 `[OperatorDefinition]` 取同一份声明。已发布模块可以传入已有 Operation ID 和版本，普通 Operation 从类型派生身份。 |
| State 以 `context.Inbox.TryGetBatch<TEvent>`、`batch.TryRead` 读取当前回调的冻结批次。在 Host 边界内挂载实现 `IEventInboxAdapter<TEvent>.Accept(in payload)` 的组件可过滤收件。 | 标准 Host 建立类型化订阅和队列。定向包仅进目标 Host，离开回调后批次句柄失效。 |
| State 调用 `context.Outbox.Publish(in payload)`。Operator 以 `Emit<TEvent>(id, domain, capacity)` 声明通道，再用 `EventOutbox.TryWrite` 写目标和载荷。 | Host 在事务接受后发布，事务取消时丢弃暂存。 |
| Operator 以 `OwnOutcome<T>(block, slot, projection, restorePolicy, defaultValue, fingerprint)` 声明拥有的槽。 | 标准目录登记形状，Host 按同一声明绑定默认值与恢复策略，写入仍经过事务与所有权检查。使用 Operator 的恢复投影时，在 Host 的 Operator Recovery 中启用相应模式。 |

在 State 类内直接读取 `TryGetBatch<TEvent>` 时，生成器会登记事件类型。独立 helper 或扩展方法读取时，在玩法程序集声明 `[assembly: StateInboxEvent(typeof(MyState), typeof(MyEvent))]`，或在 Host 边界内提供对应类型的 Inbox 适配器。

玩家和相机均使用标准 Intent 匹配。`InputIntentContract` 保留 `ActiveMap`、`Changes`、`Current`、`Count`、`FenceGeneration`、`TryGet` 和两种 `TryFind`；`RawInputRecord` 保留 `InputTime` 与 `Sequence`。`Changes`、`Current` 允许托管 `ReadOnlyMemory<RawInputRecord>`，采样游标仍在 Tick 接受后提交。

`HostProjectBindings.TryInstall` 可安装 `HostBindingProvider.Build(...)` 或 `BuildForAssemblyOf<T>()` 构建的标准目录。图编辑器及 Host 在包内编译资产并返回诊断。`Host.TryStart`、`TryStep(double, out CoCoDiagnostic)`、`TrySuspend`、`TryResume`、`TryStop`、`TryDispose`、Temporal/Persistence 入口和 `CurrentContext` 保持公开。Context 帧的引用寿命仍由 `Retain`、`Release` 配对管理。

## 迁移旧调用

| 原路径 | 迁移 |
| --- | --- |
| `IHostBindingProvider`、Host Binding Builder 和独立 Registrar | 让标准 Host 和 Editor 从 State 与 Operator 声明组装。项目的 `RegisterCatalog`、`TryBindOperation` 和 `TryBindContextSlot` 接线可删。 |
| `ICoCoEventToIntentAdapter<TEvent,TIntent>.TryProject` 与图资产中的专用声明 | 删除旧图声明和 Inspector 适配器引用。State 从 Inbox 逐条读取对应类型的事件。需要采样输入时用 Intent Source。 |
| `IntentFrameLayout.TryCreateRuntime`、帧采集和 `TryBindSource` | 提供 `IIntentFrameSource<T>`，在 State 的 Intent 属性中取得本步采样值。 |
| Operation 帧的 `TryBegin`、`TryFinalize`、`Commit` 和 `Cancel` | State 在当前回调给 Operation 属性赋值。Operator 和 Context 生产方通过框架提供的视图读取该步输出。 |
| `StateGraphInstance.TryCreate` 和 `TryStageStep` | Unity 使用 `Host.TryStep`。纯 C# 自驱动调用需迁移到 Unity Host 或独立维护调用方。 |
| `GraphAssetCompiler(cache)`、`GetOrAdd`、`Clear` 和 `Shared` | 项目将资产交给 Host；Editor 和 Host 使用包内编译器及缓存。 |

## 验证入口

[Core 消费者](CoreApi.cs)、[纯玩法消费者](GameplayApi.cs) 和 [Unity 消费者](RequiredApi.cs) 分别按允许的引用编译，均无 friend 权限。`python3 .github/ci/check_stateflow_api.py` 由 `CI Static / gate` 执行并定位失效的调用。检查覆盖必需调用的签名、约束和读写能力。负例逐项核对旧标记、输入旧入口、编译器、时钟、写入视图、读帧工厂及直接提交。随包生成器为模式缺失、非法和属性传模式报告 COCOOP001。其 Unity 引用 shim 只用于编译检查。

Unity 的 `DeclarativeGameplayPlayModeTests` 驱动标准 Host，其独立的玩法测试程序集位于 `Tests/Runtime/StateGraphHost/DeclarativeGameplay`。`StandardBindingPlayModeTests` 继续覆盖完整玩家路径和包内 Operator。完整 EditMode 与 PlayMode 由版本分支的 Unity CI 执行。

Operation 视图的源码生成器位于 `Tools~/StateFlowGenerator`。修改后以 .NET SDK 执行 `dotnet build Tools~/StateFlowGenerator/CoCoFlow.StateFlow.Generator.csproj -c Release`，将 DLL 放回 `Runtime/StateGraph/Contracts`。`RoslynAnalyzer` 标签让 Unity 将它作为构建工具加载。

## Umbra 升级清单

宿主基线为 `96f43a7`。先替换 asmdef 和包引用，再用对应版本的 Unity 重新编译宿主程序集。

| 现有接线 | 宿主迁移与验证 |
| --- | --- |
| DamageRegistrar | 在 Damage Operator 的定义中声明 Operation、Outcome 与事件。验证命令写入、伤害结果及下一 Tick 读取。 |
| EnemyNavMeshRegistrar | 声明移动 Operation，保留导航 Operator 的 Host 引用，验证敌人实际移动。 |
| HandGrip | 将旧 Builder 接线改为 HandGrip Operator 定义，核对抓取的 Operation 与结果槽。 |
| CoCoTimeBindings | 列出同一 CoCoTimeWalkState 的所有旧 descriptor ID。逐个核对所属图和节点，将资产改为标准目录可解析的 ID，或在宿主保留可解析旧 ID 的迁移方案。编译、启动每张样例图，再验证每个节点的配置与行为。 |

Umbra 宿主任务负责项目升级、重新编译及场景实跑。版本 PR 负责 Windows Mono 与 IL2CPP Player。

Core 诊断 `InvalidOperationContract = 18` 表示输出合同无效，`MissingOperationContract = 19` 表示所需输出未匹配，`InvalidHostConfiguration = 68` 表示 Host 配置无效。

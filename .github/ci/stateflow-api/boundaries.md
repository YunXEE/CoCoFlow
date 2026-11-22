# StateFlow 程序集与 API 边界

P001 在 CoCoFlow 0.4.9 的程序集布局上交付类型化 Intent、Operation、Inbox、Outbox 和 Outcome。P002 的长期行为回归覆盖同一目标分支。

## 程序集归属

下表缩写 C、F、G、A 的前缀为 `CoCoFlow.Runtime.Core.`，H 为 `CoCoFlow.Runtime.StateGraphHost`。这五个程序集均参与 Editor 和 Player 编译。

| 程序集 | 源码路径 | 直接引用 | noEngineReferences / unsafe |
| --- | --- | --- | --- |
| C · Contracts | `Runtime/Core/Contracts` | 无 | true / true |
| F · StateFlow | `Runtime/Core/StateFlow` | C | true / false |
| G · StateGraph | `Runtime/Core/StateGraph` | C、F | true / false |
| A · StateGraphAuthoring | `Runtime/Core/StateGraphAuthoring` | C、F、G | false / false |
| H · StateGraphHost | `Runtime/StateGraphHost` | C、F、G、A、`CoCoFlow.Runtime.Core` | false / true |

A 持有图资产、序列化布局和无参资产编译器。H 负责 Unity 生命周期、Host 槽绑定、事务和恢复。Editor.StateGraph、Editor.StateGraphHost 和 Editor.StateGraph.PlayerMetadata 的 asmdef 限定为 Editor。既有生产 friend 权限保持原配置，F 对既有测试和其夹具开放了用于白盒验证的友元权限。

## 标准玩法接入

| 玩法写法 | 框架处理 |
| --- | --- |
| 在 Host 的 Intent Sources 配置 `ICoCoIntentFrameSource<T>.TrySample(in CoCoTickFrame, out T)`，State 用 `[Intent]` 声明同一 `T`。 | 标准目录从 State 声明发现载荷，Host 匹配源并注入冻结的 Intent。单来源从类型派生 ID。需要合并多来源的模块可用 `CoCoIntentRegistrationAttribute` 和 `CoCoStandardIntentRegistration` 声明 reducer 与容量。 |
| Section 接口标记 `[CoCoOperationSection(CoCoOperationSectionMode.Continuous)]` 或 `Discrete`。State 用 `[Operation]` 声明接口，通过普通属性写命令。Operator 用 `CoCoStandardOperatorBuilder.Require<TSection>()` 消费同一接口。 | Roslyn 生成属性 View，目录和 Host 从 Operator 上的 `[CoCoOperatorDefinition]` 取同一份声明。已发布模块可以传入已有 Section ID 和版本，普通 Section 从类型派生身份。 |
| State 以 `context.Inbox.TryGetBatch<TEvent>`、`batch.TryRead` 读取当前回调的冻结批次。在 Host 边界内挂载实现 `ICoCoEventInboxAdapter<TEvent>.Accept(in payload)` 的组件可过滤收件。 | 标准 Host 建立类型化订阅和队列。定向包仅进目标 Host，离开回调后批次句柄失效。 |
| State 调用 `context.Outbox.Publish(in payload)`。Operator 以 `Emit<TEvent>(id, domain, capacity)` 声明通道，再用 `EventOutbox.TryWrite` 写目标和载荷。 | Host 在事务接受后发布，事务取消时丢弃暂存。 |
| Operator 以 `OwnOutcome<T>(block, slot, projection, restorePolicy, defaultValue, fingerprint)` 声明拥有的槽。 | 标准目录登记形状，Host 按同一声明绑定默认值与恢复策略，写入仍经过事务与所有权检查。使用 Operator 的恢复投影时，在 Host 的 Operator Recovery 中启用相应模式。 |

玩家和相机均使用标准 Intent 匹配。`InputIntentData` 保留 `ActiveMap`、`Changes`、`Current`、`Count`、`FenceGeneration`、`TryGet` 和两种 `TryFind`；`RawInputRecord` 保留 `InputTime` 与 `Sequence`。`Changes`、`Current` 允许托管 `ReadOnlyMemory<RawInputRecord>`，采样游标仍在 Tick 接受后提交。

`CoCoStateGraphProjectBindings.TryInstall` 可安装 `CoCoStandardBindingProvider.Build(...)` 或 `BuildForAssemblyOf<T>()` 构建的标准目录。`CoCoStateGraphAssetCompiler().Compile(asset, catalog)` 保留图、结果和诊断。`Host.TryStart`、`TryStep(double, out CoCoDiagnostic)`、`TrySuspend`、`TryResume`、`TryStop`、`TryDispose`、Temporal/Persistence 入口和 `CurrentContext` 保持公开。Context 帧的引用寿命仍由 `Retain`、`Release` 配对管理。

## 迁移旧调用

| 原路径 | 迁移 |
| --- | --- |
| `ICoCoStateGraphProjectBindingProvider`、Host Binding Builder 和独立 Registrar | 让标准 Host 和 Editor 从 State 与 Operator 声明组装。项目的 `RegisterCatalog`、`TryBindOperation` 和 `TryBindContextSlot` 接线可删。 |
| `ICoCoEventToIntentAdapter<TEvent,TIntent>.TryProject` 与图资产中的专用声明 | 删除旧图声明和 Inspector 适配器引用。State 改从 Inbox 读同一事件类型。动画反馈由 `AnimFeedbackEvent` 定向送达原 Host。需要采样输入时用 Intent Source。 |
| `CoCoIntentFrameLayout.TryCreateRuntime`、帧采集和 `TryBindSource` | 提供 `ICoCoIntentFrameSource<T>`，在 State 或 Condition 使用框架提供的冻结读句柄。 |
| Operation 帧的 `TryBegin`、`TryFinalize`、`Commit` 和 `Cancel` | State 在当前回调写 Section；Operator 或正式 producer 使用受支持的只读视图。 |
| `CoCoStateGraphRuntime.TryCreate` 和 `TryStageStep` | Unity 使用 `Host.TryStep`。纯 C# 自驱动调用需迁移到 Unity Host 或独立维护调用方。 |
| `CoCoStateGraphAssetCompiler(cache)`、`GetOrAdd`、`Clear` 和 `Shared` | 调用无参编译器。包体内部缓存夹具有测试友元权限。 |

## 验证入口

[RequiredApi.cs](RequiredApi.cs) 是无 friend 权限的外部编译用例。`python3 .github/ci/check_stateflow_api.py` 由 `CI Static / gate` 执行并定位失效的调用。检查覆盖必需调用的签名、约束和读写能力。其 Unity 引用 shim 只用于编译检查。

Unity 的 `DeclarativeGameplayPlayModeTests` 驱动标准 Host，包外示例代码位于 `Tests/Runtime/StateGraphHost/DeclarativeGameplay`。`StandardBindingPlayModeTests` 继续覆盖完整玩家路径和包内 Operator。完整 EditMode 与 PlayMode 由版本分支的 Unity CI 执行。

Section View 源码生成器位于 `Tools~/StateFlowGenerator`。修改后以 .NET SDK 执行 `dotnet build Tools~/StateFlowGenerator/CoCoFlow.StateFlow.Generator.csproj -c Release`，将 DLL 放回 `Runtime/Core/StateFlow`。`RoslynAnalyzer` 标签让 Unity 将它作为构建工具加载。

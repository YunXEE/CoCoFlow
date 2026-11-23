# StateFlow 程序集与 API 边界

CoCoFlow 0.4.10 的玩法入口由三个 Runtime 程序集提供。

| 程序集 | 源码路径 | 包内直接引用 | noEngineReferences / unsafe |
| --- | --- | --- | --- |
| CoCoFlow.Runtime.Core | `Runtime/Core` | 无 | true / false |
| CoCoFlow.Runtime.StateGraph | `Runtime/StateGraph` | Core | true / true |
| CoCoFlow.Runtime.StateGraphHost | `Runtime/StateGraphHost` | Core、StateGraph | false / true |

三个模块的目录、程序集名称和 C# 根命名空间逐一对应。服务、日志、事件和诊断使用 `CoCoFlow.Runtime.Core`。纯玩法的 State、Condition、Intent、Section 和帧类型使用 `CoCoFlow.Runtime.StateGraph`。图资产、Host 组件及 Unity 绑定使用 `CoCoFlow.Runtime.StateGraphHost`。需要多类 API 的外部程序集分别添加相应的引用和 `using`。

原先按名称引用 `CoCoFlow.Runtime.Core.StateGraph` 的 asmdef 改用 `CoCoFlow.Runtime.StateGraph`。该定义保留了 GUID，已有 GUID 引用仍然有效。原 `Core.Contracts` 的引用按所用类型拆分，`Core.StateFlow` 迁至 StateGraph，`Core.StateGraphAuthoring` 迁至 Host。外部源码需要重新编译，预编译 DLL 也需要重建。

三个 Editor 主目录使用对应的 `CoCoFlow.Editor.Core`、`CoCoFlow.Editor.StateGraph` 和 `CoCoFlow.Editor.StateGraphHost`。单独编译的 `CoCoFlow.Editor.PlayerMetadata` 用于隔离扫描 Player DLL。原 Editor.Common 的公用工具现归 Editor.Core。

旧图中的 `SerializeReference` 使用写入文件时的程序集、命名空间和类型名。Host 可读取由 `CoCoFlow.Runtime.Core.StateGraphAuthoring` 或 `CoCoFlow.Runtime.StateGraphHost` 写入旧命名空间的 AnyState，也会还原状态和条件的配置类型，保留字段值及共享引用。重新保存的图使用当前类型，图和存档的类型指纹仍使用原有身份。

## 标准玩法接入

| 玩法写法 | 框架处理 |
| --- | --- |
| 在 Host 的 Intent Sources 配置 `ICoCoIntentFrameSource<T>.TrySample(in CoCoTickFrame, out T)`，State 用 `[Intent]` 声明同一 `T`。 | 标准目录从 State 声明发现载荷，Host 匹配源并注入冻结的 Intent。单来源从类型派生 ID。需要合并多来源的模块可用 `CoCoIntentRegistrationAttribute` 和 `CoCoStandardIntentRegistration` 声明 reducer 与容量。 |
| Section 接口标记 `[CoCoOperationSection(CoCoOperationSectionMode.Continuous)]` 或 `Discrete`。State 用 `[Operation]` 声明接口，通过普通属性写命令。Operator 用 `CoCoStandardOperatorBuilder.Require<TSection>()` 消费同一接口。 | Roslyn 生成属性 View，目录和 Host 从 Operator 上的 `[CoCoOperatorDefinition]` 取同一份声明。已发布模块可以传入已有 Section ID 和版本，普通 Section 从类型派生身份。 |
| State 以 `context.Inbox.TryGetBatch<TEvent>`、`batch.TryRead` 读取当前回调的冻结批次。在 Host 边界内挂载实现 `ICoCoEventInboxAdapter<TEvent>.Accept(in payload)` 的组件可过滤收件。 | 标准 Host 建立类型化订阅和队列。定向包仅进目标 Host，离开回调后批次句柄失效。 |
| State 调用 `context.Outbox.Publish(in payload)`。Operator 以 `Emit<TEvent>(id, domain, capacity)` 声明通道，再用 `EventOutbox.TryWrite` 写目标和载荷。 | Host 在事务接受后发布，事务取消时丢弃暂存。 |
| Operator 以 `OwnOutcome<T>(block, slot, projection, restorePolicy, defaultValue, fingerprint)` 声明拥有的槽。 | 标准目录登记形状，Host 按同一声明绑定默认值与恢复策略，写入仍经过事务与所有权检查。使用 Operator 的恢复投影时，在 Host 的 Operator Recovery 中启用相应模式。 |

在 State 类内直接读取 `TryGetBatch<TEvent>` 时，生成器会登记事件类型。独立 helper 或扩展方法读取时，在玩法程序集声明 `[assembly: CoCoStateInboxEvent(typeof(MyState), typeof(MyEvent))]`，或在 Host 边界内提供对应类型的 Inbox 适配器。

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

[Core 消费者](CoreApi.cs)、[纯玩法消费者](GameplayApi.cs) 和 [Unity 消费者](RequiredApi.cs) 分别按允许的引用编译，均无 friend 权限。`python3 .github/ci/check_stateflow_api.py` 由 `CI Static / gate` 执行并定位失效的调用。检查覆盖必需调用的签名、约束和读写能力。逐项负例检查测试重置、自驱动创建和跳过校验的帧提交。其 Unity 引用 shim 只用于编译检查。

Unity 的 `DeclarativeGameplayPlayModeTests` 驱动标准 Host，包外示例代码位于 `Tests/Runtime/StateGraphHost/DeclarativeGameplay`。`StandardBindingPlayModeTests` 继续覆盖完整玩家路径和包内 Operator。完整 EditMode 与 PlayMode 由版本分支的 Unity CI 执行。

Section View 源码生成器位于 `Tools~/StateFlowGenerator`。修改后以 .NET SDK 执行 `dotnet build Tools~/StateFlowGenerator/CoCoFlow.StateFlow.Generator.csproj -c Release`，将 DLL 放回 `Runtime/StateGraph`。`RoslynAnalyzer` 标签让 Unity 将它作为构建工具加载。

## Umbra 升级清单

宿主基线为 `96f43a7`。先替换 asmdef 和包引用，再用对应版本的 Unity 重新编译宿主程序集。

| 现有接线 | 宿主迁移与验证 |
| --- | --- |
| DamageRegistrar | 在 Damage Operator 的定义中声明 Section、Outcome 与事件。验证命令写入、伤害结果及下一 Tick 读取。 |
| EnemyNavMeshRegistrar | 声明移动 Section，保留导航 Operator 的 Host 引用，验证敌人实际移动。 |
| HandGrip | 将旧 Builder 接线改为 HandGrip Operator 定义，核对抓取的 Section 与结果槽。 |
| CoCoAnim | 迁移绑定并核对动画反馈到达对应 Host 的 Inbox。 |
| CoCoTimeBindings | 列出同一 CoCoTimeWalkState 的所有旧 descriptor ID。逐个核对所属图和节点，将资产改为标准目录可解析的 ID，或在宿主保留可解析旧 ID 的迁移方案。编译、启动每张样例图，再验证每个节点的配置与行为。 |

Umbra 宿主任务负责项目升级、重新编译及场景实跑。版本 PR 负责 Windows Mono 与 IL2CPP Player。

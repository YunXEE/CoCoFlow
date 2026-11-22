# StateFlow 程序集与 API 边界

P001 把 [0.4.9 发布树](https://github.com/YunXEE/CoCoFlow/tree/11fd6fe2ecd970da57d04eae06c3de89d3c56e50) 的现有调用与 P001 v1.6 的维护者裁决 对齐。生产迁移归 P003，行为回归归 P002。

## 编译归属

程序集短名 C、F、G、A 对应 `CoCoFlow.Runtime.Core.` 下的 `Contracts`、`StateFlow`、`StateGraph`、`StateGraphAuthoring`。H 是 `CoCoFlow.Runtime.StateGraphHost`。friend 列省略 `CoCoFlow.` 前缀；箭头表示本程序集直接引用另一程序集。

| 单元／路径 | C# 文件 | 直接引用 | noEngineReferences / unsafe | 现有 friend |
| --- | ---: | --- | --- | --- |
| C · `Runtime/Core/Contracts`，身份、生命周期、时间、诊断、Logic 基类、输入值 | 16 | 无 | true / true | G |
| F · `Runtime/Core/StateFlow`，帧、Intent 仲裁、Operation、Operator、邮箱、历史 | 11 | C | true / false | G、H、Runtime.Modules.Input |
| G · `Runtime/Core/StateGraph`，回调、工厂、描述符、图编译与执行 | 20 | C、F | true / false | Editor.StateGraph、H、Tests.Editor.StateGraph、Tests.Runtime.StateGraphHost |
| A · `Runtime/Core/StateGraphAuthoring`，图资产、序列化、冻结、编译、缓存 | 10 | C、F、G | false / false | Editor.StateGraph、H、Tests.Editor.StateGraph、Tests.Runtime.StateGraphHost |
| H · `Runtime/StateGraphHost`，组件、项目绑定、事务、恢复、Unity 生命周期 | 21 | C、F、G、A、Runtime.Core | false / true | Editor.StateGraphHost、Runtime.Pooling.Temporal、Runtime.Modules.Map.Temporal、Runtime.Modules.Animation、Runtime.Modules.Locomotion、Runtime.Modules.Persistence、Tests.Editor.StateGraphHost、Tests.Runtime.Pooling.Temporal、Tests.Runtime.StateGraphHost |

78 个 C# 文件按最近的 asmdef 归属。五者的平台、宏和版本限制列表均为空，`autoReferenced=true`，可进入 Editor 和 Player。旧 `Runtime.Core` 另有四个 C# 文件，直接引用 C。Input 引用 F 并用 friend 访问采样游标。Animation、Locomotion、Camera、Map.Temporal、Pooling.Temporal、Persistence 指向 H。

`Editor.StateGraph`、`Editor.StateGraphHost` 和 `Editor.StateGraph.PlayerMetadata` 各自独立。PlayerMetadata 虽然输出 Player 所需的元数据，程序集本身仅 Editor 可见，引用 C、F。A 中的 `CoCoStateGraphEditorLayout` 由资产序列化使用，随 A 编译。保留 `CoCoStateGraphAsset.cs.meta` GUID `4123cdeecc154cdca56d605fc6bfaad8`。H 的 Temporal 调试成员有 `UNITY_EDITOR` 分支，组件身份另有 `UNITY_6000_5_OR_NEWER` 分支。

## 保留入口

编译清单在 [RequiredApi.cs](RequiredApi.cs)。表内标为迁移的接线表述记录发布 API 与目标行为。若 P003 替换了过渡接线，应在同一提交中用等价新调用覆盖原编译样例。

| 决策／角色 | 程序集、签名与约束 | 使用时机、有效期和失败语义 |
| --- | --- | --- |
| D1.1 State 与 Condition | C 的 `CoCoStateLogic`、`[CoCoState]`、属性 `[Intent]`／`[Operation]`、字段 `[Config]`／`[Memory]`。G 的 `OnEnter / Update / OnExit(CoCoStateExecutionContext)` 和 `Evaluate(CoCoConditionEvaluationContext) → bool` | 回调期间读本 Tick 的冻结 Intent 和前次已提交 Context。Enter 父到子，Update 根到叶，Exit 叶到父。Condition 只读。 |
| D1.2 State 写入 | G 的 `Operations.Write<TValue>(CoCoOperationSectionField<TValue>, in TValue) → bool` 和 `ResolveField<TSection,TValue>(int)`。TValue 为 unmanaged，TSection 为 class 且实现 `ICoCoOperationSection`。保留 `Memory<TMemory>()`（派生 `CoCoActivationMemory`）、两类转场请求与 `TrySetActionProgress(double)` | writer 仅在回调内有效，且只能写已声明的 Section。离散 Section 由 `TryEnableDiscrete<TSection>()` 启用。只请求已有转场，胜者在下一接受 Tick 生效。progress 有限、单调、在 [0,1] 内；不匹配的 Memory 返回 null 并记录错误。 |
| D1.3 事件收发 | C 的 `ICoCoEventInboxAdapter<TEvent>.Accept(in TEvent)`；G 的 `Inbox.TryGetBatch<TEvent>(out batch)`、`batch.TryRead(int,out TEvent)`、`Outbox.Publish<TEvent>(in TEvent)`。F 的 Operator `CoCoEventOutboxRequirement.TryCreate<TEvent>`、`EventOutbox.TryWrite<TEvent>(requirement,target,in payload)`。TEvent 为 unmanaged | Inbox 读不消费，批次离开回调失效。项目仍可按目标实体筛选收件。State 发布无需逐类型登记，Operator 定向发布使用声明的类型、Domain、ID 与容量。成功提交才发布，取消后零发布。 |
| D1.4 Intent Source | F 的 `ICoCoIntentFrameSource<T>.TrySample(in CoCoTickFrame,out T)`、`ICoCoIntentFrame.TryFirst<T>` 和按 `CoCoIntentHandle<T>` 的 `TryGet/IsPresent`。TIntent 无 unmanaged 约束；有合并规则时 `TReducer : unmanaged, ICoCoIntentReducer<TIntent>`，发布版 factory 实现 `ICoCoIntentReducerFactory<TIntent,TReducer>` | Source 交付样本，冻结后只读；有多个同类型 ID 时用 handle 区分。普通单来源迁移为按类型自动接线，玩家、相机、敌人与包外来源走同一路径。当前编译样例覆盖发布版 Catalog token／Host 注册绑定。 |
| D1.5 Operation 和 Outcome | F 的 `ICoCoOperator.TryExecute(in CoCoOperatorExecutionContext,out CoCoOperatorOutcome)`、`TryGet<TSection>(requirement,out entry)`、`TryWriteOutcome<TValue>(slot 或 slotId,in TValue)`；TValue 为 unmanaged。发布版 factory `ICoCoOperationSectionViewFactory<TSection>.Create(in CoCoOperationSectionViewContext<TSection>)`，TSection 为 class 且实现 `ICoCoOperationSection` | Outcome 仅允许所属 Operator 的当前回调写声明槽，成功提交后供下一 Tick 的 PreviousContext 读取。保留显式选择 Continuous／Discrete；漏选须诊断。迁移后 State 与 Operator 通过同一 Section 接口对接，框架完成重复的 Catalog/Host 接线。 |
| D1.6 项目 Provider | H 的 `ICoCoStateGraphProjectBindingProvider.Catalog`、`TryConfigure(CoCoStateGraphHostBindingBuilder,out CoCoDiagnostic)`、`CoCoStateGraphProjectBindings.TryInstall`、`CoCoStandardBindingProvider.Build`／`BuildForAssemblyOf<T>()`；Builder.Graph 返回 G 的 `CoCoCompiledStateGraph` | 首次安装须冻结 Catalog，重复安装返回 false 和诊断；启动验证绑定完整性。项目 Provider 的 State、Condition、Context 槽等显式绑定应有等价迁移。发布版 Registrar 重复接线按 D1.4/D1.5 收敛。 |
| D1.7 Host 生命周期与读取 | H 的 `TryStart/TrySuspend/TryResume/TryStop/TryDispose(out CoCoDiagnostic)`、`TryStep(double,out CoCoDiagnostic)`；Lifecycle、Fault、LastDiagnostic、GraphInstanceId、ActivePaths、Trace、InputAuthorityRevision、StateGraphAsset。CurrentContext 返回具体 F 的 `CoCoContextFrame`，包含 Header、Revision、Layout、Read、Retain/Release | 只有接受的 Tick 提交。Created 不能 Stop，公开 Dispose 接受 Created/Stopped。借读当前帧后需要跨提交保存时，Retain 成功后配对 Release。 |
| D1.8 Host 恢复和本地事件 | H 的 `TryBeginTemporalPreview`、`TryPreviewTemporal(int,out CoCoDiagnostic)`、`TryConfirmTemporalRestore`、`TryCancelTemporalPreview`、`TryCorrectWorld`、`TryValidateRestore(CoCoContextFrame,CoCoTickFrame,out CoCoContextCommitStatus)`。TemporalState、TemporalHistoryCapacity、RequiresWorldCorrection；`TryEnqueueLocal<TEvent>(in CoCoEventPacket<TEvent>)`，TEvent 为 unmanaged | Host 统一协调预览、取消、确认、修正；Validate 只检验恢复可行性。无邮箱时返回 MailboxUnavailable。 |
| D1.9 图资产与诊断 | A 的 `CoCoStateGraphAsset`、无参 `CoCoStateGraphAssetCompiler().Compile(asset,CoCoGraphDescriptorCatalog)`；结果的 Succeeded、HasErrors、Diagnostics、Graph | 资产编译经 Unity 主线程。编译错误产生诊断，Graph 为空；编译结果和 Builder.Graph 保持 G 的具体图类型。 |
| D3.1 已发布输入载荷 | C 的 `InputIntentData(activeMap,changes,current=default,fenceGeneration=0)`、ActiveMap、Changes、Current、FenceGeneration、Count、TryGet、两个 TryFind；`RawInputRecord.InputTime` 与 Sequence | Changes 先于 Current，越界返回 false。生产者保证已发布 backing storage 不再写入。ReadOnlyMemory 使 payload 含托管引用，因此 Source、Catalog、Host、Intent 读视图都接受该类型。运行时注入和候选回滚由行为用例覆盖。 |

P002 可在独立 Host 通过 State / Operator 公共回调观察冻结输入、Outcome、事件和回调顺序，通过 `CurrentContext.Header.TickFrame`、读槽或诊断观察候选是否提交，并经 Host 公共 Temporal API 检验恢复。底层构帧和手工注入只用于覆盖兼容或迁移路径。

## 收回或保持内部的入口

| 决策 | 发布符号、可见性与已知调用 | 迁移出口及签名连带 |
| --- | --- | --- |
| D1.3 Event→Intent | F 的 public `ICoCoEventToIntentAdapter<TEvent,TIntent>.TryProject`；G 的专用声明与目录入口；H 的 `TryBindEventAdapter` 及专用路由。包体 `CoCoStateGraphHostPlayModeTests` 有旧链路实例。宿主的交互组件使用 Inbox 筛选。 | P003 盘点图资产、声明、调用与包内测试。保留 Inbox 的目标过滤和 Outbox；需要 Intent 时改用 D1.4 Source。 |
| D2.1 采样游标 | F 的 `ICoCoCursorIntentSource<T>`、`CoCoIntentSourceCursor` 已 internal；Input 使用 friend，H 在 `CommitIntentTickNoFail` 推进提交读点。 | 保持 internal 与授权边。项目仅实现 `TrySample(in tick,out value)`。 |
| D2.2 Intent 装配 | F 的 public `CoCoIntentFrameLayout.TryCreateRuntime`、`CoCoIntentFrameRuntime`、`TryBindSource`／`TryBindEventAdapter`、`CoCoIntentSourceBinding<T>`、`CoCoEventToIntentBinding`，以及 `CoCoActorEventInboxCore.TryBindIntentRuntime`。H 项目绑定和事务驱动这些入口。 | 收回手工创建、装配和采集写权。同步清理接受或返回具体 Runtime 的跨程序集公开签名，保留 D1.4 的只读面和公共句柄。 |
| D2.3 Operation 提交 | F 的 public `CoCoOperationFrame.TryCreate/TryBegin`、`CoCoOperationFrameWriter.TryFinalize/Cancel`、`CoCoFinalizedOperationFrame.Commit/Cancel`；G Runtime 构造和暂存，H OperatorRuntime 消费。 | 收回原始写权。保留 State 回调写入、Operator 读取和 producer 的 staged 只读视图。隐藏具体 Frame 前同步处置 D2.4 的参数。 |
| D2.4 裸 Runtime | G 的 public `CoCoStateGraphRuntime.TryCreate(compiledGraph,logicBindings,operationFrame,actorClock,...)`、`TryStageStep(in tick, ICoCoIntentFrame, in previous, out CoCoStagedGraphStep, out diagnostic)`；H 和包体测试驱动。事务、准备令牌、帧存储已有 internal。 | 收回绕过 Host 的创建与暂存签名，保留 Host 单步和 Logic 回调。若另有纯 C# 独立调用方，P003 须逐项记录处置，MonoBehaviour Host 无法直接接替。 |
| D2.5 缓存注入 | A 的 public 构造器 `CoCoStateGraphAssetCompiler(ICoCoStateGraphCompilationCache)`、缓存 `GetOrAdd`／`Clear`、`CoCoStateGraphCompilationCache.Shared` 和对应 key；Host 与 Editor 用无参入口，`CoCoStateGraphAssetCompilerCacheTests` 注入缓存。 | 同步收回注入重载、接口、具体缓存操作及连带签名。保留无参 Compile／诊断；隔离缓存或主动清理调用单列处置。 |
| D2.6 Temporal 调试 | H 的 `TryCaptureTemporalDebugSnapshot`、调试快照与 TemporalController 元数据操作已有 internal，部分仅在 Editor 编译，由 Editor.StateGraphHost friend 消费。 | 保持 internal 与序列化资产身份。继续保留 D1.7 的公开帧读取入口。 |

P003 每个生产改动应对应上表记录，列出原文件与 .meta、项目和包内调用者、签名闭包、替代调用或遗留归属。文件移动前后逐项核对 asmdef 及 GUID；用户已批准的收回应和替代路径一起验证。

## 外部消费证据

已对锁定 0.4.9 发布提交的宿主做定点源码抽样。宿主位置与具体提交记录在 P001 交付证据中。

- 状态逻辑读 InputIntentData 与 TryFind/Sequence，使用 Operation Section，并通过 PreviousContext 解析槽。
- 敌人 Intent 使用标准泛型注册。寻路与伤害模块使用目录和 Host 的双重接线。伤害 Operator 从 PreviousContext 读取、向已声明槽写 Outcome。
- 交互组件按目标玩家筛选 Inbox，玩家系统启停并驱动 Tick，回放工具使用 Host 的预览和确认。

宿主场景、完整宿主编译、资产全量扫描和第三方二进制调用待外部验证。

## 可复现检查

运行 `python3 .github/ci/check_stateflow_api.py`（.NET 10）。脚本把实际 C# 源文件按 asmdef 分组成独立程序集，按其直接引用编译，再编译无 friend 权限的消费程序集。CI 使用少量 Unity 引用签名以在 Linux 编译，可通过 `--unity-core-module` 换成本机官方 DLL。此检查覆盖源码调用兼容性。完整 Unity Editor 编译和行为由现有 Unity 作业验证，程序集导出面、二进制兼容性和 Player 行为不在该检查范围内。

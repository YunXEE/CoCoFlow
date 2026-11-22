# StateFlow 长期回归集

这组用例供 StateFlow 变更和 P003 文件迁移前后选跑。执行时记录包体 Git SHA、Unity 版本、平台和 NUnit XML。完整发布门禁见 [CI 验证规则](ci/README.md)。

E = Editor EditMode，P = Editor PlayMode，M = Windows Mono Player，I = Windows IL2CPP Player。

| 契约 | 测试方法 | 平台 | 可观察断言 |
| --- | --- | --- | --- |
| 外部 Logic 消费冻结 Intent | [`ReaderRawIntentFlowsIntoStateLogicPerTick`](../Tests/Runtime/StateGraphHost/RawInputEndToEndPlayModeTests.cs) | P/M/I | Host 经 Reader 和 Intent 把 Move 交给独立 Fixtures 程序集的 StateLogic。两次成功 Tick 之间的短按产生按序且各一次的 Performed 和 Canceled，已松开无 Held，下一 Tick 无重放。 |
| Transition 裁决 | [`MultipleRequestsUseConditionsThenPriorityToChooseOneWinner`](../Tests/Editor/StateGraph/CoCoStateGraphRuntimeContractTests.cs) | E | 两个请求在条件通过后按优先级选出唯一活动叶。 |
| 原子提交图状态 | [`TransitionCommitCarriesEveryGraphStateRecord`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostContextAuthorityPlayModeTests.cs) | P/M/I | Context revision 前进，转移源和目标的 Graph State 在同一帧提交。 |
| Operation 离散胜者 | [`OperationFrameDiscreteWinnerGetsOneSequenceAtFinalizeAndCommit`](../Tests/Editor/CoreContracts/CoCoStateFlowContractTests.cs) | E | Finalize 只给离散胜者一个序号，取消后重试仍取得序号 1。 |
| 首个有效 Tick | [`FirstTickReadsLayoutDefaultsAndCommitsRevisionOne`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostOperatorCommitPlayModeTests.cs) | P/M/I | Host 首次 Step 读取默认值 5，提交值 6，revision 和 tick 都为 1。 |
| 越权 Outcome 回滚 | [`IgnoredUnauthorizedOutcomeWriteFaultsAndPreservesCommittedRevision`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostOperatorCommitPlayModeTests.cs) | P/M/I | 越权写入使 Host 进入 Fault，已提交帧仍有效，原 revision 保持。 |
| 非法 Outbox 回滚 | [`IgnoredInvalidOutboxWriteFaultsAndPublishesNothing`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostOperatorCommitPlayModeTests.cs) | P/M/I | 非法事件使 Tick 失败且无包发布，首次 Tick 的 Context 与 Clock 都未提交。 |
| 提交后发布 | [`PublishSeesCompleteAuthorityAndDefersStopDisposeUntilAllPacketsFinish`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostOperatorCommitPlayModeTests.cs) | P/M/I | 监听器看到已提交的 revision/tick，按序收到序号 1、2，Stop 和 Dispose 等待发布完成。 |
| 存档跨实例恢复 | [`SaveGameLoadsDurableContextIntoSiblingAndReseedsHistory`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostPersistencePlayModeTests.cs) | P/M/I | 当版生成的存档恢复至同一实体的另一 Host，验证来源和目标身份、持久值和重新播种的历史。 |
| 恢复拒绝 | [`InvalidPayloadAndRejectedBindingNeverPublishAuthority`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostPersistencePlayModeTests.cs) | P/M/I | 无效 payload 和绑定拒绝均保留恢复前的权威。 |
| 旧容器文档 | [`EmptyLegacyContainerSectionsDoNotChangeGraphPayloadOrTheWholeDocumentVersion`](../Tests/Runtime/Persistence/PersistenceContainerSerializationTests.cs) | P/M/I | 旧区段为空对象、空 records 或 null 时不改变图 payload 或容器版本，覆盖固定历史文档封装。 |
| 旧图布局 | [`FixedLegacyGraphRetainsContentFingerprint`](../Tests/Editor/StateGraph/CoCoStateGraphFingerprintTests.cs) | E | 固定旧图的内容指纹保持为发布基线值。 |
| Temporal 预览与取消 | [`PreviewAndCancelRunNoStateGraphOrAuthorityWork`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostTemporalPlayModeTests.cs) | P/M/I | 预览不执行图或提交权威，取消保留原帧和标识。 |
| Temporal 确认 | [`ConfirmRestoresContextGraphClockAndCreatesNewEpochBranch`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostTemporalPlayModeTests.cs) | P/M/I | 确认恢复历史 Context、Graph 和 Clock，并增加 epoch。 |
| Temporal 绑定失败 | [`ConfirmBindingFailurePreservesAuthorityAndRequiresCorrection`](../Tests/Runtime/StateGraphHost/CoCoStateGraphHostTemporalBindingPlayModeTests.cs) | P/M/I | 绑定拒绝或抛错时保留原权威，并允许后续修正。 |

## Editor 选跑

将 `UNITY_EDITOR` 指向对应版本的 Unity 可执行文件。将 `UNITY_PROJECT` 指向挂载待测包体且启用了 CoCoFlow `testables` 的宿主。串行运行，保留两份 XML。

```sh
edit_cases='MultipleRequestsUseConditionsThenPriorityToChooseOneWinner|OperationFrameDiscreteWinnerGetsOneSequenceAtFinalizeAndCommit|FixedLegacyGraphRetainsContentFingerprint'
play_cases='ReaderRawIntentFlowsIntoStateLogicPerTick|TransitionCommitCarriesEveryGraphStateRecord|FirstTickReadsLayoutDefaultsAndCommitsRevisionOne|IgnoredUnauthorizedOutcomeWriteFaultsAndPreservesCommittedRevision|IgnoredInvalidOutboxWriteFaultsAndPublishesNothing|PublishSeesCompleteAuthorityAndDefersStopDisposeUntilAllPacketsFinish|SaveGameLoadsDurableContextIntoSiblingAndReseedsHistory|InvalidPayloadAndRejectedBindingNeverPublishAuthority|EmptyLegacyContainerSectionsDoNotChangeGraphPayloadOrTheWholeDocumentVersion|PreviewAndCancelRunNoStateGraphOrAuthorityWork|ConfirmRestoresContextGraphClockAndCreatesNewEpochBranch|ConfirmBindingFailurePreservesAuthorityAndRequiresCorrection'

"$UNITY_EDITOR" -batchmode -nographics -projectPath "$UNITY_PROJECT" -runTests \
  -testPlatform EditMode -testFilter "($edit_cases)(\\(|$)" \
  -testResults "$PWD/stateflow-editmode.xml"
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$UNITY_PROJECT" -runTests \
  -testPlatform PlayMode -testFilter "($play_cases)(\\(|$)" \
  -testResults "$PWD/stateflow-playmode.xml"
```

检查两份 XML 是否包含表中对应模式的全部方法，并检查每个参数化用例的结果。Windows Player 按完整版本门禁执行，再用表中方法名从对应 XML 定位结果。

## P002 处置记录

- 保留 [`CommitHarnessPublishesAndConsumesEventSequenceOnlyAfterSuccess`](../Tests/Editor/CoreContracts/CoCoStateFlowContractTests.cs)。私有 Harness 自己管理发布数与序号。两个真实 Host Outbox 用例已覆盖取消零发布及成功后按序发布。「先成功提交一个无事件 Tick，随后首个包仍为序号 1」仍缺真实 Host 替代证据。
- 输入游标的 `Prepared` 在采样时更新，`Committed` 在 [StateGraphHost 提交屏障](../Runtime/StateGraphHost/CoCoStateGraphTransaction.cs)更新。[Host 失败路径](../Runtime/StateGraphHost/CoCoStateGraphHost.cs)解析候选采样，但不提交游标。现有 Outbox 和越权测试触发真实 Tick 失败，输入端到端用例验证成功 Tick 之间的交付。尚无用例在采样后触发真实 Tick 失败并从外部观察这些输入仍未消费。

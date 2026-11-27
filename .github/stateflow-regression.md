# StateFlow 长期回归集

这组用例供 StateFlow 变更和 P003 文件迁移前后选跑。执行时记录包体 Git SHA、Unity 版本、平台和 NUnit XML。完整发布门禁见 [CI 验证规则](ci/README.md)。

E = Editor EditMode，P = Editor PlayMode，M = Windows Mono Player，I = Windows IL2CPP Player。

| 契约 | 测试方法 | 平台 | 可观察断言 |
| --- | --- | --- | --- |
| 外部 Logic 消费冻结 Intent | [`ReaderRawIntentFlowsIntoStateLogicPerTick`](../Tests/Runtime/StateGraphHost/RawInputEndToEndPlayModeTests.cs) | P/M/I | Host 经 Reader 和 Intent 把 Move 交给独立 Fixtures 程序集的 StateLogic。两次成功 Tick 之间的短按产生按序且各一次的 Performed 和 Canceled，已松开无 Held，下一 Tick 无重放。 |
| Transition 裁决 | [`HighestRequestedPriorityWinsAndUnrequestedEdgesAreSkipped`](../Tests/Editor/StateGraph/StateGraphRuntimeContractTests.cs) | E | 符合窗口的请求按优先级选出唯一活动叶。 |
| 原子提交图状态 | [`TransitionCommitCarriesEveryGraphStateRecord`](../Tests/Runtime/StateGraphHost/StateGraphHostContextAuthorityPlayModeTests.cs) | P/M/I | Context revision 前进，转移源和目标的 Graph State 在同一帧提交。 |
| Operation 离散胜者 | [`OperationFrameDiscreteWinnerGetsOneSequenceAtFinalizeAndCommit`](../Tests/Editor/StateGraph/StateFlowContractTests.cs) | E | Finalize 只给离散胜者一个序号，取消后重试仍取得序号 1。 |
| 首个有效 Tick | [`FirstTickReadsLayoutDefaultsAndCommitsRevisionOne`](../Tests/Runtime/StateGraphHost/StateGraphHostOperatorCommitPlayModeTests.cs) | P/M/I | Host 首次 Step 读取默认值 5，提交值 6，revision 和 tick 都为 1。 |
| 越权 Outcome 回滚 | [`IgnoredUnauthorizedOutcomeWriteFaultsAndPreservesCommittedRevision`](../Tests/Runtime/StateGraphHost/StateGraphHostOperatorCommitPlayModeTests.cs) | P/M/I | 越权写入使 Host 进入 Fault，已提交帧仍有效，原 revision 保持。 |
| 非法 Outbox 回滚 | [`IgnoredInvalidOutboxWriteFaultsAndPublishesNothing`](../Tests/Runtime/StateGraphHost/StateGraphHostOperatorCommitPlayModeTests.cs) | P/M/I | 非法事件使 Tick 失败且无包发布，首次 Tick 的 Context 与 Clock 都未提交。 |
| 提交后发布 | [`PublishSeesCompleteAuthorityAndDefersStopDisposeUntilAllPacketsFinish`](../Tests/Runtime/StateGraphHost/StateGraphHostOperatorCommitPlayModeTests.cs) | P/M/I | 监听器看到已提交的 revision/tick，按序收到序号 1、2，Stop 和 Dispose 等待发布完成。 |
| 存档跨实例恢复 | [`SaveGameLoadsDurableContextIntoSiblingAndReseedsHistory`](../Tests/Runtime/StateGraphHost/StateGraphHostPersistencePlayModeTests.cs) | P/M/I | 当版生成的存档恢复至同一实体的另一 Host，验证来源和目标身份、持久值和重新播种的历史。 |
| 恢复拒绝 | [`InvalidPayloadAndRejectedBindingNeverPublishAuthority`](../Tests/Runtime/StateGraphHost/StateGraphHostPersistencePlayModeTests.cs) | P/M/I | 无效 payload 和绑定拒绝均保留恢复前的权威。 |
| 旧容器文档 | [`EmptyLegacyContainerSectionsDoNotChangeGraphPayloadOrTheWholeDocumentVersion`](../Tests/Runtime/Persistence/PersistenceContainerSerializationTests.cs) | P/M/I | 旧区段为空对象、空 records 或 null 时不改变图 payload 或容器版本，覆盖固定历史文档封装。 |
| 旧图布局 | [`FixedLegacyGraphRetainsContentFingerprint`](../Tests/Editor/StateGraph/StateGraphFingerprintTests.cs) | E | 固定旧图的内容指纹保持为发布基线值。 |
| Temporal 预览与取消 | [`PreviewAndCancelRunNoStateGraphOrAuthorityWork`](../Tests/Runtime/StateGraphHost/StateGraphHostTemporalPlayModeTests.cs) | P/M/I | 预览不执行图或提交权威，取消保留原帧和标识。 |
| Temporal 确认 | [`ConfirmRestoresContextGraphClockAndCreatesNewEpochBranch`](../Tests/Runtime/StateGraphHost/StateGraphHostTemporalPlayModeTests.cs) | P/M/I | 确认恢复历史 Context、Graph 和 Clock，并增加 epoch。 |
| Temporal 绑定失败 | [`ConfirmBindingFailurePreservesAuthorityAndRequiresCorrection`](../Tests/Runtime/StateGraphHost/StateGraphHostTemporalBindingPlayModeTests.cs) | P/M/I | 绑定拒绝或抛错时保留原权威，并允许后续修正。 |

## Editor 选跑

将 `UNITY_EDITOR` 指向对应版本的 Unity 可执行文件。将 `UNITY_PROJECT` 指向挂载待测包体且启用了 CoCoFlow `testables` 的宿主。串行运行，保留两份 XML。

```sh
edit_cases='HighestRequestedPriorityWinsAndUnrequestedEdgesAreSkipped|OperationFrameDiscreteWinnerGetsOneSequenceAtFinalizeAndCommit|FixedLegacyGraphRetainsContentFingerprint'
play_cases='ReaderRawIntentFlowsIntoStateLogicPerTick|TransitionCommitCarriesEveryGraphStateRecord|FirstTickReadsLayoutDefaultsAndCommitsRevisionOne|IgnoredUnauthorizedOutcomeWriteFaultsAndPreservesCommittedRevision|IgnoredInvalidOutboxWriteFaultsAndPublishesNothing|PublishSeesCompleteAuthorityAndDefersStopDisposeUntilAllPacketsFinish|SaveGameLoadsDurableContextIntoSiblingAndReseedsHistory|InvalidPayloadAndRejectedBindingNeverPublishAuthority|EmptyLegacyContainerSectionsDoNotChangeGraphPayloadOrTheWholeDocumentVersion|PreviewAndCancelRunNoStateGraphOrAuthorityWork|ConfirmRestoresContextGraphClockAndCreatesNewEpochBranch|ConfirmBindingFailurePreservesAuthorityAndRequiresCorrection'

"$UNITY_EDITOR" -batchmode -nographics -projectPath "$UNITY_PROJECT" -runTests \
  -testPlatform EditMode -testFilter "($edit_cases)(\\(|$)" \
  -testResults "$PWD/stateflow-editmode.xml"
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$UNITY_PROJECT" -runTests \
  -testPlatform PlayMode -testFilter "($play_cases)(\\(|$)" \
  -testResults "$PWD/stateflow-playmode.xml"
```

检查两份 XML 是否包含表中对应模式的全部方法，并检查每个参数化用例的结果。Windows Player 按完整版本门禁执行，再用表中方法名从对应 XML 定位结果。

## P003 补充验证

- `ReaderRawIntentFlowsIntoStateLogicPerTick` 在 State 已读到第二次短按之后拒绝该 Tick，核对 Context revision 保持；再让 InputSystem 执行清理，从正式 InputIntent 重采样仍得到原来的两个序号。
- `PublishSeesCompleteAuthorityAndDefersStopDisposeUntilAllPacketsFinish` 先成功提交不发包的 Tick，再验证首批包的序号 1、2，以及监听器读到的 Tick 和 revision 均为 2。
- `LoadsGraphAndSaveCapturedBeforeAssemblyMigration` 在 Editor PlayMode 导入提交 `3dff4e7` 的图和原始存档，验证同一实体恢复值 20、Tick 2，再推进到 Tick 3。

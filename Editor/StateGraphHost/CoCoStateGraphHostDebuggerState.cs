using System;
using System.Collections.Generic;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    /// <summary>提交快照新鲜度（N2）：Fresh=本次刷新成功；RetainedStale=刷新失败保留上次；None=从未成功。</summary>
    internal enum CoCoDebuggerSnapshotFreshness
    {
        None = 0,
        Fresh = 1,
        RetainedStale = 2
    }

    internal enum CoCoStateGraphHostTraceFilterMode
    {
        All = 0,
        StateId = 1,
        TransitionId = 2
    }

    /// <summary>快照投影行：分区 + 键 + 值。</summary>
    internal readonly struct CoCoDebuggerSnapshotRow
    {
        internal CoCoDebuggerSnapshotRow(string section, string key, string value)
        {
            Section = section;
            Key = key;
            Value = value;
        }

        internal string Section { get; }
        internal string Key { get; }
        internal string Value { get; }
    }

    /// <summary>Trace 投影行：组标题或条目文本。</summary>
    internal readonly struct CoCoDebuggerTraceRow
    {
        internal CoCoDebuggerTraceRow(bool isGroupHeader, string text)
        {
            IsGroupHeader = isGroupHeader;
            Text = text;
        }

        internal bool IsGroupHeader { get; }
        internal string Text { get; }
    }

    /// <summary>
    /// Runtime Debugger 数据层（D6/D11）：宿主身份观察、提交快照保留与新鲜度、
    /// Trace 过滤与分组投影。只读投影，不操作 Host 生命周期；刷新失败保留上次
    /// 已提交快照并标记 RetainedStale。
    /// </summary>
    internal sealed class CoCoStateGraphHostDebuggerState
    {
        internal const int MaximumVisibleTraceEntries = 128;

        private static readonly CoCoStateFlowTraceKind TransitionGroupKinds =
            CoCoStateFlowTraceKind.Transition | CoCoStateFlowTraceKind.ActivePath;
        private static readonly CoCoStateFlowTraceKind OperationGroupKinds =
            CoCoStateFlowTraceKind.OperationSection | CoCoStateFlowTraceKind.OperatorOutcome;
        private static readonly CoCoStateFlowTraceKind ContextCommitGroupKinds =
            CoCoStateFlowTraceKind.Tick |
            CoCoStateFlowTraceKind.ContextCommit |
            CoCoStateFlowTraceKind.Diagnostic |
            CoCoStateFlowTraceKind.Cancelled;
        private static readonly CoCoStateFlowTraceKind EventGroupKinds =
            CoCoStateFlowTraceKind.EventSequence | CoCoStateFlowTraceKind.EventPublished;

        private CoCoStateGraphHostDebugSnapshot _snapshot;
        private CoCoDiagnostic _lastRefreshDiagnostic;
        private CoCoStateFlowTraceEntry[] _traceEntries = Array.Empty<CoCoStateFlowTraceEntry>();
        private CoCoStateGraphHostTraceFilterMode _traceFilterMode;
        private string _traceFilterText = string.Empty;
        private EntityId _observedHostId;
        private CoCoGraphInstanceId _observedGraphInstanceId;
        private CoCoDebuggerSnapshotFreshness _freshness;
        private int _visibleTraceCount;

        internal CoCoDebuggerSnapshotFreshness Freshness => _freshness;
        internal CoCoStateGraphHostDebugSnapshot Snapshot => _snapshot;
        internal CoCoDiagnostic LastRefreshDiagnostic => _lastRefreshDiagnostic;
        internal CoCoStateGraphHostTraceFilterMode TraceFilterMode => _traceFilterMode;
        internal string TraceFilterText => _traceFilterText;
        internal int VisibleTraceCount => _visibleTraceCount;

        /// <summary>当前过滤文本是否有效（无效时 UI 显示校验消息并暂停投影）。</summary>
        internal string TraceFilterValidationMessage { get; private set; } = string.Empty;

        /// <summary>宿主身份观察：宿主或 Graph 实例变化时重置全部观察状态（语义保持）。</summary>
        internal void ObserveIdentity(CoCoStateGraphHost host)
        {
            EntityId hostId = host == null ? default : GetObjectEntityId(host);
            CoCoGraphInstanceId graphInstanceId =
                host == null ? default : host.GraphInstanceId;
            if (_observedHostId == hostId &&
                _observedGraphInstanceId == graphInstanceId)
            {
                return;
            }

            _observedHostId = hostId;
            _observedGraphInstanceId = graphInstanceId;
            _snapshot = null;
            _lastRefreshDiagnostic = CoCoDiagnostic.None;
            _traceEntries = Array.Empty<CoCoStateFlowTraceEntry>();
            _traceFilterMode = CoCoStateGraphHostTraceFilterMode.All;
            _traceFilterText = string.Empty;
            TraceFilterValidationMessage = string.Empty;
            _freshness = CoCoDebuggerSnapshotFreshness.None;
            _visibleTraceCount = 0;
        }

        /// <summary>
        /// 拉取一次提交快照。成功 → Fresh；失败 → 保留上次快照并标 RetainedStale
        /// （从未成功则保持 None），失败诊断供 UI 展示。
        /// </summary>
        internal bool TryRefresh(CoCoStateGraphHost host)
        {
            if (host == null)
            {
                return false;
            }

            if (host.TryCaptureDebugSnapshot(
                    out CoCoStateGraphHostDebugSnapshot snapshot,
                    out CoCoDiagnostic diagnostic))
            {
                _snapshot = snapshot;
                _lastRefreshDiagnostic = CoCoDiagnostic.None;
                _freshness = CoCoDebuggerSnapshotFreshness.Fresh;
                return true;
            }

            _lastRefreshDiagnostic = diagnostic;
            if (_snapshot != null)
            {
                _freshness = CoCoDebuggerSnapshotFreshness.RetainedStale;
            }

            return false;
        }

        /// <summary>设置过滤模式与文本（仅存储；有效性由 TryBuildTraceFilter 判定）。</summary>
        internal void SetTraceFilter(
            CoCoStateGraphHostTraceFilterMode mode,
            string text)
        {
            _traceFilterMode = mode;
            _traceFilterText = text ?? string.Empty;
        }

        /// <summary>
        /// 从 Host Trace 拉取可见条目（容量内最新 128 条，过滤后）。
        /// Trace 为 null（容量 0）时可见数为 0。
        /// </summary>
        internal void PullTrace(CoCoStateGraphHost host)
        {
            _visibleTraceCount = 0;
            ICoCoStateFlowTrace trace = host == null ? null : host.Trace;
            if (trace == null)
            {
                _traceEntries = Array.Empty<CoCoStateFlowTraceEntry>();
                return;
            }

            if (!TryBuildTraceFilter(
                    _traceFilterMode,
                    _traceFilterText,
                    out CoCoStateFlowTraceFilter filter,
                    out string validationMessage))
            {
                TraceFilterValidationMessage = validationMessage;
                return;
            }

            TraceFilterValidationMessage = string.Empty;
            int capacity = Math.Min(trace.Count, MaximumVisibleTraceEntries);
            if (_traceEntries.Length != capacity)
            {
                _traceEntries = capacity == 0
                    ? Array.Empty<CoCoStateFlowTraceEntry>()
                    : new CoCoStateFlowTraceEntry[capacity];
            }

            _visibleTraceCount = trace.CopyLatestTo(_traceEntries, filter);
        }

        /// <summary>Trace 计数投影（count/capacity/totalWritten/visible）。</summary>
        internal void GetTraceCounts(
            CoCoStateGraphHost host,
            out int count,
            out int capacity,
            out ulong totalWritten,
            out int visible)
        {
            ICoCoStateFlowTrace trace = host == null ? null : host.Trace;
            count = trace?.Count ?? 0;
            capacity = trace?.Capacity ?? 0;
            totalWritten = trace?.TotalWritten ?? 0UL;
            visible = _visibleTraceCount;
        }

        /// <summary>快照 → 分区键值行投影（Identity/Timeline/Runtime/Context/Layers/Claims）。</summary>
        internal List<CoCoDebuggerSnapshotRow> BuildSnapshotRows()
        {
            var rows = new List<CoCoDebuggerSnapshotRow>();
            CoCoStateGraphHostDebugSnapshot snapshot = _snapshot;
            if (snapshot == null)
            {
                return rows;
            }

            string sectionIdentity = CoCoEditorLocalization.Text("Identity", "标识");
            string sectionTimeline = CoCoEditorLocalization.Text("Timeline", "时间线");
            string sectionRuntime = CoCoEditorLocalization.Text("Runtime", "运行时");
            string sectionContext = CoCoEditorLocalization.Text("Context", "上下文");
            string sectionLayers = CoCoEditorLocalization.Text("Layers", "层");
            string sectionClaims = CoCoEditorLocalization.Text("Claims", "占用");

            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionIdentity, "Graph", snapshot.GraphId.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionIdentity, "Instance", snapshot.GraphInstanceId.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionIdentity, "Schema", snapshot.SchemaVersion.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionIdentity, "Content Fingerprint",
                snapshot.ContentFingerprint.ToString("X16")));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionIdentity, "Catalog Fingerprint",
                snapshot.CatalogFingerprint.ToString("X16")));

            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionTimeline, "Timeline", snapshot.TimelineId.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionTimeline, "Clock Domain", snapshot.ClockDomainId.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionTimeline, "Epoch", snapshot.TimelineEpoch.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionTimeline, "Tick", snapshot.Tick.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionTimeline, "Sequence", snapshot.ExecutionSequence.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionTimeline, "Seconds", snapshot.Seconds.ToString("0.######")));

            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionRuntime, "Lifecycle", snapshot.Lifecycle.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionRuntime, "Fault", snapshot.Fault.IsFaulted
                    ? snapshot.Fault.Diagnostic.Message
                    : CoCoEditorLocalization.Text("none", "无")));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionRuntime, "World Correction",
                snapshot.RequiresWorldCorrection
                    ? CoCoEditorLocalization.Text("required", "需要")
                    : CoCoEditorLocalization.Text("none", "无")));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionRuntime, "Last Diagnostic",
                snapshot.LastDiagnostic.Domain + "/" + snapshot.LastDiagnostic.Code +
                    ": " + snapshot.LastDiagnostic.Message));

            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionContext, "Revision", snapshot.ContextRevision.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionContext, "Origin", snapshot.ContextOrigin.Kind.ToString()));
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionContext, "Claims", snapshot.ClaimCount.ToString()));
            CoCoStateFlowFrameHeader contextHeader = snapshot.ContextHeader;
            rows.Add(new CoCoDebuggerSnapshotRow(
                sectionContext,
                "Header",
                contextHeader.IsValid
                    ? contextHeader.Identity.GraphInstanceId +
                      "; Kind " + contextHeader.Identity.Kind +
                      "; Tick " + contextHeader.Identity.Tick +
                      "; Sequence " + contextHeader.Identity.ExecutionSequence +
                      "; Layout " + contextHeader.LayoutId
                    : "<" + CoCoEditorLocalization.Text("none", "无") + ">"));

            for (int layerIndex = 0; layerIndex < snapshot.LayerCount; layerIndex++)
            {
                CoCoStateGraphHostDebugLayer layer = snapshot.GetLayer(layerIndex);
                rows.Add(new CoCoDebuggerSnapshotRow(
                    sectionLayers,
                    layer.LayerId.ToString(),
                    CoCoEditorLocalization.Text("Winner", "胜出") + " " +
                        layer.WinningTransitionId));
                for (int stateIndex = 0; stateIndex < layer.ActiveStateCount; stateIndex++)
                {
                    CoCoStateGraphHostDebugActiveState state =
                        layer.GetActiveState(stateIndex);
                    rows.Add(new CoCoDebuggerSnapshotRow(
                        sectionLayers,
                        "  " + state.StateId,
                        "Activation " + state.ActivationId +
                            "; Local " + state.LocalSeconds.ToString("0.######") +
                            "; Progress " + state.ActionProgress.ToString("0.######")));
                }
            }

            for (int claimIndex = 0; claimIndex < snapshot.ClaimCount; claimIndex++)
            {
                CoCoOperatorClaimState claim = snapshot.GetClaim(claimIndex);
                rows.Add(new CoCoDebuggerSnapshotRow(
                    sectionClaims,
                    claim.ClaimId.ToString(),
                    "Section " + claim.SectionId +
                        "; " + (claim.IsHeld
                            ? CoCoEditorLocalization.Text("held", "持有")
                            : CoCoEditorLocalization.Text("free", "空闲")) +
                        "; Owner " + claim.OwnerOperatorId));
            }

            return rows;
        }

        /// <summary>可见 Trace 条目 → 分组行投影（Transition/Operation/Context Commit/Event；语义保持）。</summary>
        internal List<CoCoDebuggerTraceRow> BuildTraceRows()
        {
            var rows = new List<CoCoDebuggerTraceRow>();
            AppendTraceGroup(
                rows,
                CoCoEditorLocalization.Text("Transition", "转移"),
                TransitionGroupKinds);
            AppendTraceGroup(
                rows,
                CoCoEditorLocalization.Text("Operation", "操作"),
                OperationGroupKinds);
            AppendTraceGroup(
                rows,
                CoCoEditorLocalization.Text("Context Commit", "上下文提交"),
                ContextCommitGroupKinds);
            AppendTraceGroup(
                rows,
                CoCoEditorLocalization.Text("Event", "事件"),
                EventGroupKinds);
            return rows;
        }

        private void AppendTraceGroup(
            List<CoCoDebuggerTraceRow> rows,
            string label,
            CoCoStateFlowTraceKind kinds)
        {
            bool drewHeader = false;
            for (int index = 0; index < _visibleTraceCount; index++)
            {
                CoCoStateFlowTraceEntry entry = _traceEntries[index];
                if ((entry.Kind & kinds) == 0)
                {
                    continue;
                }

                if (!drewHeader)
                {
                    rows.Add(new CoCoDebuggerTraceRow(true, label));
                    drewHeader = true;
                }

                rows.Add(new CoCoDebuggerTraceRow(false, FormatTraceEntry(entry)));
            }
        }

        /// <summary>Trace 过滤器构造（三模式互斥；语义与校验消息保持）。</summary>
        internal static bool TryBuildTraceFilter(
            CoCoStateGraphHostTraceFilterMode mode,
            string text,
            out CoCoStateFlowTraceFilter filter,
            out string validationMessage)
        {
            switch (mode)
            {
                case CoCoStateGraphHostTraceFilterMode.All:
                    filter = CoCoStateFlowTraceFilter.All;
                    validationMessage = string.Empty;
                    return true;
                case CoCoStateGraphHostTraceFilterMode.StateId:
                    if (CoCoStateId.TryParse(
                            string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim(),
                            out CoCoStateId stateId))
                    {
                        filter = new CoCoStateFlowTraceFilter(
                            CoCoStateFlowTraceKind.All,
                            stateId: stateId);
                        validationMessage = string.Empty;
                        return true;
                    }

                    filter = default;
                    validationMessage = CoCoEditorLocalization.Text(
                        "State ID must be one non-zero 32-digit hexadecimal identity.",
                        "State ID 必须是一个非零 32 位十六进制标识。");
                    return false;
                case CoCoStateGraphHostTraceFilterMode.TransitionId:
                    if (CoCoTransitionId.TryParse(
                            string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim(),
                            out CoCoTransitionId transitionId))
                    {
                        filter = new CoCoStateFlowTraceFilter(
                            CoCoStateFlowTraceKind.All,
                            transitionId: transitionId);
                        validationMessage = string.Empty;
                        return true;
                    }

                    filter = default;
                    validationMessage = CoCoEditorLocalization.Text(
                        "Transition ID must be one non-zero 32-digit hexadecimal identity.",
                        "Transition ID 必须是一个非零 32 位十六进制标识。");
                    return false;
                default:
                    filter = default;
                    validationMessage = CoCoEditorLocalization.Text(
                        "Trace filter mode is invalid.",
                        "Trace 过滤模式无效。");
                    return false;
            }
        }

        private static string FormatTraceEntry(CoCoStateFlowTraceEntry entry)
        {
            string prefix = "  " + entry.Kind + " | Tick " + entry.TickFrame.Tick;
            switch (entry.Kind)
            {
                case CoCoStateFlowTraceKind.Transition:
                    return prefix + " | Layer " + entry.LayerId + " | Transition " +
                        entry.TransitionId + " | Role " + entry.TransitionRole;
                case CoCoStateFlowTraceKind.ActivePath:
                    return prefix + " | Layer " + entry.LayerId + " | State " +
                        entry.StateId;
                case CoCoStateFlowTraceKind.OperationSection:
                    return prefix + " | Section " + entry.SectionId;
                case CoCoStateFlowTraceKind.OperatorOutcome:
                    return prefix + " | Operator " + entry.OperatorId + " | Outcome " +
                        entry.OperatorOutcome;
                case CoCoStateFlowTraceKind.ContextCommit:
                    return prefix + " | Revision " + entry.PreviousRevision + " -> " +
                        entry.NewRevision;
                case CoCoStateFlowTraceKind.EventSequence:
                case CoCoStateFlowTraceKind.EventPublished:
                    return prefix + " | Event Sequence " + entry.FirstEventSequence +
                        " -> " + entry.LastEventSequence;
                case CoCoStateFlowTraceKind.Diagnostic:
                case CoCoStateFlowTraceKind.Cancelled:
                    return prefix + " | Diagnostic " + entry.DiagnosticDomain + "/" +
                        entry.DiagnosticCode;
                default:
                    return prefix;
            }
        }

        private static EntityId GetObjectEntityId(UnityEngine.Object value)
        {
#if UNITY_6000_5_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }
    }
}

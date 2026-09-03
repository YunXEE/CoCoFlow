using System;
using System.Collections.Generic;
using System.Globalization;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    internal enum CoCoDebuggerSnapshotFreshness
    {
        None = 0,
        Fresh = 1,
        RetainedStale = 2
    }

    internal readonly struct CoCoDebuggerKeyValueRow
    {
        internal CoCoDebuggerKeyValueRow(string key, string value)
        {
            Key = key;
            Value = value;
        }

        internal string Key { get; }
        internal string Value { get; }
    }

    internal readonly struct CoCoDebuggerActiveStateRow
    {
        internal CoCoDebuggerActiveStateRow(
            string layer,
            string state,
            string stateId,
            string winningTransition,
            double localSeconds,
            double actionProgress)
        {
            Layer = layer;
            State = state;
            StateId = stateId;
            WinningTransition = winningTransition;
            LocalSeconds = localSeconds;
            ActionProgress = actionProgress;
        }

        internal string Layer { get; }
        internal string State { get; }
        internal string StateId { get; }
        internal string WinningTransition { get; }
        internal double LocalSeconds { get; }
        internal double ActionProgress { get; }
    }

    internal readonly struct CoCoDebuggerPersistedFrame
    {
        internal CoCoDebuggerPersistedFrame(
            CoCoTemporalFrameInfo frame,
            int slotIndex,
            DateTimeOffset updatedUtc)
        {
            Frame = frame;
            SlotIndex = slotIndex;
            UpdatedUtc = updatedUtc;
        }

        internal CoCoTemporalFrameInfo Frame { get; }
        internal int SlotIndex { get; }
        internal DateTimeOffset UpdatedUtc { get; }
        internal bool IsValid => Frame.IsValid && SlotIndex >= 0;
    }

    /// <summary>
    /// Read-only projection state for one concrete Host. It keeps the last
    /// successfully copied committed snapshot when a transient refresh is
    /// rejected, but never invokes a Runtime operation.
    /// </summary>
    internal sealed class CoCoStateGraphHostDebuggerState
    {
        private readonly Dictionary<CoCoLayerId, string> _layerNames =
            new Dictionary<CoCoLayerId, string>();
        private readonly Dictionary<CoCoStateId, string> _stateNames =
            new Dictionary<CoCoStateId, string>();

        private CoCoStateGraphHostTemporalDebugSnapshot _snapshot;
        private CoCoDiagnostic _lastRefreshDiagnostic;
        private CoCoDebuggerPersistedFrame _persistedFrame;
        private string _persistenceFailure = string.Empty;
        private EntityId _observedHostEntityId;
        private CoCoGraphInstanceId _observedGraphInstanceId;
        private CoCoDebuggerSnapshotFreshness _freshness;
        private int _selectedDepth;
        private CoCoStateGraphAsset _namesAsset;

        internal CoCoStateGraphHostTemporalDebugSnapshot Snapshot => _snapshot;
        internal CoCoDebuggerSnapshotFreshness Freshness => _freshness;
        internal CoCoDiagnostic LastRefreshDiagnostic => _lastRefreshDiagnostic;
        internal bool HasPersistedFrame => _persistedFrame.IsValid;
        internal string PersistenceFailure => _persistenceFailure;
        internal int SelectedDepth => _selectedDepth;

        internal void ObserveIdentity(CoCoStateGraphHost host)
        {
            EntityId hostEntityId = host == null
                ? default
                : GetObjectEntityId(host);
            CoCoGraphInstanceId graphInstanceId =
                host == null ? default : host.GraphInstanceId;
            if (_observedHostEntityId == hostEntityId &&
                _observedGraphInstanceId == graphInstanceId)
            {
                return;
            }

            _observedHostEntityId = hostEntityId;
            _observedGraphInstanceId = graphInstanceId;
            _snapshot = null;
            _lastRefreshDiagnostic = CoCoDiagnostic.None;
            _persistedFrame = default;
            _persistenceFailure = string.Empty;
            _freshness = CoCoDebuggerSnapshotFreshness.None;
            _selectedDepth = 0;
            ClearNameCache();
        }

        internal bool TryRefresh(CoCoStateGraphHost host)
        {
            if (host == null)
            {
                return false;
            }

            if (host.TryCaptureTemporalDebugSnapshot(
                    out CoCoStateGraphHostTemporalDebugSnapshot snapshot,
                    out CoCoDiagnostic diagnostic))
            {
                _snapshot = snapshot;
                _lastRefreshDiagnostic = CoCoDiagnostic.None;
                _freshness = CoCoDebuggerSnapshotFreshness.Fresh;
                _selectedDepth = snapshot.Count == 0
                    ? 0
                    : Mathf.Clamp(_selectedDepth, 0, snapshot.Count - 1);
                EnsureNameCache(host.StateGraphAsset);
                return true;
            }

            _lastRefreshDiagnostic = diagnostic;
            if (_snapshot != null)
            {
                _freshness = CoCoDebuggerSnapshotFreshness.RetainedStale;
            }

            return false;
        }

        internal bool TryRefreshPersistence(CoCoStateGraphHost host)
        {
            _persistenceFailure = string.Empty;
            string failure = string.Empty;
            if (_snapshot == null ||
                !CoCoStateGraphPersistenceDebugReader.TryReadLatestPersistedFrame(
                    host,
                    out CoCoPersistedFrameDebugInfo info,
                    out failure))
            {
                _persistedFrame = default;
                _persistenceFailure = failure ?? string.Empty;
                return false;
            }

            _persistedFrame = new CoCoDebuggerPersistedFrame(
                info.SourceFrame,
                info.SlotIndex,
                info.UpdatedUtc);
            return true;
        }

        internal void SelectDepth(int depth)
        {
            if (_snapshot == null || _snapshot.Count == 0)
            {
                _selectedDepth = 0;
                return;
            }

            _selectedDepth = Mathf.Clamp(depth, 0, _snapshot.Count - 1);
        }

        internal bool TryGetSelectedFrame(out CoCoTemporalFrameInfo frame)
        {
            if (_snapshot == null ||
                _snapshot.Count == 0 ||
                _selectedDepth < 0 ||
                _selectedDepth >= _snapshot.Count)
            {
                frame = default;
                return false;
            }

            frame = _snapshot.GetFrame(_selectedDepth);
            return frame.IsValid;
        }

        internal int FindPersistedDepth()
        {
            if (_snapshot == null || !_persistedFrame.IsValid)
            {
                return -1;
            }

            for (int depth = 0; depth < _snapshot.Count; depth++)
            {
                if (IsSameFrame(
                        _snapshot.GetFrame(depth),
                        _persistedFrame.Frame))
                {
                    return depth;
                }
            }

            return -1;
        }

        internal List<CoCoDebuggerKeyValueRow> BuildCurrentFrameRows()
        {
            var rows = new List<CoCoDebuggerKeyValueRow>();
            if (_snapshot == null)
            {
                return rows;
            }

            AppendFrameRows(
                rows,
                _snapshot.ContextHeader.Identity.GraphInstanceId,
                _snapshot.ContextHeader.TickFrame,
                _snapshot.ContextRevision,
                _snapshot.ContextOrigin);
            return rows;
        }

        internal List<CoCoDebuggerKeyValueRow> BuildSelectedFrameRows()
        {
            var rows = new List<CoCoDebuggerKeyValueRow>();
            if (!TryGetSelectedFrame(out CoCoTemporalFrameInfo frame))
            {
                return rows;
            }

            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("History depth", "历史深度"),
                _selectedDepth.ToString(CultureInfo.InvariantCulture)));
            AppendFrameRows(
                rows,
                frame.GraphInstanceId,
                frame.TickFrame,
                frame.Revision,
                frame.Origin);
            return rows;
        }

        internal List<CoCoDebuggerKeyValueRow> BuildPersistedFrameRows()
        {
            var rows = new List<CoCoDebuggerKeyValueRow>();
            if (!_persistedFrame.IsValid)
            {
                return rows;
            }

            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Save slot", "存档槽位"),
                _persistedFrame.SlotIndex.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Written at (UTC)", "写盘时间（UTC）"),
                _persistedFrame.UpdatedUtc
                    .ToUniversalTime()
                    .ToString("u", CultureInfo.InvariantCulture)));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text(
                    "Written at (Local)",
                    "写盘时间（本地）"),
                _persistedFrame.UpdatedUtc
                    .ToLocalTime()
                    .ToString(
                        "yyyy-MM-dd HH:mm:ss zzz",
                        CultureInfo.InvariantCulture)));
            AppendFrameRows(
                rows,
                _persistedFrame.Frame.GraphInstanceId,
                _persistedFrame.Frame.TickFrame,
                _persistedFrame.Frame.Revision,
                _persistedFrame.Frame.Origin);
            return rows;
        }

        internal List<CoCoDebuggerActiveStateRow> BuildActiveStateRows()
        {
            var rows = new List<CoCoDebuggerActiveStateRow>();
            if (_snapshot == null)
            {
                return rows;
            }

            for (int layerIndex = 0;
                 layerIndex < _snapshot.LayerCount;
                 layerIndex++)
            {
                CoCoStateGraphHostDebugLayer layer =
                    _snapshot.GetLayer(layerIndex);
                string layerName = ResolveLayerName(layer.LayerId);
                string winningTransition = layer.WinningTransitionId.IsValid
                    ? layer.WinningTransitionId.ToString()
                    : "—";
                for (int stateIndex = 0;
                     stateIndex < layer.ActiveStateCount;
                     stateIndex++)
                {
                    CoCoStateGraphHostDebugActiveState state =
                        layer.GetActiveState(stateIndex);
                    rows.Add(new CoCoDebuggerActiveStateRow(
                        layerName,
                        ResolveStateName(state.StateId),
                        state.StateId.ToString(),
                        winningTransition,
                        state.LocalSeconds,
                        state.ActionProgress));
                }
            }

            return rows;
        }

        internal static bool IsSameFrame(
            in CoCoTemporalFrameInfo left,
            in CoCoTemporalFrameInfo right) =>
            left.IsValid &&
            right.IsValid &&
            left.GraphInstanceId == right.GraphInstanceId &&
            left.TickFrame == right.TickFrame &&
            left.Revision == right.Revision;

        internal void SeedSnapshotForTests(
            CoCoStateGraphHostTemporalDebugSnapshot snapshot)
        {
            _snapshot = snapshot;
            _selectedDepth = 0;
        }

        internal void SeedPersistedFrameForTests(
            CoCoTemporalFrameInfo frame,
            int slotIndex,
            DateTimeOffset updatedUtc)
        {
            _persistedFrame = new CoCoDebuggerPersistedFrame(
                frame,
                slotIndex,
                updatedUtc);
        }

        private void EnsureNameCache(CoCoStateGraphAsset asset)
        {
            if (_namesAsset == asset)
            {
                return;
            }

            ClearNameCache();
            _namesAsset = asset;
            if (asset == null)
            {
                return;
            }

            var serializedAsset = new SerializedObject(asset);
            serializedAsset.Update();
            SerializedProperty layers = serializedAsset.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                return;
            }

            for (int layerIndex = 0;
                 layerIndex < layers.arraySize;
                 layerIndex++)
            {
                SerializedProperty layer =
                    layers.GetArrayElementAtIndex(layerIndex);
                string layerName = layer.FindPropertyRelative("displayName")
                    ?.stringValue;
                if (TryReadLayerId(
                        layer.FindPropertyRelative("layerId"),
                        out CoCoLayerId layerId))
                {
                    _layerNames[layerId] = string.IsNullOrWhiteSpace(layerName)
                        ? ShortId(layerId.ToString())
                        : layerName;
                }

                SerializedProperty states = layer.FindPropertyRelative("states");
                if (states == null || !states.isArray)
                {
                    continue;
                }

                for (int stateIndex = 0;
                     stateIndex < states.arraySize;
                     stateIndex++)
                {
                    SerializedProperty state =
                        states.GetArrayElementAtIndex(stateIndex);
                    string stateName = state.FindPropertyRelative("displayName")
                        ?.stringValue;
                    if (TryReadStateId(
                            state.FindPropertyRelative("stateId"),
                            out CoCoStateId stateId))
                    {
                        _stateNames[stateId] = string.IsNullOrWhiteSpace(stateName)
                            ? ShortId(stateId.ToString())
                            : stateName;
                    }
                }
            }
        }

        private void ClearNameCache()
        {
            _namesAsset = null;
            _layerNames.Clear();
            _stateNames.Clear();
        }

        private string ResolveLayerName(CoCoLayerId id) =>
            _layerNames.TryGetValue(id, out string name)
                ? name
                : ShortId(id.ToString());

        private string ResolveStateName(CoCoStateId id) =>
            _stateNames.TryGetValue(id, out string name)
                ? name
                : ShortId(id.ToString());

        private static void AppendFrameRows(
            ICollection<CoCoDebuggerKeyValueRow> rows,
            CoCoGraphInstanceId graphInstanceId,
            in CoCoTickFrame tickFrame,
            CoCoContextRevision revision,
            CoCoContextFrameOrigin origin)
        {
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Graph Instance", "Graph 实例"),
                graphInstanceId.IsValid ? graphInstanceId.ToString() : "—"));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Tick", "Tick"),
                tickFrame.Tick.Value.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Timeline position", "时间线位置"),
                tickFrame.TimelinePosition.Seconds.ToString(
                    "0.### s",
                    CultureInfo.InvariantCulture)));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Delta Time", "帧间隔"),
                tickFrame.DeltaTime.ToString(
                    "0.##### s",
                    CultureInfo.InvariantCulture)));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Timeline ID", "时间线 ID"),
                tickFrame.TimelineId.IsValid ? tickFrame.TimelineId.ToString() : "—"));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Clock Domain", "时钟域"),
                tickFrame.ClockDomainId.IsValid
                    ? tickFrame.ClockDomainId.ToString()
                    : "—"));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Timeline Epoch", "时间线 Epoch"),
                tickFrame.TimelineEpoch.Value.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Execution Sequence", "执行序列"),
                tickFrame.ExecutionSequence.Value.ToString(
                    CultureInfo.InvariantCulture)));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Context Revision", "Context Revision"),
                revision.Value.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new CoCoDebuggerKeyValueRow(
                CoCoEditorLocalization.Text("Origin", "来源"),
                FormatOrigin(origin)));
        }

        private static string FormatOrigin(CoCoContextFrameOrigin origin)
        {
            if (origin.Kind == CoCoContextFrameOriginKind.Commit)
            {
                return CoCoEditorLocalization.Text("Commit", "提交");
            }

            if (origin.Kind == CoCoContextFrameOriginKind.Restore)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    CoCoEditorLocalization.Text(
                        "Restore · Graph {0} · Tick {1} · Revision {2}",
                        "恢复 · Graph {0} · Tick {1} · Revision {2}"),
                    origin.SourceGraphInstanceId,
                    origin.SourceTick.Value,
                    origin.SourceRevision.Value);
            }

            return "—";
        }

        private static bool TryReadLayerId(
            SerializedProperty property,
            out CoCoLayerId id)
        {
            ReadSerializedId(property, out ulong high, out ulong low);
            return CoCoLayerId.TryCreate(high, low, out id);
        }

        private static bool TryReadStateId(
            SerializedProperty property,
            out CoCoStateId id)
        {
            ReadSerializedId(property, out ulong high, out ulong low);
            return CoCoStateId.TryCreate(high, low, out id);
        }

        private static void ReadSerializedId(
            SerializedProperty property,
            out ulong high,
            out ulong low)
        {
            SerializedProperty highProperty =
                property?.FindPropertyRelative("high");
            SerializedProperty lowProperty =
                property?.FindPropertyRelative("low");
            high = highProperty == null
                ? 0UL
                : unchecked((ulong)highProperty.longValue);
            low = lowProperty == null
                ? 0UL
                : unchecked((ulong)lowProperty.longValue);
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "—";
            }

            return id.Length <= 8 ? id : id.Substring(id.Length - 8);
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

using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>选择由 Update、FixedUpdate 或显式调用推进此宿主。</summary>
    internal enum StateGraphDriver
    {
        Update = 0,
        FixedUpdate = 1,
        Manual = 2
    }

    /// <summary>将场景组件绑定到一个独立的图实例，持有该实例的事务、事件订阅与历史。</summary>
    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceClassName: "CoCoStateGraphHost")]
    [DisallowMultipleComponent]
    public sealed class StateGraphHost : MonoBehaviour
    {
        #region Public API

        /// <summary>Inspector 中配置的图资产，各实例分别持有运行状态。</summary>
        public StateGraphAsset StateGraphAsset => stateGraphAsset;

        /// <summary>Inspector 设置的历史帧容量，零表示关闭历史记录。</summary>
        public int TemporalHistoryCapacity => temporalHistoryCapacity;

        /// <summary>此宿主的实例当前所处的运行阶段。</summary>
        public RuntimeLifecycleState Lifecycle => _instance?.Lifecycle ??
            (_isDisposed
                ? RuntimeLifecycleState.Disposed
                : _hasStoppedInstance
                    ? RuntimeLifecycleState.Stopped
                    : RuntimeLifecycleState.Created);

        /// <summary>此实例已锁存的故障，健康或尚未启动时为默认值。</summary>
        public RuntimeFault Fault => _instance?.Fault ?? default;

        /// <summary>图实例的身份。Host 重新启动时分配新值。</summary>
        public GraphInstanceId GraphInstanceId => _instance?.GraphInstanceId ?? default;

        /// <summary>当前提交的各层活动路径，宿主尚无实例时为空。</summary>
        public IReadOnlyList<ActivePath> ActivePaths => _instance?.ActivePaths ?? Array.Empty<ActivePath>();

        /// <summary>最近一次成功提交的 Context，首次提交前为空。跨步持有时先调用 Retain。</summary>
        public ContextFrame CurrentContext => _transaction?.CurrentContext ?? default;

        /// <summary>此实例的运行跟踪，宿主未启动或已停止时为空。</summary>
        public IStateFlowTrace Trace => _transaction?.Trace;

        /// <summary>场景可能已被失败的回调修改且需要按当前权威帧校正时为真。</summary>
        public bool RequiresWorldCorrection => _requiresWorldCorrection;

        /// <summary>最近一次宿主操作记录的诊断，成功时为无诊断。</summary>
        public CoCoDiagnostic LastDiagnostic => _lastDiagnostic;

        /// <summary>输入权威的代次，可据此识别已过期的输入。</summary>
        public ulong InputAuthorityRevision => _inputAuthorityRevision;

        /// <summary>返回此刻的历史模式、当前帧和预览帧。继续步进后可重新读取。</summary>
        public HostHistoryState TemporalState => _temporal?.ReadState(HostHistoryFrame.From(CurrentContext),
            _temporal.HasOlderSelection && !Fault.IsFaulted && _restore.IsLive &&
            (_restore.Participant == null || _restore.Participant.CanConfirmPreview(_temporal.PreviewDepth))) ??
            new HostHistoryState(
                HostHistoryMode.Disabled,
                temporalHistoryCapacity < 0 ? 0 : temporalHistoryCapacity,
                0,
                0,
                default,
                default,
                0UL,
                false);

        /// <summary>从创建或停止状态启动独立实例。完成绑定、预检和事件订阅后返回。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>完成启动时为真。缺失依赖、启动被取消或生命周期不允许时为假。</returns>
        public bool TryStart(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            _isStarting = true;
            try
            {
                bool started = TryStartCore(out diagnostic);
                if (_destroyRequested)
                {
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled StateGraph Host startup before publication.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                return started;
            }
            finally
            {
                _isStarting = false;
                if (_destroyRequested)
                {
                    _destroyRequested = false;
                    ForceDisposeHost();
                }
            }
        }

        /// <summary>暂停正在运行的健康实例，保留当前提交帧。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>已接受暂停时为真，实例状态或未解决的故障可能阻止暂停。</returns>
        public bool TrySuspend(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            bool succeeded = TrySuspendCore(true, out diagnostic);
            _lastDiagnostic = diagnostic;
            return succeeded;
        }

        /// <summary>在实例健康且没有待处理可靠事件溢出时恢复运行。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>成功从暂停状态恢复时为真。</returns>
        public bool TryResume(out CoCoDiagnostic diagnostic)
        {
            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            bool succeeded = TryResumeCore(true, out diagnostic);
            _lastDiagnostic = diagnostic;
            return succeeded;
        }

        /// <summary>停止实例并释放其运行资源。发布已提交事件期间可安排在发布结束后停止。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>停止已完成或在事件发布结束后获准执行时为真。</returns>
        public bool TryStop(out CoCoDiagnostic diagnostic)
        {
            if (_isPublishingCommittedEvents)
            {
                if (_instance == null ||
                    (_instance.Lifecycle != RuntimeLifecycleState.Running &&
                     _instance.Lifecycle != RuntimeLifecycleState.Suspended))
                {
                    diagnostic = LifecycleError(
                        "Host has no live Graph instance to stop after committed Event publication.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _stopAfterPublish = true;
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }

            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            if (_instance == null ||
                (_instance.Lifecycle != RuntimeLifecycleState.Running &&
                 _instance.Lifecycle != RuntimeLifecycleState.Suspended))
            {
                diagnostic = LifecycleError("Host has no live Graph instance to stop.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            // Close the Inbox before stopping the instance.
            _acceptsEventInput = false;
            if (!_instance.TryStop(out diagnostic))
            {
                _acceptsEventInput = true;
                _lastDiagnostic = diagnostic;
                return false;
            }

            AdvanceInputAuthorityRevision();
            DisposeInstance();
            _hasStoppedInstance = true;
            _lastDiagnostic = diagnostic;
            return !diagnostic.IsError;
        }

        /// <summary>释放宿主。发布已提交事件期间，在已接受停止请求后可安排延迟释放。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>释放已完成或已获准延迟执行时为真。</returns>
        public bool TryDispose(out CoCoDiagnostic diagnostic)
        {
            if (_isPublishingCommittedEvents)
            {
                if (!_stopAfterPublish)
                {
                    diagnostic = LifecycleError(
                        "A live Host must first accept Stop before Dispose can be deferred from Event publication.");
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _disposeAfterPublish = true;
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }

            if (RejectLifecycleReentry(out diagnostic))
            {
                return false;
            }

            if (_instance != null || _isDisposed)
            {
                diagnostic = LifecycleError(
                    "Host can be disposed only from Created or Stopped; stop a live Graph instance first.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            DisposeHostFromLegalState();
            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        /// <summary>用 Manual 驱动同步执行一个完整步长，包括提交和已提交事件的发布。</summary>
        /// <param name="deltaTime">本步的时间增量，须为大于零的有限秒数。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>整步提交并完成发布时为真。提交前失败会撤销候选，已提交后的发布失败仍返回假。</returns>
        public bool TryStep(double deltaTime, out CoCoDiagnostic diagnostic)
        {
            if (driver != StateGraphDriver.Manual)
            {
                diagnostic = LifecycleError("Public Manual Step requires the Host Manual driver.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            return TryAdvance(deltaTime, out diagnostic);
        }

        /// <summary>以当前权威帧为起点开始预览，并清理待处理的场景输入。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>成功进入预览时为真，需要至少保留当前帧及一份较早帧。</returns>
        public bool TryBeginTemporalPreview(out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Begin, 0, out diagnostic);

        /// <summary>将所选历史帧应用到场景，保留已提交 Tick。</summary>
        /// <param name="historyDepth">从当前帧向过去计算的深度。零表示当前帧。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>选择有效且场景投影成功时为真。</returns>
        public bool TryPreviewTemporal(
            int historyDepth,
            out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Preview, historyDepth, out diagnostic);

        /// <summary>在选中的较早历史帧创建新时间分支，提交恢复结果。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>图、时钟和场景恢复均成功并建立新分支时为真。</returns>
        public bool TryConfirmTemporalRestore(out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Confirm, 0, out diagnostic);

        /// <summary>用当前权威帧恢复场景并结束预览。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>退出预览并恢复当前权威投影时为真。</returns>
        public bool TryCancelTemporalPreview(out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Cancel, 0, out diagnostic);

        /// <summary>在实例发生可校正故障后按已提交帧重新应用场景状态。</summary>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        /// <returns>当前有待校正故障且场景成功恢复时为真。</returns>
        public bool TryCorrectWorld(out CoCoDiagnostic diagnostic) =>
            TryRunTemporalOperation(TemporalOperation.Correct, 0, out diagnostic);

        /// <summary>检查来源 Context 与恢复时钟是否能用于此实例。</summary>
        /// <param name="source">仍可读取的已提交来源帧。</param>
        /// <param name="resumedTickFrame">要与源帧一起校验的恢复时钟。</param>
        /// <param name="status">返回来源、布局和恢复时钟的校验结果。</param>
        /// <returns>源帧、目标时钟与本实例匹配时为真。</returns>
        public bool TryValidateRestore(
            ContextFrame source,
            TickFrame resumedTickFrame,
            out ContextCommitStatus status)
        {
            if (_transaction == null)
            {
                status = ContextCommitStatus.InvalidPreparation;
                return false;
            }

            return _transaction.TryValidateRestore(
                _instance,
                source,
                resumedTickFrame,
                out status);
        }

        /// <summary>将以本实例为来源的事件投给本地已注册的载荷通道。</summary>
        /// <param name="packet">当前事件包，包含投递元数据和类型化载荷。</param>
        /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
        /// <returns>本次接收、拒绝、通道不可用或溢出的结果。</returns>
        public InboxEnqueueResult TryEnqueueLocal<TEvent>(
            in EventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            if (!CanAcceptEventInput || _bindings == null ||
                _events?.Inbox.Find<TEvent>() == null)
            {
                return InboxEnqueueResult.MailboxUnavailable;
            }

            if (!packet.IsValid ||
                packet.Envelope.SourceGraphInstanceId != GraphInstanceId)
            {
                return InboxEnqueueResult.InvalidPacket;
            }

            InboxEnqueueResult result = _events.TryReceive(in packet);
            if (result == InboxEnqueueResult.ReliableOverflowFaultRequired)
            {
                MarkReliableOverflowPending();
            }

            return result;
        }

        #endregion

        #region Internal Logic

        [SerializeField] private StateGraphAsset stateGraphAsset;
        [SerializeField] private StateGraphDriver driver = StateGraphDriver.Update;
        [SerializeField] private bool autoStart = true;
        [SerializeField, Min(0.0001f)] private float timeScale = 1f;
        [SerializeField] private MonoBehaviour[] intentSources = Array.Empty<MonoBehaviour>();
        [SerializeField] private MonoBehaviour[] eventAdapters = Array.Empty<MonoBehaviour>();
        [SerializeField] private MonoBehaviour[] operators = Array.Empty<MonoBehaviour>();
        [FormerlySerializedAs("actorContextBinding")]
        [SerializeField] private MonoBehaviour hostContextBinding;
        [SerializeField] private HostOperatorRecovery[] operatorRecovery = Array.Empty<HostOperatorRecovery>();
        [SerializeField] private MonoBehaviour contextRestoreBinding;
        [SerializeField, Min(0)] private int temporalHistoryCapacity;
        [SerializeField, Min(2)] private int contextFrameCapacity = 3;
        [SerializeField, Min(0)] private int eventOutboxCapacity = 32;
        [SerializeField, Min(0)] private int traceCapacity;
        [SerializeField, Min(1)] private int eventLaneCapacity = 32;

        private HostRuntimeBindings _bindings;
        private StateGraphInstance _instance;
        private HostStepTransaction _transaction;
        private HostHistoryController _temporal;
        private HostRestoreBindings _restore;
        private HostInboxBindings _events;
        private bool _hasStoppedInstance;
        private bool _isDisposed;
        private CoCoDiagnostic _lastDiagnostic;
        private int _lastAutomaticFrame = -1;
        private bool _reliableOverflowPending;
        private bool _acceptsEventInput;
        private bool _isStarting;
        private bool _isAdvancing;
        private bool _isTemporalOperation;
        private bool _isPublishingCommittedEvents;
        private bool _destroyRequested;
        private bool _stopAfterPublish;
        private bool _disposeAfterPublish;
        private bool _requiresWorldCorrection;
        private ulong _inputAuthorityRevision = 1UL;
        private CommitGuard _commitGuard;

        internal StateGraphDriver Driver => driver;
        internal bool AutoStart => autoStart;
        internal float TimeScale => timeScale;
        internal IReadOnlyList<MonoBehaviour> IntentSources =>
            intentSources ?? Array.Empty<MonoBehaviour>();
        internal IReadOnlyList<MonoBehaviour> EventAdapters =>
            eventAdapters ?? Array.Empty<MonoBehaviour>();
        internal IReadOnlyList<MonoBehaviour> Operators => operators ?? Array.Empty<MonoBehaviour>();
        internal MonoBehaviour HostContextBinding => hostContextBinding;
        internal MonoBehaviour ContextRestoreBinding => contextRestoreBinding;
        internal int ContextFrameCapacity => contextFrameCapacity;
        internal int EventOutboxCapacity => eventOutboxCapacity;
        internal int TraceCapacity => traceCapacity;
        internal bool HasLiveRuntime => _instance != null;
        internal bool IsRuntimeFaulted => _instance?.IsFaulted ?? false;

        internal bool CanAcceptEventInput =>
            _acceptsEventInput &&
            _instance != null &&
            (_instance.Lifecycle == RuntimeLifecycleState.Running ||
             _instance.Lifecycle == RuntimeLifecycleState.Suspended) &&
            (!_instance.IsFaulted ||
             (_temporal != null && _temporal.Mode == HostHistoryMode.Previewing)) &&
            !_reliableOverflowPending;

        private void Start()
        {
            if (autoStart)
            {
                TryStart(out _lastDiagnostic);
            }
        }

        private void Update()
        {
            if (driver == StateGraphDriver.Update)
            {
                TryAutomaticStep(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (driver == StateGraphDriver.FixedUpdate)
            {
                TryAutomaticStep(Time.fixedDeltaTime);
            }
        }

        private void OnDestroy()
        {
            _acceptsEventInput = false;
            if (_isStarting || _isAdvancing || _isTemporalOperation)
            {
                _destroyRequested = true;
                return;
            }

            ForceDisposeHost();
        }

        private bool TryStartCore(out CoCoDiagnostic diagnostic)
        {
            if (_instance != null || _isDisposed)
            {
                diagnostic = LifecycleError("Host can start only from Created or Stopped.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!IsPositiveFinite(timeScale) ||
                !Enum.IsDefined(typeof(StateGraphDriver), driver) ||
                temporalHistoryCapacity < 0 ||
                contextFrameCapacity < 2 ||
                eventOutboxCapacity < 0 ||
                traceCapacity < 0)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.NonPositiveDeltaTime,
                    "Host Driver, TimeScale, Temporal, Context, Outbox, and Trace capacities are invalid.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            var boundary = HostComponentBoundary.For(this);
            if (!HostInboxBindings.TryValidateAdapters(boundary, EventAdapters, out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            CommitGuard guard = _commitGuard ?? new CommitGuard(this);
            if (!HostStartup.TryPrepare(stateGraphAsset, boundary, IntentSources, Operators, operatorRecovery,
                    hostContextBinding, contextRestoreBinding, (ulong)driver + 1UL, temporalHistoryCapacity,
                    contextFrameCapacity, eventOutboxCapacity, traceCapacity, guard, guard, out HostStartup ready,
                    out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            _bindings = ready.Bindings;
            _instance = ready.Instance;
            _transaction = ready.Transaction;
            _temporal = ready.History;
            _restore = ready.Restore;
            _commitGuard = guard;
            _requiresWorldCorrection = false;
            _reliableOverflowPending = false;
            _lastAutomaticFrame = -1;
            try
            {
                // 在所有实例资源就绪后才订阅，避免启动中的 Host 接受事件。
                _events = new HostInboxBindings(_instance.GraphInstanceId, eventLaneCapacity,
                    () => CanAcceptEventInput && _instance.Lifecycle == RuntimeLifecycleState.Running &&
                          !_isTemporalOperation && _temporal.Mode != HostHistoryMode.Previewing, MarkReliableOverflowPending);
                _instance.AttachEvents(_events.Inbox, _transaction.Outbox);
                _events.Subscribe(boundary, EventAdapters, GetComponentsInChildren<MonoBehaviour>(true), ready.InboxEventTypes);
                _acceptsEventInput = true;
                _hasStoppedInstance = false;
                AdvanceInputAuthorityRevision();
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                DisposeInstance();
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, CoCoDiagnosticCode.CommitPreparationFailed,
                    "Host setup failed before the StateGraph instance became observable.");
                _lastDiagnostic = diagnostic;
                return false;
            }
        }

        private bool TrySuspendCore(
            bool checkPendingOverflow,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_instance == null ||
                _instance.IsFaulted ||
                (_temporal != null && _temporal.Mode == HostHistoryMode.Previewing))
            {
                diagnostic = LifecycleError("Only a healthy Running Host can suspend.");
                return false;
            }

            if (checkPendingOverflow && LatchPendingOverflow(out diagnostic))
            {
                return false;
            }

            if (!_instance.TrySuspend(out diagnostic))
            {
                return false;
            }

            AdvanceInputAuthorityRevision();
            _transaction?.Suspend();

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryResumeCore(
            bool checkPendingOverflow,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_instance == null ||
                _instance.IsFaulted ||
                (checkPendingOverflow && LatchPendingOverflow(out diagnostic)))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = LifecycleError("A Faulted or missing Host cannot resume.");
                }

                return false;
            }

            if (!_instance.TryResume(out diagnostic))
            {
                return false;
            }

            AdvanceInputAuthorityRevision();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryCaptureDebugSnapshot(
            out HostDebugSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            snapshot = null;
            if (_instance == null ||
                _transaction == null ||
                _isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents ||
                (_temporal != null && _temporal.Mode == HostHistoryMode.Previewing))
            {
                diagnostic = LifecycleError(
                    "Debugger snapshot capture requires one live idle committed Host boundary.");
                return false;
            }

            if (!_instance.TryCaptureCommittedDebugState(
                    out StateGraphCommittedDebugState graphState,
                    out diagnostic) ||
                !_transaction.TryCaptureCommittedDebugState(
                    out ContextFrame context,
                    out diagnostic))
            {
                return false;
            }

            if (context.IsAlive)
            {
                StateFlowFrameHeader header = context.Header;
                TickFrame tickFrame = header.TickFrame;
                if (!header.IsValid ||
                    header.Identity.GraphInstanceId != graphState.GraphInstanceId ||
                    tickFrame.TimelineId != graphState.TimelineId ||
                    tickFrame.ClockDomainId != graphState.ClockDomainId ||
                    tickFrame.TimelineEpoch != graphState.TimelineEpoch ||
                    tickFrame.Tick != graphState.Tick ||
                    tickFrame.ExecutionSequence != graphState.ExecutionSequence ||
                    !tickFrame.TimelinePosition.Seconds.Equals(graphState.Seconds))
                {
                    diagnostic = LifecycleError(
                        "Committed Graph, Clock, and Context identities were not one atomic debugger boundary.");
                    return false;
                }
            }

            snapshot = HostDebugSnapshot.CopyFrom(
                graphState,
                _instance.Lifecycle,
                _instance.Fault,
                _requiresWorldCorrection,
                _lastDiagnostic,
                context);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Captures one immutable committed debugger snapshot together with a
        /// logical-depth copy of the Temporal ring metadata. No retained
        /// payload or mutable Context handle crosses this boundary.
        /// </summary>
        internal bool TryCaptureTemporalDebugSnapshot(
            out HostHistoryDebugSnapshot snapshot,
            out CoCoDiagnostic diagnostic)
        {
            snapshot = null;
            if (!TryCaptureDebugSnapshot(
                    out HostDebugSnapshot current,
                    out diagnostic))
            {
                return false;
            }

            int capacity = _temporal?.History?.Capacity ?? 0;
            HostHistoryFrame[] frames;
            if (_temporal == null)
            {
                capacity = temporalHistoryCapacity < 0
                    ? 0
                    : temporalHistoryCapacity;
                frames = Array.Empty<HostHistoryFrame>();
            }
            else if (!_temporal.TryCopyFrames(out frames))
            {
                diagnostic = LifecycleError(
                    "Temporal debugger metadata changed outside one committed Host boundary.");
                return false;
            }

            if (frames.Length > 0 &&
                (current.ContextHeader.Identity.GraphInstanceId !=
                 frames[0].GraphInstanceId ||
                 current.ContextHeader.TickFrame != frames[0].TickFrame ||
                 current.ContextRevision != frames[0].Revision))
            {
                diagnostic = LifecycleError(
                    "Current Context and Temporal ring head were not one atomic debugger boundary.");
                return false;
            }

            snapshot = new HostHistoryDebugSnapshot(
                current,
                capacity,
                frames);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>
        /// Validates and decodes only the source-frame metadata in an existing
        /// persistence payload. It never imports the payload and never updates
        /// Host diagnostics or lifecycle state.
        /// </summary>
        internal bool TryDecodePersistenceDebugFrame(
            byte[] payload,
            out HostHistoryFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            frame = default;
            if (payload == null ||
                payload.Length == 0 ||
                _instance == null ||
                _bindings == null ||
                _isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents)
            {
                diagnostic = LifecycleError(
                    "Persistence debugger decoding requires one live idle Host and one existing payload.");
                return false;
            }

            try
            {
                return HostPersistenceFrameCodec.TryCreate(
                           _instance.Graph.GraphId,
                           _bindings.ContextLayout,
                           _bindings.ContextCodecs,
                           out HostPersistenceFrameCodec codec,
                           out diagnostic) &&
                       codec.TryDecode(
                           payload,
                           out StateGraphPersistenceEnvelope envelope,
                           out ProjectionRestoreSource source,
                           out diagnostic) &&
                       HostPersistenceFrameCodec.TryCreatePersistedSourceInfo(
                           envelope,
                           source,
                           out frame,
                           out diagnostic);
            }
            catch (Exception)
            {
                frame = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "Persistence debugger decoding rejected an unreadable payload.");
                return false;
            }
        }
#endif

        internal bool TryDebugStepWhileSuspended(
            double deltaTime,
            out CoCoDiagnostic diagnostic)
        {
            if (_instance == null ||
                _transaction == null ||
                _instance.Lifecycle != RuntimeLifecycleState.Suspended ||
                _instance.IsFaulted ||
                !IsPositiveFinite(deltaTime) ||
                _isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents ||
                (_temporal != null && _temporal.Mode == HostHistoryMode.Previewing) ||
                _requiresWorldCorrection)
            {
                diagnostic = LifecycleError(
                    "Debug Step requires one healthy idle Suspended Host and a positive finite DeltaTime.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            _isAdvancing = true;
            try
            {
                if (!TryResumeCore(false, out diagnostic))
                {
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                bool advanced = TryAdvanceCore(deltaTime, out diagnostic);
                CoCoDiagnostic advanceDiagnostic = diagnostic;
                bool canReturnToSuspended =
                    _instance != null &&
                    _instance.Lifecycle == RuntimeLifecycleState.Running &&
                    !_instance.IsFaulted &&
                    !_destroyRequested &&
                    !_stopAfterPublish &&
                    !_disposeAfterPublish &&
                    !_requiresWorldCorrection;
                if (!canReturnToSuspended)
                {
                    if (advanced)
                    {
                        diagnostic = LifecycleError(
                            "Debug Step committed, but deferred lifecycle or fault handling prevented a healthy Suspended return.");
                        _lastDiagnostic = diagnostic;
                    }

                    return false;
                }

                if (!TrySuspendCore(true, out CoCoDiagnostic suspendDiagnostic))
                {
                    if (!_instance.IsFaulted)
                    {
                        CoCoDiagnostic synchronizationFailure = suspendDiagnostic.IsError
                            ? suspendDiagnostic
                            : LifecycleError(
                                "Debug Step could not synchronize Runtime back to Suspended.");
                        _instance.TryLatchExternalFault(synchronizationFailure);
                    }

                    diagnostic = _instance.IsFaulted
                        ? _instance.Fault.Diagnostic
                        : suspendDiagnostic;
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!advanced)
                {
                    diagnostic = advanceDiagnostic;
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }
            finally
            {
                _isAdvancing = false;
                if (_destroyRequested)
                {
                    _destroyRequested = false;
                    _stopAfterPublish = false;
                    _disposeAfterPublish = false;
                    ForceDisposeHost();
                }
                else
                {
                    CompleteDeferredPublishLifecycle();
                }
            }
        }

        internal bool TryPrepareRestore(
            ContextFrame source,
            TickFrame resumedTickFrame,
            out PreparedHostRestore preparedRestore,
            out ContextCommitStatus status,
            out CoCoDiagnostic diagnostic)
        {
            if (_transaction == null || _instance == null)
            {
                preparedRestore = default;
                status = ContextCommitStatus.InvalidPreparation;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Actor restore preparation requires one live Host transaction.");
                return false;
            }

            return _transaction.TryPrepareRestore(
                _instance,
                source,
                resumedTickFrame,
                out preparedRestore,
                out status,
                out diagnostic);
        }

        internal bool TryCapturePersistencePayload(
            out byte[] payload,
            out CoCoDiagnostic diagnostic)
        {
            payload = null;
            if (_isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents ||
                _instance == null ||
                _transaction == null ||
                _bindings == null ||
                _temporal == null ||
                _instance.IsFaulted ||
                (_instance.Lifecycle != RuntimeLifecycleState.Running &&
                 _instance.Lifecycle != RuntimeLifecycleState.Suspended) ||
                _temporal.Mode == HostHistoryMode.Previewing ||
                !_transaction.CurrentContext.IsAlive)
            {
                diagnostic = LifecycleError(
                    "StateGraph Persistence capture requires one idle healthy Host with a committed ContextFrame.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            bool succeeded;
            _isTemporalOperation = true;
            try
            {
                try
                {
                    succeeded =
                        HostPersistenceFrameCodec.TryCreate(
                            _instance.Graph.GraphId,
                            _bindings.ContextLayout,
                            _bindings.ContextCodecs,
                            out HostPersistenceFrameCodec codec,
                            out diagnostic) &&
                        codec.TryEncode(
                            _transaction.CurrentContext,
                            out payload,
                            out diagnostic);
                }
                catch (Exception)
                {
                    succeeded = false;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Restore,
                        CoCoDiagnosticCode.CommitPreparationFailed,
                        "StateGraph Persistence capture threw inside its synchronous Durable codec boundary.");
                }
            }
            finally
            {
                _isTemporalOperation = false;
            }

            if (_destroyRequested)
            {
                _destroyRequested = false;
                _stopAfterPublish = false;
                _disposeAfterPublish = false;
                ForceDisposeHost();
                payload = null;
                diagnostic = LifecycleError(
                    "Unity destruction cancelled StateGraph Persistence capture before publication.");
                succeeded = false;
            }

            if (!succeeded || diagnostic.IsError)
            {
                payload = null;
                _lastDiagnostic = diagnostic;
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        internal bool TryApplyPersistencePayload(
            byte[] payload,
            out CoCoDiagnostic diagnostic)
        {
            if (payload == null ||
                payload.Length == 0 ||
                _isStarting ||
                _isAdvancing ||
                _isTemporalOperation ||
                _isPublishingCommittedEvents)
            {
                diagnostic = LifecycleError(
                    "StateGraph Persistence import requires one idle Host and one non-empty payload.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_instance == null ||
                _transaction == null ||
                _bindings == null ||
                _temporal == null)
            {
                diagnostic = LifecycleError(
                    "StateGraph Persistence import requires one live Host instance.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!HostPersistenceFrameCodec.TryCreate(
                    _instance.Graph.GraphId,
                    _bindings.ContextLayout,
                    _bindings.ContextCodecs,
                    out HostPersistenceFrameCodec codec,
                    out diagnostic) ||
                !codec.TryDecode(
                    payload,
                    out StateGraphPersistenceEnvelope envelope,
                    out ProjectionRestoreSource persistedSource,
                    out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            AdvanceInputAuthorityRevision();
            bool succeeded;
            _isTemporalOperation = true;
            try
            {
                succeeded = TryImportPersistence(
                    _instance,
                    codec,
                    envelope,
                    persistedSource,
                    out diagnostic);
            }
            finally
            {
                _isTemporalOperation = false;
            }

            if (_destroyRequested)
            {
                _destroyRequested = false;
                _stopAfterPublish = false;
                _disposeAfterPublish = false;
                ForceDisposeHost();
                diagnostic = LifecycleError(
                    "Unity destruction cancelled StateGraph Persistence import before publication.");
                succeeded = false;
            }

            _lastDiagnostic = diagnostic;
            return succeeded;
        }

        internal void MarkReliableOverflowPending()
        {
            if (_instance != null && !_instance.IsFaulted)
            {
                _reliableOverflowPending = true;
            }
        }

        internal bool IsTemporalOperationCancellationRequested => _destroyRequested;

        internal void LatchWorldCorrectionFault(CoCoDiagnostic diagnostic)
        {
            _requiresWorldCorrection = true;
            CoCoDiagnostic reason = diagnostic.IsError
                ? diagnostic
                : CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.WorldCorrectionRequired,
                    "Temporal Unity projection failed and requires correction from current authority.");
            _instance?.TryLatchExternalFault(reason);
        }

        internal void ClearWorldCorrectionRequirementNoFail()
        {
            _requiresWorldCorrection = false;
        }

        private void TryAutomaticStep(float deltaTime)
        {
            if (_instance == null ||
                _instance.Lifecycle != RuntimeLifecycleState.Running ||
                _instance.IsFaulted ||
                (_temporal != null && _temporal.Mode == HostHistoryMode.Previewing) ||
                _lastAutomaticFrame == Time.frameCount ||
                !IsPositiveFinite(deltaTime))
            {
                return;
            }

            _lastAutomaticFrame = Time.frameCount;
            TryAdvance(deltaTime, out _lastDiagnostic);
        }

        private bool TryAdvance(double deltaTime, out CoCoDiagnostic diagnostic)
        {
            if (_isStarting || _isAdvancing || _isTemporalOperation)
            {
                diagnostic = LifecycleError(
                    "StateGraph Host cannot Step during startup, Temporal projection, or its active Tick.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            _isAdvancing = true;
            try
            {
                return TryAdvanceCore(deltaTime, out diagnostic);
            }
            finally
            {
                _isAdvancing = false;
                if (_destroyRequested)
                {
                    _destroyRequested = false;
                    _stopAfterPublish = false;
                    _disposeAfterPublish = false;
                    ForceDisposeHost();
                }
                else
                {
                    CompleteDeferredPublishLifecycle();
                }
            }
        }

        internal void BeginCommittedEventPublication()
        {
            _isPublishingCommittedEvents = true;
        }

        internal void EndCommittedEventPublication()
        {
            _isPublishingCommittedEvents = false;
        }

        private bool TryAdvanceCore(double deltaTime, out CoCoDiagnostic diagnostic)
        {
            if (_instance == null ||
                _transaction == null ||
                _instance.Lifecycle != RuntimeLifecycleState.Running ||
                _instance.IsFaulted ||
                (_temporal != null && _temporal.Mode == HostHistoryMode.Previewing))
            {
                diagnostic = LifecycleError("Only a healthy Running Host can Step.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            TickFrame tickFrame = default;
            StagedGraphStep stagedStep = default;
            try
            {
                if (!_instance.TryPreviewNextTick(
                        deltaTime,
                        timeScale,
                        out tickFrame,
                        out diagnostic))
                {
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (!_transaction.TryPrepareContext(
                        tickFrame,
                        out ContextCommitStatus contextStatus,
                        out diagnostic))
                {
                    // Capacity exhaustion occurs before Inbox seal and is intentionally retryable.
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _events.Inbox.Seal(tickFrame);
                if (!_bindings.TryCollectIntents(
                        tickFrame,
                        out IIntentFrame intents,
                        out diagnostic))
                {
                    CoCoDiagnostic collectionFailure = diagnostic.IsError
                        ? diagnostic
                        : CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Intent,
                            CoCoDiagnosticCode.CommitPreparationFailed,
                            "Intent collection failed after the Host sealed its Tick input.");
                    _transaction.Cancel(collectionFailure);
                    _instance.TryLatchExternalFault(collectionFailure);
                    _bindings.ResolveIntentTick(tickFrame);
                    diagnostic = _instance.IsFaulted
                        ? _instance.Fault.Diagnostic
                        : collectionFailure;
                    _lastDiagnostic = diagnostic;
                    return false;
                }

                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        default,
                        tickFrame,
                        out diagnostic);
                }

                if (!_instance.TryStageStep(
                        tickFrame,
                        intents,
                        _transaction.PreviousContext,
                        out stagedStep,
                        out diagnostic))
                {
                    if (_instance.IsFaulted)
                    {
                        _bindings.ResolveIntentTick(tickFrame);
                    }

                    _transaction.Cancel(diagnostic);

                    _lastDiagnostic = diagnostic;
                    return false;
                }

                _transaction.AppendStagedTrace(_instance, stagedStep);
            }
            catch (Exception)
            {
                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        stagedStep,
                        tickFrame,
                        out diagnostic);
                }

                CoCoDiagnostic stagingFailure = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Intent collection or Tick staging threw before transaction finalization.");
                _transaction.Cancel(stagingFailure);
                bool rejected = stagedStep.IsValid &&
                                _instance.TryRejectStagedStep(
                                    stagedStep,
                                    stagingFailure,
                                    true,
                                    _commitGuard,
                                    out diagnostic);
                if (_destroyRequested)
                {
                    return CancelTickForPendingDestroy(
                        default,
                        tickFrame,
                        out diagnostic);
                }

                if (!rejected)
                {
                    _instance.TryLatchExternalFault(stagingFailure);
                    diagnostic = _instance.IsFaulted
                        ? _instance.Fault.Diagnostic
                        : stagingFailure;
                }

                _bindings.ResolveIntentTick(tickFrame);
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_destroyRequested)
            {
                return CancelTickForPendingDestroy(
                    stagedStep,
                    tickFrame,
                    out diagnostic);
            }

            bool finalized = _transaction.TryFinalizeAndCommit(
                _instance,
                _bindings,
                _temporal,
                _restore,
                stagedStep,
                _commitGuard,
                out bool authorityCommitted,
                out bool worldMayBeDirty,
                out diagnostic);
            if (_temporal.History != null) _restore.DrainParticipantCleanup();
            if (finalized)
            {
                _lastDiagnostic = CoCoDiagnostic.None;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            CoCoDiagnostic reason = diagnostic.IsError
                ? diagnostic
                : CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operator,
                    CoCoDiagnosticCode.CommitCancelled,
                    "Operator transaction cancelled the staged Tick.");
            if (authorityCommitted)
            {
                // Publication happens after the complete authority barrier. A publish fault
                // cannot roll back Context, Graph, Claims, Clock, or assigned Sequences.
                _instance.TryLatchExternalFault(reason);
                diagnostic = _instance.IsFaulted ? _instance.Fault.Diagnostic : reason;
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (worldMayBeDirty)
            {
                _restore.Projection = null;
                _requiresWorldCorrection = true;
            }

            if (_destroyRequested)
            {
                return CancelTickForPendingDestroy(
                    stagedStep,
                    tickFrame,
                    out diagnostic);
            }

            if (stagedStep.IsValid)
            {
                _instance.TryRejectStagedStep(
                    stagedStep,
                    reason,
                    true,
                    _commitGuard,
                    out diagnostic);
            }
            else if (!_instance.IsFaulted)
            {
                _instance.TryLatchExternalFault(reason);
                diagnostic = _instance.IsFaulted ? _instance.Fault.Diagnostic : reason;
            }

            _bindings.ResolveIntentTick(tickFrame);
            if (_requiresWorldCorrection)
            {
                CoCoDiagnostic correction = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Operator,
                    CoCoDiagnosticCode.WorldCorrectionRequired,
                    "A failed Operator transaction may have changed Unity state; Restore correction is required before resuming authority.");
                if (!_instance.IsFaulted)
                {
                    _instance.TryLatchExternalFault(correction);
                }
            }

            diagnostic = _instance.IsFaulted ? _instance.Fault.Diagnostic : reason;
            _lastDiagnostic = diagnostic;
            return false;
        }

        private bool CancelTickForPendingDestroy(
            in StagedGraphStep stagedStep,
            in TickFrame tickFrame,
            out CoCoDiagnostic diagnostic)
        {
            CoCoDiagnostic reason = LifecycleError(
                "Unity destruction cancelled the unresolved Tick before commit.");
            _transaction?.Cancel(reason);
            diagnostic = reason;
            if (stagedStep.IsValid &&
                (!_instance.TryCancelStagedStep(
                     stagedStep,
                     out CoCoDiagnostic cancellationDiagnostic) ||
                 cancellationDiagnostic.IsError))
            {
                diagnostic = cancellationDiagnostic.IsError
                    ? cancellationDiagnostic
                    : reason;
            }

            _bindings.ResolveIntentTick(tickFrame);
            _lastDiagnostic = diagnostic;
            return false;
        }

        private bool LatchPendingOverflow(out CoCoDiagnostic diagnostic)
        {
            if (!_reliableOverflowPending || _instance == null)
            {
                diagnostic = CoCoDiagnostic.None;
                return false;
            }

            diagnostic = CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Mailbox,
                CoCoDiagnosticCode.MailboxOverflow,
                "A reliable Event overflowed this Actor Inbox.");
            _instance.TryLatchExternalFault(diagnostic);
            _reliableOverflowPending = false;
            return true;
        }

        private bool RejectLifecycleReentry(out CoCoDiagnostic diagnostic)
        {
            if (!_isStarting && !_isAdvancing && !_isTemporalOperation)
            {
                diagnostic = CoCoDiagnostic.None;
                return false;
            }

            diagnostic = LifecycleError(
                "StateGraph Host lifecycle cannot change while startup or a Tick is advancing.");
            _lastDiagnostic = diagnostic;
            return true;
        }

        private bool TryRunTemporalOperation(
            TemporalOperation operation,
            int historyDepth,
            out CoCoDiagnostic diagnostic)
        {
            if (_isStarting || _isAdvancing || _isTemporalOperation ||
                _isPublishingCommittedEvents)
            {
                diagnostic = LifecycleError(
                    "Temporal control requires one idle Host boundary and cannot reenter project callbacks.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (_temporal == null || _instance == null || _transaction == null)
            {
                diagnostic = LifecycleError(
                    "Temporal control requires one live StateGraph Host instance.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (operation != TemporalOperation.Correct &&
                LatchPendingOverflow(out diagnostic))
            {
                _lastDiagnostic = diagnostic;
                return false;
            }

            AdvanceInputAuthorityRevision(clearStateInbox: false);
            bool succeeded;
            _isTemporalOperation = true;
            try
            {
                switch (operation)
                {
                    case TemporalOperation.Begin:
                        succeeded = TryBegin(_instance, out diagnostic);
                        break;
                    case TemporalOperation.Preview:
                        succeeded = TryPreview(
                            _instance,
                            historyDepth,
                            out diagnostic);
                        break;
                    case TemporalOperation.Confirm:
                        succeeded = TryConfirm(_instance, out diagnostic);
                        break;
                    case TemporalOperation.Cancel:
                        succeeded = TryCancel(_instance, out diagnostic);
                        break;
                    case TemporalOperation.Correct:
                        succeeded = TryCorrectWorld(
                            _instance,
                            _requiresWorldCorrection,
                            out diagnostic);
                        break;
                    default:
                        succeeded = false;
                        diagnostic = LifecycleError("Temporal operation is not defined.");
                        break;
                }

                if (succeeded)
                {
                    _events?.Inbox.Clear();
                }
            }
            finally
            {
                _isTemporalOperation = false;
            }

            bool destroyRequested = _destroyRequested;
            if (destroyRequested)
            {
                _destroyRequested = false;
                _stopAfterPublish = false;
                _disposeAfterPublish = false;
                ForceDisposeHost();
                diagnostic = LifecycleError(
                    "Unity destruction cancelled the Temporal operation before publication.");
                succeeded = false;
            }

            _lastDiagnostic = diagnostic;
            return succeeded;
        }

        private bool TryImportPersistence(
            StateGraphInstance runtime,
            HostPersistenceFrameCodec codec,
            in StateGraphPersistenceEnvelope envelope,
            in ProjectionRestoreSource persistedSource,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_isDisposed ||
                runtime == null ||
                codec == null ||
                !envelope.IsValid ||
                !persistedSource.IsValid ||
                runtime.IsFaulted ||
                (runtime.Lifecycle != RuntimeLifecycleState.Running &&
                 runtime.Lifecycle != RuntimeLifecycleState.Suspended) ||
                _temporal.Mode == HostHistoryMode.Previewing)
            {
                diagnostic = TemporalLifecycleError(
                    "Persistence import requires one healthy Running or Suspended Host outside Temporal preview.");
                return false;
            }

            if (!HostPersistenceFrameCodec.TryCreateImportedTickFrame(
                    runtime.Clock,
                    envelope,
                    persistedSource,
                    out TickFrame importedTickFrame,
                    out diagnostic) ||
                !HostPersistenceFrameCodec.TryCreatePersistedSourceInfo(
                    envelope,
                    persistedSource,
                    out HostHistoryFrame persistedInfo,
                    out diagnostic))
            {
                return false;
            }

            _restore.Projection = ContextProjection.Durable;
            if (!_restore.IsLive || !_restore.TryAttachParticipant(0, out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = ConfigurationError(
                        "Persistence import requires its original live Restore Binding inside the Host boundary.");
                }

                return false;
            }

            if (!_transaction.TryPreparePersistenceRestore(
                    runtime,
                    codec.Projection,
                    envelope.DurablePayload,
                    importedTickFrame,
                    _restore.RetainedSlots,
                    _temporal.History,
                    out PreparedHostRestore preparedRestore,
                    out ContextRestoreReadView restoreSource,
                    out _,
                    out HostHistoryFrame targetAuthority,
                    out _,
                    out diagnostic))
            {
                _temporal.History?.CancelPreparedCapture();
                return false;
            }

            try
            {
                if (_restore.Participant != null &&
                    (!_restore.Participant.IsTemporalParticipantLive(this) ||
                     !_restore.Participant.TryPrepareAuthorityReset(
                         targetAuthority,
                         out diagnostic)))
                {
                    _restore.Participant.CancelPreparedAuthorityResetNoFail();
                    _temporal.History?.CancelPreparedCapture();
                    preparedRestore.Cancel();
                    if (!diagnostic.IsError)
                    {
                        diagnostic = HistoryError(
                            CoCoDiagnosticCode.CommitPreparationFailed,
                            "Temporal participant rejected the imported authority baseline.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                _restore.Participant?.CancelPreparedAuthorityResetNoFail();
                _temporal.History?.CancelPreparedCapture();
                preparedRestore.Cancel();
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Temporal participant authority reset threw before the Persistence authority barrier.");
                return false;
            }

            bool worldMayBeDirty = false;
            if (!_restore.TryApply(
                    HostRestoreKind.Confirm,
                    persistedInfo,
                    importedTickFrame,
                    restoreSource,
                    out worldMayBeDirty,
                    out diagnostic))
            {
                _restore.Participant?.CancelPreparedAuthorityResetNoFail();
                _temporal.History?.CancelPreparedCapture();
                preparedRestore.Cancel();
                if (worldMayBeDirty)
                {
                    LatchWorldCorrectionFault(diagnostic);
                }

                return false;
            }

            if (IsTemporalOperationCancellationRequested ||
                !preparedRestore.IsValid ||
                (_temporal.History != null && !_temporal.History.HasPreparedCapture) ||
                (_restore.Participant != null &&
                 !_restore.Participant.IsTemporalParticipantLive(this)) ||
                !_restore.IsLive)
            {
                _restore.Participant?.CancelPreparedAuthorityResetNoFail();
                _temporal.History?.CancelPreparedCapture();
                preparedRestore.Cancel();
                diagnostic = TemporalLifecycleError(
                    "Persistence import proof changed during Unity projection before authority publication.");
                if (worldMayBeDirty)
                {
                    LatchWorldCorrectionFault(diagnostic);
                }

                return false;
            }

            preparedRestore.CommitNoFail();
            _temporal.History?.PublishAuthorityResetNoFail();
            _restore.Participant?.CommitPreparedAuthorityResetNoFail();
            _temporal.FinishPreview();
            _restore.DrainParticipantCleanup();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryBegin(
            StateGraphInstance runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, HostHistoryMode.Ready, out diagnostic) ||
                _temporal.History == null ||
                _temporal.History.Count < 2)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = TemporalLifecycleError(
                        "Temporal preview requires at least current authority and one older committed entry.");
                }

                return false;
            }

            _restore.Projection = ContextProjection.Temporal;
            if (!TryRequireLiveBinding(out diagnostic))
            {
                return false;
            }

            if (_restore.Participant != null &&
                !_restore.Participant.TryBeginPreview(_temporal.History.Count, out diagnostic))
            {
                return false;
            }

            _temporal.TryBeginPreview();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryPreview(
            StateGraphInstance runtime,
            int historyDepth,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, HostHistoryMode.Previewing, out diagnostic) ||
                _temporal.History == null ||
                historyDepth < 0 ||
                historyDepth >= _temporal.History.Count)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = HistoryError(
                        CoCoDiagnosticCode.InvalidRestoreMetadata,
                        "Temporal preview depth is outside the current bounded history.");
                }

                return false;
            }

            if (!TryRequireLiveBinding(out diagnostic))
            {
                if (_temporal.HasAppliedPreview)
                {
                    LatchWorldCorrectionFault(diagnostic);
                }

                return false;
            }

            bool applied;
            bool worldMayBeDirty;
            HostHistoryFrame previewInfo;
            if (historyDepth == 0)
            {
                ContextFrameReadView authority = _transaction.PreviousContext;
                previewInfo = HostHistoryFrame.From(_transaction.CurrentContext);
                if (_restore.Participant != null &&
                    !_restore.Participant.TryPrepareProjection(
                        HostRestoreKind.Preview,
                        historyDepth,
                        previewInfo,
                        previewInfo.TickFrame,
                        out diagnostic))
                {
                    return false;
                }

                applied = _restore.TryApply(
                    HostRestoreKind.Preview,
                    previewInfo,
                    previewInfo.TickFrame,
                    authority,
                    out worldMayBeDirty,
                    out diagnostic);
                _restore.Participant?.FinishProjectionNoFail(applied);
            }
            else
            {
                if (!_temporal.TrySelect(historyDepth, _restore.RetainedSlots, _transaction.CurrentContext.Buffer,
                        out TemporalSelection selection, out diagnostic))
                {
                    return false;
                }

                previewInfo = HostHistoryController.GetFrame(selection.Info);
                if (_restore.Participant != null &&
                    !_restore.Participant.TryPrepareProjection(
                        HostRestoreKind.Preview,
                        historyDepth,
                        previewInfo,
                        previewInfo.TickFrame,
                        out diagnostic))
                {
                    return false;
                }

                applied = _restore.TryApply(
                    HostRestoreKind.Preview,
                    previewInfo,
                    previewInfo.TickFrame,
                    selection.RestoreView,
                    out worldMayBeDirty,
                    out diagnostic);
                _restore.Participant?.FinishProjectionNoFail(applied);
            }

            if (!applied)
            {
                if (_temporal.HasAppliedPreview || worldMayBeDirty)
                {
                    LatchWorldCorrectionFault(diagnostic);
                }

                return false;
            }

            _temporal.AcceptProjection(historyDepth, previewInfo);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryConfirm(
            StateGraphInstance runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, HostHistoryMode.Previewing, out diagnostic) ||
                _temporal.History == null ||
                _temporal.PreviewDepth <= 0 ||
                _temporal.PreviewDepth >= _temporal.History.Count)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = TemporalLifecycleError(
                        "Temporal Confirm requires one successfully previewed historical depth greater than zero.");
                }

                return false;
            }

            if (!TryRequireLiveBinding(out diagnostic))
            {
                LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            if (!_temporal.TrySelect(_temporal.PreviewDepth, _restore.RetainedSlots, _transaction.CurrentContext.Buffer,
                    out TemporalSelection selection, out diagnostic))
            {
                return false;
            }

            if (!HostHistoryController.TryCreateResumedTickFrame(
                    runtime.Clock.TimelineEpoch, runtime.Clock.ExecutionSequence,
                    selection.Info,
                    out TickFrame resumedTickFrame,
                    out diagnostic) ||
                !_transaction.TryPrepareTemporalRestore(
                    runtime,
                    _temporal.History,
                    selection,
                    _restore.RetainedSlots,
                    resumedTickFrame,
                    out PreparedHostRestore preparedRestore,
                    out ContextRestoreReadView restoreSource,
                    out _,
                    out diagnostic))
            {
                _temporal.History.CancelPreparedCapture();
                return false;
            }

            HostHistoryFrame sourceInfo = HostHistoryController.GetFrame(selection.Info);
            if (_restore.Participant != null &&
                (!_restore.Participant.TryPrepareBranchCapture(
                     _temporal.PreviewDepth,
                     sourceInfo,
                     out diagnostic) ||
                 !_restore.Participant.TryPrepareProjection(
                     HostRestoreKind.Confirm,
                     _temporal.PreviewDepth,
                     sourceInfo,
                     resumedTickFrame,
                     out diagnostic)))
            {
                _restore.Participant.CancelPreparedCaptureNoFail();
                preparedRestore.Cancel();
                _temporal.History.CancelPreparedCapture();
                return false;
            }

            bool applied = _restore.TryApply(
                HostRestoreKind.Confirm,
                sourceInfo,
                resumedTickFrame,
                restoreSource,
                out _,
                out diagnostic);
            _restore.Participant?.FinishProjectionNoFail(applied);
            if (!applied)
            {
                _restore.Participant?.CancelPreparedCaptureNoFail();
                preparedRestore.Cancel();
                _temporal.History.CancelPreparedCapture();
                LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            if (IsTemporalOperationCancellationRequested ||
                !preparedRestore.IsValid ||
                !_temporal.History.HasPreparedCapture)
            {
                _restore.Participant?.CancelPreparedCaptureNoFail();
                preparedRestore.Cancel();
                _temporal.History.CancelPreparedCapture();
                diagnostic = TemporalLifecycleError(
                    "Temporal restore proof changed during Unity projection before authority publication.");
                LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            preparedRestore.CommitNoFail();
            _temporal.History.PublishBranchCaptureNoFail();
            _restore.Participant?.PublishBranchCaptureNoFail();
            _restore.Participant?.CompletePreviewNoFail(HostRestoreKind.Confirm);
            _temporal.FinishPreview();
            if (_temporal.History != null) _restore.DrainParticipantCleanup();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryCancel(
            StateGraphInstance runtime,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireHealthyRunning(runtime, HostHistoryMode.Previewing, out diagnostic))
            {
                return false;
            }

            if (_temporal.HasAppliedPreview &&
                !TryRequireLiveBinding(out diagnostic))
            {
                LatchWorldCorrectionFault(diagnostic);
                return false;
            }

            HostHistoryFrame current = HostHistoryFrame.From(_transaction.CurrentContext);
            if (_temporal.HasAppliedPreview)
            {
                ContextFrameReadView authority = _transaction.PreviousContext;
                if (_restore.Participant != null &&
                    !_restore.Participant.TryPrepareProjection(
                        HostRestoreKind.Cancel,
                        0,
                        current,
                        current.TickFrame,
                        out diagnostic))
                {
                    return false;
                }

                bool applied = _restore.TryApply(
                        HostRestoreKind.Cancel,
                        current,
                        current.TickFrame,
                        authority,
                        out _,
                        out diagnostic);
                _restore.Participant?.FinishProjectionNoFail(applied);
                if (!applied)
                {
                    LatchWorldCorrectionFault(diagnostic);
                    return false;
                }
            }

            _restore.Participant?.CompletePreviewNoFail(HostRestoreKind.Cancel);
            _temporal.FinishPreview();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryCorrectWorld(
            StateGraphInstance runtime,
            bool requiresWorldCorrection,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                runtime == null ||
                !requiresWorldCorrection ||
                !runtime.IsFaulted ||
                (runtime.Lifecycle != RuntimeLifecycleState.Running &&
                 runtime.Lifecycle != RuntimeLifecycleState.Suspended))
            {
                diagnostic = TemporalLifecycleError(
                    "World correction requires one live faulted Host with an explicit correction requirement.");
                return false;
            }

            if (!TryRequireLiveBinding(out diagnostic))
            {
                return false;
            }

            if (!_restore.Projection.HasValue && !_restore.OperatorsLive(requireCoverage: true))
            {
                diagnostic = BindingError("Tick fault correction requires enabled recovery for every bound Operator.");
                return false;
            }

            if (!runtime.TryPrepareTemporalRecovery(
                    out PreparedTemporalRecovery recovery,
                    out diagnostic))
            {
                return false;
            }

            ContextFrameReadView authority = _transaction.PreviousContext;
            HostHistoryFrame current = HostHistoryFrame.From(_transaction.CurrentContext);
            if (_restore.Participant != null &&
                !_restore.Participant.TryPrepareProjection(
                    HostRestoreKind.Correction,
                    0,
                    current,
                    current.TickFrame,
                    out diagnostic))
            {
                recovery.Cancel();
                return false;
            }

            bool applied = _restore.TryApply(
                HostRestoreKind.Correction,
                current,
                current.TickFrame,
                authority,
                out _,
                out diagnostic);
            _restore.Participant?.FinishProjectionNoFail(applied);
            if (!applied)
            {
                recovery.Cancel();
                return false;
            }

            if (IsTemporalOperationCancellationRequested ||
                !recovery.IsValid)
            {
                recovery.Cancel();
                diagnostic = TemporalLifecycleError(
                    "World correction proof changed during Unity projection.");
                return false;
            }

            recovery.CompleteNoFail();
            ClearWorldCorrectionRequirementNoFail();
            _restore.Participant?.CompletePreviewNoFail(
                HostRestoreKind.Correction);
            _temporal.FinishPreview();
            if (_temporal.History != null) _restore.DrainParticipantCleanup();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryRequireHealthyRunning(
            StateGraphInstance runtime,
            HostHistoryMode requiredMode,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                runtime == null ||
                runtime.Lifecycle != RuntimeLifecycleState.Running ||
                runtime.IsFaulted ||
                _temporal.Mode != requiredMode)
            {
                diagnostic = TemporalLifecycleError(
                    "Temporal control requires one healthy Running Host in the expected Temporal mode.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryRequireLiveBinding(out CoCoDiagnostic diagnostic)
        {
            if (_restore.IsLive)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            diagnostic = ConfigurationError(
                "Temporal Unity projection requires its original live Restore Binding inside the Host boundary.");
            return false;
        }

        private static CoCoDiagnostic ConfigurationError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Context,
                CoCoDiagnosticCode.InvalidHostConfiguration,
                message);

        private static CoCoDiagnostic HistoryError(
            CoCoDiagnosticCode code,
            string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                code == CoCoDiagnosticCode.None
                    ? CoCoDiagnosticCode.InvalidRestoreMetadata
                    : code,
                message);

        private static CoCoDiagnostic BindingError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.WorldCorrectionRequired,
                message);

        private static CoCoDiagnostic TemporalLifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);

        private void CompleteDeferredPublishLifecycle()
        {
            bool stop = _stopAfterPublish;
            bool dispose = _disposeAfterPublish;
            _stopAfterPublish = false;
            _disposeAfterPublish = false;
            if (stop && _instance != null)
            {
                TryStop(out _);
            }

            if (dispose && !_isDisposed)
            {
                TryDispose(out _);
            }
        }

        private void DisposeInstance()
        {
            if (_instance != null &&
                (_instance.Lifecycle == RuntimeLifecycleState.Running ||
                 _instance.Lifecycle == RuntimeLifecycleState.Suspended))
            {
                AdvanceInputAuthorityRevision();
            }

            _acceptsEventInput = false;
            _events?.Dispose();
            _events = null;
            _restore?.Dispose();
            _restore = null;
            _temporal?.Dispose();
            _temporal = null;
            _transaction?.Dispose();
            _transaction = null;
            _bindings?.Dispose();
            _bindings = null;
            _instance?.Dispose();
            _instance = null;
            _isPublishingCommittedEvents = false;
            _stopAfterPublish = false;
            _disposeAfterPublish = false;
            _reliableOverflowPending = false;
            _requiresWorldCorrection = false;
            _lastAutomaticFrame = -1;
        }

        private void DisposeHostFromLegalState()
        {
            if (_isDisposed)
            {
                return;
            }

            _acceptsEventInput = false;
            DisposeInstance();
            _hasStoppedInstance = false;
            _isDisposed = true;
            AdvanceInputAuthorityRevision();
        }

        private void ForceDisposeHost()
        {
            if (_isDisposed)
            {
                return;
            }

            // Unity destruction cannot be rejected. It still closes a live instance through
            // the frozen Running/Suspended -> Stopped -> Disposed lifecycle edges.
            _acceptsEventInput = false;
            if (_instance != null &&
                (_instance.Lifecycle == RuntimeLifecycleState.Running ||
                 _instance.Lifecycle == RuntimeLifecycleState.Suspended))
            {
                if (!_instance.TryStop(out _))
                {
                    _instance.Dispose();
                }

                AdvanceInputAuthorityRevision();
            }

            DisposeInstance();
            _hasStoppedInstance = false;
            _isDisposed = true;
            AdvanceInputAuthorityRevision();
        }

        private void AdvanceInputAuthorityRevision(bool clearStateInbox = true)
        {
            if (clearStateInbox)
            {
                _events?.Inbox.Clear();
            }
            _inputAuthorityRevision =
                _inputAuthorityRevision == ulong.MaxValue
                    ? 1UL
                    : _inputAuthorityRevision + 1UL;
        }

        private static bool IsPositiveFinite(double value) =>
            value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);

        private static CoCoDiagnostic LifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);

        /// <summary>标识由宿主协调的预览、分支确认、取消或场景校正请求。</summary>
        private enum TemporalOperation
        {
            Begin = 0,
            Preview = 1,
            Confirm = 2,
            Cancel = 3,
            Correct = 4
        }

        /// <summary>在候选提交期间提供销毁检查，标记已提交事件发布的开始和结束。</summary>
        private sealed class CommitGuard : IStateGraphCommitGuard, IHostEventPublication
        {
            private readonly StateGraphHost _host;

            public CommitGuard(StateGraphHost host)
            {
                _host = host;
            }

            public void BeginCommittedEventPublication() => _host.BeginCommittedEventPublication();
            public void EndCommittedEventPublication() => _host.EndCommittedEventPublication();

            public bool IsCommitCancellationRequested => _host._destroyRequested;
        }

        #endregion
    }
}

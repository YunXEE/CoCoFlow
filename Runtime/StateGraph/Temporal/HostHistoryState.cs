// Host 和调试器用这些值查看单个实例的已提交帧和预览位置。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>指示实例是否记录历史以及是否正在预览。</summary>
    public enum HostHistoryMode
    {
        /// <summary>历史容量为零，或实例已释放。</summary>
        Disabled = 0,
        /// <summary>实例可以记录新的提交帧。</summary>
        Ready = 1,
        /// <summary>场景正在预览该实例保留的历史。</summary>
        Previewing = 2
    }

    /// <summary>一次已提交 Context 的身份，可在读取历史后保留。</summary>
    public readonly struct HostHistoryFrame
    {
        #region Public API

        /// <summary>拥有该帧的图实例。</summary>
        public GraphInstanceId GraphInstanceId { get; }
        /// <summary>包含 Tick、时间线代次和执行序号的时钟快照。</summary>
        public TickFrame TickFrame { get; }
        /// <summary>该次 Context 提交的修订号。</summary>
        public ContextRevision Revision { get; }
        /// <summary>标明本帧的提交或恢复来源。</summary>
        public ContextFrameOrigin Origin { get; }
        /// <summary>帧身份、时钟、修订号和来源均有效时为真。</summary>
        public bool IsValid => GraphInstanceId.IsValid && TickFrame.IsValid &&
                               Revision.IsValid && Origin.IsValid;

        #endregion

        #region Internal Logic

        internal HostHistoryFrame(GraphInstanceId graphInstanceId, in TickFrame tickFrame,
            ContextRevision revision, ContextFrameOrigin origin)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            Revision = revision;
            Origin = origin;
        }

        internal static HostHistoryFrame From(ContextFrame frame) =>
            frame.IsAlive
                ? new HostHistoryFrame(frame.Header.Identity.GraphInstanceId, frame.Header.TickFrame,
                    frame.Revision, frame.Origin)
                : default;

        #endregion
    }

    /// <summary>查询时复制的历史状态。继续步进或结束预览后可重新向 Host 查询。</summary>
    public readonly struct HostHistoryState
    {
        #region Public API

        /// <summary>查询时的历史模式。</summary>
        public HostHistoryMode Mode { get; }
        /// <summary>最多可保留的提交帧数。</summary>
        public int Capacity { get; }
        /// <summary>查询时仍保留的提交帧数。</summary>
        public int Count { get; }
        /// <summary>从当前帧向过去回看的步数，零表示当前帧。</summary>
        public int PreviewDepth { get; }
        /// <summary>当前已提交的帧；首次提交前其身份无效。</summary>
        public HostHistoryFrame Current { get; }
        /// <summary>已投影到场景的预览帧。未选择旧帧时为当前帧。</summary>
        public HostHistoryFrame Preview { get; }
        /// <summary>该实例报告的回退丢弃数；现有恢复路径返回零。</summary>
        public ulong RewindRestoreDropped { get; }
        /// <summary>当前选择较早的有效历史帧且场景参与者允许确认时为真。</summary>
        public bool CanConfirm { get; }

        #endregion

        #region Internal Logic

        internal HostHistoryState(HostHistoryMode mode, int capacity, int count, int previewDepth,
            in HostHistoryFrame current, in HostHistoryFrame preview, ulong rewindRestoreDropped,
            bool canConfirm)
        {
            Mode = mode;
            Capacity = capacity;
            Count = count;
            PreviewDepth = previewDepth;
            Current = current;
            Preview = preview;
            RewindRestoreDropped = rewindRestoreDropped;
            CanConfirm = canConfirm;
        }

        #endregion
    }
}

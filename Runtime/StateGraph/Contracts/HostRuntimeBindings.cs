using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 存放一个实例的运行绑定和输入游标，场景 Host 选择提交或丢弃的时点。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>持有一次 Host 启动的输入、槽布局和图绑定，保留重试所需的输入帧。</summary>
    internal sealed class HostRuntimeBindings : IDisposable
    {
        private readonly IHostIntentContribution[] _intentContributions;
        private TickFrame _cachedIntentTick;
        private IIntentFrame _cachedIntentFrame;
        private bool _hasCachedIntentTick;

        public HostRuntimeBindings(
            StateGraphLogicBindings logic,
            OperationFrame operations,
            ContextFrameLayout contextLayout,
            ContextCodecRegistry contextCodecs,
            IGraphContextProducerBinding[] contextProducers,
            IntentFrameRuntime intents,
            IHostIntentContribution[] intentContributions)
        {
            Logic = logic;
            Operations = operations;
            ContextLayout = contextLayout;
            ContextCodecs = contextCodecs;
            ContextProducers = contextProducers ?? Array.Empty<IGraphContextProducerBinding>();
            Intents = intents;
            _intentContributions = intentContributions;
        }

        public StateGraphLogicBindings Logic { get; }
        public OperationFrame Operations { get; }
        public ContextFrameLayout ContextLayout { get; }
        public ContextCodecRegistry ContextCodecs { get; }
        internal IReadOnlyList<IGraphContextProducerBinding> ContextProducers { get; }
        public IntentFrameRuntime Intents { get; }

        public bool TryCollectIntents(
            in TickFrame tickFrame,
            out IIntentFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (_hasCachedIntentTick)
            {
                if (_cachedIntentTick == tickFrame)
                {
                    frame = _cachedIntentFrame;
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                frame = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    "The unresolved Intent Tick must be retried or discarded before collecting a later Tick.");
                return false;
            }

            frame = null;
            if (Intents == null)
            {
                _cachedIntentTick = tickFrame;
                _cachedIntentFrame = null;
                _hasCachedIntentTick = true;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (!StateFlowFrameHeader.TryCreate(
                    Intents.GraphInstanceId,
                    Intents.LayoutId,
                    StateFlowFrameKind.Intent,
                    tickFrame,
                    out StateFlowFrameHeader header))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Frame,
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "IntentFrame header did not match the Host Tick and layout.");
                return false;
            }

            if (!Intents.TryBegin(header, out diagnostic))
            {
                return false;
            }

            for (int index = 0; index < _intentContributions.Length; index++)
            {
                if (!_intentContributions[index].TryCollect(Intents, tickFrame))
                {
                    Intents.CancelCollection();
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Intent,
                        CoCoDiagnosticCode.InvalidIntentContribution,
                        "An Intent Source or Event Adapter failed while collecting the Tick.");
                    return false;
                }
            }

            if (!Intents.TryFreeze(out diagnostic))
            {
                return false;
            }

            frame = Intents.Frame;
            _cachedIntentTick = tickFrame;
            _cachedIntentFrame = frame;
            _hasCachedIntentTick = true;
            return true;
        }

        public void ResolveIntentTick(in TickFrame tickFrame)
        {
            if (!IsIntentTickCommitReady(tickFrame))
            {
                return;
            }

            ResolveIntentTickNoFail();
        }

        internal bool IsIntentTickCommitReady(in TickFrame tickFrame) =>
            _hasCachedIntentTick && _cachedIntentTick == tickFrame;

        internal void CommitIntentTickNoFail()
        {
            foreach (IHostIntentContribution contribution in _intentContributions)
            {
                IntentSourceCursor cursor = contribution.Cursor;
                if (cursor != null)
                {
                    cursor.Committed = cursor.Prepared;
                }
            }

            ResolveIntentTickNoFail();
        }

        private void ResolveIntentTickNoFail()
        {
            _cachedIntentTick = default;
            _cachedIntentFrame = null;
            _hasCachedIntentTick = false;
        }

        public void Dispose()
        {
            _cachedIntentTick = default;
            _cachedIntentFrame = null;
            _hasCachedIntentTick = false;
            Intents?.Dispose();
        }
    }

    /// <summary>描述一个按已登记顺序为本实例提交输入的场景来源。</summary>
    internal interface IHostIntentContribution
    {
        int IntentSourceHostIndex { get; }

        IntentSourceCursor Cursor { get; }

        bool TryAssignRegistrationOrder(int registrationOrder);

        bool TryCollect(IntentFrameRuntime runtime, in TickFrame tickFrame);
    }

    /// <summary>保存一个显式来源与实例游标，成功提交后才接受新位置。</summary>
    internal sealed class HostIntentSourceContribution<TIntent> : IHostIntentContribution
    {
        private readonly IntentSourceBinding<TIntent> _binding;
        private readonly int _hostIndex;

        public HostIntentSourceContribution(
            IntentSourceBinding<TIntent> binding,
            int hostIndex)
        {
            _binding = binding;
            _hostIndex = hostIndex;
            _binding.AttachHostCursor();
        }

        public int IntentSourceHostIndex => _hostIndex;

        public IntentSourceCursor Cursor => _binding.Cursor;

        public bool TryAssignRegistrationOrder(int registrationOrder) =>
            _binding.TryAssignRegistrationOrder(registrationOrder);

        public bool TryCollect(IntentFrameRuntime runtime, in TickFrame tickFrame)
        {
            IntentSourceSampleResult result = runtime.TrySample(_binding, tickFrame);
            return result == IntentSourceSampleResult.Contributed ||
                   result == IntentSourceSampleResult.NoValue;
        }
    }

}

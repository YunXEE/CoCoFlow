using CoCoFlow.Runtime.Input.Contracts;
using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// 外部纯玩法程序集通过这个样例编译状态、输入和输出属性，持续验证公开合同。
namespace CoCoFlow.CI
{
    /// <summary>模拟只引用纯合同的项目状态，同时使用输入、输出、配置、收件和转换请求。</summary>
    [State("ExternalState", ProvidesActionProgress = true)]
    public sealed class ExternalState : StateLogic, IStateEnter, IStateUpdate, IStateExit
    {

        #region Public API

        [Intent] public InputIntentContract Input { get; set; }
        [Intent] public string CustomIntent { get; set; }
        [Operation] public IExternalOperation Output { get; set; }
        [Config] public float Duration;
        [Config] public string ClipKey;
        [Memory] public int Visits;

        /// <summary>在进入回调读取当前输入，编译检查变更列表、动作名与采样栅栏均可从纯项目访问。</summary>
        void IStateEnter.OnEnter(StateExecutionContext context) => ReadInput(Input);

        /// <summary>读上一份 Context 与收件，写项目输出，并编译验证记忆、进度和出边请求的公开读写法。</summary>
        void IStateUpdate.Update(StateExecutionContext context)
        {
            _ = CoCoServices.Get<object>();
            _ = CoreApi.GetService<object>();
            ContextFrameReadView previous = context.PreviousContext;
            if (previous.Layout.TryResolveSlot<int>(default, out var slot))
                _ = previous.Read(slot);
            bool requestedByHandle = context.RequestTransition(default);
            bool requestedByType = context.TryRequestTransitionTo<ExternalState>();
            bool progressed = context.TrySetActionProgress(Duration);
            ExternalMemory memory = context.Memory<ExternalMemory>();
            if (context.Inbox.TryGetBatch<int>(out var batch) && batch.TryRead(0, out int payload))
                context.Outbox.Publish(in payload);
            Output.Value = Visits;
            _ = CustomIntent;
        }

        /// <summary>以外部程序集的显式接口实现接收退出回调，供编译器核对参数和可见性。</summary>
        void IStateExit.OnExit(StateExecutionContext context) { }

        #endregion

        #region Internal Logic

        private static void ReadInput(InputIntentContract input)
        {
            FixedString64 activeMap = input.ActiveMap;
            ReadOnlyMemory<RawInputRecord> changes = input.Changes;
            ReadOnlyMemory<RawInputRecord> current = input.Current;
            ulong fence = input.FenceGeneration;
            int count = input.Count;
            bool indexed = input.TryGet(0, out RawInputRecord record);
            bool foundByName = input.TryFind("ExternalAction", RawInputPhase.Performed, out record);
            bool foundById = input.TryFind(activeMap, RawInputPhase.Performed, out record);
            ulong sequence = record.Sequence;
            double inputTime = record.InputTime;
        }

        /// <summary>让外部状态通过公开回调取回该激活的类型化记忆。</summary>
        private sealed class ExternalMemory : StateActivationMemory { }

        #endregion
    }

    /// <summary>示范项目如何用直接收件过滤器拒绝零载荷。</summary>
    internal sealed class ExternalInboxFilter : IEventInboxAdapter<int>
    {
        public bool Accept(in int payload) => payload != 0;
    }

    /// <summary>为外部编译样例声明连续输出，用生成视图向框架写入整数。</summary>
    [Operation(Mode.Continuous)]
    public interface IExternalOperation : IOperation
    {
        int Value { get; set; }
    }
}

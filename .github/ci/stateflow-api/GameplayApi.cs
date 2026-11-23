using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.CI
{
    [CoCoState("ExternalState", ProvidesActionProgress = true)]
    public sealed class ExternalState : CoCoStateLogic, ICoCoStateEnter, ICoCoStateUpdate, ICoCoStateExit
    {
        [Intent] public InputIntentData Input { get; set; }
        [Intent] public string CustomIntent { get; set; }
        [Operation] public IExternalSection Section { get; set; }
        [Config] public float Duration;
        [Memory] public int Visits;

        void ICoCoStateEnter.OnEnter(CoCoStateExecutionContext context) => ReadInput(Input);

        void ICoCoStateUpdate.Update(CoCoStateExecutionContext context)
        {
            _ = CoCoServices.Get<object>();
            _ = CoreApi.GetService<object>();
            CoCoContextFrameReadView previous = context.PreviousContext;
            if (previous.Layout.TryResolveSlot<int>(default, out var slot))
                _ = previous.Read(slot);
            CoCoOperationSectionField<int> field = context.Operations.ResolveField<IExternalSection, int>(0);
            bool wrote = context.Operations.Write(field, in Visits);
            bool triggered = context.Operations.TryEnableDiscrete<IExternalSection>();
            bool requestedByHandle = context.RequestTransition(default);
            bool requestedByType = context.TryRequestTransitionTo<ExternalState>();
            bool progressed = context.TrySetActionProgress(Duration);
            ExternalMemory memory = context.Memory<ExternalMemory>();
            if (context.Inbox.TryGetBatch<int>(out var batch) && batch.TryRead(0, out int payload))
                context.Outbox.Publish(in payload);
            Section.Value = Visits;
            _ = CustomIntent;
        }

        void ICoCoStateExit.OnExit(CoCoStateExecutionContext context) { }

        private static void ReadInput(InputIntentData input)
        {
            CoCoFixedString64 activeMap = input.ActiveMap;
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

        private sealed class ExternalMemory : CoCoActivationMemory { }
    }

    internal sealed class ExternalInboxFilter : ICoCoEventInboxAdapter<int>
    {
        public bool Accept(in int payload) => payload != 0;
    }

    internal sealed class ExternalCondition : ICoCoStateConditionEvaluator
    {
        bool ICoCoStateConditionEvaluator.Evaluate(CoCoConditionEvaluationContext context) =>
            context.Intents.TryFirst<InputIntentData>(out _);
    }

    [CoCoOperationSection(CoCoOperationSectionMode.Continuous)]
    public interface IExternalSection : ICoCoOperationSection
    {
        int Value { get; set; }
    }
}

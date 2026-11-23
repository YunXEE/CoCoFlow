using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.CI
{
    [State("ExternalState", ProvidesActionProgress = true)]
    public sealed class ExternalState : StateLogic, IStateEnter, IStateUpdate, IStateExit
    {
        [Intent] public InputIntentData Input { get; set; }
        [Intent] public string CustomIntent { get; set; }
        [Operation] public IExternalSection Section { get; set; }
        [Config] public float Duration;
        [Memory] public int Visits;

        void IStateEnter.OnEnter(StateExecutionContext context) => ReadInput(Input);

        void IStateUpdate.Update(StateExecutionContext context)
        {
            _ = CoCoServices.Get<object>();
            _ = CoreApi.GetService<object>();
            ContextFrameReadView previous = context.PreviousContext;
            if (previous.Layout.TryResolveSlot<int>(default, out var slot))
                _ = previous.Read(slot);
            OperationSectionField<int> field = context.Operations.ResolveField<IExternalSection, int>(0);
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

        void IStateExit.OnExit(StateExecutionContext context) { }

        private static void ReadInput(InputIntentData input)
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

        private sealed class ExternalMemory : ActivationMemory { }
    }

    internal sealed class ExternalInboxFilter : IEventInboxAdapter<int>
    {
        public bool Accept(in int payload) => payload != 0;
    }

    internal sealed class ExternalCondition : IStateConditionEvaluator
    {
        bool IStateConditionEvaluator.Evaluate(ConditionEvaluationContext context) =>
            context.Intents.TryFirst<InputIntentData>(out _);
    }

    [OperationSection(OperationSectionMode.Continuous)]
    public interface IExternalSection : IOperationSection
    {
        int Value { get; set; }
    }
}

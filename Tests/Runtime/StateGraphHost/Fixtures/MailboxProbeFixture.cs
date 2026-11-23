using System;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public readonly struct MailboxDamage
    {
        public readonly GraphInstanceId Target;
        public readonly float RawDamage;
        public MailboxDamage(GraphInstanceId target, float rawDamage)
        {
            Target = target;
            RawDamage = rawDamage;
        }
    }

    public readonly struct MailboxHeal
    {
        public readonly float Amount;
        public MailboxHeal(float amount) { Amount = amount; }
    }

    public struct MailboxNotice { public int Value; }

    [State(nameof(MailboxProbeLogic), ProvidesActionProgress = true)]
    public sealed class MailboxProbeLogic : StateLogic, IStateUpdate, IStateEnter, IStateExit
    {
        [Memory] public float Health = 100f;
        public static bool IgnoreDamage;
        public static Action<MailboxProbeLogic, StateExecutionContext> Tick;
        public static Action<StateExecutionContext> Enter;
        public static Action<StateExecutionContext> Exit;
        public void OnEnter(StateExecutionContext context) => Enter?.Invoke(context);
        public void OnExit(StateExecutionContext context) => Exit?.Invoke(context);
        public void Update(StateExecutionContext context)
        {
            if (!IgnoreDamage && context.Inbox.TryGetBatch<MailboxDamage>(out var damages))
                for (int i = 0; i < damages.Count; i++)
                    if (damages.TryRead(i, out var mail)) Health = Math.Max(0f, Health - mail.RawDamage * 0.8f);
            if (context.Inbox.TryGetBatch<MailboxHeal>(out var heals))
                for (int i = 0; i < heals.Count; i++)
                    if (heals.TryRead(i, out var mail)) Health = Math.Min(100f, Health + mail.Amount);
            context.TrySetActionProgress(0.8);
            Tick?.Invoke(this, context);
        }
    }

    [State(nameof(MailboxPlainLogic), ProvidesActionProgress = true)]
    public sealed class MailboxPlainLogic : StateLogic, IStateUpdate
    {
        public static Action<StateExecutionContext> Tick;
        public void Update(StateExecutionContext context)
        {
            context.TrySetActionProgress(0.8);
            Tick?.Invoke(context);
        }
    }

    [State(nameof(MailboxAnyLogic))]
    public sealed class MailboxAnyLogic : AnyStateLogic
    {
        [Config] public int Increment = 2;
        [Memory] public int Total = 10;
        public static Action Tick;

        public override void Update(StateExecutionContext context)
        {
            Total += Increment;
            if (context.Inbox.TryGetBatch<MailboxDamage>(out var damages))
                for (int index = 0; index < damages.Count; index++)
                    if (damages.TryRead(index, out var damage)) Total -= (int)damage.RawDamage;
            context.Outbox.Publish(new MailboxNotice { Value = Total });
            Tick?.Invoke();
        }
    }

    [State(nameof(MailboxStatelessAnyLogic))]
    public sealed class MailboxStatelessAnyLogic : AnyStateLogic
    {
        public override void Update(StateExecutionContext context) { }
    }

    [Condition]
    public sealed class MailboxReadyCondition : StateCondition, IStateConditionEvaluator
    {
        public static bool Allowed;
        public static int Calls;
        public bool Evaluate(ConditionEvaluationContext context)
        {
            Calls++;
            return Allowed && context.ActionProgress >= 0.8;
        }
    }
}

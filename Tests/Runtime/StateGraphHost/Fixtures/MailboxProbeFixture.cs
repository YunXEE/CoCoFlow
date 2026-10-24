using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public readonly struct MailboxDamage
    {
        public readonly CoCoGraphInstanceId Target;
        public readonly float RawDamage;
        public MailboxDamage(CoCoGraphInstanceId target, float rawDamage)
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

    [CoCoState(nameof(MailboxProbeLogic), ProvidesActionProgress = true)]
    public sealed class MailboxProbeLogic : CoCoStateLogic, ICoCoStateUpdate, ICoCoStateEnter, ICoCoStateExit
    {
        [Memory] public float Health = 100f;
        public static bool IgnoreDamage;
        public static Action<MailboxProbeLogic, CoCoStateExecutionContext> Tick;
        public static Action<CoCoStateExecutionContext> Enter;
        public static Action<CoCoStateExecutionContext> Exit;
        public void OnEnter(CoCoStateExecutionContext context) => Enter?.Invoke(context);
        public void OnExit(CoCoStateExecutionContext context) => Exit?.Invoke(context);
        public void Update(CoCoStateExecutionContext context)
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

    [CoCoState(nameof(MailboxPlainLogic), ProvidesActionProgress = true)]
    public sealed class MailboxPlainLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public static Action<CoCoStateExecutionContext> Tick;
        public void Update(CoCoStateExecutionContext context)
        {
            context.TrySetActionProgress(0.8);
            Tick?.Invoke(context);
        }
    }

    [CoCoState(nameof(MailboxAnyLogic))]
    public sealed class MailboxAnyLogic : CoCoAnyStateLogic
    {
        [Config] public int Increment = 2;
        [Memory] public int Total = 10;
        public static Action Tick;

        public override void Update(CoCoStateExecutionContext context)
        {
            Total += Increment;
            if (context.Inbox.TryGetBatch<MailboxDamage>(out var damages))
                for (int index = 0; index < damages.Count; index++)
                    if (damages.TryRead(index, out var damage)) Total -= (int)damage.RawDamage;
            context.Outbox.Publish(new MailboxNotice { Value = Total });
            Tick?.Invoke();
        }
    }

    [CoCoState(nameof(MailboxStatelessAnyLogic))]
    public sealed class MailboxStatelessAnyLogic : CoCoAnyStateLogic
    {
        public override void Update(CoCoStateExecutionContext context) { }
    }

    [Condition]
    public sealed class MailboxReadyCondition : CoCoStateCondition, ICoCoStateConditionEvaluator
    {
        public static bool Allowed;
        public static int Calls;
        public bool Evaluate(CoCoConditionEvaluationContext context)
        {
            Calls++;
            return Allowed && context.ActionProgress >= 0.8;
        }
    }
}

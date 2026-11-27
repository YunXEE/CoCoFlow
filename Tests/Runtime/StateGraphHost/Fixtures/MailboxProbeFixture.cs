using System;
using CoCoFlow.Runtime.StateGraph;

// 收件测试通过普通和 AnyState 读取冻结事件、写出通知并请求图上已有的转换。
namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>为收件测试携带目标实例和原始伤害值。</summary>
    public readonly struct MailboxDamage
    {
        public readonly GraphInstanceId Target;
        public readonly float RawDamage;
        /// <summary>在同一载荷中记录目标实例和原始伤害，供过滤与生命值计算。</summary>
        public MailboxDamage(GraphInstanceId target, float rawDamage)
        {
            Target = target;
            RawDamage = rawDamage;
        }
    }

    /// <summary>将本次治疗量作为独立的非托管收件载荷。</summary>
    public readonly struct MailboxHeal
    {
        public readonly float Amount;
        /// <summary>记录本次治疗量，供状态遍历冻结批次时加入生命值。</summary>
        public MailboxHeal(float amount) { Amount = amount; }
    }

    /// <summary>将回调观察值放入候选出件，供测试检查提交后的发布顺序。</summary>
    public struct MailboxNotice { public int Value; }

    /// <summary>逐条读取冻结伤害和治疗更新生命值，并提供可观察的生命周期回调。</summary>
    [State(nameof(MailboxProbeLogic), ProvidesActionProgress = true)]
    public sealed class MailboxProbeLogic : StateLogic, IStateUpdate, IStateEnter, IStateExit
    {
        [Memory] public float Health = 100f;
        public static bool IgnoreDamage;
        public static Action<MailboxProbeLogic, StateExecutionContext> Tick;
        public static Action<StateExecutionContext> Enter;
        public static Action<StateExecutionContext> Exit;
        /// <summary>在本次进入期间把有效上下文传给测试配置的观察回调。</summary>
        public void OnEnter(StateExecutionContext context) => Enter?.Invoke(context);
        /// <summary>在本次退出期间把有效上下文传给测试配置的观察回调。</summary>
        public void OnExit(StateExecutionContext context) => Exit?.Invoke(context);
        /// <summary>逐条应用当前步的伤害与治疗，报告动作进度，再让用例决定是否请求边。</summary>
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

    /// <summary>提供一条可真实切换到的目标状态，更新时报告进度与回调。</summary>
    [State(nameof(MailboxPlainLogic), ProvidesActionProgress = true)]
    public sealed class MailboxPlainLogic : StateLogic, IStateUpdate
    {
        public static Action<StateExecutionContext> Tick;
        /// <summary>提交本状态的候选进度，并通知用例目标状态已实际更新。</summary>
        public void Update(StateExecutionContext context)
        {
            context.TrySetActionProgress(0.8);
            Tick?.Invoke(context);
        }
    }

    /// <summary>在本层常驻激活内累加配置并处理伤害，将总值写入候选出件。</summary>
    [State(nameof(MailboxAnyLogic))]
    public sealed class MailboxAnyLogic : AnyStateLogic
    {
        [Config] public int Increment = 2;
        [Memory] public int Total = 10;
        public static Action Tick;

        /// <summary>将节点配置与冻结伤害合入该层的常驻记忆，并暂存含新总值的出件。</summary>
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

    /// <summary>为不携带记忆字段的常驻状态提供可运行的回调。</summary>
    [State(nameof(MailboxStatelessAnyLogic))]
    public sealed class MailboxStatelessAnyLogic : AnyStateLogic
    {
        /// <summary>提供空的常驻回调，使没有记忆字段的层也能经标准 Host 步进。</summary>
        public override void Update(StateExecutionContext context) { }
    }

}

using CoCoFlow.Tests.StateGraphTransitiveDependencyHelper;
using CoCoFlow.Runtime.Camera.Contracts;
using CoCoFlow.Runtime.Locomotion.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// 普通玩法程序集声明项目状态、直接收件和操作，实际 Host 调用逻辑并向项目与随包 Operator 提供输出。
namespace CoCoFlow.Tests.GameplayBinding
{
    /// <summary>将项目自定义的目标名称作为输入传给伤害玩法状态。</summary>
    public readonly struct EnemyIntentData
    {
        /// <summary>将目标名称保存在这一输入值中，供实际回调读取。</summary>
        public EnemyIntentData(string target) => Target = target;
        public string Target { get; }
    }

    /// <summary>用项目自定义的连续操作把攻击窗口由 State 交给伤害 Operator。</summary>
    [Operation(Mode.Continuous)]
    public interface IDamageWindowSection : IOperation
    {
        int Window { get; set; }
    }

    /// <summary>为嵌套的离散输出提供外部声明位置，供生成器和 Player 元数据解析。</summary>
    public static class DamageSections
    {
        /// <summary>将一次攻击脉冲声明为离散操作，仅在本步状态实际写入时启用。</summary>
        [Operation(Mode.Discrete)]
        public interface IDamagePulseSection : IOperation
        {
            int Pulse { get; set; }
        }
    }

    /// <summary>故意省略操作模式声明，供标准 Operator 注册检验失败原因。</summary>
    public interface IMissingModeSection : IOperation
    {
        int Value { get; set; }
    }

    /// <summary>携带收件目标玩家和攻击窗口，供直接过滤与状态读取。</summary>
    public struct Interaction
    {
        public int PlayerId;
        public int Window;
    }

    /// <summary>将已提交的命中窗口经定向信封回送给同一图实例。</summary>
    public struct CombatContactEvent
    {
        public int Window;
    }

    /// <summary>将本步发起的攻击窗口在成功提交后发布给订阅者。</summary>
    public struct AttackStarted
    {
        public int Window;
    }

    /// <summary>供项目状态检验直接调用及通过依赖程序集读取同一个 Core 服务。</summary>
    public sealed class GameplayCoreService { }

    /// <summary>从输入和冻结收件取得目标与攻击窗口，将结果写入测试的连续与离散操作。</summary>
    [State("P001.Enemy")]
    public sealed class EnemyLogic : StateLogic, IStateUpdate
    {
        [Intent] public EnemyIntentData Enemy { get; private set; }
        [Operation] public IDamageWindowSection Damage { get; private set; }
        [Operation] public DamageSections.IDamagePulseSection Pulse { get; private set; }
        public static GameplayCoreService DirectService { get; private set; }
        public static GameplayCoreService HelperService { get; private set; }
        public static string Target { get; private set; }
        public static StateEventBatch<Interaction> LastBatch { get; private set; }
        public static int RepeatedRead { get; private set; }
        public static int LastContact { get; private set; }
        public static StateSlotId Ledger { get; set; }
        public static int PreviousLedger { get; private set; }

        /// <summary>读取上一提交账本与本次收件，写入项目伤害操作并暂存攻击开始事件。</summary>
        public void Update(StateExecutionContext context)
        {
            DirectService = CoCoServices.Get<GameplayCoreService>();
            HelperService = TransitiveDependencyHelperToken.GetService<GameplayCoreService>();
            Target = Enemy.Target;
            if (context.PreviousContext.Layout.TryResolveSlot(Ledger, out StateSlot<int> slot))
                PreviousLedger = context.PreviousContext.Read(slot);
            if (context.Inbox.TryGetBatch<Interaction>(out var batch) && batch.TryRead(0, out var interaction))
            {
                LastBatch = batch;
                context.Inbox.TryGetBatch<Interaction>(out var repeated);
                RepeatedRead = repeated.Count;
                Damage.Window = interaction.Window;
                Pulse.Pulse = interaction.Window + 1;
                context.Outbox.Publish(new AttackStarted { Window = interaction.Window });
            }
            if (context.Inbox.TryGetBatch<CombatContactEvent>(out var contacts) && contacts.TryRead(0, out var contact))
                LastContact = contact.Window;
        }
    }

    /// <summary>在一次真实状态回调中组合相机离散请求和角色连续移动，并记录摄像机输入快照。</summary>
    [State("P001.Camera")]
    public sealed class CameraProbeLogic : StateLogic, IStateUpdate
    {
        [Intent] public CameraIntentData Camera { get; private set; }
        public static CameraIntentData Sample { get; private set; }
        [Operation] public CameraOperation Shot { get; private set; }
        [Operation] public ILocomotionOperation Movement { get; private set; }
        public static bool RequestShot { get; set; }

        /// <summary>保存本步相机输入，有机位请求时同时给移动与相机操作写值。</summary>
        public void Update(StateExecutionContext context)
        {
            Sample = Camera;
            if (!RequestShot) return;
            Movement.MoveZ = 2f;
            Shot.RigId = 1;
            Shot.CameraId = 2;
            Shot.Active = true;
            Shot.Priority = 20;
            Shot.SchedulingEnabled = true;
        }
    }
}

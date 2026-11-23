using CoCoFlow.Tests.StateGraphTransitiveDependencyHelper;
using CoCoFlow.Runtime.Camera.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.GameplayBinding
{
    public readonly struct EnemyIntentData
    {
        public EnemyIntentData(string target) => Target = target;
        public string Target { get; }
    }

    [OperationSection(OperationSectionMode.Continuous)]
    public interface IDamageWindowSection : IOperationSection
    {
        int Window { get; set; }
    }

    public static class DamageSections
    {
        [OperationSection(OperationSectionMode.Discrete)]
        public interface IDamagePulseSection : IOperationSection
        {
            int Pulse { get; set; }
        }
    }

    public interface IMissingModeSection : IOperationSection
    {
        int Value { get; set; }
    }

    public struct Interaction
    {
        public int PlayerId;
        public int Window;
    }

    public struct CombatContactEvent
    {
        public int Window;
    }

    public struct AttackStarted
    {
        public int Window;
    }

    public sealed class GameplayCoreService { }

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

        public void Update(StateExecutionContext context)
        {
            DirectService = CoCoServices.Get<GameplayCoreService>();
            HelperService = TransitiveDependencyHelperToken.GetService<GameplayCoreService>();
            Target = Enemy.Target;
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

    [State("P001.Camera")]
    public sealed class CameraProbeLogic : StateLogic, IStateUpdate
    {
        [Intent] public CameraIntentData Camera { get; private set; }
        public static CameraIntentData Sample { get; private set; }
        public void Update(StateExecutionContext context) => Sample = Camera;
    }
}

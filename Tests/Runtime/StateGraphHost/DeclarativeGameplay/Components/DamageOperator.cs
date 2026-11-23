using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using UnityEngine;

namespace CoCoFlow.Tests.GameplayBinding
{
    public sealed class DamageOperator : MonoBehaviour, ITemporalRestoreOperator
    {
        private static readonly OperationSectionRequirement Damage;
        private static readonly OperationSectionRequirement Pulse;
        private static readonly EventOutboxRequirement Contact;
        public static StateSlotId Ledger { get; }

        [OperatorDefinition]
        private static StandardOperatorDefinition Definition { get; }

        static DamageOperator()
        {
            const ulong scope = 0x5030303144414D47UL;
            StateBlockId.TryCreate(scope, 1, out var block);
            StateSlotId.TryCreate(scope, 2, out var slot);
            EventDomainId.TryCreate(scope, out var domain);
            EventTypeId.TryCreate(scope, 4, out var contact);
            OperatorId.TryCreate(scope, 5, out var id);
            Ledger = slot;

            var builder = new StandardOperatorBuilder();
            Damage = builder.Require<IDamageWindowSection>();
            Pulse = builder.Require<DamageSections.IDamagePulseSection>();
            builder.OwnOutcome(block, slot, ContextProjection.Temporal, ContextRestorePolicy.Stored, 0, 1);
            Contact = builder.Emit<CombatContactEvent>(contact, domain, 3 * 32 + 2);
            Definition = builder.Freeze<DamageOperator>(id);
        }

        public OperatorDescriptor Descriptor => Definition.Descriptor;
        public bool Reject { get; set; }
        public int LastWindow { get; private set; }
        public int LastPulse { get; private set; }
        public int PreviousLedger { get; private set; }
        public int RestoredLedger { get; private set; }

        public bool TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome)
        {
            outcome = OperatorOutcome.NoOp;
            if (!context.TryGet(Damage, out OperationSectionEntry<IDamageWindowSection> damage) ||
                !context.TryGet(Pulse, out OperationSectionEntry<DamageSections.IDamagePulseSection> pulse) ||
                !context.PreviousContext.Layout.TryResolveSlot(Ledger, out StateSlot<int> ledger)) return false;

            PreviousLedger = context.PreviousContext.Read(ledger);
            LastWindow = damage.View.Window;
            LastPulse = pulse.Header.Enabled ? pulse.View.Pulse : 0;
            if (LastWindow == 0) return true;

            EventOutboxTarget.TryTargeted(GetComponentInParent<StateGraphHost>().GraphInstanceId, EventReliability.Unreliable,
                default, default, default, out var destination);
            if (!context.TryWriteOutcome(Ledger, LastWindow) ||
                context.EventOutbox.TryWrite(Contact, destination, new CombatContactEvent { Window = LastWindow }) !=
                EventOutboxWriteResult.Accepted) return false;

            outcome = Reject
                ? OperatorOutcome.Rejected(CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operator,
                    CoCoDiagnosticCode.OperatorExecutionFailed, "Rejected by test Operator."))
                : OperatorOutcome.Success;
            return !Reject;
        }
        public bool TryRestore(in ContextRestoreBindingContext context, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            bool read = context.Reader.TryRead(Ledger, out int ledger);
            RestoredLedger = ledger;
            return read;
        }
    }
}

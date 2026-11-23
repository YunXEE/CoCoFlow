using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using UnityEngine;

namespace CoCoFlow.Tests.GameplayBinding
{
    public sealed class DamageOperator : MonoBehaviour, ICoCoTemporalRestoreOperator
    {
        private static readonly CoCoOperationSectionRequirement Damage;
        private static readonly CoCoOperationSectionRequirement Pulse;
        private static readonly CoCoEventOutboxRequirement Contact;
        public static CoCoStateSlotId Ledger { get; }

        [CoCoOperatorDefinition]
        private static CoCoStandardOperatorDefinition Definition { get; }

        static DamageOperator()
        {
            const ulong scope = 0x5030303144414D47UL;
            CoCoStateBlockId.TryCreate(scope, 1, out var block);
            CoCoStateSlotId.TryCreate(scope, 2, out var slot);
            CoCoEventDomainId.TryCreate(scope, out var domain);
            CoCoEventTypeId.TryCreate(scope, 4, out var contact);
            CoCoOperatorId.TryCreate(scope, 5, out var id);
            Ledger = slot;

            var builder = new CoCoStandardOperatorBuilder();
            Damage = builder.Require<IDamageWindowSection>();
            Pulse = builder.Require<DamageSections.IDamagePulseSection>();
            builder.OwnOutcome(block, slot, CoCoContextProjection.Temporal, CoCoContextRestorePolicy.Stored, 0, 1);
            Contact = builder.Emit<CombatContactEvent>(contact, domain, 3 * 32 + 2);
            Definition = builder.Freeze<DamageOperator>(id);
        }

        public CoCoOperatorDescriptor Descriptor => Definition.Descriptor;
        public bool Reject { get; set; }
        public int LastWindow { get; private set; }
        public int LastPulse { get; private set; }
        public int PreviousLedger { get; private set; }
        public int RestoredLedger { get; private set; }

        public bool TryExecute(in CoCoOperatorExecutionContext context, out CoCoOperatorOutcome outcome)
        {
            outcome = CoCoOperatorOutcome.NoOp;
            if (!context.TryGet(Damage, out CoCoOperationSectionEntry<IDamageWindowSection> damage) ||
                !context.TryGet(Pulse, out CoCoOperationSectionEntry<DamageSections.IDamagePulseSection> pulse) ||
                !context.PreviousContext.Layout.TryResolveSlot(Ledger, out CoCoStateSlot<int> ledger)) return false;

            PreviousLedger = context.PreviousContext.Read(ledger);
            LastWindow = damage.View.Window;
            LastPulse = pulse.Header.Enabled ? pulse.View.Pulse : 0;
            if (LastWindow == 0) return true;

            CoCoEventOutboxTarget.TryTargeted(GetComponentInParent<CoCoStateGraphHost>().GraphInstanceId, CoCoEventReliability.Unreliable,
                default, default, default, out var destination);
            if (!context.TryWriteOutcome(Ledger, LastWindow) ||
                context.EventOutbox.TryWrite(Contact, destination, new CombatContactEvent { Window = LastWindow }) !=
                CoCoEventOutboxWriteResult.Accepted) return false;

            outcome = Reject
                ? CoCoOperatorOutcome.Rejected(CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operator,
                    CoCoDiagnosticCode.OperatorExecutionFailed, "Rejected by test Operator."))
                : CoCoOperatorOutcome.Success;
            return !Reject;
        }
        public bool TryRestore(in CoCoContextRestoreBindingContext context, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            bool read = context.Reader.TryRead(Ledger, out int ledger);
            RestoredLedger = ledger;
            return read;
        }
    }
}

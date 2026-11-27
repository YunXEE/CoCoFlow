using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

// 外部玩法的伤害 Operator 从生成视图读取状态写入的数值，更新账本或拒绝候选以检验回滚。
namespace CoCoFlow.Tests.GameplayBinding
{
    /// <summary>在实际 Host 的准备阶段读取项目 Operation，将账本写入候选并可由测试控制拒绝。</summary>
    public sealed class DamageOperator : MonoBehaviour, ITemporalRestoreOperator
    {
        private static readonly OperationRequirement Damage;
        private static readonly OperationRequirement Pulse;
        private static readonly EventOutboxRequirement Contact;
        public static StateSlotId Ledger { get; }

        [OperatorDefinition]
        private static StandardOperatorDefinition Definition { get; }

        public OperatorDescriptor Descriptor => Definition.Descriptor;
        public bool Reject { get; set; }
        public int LastWindow { get; private set; }
        public int LastPulse { get; private set; }
        public int PreviousLedger { get; private set; }
        public int RestoredLedger { get; private set; }

        #region Public API

        /// <summary>读取连续窗口与离散脉冲，写下账本和定向事件。测试指定拒绝时返回 false 以验证回滚。</summary>
        public bool TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome)
        {
            outcome = OperatorOutcome.NoOp;
            if (!context.TryGet(Damage, out OperationEntry<IDamageWindowSection> damage) ||
                !context.TryGet(Pulse, out OperationEntry<DamageSections.IDamagePulseSection> pulse) ||
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
        /// <summary>从恢复读取器取出账本值并保存到测试观察点，找不到匹配槽时返回 false。</summary>
        public bool TryRestore(in ContextRestoreBindingContext context, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            bool read = context.Reader.TryRead(Ledger, out int ledger);
            RestoredLedger = ledger;
            return read;
        }

        #endregion

        #region Internal Logic

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

        #endregion
    }
}

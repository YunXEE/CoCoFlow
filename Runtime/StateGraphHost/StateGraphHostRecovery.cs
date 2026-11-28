using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    public sealed partial class StateGraphHost
    {
        [Serializable]
        internal sealed class OperatorRecovery
        {
            public MonoBehaviour component;
            public bool persistence;
            public bool temporal;
        }

        [SerializeField] private OperatorRecovery[] operatorRecovery = Array.Empty<OperatorRecovery>();
        private OperatorRecovery[] _recovery = Array.Empty<OperatorRecovery>();
        private StateSlotDescriptor[] _retainPersistence, _retainTemporal;
        internal ContextProjection? RecoveryProjection { get; set; }

        internal IReadOnlyList<StateSlotDescriptor> RetainedRecoverySlots =>
            RecoveryProjection == ContextProjection.Durable ? _retainPersistence : _retainTemporal;

        private void FreezeOperatorRecovery(ContextFrameLayout layout)
        {
            var persistenceSlots = new List<StateSlotDescriptor>();
            var temporalSlots = new List<StateSlotDescriptor>();
            _recovery = new OperatorRecovery[Operators.Count];
            RecoveryProjection = null;
            for (int index = 0; index < Operators.Count; index++)
            {
                MonoBehaviour component = Operators[index];
                OperatorRecovery saved = Array.Find(operatorRecovery, entry =>
                    entry != null && ReferenceEquals(entry.component, component));
                var entry = new OperatorRecovery
                {
                    component = component,
                    persistence = component is IPersistenceRestoreOperator && (saved?.persistence ?? false),
                    temporal = component is ITemporalRestoreOperator && (saved?.temporal ?? false)
                };
                _recovery[index] = entry;
                foreach (OperatorOutcomeRequirement outcome in ((IOperator)component).Descriptor.OutcomeRequirements)
                {
                    foreach (StateSlotDescriptor slot in layout.Slots)
                    {
                        if (slot.SlotId != outcome.SlotId) continue;
                        if (!entry.persistence) persistenceSlots.Add(slot);
                        if (!entry.temporal) temporalSlots.Add(slot);
                    }
                }
            }
            _retainPersistence = persistenceSlots.ToArray();
            _retainTemporal = temporalSlots.ToArray();
        }

        internal bool RecoveryOperatorsLive(bool requireCoverage = false)
        {
            foreach (OperatorRecovery entry in _recovery)
            {
                bool recoveryEnabled = RecoveryProjection.HasValue
                    ? RecoveryProjection == ContextProjection.Durable ? entry.persistence : entry.temporal
                    : entry.temporal || entry.persistence;
                if (requireCoverage && !recoveryEnabled) return false;
                if (recoveryEnabled && (entry.component == null ||
                    !StateGraphHostBoundary.Contains(this, entry.component))) return false;
            }
            return true;
        }

        internal bool TryRestoreOperators(in ContextRestoreBindingContext context,
            ref bool worldMayBeDirty, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            foreach (OperatorRecovery entry in _recovery)
            {
                bool temporal = RecoveryProjection != ContextProjection.Durable && entry.temporal;
                bool persistence = RecoveryProjection != ContextProjection.Temporal && entry.persistence;
                if (!temporal && !persistence) continue;
                worldMayBeDirty = true;
                bool applied = temporal
                    ? ((ITemporalRestoreOperator)entry.component).TryRestore(context, out diagnostic)
                    : ((IPersistenceRestoreOperator)entry.component).TryRestore(context, out diagnostic);
                if (!applied || diagnostic.IsError || !RecoveryOperatorsLive() ||
                    IsTemporalOperationCancellationRequested) return false;
            }
            return true;
        }
    }
}

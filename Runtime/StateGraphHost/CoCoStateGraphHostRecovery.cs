using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public sealed partial class CoCoStateGraphHost
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
        private CoCoStateSlotDescriptor[] _retainPersistence, _retainTemporal;
        internal CoCoContextProjection? RecoveryProjection { get; set; }

        internal IReadOnlyList<CoCoStateSlotDescriptor> RetainedRecoverySlots =>
            RecoveryProjection == CoCoContextProjection.Durable ? _retainPersistence : _retainTemporal;

        private void FreezeOperatorRecovery(CoCoContextFrameLayout layout)
        {
            var persistenceSlots = new List<CoCoStateSlotDescriptor>();
            var temporalSlots = new List<CoCoStateSlotDescriptor>();
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
                    persistence = component is ICoCoPersistenceRestoreOperator && (saved?.persistence ?? false),
                    temporal = component is ICoCoTemporalRestoreOperator && (saved?.temporal ?? false)
                };
                _recovery[index] = entry;
                foreach (CoCoOperatorOutcomeRequirement outcome in ((ICoCoOperator)component).Descriptor.OutcomeRequirements)
                {
                    foreach (CoCoStateSlotDescriptor slot in layout.Slots)
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
                    ? RecoveryProjection == CoCoContextProjection.Durable ? entry.persistence : entry.temporal
                    : entry.temporal || entry.persistence;
                if (requireCoverage && !recoveryEnabled) return false;
                if (recoveryEnabled && (entry.component == null ||
                    !CoCoStateGraphHostBoundary.Contains(this, entry.component))) return false;
            }
            return true;
        }

        internal bool TryRestoreOperators(in CoCoContextRestoreBindingContext context,
            ref bool worldMayBeDirty, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            foreach (OperatorRecovery entry in _recovery)
            {
                bool temporal = RecoveryProjection != CoCoContextProjection.Durable && entry.temporal;
                bool persistence = RecoveryProjection != CoCoContextProjection.Temporal && entry.persistence;
                if (!temporal && !persistence) continue;
                worldMayBeDirty = true;
                bool applied = temporal
                    ? ((ICoCoTemporalRestoreOperator)entry.component).TryRestore(context, out diagnostic)
                    : ((ICoCoPersistenceRestoreOperator)entry.component).TryRestore(context, out diagnostic);
                if (!applied || diagnostic.IsError || !RecoveryOperatorsLive() ||
                    IsTemporalOperationCancellationRequested) return false;
            }
            return true;
        }
    }
}

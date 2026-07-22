using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    [Serializable]
    internal struct CoCoSerializedId128 : IEquatable<CoCoSerializedId128>
    {
        [SerializeField] private ulong high;
        [SerializeField] private ulong low;

        internal CoCoSerializedId128(ulong high, ulong low)
        {
            this.high = high;
            this.low = low;
        }

        internal ulong High => high;
        internal ulong Low => low;
        internal bool IsValid => high != 0UL || low != 0UL;

        internal static CoCoSerializedId128 NewId()
        {
            string value = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            ulong parsedHigh = ulong.Parse(
                value.Substring(0, 16),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture);
            ulong parsedLow = ulong.Parse(
                value.Substring(16, 16),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture);
            return new CoCoSerializedId128(parsedHigh, parsedLow);
        }

        public bool Equals(CoCoSerializedId128 other) => high == other.high && low == other.low;
        public override bool Equals(object obj) => obj is CoCoSerializedId128 other && Equals(other);
        public override int GetHashCode() => unchecked((high.GetHashCode() * 397) ^ low.GetHashCode());

        public static bool operator ==(CoCoSerializedId128 left, CoCoSerializedId128 right) => left.Equals(right);
        public static bool operator !=(CoCoSerializedId128 left, CoCoSerializedId128 right) => !left.Equals(right);
    }

    [Serializable]
    internal sealed class CoCoStateGraphLayerRecord
    {
        [SerializeField, HideInInspector] private CoCoSerializedId128 layerId;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField, HideInInspector] private CoCoSerializedId128 initialStateId;
        [SerializeField] private List<CoCoStateGraphStateRecord> states = new List<CoCoStateGraphStateRecord>();
        [SerializeField] private List<CoCoStateGraphTransitionRecord> transitions =
            new List<CoCoStateGraphTransitionRecord>();

        internal CoCoStateGraphLayerRecord(CoCoSerializedId128 layerId, string displayName)
        {
            this.layerId = layerId;
            this.displayName = displayName ?? string.Empty;
        }

        internal CoCoSerializedId128 LayerId
        {
            get => layerId;
            set => layerId = value;
        }

        internal string DisplayName
        {
            get => displayName ?? string.Empty;
            set => displayName = value ?? string.Empty;
        }

        internal CoCoSerializedId128 InitialStateId
        {
            get => initialStateId;
            set => initialStateId = value;
        }

        internal List<CoCoStateGraphStateRecord> States =>
            states ?? (states = new List<CoCoStateGraphStateRecord>());

        internal List<CoCoStateGraphTransitionRecord> Transitions =>
            transitions ?? (transitions = new List<CoCoStateGraphTransitionRecord>());
    }

    [Serializable]
    internal sealed class CoCoStateGraphStateRecord
    {
        [SerializeField, HideInInspector] private CoCoSerializedId128 stateId;
        [SerializeField, HideInInspector] private CoCoSerializedId128 parentStateId;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private CoCoSerializedId128 stateDescriptorId;
        [SerializeReference] private CoCoStateConfig config;
        [SerializeField, HideInInspector] private CoCoSerializedId128 initialChildStateId;

        internal CoCoStateGraphStateRecord(
            CoCoSerializedId128 stateId,
            CoCoSerializedId128 parentStateId,
            string displayName,
            CoCoSerializedId128 stateDescriptorId,
            CoCoStateConfig config)
        {
            this.stateId = stateId;
            this.parentStateId = parentStateId;
            this.displayName = displayName ?? string.Empty;
            this.stateDescriptorId = stateDescriptorId;
            this.config = config;
        }

        internal CoCoSerializedId128 StateId
        {
            get => stateId;
            set => stateId = value;
        }

        internal CoCoSerializedId128 ParentStateId
        {
            get => parentStateId;
            set => parentStateId = value;
        }

        internal string DisplayName
        {
            get => displayName ?? string.Empty;
            set => displayName = value ?? string.Empty;
        }

        internal CoCoSerializedId128 StateDescriptorId
        {
            get => stateDescriptorId;
            set => stateDescriptorId = value;
        }

        internal CoCoStateConfig Config
        {
            get => config;
            set => config = value;
        }

        internal CoCoSerializedId128 InitialChildStateId
        {
            get => initialChildStateId;
            set => initialChildStateId = value;
        }
    }

    [Serializable]
    internal sealed class CoCoStateGraphTransitionRecord
    {
        [SerializeField, HideInInspector] private CoCoSerializedId128 transitionId;
        [SerializeField, HideInInspector] private CoCoSerializedId128 sourceStateId;
        [SerializeField, HideInInspector] private CoCoSerializedId128 targetStateId;
        [SerializeField] private int priority;
        [SerializeField] private List<CoCoStateGraphConditionRecord> conditions =
            new List<CoCoStateGraphConditionRecord>();
        [SerializeField] private CoCoTransitionWindowMode windowMode = CoCoTransitionWindowMode.Always;
        [SerializeField] private double windowStartInclusive;
        [SerializeField] private double windowEndExclusive;

        internal CoCoStateGraphTransitionRecord(
            CoCoSerializedId128 transitionId,
            CoCoSerializedId128 sourceStateId,
            CoCoSerializedId128 targetStateId,
            int priority)
        {
            this.transitionId = transitionId;
            this.sourceStateId = sourceStateId;
            this.targetStateId = targetStateId;
            this.priority = priority;
        }

        internal CoCoSerializedId128 TransitionId
        {
            get => transitionId;
            set => transitionId = value;
        }

        internal CoCoSerializedId128 SourceStateId
        {
            get => sourceStateId;
            set => sourceStateId = value;
        }

        internal CoCoSerializedId128 TargetStateId
        {
            get => targetStateId;
            set => targetStateId = value;
        }

        internal int Priority
        {
            get => priority;
            set => priority = value;
        }
        internal List<CoCoStateGraphConditionRecord> Conditions =>
            conditions ?? (conditions = new List<CoCoStateGraphConditionRecord>());
        internal CoCoTransitionWindowMode WindowMode
        {
            get => windowMode;
            set => windowMode = value;
        }

        internal double WindowStartInclusive
        {
            get => windowStartInclusive;
            set => windowStartInclusive = value;
        }

        internal double WindowEndExclusive
        {
            get => windowEndExclusive;
            set => windowEndExclusive = value;
        }
    }

    [Serializable]
    internal sealed class CoCoStateGraphConditionRecord
    {
        [SerializeField] private CoCoSerializedId128 conditionDescriptorId;
        [SerializeReference] private CoCoConditionConfig config;

        internal CoCoStateGraphConditionRecord(
            CoCoSerializedId128 conditionDescriptorId,
            CoCoConditionConfig config)
        {
            this.conditionDescriptorId = conditionDescriptorId;
            this.config = config;
        }

        internal CoCoSerializedId128 ConditionDescriptorId
        {
            get => conditionDescriptorId;
            set => conditionDescriptorId = value;
        }

        internal CoCoConditionConfig Config
        {
            get => config;
            set => config = value;
        }
    }

    [Serializable]
    internal sealed class CoCoStateGraphEventAdapterDeclarationRecord
    {
        [SerializeField] private CoCoSerializedId128 eventTypeId;
        [SerializeField] private CoCoSerializedId128 providedIntentId;

        internal CoCoStateGraphEventAdapterDeclarationRecord(
            CoCoSerializedId128 eventTypeId,
            CoCoSerializedId128 providedIntentId)
        {
            this.eventTypeId = eventTypeId;
            this.providedIntentId = providedIntentId;
        }

        internal CoCoSerializedId128 EventTypeId => eventTypeId;
        internal CoCoSerializedId128 ProvidedIntentId => providedIntentId;
    }
}

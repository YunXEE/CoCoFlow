using System;
using System.Collections.Generic;
using System.Globalization;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoCoFlow.Runtime.StateGraphHost
{
    [Serializable]
    internal struct SerializedId128 : IEquatable<SerializedId128>
    {
        [SerializeField] private ulong high;
        [SerializeField] private ulong low;

        internal SerializedId128(ulong high, ulong low)
        {
            this.high = high;
            this.low = low;
        }

        internal ulong High => high;
        internal ulong Low => low;
        internal bool IsValid => high != 0UL || low != 0UL;

        internal static SerializedId128 NewId()
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
            return new SerializedId128(parsedHigh, parsedLow);
        }

        public bool Equals(SerializedId128 other) => high == other.high && low == other.low;
        public override bool Equals(object obj) => obj is SerializedId128 other && Equals(other);
        public override int GetHashCode() => unchecked((high.GetHashCode() * 397) ^ low.GetHashCode());

        public static bool operator ==(SerializedId128 left, SerializedId128 right) => left.Equals(right);
        public static bool operator !=(SerializedId128 left, SerializedId128 right) => !left.Equals(right);
    }

    [Serializable]
    internal sealed class StateGraphLayerRecord : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector] private SerializedId128 layerId;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField, HideInInspector] private SerializedId128 initialStateId;
        [SerializeField] private List<StateGraphStateRecord> states = new List<StateGraphStateRecord>();
        [SerializeReference] private StateGraphStateRecord anyState;
        [SerializeField] private List<StateGraphTransitionRecord> transitions =
            new List<StateGraphTransitionRecord>();

        internal StateGraphLayerRecord(SerializedId128 layerId, string displayName)
        {
            this.layerId = layerId;
            this.displayName = displayName ?? string.Empty;
        }

        internal SerializedId128 LayerId
        {
            get => layerId;
            set => layerId = value;
        }

        internal string DisplayName
        {
            get => displayName ?? string.Empty;
            set => displayName = value ?? string.Empty;
        }

        internal SerializedId128 InitialStateId
        {
            get => initialStateId;
            set => initialStateId = value;
        }

        internal List<StateGraphStateRecord> States =>
            states ?? (states = new List<StateGraphStateRecord>());

        internal StateGraphStateRecord AnyState
        {
            get
            {
                UpgradeLegacyAnyState();
                return anyState;
            }
            set => anyState = value;
        }

        // Child managed references have finished deserializing by the first
        // read or save. Their own callbacks may run after the layer callback.
        void ISerializationCallbackReceiver.OnAfterDeserialize() { }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => UpgradeLegacyAnyState();

        private void UpgradeLegacyAnyState()
        {
            if (anyState is LegacyHostStateRecord legacy) anyState = legacy.ToCurrent();
        }

        internal IEnumerable<StateGraphStateRecord> AllStates
        {
            get
            {
                foreach (StateGraphStateRecord state in States)
                {
                    yield return state;
                }

                var layerAnyState = AnyState;
                if (layerAnyState != null)
                {
                    yield return layerAnyState;
                }
            }
        }

        internal List<StateGraphTransitionRecord> Transitions =>
            transitions ?? (transitions = new List<StateGraphTransitionRecord>());
    }

    [MovedFrom(true, sourceNamespace: "CoCoFlow.Runtime.Core", sourceAssembly: "CoCoFlow.Runtime.Core.StateGraphAuthoring", sourceClassName: "CoCoStateGraphStateRecord")]
    [Serializable]
    internal class StateGraphStateRecord : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector] private SerializedId128 stateId;
        [SerializeField, HideInInspector] private SerializedId128 parentStateId;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private SerializedId128 stateDescriptorId;
        [SerializeReference] private StateConfig config;
        [SerializeField, HideInInspector] private SerializedId128 initialChildStateId;

        internal StateGraphStateRecord(
            SerializedId128 stateId,
            SerializedId128 parentStateId,
            string displayName,
            SerializedId128 stateDescriptorId,
            StateConfig config)
        {
            this.stateId = stateId;
            this.parentStateId = parentStateId;
            this.displayName = displayName ?? string.Empty;
            this.stateDescriptorId = stateDescriptorId;
            this.config = config;
        }

        internal SerializedId128 StateId
        {
            get => stateId;
            set => stateId = value;
        }

        internal SerializedId128 ParentStateId
        {
            get => parentStateId;
            set => parentStateId = value;
        }

        internal string DisplayName
        {
            get => displayName ?? string.Empty;
            set => displayName = value ?? string.Empty;
        }

        internal SerializedId128 StateDescriptorId
        {
            get => stateDescriptorId;
            set => stateDescriptorId = value;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (config is ILegacyGraphConfig<StateConfig> legacy) config = legacy.Current;
        }

        internal StateConfig Config
        {
            get => config;
            set => config = value;
        }

        internal SerializedId128 InitialChildStateId
        {
            get => initialChildStateId;
            set => initialChildStateId = value;
        }
    }

    // The intermediate Host assembly also wrote AnyState records under the
    // original Core namespace. The main record handles the older Authoring DLL.
    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.StateGraphHost", "CoCoStateGraphStateRecord")]
    internal class LegacyHostStateRecord : StateGraphStateRecord
    {
        protected LegacyHostStateRecord() : base(default, default, string.Empty, default, null) { }

        internal StateGraphStateRecord ToCurrent()
        {
            var current = new StateGraphStateRecord(StateId, ParentStateId, DisplayName, StateDescriptorId, Config);
            current.InitialChildStateId = InitialChildStateId;
            return current;
        }
    }

    // A graph saved after the assembly move can still contain the prefixed CLR name.
    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.StateGraphHost", "CoCoFlow.Runtime.StateGraphHost", "CoCoStateGraphStateRecord")]
    internal sealed class LegacyPrefixedStateRecord : LegacyHostStateRecord { }

    [Serializable]
    internal sealed class StateGraphTransitionRecord
    {
        [SerializeField, HideInInspector] private SerializedId128 transitionId;
        [SerializeField, HideInInspector] private SerializedId128 sourceStateId;
        [SerializeField, HideInInspector] private SerializedId128 targetStateId;
        [SerializeField] private int priority;
        [SerializeField] private List<StateGraphConditionRecord> conditions =
            new List<StateGraphConditionRecord>();
        [SerializeField] private TransitionWindowMode windowMode = TransitionWindowMode.Always;
        [SerializeField] private double windowStartInclusive;
        [SerializeField] private double windowEndExclusive;

        internal StateGraphTransitionRecord(
            SerializedId128 transitionId,
            SerializedId128 sourceStateId,
            SerializedId128 targetStateId,
            int priority)
        {
            this.transitionId = transitionId;
            this.sourceStateId = sourceStateId;
            this.targetStateId = targetStateId;
            this.priority = priority;
        }

        internal SerializedId128 TransitionId
        {
            get => transitionId;
            set => transitionId = value;
        }

        internal SerializedId128 SourceStateId
        {
            get => sourceStateId;
            set => sourceStateId = value;
        }

        internal SerializedId128 TargetStateId
        {
            get => targetStateId;
            set => targetStateId = value;
        }

        internal int Priority
        {
            get => priority;
            set => priority = value;
        }
        internal List<StateGraphConditionRecord> Conditions =>
            conditions ?? (conditions = new List<StateGraphConditionRecord>());
        internal TransitionWindowMode WindowMode
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
    internal sealed class StateGraphConditionRecord : ISerializationCallbackReceiver
    {
        [SerializeField] private SerializedId128 conditionDescriptorId;
        [SerializeReference] private ConditionConfig config;

        internal StateGraphConditionRecord(
            SerializedId128 conditionDescriptorId,
            ConditionConfig config)
        {
            this.conditionDescriptorId = conditionDescriptorId;
            this.config = config;
        }

        internal SerializedId128 ConditionDescriptorId
        {
            get => conditionDescriptorId;
            set => conditionDescriptorId = value;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (config is ILegacyGraphConfig<ConditionConfig> legacy) config = legacy.Current;
        }

        internal ConditionConfig Config
        {
            get => config;
            set => config = value;
        }
    }

    [Serializable]
    internal sealed class StateGraphEventAdapterDeclarationRecord
    {
        [SerializeField] private SerializedId128 eventTypeId;
        [SerializeField] private SerializedId128 providedIntentId;

        internal StateGraphEventAdapterDeclarationRecord(
            SerializedId128 eventTypeId,
            SerializedId128 providedIntentId)
        {
            this.eventTypeId = eventTypeId;
            this.providedIntentId = providedIntentId;
        }

        internal SerializedId128 EventTypeId => eventTypeId;
        internal SerializedId128 ProvidedIntentId => providedIntentId;
    }
}

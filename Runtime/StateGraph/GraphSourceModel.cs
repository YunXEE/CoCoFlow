using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.StateGraph
{
    public enum TransitionWindowMode
    {
        None = 0,
        Always = 1,
        LocalSeconds = 2,
        ActionProgress = 3
    }

    public readonly struct TransitionWindow : IEquatable<TransitionWindow>
    {
        private TransitionWindow(
            TransitionWindowMode mode,
            double startInclusive,
            double endExclusive)
        {
            Mode = mode;
            StartInclusive = startInclusive;
            EndExclusive = endExclusive;
        }

        public static TransitionWindow Always =>
            new TransitionWindow(TransitionWindowMode.Always, 0d, 0d);

        public TransitionWindowMode Mode { get; }
        public double StartInclusive { get; }
        public double EndExclusive { get; }
        public bool IsValid => (Mode == TransitionWindowMode.Always &&
                                StartInclusive == 0d &&
                                EndExclusive == 0d) ||
                               ((Mode == TransitionWindowMode.LocalSeconds ||
                                 Mode == TransitionWindowMode.ActionProgress) &&
                                IsFinite(StartInclusive) &&
                                IsFinite(EndExclusive) &&
                                StartInclusive >= 0d &&
                                StartInclusive < EndExclusive &&
                                (Mode != TransitionWindowMode.ActionProgress || EndExclusive <= 1d));

        public static bool TryCreate(
            TransitionWindowMode mode,
            double startInclusive,
            double endExclusive,
            out TransitionWindow window)
        {
            if (mode == TransitionWindowMode.Always)
            {
                window = Always;
                return true;
            }

            window = new TransitionWindow(mode, startInclusive, endExclusive);
            if (window.IsValid)
            {
                return true;
            }

            window = default;
            return false;
        }

        public bool Equals(TransitionWindow other)
        {
            return Mode == other.Mode &&
                   StartInclusive.Equals(other.StartInclusive) &&
                   EndExclusive.Equals(other.EndExclusive);
        }

        public override bool Equals(object obj) => obj is TransitionWindow other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Mode;
                hashCode = (hashCode * 397) ^ StartInclusive.GetHashCode();
                hashCode = (hashCode * 397) ^ EndExclusive.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(TransitionWindow left, TransitionWindow right) =>
            left.Equals(right);

        public static bool operator !=(TransitionWindow left, TransitionWindow right) =>
            !left.Equals(right);

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class ConditionSource
    {
        public ConditionSource(
            ConditionDescriptorId descriptorId,
            FrozenConfigSnapshot config)
        {
            DescriptorId = descriptorId;
            Config = config;
        }

        public ConditionDescriptorId DescriptorId { get; }
        public FrozenConfigSnapshot Config { get; }
    }

    public sealed class TransitionSource
    {
        private readonly IReadOnlyList<ConditionSource> _conditions;

        public TransitionSource(
            TransitionId transitionId,
            StateId sourceStateId,
            StateId targetStateId,
            int priority,
            TransitionWindow window,
            IReadOnlyList<ConditionSource> conditions)
        {
            TransitionId = transitionId;
            SourceStateId = sourceStateId;
            TargetStateId = targetStateId;
            Priority = priority;
            Window = window;
            _conditions = GraphSourceCollections.Clone(conditions);
        }

        public TransitionId TransitionId { get; }
        public StateId SourceStateId { get; }
        public StateId TargetStateId { get; }
        public int Priority { get; }
        public TransitionWindow Window { get; }
        public IReadOnlyList<ConditionSource> Conditions => _conditions;
    }

    public sealed class StateSource
    {
        public StateSource(
            StateId stateId,
            StateId parentStateId,
            StateId initialChildStateId,
            StateDescriptorId descriptorId,
            FrozenConfigSnapshot config)
        {
            StateId = stateId;
            ParentStateId = parentStateId;
            InitialChildStateId = initialChildStateId;
            DescriptorId = descriptorId;
            Config = config;
        }

        public StateId StateId { get; }
        public StateId ParentStateId { get; }
        public StateId InitialChildStateId { get; }
        public StateDescriptorId DescriptorId { get; }
        public FrozenConfigSnapshot Config { get; }
    }

    public sealed class StateLayerSource
    {
        private readonly IReadOnlyList<StateSource> _states;
        private readonly IReadOnlyList<TransitionSource> _transitions;

        public StateLayerSource(
            LayerId layerId,
            StateId initialStateId,
            IReadOnlyList<StateSource> states,
            IReadOnlyList<TransitionSource> transitions)
        {
            LayerId = layerId;
            InitialStateId = initialStateId;
            _states = GraphSourceCollections.Clone(states);
            _transitions = GraphSourceCollections.Clone(transitions);
        }

        public StateLayerSource(
            LayerId layerId,
            StateId initialStateId,
            IReadOnlyList<StateSource> states,
            IReadOnlyList<TransitionSource> transitions,
            StateSource anyState)
            : this(layerId, initialStateId, states, transitions)
        {
            AnyState = anyState;
        }

        public LayerId LayerId { get; }
        public StateId InitialStateId { get; }
        public IReadOnlyList<StateSource> States => _states;
        public IReadOnlyList<TransitionSource> Transitions => _transitions;
        public StateSource AnyState { get; }
    }

    public sealed class EventToIntentDeclarationSource
    {
        public EventToIntentDeclarationSource(
            EventTypeId eventTypeId,
            IntentId providedIntentId)
        {
            EventTypeId = eventTypeId;
            ProvidedIntentId = providedIntentId;
        }

        public EventTypeId EventTypeId { get; }
        public IntentId ProvidedIntentId { get; }
    }

    public sealed class StateGraphSource
    {
        private readonly IReadOnlyList<StateLayerSource> _layers;
        private readonly IReadOnlyList<EventToIntentDeclarationSource> _eventAdapterDeclarations;

        public StateGraphSource(
            uint schemaVersion,
            ulong contentFingerprint,
            GraphId graphId,
            IReadOnlyList<StateLayerSource> layers,
            IReadOnlyList<EventToIntentDeclarationSource> eventAdapterDeclarations)
        {
            SchemaVersion = schemaVersion;
            ContentFingerprint = contentFingerprint;
            GraphId = graphId;
            _layers = GraphSourceCollections.Clone(layers);
            _eventAdapterDeclarations =
                GraphSourceCollections.Clone(eventAdapterDeclarations) ??
                Array.AsReadOnly(new EventToIntentDeclarationSource[0]);
        }

        public uint SchemaVersion { get; }
        public ulong ContentFingerprint { get; }
        public GraphId GraphId { get; }
        public IReadOnlyList<StateLayerSource> Layers => _layers;
        public IReadOnlyList<EventToIntentDeclarationSource> EventAdapterDeclarations =>
            _eventAdapterDeclarations;
    }

    internal static class GraphSourceCollections
    {
        public static IReadOnlyList<T> Clone<T>(IReadOnlyList<T> source)
        {
            if (source == null)
            {
                return null;
            }

            var copy = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }
    }
}

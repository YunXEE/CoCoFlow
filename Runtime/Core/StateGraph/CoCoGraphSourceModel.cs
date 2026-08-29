using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public enum CoCoTransitionWindowMode
    {
        None = 0,
        Always = 1,
        LocalSeconds = 2,
        ActionProgress = 3
    }

    public readonly struct CoCoTransitionWindow : IEquatable<CoCoTransitionWindow>
    {
        private CoCoTransitionWindow(
            CoCoTransitionWindowMode mode,
            double startInclusive,
            double endExclusive)
        {
            Mode = mode;
            StartInclusive = startInclusive;
            EndExclusive = endExclusive;
        }

        public static CoCoTransitionWindow Always =>
            new CoCoTransitionWindow(CoCoTransitionWindowMode.Always, 0d, 0d);

        public CoCoTransitionWindowMode Mode { get; }
        public double StartInclusive { get; }
        public double EndExclusive { get; }
        public bool IsValid => (Mode == CoCoTransitionWindowMode.Always &&
                                StartInclusive == 0d &&
                                EndExclusive == 0d) ||
                               ((Mode == CoCoTransitionWindowMode.LocalSeconds ||
                                 Mode == CoCoTransitionWindowMode.ActionProgress) &&
                                IsFinite(StartInclusive) &&
                                IsFinite(EndExclusive) &&
                                StartInclusive >= 0d &&
                                StartInclusive < EndExclusive &&
                                (Mode != CoCoTransitionWindowMode.ActionProgress || EndExclusive <= 1d));

        public static bool TryCreate(
            CoCoTransitionWindowMode mode,
            double startInclusive,
            double endExclusive,
            out CoCoTransitionWindow window)
        {
            if (mode == CoCoTransitionWindowMode.Always)
            {
                window = Always;
                return true;
            }

            window = new CoCoTransitionWindow(mode, startInclusive, endExclusive);
            if (window.IsValid)
            {
                return true;
            }

            window = default;
            return false;
        }

        public bool Equals(CoCoTransitionWindow other)
        {
            return Mode == other.Mode &&
                   StartInclusive.Equals(other.StartInclusive) &&
                   EndExclusive.Equals(other.EndExclusive);
        }

        public override bool Equals(object obj) => obj is CoCoTransitionWindow other && Equals(other);

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

        public static bool operator ==(CoCoTransitionWindow left, CoCoTransitionWindow right) =>
            left.Equals(right);

        public static bool operator !=(CoCoTransitionWindow left, CoCoTransitionWindow right) =>
            !left.Equals(right);

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class CoCoConditionSource
    {
        public CoCoConditionSource(
            CoCoConditionDescriptorId descriptorId,
            CoCoFrozenConfigSnapshot config)
        {
            DescriptorId = descriptorId;
            Config = config;
        }

        public CoCoConditionDescriptorId DescriptorId { get; }
        public CoCoFrozenConfigSnapshot Config { get; }
    }

    public sealed class CoCoTransitionSource
    {
        private readonly IReadOnlyList<CoCoConditionSource> _conditions;

        public CoCoTransitionSource(
            CoCoTransitionId transitionId,
            CoCoStateId sourceStateId,
            CoCoStateId targetStateId,
            int priority,
            CoCoTransitionWindow window,
            IReadOnlyList<CoCoConditionSource> conditions)
        {
            TransitionId = transitionId;
            SourceStateId = sourceStateId;
            TargetStateId = targetStateId;
            Priority = priority;
            Window = window;
            _conditions = CoCoGraphSourceCollections.Clone(conditions);
        }

        public CoCoTransitionId TransitionId { get; }
        public CoCoStateId SourceStateId { get; }
        public CoCoStateId TargetStateId { get; }
        public int Priority { get; }
        public CoCoTransitionWindow Window { get; }
        public IReadOnlyList<CoCoConditionSource> Conditions => _conditions;
    }

    public sealed class CoCoStateSource
    {
        public CoCoStateSource(
            CoCoStateId stateId,
            CoCoStateId parentStateId,
            CoCoStateId initialChildStateId,
            CoCoStateDescriptorId descriptorId,
            CoCoFrozenConfigSnapshot config)
        {
            StateId = stateId;
            ParentStateId = parentStateId;
            InitialChildStateId = initialChildStateId;
            DescriptorId = descriptorId;
            Config = config;
        }

        public CoCoStateId StateId { get; }
        public CoCoStateId ParentStateId { get; }
        public CoCoStateId InitialChildStateId { get; }
        public CoCoStateDescriptorId DescriptorId { get; }
        public CoCoFrozenConfigSnapshot Config { get; }
    }

    public sealed class CoCoStateLayerSource
    {
        private readonly IReadOnlyList<CoCoStateSource> _states;
        private readonly IReadOnlyList<CoCoTransitionSource> _transitions;

        public CoCoStateLayerSource(
            CoCoLayerId layerId,
            CoCoStateId initialStateId,
            IReadOnlyList<CoCoStateSource> states,
            IReadOnlyList<CoCoTransitionSource> transitions)
        {
            LayerId = layerId;
            InitialStateId = initialStateId;
            _states = CoCoGraphSourceCollections.Clone(states);
            _transitions = CoCoGraphSourceCollections.Clone(transitions);
        }

        public CoCoLayerId LayerId { get; }
        public CoCoStateId InitialStateId { get; }
        public IReadOnlyList<CoCoStateSource> States => _states;
        public IReadOnlyList<CoCoTransitionSource> Transitions => _transitions;
    }

    public sealed class CoCoEventToIntentDeclarationSource
    {
        public CoCoEventToIntentDeclarationSource(
            CoCoEventTypeId eventTypeId,
            CoCoIntentId providedIntentId)
        {
            EventTypeId = eventTypeId;
            ProvidedIntentId = providedIntentId;
        }

        public CoCoEventTypeId EventTypeId { get; }
        public CoCoIntentId ProvidedIntentId { get; }
    }

    public sealed class CoCoStateGraphSource
    {
        private readonly IReadOnlyList<CoCoStateLayerSource> _layers;
        private readonly IReadOnlyList<CoCoEventToIntentDeclarationSource> _eventAdapterDeclarations;

        public CoCoStateGraphSource(
            uint schemaVersion,
            ulong contentFingerprint,
            CoCoGraphId graphId,
            IReadOnlyList<CoCoStateLayerSource> layers,
            IReadOnlyList<CoCoEventToIntentDeclarationSource> eventAdapterDeclarations)
        {
            SchemaVersion = schemaVersion;
            ContentFingerprint = contentFingerprint;
            GraphId = graphId;
            _layers = CoCoGraphSourceCollections.Clone(layers);
            _eventAdapterDeclarations =
                CoCoGraphSourceCollections.Clone(eventAdapterDeclarations) ??
                Array.AsReadOnly(new CoCoEventToIntentDeclarationSource[0]);
        }

        public uint SchemaVersion { get; }
        public ulong ContentFingerprint { get; }
        public CoCoGraphId GraphId { get; }
        public IReadOnlyList<CoCoStateLayerSource> Layers => _layers;
        public IReadOnlyList<CoCoEventToIntentDeclarationSource> EventAdapterDeclarations =>
            _eventAdapterDeclarations;
    }

    internal static class CoCoGraphSourceCollections
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

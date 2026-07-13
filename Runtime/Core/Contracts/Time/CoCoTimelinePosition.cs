using System;
using System.Globalization;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoTimelineTick : IEquatable<CoCoTimelineTick>
    {
        public CoCoTimelineTick(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool Equals(CoCoTimelineTick other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CoCoTimelineTick other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CoCoTimelineTick left, CoCoTimelineTick right) => left.Equals(right);
        public static bool operator !=(CoCoTimelineTick left, CoCoTimelineTick right) => !left.Equals(right);
    }

    public readonly struct CoCoTimelinePosition : IEquatable<CoCoTimelinePosition>
    {
        private CoCoTimelinePosition(double seconds)
        {
            Seconds = seconds;
        }

        public double Seconds { get; }
        public bool IsValid => Seconds >= 0d &&
                               !double.IsNaN(Seconds) &&
                               !double.IsInfinity(Seconds);

        public static bool TryCreate(double seconds, out CoCoTimelinePosition position)
        {
            if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                position = new CoCoTimelinePosition(double.NaN);
                return false;
            }

            position = new CoCoTimelinePosition(seconds);
            return true;
        }

        public bool Equals(CoCoTimelinePosition other) => Seconds.Equals(other.Seconds);
        public override bool Equals(object obj) => obj is CoCoTimelinePosition other && Equals(other);
        public override int GetHashCode() => Seconds.GetHashCode();

        public static bool operator ==(CoCoTimelinePosition left, CoCoTimelinePosition right) => left.Equals(right);
        public static bool operator !=(CoCoTimelinePosition left, CoCoTimelinePosition right) => !left.Equals(right);
    }
}

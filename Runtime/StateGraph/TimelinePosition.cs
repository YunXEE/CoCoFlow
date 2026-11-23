using System;
using System.Globalization;

namespace CoCoFlow.Runtime.StateGraph
{
    public readonly struct TimelineTick : IEquatable<TimelineTick>
    {
        public TimelineTick(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool Equals(TimelineTick other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TimelineTick other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(TimelineTick left, TimelineTick right) => left.Equals(right);
        public static bool operator !=(TimelineTick left, TimelineTick right) => !left.Equals(right);
    }

    public readonly struct TimelinePosition : IEquatable<TimelinePosition>
    {
        private TimelinePosition(double seconds)
        {
            Seconds = seconds;
        }

        public double Seconds { get; }
        public bool IsValid => Seconds >= 0d &&
                               !double.IsNaN(Seconds) &&
                               !double.IsInfinity(Seconds);

        public static bool TryCreate(double seconds, out TimelinePosition position)
        {
            if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                position = new TimelinePosition(double.NaN);
                return false;
            }

            position = new TimelinePosition(seconds);
            return true;
        }

        public bool Equals(TimelinePosition other) => Seconds.Equals(other.Seconds);
        public override bool Equals(object obj) => obj is TimelinePosition other && Equals(other);
        public override int GetHashCode() => Seconds.GetHashCode();

        public static bool operator ==(TimelinePosition left, TimelinePosition right) => left.Equals(right);
        public static bool operator !=(TimelinePosition left, TimelinePosition right) => !left.Equals(right);
    }
}

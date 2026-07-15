using System;

namespace CoCoFlow.Runtime.Core
{
    public enum CoCoDiagnosticDomain
    {
        None = 0,
        Contracts = 1,
        Topology = 2,
        Identity = 3,
        Time = 4,
        Lifecycle = 5,
        State = 6,
        Context = 7,
        Operation = 8,
        Frame = 9,
        Registry = 10,
        Intent = 11,
        Mailbox = 12,
        Restore = 13,
        Codec = 14
    }

    public enum CoCoDiagnosticCode
    {
        None = 0,
        InvalidIdentifier = 1,
        DuplicateIdentifier = 2,
        CrossLayerReference = 3,
        NonPositiveDeltaTime = 4,
        NonFiniteDeltaTime = 5,
        InvalidClockDomain = 6,
        InvalidLifecycleTransition = 7,
        MissingContext = 8,
        MissingOperationBinding = 9,
        IllegalPublicTopology = 10,
        InvalidTimelinePosition = 11,
        InvalidFrameLayout = 12,
        InvalidFrameHandle = 13,
        InvalidStateBlock = 14,
        InvalidStateSlot = 15,
        RegistryFrozen = 16,
        RegistryNotFrozen = 17,
        InvalidOperationSection = 18,
        MissingOperationSection = 19,
        InvalidIntentDescriptor = 20,
        MissingIntentReducer = 21,
        InvalidIntentContribution = 22,
        InvalidEventPacket = 23,
        EventDomainMismatch = 24,
        EventTargetMismatch = 25,
        UndeclaredBroadcast = 26,
        DuplicateEventPacket = 27,
        StaleTimelineEpoch = 28,
        EventSequenceConflict = 29,
        MailboxOverflow = 30,
        MailboxUnavailable = 31,
        InvalidRestoreMetadata = 32,
        DerivedDependencyCycle = 33,
        UnknownCodec = 34,
        UnsupportedCodecVersion = 35,
        CommitPreparationFailed = 36,
        CommitCancelled = 37
    }

    public enum CoCoDiagnosticSeverity
    {
        None = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }

    public readonly struct CoCoDiagnostic : IEquatable<CoCoDiagnostic>
    {
        private readonly string _message;

        private CoCoDiagnostic(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            CoCoDiagnosticSeverity severity,
            string message)
        {
            Domain = domain;
            Code = code;
            Severity = severity;
            _message = message ?? string.Empty;
        }

        public static CoCoDiagnostic None => default;

        public CoCoDiagnosticDomain Domain { get; }
        public CoCoDiagnosticCode Code { get; }
        public CoCoDiagnosticSeverity Severity { get; }
        public string Message => _message ?? string.Empty;
        public bool IsNone => Domain == CoCoDiagnosticDomain.None &&
                              Code == CoCoDiagnosticCode.None &&
                              Severity == CoCoDiagnosticSeverity.None;
        public bool IsError => Severity == CoCoDiagnosticSeverity.Error;

        public static CoCoDiagnostic Error(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string message)
        {
            if (domain == CoCoDiagnosticDomain.None ||
                !Enum.IsDefined(typeof(CoCoDiagnosticDomain), domain))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(domain),
                    domain,
                    "An error diagnostic requires a defined, non-None domain.");
            }

            if (code == CoCoDiagnosticCode.None ||
                !Enum.IsDefined(typeof(CoCoDiagnosticCode), code))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(code),
                    code,
                    "An error diagnostic requires a defined, non-None code.");
            }

            return new CoCoDiagnostic(domain, code, CoCoDiagnosticSeverity.Error, message);
        }

        public bool Equals(CoCoDiagnostic other)
        {
            return Domain == other.Domain &&
                   Code == other.Code &&
                   Severity == other.Severity &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is CoCoDiagnostic other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Domain;
                hashCode = (hashCode * 397) ^ (int)Code;
                hashCode = (hashCode * 397) ^ (int)Severity;
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Message);
                return hashCode;
            }
        }

        public static bool operator ==(CoCoDiagnostic left, CoCoDiagnostic right) => left.Equals(right);
        public static bool operator !=(CoCoDiagnostic left, CoCoDiagnostic right) => !left.Equals(right);
    }
}

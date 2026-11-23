using System;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>Classifies which part of an operation a diagnostic concerns.</summary>
    public enum CoCoDiagnosticDomain
    {
        /// <summary>No domain is assigned.</summary>
        None = 0,
        /// <summary>Public contracts and their validation.</summary>
        Contracts = 1,
        /// <summary>Graph topology and relationships between elements.</summary>
        Topology = 2,
        /// <summary>Entity or descriptor identity.</summary>
        Identity = 3,
        /// <summary>Clock and timeline inputs.</summary>
        Time = 4,
        /// <summary>Lifecycle state and transitions.</summary>
        Lifecycle = 5,
        /// <summary>State definitions and runtime state.</summary>
        State = 6,
        /// <summary>Context production, storage, and reads.</summary>
        Context = 7,
        /// <summary>Operation declarations and bindings.</summary>
        Operation = 8,
        /// <summary>Frame data and layout.</summary>
        Frame = 9,
        /// <summary>Registration and lookup.</summary>
        Registry = 10,
        /// <summary>Intent declarations and arbitration.</summary>
        Intent = 11,
        /// <summary>Actor event inbox delivery.</summary>
        Mailbox = 12,
        /// <summary>Restoring recorded state.</summary>
        Restore = 13,
        /// <summary>Encoding and decoding persisted values.</summary>
        Codec = 14,
        /// <summary>Operator binding and execution.</summary>
        Operator = 15,
        /// <summary>Events staged for publication after a commit.</summary>
        EventOutbox = 16,
        /// <summary>Content references, loading, and release.</summary>
        Content = 17,
        /// <summary>Pooled instance ownership and lifecycle.</summary>
        Pooling = 18,
        /// <summary>Map region compilation and transitions.</summary>
        Map = 19
    }

    /// <summary>Identifies a diagnostic condition using stable numeric values.</summary>
    public enum CoCoDiagnosticCode
    {
        /// <summary>No condition is assigned.</summary>
        None = 0,
        /// <summary>An identifier is malformed or absent.</summary>
        InvalidIdentifier = 1,
        /// <summary>An identifier is already in use.</summary>
        DuplicateIdentifier = 2,
        /// <summary>A reference crosses a graph layer boundary.</summary>
        CrossLayerReference = 3,
        /// <summary>Delta time is zero or negative.</summary>
        NonPositiveDeltaTime = 4,
        /// <summary>Delta time is NaN or infinite.</summary>
        NonFiniteDeltaTime = 5,
        /// <summary>The clock domain is invalid for the operation.</summary>
        InvalidClockDomain = 6,
        /// <summary>The requested lifecycle transition is invalid.</summary>
        InvalidLifecycleTransition = 7,
        /// <summary>Required context is unavailable.</summary>
        MissingContext = 8,
        /// <summary>A required operation has no binding.</summary>
        MissingOperationBinding = 9,
        /// <summary>The public topology violates a graph constraint.</summary>
        IllegalPublicTopology = 10,
        /// <summary>The timeline position is invalid.</summary>
        InvalidTimelinePosition = 11,
        /// <summary>The frame layout is invalid.</summary>
        InvalidFrameLayout = 12,
        /// <summary>A frame handle cannot resolve to the expected frame.</summary>
        InvalidFrameHandle = 13,
        /// <summary>A state block is invalid.</summary>
        InvalidStateBlock = 14,
        /// <summary>A state slot cannot be resolved or used.</summary>
        InvalidStateSlot = 15,
        /// <summary>The registry is already frozen.</summary>
        RegistryFrozen = 16,
        /// <summary>The operation requires a frozen registry.</summary>
        RegistryNotFrozen = 17,
        /// <summary>An operation section is invalid.</summary>
        InvalidOperationSection = 18,
        /// <summary>A required operation section is missing.</summary>
        MissingOperationSection = 19,
        /// <summary>An intent descriptor is invalid.</summary>
        InvalidIntentDescriptor = 20,
        /// <summary>A required intent reducer is missing.</summary>
        MissingIntentReducer = 21,
        /// <summary>An intent contribution is invalid.</summary>
        InvalidIntentContribution = 22,
        /// <summary>An event packet is invalid.</summary>
        InvalidEventPacket = 23,
        /// <summary>The event domain does not match the expected domain.</summary>
        EventDomainMismatch = 24,
        /// <summary>The event target does not match the expected target.</summary>
        EventTargetMismatch = 25,
        /// <summary>A broadcast was requested without a broadcast declaration.</summary>
        UndeclaredBroadcast = 26,
        /// <summary>An event packet was already seen.</summary>
        DuplicateEventPacket = 27,
        /// <summary>The event refers to an earlier timeline epoch.</summary>
        StaleTimelineEpoch = 28,
        /// <summary>The event sequence conflicts with an existing sequence.</summary>
        EventSequenceConflict = 29,
        /// <summary>The actor mailbox has reached capacity.</summary>
        MailboxOverflow = 30,
        /// <summary>The actor mailbox cannot accept the event.</summary>
        MailboxUnavailable = 31,
        /// <summary>Restore metadata is invalid.</summary>
        InvalidRestoreMetadata = 32,
        /// <summary>Derived state dependencies contain a cycle.</summary>
        DerivedDependencyCycle = 33,
        /// <summary>The requested codec is not registered.</summary>
        UnknownCodec = 34,
        /// <summary>The codec does not support the stored version.</summary>
        UnsupportedCodecVersion = 35,
        /// <summary>Preparing the commit failed.</summary>
        CommitPreparationFailed = 36,
        /// <summary>The commit was cancelled.</summary>
        CommitCancelled = 37,
        /// <summary>The schema version is unsupported.</summary>
        UnsupportedSchemaVersion = 38,
        /// <summary>A referenced topology element is missing.</summary>
        MissingTopologyElement = 39,
        /// <summary>Parent state relationships form a cycle.</summary>
        ParentStateCycle = 40,
        /// <summary>The initial state is invalid.</summary>
        InvalidInitialState = 41,
        /// <summary>A state cannot be reached in the graph.</summary>
        UnreachableState = 42,
        /// <summary>A required descriptor is missing.</summary>
        MissingDescriptor = 43,
        /// <summary>The descriptor type does not match the expected type.</summary>
        DescriptorTypeMismatch = 44,
        /// <summary>An authoring type has an invalid dependency.</summary>
        InvalidAuthoringDependency = 45,
        /// <summary>Frozen configuration data is invalid.</summary>
        InvalidFrozenConfig = 46,
        /// <summary>Manifest declarations conflict.</summary>
        ManifestConflict = 47,
        /// <summary>A transition window is invalid.</summary>
        InvalidTransitionWindow = 48,
        /// <summary>A transition endpoint must be a leaf.</summary>
        NonLeafTransitionEndpoint = 50,
        /// <summary>Transitions that compete for selection share a priority.</summary>
        DuplicateTransitionPriority = 51,
        /// <summary>An action-progress provider is required.</summary>
        MissingActionProgressProvider = 52,
        /// <summary>Operations overlap along an active state path.</summary>
        ActivePathOperationOverlap = 53,
        /// <summary>An operator descriptor is invalid.</summary>
        InvalidOperatorDescriptor = 54,
        /// <summary>A required operator binding is missing.</summary>
        MissingOperatorBinding = 55,
        /// <summary>Multiple declarations claim the same outcome.</summary>
        OutcomeOwnershipConflict = 56,
        /// <summary>Operator claims conflict.</summary>
        OperatorClaimConflict = 57,
        /// <summary>An operator activation is duplicated.</summary>
        DuplicateOperatorActivation = 58,
        /// <summary>An operator failed to execute.</summary>
        OperatorExecutionFailed = 59,
        /// <summary>The event outbox has reached capacity.</summary>
        EventOutboxOverflow = 60,
        /// <summary>No further event sequence value is available.</summary>
        EventSequenceExhausted = 61,
        /// <summary>Publishing an event failed.</summary>
        EventPublishFailed = 62,
        /// <summary>The world requires correction before continuing.</summary>
        WorldCorrectionRequired = 63,
        /// <summary>A context producer is invalid.</summary>
        InvalidContextProducer = 64,
        /// <summary>Capturing context failed.</summary>
        ContextCaptureFailed = 65,
        /// <summary>Restoring the graph failed validation.</summary>
        InvalidGraphRestore = 66,
        /// <summary>An operator claim could not be restored.</summary>
        InvalidClaimRestore = 67,
        /// <summary>An actor binding is invalid.</summary>
        InvalidActorBinding = 68,
        /// <summary>A content identifier is invalid.</summary>
        InvalidContentId = 69,
        /// <summary>A content reference is invalid.</summary>
        InvalidContentReference = 70,
        /// <summary>No content backend is available for the reference.</summary>
        MissingContentBackend = 71,
        /// <summary>The loaded content type differs from the requested type.</summary>
        ContentTypeMismatch = 72,
        /// <summary>Loading the content failed.</summary>
        ContentLoadFailed = 73,
        /// <summary>The content request was cancelled.</summary>
        ContentRequestCancelled = 74,
        /// <summary>The content scope has been disposed.</summary>
        ContentScopeDisposed = 75,
        /// <summary>Releasing the content failed.</summary>
        ContentReleaseFailed = 76,
        /// <summary>The content runtime has been disposed.</summary>
        ContentRuntimeDisposed = 77,
        /// <summary>Content references conflict for the same identity.</summary>
        ContentReferenceConflict = 78,
        /// <summary>The content operation must run on the main thread.</summary>
        ContentMainThreadRequired = 79,
        /// <summary>Content backend registrations conflict.</summary>
        ContentBackendConflict = 80,
        /// <summary>A pool identifier is invalid.</summary>
        InvalidPoolId = 81,
        /// <summary>A pool profile is invalid.</summary>
        InvalidPoolProfile = 82,
        /// <summary>Pool profile registrations conflict.</summary>
        PoolProfileConflict = 83,
        /// <summary>The pool runtime has been disposed.</summary>
        PoolRuntimeDisposed = 84,
        /// <summary>The pool scope is closing.</summary>
        PoolScopeClosing = 85,
        /// <summary>The pool is not ready for the operation.</summary>
        PoolNotReady = 86,
        /// <summary>A pool operation is already in progress.</summary>
        PoolOperationInProgress = 87,
        /// <summary>The pool operation was cancelled.</summary>
        PoolOperationCancelled = 88,
        /// <summary>Creating the pooled instance failed.</summary>
        PoolInstanceCreateFailed = 89,
        /// <summary>The pooled handle is invalid.</summary>
        InvalidPooledHandle = 90,
        /// <summary>The pooled handle has already been returned.</summary>
        PooledHandleAlreadyReturned = 91,
        /// <summary>The pooled handle refers to an earlier lease.</summary>
        StalePooledHandle = 92,
        /// <summary>The handle belongs to a different pool owner.</summary>
        PooledHandleOwnerMismatch = 93,
        /// <summary>The requested pool transition is invalid.</summary>
        InvalidPoolTransition = 94,
        /// <summary>Activating a pooled instance failed.</summary>
        PoolActivationFailed = 95,
        /// <summary>Resetting a pooled instance failed.</summary>
        PoolResetFailed = 96,
        /// <summary>The instance underlying the pool handle was destroyed.</summary>
        PooledInstanceDestroyed = 97,
        /// <summary>The pool operation must run on the main thread.</summary>
        PoolMainThreadRequired = 98,
        /// <summary>A callback tried to reenter a pool operation.</summary>
        PoolCallbackReentry = 99,
        /// <summary>A pooled handle was left outstanding.</summary>
        PoolHandleLeak = 100,
        /// <summary>The pool was forcibly shut down.</summary>
        PoolForcedShutdown = 101,
        /// <summary>Temporal pool bindings conflict.</summary>
        PoolTemporalConflict = 102,
        /// <summary>The temporal entity is unavailable for the pool operation.</summary>
        PoolTemporalEntityUnavailable = 103,
        /// <summary>Projecting temporal state into the pool failed.</summary>
        PoolTemporalProjectionFailed = 104,
        /// <summary>Cleaning up the temporal pool binding failed.</summary>
        PoolTemporalCleanupFailed = 105,
        /// <summary>A region identifier is invalid.</summary>
        InvalidRegionIdentifier = 106,
        /// <summary>A region capability declaration is invalid.</summary>
        InvalidRegionCapability = 107,
        /// <summary>A region capability is unsupported.</summary>
        UnsupportedRegionCapability = 108,
        /// <summary>A region coverage declaration is invalid.</summary>
        InvalidRegionCoverage = 109,
        /// <summary>A region profile is invalid.</summary>
        InvalidRegionProfile = 110,
        /// <summary>Compiling the region failed.</summary>
        RegionCompilationFailed = 111,
        /// <summary>Region catalog registrations conflict.</summary>
        RegionCatalogConflict = 112,
        /// <summary>The region runtime has been disposed.</summary>
        RegionRuntimeDisposed = 113,
        /// <summary>The region operation must run on the main thread.</summary>
        RegionMainThreadRequired = 114,
        /// <summary>Region demands conflict.</summary>
        RegionDemandConflict = 115,
        /// <summary>A newer region demand superseded this one.</summary>
        RegionDemandSuperseded = 116,
        /// <summary>The region transition failed.</summary>
        RegionTransitionFailed = 117,
        /// <summary>Committing a region transition faulted.</summary>
        RegionCommitFaulted = 118,
        /// <summary>Region cleanup could not proceed.</summary>
        RegionCleanupBlocked = 119,
        /// <summary>An optional region capability is degraded.</summary>
        RegionOptionalDegraded = 120,
        /// <summary>A scene violates the region contract.</summary>
        RegionSceneContractViolation = 121,
        /// <summary>Temporal region bindings conflict.</summary>
        RegionTemporalConflict = 122,
        /// <summary>Projecting temporal state into the region failed.</summary>
        RegionTemporalProjectionFailed = 123,
        /// <summary>Cleaning up temporal region state failed.</summary>
        RegionTemporalCleanupFailed = 124
    }

    /// <summary>Indicates how a diagnostic should be interpreted.</summary>
    public enum CoCoDiagnosticSeverity
    {
        /// <summary>No diagnostic severity is assigned.</summary>
        None = 0,
        /// <summary>Informational detail about an operation.</summary>
        Information = 1,
        /// <summary>A condition worth reporting as a warning.</summary>
        Warning = 2,
        /// <summary>A condition reported as an error.</summary>
        Error = 3
    }

    /// <summary>Describes an operation result without emitting a log.</summary>
    public readonly struct CoCoDiagnostic : IEquatable<CoCoDiagnostic>
    {
        #region Public API

        /// <summary>The default result, with no domain, code, severity, or message.</summary>
        public static CoCoDiagnostic None => default;

        /// <summary>The operation area that produced the diagnostic.</summary>
        public CoCoDiagnosticDomain Domain { get; }
        /// <summary>The stable identifier for the reported condition.</summary>
        public CoCoDiagnosticCode Code { get; }
        /// <summary>The level assigned by the diagnostic factory.</summary>
        public CoCoDiagnosticSeverity Severity { get; }
        /// <summary>The human-readable detail, or an empty string when absent.</summary>
        public string Message => _message ?? string.Empty;
        /// <summary>Whether domain, code, and severity are all None.</summary>
        public bool IsNone => Domain == CoCoDiagnosticDomain.None &&
                              Code == CoCoDiagnosticCode.None &&
                              Severity == CoCoDiagnosticSeverity.None;
        /// <summary>Whether the severity is Error.</summary>
        public bool IsError => Severity == CoCoDiagnosticSeverity.Error;
        /// <summary>Whether the severity is Warning.</summary>
        public bool IsWarning => Severity == CoCoDiagnosticSeverity.Warning;

        /// <summary>Creates an error diagnostic.</summary>
        /// <param name="domain">A defined domain other than None.</param>
        /// <param name="code">A defined code other than None.</param>
        /// <param name="message">The human-readable detail. Null is stored as empty text.</param>
        /// <exception cref="ArgumentOutOfRangeException">The domain or code is None or undefined.</exception>
        public static CoCoDiagnostic Error(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string message)
        {
            Validate(domain, code, "An error diagnostic");
            return new CoCoDiagnostic(domain, code, CoCoDiagnosticSeverity.Error, message);
        }

        /// <summary>Creates a warning diagnostic.</summary>
        /// <param name="domain">A defined domain other than None.</param>
        /// <param name="code">A defined code other than None.</param>
        /// <param name="message">The human-readable detail. Null is stored as empty text.</param>
        /// <exception cref="ArgumentOutOfRangeException">The domain or code is None or undefined.</exception>
        public static CoCoDiagnostic Warning(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string message)
        {
            Validate(domain, code, "A warning diagnostic");
            return new CoCoDiagnostic(domain, code, CoCoDiagnosticSeverity.Warning, message);
        }

        /// <summary>Creates an informational diagnostic.</summary>
        /// <param name="domain">A defined domain other than None.</param>
        /// <param name="code">A defined code other than None.</param>
        /// <param name="message">The human-readable detail. Null is stored as empty text.</param>
        /// <exception cref="ArgumentOutOfRangeException">The domain or code is None or undefined.</exception>
        public static CoCoDiagnostic Info(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string message)
        {
            Validate(domain, code, "An information diagnostic");
            return new CoCoDiagnostic(domain, code, CoCoDiagnosticSeverity.Information, message);
        }

        /// <summary>Compares domain, code, severity, and ordinal message text.</summary>
        public bool Equals(CoCoDiagnostic other)
        {
            return Domain == other.Domain &&
                   Code == other.Code &&
                   Severity == other.Severity &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        /// <summary>Compares another diagnostic by value.</summary>
        public override bool Equals(object obj) => obj is CoCoDiagnostic other && Equals(other);

        /// <summary>Returns a hash consistent with diagnostic value equality.</summary>
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

        /// <summary>Tests whether the diagnostics have equal values.</summary>
        public static bool operator ==(CoCoDiagnostic left, CoCoDiagnostic right) => left.Equals(right);
        /// <summary>Tests whether the diagnostic values differ.</summary>
        public static bool operator !=(CoCoDiagnostic left, CoCoDiagnostic right) => !left.Equals(right);

        #endregion

        #region Internal Logic

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

        private static void Validate(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string diagnosticKind)
        {
            if (domain == CoCoDiagnosticDomain.None ||
                !Enum.IsDefined(typeof(CoCoDiagnosticDomain), domain))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(domain),
                    domain,
                    diagnosticKind + " requires a defined, non-None domain.");
            }

            if (code == CoCoDiagnosticCode.None ||
                !Enum.IsDefined(typeof(CoCoDiagnosticCode), code))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(code),
                    code,
                    diagnosticKind + " requires a defined, non-None code.");
            }
        }

        #endregion
    }
}

using System;

// 这个文件定义 CoCoFlow 各模块共用的诊断结果，包括领域、代码、严重级别和消息。
// StateGraph、Host、Content、Pooling 和 Map 用它返回操作结果，Editor 读取这些结果用于展示。
// 构造诊断只生成结果值。调用方需要记录日志时，由调用方决定何时输出。
namespace CoCoFlow.Runtime.Core
{
    /// <summary>诊断所属的领域。</summary>
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
        Codec = 14,
        Operator = 15,
        EventOutbox = 16,
        Content = 17,
        Pooling = 18,
        Map = 19
    }

    /// <summary>标识诊断原因，枚举数值保持稳定。</summary>
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
        CommitCancelled = 37,
        UnsupportedSchemaVersion = 38,
        MissingTopologyElement = 39,
        ParentStateCycle = 40,
        InvalidInitialState = 41,
        UnreachableState = 42,
        MissingDescriptor = 43,
        DescriptorTypeMismatch = 44,
        InvalidAuthoringDependency = 45,
        InvalidFrozenConfig = 46,
        ManifestConflict = 47,
        InvalidTransitionWindow = 48,
        NonLeafTransitionEndpoint = 50,
        DuplicateTransitionPriority = 51,
        MissingActionProgressProvider = 52,
        ActivePathOperationOverlap = 53,
        InvalidOperatorDescriptor = 54,
        MissingOperatorBinding = 55,
        OutcomeOwnershipConflict = 56,
        OperatorClaimConflict = 57,
        DuplicateOperatorActivation = 58,
        OperatorExecutionFailed = 59,
        EventOutboxOverflow = 60,
        EventSequenceExhausted = 61,
        EventPublishFailed = 62,
        WorldCorrectionRequired = 63,
        InvalidContextProducer = 64,
        ContextCaptureFailed = 65,
        InvalidGraphRestore = 66,
        InvalidClaimRestore = 67,
        InvalidActorBinding = 68,
        InvalidContentId = 69,
        InvalidContentReference = 70,
        MissingContentBackend = 71,
        ContentTypeMismatch = 72,
        ContentLoadFailed = 73,
        ContentRequestCancelled = 74,
        ContentScopeDisposed = 75,
        ContentReleaseFailed = 76,
        ContentRuntimeDisposed = 77,
        ContentReferenceConflict = 78,
        ContentMainThreadRequired = 79,
        ContentBackendConflict = 80,
        InvalidPoolId = 81,
        InvalidPoolProfile = 82,
        PoolProfileConflict = 83,
        PoolRuntimeDisposed = 84,
        PoolScopeClosing = 85,
        PoolNotReady = 86,
        PoolOperationInProgress = 87,
        PoolOperationCancelled = 88,
        PoolInstanceCreateFailed = 89,
        InvalidPooledHandle = 90,
        PooledHandleAlreadyReturned = 91,
        StalePooledHandle = 92,
        PooledHandleOwnerMismatch = 93,
        InvalidPoolTransition = 94,
        PoolActivationFailed = 95,
        PoolResetFailed = 96,
        PooledInstanceDestroyed = 97,
        PoolMainThreadRequired = 98,
        PoolCallbackReentry = 99,
        PoolHandleLeak = 100,
        PoolForcedShutdown = 101,
        PoolTemporalConflict = 102,
        PoolTemporalEntityUnavailable = 103,
        PoolTemporalProjectionFailed = 104,
        PoolTemporalCleanupFailed = 105,
        InvalidRegionIdentifier = 106,
        InvalidRegionCapability = 107,
        UnsupportedRegionCapability = 108,
        InvalidRegionCoverage = 109,
        InvalidRegionProfile = 110,
        RegionCompilationFailed = 111,
        RegionCatalogConflict = 112,
        RegionRuntimeDisposed = 113,
        RegionMainThreadRequired = 114,
        RegionDemandConflict = 115,
        RegionDemandSuperseded = 116,
        RegionTransitionFailed = 117,
        RegionCommitFaulted = 118,
        RegionCleanupBlocked = 119,
        RegionOptionalDegraded = 120,
        RegionSceneContractViolation = 121,
        RegionTemporalConflict = 122,
        RegionTemporalProjectionFailed = 123,
        RegionTemporalCleanupFailed = 124
    }

    /// <summary>诊断的严重级别。</summary>
    public enum CoCoDiagnosticSeverity
    {
        None = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>记录操作的诊断结果，构造时不会写日志。</summary>
    public readonly struct CoCoDiagnostic : IEquatable<CoCoDiagnostic>
    {
        #region Public API

        public static CoCoDiagnostic None => default;

        public CoCoDiagnosticDomain Domain { get; }
        public CoCoDiagnosticCode Code { get; }
        public CoCoDiagnosticSeverity Severity { get; }
        public string Message => _message ?? string.Empty;
        public bool IsNone => Domain == CoCoDiagnosticDomain.None &&
                              Code == CoCoDiagnosticCode.None &&
                              Severity == CoCoDiagnosticSeverity.None;
        public bool IsError => Severity == CoCoDiagnosticSeverity.Error;
        public bool IsWarning => Severity == CoCoDiagnosticSeverity.Warning;

        /// <summary>创建错误诊断。</summary>
        /// <param name="domain">已定义且非 None 的领域。</param>
        /// <param name="code">已定义且非 None 的诊断代码。</param>
        /// <param name="message">诊断消息。传入 null 时使用空字符串。</param>
        /// <exception cref="ArgumentOutOfRangeException">领域或代码为 None，或者超出已定义的枚举值。</exception>
        public static CoCoDiagnostic Error(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string message)
        {
            Validate(domain, code, "An error diagnostic");
            return new CoCoDiagnostic(domain, code, CoCoDiagnosticSeverity.Error, message);
        }

        /// <summary>创建警告诊断。</summary>
        /// <param name="domain">已定义且非 None 的领域。</param>
        /// <param name="code">已定义且非 None 的诊断代码。</param>
        /// <param name="message">诊断消息。传入 null 时使用空字符串。</param>
        /// <exception cref="ArgumentOutOfRangeException">领域或代码为 None，或者超出已定义的枚举值。</exception>
        public static CoCoDiagnostic Warning(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string message)
        {
            Validate(domain, code, "A warning diagnostic");
            return new CoCoDiagnostic(domain, code, CoCoDiagnosticSeverity.Warning, message);
        }

        /// <summary>创建信息诊断。</summary>
        /// <param name="domain">已定义且非 None 的领域。</param>
        /// <param name="code">已定义且非 None 的诊断代码。</param>
        /// <param name="message">诊断消息。传入 null 时使用空字符串。</param>
        /// <exception cref="ArgumentOutOfRangeException">领域或代码为 None，或者超出已定义的枚举值。</exception>
        public static CoCoDiagnostic Info(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string message)
        {
            Validate(domain, code, "An information diagnostic");
            return new CoCoDiagnostic(domain, code, CoCoDiagnosticSeverity.Information, message);
        }

        /// <summary>比较诊断的领域、代码、级别和消息。消息按序号比较。</summary>
        public bool Equals(CoCoDiagnostic other)
        {
            return Domain == other.Domain &&
                   Code == other.Code &&
                   Severity == other.Severity &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        /// <summary>比较诊断的领域、代码、级别和消息。消息按序号比较。</summary>
        public override bool Equals(object obj) => obj is CoCoDiagnostic other && Equals(other);

        /// <summary>返回与值相等规则一致的哈希值。</summary>
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

        /// <summary>判断两个诊断的值是否相等。</summary>
        public static bool operator ==(CoCoDiagnostic left, CoCoDiagnostic right) => left.Equals(right);
        /// <summary>判断两个诊断的值是否不同。</summary>
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

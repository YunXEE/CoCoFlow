using System;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("CoCoFlow/Animation/Anim Auto Operator")]
    [CoCoOperatorRegistration(typeof(AnimSectionRegistrar))]
    public sealed class AnimAutoOperator :
        MonoBehaviour,
        ICoCoOperator,
        ICoCoContextRestoreBinding,
        ICoCoTemporalDecoratorBinding,
        ICoCoEventToIntentAdapter<AnimFeedbackEvent, AnimFeedbackIntent>,
        IAnimEventReceiver,
        IAnimEventStateResolver
    {
        [SerializeField] private Animator animator;
        [SerializeField] private CoCoStateGraphHost stateGraphHost;
        [SerializeField] private AnimParameterBinding[] parameterBindings =
            Array.Empty<AnimParameterBinding>();
        [SerializeField] private AnimTriggerBinding[] triggerBindings =
            Array.Empty<AnimTriggerBinding>();
        [SerializeField] private MonoBehaviour downstreamRestoreBinding;

        private readonly AnimFeedbackBuffer _feedback = new AnimFeedbackBuffer();
        private readonly AnimFeedbackEventToIntentAdapter _feedbackAdapter =
            new AnimFeedbackEventToIntentAdapter();
        private AnimParameterTarget[] _parameterTargets = Array.Empty<AnimParameterTarget>();
        private AnimTriggerTarget[] _triggerTargets = Array.Empty<AnimTriggerTarget>();
        private CoCoDiagnostic _lastDiagnostic;
        private bool _isInitialized;

        public CoCoOperatorDescriptor Descriptor => AnimOperatorContracts.AutoDescriptor;

        MonoBehaviour ICoCoTemporalDecoratorBinding.DownstreamRestoreBinding =>
            downstreamRestoreBinding;
        public Animator Animator => animator;
        public CoCoStateGraphHost StateGraphHost => stateGraphHost;
        public CoCoDiagnostic LastDiagnostic => _lastDiagnostic;

        /// <summary>
        /// Direct Animator callbacks are staged in this Operator and enter StateFlow
        /// on the Tick after the next successful Operator commit.
        /// </summary>
        public bool UsesStagedSmbFeedback => true;

        private void Reset()
        {
            animator = GetComponent<Animator>();
            stateGraphHost = GetComponentInParent<CoCoStateGraphHost>();
        }

        private void Awake()
        {
            TryRebuildBindings(out _lastDiagnostic);
        }

        public bool TryRebuildBindings(out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            // Rebuild is the explicit recovery boundary for a fail-closed
            // feedback overflow. Never carry a staged batch across bindings.
            _feedback.Clear();
            animator ??= GetComponent<Animator>();
            stateGraphHost ??= GetComponentInParent<CoCoStateGraphHost>();
            if (animator == null ||
                stateGraphHost == null ||
                !CoCoStateGraphHostBoundary.Contains(stateGraphHost, this) ||
                !AnimBindingRuntime.TryBuildParameters(
                    animator,
                    parameterBindings,
                    out _parameterTargets,
                    out diagnostic) ||
                !AnimBindingRuntime.TryBuildTriggers(
                    animator,
                    triggerBindings,
                    out _triggerTargets,
                    out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = AnimOperatorContracts.Error(
                        "AnimAutoOperator requires one same-boundary StateGraph Host, " +
                        "one Animator, and no AnimOperator on the same Animator.");
                }

                _isInitialized = false;
                _lastDiagnostic = diagnostic;
                return false;
            }

            _isInitialized = true;
            diagnostic = CoCoDiagnostic.None;
            _lastDiagnostic = diagnostic;
            return true;
        }

        public bool TryExecute(
            in CoCoOperatorExecutionContext context,
            out CoCoOperatorOutcome outcome)
        {
            _feedback.PrepareForExecution(context, stateGraphHost);
            if (_feedback.Overflowed)
            {
                return Reject(
                    "AnimAutoOperator SMB feedback overflowed its fixed reliable batch of 16 records. " +
                    "The entire batch is rejected; stop, rebuild bindings, then start the Host to recover.",
                    out outcome);
            }

            if (!context.IsValid ||
                !_isInitialized ||
                animator == null ||
                stateGraphHost == null ||
                !CoCoStateGraphHostBoundary.Contains(stateGraphHost, this) ||
                !context.TryGet(
                    AnimOperatorContracts.ParameterRequirement,
                    out CoCoOperationSectionEntry<IAnimParameterOperationSection> parameters) ||
                !context.TryGet(
                    AnimOperatorContracts.TriggerRequirement,
                    out CoCoOperationSectionEntry<IAnimTriggerOperationSection> triggers) ||
                !AnimBindingRuntime.ValidateParameters(
                    parameters.View,
                    _parameterTargets) ||
                (triggers.Header.Enabled &&
                 !AnimBindingRuntime.ValidateTriggers(
                     triggers.View,
                     _triggerTargets)))
            {
                return Reject(
                    "AnimAutoOperator input or frozen Animator binding validation failed.",
                    out outcome);
            }

            bool wroteAnimator = ApplyParameters(parameters.View);
            if (triggers.Header.Enabled)
            {
                wroteAnimator |= ApplyTriggers(triggers.View);
            }

            bool wroteFeedback = _feedback.Count > 0;
            if (wroteFeedback && !_feedback.TryWrite(context, stateGraphHost))
            {
                return Reject(
                    "AnimAutoOperator could not commit staged SMB feedback.",
                    out outcome);
            }

            // Engine-fact segment (the mirror of LocomotionOperator's
            // Sample write): the engine is the authority on animation
            // state, the Operator records what actually happened. The
            // Animator advances on Unity's own update phase, so the value
            // read here is the engine's latest verdict — one tick of
            // latency by design, never a prediction.
            AnimSnapshotState snapshot =
                AnimSnapshot.Sample(animator, parameterBindings);
            if (!context.TryWriteOutcome(
                    AnimContractIds.SnapshotSlotId,
                    snapshot))
            {
                return Reject(
                    "AnimAutoOperator snapshot slot write was rejected.",
                    out outcome);
            }

            // The snapshot write above is an outcome write — a tick that
            // reached this point always owns at least that write.
            outcome = CoCoOperatorOutcome.Success;
            _lastDiagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>
        /// Restore projection (the mirror of LocomotionStateMath.
        /// ProjectToWorld): the Animator adopts the ledger once. Applies
        /// for every restore kind — preview, confirm, cancel and
        /// correction all read the same slot and write the same world.
        /// </summary>
        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            if (animator == null || !context.IsValid)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.MissingDescriptor,
                    "AnimAutoOperator restore projection requires one live Animator and one valid restore context.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!context.Reader.TryRead(
                    AnimContractIds.SnapshotSlotId,
                    out AnimSnapshotState snapshot))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.MissingDescriptor,
                    "AnimAutoOperator restore projection found no committed Animator snapshot slot.");
                _lastDiagnostic = diagnostic;
                return false;
            }

            if (!AnimSnapshot.TryProject(
                    animator,
                    parameterBindings,
                    snapshot,
                    out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Restore,
                        CoCoDiagnosticCode.WorldCorrectionRequired,
                        "AnimAutoOperator restore projection failed: the snapshot does not match the current Animator layout.");
                }

                _lastDiagnostic = diagnostic;
                return false;
            }

            _lastDiagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryProject(
            in CoCoEventPacket<AnimFeedbackEvent> packet,
            out AnimFeedbackIntent intent)
        {
            return _feedbackAdapter.TryProject(packet, out intent);
        }

        bool IAnimEventReceiver.TryReceiveSmbSignal(in AnimSmbSignal signal)
        {
            if (!TryCreateSmbFeedback(signal, out AnimFeedbackRecord record) ||
                !AnimFeedbackSourceStamp.TryCaptureCommitted(
                    stateGraphHost,
                    out AnimFeedbackSourceStamp source) ||
                !_feedback.TryAppend(record, source))
            {
                _lastDiagnostic = AnimOperatorContracts.Error(
                    _feedback.Overflowed
                        ? "AnimAutoOperator SMB feedback overflowed its fixed reliable batch of 16 records. " +
                          "The entire batch is rejected; stop, rebuild bindings, then start the Host to recover."
                        : "AnimAutoOperator SMB feedback buffer rejected an unattributable or invalid signal.");
                return false;
            }

            return true;
        }

        bool IAnimEventStateResolver.TryCaptureSmbState(
            int layerIndex,
            out AnimSmbStateSnapshot snapshot)
        {
            if (animator == null ||
                layerIndex < 0 ||
                layerIndex >= animator.layerCount)
            {
                snapshot = default;
                return false;
            }

            bool inTransition = animator.IsInTransition(layerIndex);
            snapshot = new AnimSmbStateSnapshot(
                animator.GetCurrentAnimatorStateInfo(layerIndex),
                inTransition,
                inTransition
                    ? animator.GetNextAnimatorStateInfo(layerIndex)
                    : default);
            return snapshot.IsValid;
        }

        private bool ApplyParameters(IAnimParameterOperationSection section)
        {
            bool wrote = false;
            for (int lane = 0; lane < AnimContractLimits.ParameterLaneCount; lane++)
            {
                AnimParameterCommand command = AnimOperationLaneReader.Read(section, lane);
                if (command.Kind == AnimParameterValueKind.None)
                {
                    continue;
                }

                AnimBindingRuntime.TryFind(
                    _parameterTargets,
                    command.BindingId,
                    out AnimParameterTarget target);
                switch (command.Kind)
                {
                    case AnimParameterValueKind.Float:
                        animator.SetFloat(target.ParameterHash, command.FloatValue);
                        break;
                    case AnimParameterValueKind.Integer:
                        animator.SetInteger(target.ParameterHash, command.IntegerValue);
                        break;
                    case AnimParameterValueKind.Boolean:
                        animator.SetBool(target.ParameterHash, command.BooleanValue);
                        break;
                }

                wrote = true;
            }

            return wrote;
        }

        private bool ApplyTriggers(IAnimTriggerOperationSection section)
        {
            bool wrote = false;
            for (int lane = 0; lane < AnimContractLimits.TriggerLaneCount; lane++)
            {
                AnimTriggerCommand command = AnimOperationLaneReader.Read(section, lane);
                if (command.Kind == AnimTriggerCommandKind.None)
                {
                    continue;
                }

                AnimBindingRuntime.TryFind(
                    _triggerTargets,
                    command.BindingId,
                    out AnimTriggerTarget target);
                if (command.Kind == AnimTriggerCommandKind.Set)
                {
                    animator.SetTrigger(target.ParameterHash);
                }
                else
                {
                    animator.ResetTrigger(target.ParameterHash);
                }

                wrote = true;
            }

            return wrote;
        }

        private bool Reject(string message, out CoCoOperatorOutcome outcome)
        {
            _lastDiagnostic = AnimOperatorContracts.Error(message);
            outcome = CoCoOperatorOutcome.Rejected(_lastDiagnostic);
            return true;
        }

        internal static bool TryCreateSmbFeedback(
            in AnimSmbSignal signal,
            out AnimFeedbackRecord record)
        {
            AnimFeedbackKind kind;
            switch (signal.Kind)
            {
                case AnimSmbSignalKind.StateEnter:
                    kind = AnimFeedbackKind.StateEnter;
                    break;
                case AnimSmbSignalKind.Marker:
                    kind = AnimFeedbackKind.StateMarker;
                    break;
                case AnimSmbSignalKind.StateExit:
                    kind = AnimFeedbackKind.StateExit;
                    break;
                default:
                    record = default;
                    return false;
            }

            return AnimFeedbackRecord.TryCreateState(
                kind,
                signal.BindingId,
                signal.StateHash,
                signal.LayerIndex,
                signal.LoopCount,
                signal.NormalizedTime,
                out record);
        }
    }

    /// <summary>
    /// Engine-side snapshot math: sample what the Animator actually
    /// decided (engine fact), and project a snapshot back onto the
    /// Animator (restore projection, the exception moment #2). Layout
    /// mismatches fail loudly — never a silent partial restore.
    /// </summary>
    public static class AnimSnapshot
    {
        public static AnimSnapshotState Sample(
            Animator animator,
            AnimParameterBinding[] bindings)
        {
            var snapshot = new AnimSnapshotState();
            int layerCount = animator != null
                ? Mathf.Min(animator.layerCount, AnimSnapshotState.MaxLayers)
                : 0;
            snapshot.LayerCount = (byte)layerCount;
            for (int index = 0; index < layerCount; index++)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(index);
                snapshot.SetLayer(
                    index,
                    info.shortNameHash,
                    info.normalizedTime,
                    animator.GetLayerWeight(index));
            }

            int laneCount = bindings != null
                ? Mathf.Min(bindings.Length, AnimSnapshotState.MaxParameterLanes)
                : 0;
            snapshot.LaneCount = (byte)laneCount;
            for (int index = 0; index < laneCount; index++)
            {
                // BUG-032: typed read per binding kind — Integer rides the
                // lane as raw 32-bit bits (never a numeric cast), Boolean
                // as 0f/1f, Float keeps GetFloat.
                snapshot.SetLane(index, SampleLane(animator, bindings[index]));
            }

            return snapshot;
        }

        private static float SampleLane(
            Animator animator,
            AnimParameterBinding binding)
        {
            switch (binding.ParameterKind)
            {
                case AnimParameterValueKind.Float:
                    return animator.GetFloat(binding.ParameterName);
                case AnimParameterValueKind.Integer:
                    return BitConverter.Int32BitsToSingle(
                        animator.GetInteger(binding.ParameterName));
                case AnimParameterValueKind.Boolean:
                    return animator.GetBool(binding.ParameterName) ? 1f : 0f;
                default:
                    throw new ArgumentException(
                        "AnimSnapshot.Sample received binding '" +
                        binding.ParameterName + "' with invalid ParameterKind " +
                        (int)binding.ParameterKind + ".");
            }
        }

        public static bool TryProject(
            Animator animator,
            AnimParameterBinding[] bindings,
            in AnimSnapshotState snapshot,
            out CoCoDiagnostic diagnostic)
        {
            int layerCount = snapshot.LayerCount;
            if (layerCount < 0 ||
                layerCount > AnimSnapshotState.MaxLayers ||
                animator == null ||
                layerCount > animator.layerCount)
            {
                diagnostic = LayoutMismatch(
                    "The saved Animator layer layout does not match the current Animator.");
                return false;
            }

            int laneCount = snapshot.LaneCount;
            if (laneCount < 0 ||
                laneCount > AnimSnapshotState.MaxParameterLanes ||
                laneCount > (bindings?.Length ?? 0))
            {
                diagnostic = LayoutMismatch(
                    "The saved Animator parameter layout does not match the current bindings.");
                return false;
            }

            for (int index = 0; index < layerCount; index++)
            {
                if (!animator.HasState(index, snapshot.LayerStateHash(index)))
                {
                    diagnostic = LayoutMismatch(
                        "The saved Animator state hash is unknown to the current controller on layer " +
                        index + ".");
                    return false;
                }
            }

            // BUG-032: validate every projected lane's kind before any
            // Animator write (parameters, Play, weights) — an invalid
            // kind is a layout mismatch and must never partially restore.
            for (int index = 0; index < laneCount; index++)
            {
                AnimParameterValueKind kind = bindings[index].ParameterKind;
                if (kind < AnimParameterValueKind.Float ||
                    kind > AnimParameterValueKind.Boolean)
                {
                    diagnostic = LayoutMismatch(
                        "The saved Animator parameter layout does not match the current bindings: lane " +
                        index + " ('" + bindings[index].ParameterName +
                        "') declares invalid parameter kind " + (int)kind + ".");
                    return false;
                }
            }

            for (int index = 0; index < laneCount; index++)
            {
                ApplyLane(animator, bindings[index], snapshot.Lane(index));
            }

            for (int index = 0; index < layerCount; index++)
            {
                animator.Play(
                    snapshot.LayerStateHash(index),
                    index,
                    snapshot.LayerTime(index));
                animator.SetLayerWeight(index, snapshot.LayerWeight(index));
            }

            // Zero-time evaluate so the restored pose is applied on this
            // frame (no time passes — this is not a manual advance).
            animator.Update(0f);

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        // BUG-032: symmetric typed projection — Integer decodes the raw
        // 32-bit payload with SingleToInt32Bits; Boolean restores from
        // the stored 0f/1f encoding; Float keeps SetFloat.
        private static void ApplyLane(
            Animator animator,
            AnimParameterBinding binding,
            float lane)
        {
            switch (binding.ParameterKind)
            {
                case AnimParameterValueKind.Float:
                    animator.SetFloat(binding.ParameterName, lane);
                    break;
                case AnimParameterValueKind.Integer:
                    animator.SetInteger(
                        binding.ParameterName,
                        BitConverter.SingleToInt32Bits(lane));
                    break;
                case AnimParameterValueKind.Boolean:
                    animator.SetBool(binding.ParameterName, lane != 0f);
                    break;
            }
        }

        private static CoCoDiagnostic LayoutMismatch(string message)
        {
            return CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.WorldCorrectionRequired,
                message);
        }
    }
}

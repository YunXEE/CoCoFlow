using System;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("CoCoFlow/Animation/Anim Auto Operator")]
    public sealed class AnimAutoOperator :
        MonoBehaviour,
        ICoCoOperator,
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

        private readonly AnimFeedbackBuffer _feedback = new AnimFeedbackBuffer();
        private readonly AnimFeedbackEventToIntentAdapter _feedbackAdapter =
            new AnimFeedbackEventToIntentAdapter();
        private AnimParameterTarget[] _parameterTargets = Array.Empty<AnimParameterTarget>();
        private AnimTriggerTarget[] _triggerTargets = Array.Empty<AnimTriggerTarget>();
        private CoCoDiagnostic _lastDiagnostic;
        private bool _isInitialized;

        public CoCoOperatorDescriptor Descriptor => AnimOperatorContracts.AutoDescriptor;
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
                animator.GetComponent<AnimOperator>() != null ||
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

            outcome = wroteAnimator || wroteFeedback
                ? CoCoOperatorOutcome.Success
                : CoCoOperatorOutcome.NoOp;
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
}

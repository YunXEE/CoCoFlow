using System;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CoCoFlow.Runtime.Modules.Animation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("CoCoFlow/Animation/Anim Operator")]
    public sealed class AnimOperator :
        MonoBehaviour,
        ICoCoOperator,
        ICoCoEventToIntentAdapter<AnimFeedbackEvent, AnimFeedbackIntent>,
        ICoCoContextRestoreBinding,
        ICoCoTemporalDecoratorBinding,
        ICoCoStateGraphTemporalParticipant,
        IAnimEventReceiver,
        IAnimEventStateResolver,
        IAnimModulationHost
    {
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController controller;
        [SerializeField] private CoCoStateGraphHost stateGraphHost;
        [SerializeField] private AnimEvaluationMode evaluationMode = AnimEvaluationMode.Tick;
        [SerializeField] private AnimParameterBinding[] parameterBindings =
            Array.Empty<AnimParameterBinding>();
        [SerializeField] private AnimTriggerBinding[] triggerBindings =
            Array.Empty<AnimTriggerBinding>();
        [SerializeField] private AnimPlaybackLayerBinding[] playbackLayers =
            Array.Empty<AnimPlaybackLayerBinding>();
        [SerializeField] private AnimStateBinding[] stateBindings =
            Array.Empty<AnimStateBinding>();
        [SerializeField] private AnimModulationBinding[] modulationBindings =
            Array.Empty<AnimModulationBinding>();
        [SerializeField] private bool enableRootMotionRelay;
        [SerializeField] private bool relayPosition = true;
        [SerializeField] private bool relayRotation = true;
        [SerializeField] private MonoBehaviour downstreamRestoreBinding;

        private readonly AnimFeedbackBuffer _feedback = new AnimFeedbackBuffer();
        private readonly AnimFeedbackEventToIntentAdapter _feedbackAdapter =
            new AnimFeedbackEventToIntentAdapter();
        private readonly AnimRootMotionRelay _rootMotionRelay =
            new AnimRootMotionRelay();
        private readonly AnimPlaybackLayer[] _layerStates =
            new AnimPlaybackLayer[AnimContractLimits.PlaybackLayerCount];
        private readonly TransitionObservation[] _transitionObservations =
            new TransitionObservation[AnimContractLimits.PlaybackLayerCount];

        private AnimParameterTarget[] _parameterTargets = Array.Empty<AnimParameterTarget>();
        private AnimTriggerTarget[] _triggerTargets = Array.Empty<AnimTriggerTarget>();
        private AnimStateTarget[] _stateTargets = Array.Empty<AnimStateTarget>();
        private AnimModulationTarget[] _modulationTargets =
            Array.Empty<AnimModulationTarget>();
        private AnimModulationStamp[] _modulationStamps =
            Array.Empty<AnimModulationStamp>();
        private int[] _controllerLayers = Array.Empty<int>();
        private IAnimModulationAdapter _modulationAdapter;
        private PlayableGraph _graph;
        private AnimatorControllerPlayable _controllerPlayable;
        private AnimationPlayableOutput _output;
        private CoCoStateGraphHost _attachedTemporalHost;
        private CoCoDiagnostic _lastDiagnostic;
        private AnimFeedbackSourceStamp _candidateFeedbackStamp;
        private CoCoGraphInstanceId _boundGraphInstanceId;
        private bool _isInitialized;
        private bool _isEvaluating;
        private bool _isHeld;
        private bool _hasAnimatorSettingsSnapshot;
        private bool _originalApplyRootMotion;

        public CoCoOperatorDescriptor Descriptor => AnimOperatorContracts.AdvancedDescriptor;
        public Animator Animator => animator;
        public RuntimeAnimatorController Controller => controller;
        public CoCoStateGraphHost StateGraphHost => stateGraphHost;
        public AnimEvaluationMode EvaluationMode => evaluationMode;
        public AnimExactReplayStatus ExactTemporalReplay => AnimExactReplayStatus.Deferred;
        public AnimPlaybackContext CurrentPlayback =>
            TryReadCommittedPlayback(out AnimPlaybackContext playback)
                ? playback
                : default;
        public CoCoDiagnostic LastDiagnostic => _lastDiagnostic;

        MonoBehaviour ICoCoTemporalDecoratorBinding.DownstreamRestoreBinding =>
            downstreamRestoreBinding;

        private void Reset()
        {
            animator = GetComponent<Animator>();
            controller = animator == null ? null : animator.runtimeAnimatorController;
            stateGraphHost = GetComponentInParent<CoCoStateGraphHost>();
        }

        private void Awake()
        {
            TryRebuildBindings(out _lastDiagnostic);
        }

        private void OnDestroy()
        {
            ((ICoCoStateGraphTemporalParticipant)this).DetachTemporalHostNoFail();
            DisposeRuntime();
        }

        private void OnAnimatorMove()
        {
            if (_isEvaluating && animator != null)
            {
                _rootMotionRelay.Capture(
                    animator.deltaPosition,
                    animator.deltaRotation);
            }
        }

        public bool TryRebuildBindings(out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            DisposeRuntime();
            animator ??= GetComponent<Animator>();
            stateGraphHost ??= GetComponentInParent<CoCoStateGraphHost>();
            controller ??= animator == null ? null : animator.runtimeAnimatorController;
            if (animator == null ||
                controller == null ||
                animator.runtimeAnimatorController != controller ||
                stateGraphHost == null ||
                !CoCoStateGraphHostBoundary.Contains(stateGraphHost, this) ||
                animator.GetComponent<AnimAutoOperator>() != null ||
                evaluationMode < AnimEvaluationMode.Tick ||
                evaluationMode > AnimEvaluationMode.Step ||
                (stateGraphHost.TemporalHistoryCapacity > 0 &&
                 !ReferenceEquals(stateGraphHost.ContextRestoreBinding, this)) ||
                !AnimBindingRuntime.TryBuildParameters(
                    animator,
                    parameterBindings,
                    out _parameterTargets,
                    out diagnostic) ||
                !AnimBindingRuntime.TryBuildTriggers(
                    animator,
                    triggerBindings,
                    out _triggerTargets,
                    out diagnostic) ||
                !AnimBindingRuntime.TryBuildModulation(
                    animator,
                    modulationBindings,
                    out _modulationTargets,
                    out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = AnimOperatorContracts.Error(
                        "AnimOperator requires one same-boundary Host, the Animator's " +
                        "assigned Controller, unique mappings, and no AnimAutoOperator. " +
                        "When Temporal history is enabled it must be the root Restore Binding.");
                }

                return FailInitialization(diagnostic);
            }

            try
            {
                _graph = PlayableGraph.Create(name + ".AnimOperator");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                _controllerPlayable =
                    AnimatorControllerPlayable.Create(_graph, controller);
                if (!AnimBindingRuntime.TryBuildPlayback(
                        _controllerPlayable,
                        playbackLayers,
                        stateBindings,
                        out _controllerLayers,
                        out _stateTargets,
                        out diagnostic))
                {
                    return FailInitialization(diagnostic);
                }

                _output = AnimationPlayableOutput.Create(
                    _graph,
                    "AnimatorController",
                    animator);
                _output.SetSourcePlayable(_controllerPlayable);
                _graph.Play();
                _originalApplyRootMotion = animator.applyRootMotion;
                _hasAnimatorSettingsSnapshot = true;
                _rootMotionRelay.Configure(
                    enableRootMotionRelay,
                    relayPosition,
                    relayRotation);
                animator.applyRootMotion = _rootMotionRelay.Enabled;
                _modulationStamps =
                    new AnimModulationStamp[_modulationTargets.Length];
                _modulationAdapter = AnimModulationAdapterRegistry.Create(this);
                InitializePlaybackContext();
                _boundGraphInstanceId = stateGraphHost.GraphInstanceId;
                _isInitialized = true;
                diagnostic = CoCoDiagnostic.None;
                _lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception)
            {
                diagnostic = AnimOperatorContracts.Error(
                    "AnimOperator could not create its manual AnimatorControllerPlayable graph.");
                return FailInitialization(diagnostic);
            }
        }

        public bool TryExecute(
            in CoCoOperatorExecutionContext context,
            out CoCoOperatorOutcome outcome)
        {
            if (!TryPrepareGraphInstance(out CoCoDiagnostic graphDiagnostic))
            {
                _lastDiagnostic = graphDiagnostic;
                outcome = CoCoOperatorOutcome.Rejected(_lastDiagnostic);
                return true;
            }

            _feedback.PrepareForExecution(context, stateGraphHost);
            if (_feedback.Overflowed)
            {
                return Reject(
                    "AnimOperator feedback overflowed its fixed reliable batch of 16 records. " +
                    "The entire batch is rejected; stop, rebuild bindings, then start the Host to recover.",
                    out outcome);
            }

            if (!TryPrevalidate(
                    context,
                    out CoCoOperationSectionEntry<IAnimParameterOperationSection> parameters,
                    out CoCoOperationSectionEntry<IAnimTriggerOperationSection> triggers,
                    out CoCoOperationSectionEntry<IAnimPlaybackOperationSection> playback,
                    out CoCoOperationSectionEntry<IAnimModulationOperationSection> modulation,
                    out CoCoStateSlot<AnimPlaybackContext> outcomeSlot,
                    out EvaluationPlan evaluationPlan))
            {
                return Reject(
                    "AnimOperator input, mapping, Playable, or evaluation prevalidation failed.",
                    out outcome);
            }

            if (!AnimFeedbackSourceStamp.TryCaptureCandidate(
                    context,
                    stateGraphHost,
                    out _candidateFeedbackStamp))
            {
                return Reject(
                    "AnimOperator could not capture the candidate feedback identity.",
                    out outcome);
            }

            try
            {
                return TryExecuteCandidate(
                    context,
                    parameters,
                    triggers,
                    playback,
                    modulation,
                    outcomeSlot,
                    evaluationPlan,
                    out outcome);
            }
            finally
            {
                _candidateFeedbackStamp = default;
            }
        }

        private bool TryExecuteCandidate(
            in CoCoOperatorExecutionContext context,
            in CoCoOperationSectionEntry<IAnimParameterOperationSection> parameters,
            in CoCoOperationSectionEntry<IAnimTriggerOperationSection> triggers,
            in CoCoOperationSectionEntry<IAnimPlaybackOperationSection> playback,
            in CoCoOperationSectionEntry<IAnimModulationOperationSection> modulation,
            CoCoStateSlot<AnimPlaybackContext> outcomeSlot,
            in EvaluationPlan evaluationPlan,
            out CoCoOperatorOutcome outcome)
        {
            bool changed = ApplyParameters(parameters.View);
            if (triggers.Header.Enabled)
            {
                changed |= ApplyTriggers(triggers.View);
            }

            changed |= ApplyModulationCommands(modulation.View);

            if (playback.Header.Enabled)
            {
                changed |= ApplyPlaybackCommands(
                    playback,
                    stateGraphHost.GraphInstanceId,
                    context.TickFrame.TimelineEpoch);
            }

            if (evaluationPlan.ShouldEvaluate)
            {
                if (_modulationAdapter != null &&
                    !_modulationAdapter.TryManualUpdate(
                        evaluationPlan.DeltaSeconds,
                        out _lastDiagnostic))
                {
                    Array.Clear(
                        _modulationStamps,
                        0,
                        _modulationStamps.Length);
                    outcome = CoCoOperatorOutcome.Rejected(_lastDiagnostic);
                    return true;
                }

                _rootMotionRelay.ResetEvaluation();
                _isEvaluating = true;
                try
                {
                    _graph.Evaluate(evaluationPlan.DeltaSeconds);
                }
                finally
                {
                    _isEvaluating = false;
                }

                changed = true;
                if (_rootMotionRelay.TryComplete(
                        animator,
                        out AnimFeedbackRecord rootMotion))
                {
                    _feedback.TryAppend(
                        rootMotion,
                        _candidateFeedbackStamp);
                }

                UpdatePlaybackStates();
            }

            AnimPlaybackContext playbackContext = BuildPlaybackContext();
            if (changed &&
                !context.TryWriteOutcome(outcomeSlot, playbackContext))
            {
                outcome = default;
                return false;
            }

            bool wroteFeedback = _feedback.Count > 0;
            if (wroteFeedback && !_feedback.TryWrite(context, stateGraphHost))
            {
                outcome = default;
                return false;
            }

            outcome = changed || wroteFeedback
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

        public bool TryGetPlayback(
            AnimPlaybackLayerSlot layer,
            out AnimPlaybackLayer playback)
        {
            if (layer < AnimPlaybackLayerSlot.Layer00 ||
                layer > AnimPlaybackLayerSlot.Layer03 ||
                !TryReadCommittedPlayback(out AnimPlaybackContext context))
            {
                playback = default;
                return false;
            }

            playback = context.GetLayer(layer);
            return playback.IsValid;
        }

        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = AnimOperatorContracts.RestoreUnavailable();
            _lastDiagnostic = diagnostic;
            return false;
        }

        bool IAnimEventReceiver.TryReceiveSmbSignal(in AnimSmbSignal signal)
        {
            AnimFeedbackSourceStamp source;
            if (_isEvaluating)
            {
                source = _candidateFeedbackStamp;
            }
            else if (!AnimFeedbackSourceStamp.TryCaptureCommitted(
                         stateGraphHost,
                         out source))
            {
                _lastDiagnostic = AnimOperatorContracts.Error(
                    "AnimOperator SMB feedback buffer rejected an unattributable signal.");
                return false;
            }

            if (!AnimAutoOperator.TryCreateSmbFeedback(
                    signal,
                    out AnimFeedbackRecord record) ||
                !source.IsValid ||
                !_feedback.TryAppend(
                    record,
                    source))
            {
                _lastDiagnostic = AnimOperatorContracts.Error(
                    _feedback.Overflowed
                        ? "AnimOperator SMB feedback overflowed its fixed reliable batch of 16 records. " +
                          "The entire batch is rejected; stop, rebuild bindings, then start the Host to recover."
                        : "AnimOperator SMB feedback buffer rejected an unattributable or invalid signal.");
                return false;
            }

            return true;
        }

        bool IAnimEventStateResolver.TryCaptureSmbState(
            int layerIndex,
            out AnimSmbStateSnapshot snapshot)
        {
            if (!_controllerPlayable.IsValid() ||
                layerIndex < 0 ||
                layerIndex >= _controllerPlayable.GetLayerCount())
            {
                snapshot = default;
                return false;
            }

            bool inTransition =
                _controllerPlayable.IsInTransition(layerIndex);
            snapshot = new AnimSmbStateSnapshot(
                _controllerPlayable.GetCurrentAnimatorStateInfo(
                    layerIndex),
                inTransition,
                inTransition
                    ? _controllerPlayable.GetNextAnimatorStateInfo(
                        layerIndex)
                    : default);
            return snapshot.IsValid;
        }

        bool IAnimModulationHost.TryReadModulation(
            in AnimModulationTarget target,
            out Vector4 value)
        {
            if (!_controllerPlayable.IsValid())
            {
                value = default;
                return false;
            }

            switch (target.Kind)
            {
                case AnimModulationKind.FloatParameter:
                    value = new Vector4(
                        _controllerPlayable.GetFloat(target.ParameterHash),
                        0f,
                        0f,
                        0f);
                    return true;
                case AnimModulationKind.LayerWeight:
                    value = new Vector4(
                        _controllerPlayable.GetLayerWeight(target.ControllerLayer),
                        0f,
                        0f,
                        0f);
                    return true;
                case AnimModulationKind.PresentationOffsetPosition:
                    if (target.PresentationOffset != null)
                    {
                        Vector3 position = target.PresentationOffset.localPosition;
                        value = new Vector4(position.x, position.y, position.z, 0f);
                        return true;
                    }

                    break;
                case AnimModulationKind.PresentationOffsetRotation:
                    if (target.PresentationOffset != null)
                    {
                        Quaternion rotation = target.PresentationOffset.localRotation;
                        value = new Vector4(
                            rotation.x,
                            rotation.y,
                            rotation.z,
                            rotation.w);
                        return true;
                    }

                    break;
            }

            value = default;
            return false;
        }

        bool IAnimModulationHost.TryWriteModulation(
            in AnimModulationTarget target,
            in Vector4 value)
        {
            if (!_controllerPlayable.IsValid() ||
                !IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z) ||
                !IsFinite(value.w))
            {
                return false;
            }

            switch (target.Kind)
            {
                case AnimModulationKind.FloatParameter:
                    _controllerPlayable.SetFloat(target.ParameterHash, value.x);
                    return true;
                case AnimModulationKind.LayerWeight:
                    if (value.x < 0f || value.x > 1f)
                    {
                        return false;
                    }

                    _controllerPlayable.SetLayerWeight(target.ControllerLayer, value.x);
                    return true;
                case AnimModulationKind.PresentationOffsetPosition:
                    if (target.PresentationOffset != null)
                    {
                        target.PresentationOffset.localPosition =
                            new Vector3(value.x, value.y, value.z);
                        return true;
                    }

                    break;
                case AnimModulationKind.PresentationOffsetRotation:
                    if (target.PresentationOffset != null)
                    {
                        var rotation = new Quaternion(
                            value.x,
                            value.y,
                            value.z,
                            value.w);
                        float magnitude = Mathf.Sqrt(
                            rotation.x * rotation.x +
                            rotation.y * rotation.y +
                            rotation.z * rotation.z +
                            rotation.w * rotation.w);
                        if (magnitude <= 0.000001f)
                        {
                            return false;
                        }

                        target.PresentationOffset.localRotation =
                            new Quaternion(
                                rotation.x / magnitude,
                                rotation.y / magnitude,
                                rotation.z / magnitude,
                                rotation.w / magnitude);
                        return true;
                    }

                    break;
            }

            return false;
        }

        bool ICoCoStateGraphTemporalParticipant.TryAttachTemporalHost(
            CoCoStateGraphHost host,
            int historyCapacity,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (!_isInitialized ||
                _attachedTemporalHost != null ||
                host == null ||
                !ReferenceEquals(host, stateGraphHost) ||
                historyCapacity <= 0 ||
                !ReferenceEquals(host.ContextRestoreBinding, this) ||
                !CoCoStateGraphHostBoundary.Contains(host, this) ||
                !CoCoTemporalDecoratorChain.TryValidate(host, this, out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = AnimOperatorContracts.Error(
                        "AnimOperator Temporal fail-closed binding must be the " +
                        "same-boundary root Restore Binding.");
                }

                _lastDiagnostic = diagnostic;
                return false;
            }

            _attachedTemporalHost = host;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        bool ICoCoStateGraphTemporalParticipant.IsTemporalParticipantLive(
            CoCoStateGraphHost host)
        {
            return _attachedTemporalHost != null &&
                   _isInitialized &&
                   ReferenceEquals(_attachedTemporalHost, host) &&
                   ReferenceEquals(host, stateGraphHost) &&
                   ReferenceEquals(host.ContextRestoreBinding, this) &&
                   CoCoStateGraphHostBoundary.Contains(host, this);
        }

        bool ICoCoStateGraphTemporalParticipant.TryPrepareForwardCapture(
            in CoCoTemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic)
        {
            if (_attachedTemporalHost == null ||
                !_isInitialized ||
                !candidate.IsValid)
            {
                diagnostic = AnimOperatorContracts.Error(
                    "AnimOperator Temporal forward-capture participant is not live.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        void ICoCoStateGraphTemporalParticipant.PublishForwardCaptureNoFail()
        {
        }

        void ICoCoStateGraphTemporalParticipant.CancelPreparedCaptureNoFail()
        {
        }

        bool ICoCoStateGraphTemporalParticipant.TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = AnimOperatorContracts.RestoreUnavailable();
            _lastDiagnostic = diagnostic;
            return false;
        }

        bool ICoCoStateGraphTemporalParticipant.TryPrepareProjection(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = AnimOperatorContracts.RestoreUnavailable();
            _lastDiagnostic = diagnostic;
            return false;
        }

        void ICoCoStateGraphTemporalParticipant.FinishProjectionNoFail(bool succeeded)
        {
        }

        bool ICoCoStateGraphTemporalParticipant.CanConfirmPreview(int historyDepth)
        {
            return false;
        }

        bool ICoCoStateGraphTemporalParticipant.TryPrepareBranchCapture(
            int historyDepth,
            in CoCoTemporalFrameInfo branchHead,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = AnimOperatorContracts.RestoreUnavailable();
            _lastDiagnostic = diagnostic;
            return false;
        }

        void ICoCoStateGraphTemporalParticipant.PublishBranchCaptureNoFail()
        {
        }

        void ICoCoStateGraphTemporalParticipant.CompletePreviewNoFail(
            CoCoContextRestoreApplyKind applyKind)
        {
        }

        void ICoCoStateGraphTemporalParticipant.DrainPublishedCleanupNoFail()
        {
        }

        void ICoCoStateGraphTemporalParticipant.DetachTemporalHostNoFail()
        {
            _attachedTemporalHost = null;
        }

        private bool TryPrevalidate(
            in CoCoOperatorExecutionContext context,
            out CoCoOperationSectionEntry<IAnimParameterOperationSection> parameters,
            out CoCoOperationSectionEntry<IAnimTriggerOperationSection> triggers,
            out CoCoOperationSectionEntry<IAnimPlaybackOperationSection> playback,
            out CoCoOperationSectionEntry<IAnimModulationOperationSection> modulation,
            out CoCoStateSlot<AnimPlaybackContext> outcomeSlot,
            out EvaluationPlan evaluationPlan)
        {
            if (!context.IsValid ||
                !_isInitialized ||
                animator == null ||
                stateGraphHost == null ||
                !_graph.IsValid() ||
                !_controllerPlayable.IsValid() ||
                _feedback.Overflowed ||
                !CoCoStateGraphHostBoundary.Contains(stateGraphHost, this) ||
                !context.TryGet(AnimOperatorContracts.ParameterRequirement, out parameters) ||
                !context.TryGet(AnimOperatorContracts.TriggerRequirement, out triggers) ||
                !context.TryGet(AnimOperatorContracts.PlaybackRequirement, out playback) ||
                !context.TryGet(AnimOperatorContracts.ModulationRequirement, out modulation) ||
                !context.PreviousContext.Layout.TryResolveSlot(
                    AnimContractIds.PlaybackContextSlotId,
                    out outcomeSlot) ||
                !TryResolveEvaluationPlan(
                    playback,
                    context.TickFrame.DeltaTime,
                    out evaluationPlan) ||
                !AnimBindingRuntime.ValidateParameters(
                    parameters.View,
                    _parameterTargets) ||
                (triggers.Header.Enabled &&
                 !AnimBindingRuntime.ValidateTriggers(
                     triggers.View,
                     _triggerTargets)) ||
                !ValidateModulation(modulation.View) ||
                (playback.Header.Enabled && !ValidatePlayback(playback.View)))
            {
                parameters = default;
                triggers = default;
                playback = default;
                modulation = default;
                outcomeSlot = default;
                evaluationPlan = default;
                return false;
            }

            return true;
        }

        private bool ValidatePlayback(IAnimPlaybackOperationSection section)
        {
            AnimPlaybackCommand control = section.Control;
            if (control.Kind != AnimPlaybackCommandKind.None &&
                (!control.IsValid ||
                 !control.IsControlCommand ||
                 (control.Kind == AnimPlaybackCommandKind.Step &&
                  evaluationMode != AnimEvaluationMode.Step)))
            {
                return false;
            }

            bool hasLayerCommand = false;
            for (int lane = 0; lane < AnimContractLimits.PlaybackLayerCount; lane++)
            {
                AnimPlaybackCommand command = AnimOperationLaneReader.Read(section, lane);
                if (command.Kind == AnimPlaybackCommandKind.None)
                {
                    continue;
                }

                hasLayerCommand = true;
                if (!command.IsValid ||
                    !command.IsLayerCommand ||
                    lane >= _controllerLayers.Length ||
                    !AnimBindingRuntime.TryFind(
                        _stateTargets,
                        command.StateBindingId,
                        out AnimStateTarget target) ||
                    target.ControllerLayer != _controllerLayers[lane])
                {
                    return false;
                }
            }

            return IsPlaybackControlAllowed(
                control.Kind,
                _isHeld,
                hasLayerCommand);
        }

        private bool ValidateModulation(IAnimModulationOperationSection section)
        {
            for (int lane = 0; lane < AnimContractLimits.ModulationLaneCount; lane++)
            {
                AnimModulationCommand command = AnimOperationLaneReader.Read(section, lane);
                if (command.Kind == AnimModulationKind.None)
                {
                    continue;
                }

                if (!command.IsValid ||
                    !AnimBindingRuntime.TryFind(
                        _modulationTargets,
                        command.BindingId,
                        out AnimModulationTarget target) ||
                    target.Kind != command.Kind ||
                    HasEarlierModulationTarget(section, lane, command.BindingId) ||
                    !ValidateModulationValue(command) ||
                    (command.Interpolation == AnimModulationInterpolation.AdapterOwned &&
                     !EnsureModulationAdapter()))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ApplyParameters(IAnimParameterOperationSection section)
        {
            bool changed = false;
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
                        _controllerPlayable.SetFloat(target.ParameterHash, command.FloatValue);
                        break;
                    case AnimParameterValueKind.Integer:
                        _controllerPlayable.SetInteger(target.ParameterHash, command.IntegerValue);
                        break;
                    case AnimParameterValueKind.Boolean:
                        _controllerPlayable.SetBool(target.ParameterHash, command.BooleanValue);
                        break;
                }

                changed = true;
            }

            return changed;
        }

        private bool ApplyTriggers(IAnimTriggerOperationSection section)
        {
            bool changed = false;
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
                    _controllerPlayable.SetTrigger(target.ParameterHash);
                }
                else
                {
                    _controllerPlayable.ResetTrigger(target.ParameterHash);
                }

                changed = true;
            }

            return changed;
        }

        private bool ApplyPlaybackCommands(
            CoCoOperationSectionEntry<IAnimPlaybackOperationSection> entry,
            CoCoGraphInstanceId graphInstanceId,
            CoCoTimelineEpoch timelineEpoch)
        {
            bool changed = false;
            AnimPlaybackCommand control = entry.View.Control;
            if (control.Kind == AnimPlaybackCommandKind.Stop)
            {
                _isHeld = true;
                _modulationAdapter?.StopAll();
                Array.Clear(
                    _modulationStamps,
                    0,
                    _modulationStamps.Length);
                InterruptAllPlayback();
                return true;
            }

            if (control.Kind == AnimPlaybackCommandKind.Step)
            {
                changed = true;
            }

            for (int lane = 0; lane < AnimContractLimits.PlaybackLayerCount; lane++)
            {
                AnimPlaybackCommand command = AnimOperationLaneReader.Read(entry.View, lane);
                if (command.Kind == AnimPlaybackCommandKind.None)
                {
                    continue;
                }

                AnimBindingRuntime.TryFind(
                    _stateTargets,
                    command.StateBindingId,
                    out AnimStateTarget target);
                AnimPlaybackLayerSlot slot = (AnimPlaybackLayerSlot)(lane + 1);
                AnimPlaybackToken.TryCreate(
                    graphInstanceId,
                    command.SourceActivationId,
                    timelineEpoch,
                    entry.Header.OperationSequence,
                    slot,
                    out AnimPlaybackToken token);
                InterruptLayer(lane);
                if (command.Kind == AnimPlaybackCommandKind.Play)
                {
                    _controllerPlayable.Play(
                        target.StateHash,
                        target.ControllerLayer,
                        command.StartNormalizedTime);
                    _layerStates[lane] = new AnimPlaybackLayer(
                        slot,
                        token,
                        command.StateBindingId,
                        AnimPlaybackStatus.Playing,
                        command.StartNormalizedTime);
                }
                else
                {
                    _controllerPlayable.CrossFadeInFixedTime(
                        target.StateHash,
                        command.TransitionDurationSeconds,
                        target.ControllerLayer,
                        command.StartNormalizedTime);
                    _layerStates[lane] = new AnimPlaybackLayer(
                        slot,
                        token,
                        command.StateBindingId,
                        AnimPlaybackStatus.CrossFading,
                        command.StartNormalizedTime);
                }

                _isHeld = false;
                AppendPlaybackFeedback(
                    AnimFeedbackKind.PlaybackStarted,
                    token,
                    command.StartNormalizedTime);
                changed = true;
            }

            return changed;
        }

        private bool ApplyModulationCommands(IAnimModulationOperationSection section)
        {
            bool changed = false;
            for (int lane = 0; lane < AnimContractLimits.ModulationLaneCount; lane++)
            {
                AnimModulationCommand command = AnimOperationLaneReader.Read(section, lane);
                if (command.Kind == AnimModulationKind.None)
                {
                    continue;
                }

                AnimBindingRuntime.TryFind(
                    _modulationTargets,
                    command.BindingId,
                    out AnimModulationTarget target);
                if (command.Interpolation == AnimModulationInterpolation.Immediate)
                {
                    _modulationAdapter?.Stop(target);
                    int immediateTargetIndex = FindModulationTargetIndex(
                        command.BindingId);
                    _modulationStamps[immediateTargetIndex] = default;
                    if (!((IAnimModulationHost)this).TryWriteModulation(
                            target,
                            new Vector4(
                                command.ValueX,
                                command.ValueY,
                                command.ValueZ,
                                command.ValueW)))
                    {
                        throw new InvalidOperationException(
                            "A prevalidated immediate Animation modulation could not be applied.");
                    }

                    changed = true;
                    continue;
                }

                var stamp = new AnimModulationStamp(
                    command.BindingId,
                    command.SourceActivationId,
                    command.Serial);
                int targetIndex = FindModulationTargetIndex(command.BindingId);
                if (_modulationStamps[targetIndex].Equals(stamp))
                {
                    continue;
                }

                if (!_modulationAdapter.TryStart(command, target, out _lastDiagnostic))
                {
                    throw new InvalidOperationException(
                        "The installed Animation modulation adapter rejected a prevalidated command.");
                }

                _modulationStamps[targetIndex] = stamp;
                changed = true;
            }

            return changed;
        }

        private void UpdatePlaybackStates()
        {
            for (int lane = 0; lane < _controllerLayers.Length; lane++)
            {
                AnimPlaybackLayer previous = _layerStates[lane];
                if (!previous.IsActive)
                {
                    continue;
                }

                int controllerLayer = _controllerLayers[lane];
                AnimBindingRuntime.TryFind(
                    _stateTargets,
                    previous.StateBindingId,
                    out AnimStateTarget target);
                bool inTransition = _controllerPlayable.IsInTransition(controllerLayer);
                AnimatorStateInfo current =
                    _controllerPlayable.GetCurrentAnimatorStateInfo(controllerLayer);
                AnimatorStateInfo next = inTransition
                    ? _controllerPlayable.GetNextAnimatorStateInfo(controllerLayer)
                    : default;
                bool currentMatches = current.fullPathHash == target.StateHash;
                bool nextMatches = inTransition && next.fullPathHash == target.StateHash;
                bool sameStateTransition =
                    currentMatches && nextMatches;
                float normalizedTime = sameStateTransition
                    ? Mathf.Max(0f, next.normalizedTime)
                    : currentMatches
                    ? Mathf.Max(0f, current.normalizedTime)
                    : nextMatches
                        ? Mathf.Max(0f, next.normalizedTime)
                        : previous.NormalizedTime;
                bool outgoingTransition =
                    !sameStateTransition &&
                    inTransition &&
                    currentMatches &&
                    !nextMatches;
                if (sameStateTransition)
                {
                    _transitionObservations[lane] = default;
                }
                else if (outgoingTransition)
                {
                    TransitionObservation observation =
                        ObserveTransition(
                            lane,
                            previous.Token,
                            current,
                            _controllerPlayable.GetAnimatorTransitionInfo(
                                controllerLayer));
                    if (observation.StartedBeforeCompletion)
                    {
                        CompletePlayback(
                            lane,
                            previous,
                            AnimPlaybackStatus.Interrupted,
                            AnimFeedbackKind.PlaybackInterrupted,
                            normalizedTime);
                        continue;
                    }

                    if (!current.loop && current.normalizedTime >= 1f)
                    {
                        normalizedTime = Mathf.Max(
                            normalizedTime,
                            1f);
                        CompletePlayback(
                            lane,
                            previous,
                            AnimPlaybackStatus.Completed,
                            AnimFeedbackKind.PlaybackCompleted,
                            normalizedTime);
                        continue;
                    }
                }
                else
                {
                    _transitionObservations[lane] = default;
                }

                if (!sameStateTransition &&
                    !outgoingTransition &&
                    currentMatches &&
                    !current.loop &&
                    current.normalizedTime >= 1f)
                {
                    normalizedTime = Mathf.Max(
                        normalizedTime,
                        1f);
                    CompletePlayback(
                        lane,
                        previous,
                        AnimPlaybackStatus.Completed,
                        AnimFeedbackKind.PlaybackCompleted,
                        normalizedTime);
                    continue;
                }

                if ((!currentMatches && !nextMatches) ||
                    outgoingTransition)
                {
                    CompletePlayback(
                        lane,
                        previous,
                        AnimPlaybackStatus.Interrupted,
                        AnimFeedbackKind.PlaybackInterrupted,
                        normalizedTime);
                    continue;
                }

                _layerStates[lane] = new AnimPlaybackLayer(
                    previous.Slot,
                    previous.Token,
                    previous.StateBindingId,
                    nextMatches
                        ? AnimPlaybackStatus.CrossFading
                        : AnimPlaybackStatus.Playing,
                    normalizedTime);
            }
        }

        private void InterruptAllPlayback()
        {
            for (int lane = 0; lane < _layerStates.Length; lane++)
            {
                InterruptLayer(lane);
            }
        }

        private void InterruptLayer(int lane)
        {
            _transitionObservations[lane] = default;
            AnimPlaybackLayer previous = _layerStates[lane];
            if (!previous.IsActive)
            {
                return;
            }

            _layerStates[lane] = new AnimPlaybackLayer(
                previous.Slot,
                previous.Token,
                previous.StateBindingId,
                AnimPlaybackStatus.Interrupted,
                previous.NormalizedTime);
            AppendPlaybackFeedback(
                AnimFeedbackKind.PlaybackInterrupted,
                previous.Token,
                previous.NormalizedTime);
        }

        private void CompletePlayback(
            int lane,
            in AnimPlaybackLayer previous,
            AnimPlaybackStatus status,
            AnimFeedbackKind feedbackKind,
            float normalizedTime)
        {
            _transitionObservations[lane] = default;
            _layerStates[lane] = new AnimPlaybackLayer(
                previous.Slot,
                previous.Token,
                previous.StateBindingId,
                status,
                normalizedTime);
            AppendPlaybackFeedback(
                feedbackKind,
                previous.Token,
                normalizedTime);
        }

        private TransitionObservation ObserveTransition(
            int lane,
            AnimPlaybackToken token,
            in AnimatorStateInfo current,
            in AnimatorTransitionInfo transition)
        {
            TransitionObservation observation =
                _transitionObservations[lane];
            if (observation.HasValue && observation.Token == token)
            {
                return observation;
            }

            bool startedBeforeCompletion =
                !TryRecoverTransitionStart(
                    current,
                    transition,
                    out float startNormalizedTime) ||
                startNormalizedTime < 0.9999f;
            observation = new TransitionObservation(
                token,
                startedBeforeCompletion);
            _transitionObservations[lane] = observation;
            return observation;
        }

        private static bool TryRecoverTransitionStart(
            in AnimatorStateInfo current,
            in AnimatorTransitionInfo transition,
            out float startNormalizedTime)
        {
            startNormalizedTime = default;
            if (!IsFinite(current.normalizedTime) ||
                !IsFinite(transition.normalizedTime) ||
                !IsFinite(transition.duration) ||
                transition.normalizedTime < 0f ||
                transition.duration < 0f)
            {
                return false;
            }

            float normalizedAdvance;
            if (transition.durationUnit == DurationUnit.Fixed)
            {
                float effectiveSpeed =
                    Mathf.Abs(current.speed * current.speedMultiplier);
                if (!IsFinite(current.length) ||
                    !IsFinite(effectiveSpeed) ||
                    current.length <= 0f ||
                    effectiveSpeed <= 0f)
                {
                    return false;
                }

                normalizedAdvance =
                    transition.normalizedTime *
                    transition.duration /
                    current.length;
            }
            else
            {
                normalizedAdvance =
                    transition.normalizedTime *
                    transition.duration;
            }

            startNormalizedTime =
                current.normalizedTime - normalizedAdvance;
            return IsFinite(normalizedAdvance) &&
                   normalizedAdvance >= 0f &&
                   IsFinite(startNormalizedTime);
        }

        private void AppendPlaybackFeedback(
            AnimFeedbackKind kind,
            AnimPlaybackToken token,
            float normalizedTime)
        {
            if (AnimFeedbackRecord.TryCreatePlayback(
                    kind,
                    token,
                    normalizedTime,
                    out AnimFeedbackRecord record))
            {
                _feedback.TryAppend(
                    record,
                    _candidateFeedbackStamp);
            }
        }

        private AnimPlaybackContext BuildPlaybackContext()
        {
            return new AnimPlaybackContext(
                _layerStates[0],
                _layerStates[1],
                _layerStates[2],
                _layerStates[3],
                _isHeld);
        }

        private void InitializePlaybackContext()
        {
            for (int lane = 0; lane < _layerStates.Length; lane++)
            {
                _layerStates[lane] = new AnimPlaybackLayer(
                    (AnimPlaybackLayerSlot)(lane + 1),
                    default,
                    default,
                    AnimPlaybackStatus.None,
                    0f);
                _transitionObservations[lane] = default;
            }

            _isHeld = false;
        }

        private bool TryResolveEvaluationPlan(
            in CoCoOperationSectionEntry<IAnimPlaybackOperationSection> playback,
            double tickDeltaSeconds,
            out EvaluationPlan plan)
        {
            bool willBeHeld = _isHeld;
            AnimPlaybackCommand control = default;
            if (playback.Header.Enabled)
            {
                control = playback.View.Control;
                if (control.Kind == AnimPlaybackCommandKind.Stop)
                {
                    willBeHeld = true;
                }

                for (int lane = 0;
                     lane < AnimContractLimits.PlaybackLayerCount;
                     lane++)
                {
                    AnimPlaybackCommand command =
                        AnimOperationLaneReader.Read(playback.View, lane);
                    if (command.Kind == AnimPlaybackCommandKind.None)
                    {
                        continue;
                    }

                    willBeHeld = false;
                }
            }

            if (control.Kind == AnimPlaybackCommandKind.Step)
            {
                plan = new EvaluationPlan(true, control.StepDeltaSeconds);
                return plan.IsValid;
            }

            if (evaluationMode != AnimEvaluationMode.Tick || willBeHeld)
            {
                plan = EvaluationPlan.NoEvaluation;
                return true;
            }

            if (!TryConvertTickDeltaSeconds(
                    tickDeltaSeconds,
                    out float deltaSeconds))
            {
                plan = default;
                return false;
            }

            plan = new EvaluationPlan(true, deltaSeconds);
            return true;
        }

        private bool TryReadCommittedPlayback(
            out AnimPlaybackContext playback)
        {
            CoCoContextFrame context = stateGraphHost == null
                ? default
                : stateGraphHost.CurrentContext;
            if (!context.IsAlive ||
                context.Layout == null ||
                !context.Layout.TryResolveSlot(
                    AnimContractIds.PlaybackContextSlotId,
                    out CoCoStateSlot<AnimPlaybackContext> slot))
            {
                playback = default;
                return false;
            }

            playback = context.Read(slot);
            return true;
        }

        private bool ValidateModulationValue(in AnimModulationCommand command)
        {
            if (command.Kind == AnimModulationKind.LayerWeight &&
                (command.ValueX < 0f || command.ValueX > 1f))
            {
                return false;
            }

            if (command.Kind == AnimModulationKind.PresentationOffsetRotation)
            {
                float magnitudeSquared =
                    command.ValueX * command.ValueX +
                    command.ValueY * command.ValueY +
                    command.ValueZ * command.ValueZ +
                    command.ValueW * command.ValueW;
                return magnitudeSquared > 0.000000000001f;
            }

            return true;
        }

        private bool EnsureModulationAdapter()
        {
            _modulationAdapter ??= AnimModulationAdapterRegistry.Create(this);
            return _modulationAdapter != null;
        }

        private int FindModulationTargetIndex(AnimBindingId bindingId)
        {
            for (int index = 0; index < _modulationTargets.Length; index++)
            {
                if (_modulationTargets[index].BindingId == bindingId)
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool HasEarlierModulationTarget(
            IAnimModulationOperationSection section,
            int lane,
            AnimBindingId bindingId)
        {
            for (int previous = 0; previous < lane; previous++)
            {
                AnimModulationCommand command = AnimOperationLaneReader.Read(section, previous);
                if (command.Kind != AnimModulationKind.None &&
                    command.BindingId == bindingId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool Reject(string message, out CoCoOperatorOutcome outcome)
        {
            _lastDiagnostic = AnimOperatorContracts.Error(message);
            outcome = CoCoOperatorOutcome.Rejected(_lastDiagnostic);
            return true;
        }

        private bool TryPrepareGraphInstance(out CoCoDiagnostic diagnostic)
        {
            CoCoGraphInstanceId current =
                stateGraphHost == null ? default : stateGraphHost.GraphInstanceId;
            if (!current.IsValid)
            {
                diagnostic = AnimOperatorContracts.Error(
                    "AnimOperator requires a running Host GraphInstance before execution.");
                return false;
            }

            if (!_boundGraphInstanceId.IsValid)
            {
                _boundGraphInstanceId = current;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (_boundGraphInstanceId == current)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (!TryRebuildBindings(out diagnostic))
            {
                return false;
            }

            current = stateGraphHost == null
                ? default
                : stateGraphHost.GraphInstanceId;
            if (!current.IsValid || _boundGraphInstanceId != current)
            {
                diagnostic = AnimOperatorContracts.Error(
                    "AnimOperator could not bind its Playable runtime to the current Host GraphInstance.");
                return false;
            }

            return true;
        }

        private bool FailInitialization(CoCoDiagnostic diagnostic)
        {
            _isInitialized = false;
            _lastDiagnostic = diagnostic;
            DisposeRuntime();
            return false;
        }

        private void DisposeRuntime()
        {
            _isInitialized = false;
            _isEvaluating = false;
            _candidateFeedbackStamp = default;
            _boundGraphInstanceId = default;
            _modulationAdapter?.StopAll();
            _modulationAdapter?.Dispose();
            _modulationAdapter = null;
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _graph = default;
            _controllerPlayable = default;
            _output = default;
            _feedback.Clear();
            if (_hasAnimatorSettingsSnapshot && animator != null)
            {
                animator.applyRootMotion = _originalApplyRootMotion;
            }

            _hasAnimatorSettingsSnapshot = false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool TryConvertTickDeltaSeconds(
            double tickDeltaSeconds,
            out float deltaSeconds)
        {
            deltaSeconds = (float)tickDeltaSeconds;
            return deltaSeconds > 0f && IsFinite(deltaSeconds);
        }

        private readonly struct EvaluationPlan
        {
            internal EvaluationPlan(bool shouldEvaluate, float deltaSeconds)
            {
                ShouldEvaluate = shouldEvaluate;
                DeltaSeconds = deltaSeconds;
            }

            internal static EvaluationPlan NoEvaluation => default;

            internal bool ShouldEvaluate { get; }
            internal float DeltaSeconds { get; }
            internal bool IsValid =>
                !ShouldEvaluate ||
                (DeltaSeconds > 0f && IsFinite(DeltaSeconds));
        }

        private readonly struct TransitionObservation
        {
            internal TransitionObservation(
                AnimPlaybackToken token,
                bool startedBeforeCompletion)
            {
                Token = token;
                StartedBeforeCompletion = startedBeforeCompletion;
                HasValue = token.IsValid;
            }

            internal AnimPlaybackToken Token { get; }
            internal bool StartedBeforeCompletion { get; }
            internal bool HasValue { get; }
        }

        internal static bool IsPlaybackControlAllowed(
            AnimPlaybackCommandKind controlKind,
            bool isHeld,
            bool hasLayerCommand)
        {
            return !(hasLayerCommand &&
                     controlKind == AnimPlaybackCommandKind.Stop) &&
                   (controlKind != AnimPlaybackCommandKind.Step ||
                    !isHeld ||
                    hasLayerCommand);
        }
    }
}

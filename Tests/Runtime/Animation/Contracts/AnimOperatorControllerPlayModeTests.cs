#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Animation
{
    public sealed class AnimOperatorControllerPlayModeTests
    {
        private const ulong OneShotBindingValue = 601UL;
        private const ulong ModulationBindingValue = 603UL;
        private const ulong EarlyExitBindingValue = 604UL;
        private const ulong PlainOneShotBindingValue = 605UL;
        private const ulong IncomingMarkerBindingValue = 606UL;
        private const ulong OutgoingMarkerBindingValue = 607UL;
        private const ulong OffsetTargetBindingValue = 608UL;
        private readonly List<UnityEngine.Object> _objects =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
        }

        [UnityTest]
        public IEnumerator RealControllerFixture_ClassifiesNaturalAndEarlyExitOnce()
        {
            AnimatorController controller = CreateControllerFixture(
                out AnimationClip rootMotionClip);
            AssertFixtureSurface(controller, rootMotionClip);

            var gameObject = new GameObject("Pre11 Animation Fixture");
            _objects.Add(gameObject);
            gameObject.SetActive(false);
            Animator animator = gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CoCoStateGraphHost host =
                gameObject.AddComponent<CoCoStateGraphHost>();
            AnimOperator animationOperator =
                gameObject.AddComponent<AnimOperator>();
            ConfigureOperator(
                animationOperator,
                animator,
                controller,
                host);
            gameObject.SetActive(true);

            Assert.IsTrue(
                animationOperator.TryRebuildBindings(
                    out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            yield return null;

            PlayableGraph graph = ReadField<PlayableGraph>(
                animationOperator,
                "_graph");
            AnimatorControllerPlayable playable =
                ReadField<AnimatorControllerPlayable>(
                    animationOperator,
                    "_controllerPlayable");
            Assert.IsTrue(graph.IsValid());
            Assert.IsTrue(playable.IsValid());

            Assert.IsTrue(
                AnimBindingId.TryCreate(
                    ModulationBindingValue,
                    out AnimBindingId modulationBindingId));
            Assert.IsTrue(
                CoCoActivationId.TryCreate(
                    21UL,
                    out CoCoActivationId modulationActivationId));
            Assert.IsTrue(
                AnimModulationCommand.TryCreate(
                    AnimModulationKind.FloatParameter,
                    modulationBindingId,
                    AnimModulationInterpolation.Immediate,
                    modulationActivationId,
                    1U,
                    0f,
                    0.75f,
                    0f,
                    0f,
                    0f,
                    out AnimModulationCommand immediate));
            var recordingAdapter = new RecordingModulationAdapter();
            SetField(
                animationOperator,
                "_modulationAdapter",
                recordingAdapter);
            MethodInfo applyModulation =
                typeof(AnimOperator).GetMethod(
                    "ApplyModulationCommands",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(applyModulation, Is.Not.Null);
            Assert.That(
                applyModulation.Invoke(
                    animationOperator,
                    new object[]
                    {
                        new ModulationSection(immediate)
                    }),
                Is.EqualTo(true));
            Assert.That(recordingAdapter.StopCount, Is.EqualTo(1));
            Assert.That(
                recordingAdapter.LastStoppedBinding,
                Is.EqualTo(modulationBindingId));
            Assert.That(
                playable.GetFloat(Animator.StringToHash("Speed")),
                Is.EqualTo(0.75f));

            AnimRootMotionRelay rootMotionRelay =
                ReadField<AnimRootMotionRelay>(
                    animationOperator,
                    "_rootMotionRelay");
            playable.Play(
                Animator.StringToHash("Base Layer.RootMotion"),
                0,
                0f);
            graph.Evaluate(0f);
            rootMotionRelay.ResetEvaluation();
            SetField(animationOperator, "_isEvaluating", true);
            try
            {
                graph.Evaluate(0.5f);
            }
            finally
            {
                SetField(animationOperator, "_isEvaluating", false);
            }

            Assert.IsTrue(
                rootMotionRelay.TryComplete(
                    animator,
                    out AnimFeedbackRecord rootMotion));
            Assert.That(
                rootMotion.Kind,
                Is.EqualTo(AnimFeedbackKind.RootMotion));
            Assert.That(rootMotion.PositionX, Is.GreaterThan(0f));

            AnimFeedbackBuffer feedback = ReadField<AnimFeedbackBuffer>(
                animationOperator,
                "_feedback");
            feedback.Clear();
            playable.Play(
                Animator.StringToHash("Base Layer.Idle"),
                0,
                0f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            Assert.IsTrue(
                AnimBindingId.TryCreate(
                    OffsetTargetBindingValue,
                    out AnimBindingId offsetTargetBinding));
            Assert.IsTrue(
                CoCoActivationId.TryCreate(
                    18UL,
                    out CoCoActivationId offsetActivation));
            Assert.IsTrue(
                AnimPlaybackCommand.TryCreateCrossFade(
                    offsetTargetBinding,
                    offsetActivation,
                    0.4f,
                    0.5f,
                    out AnimPlaybackCommand offsetCrossFade));
            Assert.IsTrue(
                CoCoOperationSequence.TryCreate(
                    8UL,
                    out CoCoOperationSequence offsetSequence));
            var offsetHeader =
                (CoCoOperationSectionEntryHeader)Activator.CreateInstance(
                    typeof(CoCoOperationSectionEntryHeader),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[]
                    {
                        true,
                        offsetActivation,
                        offsetSequence
                    },
                    null);
            var offsetEntry =
                (CoCoOperationSectionEntry<IAnimPlaybackOperationSection>)
                Activator.CreateInstance(
                    typeof(CoCoOperationSectionEntry<
                        IAnimPlaybackOperationSection>),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[]
                    {
                        offsetHeader,
                        new PlaybackSection(offsetCrossFade)
                    },
                    null);
            MethodInfo validatePlayback =
                typeof(AnimOperator).GetMethod(
                    "ValidatePlayback",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(validatePlayback, Is.Not.Null);
            Assert.That(
                validatePlayback.Invoke(
                    animationOperator,
                    new object[] { offsetEntry.View }),
                Is.EqualTo(true));
            MethodInfo applyPlayback =
                typeof(AnimOperator).GetMethod(
                    "ApplyPlaybackCommands",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(applyPlayback, Is.Not.Null);
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    1UL,
                    out CoCoGraphInstanceId offsetGraph));
            SetCandidateFeedbackStamp(animationOperator, 8UL);
            Assert.That(
                applyPlayback.Invoke(
                    animationOperator,
                    new[]
                    {
                        (object)offsetEntry,
                        offsetGraph,
                        new CoCoTimelineEpoch(0UL)
                    }),
                Is.EqualTo(true));
            EvaluateAsCandidate(animationOperator, graph, 0f);

            int offsetTargetHash =
                Animator.StringToHash("Base Layer.OffsetTarget");
            Assert.IsTrue(playable.IsInTransition(0));
            AnimatorStateInfo offsetTarget =
                playable.GetNextAnimatorStateInfo(0);
            Assert.That(offsetTarget.fullPathHash, Is.EqualTo(offsetTargetHash));
            Assert.That(offsetTarget.length, Is.EqualTo(2f).Within(0.001f));
            Assert.That(
                offsetTarget.normalizedTime,
                Is.EqualTo(0.5f).Within(0.001f),
                "CrossFade must preserve the command's normalized destination offset.");
            Assert.That(
                ReadLayers(animationOperator)[0].NormalizedTime,
                Is.EqualTo(0.5f));
            Assert.That(
                ReadField<AnimFeedbackEvent[]>(feedback, "_events")
                    .Take(feedback.Count)
                    .Single(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.PlaybackStarted)
                    .Record.NormalizedTime,
                Is.EqualTo(0.5f));
            EvaluateAsCandidate(animationOperator, graph, 0.2f);
            Assert.That(
                playable.GetAnimatorTransitionInfo(0).normalizedTime,
                Is.EqualTo(0.5f).Within(0.01f),
                "CrossFade must retain the command's transition duration in seconds.");

            feedback.Clear();
            AnimPlaybackToken completedToken =
                CreateToken(11UL, 1UL);
            SetCandidateFeedbackStamp(animationOperator, 1UL);
            SetActiveLayer(animationOperator, completedToken);
            int oneShotHash =
                Animator.StringToHash("Base Layer.OneShot");
            playable.Play(oneShotHash, 0, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            EvaluateAsCandidate(animationOperator, graph, 1.01f);
            Assert.That(
                playable.GetCurrentAnimatorStateInfo(0).fullPathHash,
                Is.EqualTo(oneShotHash));
            Assert.IsTrue(playable.IsInTransition(0));
            AnimFeedbackEvent[] stagedEvents =
                ReadField<AnimFeedbackEvent[]>(
                    feedback,
                    "_events");
            Assert.IsTrue(
                stagedEvents
                    .Take(feedback.Count)
                    .Any(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.StateMarker),
                "The real Controller SMB did not emit its 0.8 marker.");
            int feedbackBeforeCompletion = feedback.Count;
            InvokeUpdatePlaybackStates(animationOperator);

            AnimPlaybackLayer completed =
                ReadLayers(animationOperator)[0];
            Assert.That(
                completed.Status,
                Is.EqualTo(AnimPlaybackStatus.Completed));
            Assert.That(completed.Token, Is.EqualTo(completedToken));
            Assert.IsFalse(
                animationOperator.TryGetPlayback(
                    AnimPlaybackLayerSlot.Layer00,
                    out _),
                "Uncommitted local playback must not leak through the public query.");
            Assert.That(
                animationOperator.CurrentPlayback,
                Is.EqualTo(default(AnimPlaybackContext)));
            Assert.That(
                stagedEvents
                    .Take(feedback.Count)
                    .Count(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.PlaybackCompleted &&
                        staged.Record.PlaybackToken == completedToken),
                Is.EqualTo(1));
            Assert.That(
                stagedEvents
                    .Take(feedback.Count)
                    .Count(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.PlaybackInterrupted &&
                        staged.Record.PlaybackToken == completedToken),
                Is.Zero);
            Assert.That(
                feedback.Count,
                Is.EqualTo(feedbackBeforeCompletion + 1));
            int feedbackAfterCompletion = feedback.Count;
            InvokeUpdatePlaybackStates(animationOperator);
            Assert.That(
                feedback.Count,
                Is.EqualTo(feedbackAfterCompletion));

            feedback.Clear();
            AnimPlaybackToken plainCompletedToken =
                CreateToken(16UL, 6UL);
            SetCandidateFeedbackStamp(animationOperator, 6UL);
            SetActiveLayer(
                animationOperator,
                plainCompletedToken,
                PlainOneShotBindingValue);
            int plainOneShotHash =
                Animator.StringToHash("Base Layer.PlainOneShot");
            playable.Play(plainOneShotHash, 0, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0.51f);
            AnimatorStateInfo plainCurrent =
                playable.GetCurrentAnimatorStateInfo(0);
            AnimatorTransitionInfo plainTransition =
                playable.GetAnimatorTransitionInfo(0);
            InvokeUpdatePlaybackStates(animationOperator);

            AnimPlaybackLayer plainCompleted =
                ReadLayers(animationOperator)[0];
            Assert.That(
                plainCompleted.Status,
                Is.EqualTo(AnimPlaybackStatus.Completed),
                "Completion must not depend on AnimEventSmb. " +
                $"current={plainCurrent.normalizedTime}, " +
                $"length={plainCurrent.length}, " +
                $"speed={plainCurrent.speed}, " +
                $"multiplier={plainCurrent.speedMultiplier}, " +
                $"transition={plainTransition.normalizedTime}, " +
                $"duration={plainTransition.duration}, " +
                $"unit={plainTransition.durationUnit}.");
            Assert.That(
                plainCompleted.Token,
                Is.EqualTo(plainCompletedToken));
            Assert.That(
                stagedEvents
                    .Take(feedback.Count)
                    .Count(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.PlaybackCompleted &&
                        staged.Record.PlaybackToken ==
                        plainCompletedToken),
                Is.EqualTo(1));

            feedback.Clear();
            AnimPlaybackToken interruptedToken =
                CreateToken(12UL, 2UL);
            SetCandidateFeedbackStamp(animationOperator, 2UL);
            playable.Play(oneShotHash, 0, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0.2f);
            SetActiveLayer(animationOperator, interruptedToken);
            playable.Play(
                Animator.StringToHash("Base Layer.Idle"),
                0,
                0f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            int feedbackBeforeInterruption = feedback.Count;
            InvokeUpdatePlaybackStates(animationOperator);

            AnimPlaybackLayer interrupted =
                ReadLayers(animationOperator)[0];
            Assert.That(
                interrupted.Status,
                Is.EqualTo(AnimPlaybackStatus.Interrupted));
            Assert.That(interrupted.Token, Is.EqualTo(interruptedToken));
            Assert.That(
                feedback.Count,
                Is.EqualTo(feedbackBeforeInterruption + 1));

            feedback.Clear();
            AnimPlaybackToken earlyExitToken =
                CreateToken(13UL, 3UL);
            SetCandidateFeedbackStamp(animationOperator, 3UL);
            SetActiveLayer(
                animationOperator,
                earlyExitToken,
                EarlyExitBindingValue);
            int earlyExitHash =
                Animator.StringToHash("Base Layer.EarlyExit");
            playable.Play(earlyExitHash, 0, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            EvaluateAsCandidate(animationOperator, graph, 1.01f);
            Assert.That(
                playable.GetCurrentAnimatorStateInfo(0).fullPathHash,
                Is.EqualTo(earlyExitHash));
            Assert.IsTrue(playable.IsInTransition(0));
            InvokeUpdatePlaybackStates(animationOperator);
            AnimPlaybackLayer earlyExit =
                ReadLayers(animationOperator)[0];
            Assert.That(
                earlyExit.Status,
                Is.EqualTo(AnimPlaybackStatus.Interrupted),
                string.Join(
                    ", ",
                    stagedEvents
                        .Take(feedback.Count)
                        .Select(staged =>
                            staged.Record.Kind + "@" +
                            staged.Record.NormalizedTime)));
            Assert.That(earlyExit.Token, Is.EqualTo(earlyExitToken));
            Assert.That(
                stagedEvents
                    .Take(feedback.Count)
                    .Count(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.PlaybackInterrupted &&
                        staged.Record.PlaybackToken == earlyExitToken),
                Is.EqualTo(1));
            Assert.That(
                stagedEvents
                    .Take(feedback.Count)
                    .Count(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.PlaybackCompleted &&
                        staged.Record.PlaybackToken == earlyExitToken),
                Is.Zero);

            feedback.Clear();
            AnimPlaybackToken oldSameStateToken =
                CreateToken(14UL, 4UL);
            AnimPlaybackToken replayedSameStateToken =
                CreateToken(15UL, 5UL);
            SetCandidateFeedbackStamp(animationOperator, 4UL);
            SetActiveLayer(
                animationOperator,
                oldSameStateToken,
                OneShotBindingValue);
            playable.Play(oneShotHash, 0, 0.8f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            SetActiveLayer(
                animationOperator,
                replayedSameStateToken,
                OneShotBindingValue);
            playable.Play(oneShotHash, 0, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0.3f);
            InvokeUpdatePlaybackStates(animationOperator);

            AnimPlaybackLayer replayedSameState =
                ReadLayers(animationOperator)[0];
            Assert.That(
                replayedSameState.Token,
                Is.EqualTo(replayedSameStateToken));
            Assert.That(
                replayedSameState.Status,
                Is.EqualTo(AnimPlaybackStatus.Playing),
                "A replayed state must not inherit the replaced token's exit.");

            feedback.Clear();
            AnimPlaybackToken crossFadedSameStateToken =
                CreateToken(17UL, 7UL);
            SetCandidateFeedbackStamp(animationOperator, 7UL);
            playable.Play(oneShotHash, 0, 0.8f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            SetActiveLayer(
                animationOperator,
                crossFadedSameStateToken,
                OneShotBindingValue);
            playable.CrossFadeInFixedTime(
                oneShotHash,
                0.5f,
                0,
                0f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0.3f);
            InvokeUpdatePlaybackStates(animationOperator);

            AnimPlaybackLayer crossFadedSameState =
                ReadLayers(animationOperator)[0];
            Assert.That(
                crossFadedSameState.Token,
                Is.EqualTo(crossFadedSameStateToken));
            Assert.That(
                crossFadedSameState.Status,
                Is.EqualTo(AnimPlaybackStatus.CrossFading));
            EvaluateAsCandidate(animationOperator, graph, 0.3f);
            string sameStateFeedback = string.Join(
                ", ",
                stagedEvents
                    .Take(feedback.Count)
                    .Select(staged =>
                        staged.Record.Kind + ":" +
                        staged.Record.EventBindingId.Value + "@" +
                        staged.Record.NormalizedTime));
            Assert.That(
                stagedEvents
                    .Take(feedback.Count)
                    .Count(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.StateMarker &&
                        staged.Record.EventBindingId.Value ==
                        IncomingMarkerBindingValue),
                Is.EqualTo(1),
                "The incoming same-state cursor must emit its marker exactly once. " +
                sameStateFeedback);
            Assert.That(
                stagedEvents
                    .Take(feedback.Count)
                    .Count(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.StateMarker &&
                        staged.Record.EventBindingId.Value ==
                        OutgoingMarkerBindingValue),
                Is.EqualTo(1),
                "The outgoing same-state cursor must emit its marker exactly once. " +
                sameStateFeedback);
            Assert.That(
                stagedEvents
                    .Take(feedback.Count)
                    .Any(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.PlaybackCompleted &&
                        staged.Record.PlaybackToken ==
                        crossFadedSameStateToken),
                Is.False,
                "The old current instance must not complete the new next token.");

            PoisonFeedback(feedback, 31UL);
            Assert.That(feedback.Overflowed, Is.True);
            Assert.IsTrue(
                animationOperator.TryRebuildBindings(
                    out diagnostic),
                diagnostic.Message);
            Assert.That(feedback.Count, Is.Zero);
            Assert.That(feedback.Overflowed, Is.False);

            var autoObject = new GameObject("Pre11 Auto Recovery Fixture");
            _objects.Add(autoObject);
            autoObject.SetActive(false);
            Animator autoAnimator = autoObject.AddComponent<Animator>();
            CoCoStateGraphHost autoHost =
                autoObject.AddComponent<CoCoStateGraphHost>();
            AnimAutoOperator autoOperator =
                autoObject.AddComponent<AnimAutoOperator>();
            SetField(autoOperator, "animator", autoAnimator);
            SetField(autoOperator, "stateGraphHost", autoHost);
            autoObject.SetActive(true);
            Assert.IsTrue(
                autoOperator.TryRebuildBindings(
                    out diagnostic),
                diagnostic.Message);
            AnimFeedbackBuffer autoFeedback =
                ReadField<AnimFeedbackBuffer>(
                    autoOperator,
                    "_feedback");
            PoisonFeedback(autoFeedback, 32UL);
            Assert.That(autoFeedback.Overflowed, Is.True);
            Assert.IsTrue(
                autoOperator.TryRebuildBindings(
                    out diagnostic),
                diagnostic.Message);
            Assert.That(autoFeedback.Count, Is.Zero);
            Assert.That(autoFeedback.Overflowed, Is.False);
        }

        private AnimatorController CreateControllerFixture(
            out AnimationClip rootMotionClip)
        {
            var controller = new AnimatorController();
            controller.name = "Pre11 Animator Controller Fixture";
            _objects.Add(controller);

            var stateMachine = new AnimatorStateMachine();
            stateMachine.name = "Base Layer";
            _objects.Add(stateMachine);
            controller.layers =
                new[]
                {
                    new AnimatorControllerLayer
                    {
                        name = "Base Layer",
                        defaultWeight = 1f,
                        stateMachine = stateMachine
                    }
                };
            controller.AddParameter(
                "Speed",
                AnimatorControllerParameterType.Float);
            controller.AddParameter(
                "PlayOneShot",
                AnimatorControllerParameterType.Trigger);

            AnimationClip idleClip = CreateClip("Loop", true, false);
            AnimationClip oneShotClip =
                CreateClip("OneShot", false, false);
            AnimationClip earlyExitClip =
                CreateClip("EarlyExit", false, false);
            AnimationClip plainOneShotClip =
                CreateClip("PlainOneShot", false, false);
            AnimationClip offsetTargetClip =
                CreateClip("OffsetTarget", false, false, 2f);
            rootMotionClip = CreateClip("RootMotion", false, true);
            AnimatorState idle = stateMachine.AddState("Idle");
            idle.motion = idleClip;
            AnimatorState oneShot = stateMachine.AddState("OneShot");
            oneShot.motion = oneShotClip;
            AnimatorState earlyExit = stateMachine.AddState("EarlyExit");
            earlyExit.motion = earlyExitClip;
            AnimatorState plainOneShot =
                stateMachine.AddState("PlainOneShot");
            plainOneShot.motion = plainOneShotClip;
            plainOneShot.speed = 2f;
            AnimatorState offsetTarget =
                stateMachine.AddState("OffsetTarget");
            offsetTarget.motion = offsetTargetClip;
            AnimatorState rootMotion = stateMachine.AddState("RootMotion");
            rootMotion.motion = rootMotionClip;
            stateMachine.defaultState = idle;

            AnimatorStateTransition enterOneShot =
                idle.AddTransition(oneShot);
            enterOneShot.hasExitTime = false;
            enterOneShot.duration = 0f;
            enterOneShot.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "PlayOneShot");
            AnimatorStateTransition exitOneShot =
                oneShot.AddTransition(idle);
            exitOneShot.hasExitTime = true;
            exitOneShot.exitTime = 1f;
            exitOneShot.hasFixedDuration = true;
            exitOneShot.duration = 0.5f;

            AnimEventSmb smb =
                oneShot.AddStateMachineBehaviour<AnimEventSmb>();
            SetField(
                smb,
                "eventConfigs",
                new[]
                {
                    CreateEventConfig(IncomingMarkerBindingValue, 0.2f),
                    CreateEventConfig(602UL, 0.8f),
                    CreateEventConfig(OutgoingMarkerBindingValue, 0.9f)
                });
            AnimatorStateTransition exitEarly =
                earlyExit.AddTransition(idle);
            exitEarly.hasExitTime = true;
            exitEarly.exitTime = 0.8f;
            exitEarly.hasFixedDuration = true;
            exitEarly.duration = 0.5f;
            earlyExit.AddStateMachineBehaviour<AnimEventSmb>();
            AnimatorStateTransition exitPlainOneShot =
                plainOneShot.AddTransition(idle);
            exitPlainOneShot.hasExitTime = true;
            exitPlainOneShot.exitTime = 1f;
            exitPlainOneShot.hasFixedDuration = true;
            exitPlainOneShot.duration = 0.5f;
            return controller;
        }

        private AnimationClip CreateClip(
            string name,
            bool loop,
            bool rootMotion,
            float durationSeconds = 1f)
        {
            var clip = new AnimationClip
            {
                name = name,
                frameRate = 60f
            };
            clip.SetCurve(
                string.Empty,
                typeof(Transform),
                rootMotion ? "m_LocalPosition.x" : "m_LocalScale.x",
                AnimationCurve.Linear(0f, 0f, durationSeconds, 1f));
            AnimationClipSettings settings =
                AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            _objects.Add(clip);
            return clip;
        }

        private static void AssertFixtureSurface(
            AnimatorController controller,
            AnimationClip rootMotionClip)
        {
            Assert.That(
                controller.parameters.Select(parameter => parameter.name),
                Does.Contain("Speed"));
            Assert.That(
                controller.parameters.Select(parameter => parameter.name),
                Does.Contain("PlayOneShot"));
            AnimatorStateMachine stateMachine =
                controller.layers[0].stateMachine;
            AnimatorState oneShot = stateMachine.states
                .Select(child => child.state)
                .Single(state => state.name == "OneShot");
            Assert.That(
                oneShot.behaviours.OfType<AnimEventSmb>().Count(),
                Is.EqualTo(1));
            Assert.That(
                AnimationUtility.GetCurveBindings(rootMotionClip)
                    .Select(binding => binding.propertyName),
                Does.Contain("m_LocalPosition.x"));
            Assert.IsTrue(
                oneShot.transitions.Any(transition =>
                    transition.hasExitTime &&
                    transition.exitTime == 1f));
            AnimatorState earlyExit = stateMachine.states
                .Select(child => child.state)
                .Single(state => state.name == "EarlyExit");
            Assert.IsTrue(
                earlyExit.transitions.Any(transition =>
                    transition.hasExitTime &&
                    transition.exitTime == 0.8f));
            AnimatorState plainOneShot = stateMachine.states
                .Select(child => child.state)
                .Single(state => state.name == "PlainOneShot");
            Assert.That(plainOneShot.behaviours, Is.Empty);
            Assert.IsTrue(
                plainOneShot.transitions.Any(transition =>
                    transition.hasExitTime &&
                    transition.exitTime == 1f));
        }

        private static void ConfigureOperator(
            AnimOperator animationOperator,
            Animator animator,
            RuntimeAnimatorController controller,
            CoCoStateGraphHost host)
        {
            SetField(animationOperator, "animator", animator);
            SetField(animationOperator, "controller", controller);
            SetField(animationOperator, "stateGraphHost", host);
            SetField(
                animationOperator,
                "playbackLayers",
                new[] { CreateLayerBinding(0) });
            SetField(
                animationOperator,
                "stateBindings",
                new[]
                {
                    CreateStateBinding(
                        OneShotBindingValue,
                        0,
                        "Base Layer.OneShot"),
                    CreateStateBinding(
                        EarlyExitBindingValue,
                        0,
                        "Base Layer.EarlyExit"),
                    CreateStateBinding(
                        PlainOneShotBindingValue,
                        0,
                        "Base Layer.PlainOneShot"),
                    CreateStateBinding(
                        OffsetTargetBindingValue,
                        0,
                        "Base Layer.OffsetTarget")
                });
            SetField(
                animationOperator,
                "modulationBindings",
                new[]
                {
                    CreateModulationBinding(
                        ModulationBindingValue,
                        "Speed")
                });
            SetField(animationOperator, "enableRootMotionRelay", true);
            SetField(animationOperator, "relayPosition", true);
            SetField(animationOperator, "relayRotation", true);
        }

        private static AnimPlaybackLayerBinding CreateLayerBinding(
            int controllerLayer)
        {
            object boxed = new AnimPlaybackLayerBinding();
            SetField(boxed, "controllerLayer", controllerLayer);
            return (AnimPlaybackLayerBinding)boxed;
        }

        private static AnimStateBinding CreateStateBinding(
            ulong bindingId,
            int controllerLayer,
            string fullPath)
        {
            object boxed = new AnimStateBinding();
            SetField(boxed, "bindingId", bindingId);
            SetField(boxed, "controllerLayer", controllerLayer);
            SetField(boxed, "fullPath", fullPath);
            return (AnimStateBinding)boxed;
        }

        private static AnimModulationBinding CreateModulationBinding(
            ulong bindingId,
            string parameterName)
        {
            object boxed = new AnimModulationBinding();
            SetField(boxed, "bindingId", bindingId);
            SetField(
                boxed,
                "modulationKind",
                AnimModulationKind.FloatParameter);
            SetField(boxed, "parameterName", parameterName);
            return (AnimModulationBinding)boxed;
        }

        private static AnimEventConfig CreateEventConfig(
            ulong bindingId,
            float triggerTime)
        {
            var config = new AnimEventConfig();
            SetField(config, "bindingId", bindingId);
            SetField(config, "eventName", "FixtureMarker");
            SetField(config, "triggerTime", triggerTime);
            return config;
        }

        private static AnimPlaybackToken CreateToken(
            ulong activationValue,
            ulong operationSequenceValue)
        {
            Assert.IsTrue(
                CoCoActivationId.TryCreate(
                    activationValue,
                    out CoCoActivationId activationId));
            Assert.IsTrue(
                CoCoOperationSequence.TryCreate(
                    operationSequenceValue,
                    out CoCoOperationSequence operationSequence));
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    1UL,
                    out CoCoGraphInstanceId graphInstanceId));
            Assert.IsTrue(
                AnimPlaybackToken.TryCreate(
                    graphInstanceId,
                    activationId,
                    new CoCoTimelineEpoch(0UL),
                    operationSequence,
                    AnimPlaybackLayerSlot.Layer00,
                    out AnimPlaybackToken token));
            return token;
        }

        private static void SetActiveLayer(
            AnimOperator animationOperator,
            AnimPlaybackToken token,
            ulong bindingValue = OneShotBindingValue)
        {
            Assert.IsTrue(
                AnimBindingId.TryCreate(
                    bindingValue,
                    out AnimBindingId bindingId));
            AnimPlaybackLayer[] layers = ReadLayers(animationOperator);
            layers[0] = new AnimPlaybackLayer(
                AnimPlaybackLayerSlot.Layer00,
                token,
                bindingId,
                AnimPlaybackStatus.Playing,
                0f);
        }

        private static void SetCandidateFeedbackStamp(
            AnimOperator animationOperator,
            ulong tickValue)
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    1UL,
                    out CoCoGraphInstanceId graphInstanceId));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graphInstanceId,
                    new CoCoTimelineEpoch(0UL),
                    new CoCoTimelineTick(tickValue),
                    new CoCoExecutionSequence(tickValue),
                    out AnimFeedbackSourceStamp source));
            SetField(
                animationOperator,
                "_candidateFeedbackStamp",
                source);
        }

        private static AnimPlaybackLayer[] ReadLayers(
            AnimOperator animationOperator)
        {
            return ReadField<AnimPlaybackLayer[]>(
                animationOperator,
                "_layerStates");
        }

        private static void InvokeUpdatePlaybackStates(
            AnimOperator animationOperator)
        {
            MethodInfo method = typeof(AnimOperator).GetMethod(
                "UpdatePlaybackStates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(animationOperator, null);
        }

        private static void EvaluateAsCandidate(
            AnimOperator animationOperator,
            PlayableGraph graph,
            float deltaSeconds)
        {
            SetField(animationOperator, "_isEvaluating", true);
            try
            {
                graph.Evaluate(deltaSeconds);
            }
            finally
            {
                SetField(animationOperator, "_isEvaluating", false);
            }
        }

        private static void PoisonFeedback(
            AnimFeedbackBuffer feedback,
            ulong graphValue)
        {
            Assert.IsTrue(
                CoCoGraphInstanceId.TryCreate(
                    graphValue,
                    out CoCoGraphInstanceId graph));
            Assert.IsTrue(
                AnimFeedbackSourceStamp.TryCreateCandidate(
                    graph,
                    new CoCoTimelineEpoch(0UL),
                    new CoCoTimelineTick(1UL),
                    new CoCoExecutionSequence(1UL),
                    out AnimFeedbackSourceStamp source));
            Assert.IsTrue(
                AnimFeedbackRecord.TryCreateRootMotion(
                    1f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1f,
                    out AnimFeedbackRecord record));
            for (int index = 0;
                 index < AnimContractLimits.FeedbackCapacity;
                 index++)
            {
                Assert.IsTrue(feedback.TryAppend(record, source));
            }

            Assert.IsFalse(feedback.TryAppend(record, source));
        }

        private static T ReadField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(target);
        }

        private static void SetField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private sealed class ModulationSection :
            IAnimModulationOperationSection
        {
            internal ModulationSection(AnimModulationCommand slot00)
            {
                Slot00 = slot00;
            }

            public AnimModulationCommand Slot00 { get; }
            public AnimModulationCommand Slot01 => default;
            public AnimModulationCommand Slot02 => default;
            public AnimModulationCommand Slot03 => default;
            public AnimModulationCommand Slot04 => default;
            public AnimModulationCommand Slot05 => default;
            public AnimModulationCommand Slot06 => default;
            public AnimModulationCommand Slot07 => default;
        }

        private sealed class PlaybackSection :
            IAnimPlaybackOperationSection
        {
            internal PlaybackSection(AnimPlaybackCommand layer00)
            {
                Layer00 = layer00;
            }

            public AnimPlaybackCommand Control => default;
            public AnimPlaybackCommand Layer00 { get; }
            public AnimPlaybackCommand Layer01 => default;
            public AnimPlaybackCommand Layer02 => default;
            public AnimPlaybackCommand Layer03 => default;
        }

        private sealed class RecordingModulationAdapter :
            IAnimModulationAdapter
        {
            internal int StopCount { get; private set; }
            internal AnimBindingId LastStoppedBinding { get; private set; }

            public bool TryStart(
                in AnimModulationCommand command,
                in AnimModulationTarget target,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public void Stop(in AnimModulationTarget target)
            {
                StopCount++;
                LastStoppedBinding = target.BindingId;
            }

            public bool TryManualUpdate(
                float positiveDeltaSeconds,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public void StopAll()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
#endif

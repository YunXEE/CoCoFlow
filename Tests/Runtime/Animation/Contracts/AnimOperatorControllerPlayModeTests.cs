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
            AnimPlaybackToken completedToken =
                CreateToken(11UL, 1UL);
            SetCandidateFeedbackStamp(animationOperator, 1UL);
            SetActiveLayer(animationOperator, completedToken);
            int oneShotHash =
                Animator.StringToHash("Base Layer.OneShot");
            playable.Play(oneShotHash, 0, 0f);
            EvaluateAsCandidate(animationOperator, graph, 0f);
            EvaluateAsCandidate(animationOperator, graph, 1.01f);
            int feedbackBeforeCompletion = feedback.Count;
            AnimFeedbackEvent[] stagedEvents =
                ReadField<AnimFeedbackEvent[]>(
                    feedback,
                    "_events");
            Assert.IsTrue(
                stagedEvents
                    .Take(feedbackBeforeCompletion)
                    .Any(staged =>
                        staged.Record.Kind ==
                        AnimFeedbackKind.StateMarker),
                "The real Controller SMB did not emit its 0.8 marker.");
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
                feedback.Count,
                Is.EqualTo(feedbackBeforeCompletion + 1));
            InvokeUpdatePlaybackStates(animationOperator);
            Assert.That(
                feedback.Count,
                Is.EqualTo(feedbackBeforeCompletion + 1));

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
            rootMotionClip = CreateClip("RootMotion", false, true);
            AnimatorState idle = stateMachine.AddState("Idle");
            idle.motion = idleClip;
            AnimatorState oneShot = stateMachine.AddState("OneShot");
            oneShot.motion = oneShotClip;
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
                new[] { CreateEventConfig(602UL, 0.8f) });
            return controller;
        }

        private AnimationClip CreateClip(
            string name,
            bool loop,
            bool rootMotion)
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
                AnimationCurve.Linear(0f, 0f, 1f, 1f));
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
                        "Base Layer.OneShot")
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
                AnimPlaybackToken.TryCreate(
                    activationId,
                    new CoCoTimelineEpoch(0UL),
                    operationSequence,
                    AnimPlaybackLayerSlot.Layer00,
                    out AnimPlaybackToken token));
            return token;
        }

        private static void SetActiveLayer(
            AnimOperator animationOperator,
            AnimPlaybackToken token)
        {
            Assert.IsTrue(
                AnimBindingId.TryCreate(
                    OneShotBindingValue,
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
    }
}
#endif

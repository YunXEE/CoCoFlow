using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Modules.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    /// <summary>
    /// C7 commit 1: Animator snapshot sample/project round trip on a real
    /// runtime-built controller, plus the two loud-failure layout guards
    /// (unknown state hash, layer overflow). No silent partial restore.
    /// </summary>
    public sealed class AnimSnapshotProjectionPlayModeTests
    {
        private const string ControllerPath =
            "Assets/AnimSnapshotProjectionTest.controller";

        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _objects)
            {
                if (obj != null)
                {
                    Object.Destroy(obj);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }
        }

        [Test]
        public void SnapshotRoundTripsThroughAnimator()
        {
            Animator animator = CreateAnimatorWithTwoStatesAndSpeed();
            animator.Play(Animator.StringToHash("Jump"), 0, 0.5f);
            animator.Update(0f);
            animator.SetFloat("Speed", 3.25f);

            AnimSnapshotState snapshot = AnimSnapshot.Sample(
                animator,
                new[] { SpeedBinding() });
            Assert.AreEqual(1, snapshot.LayerCount);
            Assert.AreEqual(1, snapshot.LaneCount);
            Assert.AreEqual(Animator.StringToHash("Jump"), snapshot.LayerStateHash(0));
            Assert.GreaterOrEqual(snapshot.LayerTime(0), 0.49f);
            Assert.LessOrEqual(snapshot.LayerTime(0), 0.55f);
            Assert.AreEqual(3.25f, snapshot.Lane(0), 0.0001f);

            // World drifts to another state and parameter.
            animator.Play(Animator.StringToHash("Idle"), 0, 0f);
            animator.SetFloat("Speed", 0f);
            animator.Update(0f);

            Assert.IsTrue(AnimSnapshot.TryProject(
                animator,
                new[] { SpeedBinding() },
                snapshot,
                out CoCoDiagnostic diagnostic), diagnostic.Message);

            AnimatorStateInfo restored = animator.GetCurrentAnimatorStateInfo(0);
            Assert.AreEqual(Animator.StringToHash("Jump"), restored.shortNameHash);
            Assert.GreaterOrEqual(restored.normalizedTime, 0.49f);
            Assert.LessOrEqual(restored.normalizedTime, 0.55f);
            Assert.AreEqual(3.25f, animator.GetFloat("Speed"), 0.0001f);
        }

        [Test]
        public void ProjectionFailsLoudlyOnUnknownStateHash()
        {
            Animator animator = CreateAnimatorWithTwoStatesAndSpeed();
            AnimSnapshotState snapshot = default;
            snapshot.LayerCount = 1;
            snapshot.SetLayer(0, Animator.StringToHash("NoSuchState"), 0.5f, 1f);

            Assert.IsFalse(AnimSnapshot.TryProject(
                animator,
                new[] { SpeedBinding() },
                snapshot,
                out CoCoDiagnostic diagnostic));
            Assert.IsTrue(diagnostic.IsError);
            StringAssert.Contains("state hash", diagnostic.Message);
        }

        [Test]
        public void ProjectionFailsLoudlyOnLayerOverflow()
        {
            Animator animator = CreateAnimatorWithTwoStatesAndSpeed();
            AnimSnapshotState snapshot = default;
            snapshot.LayerCount = AnimSnapshotState.MaxLayers + 1;

            Assert.IsFalse(AnimSnapshot.TryProject(
                animator,
                System.Array.Empty<AnimParameterBinding>(),
                snapshot,
                out CoCoDiagnostic diagnostic));
            Assert.IsTrue(diagnostic.IsError);
            StringAssert.Contains("layer", diagnostic.Message);
        }

        private static AnimParameterBinding SpeedBinding()
        {
            // struct reflection writes through the boxed copy — keep it.
            object boxed = new AnimParameterBinding();
            typeof(AnimParameterBinding)
                .GetField("parameterName",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(boxed, "Speed");
            typeof(AnimParameterBinding)
                .GetField("parameterKind",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(boxed, AnimParameterValueKind.Float);
            return (AnimParameterBinding)boxed;
        }

        private Animator CreateAnimatorWithTwoStatesAndSpeed()
        {
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorControllerLayer layer = controller.layers[0];
            layer.stateMachine.AddState("Idle");
            layer.stateMachine.AddState("Jump");
            controller.AddParameter(
                "Speed",
                AnimatorControllerParameterType.Float);

            var gameObject = new GameObject("AnimSnapshotProjection");
            _objects.Add(gameObject);
            Animator animator = gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            return animator;
        }
    }
}

#if UNITY_EDITOR
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Modules.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
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
            Animator animator = CreateAnimatorWithTypedParameters();
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
            Animator animator = CreateAnimatorWithTypedParameters();
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
            Animator animator = CreateAnimatorWithTypedParameters();
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

        // BUG-032: Integer round-trips through the raw 32-bit lane payload.
        // int.MinValue is the -0f bit pattern, ±16777217 sit beyond float's
        // exact integer range (any numeric cast collapses them), and
        // int.MaxValue is NaN-shaped — none may normalize.
        [Test]
        public void SnapshotRoundTripsIntegerThroughRawBits()
        {
            Animator animator = CreateAnimatorWithTypedParameters();
            animator.Play(Animator.StringToHash("Jump"), 0, 0.5f);
            animator.Update(0f);
            int[] probes = { int.MinValue, -16777217, 16777217, int.MaxValue };
            foreach (int probe in probes)
            {
                animator.SetInteger("Gear", probe);
                AnimSnapshotState snapshot = AnimSnapshot.Sample(
                    animator,
                    new[] { TypedBinding("Gear", AnimParameterValueKind.Integer) });
                Assert.AreEqual(
                    probe,
                    System.BitConverter.SingleToInt32Bits(snapshot.Lane(0)),
                    "the lane must carry the exact raw bits of " + probe);

                animator.SetInteger("Gear", 7);
                Assert.IsTrue(AnimSnapshot.TryProject(
                    animator,
                    new[] { TypedBinding("Gear", AnimParameterValueKind.Integer) },
                    snapshot,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                Assert.AreEqual(probe, animator.GetInteger("Gear"));
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SnapshotRoundTripsBooleanThroughFlagLanes()
        {
            Animator animator = CreateAnimatorWithTypedParameters();
            animator.Play(Animator.StringToHash("Jump"), 0, 0.5f);
            animator.Update(0f);
            foreach (bool probe in new[] { false, true })
            {
                animator.SetBool("Boost", probe);
                AnimSnapshotState snapshot = AnimSnapshot.Sample(
                    animator,
                    new[] { TypedBinding("Boost", AnimParameterValueKind.Boolean) });
                Assert.AreEqual(probe ? 1f : 0f, snapshot.Lane(0));

                animator.SetBool("Boost", !probe);
                Assert.IsTrue(AnimSnapshot.TryProject(
                    animator,
                    new[] { TypedBinding("Boost", AnimParameterValueKind.Boolean) },
                    snapshot,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
                Assert.AreEqual(probe, animator.GetBool("Boost"));
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SampleThrowsOnInvalidParameterKind()
        {
            Animator animator = CreateAnimatorWithTypedParameters();
            animator.Play(Animator.StringToHash("Jump"), 0, 0.5f);
            animator.Update(0f);

            Assert.Throws<System.ArgumentException>(() => AnimSnapshot.Sample(
                animator,
                new[] { TypedBinding("Gear", (AnimParameterValueKind)99) }));
        }

        // BUG-032: an invalid kind anywhere in the projected layout must
        // fail before any Animator write — the valid Speed lane sits
        // before the invalid one so a write-then-fail would mutate Speed.
        [Test]
        public void ProjectionFailsBeforeAnyWriteOnInvalidParameterKind()
        {
            Animator animator = CreateAnimatorWithTypedParameters();
            animator.Play(Animator.StringToHash("Jump"), 0, 0.5f);
            animator.Update(0f);
            animator.SetFloat("Speed", 3.25f);
            animator.SetInteger("Gear", 12);

            AnimSnapshotState snapshot = default;
            snapshot.LayerCount = 1;
            snapshot.SetLayer(0, Animator.StringToHash("Jump"), 0.5f, 1f);
            snapshot.LaneCount = 2;
            snapshot.SetLane(0, 3.25f);
            snapshot.SetLane(1, 3.25f);

            animator.Play(Animator.StringToHash("Idle"), 0, 0f);
            animator.SetFloat("Speed", 0f);
            animator.SetInteger("Gear", 34);
            animator.Update(0f);

            Assert.IsFalse(AnimSnapshot.TryProject(
                animator,
                new[]
                {
                    TypedBinding("Speed", AnimParameterValueKind.Float),
                    TypedBinding("Gear", (AnimParameterValueKind)77),
                },
                snapshot,
                out CoCoDiagnostic diagnostic));
            Assert.IsTrue(diagnostic.IsError);
            StringAssert.Contains("parameter kind", diagnostic.Message);

            Assert.AreEqual(0f, animator.GetFloat("Speed"),
                "no parameter lane may be written before the layout check");
            Assert.AreEqual(34, animator.GetInteger("Gear"));
            AnimatorStateInfo drifted = animator.GetCurrentAnimatorStateInfo(0);
            Assert.AreEqual(Animator.StringToHash("Idle"), drifted.shortNameHash,
                "no layer may be played before the layout check");
            Assert.LessOrEqual(drifted.normalizedTime, 0.01f);
        }

        private static AnimParameterBinding SpeedBinding()
        {
            return TypedBinding("Speed", AnimParameterValueKind.Float);
        }

        private static AnimParameterBinding TypedBinding(
            string parameterName,
            AnimParameterValueKind kind)
        {
            // struct reflection writes through the boxed copy — keep it.
            object boxed = new AnimParameterBinding();
            typeof(AnimParameterBinding)
                .GetField("parameterName",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(boxed, parameterName);
            typeof(AnimParameterBinding)
                .GetField("parameterKind",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(boxed, kind);
            return (AnimParameterBinding)boxed;
        }

        private Animator CreateAnimatorWithTypedParameters()
        {
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorControllerLayer layer = controller.layers[0];
            layer.stateMachine.AddState("Idle");
            layer.stateMachine.AddState("Jump");
            controller.AddParameter(
                "Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter(
                "Gear", AnimatorControllerParameterType.Int);
            controller.AddParameter(
                "Boost", AnimatorControllerParameterType.Bool);

            var gameObject = new GameObject("AnimSnapshotProjection");
            _objects.Add(gameObject);
            Animator animator = gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            return animator;
        }
    }
}
#endif

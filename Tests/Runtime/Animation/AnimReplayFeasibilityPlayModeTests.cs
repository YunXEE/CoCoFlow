using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CoCoFlow.Tests.Runtime.Animation
{
    /// <summary>
    /// Records the Pre11 exact-replay gate independently from the future
    /// Animation module implementation. These tests must not be weakened into
    /// approximate restore assertions.
    /// </summary>
    public sealed class AnimReplayFeasibilityPlayModeTests
    {
        [Test]
        public void G1_PositiveReplayPrimitivesDoNotPromoteDeferredGate()
        {
            MethodInfo[] controllerMethods =
                PublicInstanceMethods(typeof(AnimatorControllerPlayable));

            Assert.That(
                controllerMethods.Any(method => method.Name == "Play"),
                Is.True);
            Assert.That(
                controllerMethods.Any(method => method.Name == "CrossFade"),
                Is.True);
            Assert.That(
                controllerMethods.Any(method => method.Name == "SetTrigger"),
                Is.True);
            Assert.That(
                typeof(PlayableGraph).GetMethod(
                    nameof(PlayableGraph.Evaluate),
                    new[] { typeof(float) }),
                Is.Not.Null);

            Assert.That(
                CanShipExactReplay(
                    ReplayGateStatus.Unverified,
                    ReplayGateStatus.Go,
                    ReplayGateStatus.Go),
                Is.False,
                "G1 remains UNVERIFIED. API presence must not promote the " +
                "frozen exact-replay gate to GO.");
        }

        [Test]
        public void G2_PublicSurfaceCannotRestoreABoundedControllerAnchor()
        {
            string[] methodNames =
                PublicInstanceMethods(typeof(AnimatorControllerPlayable))
                    .Select(method => method.Name)
                    .Distinct()
                    .ToArray();

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "GetCurrentAnimatorStateInfo",
                    "GetNextAnimatorStateInfo",
                    "GetAnimatorTransitionInfo",
                    "IsInTransition"
                },
                methodNames,
                "The public surface must expose the hidden-state readers " +
                "whose missing inverse contract is under test.");

            CollectionAssert.DoesNotContain(
                methodNames,
                "SetCurrentAnimatorStateInfo");
            CollectionAssert.DoesNotContain(
                methodNames,
                "SetNextAnimatorStateInfo");
            CollectionAssert.DoesNotContain(
                methodNames,
                "SetAnimatorTransitionInfo");

            Assert.That(
                methodNames.Any(NamesControllerSnapshotOperation),
                Is.False,
                "A new public snapshot/restore surface would reopen G2.");

            Assert.Pass(
                "G2 NO-GO at the frozen Pre11 scope: public ACP APIs can " +
                "observe current/next/transition state but cannot restore " +
                "that composite hidden state as a periodic anchor. Replaying " +
                "from controller creation remains possible in principle, " +
                "but its journal grows with session lifetime and therefore " +
                "does not satisfy the bounded-anchor gate.");
        }

        [Test]
        public void G3_TargetlessCandidatePrimitivesDoNotPromoteDeferredGate()
        {
            MethodInfo setTarget = typeof(AnimationPlayableOutput).GetMethod(
                nameof(AnimationPlayableOutput.SetTarget));
            MethodInfo evaluate = typeof(PlayableGraph).GetMethod(
                nameof(PlayableGraph.Evaluate),
                new[] { typeof(float) });

            Assert.That(setTarget, Is.Not.Null);
            Assert.That(evaluate, Is.Not.Null);

            Assert.That(
                CanShipExactReplay(
                    ReplayGateStatus.Go,
                    ReplayGateStatus.Go,
                    ReplayGateStatus.Unverified),
                Is.False,
                "G3 remains UNVERIFIED. SetTarget/Evaluate presence must " +
                "not promote the frozen exact-replay gate to GO.");
        }

        [TestCase(
            ReplayGateStatus.Go,
            ReplayGateStatus.Go,
            ReplayGateStatus.Go,
            true)]
        [TestCase(
            ReplayGateStatus.Unverified,
            ReplayGateStatus.Go,
            ReplayGateStatus.Go,
            false)]
        [TestCase(
            ReplayGateStatus.NoGo,
            ReplayGateStatus.Go,
            ReplayGateStatus.Go,
            false)]
        [TestCase(
            ReplayGateStatus.Go,
            ReplayGateStatus.NoGo,
            ReplayGateStatus.Go,
            false)]
        [TestCase(
            ReplayGateStatus.Go,
            ReplayGateStatus.Unverified,
            ReplayGateStatus.Go,
            false)]
        [TestCase(
            ReplayGateStatus.Go,
            ReplayGateStatus.Go,
            ReplayGateStatus.Unverified,
            false)]
        [TestCase(
            ReplayGateStatus.Go,
            ReplayGateStatus.Go,
            ReplayGateStatus.NoGo,
            false)]
        public void ExactReplayShippingGateRequiresEveryGateToBeGo(
            ReplayGateStatus g1,
            ReplayGateStatus g2,
            ReplayGateStatus g3,
            bool expected)
        {
            Assert.That(
                CanShipExactReplay(g1, g2, g3),
                Is.EqualTo(expected),
                "Exact replay is shippable only when every frozen gate is GO.");
        }

        [Test]
        public void ExactReplayGateSnapshotRemainsDeferred()
        {
            Assert.That(
                CanShipExactReplay(
                    ReplayGateStatus.Unverified,
                    ReplayGateStatus.NoGo,
                    ReplayGateStatus.Unverified),
                Is.False);
        }

        private static MethodInfo[] PublicInstanceMethods(Type type)
        {
            return type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
        }

        private static bool NamesControllerSnapshotOperation(string name)
        {
            return name.IndexOf(
                       "Capture",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf(
                       "Restore",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf(
                       "Snapshot",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf(
                       "Clone",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CanShipExactReplay(
            ReplayGateStatus g1,
            ReplayGateStatus g2,
            ReplayGateStatus g3)
        {
            return g1 == ReplayGateStatus.Go &&
                   g2 == ReplayGateStatus.Go &&
                   g3 == ReplayGateStatus.Go;
        }

        public enum ReplayGateStatus
        {
            Unverified,
            Go,
            NoGo
        }
    }
}

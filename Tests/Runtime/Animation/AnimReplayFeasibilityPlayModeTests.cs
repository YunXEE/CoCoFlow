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
        public void G1_PositiveReplayPrimitivesExistButExactReplayIsUnverified()
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

            Assert.Inconclusive(
                "G1 UNVERIFIED: Unity 6000.3.20f1 exposes the positive " +
                "journal-replay primitives, but the isolated live/candidate " +
                "tick comparison could not run because the batch Editor " +
                "never completed LicensingClient initialization. API " +
                "presence is not evidence of bit-exact replay.");
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
        public void G3_TargetlessCandidateAndZeroDeltaSwapAreUnverified()
        {
            MethodInfo setTarget = typeof(AnimationPlayableOutput).GetMethod(
                nameof(AnimationPlayableOutput.SetTarget));
            MethodInfo evaluate = typeof(PlayableGraph).GetMethod(
                nameof(PlayableGraph.Evaluate),
                new[] { typeof(float) });

            Assert.That(setTarget, Is.Not.Null);
            Assert.That(evaluate, Is.Not.Null);

            Assert.Inconclusive(
                "G3 UNVERIFIED: SetTarget and Evaluate(0) exist, but their " +
                "presence does not prove that a null-target controller " +
                "continues evolving, that binding it applies no stale " +
                "side effects, or that the following positive tick remains " +
                "identical. Those properties require an executed fixture.");
        }

        [Test]
        public void ExactReplayGateRemainsDeferredWhenAnyGateIsNotGo()
        {
            ReplayGateStatus g1 = ReplayGateStatus.Unverified;
            ReplayGateStatus g2 = ReplayGateStatus.NoGo;
            ReplayGateStatus g3 = ReplayGateStatus.Unverified;

            Assert.That(
                new[] { g1, g2, g3 },
                Has.None.EqualTo(ReplayGateStatus.Go));
            Assert.That(
                CanShipExactReplay(g1, g2, g3),
                Is.False,
                "Exact replay is shippable only when every frozen gate is GO.");
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

        private enum ReplayGateStatus
        {
            Unverified,
            Go,
            NoGo
        }
    }
}

using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.EnemySamples.Tests
{
    public class CCS_Enemy_StateBaseTests
    {
        #region Public API

        [Test]
        public void ReleaseNavigationClearsDriveFactsBeforeReleasingOwner()
        {
            var root = new GameObject("Enemy Sample Navigation Release Test");
            var stateRoot = new GameObject("CoCoStates");

            try
            {
                stateRoot.transform.SetParent(root.transform, false);
                var navigation = root.AddComponent<CharacterNavigation>();
                var controller = stateRoot.AddComponent<CoCoStateMachineController>();
                var state = stateRoot.AddComponent<NavigationReleaseProbeState>();
                state.Init(controller);

                navigation.Context.TryClaimControl("Probe", 20);
                navigation.Context.SetDestination(
                    new Vector3(4f, 0f, 2f),
                    5f,
                    0.2f,
                    CharacterNavigationMode.Chase);
                navigation.Context.SetDesiredVelocity(Vector3.forward * 5f);

                state.ReleaseForTest();

                Assert.IsFalse(navigation.Context.HasAnyControl);
                Assert.IsFalse(navigation.Context.HasDestination);
                Assert.IsFalse(navigation.Context.HasDesiredVelocity);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        #endregion

        #region Internal Logic

        private sealed class NavigationReleaseProbeState : CCS_Enemy_StateBase
        {
            protected override string NavigationOwner => "Probe";

            public void ReleaseForTest()
            {
                ReleaseNavigation();
            }
        }

        #endregion
    }
}

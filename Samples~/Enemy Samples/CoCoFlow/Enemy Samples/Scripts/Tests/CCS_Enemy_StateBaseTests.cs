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
                var provider = root.AddComponent<CharacterContextProvider>();
                var controller = stateRoot.AddComponent<CoCoStateController>();
                var state = stateRoot.AddComponent<NavigationReleaseProbeState>();
                controller.SetContextProvider(provider);
                state.Init(controller);

                provider.Context.Navigation.TryClaimControl("Probe", 20);
                provider.Context.Navigation.SetDestination(
                    new Vector3(4f, 0f, 2f),
                    5f,
                    0.2f,
                    CharacterNavigationMode.Chase);
                provider.Context.Navigation.SetDesiredVelocity(Vector3.forward * 5f);

                state.ReleaseForTest();

                Assert.IsFalse(provider.Context.Navigation.HasAnyControl);
                Assert.IsFalse(provider.Context.Navigation.HasDestination);
                Assert.IsFalse(provider.Context.Navigation.HasDesiredVelocity);
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

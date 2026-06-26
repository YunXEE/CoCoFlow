using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Camera;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
{
    public class CameraModuleTests
    {
        [SetUp]
        public void SetUp()
        {
            CoCoServices.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            CoCoServices.ClearAll();
        }

        [Test]
        public void DirectorActivatesHighestPriorityActiveRig()
        {
            var fixture = CreateDirectorFixture();
            var playerRig = CreateRig("Player", fixture.Director, 50);
            var spectateRig = CreateRig("Spectate", fixture.Director, 65);
            try
            {
                Assert.AreSame(spectateRig.Rig, fixture.Director.ActiveRig);
                Assert.AreSame(spectateRig.FreeCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual(0, GetPriority(playerRig.FreeCamera));
                Assert.AreEqual(65, GetPriority(spectateRig.FreeCamera));
            }
            finally
            {
                spectateRig.Destroy();
                playerRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void InactiveRigDoesNotParticipateUntilActivated()
        {
            var fixture = CreateDirectorFixture();
            var playerRig = CreateRig("Player", fixture.Director, 60);
            var remoteRig = CreateRig("Remote", fixture.Director, 90, active: false);
            try
            {
                Assert.AreSame(playerRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(60, GetPriority(playerRig.FreeCamera));
                Assert.AreEqual(0, GetPriority(remoteRig.FreeCamera));

                fixture.Director.SetRigActive(remoteRig.Rig, true);

                Assert.AreSame(remoteRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(0, GetPriority(playerRig.FreeCamera));
                Assert.AreEqual(90, GetPriority(remoteRig.FreeCamera));
            }
            finally
            {
                remoteRig.Destroy();
                playerRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void RigPriorityChangeReordersDirector()
        {
            var fixture = CreateDirectorFixture();
            var localRig = CreateRig("Local Player", fixture.Director, 70);
            var teammateRig = CreateRig("Teammate", fixture.Director, 65);
            try
            {
                Assert.AreSame(localRig.Rig, fixture.Director.ActiveRig);

                fixture.Director.SetRigPriority(localRig.Rig, 60);

                Assert.AreSame(teammateRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(0, GetPriority(localRig.FreeCamera));
                Assert.AreEqual(65, GetPriority(teammateRig.FreeCamera));

                fixture.Director.SetRigPriority(localRig.Rig, 80);

                Assert.AreSame(localRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(80, GetPriority(localRig.FreeCamera));
                Assert.AreEqual(0, GetPriority(teammateRig.FreeCamera));
            }
            finally
            {
                teammateRig.Destroy();
                localRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void StringModeIdSwitchesCurrentCameraWithoutTargetMutation()
        {
            var fixture = CreateDirectorFixture();
            var playerRig = CreateRig("Player", fixture.Director, 70);
            try
            {
                Assert.AreSame(playerRig.FreeCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual("Explore", playerRig.Rig.CurrentModeId);

                playerRig.Rig.SetMode("Aim");

                Assert.AreSame(playerRig.AimCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual("Aim", playerRig.Rig.CurrentModeId);
                Assert.AreSame(playerRig.FreeFollowTarget, playerRig.FreeCamera.Follow);
                Assert.AreSame(playerRig.FreeLookAtTarget, playerRig.FreeCamera.LookAt);
                Assert.AreSame(playerRig.AimFollowTarget, playerRig.AimCamera.Follow);
                Assert.AreSame(playerRig.AimLookAtTarget, playerRig.AimCamera.LookAt);

                playerRig.Rig.SetMode("Lock");

                Assert.AreSame(playerRig.LockCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual("Lock", playerRig.Rig.CurrentModeId);
                Assert.AreSame(playerRig.LockFollowTarget, playerRig.LockCamera.Follow);
                Assert.AreSame(playerRig.LockLookAtTarget, playerRig.LockCamera.LookAt);
            }
            finally
            {
                playerRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void DirectorUsesRigCurrentCameraAsWinnerOutput()
        {
            var fixture = CreateDirectorFixture();
            var playerRig = CreateRig("Player", fixture.Director, 70);
            try
            {
                playerRig.Rig.SetMode("Aim");

                Assert.AreSame(playerRig.Rig, fixture.Director.ActiveRig);
                Assert.AreSame(playerRig.AimCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual(0, GetPriority(playerRig.FreeCamera));
                Assert.AreEqual(70, GetPriority(playerRig.AimCamera));
            }
            finally
            {
                playerRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void SetCameraAddsAndUpdatesArbitraryModeId()
        {
            var fixture = CreateDirectorFixture();
            var playerRig = CreateRig("Player", fixture.Director, 70);
            var bossCamera = CreateCamera(
                playerRig.RootObject.transform,
                "Player Boss Combat Camera");
            var replacementCamera = CreateCamera(
                playerRig.RootObject.transform,
                "Player Replacement Boss Combat Camera");
            try
            {
                Assert.IsFalse(playerRig.Rig.TryGetCamera("BossCombat", out _));

                playerRig.Rig.SetCamera("BossCombat", bossCamera);

                Assert.IsTrue(playerRig.Rig.TryGetCamera(
                    "BossCombat",
                    out var configuredCamera));
                Assert.AreSame(bossCamera, configuredCamera);
                Assert.AreSame(bossCamera, playerRig.Rig.GetCamera("BossCombat"));

                playerRig.Rig.SetMode("BossCombat");

                Assert.AreSame(bossCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual(0, GetPriority(playerRig.FreeCamera));
                Assert.AreEqual(70, GetPriority(bossCamera));

                playerRig.Rig.SetCamera("BossCombat", replacementCamera);

                Assert.AreSame(replacementCamera, playerRig.Rig.GetCamera("BossCombat"));
                Assert.AreSame(replacementCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual(0, GetPriority(bossCamera));
                Assert.AreEqual(70, GetPriority(replacementCamera));
            }
            finally
            {
                playerRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void UnknownOrEmptyModeMakesRigUnavailableAndDirectorFallsBack()
        {
            var fixture = CreateDirectorFixture();
            var playerRig = CreateRig("Player", fixture.Director, 70);
            var fallbackRig = CreateRig("Fallback", fixture.Director, 60);
            try
            {
                Assert.AreSame(playerRig.Rig, fixture.Director.ActiveRig);

                playerRig.Rig.SetMode("explore");

                Assert.AreSame(fallbackRig.Rig, fixture.Director.ActiveRig);
                Assert.AreSame(fallbackRig.FreeCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual(0, GetPriority(playerRig.FreeCamera));
                Assert.AreEqual(60, GetPriority(fallbackRig.FreeCamera));

                playerRig.Rig.SetMode("Aim");

                Assert.AreSame(playerRig.Rig, fixture.Director.ActiveRig);
                Assert.AreSame(playerRig.AimCamera, fixture.Director.ActiveVirtualCamera);

                playerRig.Rig.SetMode(" ");

                Assert.AreEqual(string.Empty, playerRig.Rig.CurrentModeId);
                Assert.AreSame(fallbackRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(0, GetPriority(playerRig.AimCamera));
                Assert.AreEqual(60, GetPriority(fallbackRig.FreeCamera));

                fallbackRig.Rig.SetActive(false);

                Assert.IsNull(fixture.Director.ActiveRig);
                Assert.IsNull(fixture.Director.ActiveVirtualCamera);
            }
            finally
            {
                fallbackRig.Destroy();
                playerRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void CutsceneRigOverridesAndReturnsToPlayerMode()
        {
            var fixture = CreateDirectorFixture();
            var playerRig = CreateRig("Player", fixture.Director, 70);
            var cutsceneRig = CreateRig("Cutscene", fixture.Director, 100, active: false);
            try
            {
                playerRig.Rig.SetMode("Aim");
                Assert.AreSame(playerRig.AimCamera, fixture.Director.ActiveVirtualCamera);

                fixture.Director.SetRigActive(cutsceneRig.Rig, true);

                Assert.AreSame(cutsceneRig.Rig, fixture.Director.ActiveRig);
                Assert.AreSame(cutsceneRig.FreeCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual(0, GetPriority(playerRig.AimCamera));
                Assert.AreEqual(100, GetPriority(cutsceneRig.FreeCamera));

                fixture.Director.SetRigActive(cutsceneRig.Rig, false);

                Assert.AreSame(playerRig.Rig, fixture.Director.ActiveRig);
                Assert.AreSame(playerRig.AimCamera, fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual(70, GetPriority(playerRig.AimCamera));
                Assert.AreEqual(0, GetPriority(cutsceneRig.FreeCamera));
            }
            finally
            {
                cutsceneRig.Destroy();
                playerRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void SuspendedSchedulingClearsRigPrioritiesAndRestores()
        {
            var fixture = CreateDirectorFixture();
            var playerRig = CreateRig("Player", fixture.Director, 70);
            try
            {
                Assert.AreSame(playerRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(70, GetPriority(playerRig.FreeCamera));
                playerRig.AimCamera.Priority = 25;
                playerRig.LockCamera.Priority = 35;

                fixture.Director.SetSchedulingSuspended(true);

                Assert.IsTrue(fixture.Director.IsSchedulingSuspended);
                Assert.IsNull(fixture.Director.ActiveRig);
                Assert.IsNull(fixture.Director.ActiveVirtualCamera);
                Assert.AreEqual(0, GetPriority(playerRig.FreeCamera));
                Assert.AreEqual(0, GetPriority(playerRig.AimCamera));
                Assert.AreEqual(0, GetPriority(playerRig.LockCamera));

                fixture.Director.SetSchedulingSuspended(false);

                Assert.IsFalse(fixture.Director.IsSchedulingSuspended);
                Assert.AreSame(playerRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(70, GetPriority(playerRig.FreeCamera));
                Assert.AreEqual(0, GetPriority(playerRig.AimCamera));
                Assert.AreEqual(0, GetPriority(playerRig.LockCamera));
            }
            finally
            {
                playerRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void SamePriorityUsesMostRecentlyRegisteredRig()
        {
            var fixture = CreateDirectorFixture();
            var firstRig = CreateRig("First", fixture.Director, 50);
            var secondRig = CreateRig("Second", fixture.Director, 50);
            try
            {
                Assert.AreSame(secondRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(0, GetPriority(firstRig.FreeCamera));
                Assert.AreEqual(50, GetPriority(secondRig.FreeCamera));
            }
            finally
            {
                secondRig.Destroy();
                firstRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void UnregisterRigRemovesItFromDirectorSelection()
        {
            var fixture = CreateDirectorFixture();
            var firstRig = CreateRig("First", fixture.Director, 50);
            var secondRig = CreateRig("Second", fixture.Director, 60);
            try
            {
                Assert.AreSame(secondRig.Rig, fixture.Director.ActiveRig);

                secondRig.Rig.UnregisterRig();

                Assert.IsFalse(secondRig.Rig.IsRegistered);
                Assert.AreSame(firstRig.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(0, GetPriority(secondRig.FreeCamera));
                Assert.AreEqual(50, GetPriority(firstRig.FreeCamera));
            }
            finally
            {
                secondRig.Destroy();
                firstRig.Destroy();
                fixture.Destroy();
            }
        }

        [Test]
        public void CameraAimCouplerRotatesAimCoreFromLookInput()
        {
            var root = new GameObject("Aim Coupler Input Test");
            var aimCore = new GameObject("Aim Core");
            try
            {
                aimCore.transform.SetParent(root.transform);
                var input = root.AddComponent<TestInputStateProvider>();
                var coupler = aimCore.AddComponent<CameraAimCoupler>();
                coupler.SetInputStateProvider(input);
                input.LookInputValue = Vector2.right;

                SampleInput(coupler, 1f);

                AssertQuaternionApproximately(
                    Quaternion.Euler(0f, 180f, 0f),
                    aimCore.transform.localRotation);
            }
            finally
            {
                Object.DestroyImmediate(aimCore);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CameraAimCouplerDoesNotSyncTargetWhenDecoupled()
        {
            var root = new GameObject("Aim Coupler Decoupled Test");
            var aimCore = new GameObject("Aim Core");
            var syncTarget = new GameObject("Sync Target");
            try
            {
                aimCore.transform.SetParent(root.transform);
                syncTarget.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
                var expectedTargetRotation = syncTarget.transform.rotation;

                var input = root.AddComponent<TestInputStateProvider>();
                var coupler = aimCore.AddComponent<CameraAimCoupler>();
                coupler.SetInputStateProvider(input);
                coupler.SetSyncTarget(syncTarget.transform);
                coupler.SetCoupled(false);
                input.LookInputValue = Vector2.right;

                SampleInput(coupler, 1f);

                AssertQuaternionApproximately(
                    expectedTargetRotation,
                    syncTarget.transform.rotation);
            }
            finally
            {
                Object.DestroyImmediate(syncTarget);
                Object.DestroyImmediate(aimCore);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CameraAimCouplerWritesFullGlobalRotationWhenCoupled()
        {
            var root = new GameObject("Aim Coupler Coupled Test");
            var aimCore = new GameObject("Aim Core");
            var syncTarget = new GameObject("Sync Target");
            try
            {
                root.transform.rotation = Quaternion.Euler(0f, 25f, 10f);
                aimCore.transform.SetParent(root.transform);

                var coupler = aimCore.AddComponent<CameraAimCoupler>();
                coupler.SetSyncTarget(syncTarget.transform);
                coupler.SetCoupled(true);

                coupler.SetLookAngles(30f, 10f);

                AssertQuaternionApproximately(
                    aimCore.transform.rotation,
                    syncTarget.transform.rotation);
            }
            finally
            {
                Object.DestroyImmediate(syncTarget);
                Object.DestroyImmediate(aimCore);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CameraAimCouplerRebasesAncestorSyncWithoutDoubleApplyingYaw()
        {
            var root = new GameObject("Aim Coupler Ancestor Sync Test");
            var aimCore = new GameObject("Aim Core");
            try
            {
                aimCore.transform.SetParent(root.transform);

                var coupler = aimCore.AddComponent<CameraAimCoupler>();
                coupler.SetSyncTarget(root.transform);
                coupler.SetCoupled(true);

                coupler.SetLookAngles(45f, 10f);

                AssertQuaternionApproximately(
                    Quaternion.Euler(0f, 45f, 0f),
                    root.transform.rotation);
                AssertQuaternionApproximately(
                    Quaternion.Euler(10f, 45f, 0f),
                    aimCore.transform.rotation);
                AssertQuaternionApproximately(
                    Quaternion.Euler(10f, 0f, 0f),
                    aimCore.transform.localRotation);
            }
            finally
            {
                Object.DestroyImmediate(aimCore);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CameraAimCouplerDoesNotDriftWhenAncestorSyncHasNoInput()
        {
            var root = new GameObject("Aim Coupler Ancestor No Input Test");
            var aimCore = new GameObject("Aim Core");
            try
            {
                aimCore.transform.SetParent(root.transform);
                var input = root.AddComponent<TestInputStateProvider>();
                var coupler = aimCore.AddComponent<CameraAimCoupler>();
                coupler.SetInputStateProvider(input);
                coupler.SetSyncTarget(root.transform);
                coupler.SetCoupled(true);

                input.LookInputValue = Vector2.right;
                SampleInput(coupler, 0.25f);

                var expectedRootRotation = root.transform.rotation;
                var expectedAimWorldRotation = aimCore.transform.rotation;
                var expectedAimLocalRotation = aimCore.transform.localRotation;

                input.LookInputValue = Vector2.zero;
                SampleInput(coupler, 1f);

                AssertQuaternionApproximately(
                    expectedRootRotation,
                    root.transform.rotation);
                AssertQuaternionApproximately(
                    expectedAimWorldRotation,
                    aimCore.transform.rotation);
                AssertQuaternionApproximately(
                    expectedAimLocalRotation,
                    aimCore.transform.localRotation);
            }
            finally
            {
                Object.DestroyImmediate(aimCore);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CameraAimCouplerDoesNothingWithoutInputProvider()
        {
            var aimCore = new GameObject("Aim Core");
            var syncTarget = new GameObject("Sync Target");
            try
            {
                aimCore.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
                syncTarget.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
                var expectedAimRotation = aimCore.transform.localRotation;
                var expectedTargetRotation = syncTarget.transform.rotation;

                var coupler = aimCore.AddComponent<CameraAimCoupler>();
                coupler.SetSyncTarget(syncTarget.transform);
                coupler.SetCoupled(true);

                SampleInput(coupler, 1f);

                AssertQuaternionApproximately(
                    expectedAimRotation,
                    aimCore.transform.localRotation);
                AssertQuaternionApproximately(
                    expectedTargetRotation,
                    syncTarget.transform.rotation);
            }
            finally
            {
                Object.DestroyImmediate(syncTarget);
                Object.DestroyImmediate(aimCore);
            }
        }

        [Test]
        public void ActiveRigChangedReportsPreviousAndCurrentSelection()
        {
            var fixture = CreateDirectorFixture();
            var firstRig = CreateRig("First", fixture.Director, 50);
            CameraRigChangedEvent receivedEvent = default;
            bool received = false;
            fixture.Director.ActiveRigChanged += changeEvent =>
            {
                receivedEvent = changeEvent;
                received = true;
            };

            var secondRig = CreateRig("Second", fixture.Director, 60);
            try
            {
                Assert.IsTrue(received);
                Assert.AreSame(firstRig.Rig, receivedEvent.PreviousRig);
                Assert.AreSame(secondRig.Rig, receivedEvent.ActiveRig);
                Assert.AreSame(firstRig.FreeCamera, receivedEvent.PreviousVirtualCamera);
                Assert.AreSame(secondRig.FreeCamera, receivedEvent.ActiveVirtualCamera);
            }
            finally
            {
                secondRig.Destroy();
                firstRig.Destroy();
                fixture.Destroy();
            }
        }

        private static DirectorFixture CreateDirectorFixture()
        {
            var rootObject = new GameObject("Camera Director Test");
            var director = rootObject.AddComponent<CameraDirector>();
            return new DirectorFixture(rootObject, director);
        }

        private static CameraRigFixture CreateRig(
            string name,
            CameraDirector director,
            int priority,
            bool active = true)
        {
            var rootObject = new GameObject(name);
            var rig = rootObject.AddComponent<CameraRig>();

            var freeCamera = CreateCamera(rootObject.transform, $"{name} Free Camera");
            var aimCamera = CreateCamera(rootObject.transform, $"{name} Aim Camera");
            var lockCamera = CreateCamera(rootObject.transform, $"{name} Lock Camera");
            var spectateCamera = CreateCamera(rootObject.transform, $"{name} Spectate Camera");
            var focusCamera = CreateCamera(rootObject.transform, $"{name} Focus Camera");

            var freeFollowTarget = CreateChildTransform(rootObject.transform, $"{name} Free Follow");
            var freeLookAtTarget = CreateChildTransform(rootObject.transform, $"{name} Free LookAt");
            var aimFollowTarget = CreateChildTransform(rootObject.transform, $"{name} Aim Follow");
            var aimLookAtTarget = CreateChildTransform(rootObject.transform, $"{name} Aim LookAt");
            var lockFollowTarget = CreateChildTransform(rootObject.transform, $"{name} Lock Follow");
            var lockLookAtTarget = CreateChildTransform(rootObject.transform, $"{name} Lock LookAt");

            freeCamera.Follow = freeFollowTarget;
            freeCamera.LookAt = freeLookAtTarget;
            aimCamera.Follow = aimFollowTarget;
            aimCamera.LookAt = aimLookAtTarget;
            lockCamera.Follow = lockFollowTarget;
            lockCamera.LookAt = lockLookAtTarget;

            rig.SetCameraDirector(director);
            rig.SetCamera("Explore", freeCamera);
            rig.SetCamera("Aim", aimCamera);
            rig.SetCamera("Lock", lockCamera);
            rig.SetCamera("Spectate", spectateCamera);
            rig.SetCamera("Focus", focusCamera);
            rig.SetMode("Explore");
            rig.SetPriority(priority);
            rig.SetActive(active);
            rig.RegisterRig();

            return new CameraRigFixture(
                rootObject,
                rig,
                freeFollowTarget,
                freeLookAtTarget,
                aimFollowTarget,
                aimLookAtTarget,
                lockFollowTarget,
                lockLookAtTarget,
                freeCamera,
                aimCamera,
                lockCamera,
                spectateCamera,
                focusCamera);
        }

        private static Transform CreateChildTransform(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent);
            return child.transform;
        }

        private static CinemachineCamera CreateCamera(Transform parent, string name)
        {
            var cameraObject = new GameObject(name);
            cameraObject.transform.SetParent(parent);
            return cameraObject.AddComponent<CinemachineCamera>();
        }

        private static int GetPriority(CinemachineVirtualCameraBase camera)
        {
            return camera.Priority;
        }

        private static void SampleInput(CameraAimCoupler coupler, float deltaTime)
        {
            var method = typeof(CameraAimCoupler).GetMethod(
                "SampleInput",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(coupler, new object[] { deltaTime });
        }

        private static void AssertQuaternionApproximately(
            Quaternion expected,
            Quaternion actual)
        {
            Assert.LessOrEqual(Quaternion.Angle(expected, actual), 0.01f);
        }

        private sealed class DirectorFixture
        {
            public DirectorFixture(GameObject rootObject, CameraDirector director)
            {
                RootObject = rootObject;
                Director = director;
            }

            public GameObject RootObject { get; }
            public CameraDirector Director { get; }

            public void Destroy()
            {
                if (RootObject != null)
                {
                    Object.DestroyImmediate(RootObject);
                }
            }
        }

        private sealed class CameraRigFixture
        {
            public CameraRigFixture(
                GameObject rootObject,
                CameraRig rig,
                Transform freeFollowTarget,
                Transform freeLookAtTarget,
                Transform aimFollowTarget,
                Transform aimLookAtTarget,
                Transform lockFollowTarget,
                Transform lockLookAtTarget,
                CinemachineCamera freeCamera,
                CinemachineCamera aimCamera,
                CinemachineCamera lockCamera,
                CinemachineCamera spectateCamera,
                CinemachineCamera focusCamera)
            {
                RootObject = rootObject;
                Rig = rig;
                FreeFollowTarget = freeFollowTarget;
                FreeLookAtTarget = freeLookAtTarget;
                AimFollowTarget = aimFollowTarget;
                AimLookAtTarget = aimLookAtTarget;
                LockFollowTarget = lockFollowTarget;
                LockLookAtTarget = lockLookAtTarget;
                FreeCamera = freeCamera;
                AimCamera = aimCamera;
                LockCamera = lockCamera;
                SpectateCamera = spectateCamera;
                FocusCamera = focusCamera;
            }

            public GameObject RootObject { get; }
            public CameraRig Rig { get; }
            public Transform FreeFollowTarget { get; }
            public Transform FreeLookAtTarget { get; }
            public Transform AimFollowTarget { get; }
            public Transform AimLookAtTarget { get; }
            public Transform LockFollowTarget { get; }
            public Transform LockLookAtTarget { get; }
            public CinemachineCamera FreeCamera { get; }
            public CinemachineCamera AimCamera { get; }
            public CinemachineCamera LockCamera { get; }
            public CinemachineCamera SpectateCamera { get; }
            public CinemachineCamera FocusCamera { get; }

            public void Destroy()
            {
                if (RootObject != null)
                {
                    Object.DestroyImmediate(RootObject);
                }
            }
        }

        private sealed class TestInputStateProvider : MonoBehaviour, IInputStateProvider
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookInput => LookInputValue;
            public Vector2 ZoomInput => Vector2.zero;
            public Vector2 LookInputValue { get; set; }
        }
    }
}

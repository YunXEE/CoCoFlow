using CoCoFlow.Runtime.Modules.Camera;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
{
    public class CameraModuleTests
    {
        [Test]
        public void DirectorActivatesDefaultProfileForBoundLocalRig()
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Director.BindLocalRig(fixture.Rig);

                Assert.AreEqual(CameraProfileKeys.Default, fixture.Director.ActiveProfileId);
                Assert.AreSame(fixture.Rig, fixture.Director.ActiveRig);
                Assert.AreEqual(20, GetPriority(fixture.DefaultCamera));
                Assert.AreEqual(0, GetPriority(fixture.AimCamera));
                Assert.AreSame(fixture.FollowTarget, fixture.DefaultCamera.Follow);
                Assert.AreSame(fixture.LookAtTarget, fixture.DefaultCamera.LookAt);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void HigherPriorityRequestOverridesAndReleaseRestoresDefault()
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Director.BindLocalRig(fixture.Rig);

                int requestId = fixture.Director.Request(
                    CameraProfileKeys.Aim,
                    focusTarget: fixture.FocusTarget,
                    priority: 10);

                Assert.AreEqual(CameraProfileKeys.Aim, fixture.Director.ActiveProfileId);
                Assert.AreEqual(30, GetPriority(fixture.AimCamera));
                Assert.AreEqual(0, GetPriority(fixture.DefaultCamera));
                Assert.AreSame(fixture.FollowTarget, fixture.AimCamera.Follow);
                Assert.AreSame(fixture.FocusTarget, fixture.AimCamera.LookAt);

                Assert.IsTrue(fixture.Director.Release(requestId));
                Assert.AreEqual(CameraProfileKeys.Default, fixture.Director.ActiveProfileId);
                Assert.AreEqual(20, GetPriority(fixture.DefaultCamera));
                Assert.AreEqual(0, GetPriority(fixture.AimCamera));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void ReleaseOwnerClearsOnlyOwnedRequests()
        {
            var fixture = CreateFixture();
            var ownerA = new object();
            var ownerB = new object();
            try
            {
                fixture.Director.BindLocalRig(fixture.Rig);
                fixture.Director.Request(CameraProfileKeys.Aim, owner: ownerA, priority: 10);
                fixture.Director.Request(CameraProfileKeys.Focus, owner: ownerB, priority: 20);

                Assert.AreEqual(CameraProfileKeys.Focus, fixture.Director.ActiveProfileId);
                Assert.AreEqual(1, fixture.Director.ReleaseOwner(ownerB));
                Assert.AreEqual(CameraProfileKeys.Aim, fixture.Director.ActiveProfileId);
                Assert.AreEqual(1, fixture.Director.ReleaseOwner(ownerA));
                Assert.AreEqual(CameraProfileKeys.Default, fixture.Director.ActiveProfileId);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void SuspendedGameplayRequestsLeaveProfilesAtStandby()
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Director.BindLocalRig(fixture.Rig);
                fixture.Director.Request(CameraProfileKeys.Aim, priority: 10);

                fixture.Director.SetGameplayRequestsSuspended(true);

                Assert.AreEqual(string.Empty, fixture.Director.ActiveProfileId);
                Assert.IsNull(fixture.Director.ActiveRig);
                Assert.AreEqual(0, GetPriority(fixture.DefaultCamera));
                Assert.AreEqual(0, GetPriority(fixture.AimCamera));

                fixture.Director.SetGameplayRequestsSuspended(false);

                Assert.AreEqual(CameraProfileKeys.Aim, fixture.Director.ActiveProfileId);
                Assert.AreEqual(30, GetPriority(fixture.AimCamera));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void ClearingLocalRigFallsBackToDefaultRig()
        {
            var fixture = CreateFixture();
            var defaultRigObject = new GameObject("Default Rig");
            var defaultRig = defaultRigObject.AddComponent<CameraRig>();
            try
            {
                fixture.Director.SetDefaultRig(defaultRig);
                fixture.Director.BindLocalRig(fixture.Rig);

                Assert.AreSame(fixture.Rig, fixture.Director.ActiveRig);

                fixture.Director.ClearLocalRig(null);

                Assert.AreSame(defaultRig, fixture.Director.ActiveRig);
                Assert.AreSame(defaultRig.FollowTarget, fixture.DefaultCamera.Follow);
                Assert.AreSame(defaultRig.LookAtTarget, fixture.DefaultCamera.LookAt);
            }
            finally
            {
                Object.DestroyImmediate(defaultRigObject);
                fixture.Destroy();
            }
        }

        private static CameraFixture CreateFixture()
        {
            var directorObject = new GameObject("Camera Director");
            var rigObject = new GameObject("Camera Rig");
            var defaultCameraObject = new GameObject("Default Camera");
            var aimCameraObject = new GameObject("Aim Camera");
            var focusCameraObject = new GameObject("Focus Camera");
            var followTarget = new GameObject("Follow Target").transform;
            var lookAtTarget = new GameObject("Look At Target").transform;
            var focusTarget = new GameObject("Focus Target").transform;

            var rig = rigObject.AddComponent<CameraRig>();
            rig.SetTargets(rigObject.transform, followTarget, lookAtTarget);

            var defaultCamera = defaultCameraObject.AddComponent<CinemachineCamera>();
            var aimCamera = aimCameraObject.AddComponent<CinemachineCamera>();
            var focusCamera = focusCameraObject.AddComponent<CinemachineCamera>();

            var director = directorObject.AddComponent<CameraDirector>();
            director.SetProfileEntries(new[]
            {
                new CameraProfileEntry(
                    CameraProfileKeys.Default,
                    defaultCamera),
                new CameraProfileEntry(
                    CameraProfileKeys.Aim,
                    aimCamera,
                    activePriority: 30,
                    lookAtTarget: CameraTargetRole.RequestFocus),
                new CameraProfileEntry(
                    CameraProfileKeys.Focus,
                    focusCamera,
                    activePriority: 40,
                    lookAtTarget: CameraTargetRole.RequestFocus)
            });

            return new CameraFixture(
                directorObject,
                rigObject,
                defaultCameraObject,
                aimCameraObject,
                focusCameraObject,
                followTarget.gameObject,
                lookAtTarget.gameObject,
                focusTarget.gameObject,
                director,
                rig,
                defaultCamera,
                aimCamera,
                focusCamera,
                followTarget,
                lookAtTarget,
                focusTarget);
        }

        private static int GetPriority(CinemachineCamera camera)
        {
            return camera.Priority;
        }

        private sealed class CameraFixture
        {
            private readonly GameObject[] _objects;

            public CameraFixture(
                GameObject directorObject,
                GameObject rigObject,
                GameObject defaultCameraObject,
                GameObject aimCameraObject,
                GameObject focusCameraObject,
                GameObject followTargetObject,
                GameObject lookAtTargetObject,
                GameObject focusTargetObject,
                CameraDirector director,
                CameraRig rig,
                CinemachineCamera defaultCamera,
                CinemachineCamera aimCamera,
                CinemachineCamera focusCamera,
                Transform followTarget,
                Transform lookAtTarget,
                Transform focusTarget)
            {
                _objects = new[]
                {
                    directorObject,
                    rigObject,
                    defaultCameraObject,
                    aimCameraObject,
                    focusCameraObject,
                    followTargetObject,
                    lookAtTargetObject,
                    focusTargetObject
                };
                Director = director;
                Rig = rig;
                DefaultCamera = defaultCamera;
                AimCamera = aimCamera;
                FocusCamera = focusCamera;
                FollowTarget = followTarget;
                LookAtTarget = lookAtTarget;
                FocusTarget = focusTarget;
            }

            public CameraDirector Director { get; }
            public CameraRig Rig { get; }
            public CinemachineCamera DefaultCamera { get; }
            public CinemachineCamera AimCamera { get; }
            public CinemachineCamera FocusCamera { get; }
            public Transform FollowTarget { get; }
            public Transform LookAtTarget { get; }
            public Transform FocusTarget { get; }

            public void Destroy()
            {
                foreach (var targetObject in _objects)
                {
                    Object.DestroyImmediate(targetObject);
                }
            }
        }
    }
}

using System.Reflection;
using CoCoFlow.Runtime.Modules.Animation.Rig;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.Animation
{
    public class AnimRigModuleTests
    {
        [Test]
        public void AnimRigCharacterProfileDefaultsAreValid()
        {
            var profile = AnimRigCharacterProfile.CreateRuntimeDefault();
            try
            {
                Assert.Greater(profile.FootProbeDistance, 0f);
                Assert.GreaterOrEqual(profile.FootProbeUpOffset, 0f);
                Assert.GreaterOrEqual(profile.FootOffset, 0f);
                Assert.Greater(profile.FootBlendSpeed, 0f);
                Assert.Greater(profile.FootLockBlendSpeed, 0f);
                Assert.Greater(profile.LockReleaseDistance, 0f);
                Assert.Greater(profile.TeleportReleaseDistance, 0f);
                Assert.GreaterOrEqual(profile.AutomaticReleaseVelocity, profile.AutomaticPlantVelocity);
                Assert.AreEqual(AnimRigCharacterProfile.DefaultLeftFootPlantCurve, profile.LeftFootPlantCurve);
                Assert.AreEqual(AnimRigCharacterProfile.DefaultRightFootPlantCurve, profile.RightFootPlantCurve);
                Assert.Greater(profile.PlantEnterThreshold, profile.PlantExitThreshold);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AnimRigCharacterControllerDefaultsToExplicitlyDisabledFootRig()
        {
            var fixture = CreateRigFixture("Rig Defaults Test");
            try
            {
                Assert.IsFalse(fixture.Controller.FootRigEnabled);
                Assert.AreEqual(AnimRigFootLockMode.Off, fixture.Controller.FootLockMode);
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void AnimRigFootDriverSamplesGroundAndAppliesOffset()
        {
            var fixture = CreateRigFixture("Foot Rig Ground Test");
            var ground = CreateGround("Ground", new Vector3(0f, -0.05f, 0f));
            try
            {
                fixture.Controller.SetFootRigEnabled(true);
                fixture.Controller.TickRig(0.2f);

                var pose = fixture.Driver.GetFootPose(AnimRigFootSlot.Left);
                Assert.IsTrue(pose.HasGround);
                Assert.AreEqual(1f, fixture.Driver.GetAppliedPose(AnimRigFootSlot.Left).Weight, 0.001f);
                Assert.AreEqual(0.03f, fixture.LeftTarget.transform.position.y, 0.01f);
            }
            finally
            {
                fixture.Destroy();
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void AnimRigFootDriverRejectsSlopeAboveProfileLimit()
        {
            var fixture = CreateRigFixture("Foot Rig Slope Test");
            var slope = GameObject.CreatePrimitive(PrimitiveType.Plane);
            slope.name = "Rejected Slope";
            slope.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
            try
            {
                fixture.Controller.SetFootRigEnabled(true);
                Physics.SyncTransforms();
                fixture.Controller.TickRig(0.2f);

                Assert.IsFalse(fixture.Driver.GetFootPose(AnimRigFootSlot.Left).HasGround);
                Assert.AreEqual(0f, fixture.Driver.GetAppliedPose(AnimRigFootSlot.Left).Weight, 0.001f);
            }
            finally
            {
                fixture.Destroy();
                Object.DestroyImmediate(slope);
            }
        }

        [Test]
        public void ExplicitFootLockPlantsLocksAndReleases()
        {
            var fixture = CreateRigFixture("Foot Lock Transition Test");
            var ground = CreateGround("Ground", new Vector3(0f, -0.05f, 0f));
            try
            {
                fixture.Controller.SetFootRigEnabled(true);
                fixture.Controller.SetFootLockMode(AnimRigFootLockMode.Explicit);
                fixture.Controller.TickRig(0.2f);

                Assert.IsTrue(fixture.Controller.PlantFoot(AnimRigFootSlot.Left));
                Assert.AreEqual(AnimRigFootLockState.Planting, fixture.Controller.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Controller.TickRig(0.2f);
                Assert.AreEqual(AnimRigFootLockState.Locked, fixture.Controller.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Controller.ReleaseFoot(AnimRigFootSlot.Left);
                fixture.Controller.TickRig(0.2f);
                Assert.AreEqual(AnimRigFootLockState.Released, fixture.Controller.GetFootLockState(AnimRigFootSlot.Left));
            }
            finally
            {
                fixture.Destroy();
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void LockedFootTargetDoesNotDriftWithSmallRootMovement()
        {
            var fixture = CreateRigFixture("Foot Lock Drift Test");
            var ground = CreateGround("Ground", new Vector3(0f, -0.05f, 0f));
            try
            {
                fixture.Controller.SetFootRigEnabled(true);
                fixture.Controller.SetFootLockMode(AnimRigFootLockMode.Explicit);
                fixture.Controller.TickRig(0.2f);
                Assert.IsTrue(fixture.Controller.PlantFoot(AnimRigFootSlot.Left));
                fixture.Controller.TickRig(0.2f);

                var lockedPosition = fixture.LeftTarget.transform.position;
                fixture.Root.transform.position += Vector3.right * 0.1f;
                Physics.SyncTransforms();
                fixture.Controller.TickRig(0.2f);

                Assert.AreEqual(AnimRigFootLockState.Locked, fixture.Controller.GetFootLockState(AnimRigFootSlot.Left));
                Assert.That(Vector3.Distance(lockedPosition, fixture.LeftTarget.transform.position), Is.LessThan(0.001f));
            }
            finally
            {
                fixture.Destroy();
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void LockedFootReleasesWhenDistanceThresholdIsExceeded()
        {
            var fixture = CreateRigFixture("Foot Lock Release Test");
            var ground = CreateGround("Ground", new Vector3(0f, -0.05f, 0f));
            try
            {
                fixture.Controller.SetFootRigEnabled(true);
                fixture.Controller.SetFootLockMode(AnimRigFootLockMode.Explicit);
                fixture.Controller.TickRig(0.2f);
                Assert.IsTrue(fixture.Controller.PlantFoot(AnimRigFootSlot.Left));
                fixture.Controller.TickRig(0.2f);

                fixture.Root.transform.position += Vector3.right * 0.5f;
                Physics.SyncTransforms();
                fixture.Controller.TickRig(0.2f);

                Assert.AreEqual(AnimRigFootLockState.Released, fixture.Controller.GetFootLockState(AnimRigFootSlot.Left));
            }
            finally
            {
                fixture.Destroy();
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void AnimRigCharacterControllerTogglesFootRigAndLockMode()
        {
            var fixture = CreateRigFixture("Rig Controller API Test");
            var ground = CreateGround("Ground", new Vector3(0f, -0.05f, 0f));
            try
            {
                fixture.Controller.SetFootRigEnabled(true);
                fixture.Controller.SetFootLockMode(AnimRigFootLockMode.Explicit);
                fixture.Controller.TickRig(0.2f);
                Assert.IsTrue(fixture.Controller.PlantFoot(AnimRigFootSlot.Left));
                fixture.Controller.TickRig(0.2f);
                Assert.AreEqual(AnimRigFootLockState.Locked, fixture.Controller.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Controller.SetFootRigEnabled(false);
                fixture.Controller.TickRig(0.2f);

                Assert.IsFalse(fixture.Controller.FootRigEnabled);
                Assert.AreEqual(AnimRigFootLockState.Released, fixture.Controller.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Controller.SetFootRigEnabled(true);
                fixture.Controller.SetFootLockMode(AnimRigFootLockMode.Automatic);

                Assert.IsTrue(fixture.Controller.FootRigEnabled);
                Assert.AreEqual(AnimRigFootLockMode.Automatic, fixture.Controller.FootLockMode);
            }
            finally
            {
                fixture.Destroy();
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void AnimationDrivenFootLockUsesCurveThresholds()
        {
            var fixture = CreateRigFixture("Foot Lock Curve Test");
            var ground = CreateGround("Ground", new Vector3(0f, -0.05f, 0f));
            try
            {
                fixture.Controller.SetFootRigEnabled(true);
                fixture.Controller.TickRig(0.2f);

                fixture.Driver.UpdateFootLocks(
                    AnimRigFootLockMode.AnimationDriven,
                    fixture.Profile,
                    fixture.Root.transform,
                    0.2f,
                    0.7f,
                    0f);
                Assert.AreEqual(AnimRigFootLockState.Locked, fixture.Driver.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Driver.UpdateFootLocks(
                    AnimRigFootLockMode.AnimationDriven,
                    fixture.Profile,
                    fixture.Root.transform,
                    0.2f,
                    0.5f,
                    0f);
                Assert.AreEqual(AnimRigFootLockState.Locked, fixture.Driver.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Driver.UpdateFootLocks(
                    AnimRigFootLockMode.AnimationDriven,
                    fixture.Profile,
                    fixture.Root.transform,
                    0.2f,
                    0.2f,
                    0f);
                Assert.AreEqual(AnimRigFootLockState.Released, fixture.Driver.GetFootLockState(AnimRigFootSlot.Left));
            }
            finally
            {
                fixture.Destroy();
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void AnimationDrivenSafetyReleaseSuppressesReplantUntilCurveExits()
        {
            var fixture = CreateRigFixture("Foot Lock Curve Safety Test");
            var ground = CreateGround("Ground", new Vector3(0f, -0.05f, 0f));
            try
            {
                fixture.Controller.SetFootRigEnabled(true);
                fixture.Controller.TickRig(0.2f);
                fixture.Driver.UpdateFootLocks(
                    AnimRigFootLockMode.AnimationDriven,
                    fixture.Profile,
                    fixture.Root.transform,
                    0.2f,
                    1f,
                    0f);
                Assert.AreEqual(AnimRigFootLockState.Locked, fixture.Driver.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Root.transform.position += Vector3.right * 0.5f;
                Physics.SyncTransforms();
                fixture.Driver.SampleFeet(fixture.Profile, true, 0.2f);
                fixture.Driver.UpdateFootLocks(
                    AnimRigFootLockMode.AnimationDriven,
                    fixture.Profile,
                    fixture.Root.transform,
                    0.2f,
                    1f,
                    0f);
                Assert.AreEqual(AnimRigFootLockState.Released, fixture.Driver.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Driver.UpdateFootLocks(
                    AnimRigFootLockMode.AnimationDriven,
                    fixture.Profile,
                    fixture.Root.transform,
                    0.2f,
                    1f,
                    0f);
                Assert.AreEqual(AnimRigFootLockState.Released, fixture.Driver.GetFootLockState(AnimRigFootSlot.Left));

                fixture.Driver.UpdateFootLocks(
                    AnimRigFootLockMode.AnimationDriven,
                    fixture.Profile,
                    fixture.Root.transform,
                    0.2f,
                    0f,
                    0f);
                fixture.Driver.UpdateFootLocks(
                    AnimRigFootLockMode.AnimationDriven,
                    fixture.Profile,
                    fixture.Root.transform,
                    0.2f,
                    1f,
                    0f);
                Assert.AreEqual(AnimRigFootLockState.Locked, fixture.Driver.GetFootLockState(AnimRigFootSlot.Left));
            }
            finally
            {
                fixture.Destroy();
                Object.DestroyImmediate(ground);
            }
        }

        private static RigFixture CreateRigFixture(string name)
        {
            var root = new GameObject(name);
            var leftFoot = new GameObject("LeftFoot");
            var rightFoot = new GameObject("RightFoot");
            var leftTarget = new GameObject("LeftFootTarget");
            var rightTarget = new GameObject("RightFootTarget");

            leftFoot.transform.SetParent(root.transform, false);
            rightFoot.transform.SetParent(root.transform, false);
            leftTarget.transform.SetParent(root.transform, false);
            rightTarget.transform.SetParent(root.transform, false);

            leftFoot.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            rightFoot.transform.localPosition = new Vector3(0.2f, 0.6f, 0f);
            leftTarget.transform.localPosition = leftFoot.transform.localPosition;
            rightTarget.transform.localPosition = rightFoot.transform.localPosition;

            var controller = root.AddComponent<AnimRigCharacterController>();
            var driver = root.GetComponent<AnimRigFootDriver>();
            var profile = AnimRigCharacterProfile.CreateRuntimeDefault();
            controller.SetProfile(profile);

            SetBindingTransform(driver.LeftFoot, "footBone", leftFoot.transform);
            SetBindingTransform(driver.LeftFoot, "ikTarget", leftTarget.transform);
            SetBindingTransform(driver.RightFoot, "footBone", rightFoot.transform);
            SetBindingTransform(driver.RightFoot, "ikTarget", rightTarget.transform);
            Physics.SyncTransforms();

            return new RigFixture(
                root,
                leftFoot,
                rightFoot,
                leftTarget,
                rightTarget,
                controller,
                driver,
                profile);
        }

        private static GameObject CreateGround(string name, Vector3 position)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = name;
            ground.transform.position = position;
            ground.transform.localScale = new Vector3(10f, 0.1f, 10f);
            Physics.SyncTransforms();
            return ground;
        }

        private static void SetBindingTransform(
            AnimRigFootBinding binding,
            string fieldName,
            Transform value)
        {
            var field = typeof(AnimRigFootBinding).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(binding, value);
        }

        private sealed class RigFixture
        {
            public RigFixture(
                GameObject root,
                GameObject leftFoot,
                GameObject rightFoot,
                GameObject leftTarget,
                GameObject rightTarget,
                AnimRigCharacterController controller,
                AnimRigFootDriver driver,
                AnimRigCharacterProfile profile)
            {
                Root = root;
                LeftFoot = leftFoot;
                RightFoot = rightFoot;
                LeftTarget = leftTarget;
                RightTarget = rightTarget;
                Controller = controller;
                Driver = driver;
                Profile = profile;
            }

            public GameObject Root { get; }
            public GameObject LeftFoot { get; }
            public GameObject RightFoot { get; }
            public GameObject LeftTarget { get; }
            public GameObject RightTarget { get; }
            public AnimRigCharacterController Controller { get; }
            public AnimRigFootDriver Driver { get; }
            public AnimRigCharacterProfile Profile { get; }

            public void Destroy()
            {
                Object.DestroyImmediate(Profile);
                Object.DestroyImmediate(Root);
            }
        }
    }
}

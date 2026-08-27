using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;
using CoCoFlow.Runtime.Modules.Locomotion;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.Locomotion
{
    /// <summary>
    /// Pure-step unit tests (CL verbatim carry, D80): gravity tiers, ground
    /// stick, jump gating, launch gating, forced priority, hover reset,
    /// teleport bypass.
    /// </summary>
    public sealed class LocomotionStateMathTests
    {
        private static LocoConfig Config() => new LocoConfig
        {
            Gravity = -9.81f,
            BaseGravityMultiplier = 2f,
            IsUsingGravity = true,
            GroundLayer = ~0,
            GroundCheckRadius = 0.2f,
            GroundCheckOffset = new Vector3(0f, 0.1f, 0f),
            RotationSmoothTime = 0.1f,
        };

        private static LocoSectionInput Idle() => new LocoSectionInput
        {
            UseGravity = true,
            GravityScale = 1f,
        };

        [Test]
        public void GroundedStickAppliesMinusTwo()
        {
            var state = default(LocomotionState); // VerticalVelocity = 0
            var next = LocomotionStateMath.Step(
                state, Idle(), Config(), grounded: true, 1f / 60f,
                out _, out _, out _, out _);
            Assert.AreEqual(-2f, next.VerticalVelocity);
        }

        [Test]
        public void AirborneGravityIntegratesThreeTiers()
        {
            var state = default(LocomotionState);
            float dt = 1f / 60f;
            // one step from rest
            var next = LocomotionStateMath.Step(
                state, Idle(), Config(), grounded: false, dt,
                out _, out _, out _, out _);
            Assert.AreEqual(
                -9.81f * 2f * 1f * dt,
                next.VerticalVelocity,
                1e-6f);
            // gravity scale tier
            var slow = new LocoSectionInput { UseGravity = true, GravityScale = 0.5f };
            var nextSlow = LocomotionStateMath.Step(
                default(LocomotionState), slow, Config(), false, dt,
                out _, out _, out _, out _);
            Assert.AreEqual(
                -9.81f * 2f * 0.5f * dt,
                nextSlow.VerticalVelocity,
                1e-6f);
        }

        [Test]
        public void JumpGatedByGrounded()
        {
            var input = Idle();
            input.JumpRequested = true;
            input.VerticalImpulse = 5f;

            var airborne = LocomotionStateMath.Step(
                default(LocomotionState), input, Config(), false, 1f / 60f,
                out _, out _, out _, out _);
            Assert.AreNotEqual(5f, airborne.VerticalVelocity); // gated

            var groundedRun = LocomotionStateMath.Step(
                default(LocomotionState), input, Config(), true, 1f / 60f,
                out _, out _, out _, out _);
            Assert.AreEqual(5f, groundedRun.VerticalVelocity);
        }

        [Test]
        public void LaunchBypassesGroundedGate()
        {
            var input = Idle();
            input.LaunchForced = true;
            input.VerticalImpulse = 8f;
            var next = LocomotionStateMath.Step(
                default(LocomotionState), input, Config(), false, 1f / 60f,
                out _, out _, out _, out _);
            Assert.AreEqual(8f, next.VerticalVelocity);
        }

        [Test]
        public void ForcedOverridesMove()
        {
            var input = Idle();
            input.MoveX = 3f;
            input.ForcedZ = 6f;
            LocomotionStateMath.Step(
                default(LocomotionState), input, Config(), true, 1f,
                out float dx, out float dz, out _, out _);
            // CL semantics: forced presence replaces the whole horizontal
            // vector (ForcedX=0 overrides MoveX=3 — knockback replaces,
            // never blends).
            Assert.AreEqual(0f, dx);
            Assert.AreEqual(6f, dz);
        }

        [Test]
        public void HoverResetsVerticalVelocity()
        {
            var state = default(LocomotionState);
            state.VerticalVelocity = -12f;
            var input = Idle();
            input.UseGravity = false;
            var next = LocomotionStateMath.Step(
                state, input, Config(), false, 1f / 60f,
                out _, out _, out _, out _);
            Assert.AreEqual(0f, next.VerticalVelocity);
        }

        [Test]
        public void TeleportBypassesDeltaAndSetsRegister()
        {
            var input = Idle();
            input.TeleportRequested = true;
            input.TeleportX = 10f;
            input.TeleportY = 2f;
            input.TeleportZ = -3f;
            var next = LocomotionStateMath.Step(
                default(LocomotionState), input, Config(), true, 1f / 60f,
                out float dx, out float dz, out float dy, out bool teleport);
            Assert.IsTrue(teleport);
            Assert.AreEqual(10f, next.PositionX);
            Assert.AreEqual(2f, next.PositionY);
            Assert.AreEqual(-3f, next.PositionZ);
            // delta still synthesized but the engine segment must skip Move
            // (teleport flag); position register carries the target.
        }

        [Test]
        public void RotationSmoothDampUsesMathfDirectly()
        {
            var input = Idle();
            input.LookX = 0f;
            input.LookZ = 1f; // face +Z → target angle 0 from 180 → half turn
            var state = default(LocomotionState);
            state.Rotation = 180f;

            float velocity = 0f;
            float expected = Mathf.SmoothDampAngle(
                180f, 0f, ref velocity, 0.1f, Mathf.Infinity, 1f / 60f);

            var next = LocomotionStateMath.Step(
                state, input, Config(), true, 1f / 60f,
                out _, out _, out _, out _);
            Assert.AreEqual(expected, next.Rotation, 1e-5f);
        }

        [Test]
        public void InstantRotationSnaps()
        {
            var input = Idle();
            input.LookZ = 1f;
            input.InstantRotation = true;
            var state = default(LocomotionState);
            state.Rotation = 170f;
            var next = LocomotionStateMath.Step(
                state, input, Config(), true, 1f / 60f,
                out _, out _, out _, out _);
            Assert.AreEqual(0f, next.Rotation, 1e-4f);
            Assert.AreEqual(0f, next.RotationVelocity);
        }

        [Test]
        public void SectionFieldConstantsMatchFrozenAlphabeticalShape()
        {
            Assert.IsTrue(CoCoOperationSectionShape.TryCreate(
                typeof(ILocomotionSection),
                out CoCoOperationSectionShape shape,
                out CoCoDiagnostic diagnostic), diagnostic.Message);

            AssertField(shape, LocomotionSectionFields.ForcedX, nameof(ILocomotionSection.ForcedX), typeof(float));
            AssertField(shape, LocomotionSectionFields.ForcedZ, nameof(ILocomotionSection.ForcedZ), typeof(float));
            AssertField(shape, LocomotionSectionFields.GravityScale, nameof(ILocomotionSection.GravityScale), typeof(float));
            AssertField(shape, LocomotionSectionFields.InstantRotation, nameof(ILocomotionSection.InstantRotation), typeof(bool));
            AssertField(shape, LocomotionSectionFields.JumpRequested, nameof(ILocomotionSection.JumpRequested), typeof(bool));
            AssertField(shape, LocomotionSectionFields.LaunchForced, nameof(ILocomotionSection.LaunchForced), typeof(bool));
            AssertField(shape, LocomotionSectionFields.LookX, nameof(ILocomotionSection.LookX), typeof(float));
            AssertField(shape, LocomotionSectionFields.LookZ, nameof(ILocomotionSection.LookZ), typeof(float));
            AssertField(shape, LocomotionSectionFields.MoveX, nameof(ILocomotionSection.MoveX), typeof(float));
            AssertField(shape, LocomotionSectionFields.MoveZ, nameof(ILocomotionSection.MoveZ), typeof(float));
            AssertField(shape, LocomotionSectionFields.TeleportRequested, nameof(ILocomotionSection.TeleportRequested), typeof(bool));
            AssertField(shape, LocomotionSectionFields.TeleportRotationW, nameof(ILocomotionSection.TeleportRotationW), typeof(float));
            AssertField(shape, LocomotionSectionFields.TeleportRotationX, nameof(ILocomotionSection.TeleportRotationX), typeof(float));
            AssertField(shape, LocomotionSectionFields.TeleportRotationY, nameof(ILocomotionSection.TeleportRotationY), typeof(float));
            AssertField(shape, LocomotionSectionFields.TeleportRotationZ, nameof(ILocomotionSection.TeleportRotationZ), typeof(float));
            AssertField(shape, LocomotionSectionFields.TeleportX, nameof(ILocomotionSection.TeleportX), typeof(float));
            AssertField(shape, LocomotionSectionFields.TeleportY, nameof(ILocomotionSection.TeleportY), typeof(float));
            AssertField(shape, LocomotionSectionFields.TeleportZ, nameof(ILocomotionSection.TeleportZ), typeof(float));
            AssertField(shape, LocomotionSectionFields.UseGravity, nameof(ILocomotionSection.UseGravity), typeof(bool));
            AssertField(shape, LocomotionSectionFields.VerticalImpulse, nameof(ILocomotionSection.VerticalImpulse), typeof(float));
        }

        private static void AssertField(
            CoCoOperationSectionShape shape,
            int denseIndex,
            string name,
            Type valueType)
        {
            Assert.That(denseIndex, Is.InRange(0, shape.FieldCount - 1));
            CoCoOperationSectionFieldShape field = shape.Fields[denseIndex];
            Assert.AreEqual(denseIndex, field.DenseIndex);
            Assert.AreEqual(name, field.Name);
            Assert.AreEqual(valueType, field.ValueType);
        }
    }
}

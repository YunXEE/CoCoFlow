using System.Collections.Generic;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Animation;
using CoCoFlow.Runtime.Modules.Animation.DOTween;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.Animation.DOTween
{
    public sealed class AnimDotweenModulationAdapterTests
    {
        private AnimDotweenModulationAdapter _adapter;
        private RecordingHost _host;

        [SetUp]
        public void SetUp()
        {
            _host = new RecordingHost();
            _adapter = new AnimDotweenModulationAdapter(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _adapter?.Dispose();
            _adapter = null;
            _host = null;
        }

        [Test]
        public void Stop_CancelsOnlyTheOwnedTweenForTheMatchingBinding()
        {
            AnimModulationTarget first = CreateTarget(
                501UL,
                AnimModulationKind.FloatParameter);
            AnimModulationTarget second = CreateTarget(
                502UL,
                AnimModulationKind.FloatParameter);
            _host.Set(first, Vector4.zero);
            _host.Set(second, Vector4.zero);

            Assert.IsTrue(
                _adapter.TryStart(
                    CreateCommand(first, 10f, 1f),
                    first,
                    out CoCoDiagnostic firstDiagnostic),
                firstDiagnostic.Message);
            Assert.IsTrue(
                _adapter.TryStart(
                    CreateCommand(second, 20f, 1f),
                    second,
                    out CoCoDiagnostic secondDiagnostic),
                secondDiagnostic.Message);
            Assert.IsTrue(
                _adapter.TryManualUpdate(
                    0.25f,
                    out CoCoDiagnostic updateDiagnostic),
                updateDiagnostic.Message);
            float stoppedValue = _host.Read(first).x;
            float secondValue = _host.Read(second).x;

            _adapter.Stop(first);
            Assert.IsTrue(
                _adapter.TryManualUpdate(
                    0.25f,
                    out updateDiagnostic),
                updateDiagnostic.Message);

            Assert.That(_host.Read(first).x, Is.EqualTo(stoppedValue));
            Assert.That(_host.Read(second).x, Is.GreaterThan(secondValue));
        }

        [Test]
        public void RotationTween_FlipsEquivalentNegativeQuaternionToSameHemisphere()
        {
            AnimModulationTarget target = CreateTarget(
                503UL,
                AnimModulationKind.PresentationOffsetRotation);
            _host.Set(target, new Vector4(0f, 0f, 0f, 1f));

            Assert.IsTrue(
                _adapter.TryStart(
                    CreateCommand(
                        target,
                        0f,
                        0f,
                        0f,
                        -float.MaxValue,
                        1f),
                    target,
                    out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(
                _adapter.TryManualUpdate(
                    0.5f,
                    out CoCoDiagnostic updateDiagnostic),
                updateDiagnostic.Message);

            Vector4 value = _host.Read(target);
            Assert.That(value.sqrMagnitude, Is.GreaterThan(0.99f));
            Assert.That(value.w, Is.GreaterThan(0.99f));
        }

        [Test]
        public void RotationTween_NormalizesHugeFiniteTargetBeforeInterpolation()
        {
            AnimModulationTarget target = CreateTarget(
                506UL,
                AnimModulationKind.PresentationOffsetRotation);
            _host.Set(target, new Vector4(0f, 0f, 0f, 1f));

            Assert.IsTrue(
                _adapter.TryStart(
                    CreateCommand(
                        target,
                        float.MaxValue,
                        float.MaxValue,
                        0f,
                        0f,
                        1f),
                    target,
                    out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(
                _adapter.TryManualUpdate(
                    1f,
                    out CoCoDiagnostic updateDiagnostic),
                updateDiagnostic.Message);

            Vector4 value = _host.Read(target);
            Assert.That(value.magnitude, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(value.x, Is.EqualTo(0.7071068f).Within(0.00001f));
            Assert.That(value.y, Is.EqualTo(0.7071068f).Within(0.00001f));
            Assert.That(value.z, Is.EqualTo(0f).Within(0.00001f));
            Assert.That(value.w, Is.EqualTo(0f).Within(0.00001f));
        }

        [Test]
        public void RotationTween_RejectsNearZeroEndpointBeforeOwningTween()
        {
            AnimModulationTarget target = CreateTarget(
                507UL,
                AnimModulationKind.PresentationOffsetRotation);
            Vector4 current = new Vector4(0f, 0f, 0f, 1f);
            _host.Set(target, current);

            Assert.IsFalse(
                _adapter.TryStart(
                    CreateCommand(
                        target,
                        0.0000001f,
                        0f,
                        0f,
                        0f,
                        1f),
                    target,
                    out CoCoDiagnostic diagnostic));
            Assert.IsTrue(diagnostic.IsError);
            Assert.That(_host.Read(target), Is.EqualTo(current));
        }

        [Test]
        public void ManualUpdate_WriteFailureStopsAllOwnedTweensAndReturnsError()
        {
            AnimModulationTarget first = CreateTarget(
                504UL,
                AnimModulationKind.FloatParameter);
            AnimModulationTarget second = CreateTarget(
                505UL,
                AnimModulationKind.FloatParameter);
            _host.Set(first, Vector4.zero);
            _host.Set(second, Vector4.zero);
            Assert.IsTrue(
                _adapter.TryStart(
                    CreateCommand(first, 10f, 1f),
                    first,
                    out CoCoDiagnostic firstDiagnostic),
                firstDiagnostic.Message);
            Assert.IsTrue(
                _adapter.TryStart(
                    CreateCommand(second, 20f, 1f),
                    second,
                    out CoCoDiagnostic secondDiagnostic),
                secondDiagnostic.Message);

            _host.RejectedBinding = first.BindingId;
            Assert.IsFalse(
                _adapter.TryManualUpdate(
                    0.25f,
                    out CoCoDiagnostic diagnostic));
            Assert.IsTrue(diagnostic.IsError);
            Vector4 secondValue = _host.Read(second);
            _host.RejectedBinding = default;
            Assert.IsTrue(
                _adapter.TryManualUpdate(
                    0.25f,
                    out diagnostic),
                diagnostic.Message);
            Assert.That(_host.Read(second), Is.EqualTo(secondValue));
        }

        private static AnimModulationTarget CreateTarget(
            ulong bindingValue,
            AnimModulationKind kind)
        {
            Assert.IsTrue(
                AnimBindingId.TryCreate(
                    bindingValue,
                    out AnimBindingId bindingId));
            return new AnimModulationTarget(
                bindingId,
                kind,
                0,
                0,
                null);
        }

        private static AnimModulationCommand CreateCommand(
            in AnimModulationTarget target,
            float value,
            float durationSeconds)
        {
            return CreateCommand(
                target,
                value,
                0f,
                0f,
                0f,
                durationSeconds);
        }

        private static AnimModulationCommand CreateCommand(
            in AnimModulationTarget target,
            float valueX,
            float valueY,
            float valueZ,
            float valueW,
            float durationSeconds)
        {
            Assert.IsTrue(
                CoCoActivationId.TryCreate(
                    1UL,
                    out CoCoActivationId activationId));
            Assert.IsTrue(
                AnimModulationCommand.TryCreate(
                    target.Kind,
                    target.BindingId,
                    AnimModulationInterpolation.AdapterOwned,
                    activationId,
                    1U,
                    durationSeconds,
                    valueX,
                    valueY,
                    valueZ,
                    valueW,
                    out AnimModulationCommand command));
            return command;
        }

        private sealed class RecordingHost : IAnimModulationHost
        {
            private readonly Dictionary<AnimBindingId, Vector4> _values =
                new Dictionary<AnimBindingId, Vector4>();
            internal AnimBindingId RejectedBinding { get; set; }

            internal void Set(
                in AnimModulationTarget target,
                in Vector4 value)
            {
                _values[target.BindingId] = value;
            }

            internal Vector4 Read(in AnimModulationTarget target)
            {
                return _values[target.BindingId];
            }

            public bool TryReadModulation(
                in AnimModulationTarget target,
                out Vector4 value)
            {
                return _values.TryGetValue(target.BindingId, out value);
            }

            public bool TryWriteModulation(
                in AnimModulationTarget target,
                in Vector4 value)
            {
                if (target.BindingId == RejectedBinding)
                {
                    return false;
                }

                _values[target.BindingId] = value;
                return true;
            }
        }
    }
}

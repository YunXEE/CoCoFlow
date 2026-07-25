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
            _adapter.ManualUpdate(0.25f);
            float stoppedValue = _host.Read(first).x;
            float secondValue = _host.Read(second).x;

            _adapter.Stop(first);
            _adapter.ManualUpdate(0.25f);

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
                        -1f,
                        1f),
                    target,
                    out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            _adapter.ManualUpdate(0.5f);

            Vector4 value = _host.Read(target);
            Assert.That(value.sqrMagnitude, Is.GreaterThan(0.99f));
            Assert.That(value.w, Is.GreaterThan(0.99f));
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
                _values[target.BindingId] = value;
                return true;
            }
        }
    }
}

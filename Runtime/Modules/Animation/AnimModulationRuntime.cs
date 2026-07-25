using System;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation
{
    internal interface IAnimModulationHost
    {
        bool TryReadModulation(
            in AnimModulationTarget target,
            out Vector4 value);

        bool TryWriteModulation(
            in AnimModulationTarget target,
            in Vector4 value);
    }

    internal interface IAnimModulationAdapter : IDisposable
    {
        bool TryStart(
            in AnimModulationCommand command,
            in AnimModulationTarget target,
            out CoCoDiagnostic diagnostic);

        void Stop(in AnimModulationTarget target);

        bool TryManualUpdate(
            float positiveDeltaSeconds,
            out CoCoDiagnostic diagnostic);

        void StopAll();
    }

    internal interface IAnimModulationAdapterFactory
    {
        IAnimModulationAdapter Create(IAnimModulationHost host);
    }

    internal static class AnimModulationMath
    {
        private const float MinimumRotationMagnitude = 0.000001f;

        internal static bool TryNormalizeRotation(
            in Vector4 value,
            out Vector4 normalized)
        {
            normalized = default;
            if (!IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z) ||
                !IsFinite(value.w))
            {
                return false;
            }

            float maximum = Mathf.Max(
                Mathf.Max(
                    Mathf.Abs(value.x),
                    Mathf.Abs(value.y)),
                Mathf.Max(
                    Mathf.Abs(value.z),
                    Mathf.Abs(value.w)));
            if (!IsFinite(maximum) || maximum <= 0f)
            {
                return false;
            }

            Vector4 scaled = value / maximum;
            float scaledMagnitude = Mathf.Sqrt(
                scaled.x * scaled.x +
                scaled.y * scaled.y +
                scaled.z * scaled.z +
                scaled.w * scaled.w);
            if (!IsFinite(scaledMagnitude) ||
                scaledMagnitude <= 0f ||
                (maximum <= MinimumRotationMagnitude &&
                 maximum * scaledMagnitude <= MinimumRotationMagnitude))
            {
                return false;
            }

            normalized = scaled / scaledMagnitude;
            return IsFinite(normalized.x) &&
                   IsFinite(normalized.y) &&
                   IsFinite(normalized.z) &&
                   IsFinite(normalized.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal static class AnimModulationAdapterRegistry
    {
        private static IAnimModulationAdapterFactory _factory;

        internal static bool TryInstall(IAnimModulationAdapterFactory factory)
        {
            if (factory == null || _factory != null)
            {
                return false;
            }

            _factory = factory;
            return true;
        }

        internal static IAnimModulationAdapter Create(IAnimModulationHost host)
        {
            return host == null ? null : _factory?.Create(host);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _factory = null;
        }
    }

    internal readonly struct AnimModulationStamp : IEquatable<AnimModulationStamp>
    {
        internal AnimModulationStamp(
            AnimBindingId bindingId,
            CoCoActivationId activationId,
            uint serial)
        {
            BindingId = bindingId;
            ActivationId = activationId;
            Serial = serial;
        }

        internal AnimBindingId BindingId { get; }
        internal CoCoActivationId ActivationId { get; }
        internal uint Serial { get; }
        internal bool IsValid => BindingId.IsValid && ActivationId.IsValid && Serial != 0U;

        public bool Equals(AnimModulationStamp other)
        {
            return BindingId == other.BindingId &&
                   ActivationId == other.ActivationId &&
                   Serial == other.Serial;
        }

        public override bool Equals(object obj)
        {
            return obj is AnimModulationStamp other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = BindingId.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Serial;
                return hashCode;
            }
        }
    }
}

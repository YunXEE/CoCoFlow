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

        void ManualUpdate(float positiveDeltaSeconds);

        void StopAll();
    }

    internal interface IAnimModulationAdapterFactory
    {
        IAnimModulationAdapter Create(IAnimModulationHost host);
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

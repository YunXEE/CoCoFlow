using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using DG.Tweening;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation.DOTween
{
    internal sealed class AnimDotweenModulationAdapter :
        IAnimModulationAdapter
    {
        private readonly IAnimModulationHost _host;
        private readonly OwnedTween[] _ownedTweens =
            new OwnedTween[AnimContractLimits.ModulationLaneCount];
        private int _ownedTweenCount;
        private bool _isDisposed;

        internal AnimDotweenModulationAdapter(IAnimModulationHost host)
        {
            _host = host;
        }

        public bool TryStart(
            in AnimModulationCommand command,
            in AnimModulationTarget target,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (_isDisposed ||
                _host == null ||
                !command.IsValid ||
                command.Interpolation != AnimModulationInterpolation.AdapterOwned ||
                command.BindingId != target.BindingId ||
                command.Kind != target.Kind ||
                !_host.TryReadModulation(target, out Vector4 currentValue))
            {
                diagnostic = AnimOperatorContracts.Error(
                    "DOTween modulation requires one live, matching AdapterOwned target.");
                return false;
            }

            Stop(target);
            var targetValue = new Vector4(
                command.ValueX,
                command.ValueY,
                command.ValueZ,
                command.ValueW);
            if (target.Kind == AnimModulationKind.PresentationOffsetRotation &&
                Vector4.Dot(currentValue, targetValue) < 0f)
            {
                targetValue = -targetValue;
            }

            if (command.DurationSeconds == 0f)
            {
                if (!_host.TryWriteModulation(target, targetValue))
                {
                    diagnostic = AnimOperatorContracts.Error(
                        "DOTween modulation could not write its zero-duration target.");
                    return false;
                }

                return true;
            }

            if (_ownedTweenCount >= _ownedTweens.Length)
            {
                diagnostic = AnimOperatorContracts.Error(
                    "DOTween modulation exhausted its fixed eight-target capacity.");
                return false;
            }

            var owned = new OwnedTween(command.BindingId);
            AnimModulationTarget ownedTarget = target;
            Vector4 value = currentValue;
            Tween tween = global::DG.Tweening.DOTween
                .To(
                    () => value,
                    nextValue =>
                    {
                        value = nextValue;
                        if (!_host.TryWriteModulation(ownedTarget, nextValue))
                        {
                            owned.WriteFailed = true;
                        }
                    },
                    targetValue,
                    command.DurationSeconds)
                .SetEase(Ease.Linear)
                .SetUpdate(UpdateType.Manual)
                .SetAutoKill(true);
            if (tween == null || !tween.IsActive())
            {
                tween?.Kill(false);
                diagnostic = AnimOperatorContracts.Error(
                    "DOTween modulation could not create its owned manual tween.");
                return false;
            }

            owned.Tween = tween;
            _ownedTweens[_ownedTweenCount++] = owned;
            return true;
        }

        public void ManualUpdate(float positiveDeltaSeconds)
        {
            if (_isDisposed ||
                positiveDeltaSeconds <= 0f ||
                float.IsNaN(positiveDeltaSeconds) ||
                float.IsInfinity(positiveDeltaSeconds))
            {
                return;
            }

            for (int index = _ownedTweenCount - 1; index >= 0; index--)
            {
                OwnedTween owned = _ownedTweens[index];
                if (owned.Tween == null ||
                    !owned.Tween.IsActive() ||
                    owned.WriteFailed)
                {
                    RemoveAt(index);
                    continue;
                }

                owned.Tween.ManualUpdate(
                    positiveDeltaSeconds,
                    positiveDeltaSeconds);
                if (owned.WriteFailed ||
                    !owned.Tween.IsActive() ||
                    owned.Tween.IsComplete())
                {
                    RemoveAt(index);
                }
            }
        }

        public void Stop(in AnimModulationTarget target)
        {
            if (_isDisposed)
            {
                return;
            }

            RemoveOwnedTween(target.BindingId);
        }

        public void StopAll()
        {
            for (int index = _ownedTweenCount - 1; index >= 0; index--)
            {
                RemoveAt(index);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            StopAll();
            _isDisposed = true;
        }

        private void RemoveOwnedTween(AnimBindingId bindingId)
        {
            for (int index = _ownedTweenCount - 1; index >= 0; index--)
            {
                if (_ownedTweens[index].BindingId == bindingId)
                {
                    RemoveAt(index);
                }
            }
        }

        private void RemoveAt(int index)
        {
            OwnedTween owned = _ownedTweens[index];
            if (owned?.Tween != null && owned.Tween.IsActive())
            {
                owned.Tween.Kill(false);
            }

            int lastIndex = --_ownedTweenCount;
            _ownedTweens[index] = _ownedTweens[lastIndex];
            _ownedTweens[lastIndex] = null;
        }

        private sealed class OwnedTween
        {
            internal OwnedTween(AnimBindingId bindingId)
            {
                BindingId = bindingId;
            }

            internal AnimBindingId BindingId { get; }
            internal Tween Tween { get; set; }
            internal bool WriteFailed { get; set; }
        }
    }

    internal static class AnimDotweenModulationInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            AnimModulationAdapterRegistry.TryInstall(
                new AnimDotweenModulationAdapterFactory());
        }

        private sealed class AnimDotweenModulationAdapterFactory :
            IAnimModulationAdapterFactory
        {
            public IAnimModulationAdapter Create(IAnimModulationHost host)
            {
                return host == null
                    ? null
                    : new AnimDotweenModulationAdapter(host);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public class CameraDirector : MonoBehaviour, ICameraDirector
    {
        [Header("Rigs")]
        [SerializeField] private List<CameraRig> initialRigs = new List<CameraRig>();

        [Header("Runtime Binding")]
        [SerializeField] private bool registerAsService = true;
        [SerializeField] private bool schedulingSuspended;

        private readonly List<RegisteredCameraRig> _rigs = new List<RegisteredCameraRig>();

        private CameraRig _activeRig;
        private CinemachineVirtualCameraBase _activeVirtualCamera;
        private int _nextSequence;

        public CameraRig ActiveRig => _activeRig;
        public CinemachineVirtualCameraBase ActiveVirtualCamera => _activeVirtualCamera;
        public bool IsSchedulingSuspended => schedulingSuspended;

        public event Action<CameraRigChangedEvent> ActiveRigChanged;

        public void RegisterRig(CameraRig rig)
        {
            if (!IsRigAlive(rig)) return;

            foreach (var entry in _rigs)
            {
                if (ReferenceEquals(entry.Rig, rig))
                {
                    ApplyCurrentRig();
                    return;
                }
            }

            _rigs.Add(new RegisteredCameraRig(rig, _nextSequence++));
            ApplyCurrentRig();
        }

        public void UnregisterRig(CameraRig rig)
        {
            bool removed = false;
            for (int i = _rigs.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_rigs[i].Rig, rig)) continue;

                _rigs.RemoveAt(i);
                rig.ClearRuntimePriorities();
                removed = true;
            }

            if (removed)
            {
                ApplyCurrentRig();
            }
        }

        public void RefreshRig(CameraRig rig)
        {
            if (!IsRigAlive(rig)) return;

            foreach (var entry in _rigs)
            {
                if (ReferenceEquals(entry.Rig, rig))
                {
                    ApplyCurrentRig();
                    return;
                }
            }
        }

        public void SetRigActive(CameraRig rig, bool active)
        {
            if (!IsRigAlive(rig)) return;

            rig.SetActive(active);
        }

        public void SetRigPriority(CameraRig rig, int priority)
        {
            if (!IsRigAlive(rig)) return;

            rig.SetPriority(priority);
        }

        public void SetSchedulingSuspended(bool suspended)
        {
            if (schedulingSuspended == suspended) return;

            schedulingSuspended = suspended;
            ApplyCurrentRig();
        }

        private void Awake()
        {
            foreach (var rig in initialRigs)
            {
                RegisterRig(rig);
            }

            ApplyCurrentRig();
        }

        private void OnEnable()
        {
            if (registerAsService)
            {
                CoCoServices.Register<ICameraDirector>(this);
            }

            ApplyCurrentRig();
        }

        private void OnDisable()
        {
            ResetRigPriorities();
            SetActiveRig(null, null);

            if (registerAsService)
            {
                CoCoServices.Unregister<ICameraDirector>(this);
            }
        }

        private void ApplyCurrentRig()
        {
            RemoveInvalidRigs();
            ResetRigPriorities();

            if (schedulingSuspended)
            {
                SetActiveRig(null, null);
                return;
            }

            var candidate = SelectActiveRig();
            if (candidate == null)
            {
                SetActiveRig(null, null);
                return;
            }

            candidate.Rig.ApplyRuntimePriority();
            SetActiveRig(candidate.Rig, candidate.Rig.CurrentCamera);
        }

        private RegisteredCameraRig SelectActiveRig()
        {
            RegisteredCameraRig selected = null;
            foreach (var entry in _rigs)
            {
                var rig = entry.Rig;
                if (!IsRigAlive(rig) ||
                    !rig.IsAvailable ||
                    rig.CurrentCamera == null)
                {
                    continue;
                }

                if (selected == null ||
                    rig.Priority > selected.Rig.Priority ||
                    rig.Priority == selected.Rig.Priority &&
                    entry.Sequence > selected.Sequence)
                {
                    selected = entry;
                }
            }

            return selected;
        }

        private void ResetRigPriorities()
        {
            foreach (var entry in _rigs)
            {
                if (IsRigAlive(entry.Rig))
                {
                    entry.Rig.ClearRuntimePriorities();
                }
            }
        }

        private void SetActiveRig(
            CameraRig rig,
            CinemachineVirtualCameraBase virtualCamera)
        {
            var previousRig = _activeRig;
            var previousVirtualCamera = _activeVirtualCamera;

            _activeRig = rig;
            _activeVirtualCamera = virtualCamera;

            if (previousRig != _activeRig ||
                previousVirtualCamera != _activeVirtualCamera)
            {
                ActiveRigChanged?.Invoke(new CameraRigChangedEvent(
                    previousRig,
                    _activeRig,
                    previousVirtualCamera,
                    _activeVirtualCamera));
            }
        }

        private void RemoveInvalidRigs()
        {
            for (int i = _rigs.Count - 1; i >= 0; i--)
            {
                if (!IsRigAlive(_rigs[i].Rig))
                {
                    _rigs.RemoveAt(i);
                }
            }
        }

        private static bool IsRigAlive(CameraRig rig)
        {
            return rig != null;
        }

        private sealed class RegisteredCameraRig
        {
            public RegisteredCameraRig(CameraRig rig, int sequence)
            {
                Rig = rig;
                Sequence = sequence;
            }

            public CameraRig Rig { get; }
            public int Sequence { get; }
        }
    }
}

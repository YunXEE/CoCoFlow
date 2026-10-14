using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Camera.Contracts;
using Unity.Cinemachine;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public sealed class CameraDirector : MonoBehaviour, ICameraDirector
    {
        [Header("Rigs")]
        [SerializeField] private List<CameraRig> initialRigs = new List<CameraRig>();

        [Header("StateGraph Binding")]
        [SerializeField] private CinemachineBrain brain;
        [SerializeField] private RigMapping[] rigMappings = Array.Empty<RigMapping>();

        [Header("Runtime Binding")]
        [SerializeField] private bool registerAsService = true;
        [SerializeField] private bool schedulingSuspended;

        private readonly List<RegisteredCameraRig> _rigs = new List<RegisteredCameraRig>();

        private CameraRig _activeRig;
        private CinemachineVirtualCameraBase _activeVirtualCamera;
        private int _nextSequence;
        private bool _batchApplying;

        public UnityEngine.Camera OutputCamera => brain != null ? brain.OutputCamera : null;

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
            if (_batchApplying) return;
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

        [Serializable]
        private sealed class CameraMapping
        {
            public int cameraId;
            public string modeId;
        }

        [Serializable]
        private sealed class RigMapping
        {
            public int rigId;
            public CameraRig rig;
            public CameraMapping[] cameras = Array.Empty<CameraMapping>();
        }

        public bool ApplyRequest(int rigId, bool active, int priority,
            int cameraId, bool schedulingEnabled, out CoCoDiagnostic diagnostic)
        {
            if (!ValidateMappings(out diagnostic)) return false;
            var mapping = FindMapping(rigId);
            var cameraMapping = FindCamera(mapping, cameraId);
            if (mapping == null || FindRegistered(mapping.rig) == null || cameraMapping == null)
                return Reject("Camera request refers to an unregistered Rig or unknown CameraId.", out diagnostic);
            _batchApplying = true;
            try
            {
                mapping.rig.SetActive(active);
                mapping.rig.SetPriority(priority);
                mapping.rig.SetMode(cameraMapping.modeId);
                SetSchedulingSuspended(!schedulingEnabled);
            }
            finally { _batchApplying = false; }
            ApplyCurrentRig();
            return true;
        }

        public bool GetState(out CamSnapshotState snapshot, out CoCoDiagnostic diagnostic)
        {
            snapshot = default;
            if (!ValidateMappings(out diagnostic)) return false;
            var output = OutputCamera;
            if (output == null || _rigs.Count > CamSnapshotState.RigCapacity)
                return Reject("Camera capture needs an output Camera and at most 64 registered Rigs.", out diagnostic);
            Vector3 position = output.transform.position;
            Quaternion rotation = output.transform.rotation;
            var result = new CamSnapshotState
            {
                PositionX = position.x, PositionY = position.y, PositionZ = position.z,
                RotationX = rotation.x, RotationY = rotation.y, RotationZ = rotation.z, RotationW = rotation.w,
                SchedulingEnabled = !schedulingSuspended
            };
            if (!ValidPose(position, rotation))
                return Reject("Camera capture found an invalid output pose.", out diagnostic);
            // The list retains registration order; SetState restores this relative order.
            foreach (var entry in _rigs)
            {
                RigMapping mapping = null;
                foreach (var candidate in rigMappings)
                    if (candidate.rig == entry.Rig) { mapping = candidate; break; }
                if (mapping == null || entry.Rig == null)
                    return Reject("A registered Rig has no stable numeric mapping.", out diagnostic);
                CameraMapping cameraMapping = null;
                foreach (var candidate in mapping.cameras)
                    if (string.Equals(candidate.modeId, entry.Rig.CurrentModeId, StringComparison.Ordinal))
                    { cameraMapping = candidate; break; }
                if (cameraMapping == null || !result.TryAddRig(new CamRigState
                    {
                        RigId = mapping.rigId, Active = entry.Rig.IsActive,
                        Priority = entry.Rig.Priority, CameraId = cameraMapping.cameraId
                    }))
                    return Reject("A registered Rig has no current CameraId or exceeds snapshot capacity.", out diagnostic);
            }
            snapshot = result;
            return true;
        }

        public bool SetState(in CamSnapshotState snapshot, out CoCoDiagnostic diagnostic)
        {
            if (!ValidateMappings(out diagnostic)) return false;
            var output = OutputCamera;
            // Copy once: fixed-buffer accessors on an in argument otherwise copy per access.
            var saved = snapshot;
            var position = new Vector3(saved.PositionX, saved.PositionY, saved.PositionZ);
            var rotation = new Quaternion(saved.RotationX, saved.RotationY, saved.RotationZ, saved.RotationW);
            if (output == null || !saved.HasValidHeader || saved.RigCount != _rigs.Count ||
                !ValidPose(position, rotation))
                return Reject("Camera restore requires a valid pose and the same registered Rig set.", out diagnostic);
            var targets = new (RegisteredCameraRig Entry, CameraMapping Camera, CamRigState State)[saved.RigCount];
            for (int i = 0; i < targets.Length; i++)
            {
                if (!saved.TryGetRig(i, out var state))
                    return Reject("Camera snapshot contains an invalid Rig record.", out diagnostic);
                var mapping = FindMapping(state.RigId);
                var entry = mapping == null ? null : FindRegistered(mapping.rig);
                var cameraMapping = FindCamera(mapping, state.CameraId);
                if (entry == null || cameraMapping == null)
                    return Reject("Camera snapshot refers to a missing Rig or CameraId.", out diagnostic);
                for (int previous = 0; previous < i; previous++)
                    if (targets[previous].Entry == entry)
                        return Reject("Camera snapshot contains a duplicate RigId.", out diagnostic);
                targets[i] = (entry, cameraMapping, state);
            }
            // All records have been resolved before changing any engine state.
            _batchApplying = true;
            try
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var target = targets[i];
                    target.Entry.Rig.SetActive(target.State.Active);
                    target.Entry.Rig.SetPriority(target.State.Priority);
                    target.Entry.Rig.SetMode(target.Camera.modeId);
                    target.Entry.Sequence = i;
                }
                _rigs.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
                _nextSequence = _rigs.Count;
                SetSchedulingSuspended(!saved.SchedulingEnabled);
            }
            finally { _batchApplying = false; }
            ApplyCurrentRig();
            brain.ResetState();
            rotation = rotation.normalized;
            if (_activeVirtualCamera != null)
                _activeVirtualCamera.ForceCameraPosition(position, rotation);
            output.transform.SetPositionAndRotation(position, rotation);
            return true;
        }

        private bool ValidateMappings(out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (rigMappings == null)
                return Reject("Camera numeric mappings are missing.", out diagnostic);
            for (int i = 0; i < rigMappings.Length; i++)
            {
                var mapping = rigMappings[i];
                if (mapping == null || mapping.rig == null || mapping.cameras == null)
                    return Reject("Camera mapping requires a live Rig and its CameraId table.", out diagnostic);
                for (int previous = 0; previous < i; previous++)
                    if (rigMappings[previous].rigId == mapping.rigId || rigMappings[previous].rig == mapping.rig)
                        return Reject("Camera mappings require unique RigIds and Rig references.", out diagnostic);
                for (int c = 0; c < mapping.cameras.Length; c++)
                {
                    var cameraMapping = mapping.cameras[c];
                    if (cameraMapping == null || string.IsNullOrWhiteSpace(cameraMapping.modeId) ||
                        mapping.rig.GetCamera(cameraMapping.modeId) == null)
                        return Reject("CameraId must map to an existing Rig Mode and Vcam.", out diagnostic);
                    for (int previous = 0; previous < c; previous++)
                        if (mapping.cameras[previous].cameraId == cameraMapping.cameraId ||
                            string.Equals(mapping.cameras[previous].modeId, cameraMapping.modeId, StringComparison.Ordinal))
                            return Reject("A Rig requires unique CameraIds and ModeIds.", out diagnostic);
                }
            }
            return true;
        }

        private RigMapping FindMapping(int rigId)
        {
            foreach (var mapping in rigMappings)
                if (mapping.rigId == rigId) return mapping;
            return null;
        }

        private static CameraMapping FindCamera(RigMapping mapping, int cameraId)
        {
            if (mapping != null)
                foreach (var cameraMapping in mapping.cameras)
                    if (cameraMapping.cameraId == cameraId) return cameraMapping;
            return null;
        }

        private RegisteredCameraRig FindRegistered(CameraRig rig)
        {
            foreach (var entry in _rigs)
                if (entry.Rig != null && entry.Rig == rig) return entry;
            return null;
        }

        private static bool ValidPose(Vector3 position, Quaternion rotation)
        {
            float length = Quaternion.Dot(rotation, rotation);
            return Finite(position.x) && Finite(position.y) && Finite(position.z) &&
                   Finite(length) && length > 0.000001f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Reject(string message, out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operator,
                CoCoDiagnosticCode.OperatorExecutionFailed, message);
            return false;
        }

        private sealed class RegisteredCameraRig
        {
            public RegisteredCameraRig(CameraRig rig, int sequence)
            {
                Rig = rig;
                Sequence = sequence;
            }

            public CameraRig Rig { get; }
            public int Sequence { get; set; }
        }
    }
}

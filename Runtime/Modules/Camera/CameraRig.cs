using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [Serializable]
    public class CameraRigCameraEntry
    {
        [SerializeField] private string modeId;
        [SerializeField] private CinemachineVirtualCameraBase camera;

        public CameraRigCameraEntry() { }

        public CameraRigCameraEntry(
            string modeId,
            CinemachineVirtualCameraBase camera)
        {
            this.modeId = modeId;
            this.camera = camera;
        }

        public string ModeId => modeId;
        public CinemachineVirtualCameraBase Camera => camera;

        internal void SetCamera(CinemachineVirtualCameraBase virtualCamera)
        {
            camera = virtualCamera;
        }
    }

    public class CameraRig : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string rigId;

        [Header("Scheduling")]
        [SerializeField] private int priority = 10;
        [SerializeField] private bool isActive = true;
        [SerializeField] private bool registerOnEnable = true;
        [SerializeField] private MonoBehaviour cameraDirector;

        [Header("Current Mode")]
        [SerializeField] private string currentModeId;

        [Header("Virtual Cameras")]
        [SerializeField] private List<CameraRigCameraEntry> cameras =
            new List<CameraRigCameraEntry>();

        private ICameraDirector _director;
        private IDisposable _directorWait;
        private bool _isRegistered;

        public string RigId => string.IsNullOrWhiteSpace(rigId) ? name : rigId;
        public int Priority => priority;
        public bool IsActive => isActive;
        public bool IsAvailable => isActive && isActiveAndEnabled && CurrentCamera != null;
        public bool IsRegistered => _isRegistered;
        public string CurrentModeId => currentModeId;
        public CinemachineVirtualCameraBase CurrentCamera => ResolveCamera(currentModeId);

        private List<CameraRigCameraEntry> Cameras
        {
            get
            {
                if (cameras == null)
                {
                    cameras = new List<CameraRigCameraEntry>();
                }

                return cameras;
            }
        }

        public void SetRigId(string id)
        {
            rigId = id;
            NotifyRigChanged();
        }

        public void SetPriority(int value)
        {
            if (priority == value) return;

            priority = value;
            NotifyRigChanged();
        }

        public void SetActive(bool active)
        {
            if (isActive == active) return;

            isActive = active;
            NotifyRigChanged();
        }

        public void SetMode(string modeId)
        {
            var nextModeId = string.IsNullOrWhiteSpace(modeId)
                ? string.Empty
                : modeId;
            if (string.Equals(currentModeId, nextModeId, StringComparison.Ordinal)) return;

            var previousCamera = CurrentCamera;
            currentModeId = nextModeId;
            if (previousCamera != CurrentCamera && previousCamera != null)
            {
                previousCamera.Priority = 0;
            }

            NotifyRigChanged();
        }

        public void SetCamera(
            string modeId,
            CinemachineVirtualCameraBase virtualCamera)
        {
            if (string.IsNullOrWhiteSpace(modeId)) return;

            var previousCamera = ResolveCamera(modeId);
            var entry = FindCameraEntry(modeId);
            if (entry != null)
            {
                entry.SetCamera(virtualCamera);
            }
            else
            {
                Cameras.Add(new CameraRigCameraEntry(modeId, virtualCamera));
            }

            if (previousCamera != virtualCamera && previousCamera != null)
            {
                previousCamera.Priority = 0;
            }

            NotifyRigChanged();
        }

        public CinemachineVirtualCameraBase GetCamera(string modeId)
        {
            return ResolveCamera(modeId);
        }

        public bool TryGetCamera(
            string modeId,
            out CinemachineVirtualCameraBase virtualCamera)
        {
            virtualCamera = ResolveCamera(modeId);
            return virtualCamera != null;
        }

        public void SetCameraDirector(MonoBehaviour director)
        {
            bool wasRegistered = _isRegistered;
            if (wasRegistered)
            {
                UnregisterRig();
            }
            else
            {
                _directorWait?.Dispose();
                _directorWait = null;
            }

            cameraDirector = director;
            _director = null;

            if (wasRegistered || isActiveAndEnabled && registerOnEnable)
            {
                RegisterRig();
            }
        }

        public void RegisterRig()
        {
            if (_isRegistered)
            {
                NotifyRigChanged();
                return;
            }

            var director = ResolveDirector();
            if (director == null)
            {
                WaitForDirector();
                return;
            }

            director.RegisterRig(this);
            _director = director;
            _isRegistered = true;
        }

        public void UnregisterRig()
        {
            _directorWait?.Dispose();
            _directorWait = null;

            var director = ResolveDirector();
            if (_isRegistered && director != null)
            {
                director.UnregisterRig(this);
            }

            ClearRuntimePriorities();
            _isRegistered = false;
        }

        internal void ApplyRuntimePriority()
        {
            ClearRuntimePriorities();
            var currentCamera = CurrentCamera;
            if (currentCamera != null)
            {
                currentCamera.Priority = priority;
            }
        }

        internal void ClearRuntimePriorities()
        {
            foreach (var entry in Cameras)
            {
                ClearCameraPriority(entry?.Camera);
            }
        }

        private void OnEnable()
        {
            if (registerOnEnable)
            {
                RegisterRig();
            }
        }

        private void OnDisable()
        {
            UnregisterRig();
        }

        private void NotifyRigChanged()
        {
            if (!_isRegistered) return;

            var director = ResolveDirector();
            director?.RefreshRig(this);
        }

        private CinemachineVirtualCameraBase ResolveCamera(string modeId)
        {
            var entry = FindCameraEntry(modeId);
            return entry?.Camera;
        }

        private CameraRigCameraEntry FindCameraEntry(string modeId)
        {
            if (string.IsNullOrWhiteSpace(modeId)) return null;

            foreach (var entry in Cameras)
            {
                if (entry != null &&
                    string.Equals(entry.ModeId, modeId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private ICameraDirector ResolveDirector()
        {
            if (IsDirectorAvailable(_director)) return _director;

            _director = null;

            if (cameraDirector is ICameraDirector explicitDirector &&
                IsDirectorAvailable(explicitDirector))
            {
                _director = explicitDirector;
                return _director;
            }

            if (CoCoServices.TryGet(out ICameraDirector serviceDirector) &&
                IsDirectorAvailable(serviceDirector))
            {
                _director = serviceDirector;
            }

            return _director;
        }

        private void WaitForDirector()
        {
            if (cameraDirector != null || _directorWait != null) return;

            _directorWait = CoCoServices.WaitFor<ICameraDirector>(director =>
            {
                if (!IsDirectorAvailable(director)) return;

                _director = director;
                _directorWait = null;

                if (isActiveAndEnabled && !_isRegistered)
                {
                    RegisterRig();
                }
            });
        }

        private static void ClearCameraPriority(CinemachineVirtualCameraBase virtualCamera)
        {
            if (virtualCamera != null)
            {
                virtualCamera.Priority = 0;
            }
        }

        private static bool IsDirectorAvailable(ICameraDirector director)
        {
            if (director == null) return false;

            if (director is Behaviour behaviour)
            {
                return behaviour != null && behaviour.isActiveAndEnabled;
            }

            if (director is UnityEngine.Object unityObject)
            {
                return unityObject != null;
            }

            return true;
        }
    }
}

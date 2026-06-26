using System;
using CoCoFlow.Runtime.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public enum CameraRigMode
    {
        Free,
        Aim,
        Lock,
        Spectate,
        Focus,
        Custom
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

        [Header("Mode")]
        [SerializeField] private CameraRigMode mode = CameraRigMode.Free;

        [Header("Virtual Cameras")]
        [SerializeField] private CinemachineVirtualCameraBase freeCamera;
        [SerializeField] private CinemachineVirtualCameraBase aimCamera;
        [SerializeField] private CinemachineVirtualCameraBase lockCamera;
        [SerializeField] private CinemachineVirtualCameraBase spectateCamera;
        [SerializeField] private CinemachineVirtualCameraBase focusCamera;
        [SerializeField] private CinemachineVirtualCameraBase customCamera;

        private ICameraDirector _director;
        private IDisposable _directorWait;
        private bool _isRegistered;

        public string RigId => string.IsNullOrWhiteSpace(rigId) ? name : rigId;
        public int Priority => priority;
        public bool IsActive => isActive;
        public bool IsAvailable => isActive && isActiveAndEnabled && CurrentCamera != null;
        public bool IsRegistered => _isRegistered;
        public CameraRigMode Mode => mode;
        public CinemachineVirtualCameraBase CurrentCamera => ResolveCamera(mode);

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

        public void SetMode(CameraRigMode value)
        {
            if (mode == value) return;

            var previousCamera = CurrentCamera;
            mode = value;
            if (previousCamera != CurrentCamera && previousCamera != null)
            {
                previousCamera.Priority = 0;
            }

            NotifyRigChanged();
        }

        public void SetCamera(
            CameraRigMode targetMode,
            CinemachineVirtualCameraBase virtualCamera)
        {
            var previousCamera = ResolveCamera(targetMode);
            switch (targetMode)
            {
                case CameraRigMode.Free:
                    freeCamera = virtualCamera;
                    break;
                case CameraRigMode.Aim:
                    aimCamera = virtualCamera;
                    break;
                case CameraRigMode.Lock:
                    lockCamera = virtualCamera;
                    break;
                case CameraRigMode.Spectate:
                    spectateCamera = virtualCamera;
                    break;
                case CameraRigMode.Focus:
                    focusCamera = virtualCamera;
                    break;
                case CameraRigMode.Custom:
                    customCamera = virtualCamera;
                    break;
                default:
                    return;
            }

            if (previousCamera != virtualCamera && previousCamera != null)
            {
                previousCamera.Priority = 0;
            }

            NotifyRigChanged();
        }

        public CinemachineVirtualCameraBase GetCamera(CameraRigMode targetMode)
        {
            return ResolveCamera(targetMode);
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
            ClearCameraPriority(freeCamera);
            ClearCameraPriority(aimCamera);
            ClearCameraPriority(lockCamera);
            ClearCameraPriority(spectateCamera);
            ClearCameraPriority(focusCamera);
            ClearCameraPriority(customCamera);
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

        private CinemachineVirtualCameraBase ResolveCamera(CameraRigMode targetMode)
        {
            switch (targetMode)
            {
                case CameraRigMode.Free:
                    return freeCamera;
                case CameraRigMode.Aim:
                    return aimCamera;
                case CameraRigMode.Lock:
                    return lockCamera;
                case CameraRigMode.Spectate:
                    return spectateCamera;
                case CameraRigMode.Focus:
                    return focusCamera;
                case CameraRigMode.Custom:
                    return customCamera;
                default:
                    return null;
            }
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

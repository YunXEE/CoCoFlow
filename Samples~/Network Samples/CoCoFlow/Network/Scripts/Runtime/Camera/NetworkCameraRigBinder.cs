using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Camera;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.NetworkSamples
{
    public class NetworkCameraRigBinder : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private MonoBehaviour cameraDirector;

        [Header("Authority")]
        [SerializeField] private bool hasLocalCameraAuthority;
        [SerializeField] private bool bindOnEnable = true;

        private ICameraDirector _director;
        private IDisposable _directorWait;
        private bool _isBound;

        public bool HasLocalCameraAuthority => hasLocalCameraAuthority;

        public void SetLocalCameraAuthority(bool hasAuthority)
        {
            if (hasLocalCameraAuthority == hasAuthority)
            {
                if (!isActiveAndEnabled) return;

                if (hasLocalCameraAuthority && !_isBound)
                {
                    ActivateRig();
                }
                else if (!hasLocalCameraAuthority && _isBound)
                {
                    DeactivateRig();
                }

                return;
            }

            hasLocalCameraAuthority = hasAuthority;
            if (!isActiveAndEnabled) return;

            if (hasLocalCameraAuthority)
            {
                ActivateRig();
            }
            else
            {
                DeactivateRig();
            }
        }

        public void SetCameraRig(CameraRig rig)
        {
            bool wasBound = _isBound;
            if (wasBound)
            {
                DeactivateRig();
            }

            cameraRig = rig;

            if (wasBound && hasLocalCameraAuthority)
            {
                ActivateRig();
            }
        }

        public void ActivateRig()
        {
            var rig = ResolveRig();
            var director = ResolveDirector();
            if (rig == null) return;

            if (director == null)
            {
                WaitForDirector();
                return;
            }

            if (cameraDirector != null)
            {
                rig.SetCameraDirector(cameraDirector);
            }

            rig.RegisterRig();
            rig.SetActive(true);
            director.RefreshRig(rig);
            _isBound = true;
        }

        public void DeactivateRig()
        {
            var rig = ResolveRig();
            var director = ResolveDirector();
            if (rig != null && director != null)
            {
                rig.SetActive(false);
                director.RefreshRig(rig);
            }

            _isBound = false;
        }

        private void OnEnable()
        {
            if (bindOnEnable && hasLocalCameraAuthority)
            {
                ActivateRig();
            }
        }

        private void OnDisable()
        {
            _directorWait?.Dispose();
            _directorWait = null;

            if (_isBound)
            {
                DeactivateRig();
            }
        }

        private CameraRig ResolveRig()
        {
            if (cameraRig != null) return cameraRig;

            cameraRig = GetComponent<CameraRig>();
            return cameraRig;
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

                if (isActiveAndEnabled && hasLocalCameraAuthority && !_isBound)
                {
                    ActivateRig();
                }
            });
        }

        private void Reset()
        {
            cameraRig = GetComponent<CameraRig>();
        }

        private void OnValidate()
        {
            if (ReferenceEquals(cameraDirector, this))
            {
                cameraDirector = null;
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

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
                    BindLocalRig();
                }
                else if (!hasLocalCameraAuthority && _isBound)
                {
                    UnbindLocalRig();
                }

                return;
            }

            hasLocalCameraAuthority = hasAuthority;
            if (!isActiveAndEnabled) return;

            if (hasLocalCameraAuthority)
            {
                BindLocalRig();
            }
            else
            {
                UnbindLocalRig();
            }
        }

        public void SetCameraRig(CameraRig rig)
        {
            bool wasBound = _isBound;
            if (wasBound)
            {
                UnbindLocalRig();
            }

            cameraRig = rig;

            if (wasBound && hasLocalCameraAuthority)
            {
                BindLocalRig();
            }
        }

        public void BindLocalRig()
        {
            var rig = ResolveRig();
            var director = ResolveDirector();
            if (rig == null) return;

            if (director == null)
            {
                WaitForDirector();
                return;
            }

            director.BindLocalRig(rig);
            _isBound = true;
        }

        public void UnbindLocalRig()
        {
            var rig = ResolveRig();
            var director = ResolveDirector();
            if (rig != null && director != null)
            {
                director.ClearLocalRig(rig);
            }

            _isBound = false;
        }

        private void OnEnable()
        {
            if (bindOnEnable && hasLocalCameraAuthority)
            {
                BindLocalRig();
            }
        }

        private void OnDisable()
        {
            _directorWait?.Dispose();
            _directorWait = null;

            if (_isBound)
            {
                UnbindLocalRig();
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
            if (_director != null) return _director;

            if (cameraDirector is ICameraDirector explicitDirector)
            {
                _director = explicitDirector;
                return _director;
            }

            if (CoCoServices.TryGet(out ICameraDirector serviceDirector))
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
                _director = director;
                _directorWait = null;

                if (isActiveAndEnabled && hasLocalCameraAuthority && !_isBound)
                {
                    BindLocalRig();
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
    }
}

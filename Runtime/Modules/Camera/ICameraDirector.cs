using System;
using Unity.Cinemachine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public readonly struct CameraRigChangedEvent
    {
        public CameraRigChangedEvent(
            CameraRig previousRig,
            CameraRig activeRig,
            CinemachineVirtualCameraBase previousVirtualCamera,
            CinemachineVirtualCameraBase activeVirtualCamera)
        {
            PreviousRig = previousRig;
            ActiveRig = activeRig;
            PreviousVirtualCamera = previousVirtualCamera;
            ActiveVirtualCamera = activeVirtualCamera;
        }

        public CameraRig PreviousRig { get; }
        public CameraRig ActiveRig { get; }
        public CinemachineVirtualCameraBase PreviousVirtualCamera { get; }
        public CinemachineVirtualCameraBase ActiveVirtualCamera { get; }
    }

    public interface ICameraDirector
    {
        CameraRig ActiveRig { get; }
        CinemachineVirtualCameraBase ActiveVirtualCamera { get; }
        bool IsSchedulingSuspended { get; }

        event Action<CameraRigChangedEvent> ActiveRigChanged;

        void RegisterRig(CameraRig rig);
        void UnregisterRig(CameraRig rig);
        void RefreshRig(CameraRig rig);
        void SetRigActive(CameraRig rig, bool active);
        void SetRigPriority(CameraRig rig, int priority);
        void SetSchedulingSuspended(bool suspended);
    }
}

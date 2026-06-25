namespace CoCoFlow.Runtime.Modules.Camera
{
    public interface ICameraDirector
    {
        string ActiveProfileId { get; }
        CameraRig LocalRig { get; }
        CameraRig ActiveRig { get; }

        int Request(CameraModeRequest request);
        int Request(
            string profileId,
            CameraRig subjectRig = null,
            UnityEngine.Transform focusTarget = null,
            object owner = null,
            int priority = 0,
            float duration = 0f);
        bool Release(int requestId);
        int ReleaseOwner(object owner);
        void BindLocalRig(CameraRig rig);
        void ClearLocalRig(CameraRig rig);
        void SetGameplayRequestsSuspended(bool suspended);
    }
}

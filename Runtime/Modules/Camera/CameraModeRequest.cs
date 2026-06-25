using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public readonly struct CameraModeRequest
    {
        public CameraModeRequest(
            string profileId,
            CameraRig subjectRig = null,
            Transform focusTarget = null,
            object owner = null,
            int priority = 0,
            float duration = 0f)
        {
            ProfileId = string.IsNullOrWhiteSpace(profileId)
                ? CameraProfileKeys.Default
                : profileId;
            SubjectRig = subjectRig;
            FocusTarget = focusTarget;
            Owner = owner;
            Priority = priority;
            Duration = Mathf.Max(0f, duration);
        }

        public string ProfileId { get; }
        public CameraRig SubjectRig { get; }
        public Transform FocusTarget { get; }
        public object Owner { get; }
        public int Priority { get; }
        public float Duration { get; }
        public bool HasDuration => Duration > 0f;
    }
}

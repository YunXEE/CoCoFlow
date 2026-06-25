using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    public class CameraDirector : MonoBehaviour, ICameraDirector
    {
        [Header("Profiles")]
        [SerializeField] private string defaultProfileId = CameraProfileKeys.Default;
        [SerializeField] private List<CameraProfileEntry> profiles =
            new List<CameraProfileEntry>();

        [Header("Runtime Binding")]
        [SerializeField] private CameraRig defaultRig;
        [SerializeField] private bool registerAsService = true;
        [SerializeField] private bool gameplayRequestsSuspended;

        private readonly Dictionary<string, CameraProfileEntry> _profilesById =
            new Dictionary<string, CameraProfileEntry>(System.StringComparer.Ordinal);
        private readonly List<ActiveRequest> _requests = new List<ActiveRequest>();

        private CameraRig _localRig;
        private CameraRig _activeRig;
        private string _activeProfileId = string.Empty;
        private int _nextRequestId = 1;
        private int _nextSequence;

        public string ActiveProfileId => _activeProfileId;
        public CameraRig LocalRig => _localRig;
        public CameraRig ActiveRig => _activeRig;

        public int Request(CameraModeRequest request)
        {
            int requestId = _nextRequestId++;
            _requests.Add(new ActiveRequest(requestId, request, _nextSequence++));
            ApplyCurrentRequest();
            return requestId;
        }

        public int Request(
            string profileId,
            CameraRig subjectRig = null,
            Transform focusTarget = null,
            object owner = null,
            int priority = 0,
            float duration = 0f)
        {
            return Request(new CameraModeRequest(
                profileId,
                subjectRig,
                focusTarget,
                owner,
                priority,
                duration));
        }

        public bool Release(int requestId)
        {
            for (int i = _requests.Count - 1; i >= 0; i--)
            {
                if (_requests[i].Id != requestId) continue;

                _requests.RemoveAt(i);
                ApplyCurrentRequest();
                return true;
            }

            return false;
        }

        public int ReleaseOwner(object owner)
        {
            if (owner == null) return 0;

            int removed = 0;
            for (int i = _requests.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_requests[i].Request.Owner, owner)) continue;

                _requests.RemoveAt(i);
                removed++;
            }

            if (removed > 0)
            {
                ApplyCurrentRequest();
            }

            return removed;
        }

        public void BindLocalRig(CameraRig rig)
        {
            _localRig = rig;
            ApplyCurrentRequest();
        }

        public void ClearLocalRig(CameraRig rig)
        {
            if (rig != null && _localRig != null && !ReferenceEquals(_localRig, rig)) return;

            _localRig = null;
            ApplyCurrentRequest();
        }

        public void SetGameplayRequestsSuspended(bool suspended)
        {
            if (gameplayRequestsSuspended == suspended) return;

            gameplayRequestsSuspended = suspended;
            ApplyCurrentRequest();
        }

        public void SetProfileEntries(IEnumerable<CameraProfileEntry> profileEntries)
        {
            profiles.Clear();
            if (profileEntries != null)
            {
                foreach (var entry in profileEntries)
                {
                    if (entry != null)
                    {
                        profiles.Add(entry);
                    }
                }
            }

            RebuildProfileLookup();
            ApplyCurrentRequest();
        }

        public void SetDefaultRig(CameraRig rig)
        {
            defaultRig = rig;
            ApplyCurrentRequest();
        }

        private void Awake()
        {
            RebuildProfileLookup();
            ApplyCurrentRequest();
        }

        private void OnEnable()
        {
            if (registerAsService)
            {
                CoCoServices.Register<ICameraDirector>(this);
            }
        }

        private void OnDisable()
        {
            if (registerAsService)
            {
                CoCoServices.Unregister<ICameraDirector>(this);
            }
        }

        private void Update()
        {
            if (RemoveExpiredRequests())
            {
                ApplyCurrentRequest();
            }
        }

        private bool RemoveExpiredRequests()
        {
            bool removed = false;
            float currentTime = Time.time;
            for (int i = _requests.Count - 1; i >= 0; i--)
            {
                if (!_requests[i].IsExpired(currentTime)) continue;

                _requests.RemoveAt(i);
                removed = true;
            }

            return removed;
        }

        private void RebuildProfileLookup()
        {
            _profilesById.Clear();
            foreach (var entry in profiles)
            {
                if (entry == null || !entry.IsUsable) continue;

                if (!_profilesById.TryAdd(entry.ProfileId, entry))
                {
                    CoCoLog.Warning($"Duplicate Camera profile ignored: {entry.ProfileId}");
                }
            }
        }

        private void ApplyCurrentRequest()
        {
            ResetProfilePriorities();

            if (gameplayRequestsSuspended)
            {
                _activeProfileId = string.Empty;
                _activeRig = null;
                return;
            }

            var activeRequest = SelectActiveRequest();
            var request = activeRequest?.Request ??
                new CameraModeRequest(defaultProfileId, ResolveFallbackRig());

            var profile = ResolveProfile(request.ProfileId);
            if (profile == null)
            {
                _activeProfileId = string.Empty;
                _activeRig = null;
                return;
            }

            var subjectRig = request.SubjectRig ?? ResolveFallbackRig();

            _activeProfileId = profile.ProfileId;
            _activeRig = subjectRig;

            profile.ApplyActive(subjectRig, request.FocusTarget);
        }

        private CameraRig ResolveFallbackRig()
        {
            return _localRig != null ? _localRig : defaultRig;
        }

        private CameraProfileEntry ResolveProfile(string profileId)
        {
            if (!string.IsNullOrWhiteSpace(profileId) &&
                _profilesById.TryGetValue(profileId, out var requestedProfile))
            {
                return requestedProfile;
            }

            if (!string.IsNullOrWhiteSpace(defaultProfileId) &&
                _profilesById.TryGetValue(defaultProfileId, out var defaultProfile))
            {
                return defaultProfile;
            }

            foreach (var entry in profiles)
            {
                if (entry is { IsUsable: true })
                {
                    return entry;
                }
            }

            return null;
        }

        private ActiveRequest SelectActiveRequest()
        {
            ActiveRequest selected = null;
            foreach (var request in _requests)
            {
                if (selected == null ||
                    request.Request.Priority > selected.Request.Priority ||
                    request.Request.Priority == selected.Request.Priority &&
                    request.Sequence > selected.Sequence)
                {
                    selected = request;
                }
            }

            return selected;
        }

        private void ResetProfilePriorities()
        {
            foreach (var entry in profiles)
            {
                entry?.ApplyStandbyPriority();
            }
        }

        private sealed class ActiveRequest
        {
            private readonly float _expiresAt;

            public ActiveRequest(
                int id,
                CameraModeRequest request,
                int sequence)
            {
                Id = id;
                Request = request;
                Sequence = sequence;
                _expiresAt = request.HasDuration
                    ? Time.time + request.Duration
                    : float.PositiveInfinity;
            }

            public int Id { get; }
            public CameraModeRequest Request { get; }
            public int Sequence { get; }

            public bool IsExpired(float currentTime)
            {
                return currentTime >= _expiresAt;
            }
        }
    }
}

using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    public enum CharacterNavigationMode
    {
        None = 0,
        Patrol = 1,
        Chase = 2,
        Combat = 3,
        ReturnToRoute = 4
    }

    [Serializable]
    public class CharacterNavigationContext : ICoCoContext
    {
        [SerializeField] private CharacterNavigationMode mode = CharacterNavigationMode.None;
        [SerializeField] private string controlOwner = string.Empty;
        [SerializeField] private int controlPriority;
        [SerializeField] private Vector3 destination;
        [SerializeField] private Vector3 desiredVelocity;
        [SerializeField] private float desiredSpeed;
        [SerializeField] private float stoppingDistance = 0.1f;
        [SerializeField] private bool hasDestination;
        [SerializeField] private bool hasDesiredVelocity;
        [SerializeField] private Vector3 routePosition;
        [SerializeField] private float routeProgress;
        [SerializeField] private bool reverseRoute;
        [SerializeField] private bool hasRoute;
        [SerializeField] private Vector3 warpPosition;
        [SerializeField] private bool hasWarpRequest;

        #region Public API

        public CharacterNavigationMode Mode => mode;
        public string ControlOwner => controlOwner;
        public int ControlPriority => controlPriority;
        public Vector3 Destination => destination;
        public Vector3 DesiredVelocity => desiredVelocity;
        public float DesiredSpeed => desiredSpeed;
        public float StoppingDistance => stoppingDistance;
        public bool HasDestination => hasDestination;
        public bool HasDesiredVelocity => hasDesiredVelocity;
        public Vector3 RoutePosition => routePosition;
        public float RouteProgress => routeProgress;
        public bool ReverseRoute => reverseRoute;
        public bool HasRoute => hasRoute;
        public Vector3 WarpPosition => warpPosition;
        public bool HasWarpRequest => hasWarpRequest;
        public bool HasAnyControl => !string.IsNullOrEmpty(controlOwner);

        public bool HasControl(string owner)
        {
            return !string.IsNullOrEmpty(owner) &&
                   string.Equals(controlOwner, owner, StringComparison.Ordinal);
        }

        public bool TryClaimControl(string owner, int priority = 0, bool force = false)
        {
            if (string.IsNullOrEmpty(owner)) return false;

            if (force ||
                string.IsNullOrEmpty(controlOwner) ||
                string.Equals(controlOwner, owner, StringComparison.Ordinal) ||
                priority >= controlPriority)
            {
                controlOwner = owner;
                controlPriority = priority;
                return true;
            }

            return false;
        }

        public bool ReleaseControl(string owner)
        {
            if (!HasControl(owner)) return false;

            ClearControl();
            return true;
        }

        public void ClearControl()
        {
            controlOwner = string.Empty;
            controlPriority = 0;
        }

        public void SetMode(CharacterNavigationMode nextMode)
        {
            mode = nextMode;
        }

        public void SetDestination(
            Vector3 nextDestination,
            float speed = 0f,
            float stopDistance = 0.1f,
            CharacterNavigationMode nextMode = CharacterNavigationMode.None)
        {
            destination = nextDestination;
            desiredSpeed = Mathf.Max(0f, speed);
            stoppingDistance = Mathf.Max(0f, stopDistance);
            hasDestination = true;
            if (nextMode != CharacterNavigationMode.None)
            {
                mode = nextMode;
            }
        }

        public void ClearDestination()
        {
            destination = Vector3.zero;
            desiredSpeed = 0f;
            hasDestination = false;
        }

        public void SetDesiredVelocity(Vector3 velocity)
        {
            desiredVelocity = velocity;
            hasDesiredVelocity = velocity.sqrMagnitude > 0.0001f;
        }

        public void ClearDesiredVelocity()
        {
            desiredVelocity = Vector3.zero;
            hasDesiredVelocity = false;
        }

        public void SetRoute(Vector3 position, float progress, bool isReversed)
        {
            routePosition = position;
            routeProgress = Mathf.Repeat(progress, 1f);
            reverseRoute = isReversed;
            hasRoute = true;
        }

        public void ClearRoute()
        {
            routePosition = Vector3.zero;
            routeProgress = 0f;
            reverseRoute = false;
            hasRoute = false;
        }

        public void RequestWarp(Vector3 position)
        {
            warpPosition = position;
            hasWarpRequest = true;
        }

        public bool TryConsumeWarpRequest(out Vector3 position)
        {
            position = warpPosition;
            if (!hasWarpRequest) return false;

            hasWarpRequest = false;
            warpPosition = Vector3.zero;
            return true;
        }

        public void Clear()
        {
            mode = CharacterNavigationMode.None;
            ClearControl();
            ClearDestination();
            ClearDesiredVelocity();
            ClearRoute();
            hasWarpRequest = false;
            warpPosition = Vector3.zero;
        }

        #endregion
    }

    public class CharacterNavigation : MonoBehaviour, ICoCoContextProvider<CharacterNavigationContext>
    {
        [Header("Context")]
        [SerializeField] private CharacterNavigationContext context = new CharacterNavigationContext();

        #region Public API

        public CharacterNavigationContext Context => context;

        public bool TryClaimControl(string owner, int priority = 0, bool force = false)
        {
            return context.TryClaimControl(owner, priority, force);
        }

        public bool ReleaseControl(string owner)
        {
            return context.ReleaseControl(owner);
        }

        public void SetDestination(
            Vector3 destination,
            float speed = 0f,
            float stoppingDistance = 0.1f,
            CharacterNavigationMode mode = CharacterNavigationMode.None)
        {
            context.SetDestination(destination, speed, stoppingDistance, mode);
        }

        public void RequestWarp(Vector3 position)
        {
            context.RequestWarp(position);
        }

        public void ResetContext()
        {
            context = new CharacterNavigationContext();
        }

        #endregion
    }
}

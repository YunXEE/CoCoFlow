using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation.Rig
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnimRigFootDriver))]
    public class AnimRigCharacterController : MonoBehaviour
    {
        [Header("Profile")]
        [SerializeField] private AnimRigCharacterProfile profile;

        [Header("Components")]
        [SerializeField] private Animator animator;
        [SerializeField] private AnimRigFootDriver footRigDriver;

        [Header("Runtime")]
        [SerializeField] private bool footRigEnabled;
        [SerializeField] private AnimRigFootLockMode footLockMode;

        private AnimRigCharacterProfile _runtimeDefaultProfile;
        private readonly HashSet<int> _animatorFloatParameterHashes = new HashSet<int>();
        private Animator _cachedAnimator;
        private RuntimeAnimatorController _cachedRuntimeAnimatorController;

        public AnimRigCharacterProfile Profile => profile;
        public Animator RigAnimator => animator;
        public AnimRigFootDriver AnimRigFootDriver => footRigDriver;
        public bool FootRigEnabled => footRigEnabled;
        public AnimRigFootLockMode FootLockMode => footLockMode;

        public void SetFootRigEnabled(bool isEnabled)
        {
            if (footRigEnabled == isEnabled) return;

            footRigEnabled = isEnabled;
            if (!footRigEnabled)
            {
                ReleaseAllFeet();
            }
        }

        public void SetFootLockMode(AnimRigFootLockMode mode)
        {
            if (footLockMode == mode) return;

            footLockMode = mode;
            if (footLockMode == AnimRigFootLockMode.Off)
            {
                ReleaseAllFeet();
            }
        }

        public bool PlantFoot(AnimRigFootSlot foot)
        {
            ResolveComponents();
            if (footRigDriver == null) return false;

            return footRigDriver.PlantFoot(foot);
        }

        public void ReleaseFoot(AnimRigFootSlot foot)
        {
            ResolveComponents();
            footRigDriver?.ReleaseFoot(foot);
        }

        public void ReleaseAllFeet()
        {
            ResolveComponents();
            footRigDriver?.ReleaseAllFeet();
        }

        public AnimRigFootLockState GetFootLockState(AnimRigFootSlot foot)
        {
            ResolveComponents();
            return footRigDriver != null
                ? footRigDriver.GetFootLockState(foot)
                : AnimRigFootLockState.Released;
        }

        public void SetProfile(AnimRigCharacterProfile value)
        {
            profile = value;
        }

        public void SetAnimator(Animator value)
        {
            animator = value;
            InvalidateAnimatorParameterCache();
        }

        public void TickRig(float deltaTime)
        {
            ResolveComponents();
            var activeProfile = ResolveProfile();
            if (activeProfile == null || footRigDriver == null) return;

            footRigDriver.SampleFeet(activeProfile, footRigEnabled, deltaTime);
            footRigDriver.UpdateFootLocks(
                footRigEnabled ? footLockMode : AnimRigFootLockMode.Off,
                activeProfile,
                transform,
                deltaTime,
                GetPlantCurveValue(AnimRigFootSlot.Left, activeProfile),
                GetPlantCurveValue(AnimRigFootSlot.Right, activeProfile));
            footRigDriver.ApplyFootTargets(footRigEnabled);
        }

        private void Awake()
        {
            ResolveComponents();
        }

        private void LateUpdate()
        {
            TickRig(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (_runtimeDefaultProfile == null) return;

            if (Application.isPlaying)
            {
                Destroy(_runtimeDefaultProfile);
            }
            else
            {
                DestroyImmediate(_runtimeDefaultProfile);
            }

            _runtimeDefaultProfile = null;
        }

        private AnimRigCharacterProfile ResolveProfile()
        {
            if (profile != null) return profile;

            if (_runtimeDefaultProfile == null)
            {
                _runtimeDefaultProfile = AnimRigCharacterProfile.CreateRuntimeDefault();
            }

            return _runtimeDefaultProfile;
        }

        private void ResolveComponents()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
                if (animator == null)
                {
                    animator = GetComponentInChildren<Animator>(true);
                }
            }

            if (footRigDriver == null)
            {
                footRigDriver = GetComponent<AnimRigFootDriver>();
            }
        }

        private float GetPlantCurveValue(
            AnimRigFootSlot foot,
            AnimRigCharacterProfile activeProfile)
        {
            if (footLockMode != AnimRigFootLockMode.AnimationDriven ||
                !footRigEnabled ||
                animator == null ||
                activeProfile == null)
            {
                return 0f;
            }

            string curveName = foot == AnimRigFootSlot.Left
                ? activeProfile.LeftFootPlantCurve
                : activeProfile.RightFootPlantCurve;
            if (string.IsNullOrEmpty(curveName)) return 0f;

            RefreshAnimatorParameterCache();
            int curveHash = Animator.StringToHash(curveName);
            return _animatorFloatParameterHashes.Contains(curveHash)
                ? animator.GetFloat(curveHash)
                : 0f;
        }

        private void RefreshAnimatorParameterCache()
        {
            if (animator == _cachedAnimator &&
                animator != null &&
                animator.runtimeAnimatorController == _cachedRuntimeAnimatorController)
            {
                return;
            }

            _animatorFloatParameterHashes.Clear();
            _cachedAnimator = animator;
            _cachedRuntimeAnimatorController = animator != null
                ? animator.runtimeAnimatorController
                : null;

            if (animator == null) return;

            foreach (var parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Float) continue;

                _animatorFloatParameterHashes.Add(parameter.nameHash);
            }
        }

        private void InvalidateAnimatorParameterCache()
        {
            _cachedAnimator = null;
            _cachedRuntimeAnimatorController = null;
            _animatorFloatParameterHashes.Clear();
        }

        private void Reset()
        {
            ResolveComponents();
        }

        private void OnValidate()
        {
            ResolveComponents();
        }
    }
}

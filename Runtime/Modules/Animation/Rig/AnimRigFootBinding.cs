using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Animation.Rig
{
    [Serializable]
    public class AnimRigFootBinding
    {
        [SerializeField] private AnimRigFootSlot slot;
        [SerializeField] private Transform footBone;
        [SerializeField] private Transform ikTarget;
        [SerializeField] private Transform hintTarget;
        [SerializeField] private Transform raycastOrigin;

        public AnimRigFootBinding() { }

        public AnimRigFootBinding(AnimRigFootSlot slot)
        {
            this.slot = slot;
        }

        public AnimRigFootSlot Slot => slot;
        public Transform FootBone => footBone;
        public Transform IkTarget => ikTarget;
        public Transform HintTarget => hintTarget;
        public Transform RaycastOrigin => raycastOrigin;
        public bool HasTarget => ikTarget != null;

        public Transform ProbeTransform
        {
            get
            {
                if (raycastOrigin != null) return raycastOrigin;
                if (footBone != null) return footBone;
                return ikTarget;
            }
        }
    }
}

using CoCoFlow.Runtime.Modules.Animation.Rig;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.RiggingSamples
{
    public class RiggingSampleFootEventBridge : MonoBehaviour
    {
        [SerializeField] private AnimRigCharacterController rigController;

        public void PlantLeftFoot() => ResolveRigController()?.PlantFoot(AnimRigFootSlot.Left);

        public void PlantRightFoot() => ResolveRigController()?.PlantFoot(AnimRigFootSlot.Right);

        public void ReleaseLeftFoot() => ResolveRigController()?.ReleaseFoot(AnimRigFootSlot.Left);

        public void ReleaseRightFoot() => ResolveRigController()?.ReleaseFoot(AnimRigFootSlot.Right);

        public void ReleaseAllFeet() => ResolveRigController()?.ReleaseAllFeet();

        private AnimRigCharacterController ResolveRigController()
        {
            if (rigController != null) return rigController;

            rigController = GetComponent<AnimRigCharacterController>();
            if (rigController == null)
            {
                rigController = GetComponentInParent<AnimRigCharacterController>();
            }

            return rigController;
        }
    }
}

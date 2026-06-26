using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using CoCoFlow.Runtime.Modules.Animation.Rig;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.RiggingSamples
{
    public abstract class CCS_RiggingPlayer_StateBase : CoCoStateBase
    {
        protected CharacterContext CharacterContext => Controller != null ? Controller.Context as CharacterContext : null;
        protected Transform ActorTransform { get; private set; }
        protected AnimRigCharacterController RigController { get; private set; }

        public override void Init(CoCoStateController targetController)
        {
            base.Init(targetController);
            ActorTransform = targetController.transform.parent != null
                ? targetController.transform.parent
                : targetController.transform;
            RigController = ActorTransform.GetComponent<AnimRigCharacterController>();
        }

        protected void EnableGroundedFootRig()
        {
            if (RigController == null) return;

            RigController.SetFootRigEnabled(true);
            RigController.SetFootLockMode(AnimRigFootLockMode.AnimationDriven);
        }

        protected void DisableAirborneFootRig()
        {
            if (RigController == null) return;

            RigController.ReleaseAllFeet();
            RigController.SetFootRigEnabled(false);
        }

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            builder.UsesOperation<AnimRigCharacterController>("Foot IK and foot lock operation component");
        }
    }
}

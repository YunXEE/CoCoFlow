using UnityEngine;
using UnityEngine.Serialization;

namespace CoCoFlow.Runtime.Core
{
    public abstract class CoCoStateMachineBase : MonoBehaviour
    {
        // Controller injects this during registration.
        protected CoCoStateMachineController Controller;

        [FormerlySerializedAs("ubStateMachine")]
        [FormerlySerializedAs("SubStateMachine")]
        [Header("Hierarchy (Optional)")]
        [Tooltip("该状态所属的子状态机。若赋值，进入该状态时将同步进入子状态机。")]
        [SerializeField] protected CoCoStateMachineController subStateMachine;

        #region Public API

        public bool IsFinished { get; protected set; }

        /// <summary>
        /// 初始化状态，绑定所属控制器。
        /// </summary>
        public virtual void Init(CoCoStateMachineController targetController)
        {
            this.Controller = targetController;
        }

        /// <summary>
        /// 进入状态时的逻辑。
        /// </summary>
        public virtual void Enter()
        {
            IsFinished = false;
            if (subStateMachine != null)
            {
                subStateMachine.EnterStateMachine();
            }
        }

        public virtual void Enter(ICoCoContext context)
        {
            Enter();
        }

        /// <summary>
        /// 每帧更新逻辑，由 Controller.Update 调用。
        /// </summary>
        public virtual void OnStateUpdate()
        {
            if (subStateMachine != null)
            {
                subStateMachine.UpdateStateMachine();
            }
        }

        public virtual void OnStateUpdate(ICoCoContext context)
        {
            OnStateUpdate();
        }

        /// <summary>
        /// 物理帧更新逻辑，由 Controller.FixedUpdate 调用。
        /// </summary>
        public virtual void OnStateFixedUpdate()
        {
            if (subStateMachine != null)
            {
                subStateMachine.FixedUpdateStateMachine();
            }
        }

        public virtual void OnStateFixedUpdate(ICoCoContext context)
        {
            OnStateFixedUpdate();
        }

        /// <summary>
        /// 退出状态时的逻辑。
        /// </summary>
        public virtual void Exit()
        {
            if (subStateMachine != null)
            {
                subStateMachine.ExitStateMachine();
            }
        }

        public virtual void Exit(ICoCoContext context)
        {
            Exit();
        }

        #endregion
    }
}

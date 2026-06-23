using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public abstract class CoCoStateBase : MonoBehaviour
    {
        // Controller injects this during registration.
        protected CoCoStateController Controller;
        private CoCoStateDefinition _definition;

        #region Public API

        public bool IsFinished { get; protected set; }
        public CoCoStateDefinition Definition => GetDefinition();

        /// <summary>
        /// 初始化状态，绑定所属控制器。
        /// </summary>
        public virtual void Init(CoCoStateController targetController)
        {
            this.Controller = targetController;
        }

        /// <summary>
        /// 进入状态时的逻辑。
        /// </summary>
        public virtual void Enter()
        {
            IsFinished = false;
        }

        public virtual void Enter(ICoCoContext context)
        {
            Enter();
        }

        /// <summary>
        /// 每帧更新逻辑，由 Controller.Update 调用。
        /// </summary>
        public virtual void OnStateUpdate() { }

        public virtual void OnStateUpdate(ICoCoContext context)
        {
            OnStateUpdate();
        }

        /// <summary>
        /// 物理帧更新逻辑，由 Controller.FixedUpdate 调用。
        /// </summary>
        public virtual void OnStateFixedUpdate() { }

        public virtual void OnStateFixedUpdate(ICoCoContext context)
        {
            OnStateFixedUpdate();
        }

        /// <summary>
        /// 退出状态时的逻辑。
        /// </summary>
        public virtual void Exit() { }

        public virtual void Exit(ICoCoContext context)
        {
            Exit();
        }

        protected void ChangeState<T>() where T : CoCoStateBase
        {
            Controller?.ChangeStateFrom(this, typeof(T));
        }

        protected bool IfHasState<T>() where T : CoCoStateBase
        {
            return Controller != null && Controller.IfHasStateFrom(this, typeof(T));
        }

        #endregion

        #region Protected API

        protected virtual string DisplayName => GetType().Name;

        protected abstract void DefineState(CoCoStateDefinitionBuilder builder);

        #endregion

        #region Internal Logic

        private CoCoStateDefinition GetDefinition()
        {
            if (_definition != null) return _definition;

            var builder = new CoCoStateDefinitionBuilder(GetType(), DisplayName);
            DefineState(builder);
            _definition = builder.Build();
            return _definition;
        }

        #endregion
    }
}

using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public abstract class StateMachineBase: MonoBehaviour
    {
        // 依赖注入
        protected StateMachineController Controller;
        public bool IsFinished { get; protected set; }

        // 初始化
        public virtual void Init(StateMachineController targetController)
        {
            this.Controller = targetController;
        }

        public virtual void Enter() 
        { 
            IsFinished = false; 
        }
        
        public virtual void OnStateUpdate() { }
        public virtual void OnStateFixedUpdate() { }
        public virtual void Exit() { }
    }
}
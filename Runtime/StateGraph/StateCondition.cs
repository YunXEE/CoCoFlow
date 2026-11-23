using System;

namespace CoCoFlow.Runtime.StateGraph
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ConditionAttribute : Attribute
    {
    }

    public interface IEventInboxAdapter<TEvent> where TEvent : unmanaged
    {
        bool Accept(in TEvent payload);
    }

    public abstract class StateCondition
    {
        private object _runtimeOwner;

        internal bool TryClaimRuntimeOwner(object owner)
        {
            if (owner == null || _runtimeOwner != null)
            {
                return false;
            }

            _runtimeOwner = owner;
            return true;
        }

        internal void ReleaseRuntimeOwner(object owner)
        {
            if (ReferenceEquals(_runtimeOwner, owner))
            {
                _runtimeOwner = null;
            }
        }
    }
}

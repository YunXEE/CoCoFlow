using System;

namespace CoCoFlow.Runtime.Core
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ConditionAttribute : Attribute
    {
    }

    public interface ICoCoEventInboxAdapter<TEvent> where TEvent : unmanaged
    {
        bool Accept(in TEvent payload);
    }

    public abstract class CoCoStateCondition
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

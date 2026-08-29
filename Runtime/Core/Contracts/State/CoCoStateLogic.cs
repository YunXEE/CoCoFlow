namespace CoCoFlow.Runtime.Core
{
    public abstract class CoCoStateLogic
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

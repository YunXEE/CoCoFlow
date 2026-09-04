using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Persistence.Context
{
    public interface IPersistenceContextAdapter
    {
        bool CanCapture(ICoCoContext context);
        bool CanApply(PersistenceContextRecord record, ICoCoContext context);
        PersistenceContextRecord Capture(string stableEntityId, ICoCoContext context);
        void Apply(PersistenceContextRecord record, ICoCoContext context);
    }
}

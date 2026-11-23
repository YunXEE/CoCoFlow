using System;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.StateGraphPlayerMetadataFixtures
{
    public interface ICoCoPlayerMetadataGenericSection : ICoCoOperationSection
    {
        ValueTuple<CoCoPlayerMetadataNestedValue, int> Payload { get; }
    }
}

using System;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.StateGraphPlayerMetadataFixtures
{
    public interface IPlayerMetadataGenericSection : IOperationSection
    {
        ValueTuple<PlayerMetadataNestedValue, int> Payload { get; }
    }
}

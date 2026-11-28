using System;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.StateGraphPlayerMetadataFixtures
{
    public interface IPlayerMetadataGenericSection : IOperation
    {
        ValueTuple<PlayerMetadataNestedValue, int> Payload { get; }
    }
}

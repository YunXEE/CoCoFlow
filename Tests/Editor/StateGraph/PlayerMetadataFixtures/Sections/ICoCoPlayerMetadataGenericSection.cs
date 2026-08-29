using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.StateGraphPlayerMetadataFixtures
{
    public interface ICoCoPlayerMetadataGenericSection : ICoCoOperationSection
    {
        ValueTuple<CoCoPlayerMetadataNestedValue, int> Payload { get; }
    }
}

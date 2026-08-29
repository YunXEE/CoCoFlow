using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Built-in pass-through reduction for RawInputIntent. With one source
    /// per runtime (the scene InputReader) the latest contribution is the
    /// frozen value; multi-source merges require an explicit reducer.
    /// </summary>
    public struct RawInputPassThroughReducer : ICoCoIntentReducer<RawInputIntent>
    {
        public RawInputIntent Reduce(
            in RawInputIntent current,
            in RawInputIntent candidate) => candidate;
    }

    public sealed class RawInputReducerFactory :
        ICoCoIntentReducerFactory<RawInputIntent, RawInputPassThroughReducer>
    {
        public RawInputPassThroughReducer Create(
            CoCoGraphInstanceId graphInstanceId) => default;
    }

    /// <summary>
    /// Official package ids for the raw input intent lane.
    /// </summary>
    public static class RawIntents
    {
        private const ulong High = 0x434F434F52415749UL; // "COCORAWI"

        static RawIntents()
        {
            if (!CoCoIntentId.TryCreate(High, 1UL, out CoCoIntentId player))
            {
                throw new InvalidOperationException(
                    "RawInput intent id is invalid.");
            }

            Player = player;
        }

        public static CoCoIntentId Player { get; }
    }
}

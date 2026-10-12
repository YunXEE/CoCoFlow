using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    // Deliberately outside CoCoFlow.Runtime.Modules: discovery follows metadata,
    // not a public list of package modules or the State author's assembly.
    [CoCoIntentRegistration(typeof(AuthorMoveRegistrar))]
    public sealed class AuthorMoveSource : MonoBehaviour, ICoCoIntentFrameSource<AuthorMoveIntent>
    {
        public float Value = 2f;
        public bool HasValue = true;
        public int Samples;
        public bool TrySample(in CoCoTickFrame tickFrame, out AuthorMoveIntent intent)
        {
            Samples++;
            intent = new AuthorMoveIntent { Speed = Value };
            return HasValue;
        }
    }
    [CoCoIntentRegistration(typeof(AuthorLookRegistrar))]
    public sealed class AuthorLookSource : MonoBehaviour, ICoCoIntentFrameSource<AuthorLookIntent>
    {
        public float Value = 3f;
        public int Samples;
        public bool TrySample(in CoCoTickFrame tickFrame, out AuthorLookIntent intent)
        {
            Samples++;
            intent = new AuthorLookIntent { X = Value };
            return true;
        }
    }
    public sealed class AuthorMoveRegistrar :
        CoCoStandardIntentRegistrar<AuthorMoveIntent, AuthorMoveReducer, AuthorMoveFactory>
    {
        public static CoCoIntentId Id { get; } = CreateId(1UL);
        public AuthorMoveRegistrar() : base(Id, 1, 6UL) { }
        protected override AuthorMoveFactory CreateReducerFactory() => new AuthorMoveFactory();
        internal static CoCoIntentId CreateId(ulong low)
        {
            CoCoIntentId.TryCreate(0x415554484F523036UL, low, out CoCoIntentId id);
            return id;
        }
    }
    public sealed class AuthorLookRegistrar :
        CoCoStandardIntentRegistrar<AuthorLookIntent, AuthorLookReducer, AuthorLookFactory>
    {
        public AuthorLookRegistrar() : base(AuthorMoveRegistrar.CreateId(2UL), 1, 6UL) { }
        protected override AuthorLookFactory CreateReducerFactory() => new AuthorLookFactory();
    }
}

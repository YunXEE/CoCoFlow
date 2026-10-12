using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public struct AuthorMoveIntent { public float Speed; }
    public struct AuthorLookIntent { public float X; }
    public struct AuthorMoveReducer : ICoCoIntentReducer<AuthorMoveIntent>
    {
        public AuthorMoveIntent Reduce(in AuthorMoveIntent current, in AuthorMoveIntent candidate) => candidate;
    }
    public sealed class AuthorMoveFactory : ICoCoIntentReducerFactory<AuthorMoveIntent, AuthorMoveReducer>
    {
        public AuthorMoveReducer Create(CoCoGraphInstanceId graphInstanceId) => default;
    }
    public struct AuthorLookReducer : ICoCoIntentReducer<AuthorLookIntent>
    {
        public AuthorLookIntent Reduce(in AuthorLookIntent current, in AuthorLookIntent candidate) => candidate;
    }
    public sealed class AuthorLookFactory : ICoCoIntentReducerFactory<AuthorLookIntent, AuthorLookReducer>
    {
        public AuthorLookReducer Create(CoCoGraphInstanceId graphInstanceId) => default;
    }
    public static class IntentAuthoringCapture
    {
        public static bool ReadMove;
        public static bool ReadLook;
        public static bool UndeclaredRead;
        public static bool LegacyUndeclaredRead;
        public static bool Wrote;
        public static float Move;
        public static float Look;
        public static LocomotionOperationAuthor SavedAuthor;
        public static void Reset()
        {
            ReadMove = ReadLook = UndeclaredRead = LegacyUndeclaredRead = Wrote = false;
            Move = Look = 0f;
            SavedAuthor = default;
        }
    }
    [CoCoState("AuthorSingle")]
    [CoCoIntentConsume(typeof(AuthorMoveIntent))]
    [CoCoOperationProvide(typeof(ILocomotionSection))]
    public sealed class AuthorSingleLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public void Update(CoCoStateExecutionContext context)
        {
            IntentAuthoringCapture.ReadMove = context.TryReadIntent(out AuthorMoveIntent move);
            IntentAuthoringCapture.UndeclaredRead = context.TryReadIntent(out AuthorLookIntent ignored);
            IntentAuthoringCapture.LegacyUndeclaredRead = context.Intents.TryFirst(out ignored);
            IntentAuthoringCapture.Move = move.Speed;
            var author = new LocomotionOperationAuthor(context.Operations);
            IntentAuthoringCapture.Wrote = author.Move(0f, move.Speed) && author.Gravity(false);
            IntentAuthoringCapture.SavedAuthor = author;
            if (context.OutgoingTransitions.Count > 0) context.TryRequestTransitionTo<AuthorLookLogic>();
        }
    }
    [CoCoState("AuthorMultiple")]
    [CoCoIntentConsume(typeof(AuthorMoveIntent))]
    [CoCoIntentConsume(typeof(AuthorLookIntent))]
    [CoCoOperationProvide(typeof(ILocomotionSection))]
    public sealed class AuthorMultipleLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public void Update(CoCoStateExecutionContext context)
        {
            IntentAuthoringCapture.ReadMove = context.TryReadIntent(out AuthorMoveIntent move);
            IntentAuthoringCapture.ReadLook = context.TryReadIntent(out AuthorLookIntent look);
            IntentAuthoringCapture.Move = move.Speed;
            IntentAuthoringCapture.Look = look.X;
            var author = new LocomotionOperationAuthor(context.Operations);
            IntentAuthoringCapture.Wrote = author.Move(look.X, move.Speed) && author.Gravity(false);
        }
    }
    [CoCoState("AuthorLook")]
    [CoCoIntentConsume(typeof(AuthorLookIntent))]
    [CoCoOperationProvide(typeof(ILocomotionSection))]
    public sealed class AuthorLookLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public void Update(CoCoStateExecutionContext context)
        {
            IntentAuthoringCapture.ReadLook = context.TryReadIntent(out AuthorLookIntent look);
            IntentAuthoringCapture.Look = look.X;
            IntentAuthoringCapture.Wrote = new LocomotionOperationAuthor(context.Operations).Move(look.X, 0f);
        }
    }
    [CoCoState("AuthorUndeclaredOperation")]
    public sealed class AuthorUndeclaredOperationLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public void Update(CoCoStateExecutionContext context) =>
            IntentAuthoringCapture.Wrote = new LocomotionOperationAuthor(context.Operations).Move(0f, 100f);
    }
    public sealed class AuthorExactLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        private readonly CoCoIntentHandle<AuthorMoveIntent> _otherStateHandle;
        public static float TypedValue;
        public static float LegacyValue;
        public static bool OtherStateHandleRead;
        public AuthorExactLogic(CoCoIntentHandle<AuthorMoveIntent> otherStateHandle) => _otherStateHandle = otherStateHandle;
        public void Update(CoCoStateExecutionContext context)
        {
            context.TryReadIntent(out AuthorMoveIntent typed);
            context.Intents.TryFirst(out AuthorMoveIntent legacy);
            TypedValue = typed.Speed;
            LegacyValue = legacy.Speed;
            OtherStateHandleRead = context.Intents.TryGet(_otherStateHandle, out AuthorMoveIntent ignored);
        }
    }

}

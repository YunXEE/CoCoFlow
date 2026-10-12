using System;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>
    /// Writes a fixed forward velocity into the locomotion section via the
    /// typed field resolver. It deliberately consumes no Intent so the test
    /// also proves that an Operation-only graph needs no Intent source.
    /// </summary>
    [CoCoState("LocoMoveProbe")]
    [CoCoOperationProvide(typeof(ILocomotionSection))]
    public sealed class LocoMoveProbeLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public static float? LastDelta;

        public void Update(CoCoStateExecutionContext context)
        {
            var author = new LocomotionOperationAuthor(context.Operations);
            if (!author.Move(0f, 2f) || !author.Gravity(true))
            {
                LastDelta = null;
                return;
            }

            LastDelta = 2f;
        }
    }

    /// <summary>
    /// Declares both Sections consumed by AnimAutoOperator. The State does
    /// not need to write commands for the registration test; its descriptor
    /// is sufficient to freeze both Sections into the compiled manifest.
    /// </summary>
    [CoCoState("AnimOperationProbe")]
    [CoCoOperationProvide(typeof(IAnimParameterOperationSection))]
    [CoCoOperationProvide(typeof(IAnimTriggerOperationSection))]
    public sealed class AnimOperationProbeLogic :
        CoCoStateLogic,
        ICoCoStateUpdate
    {
        public void Update(CoCoStateExecutionContext context)
        {
        }
    }
}

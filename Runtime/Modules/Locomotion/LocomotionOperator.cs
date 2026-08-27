using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Locomotion
{
    /// <summary>
    /// Locomotion operator (D79/D80/D81): carries the proven
    /// CharacterLocomotion algorithm (sample-proven via many battles)
    /// into the paradigm — semantics moved, algorithm untouched.
    /// Pipeline inside TryExecute: read Section (desire) + previous slot
    /// (facts/phase) → pure step (gravity/jump/launch/rotation integration
    /// + delta synthesis) → engine-fact segment (CharacterController.Move
    /// or teleport: the engine alone decides the actual landing) → sample
    /// the real transform back into the candidate slot → commit barrier
    /// atomically promotes it. Rejected ticks leave the engine fact on the
    /// transform; the same delta replays next tick and reconverges.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("CoCoFlow/Locomotion/Locomotion Operator")]
    [CoCoOperatorRegistration(typeof(LocomotionSectionRegistrar))]
    public sealed class LocomotionOperator :
        MonoBehaviour,
        ICoCoOperator,
        ICoCoContextRestoreBinding,
        ICoCoTemporalDecoratorBinding
    {
        private static readonly CoCoOperationSectionRequirement SectionRequirement;
        private static readonly CoCoOperatorDescriptor OperatorDescriptor;

        static LocomotionOperator()
        {
            var builder = new CoCoOperatorDescriptorBuilder();
            CoCoDiagnostic requirementDiagnostic = default;
            CoCoDiagnostic outcomeDiagnostic = default;
            CoCoDiagnostic freezeDiagnostic = default;
            if (!builder.TryRequire<ILocomotionSection>(
                    LocoContractIds.SectionId,
                    CoCoOperationSectionMode.Continuous,
                    out SectionRequirement,
                    out requirementDiagnostic) ||
                !builder.TryOwnOutcome<LocomotionState>(
                    LocoContractIds.StateSlotId,
                    out outcomeDiagnostic) ||
                !builder.TryFreeze<LocomotionOperator>(
                    LocoContractIds.OperatorId,
                    out OperatorDescriptor,
                    out freezeDiagnostic))
            {
                throw new InvalidOperationException(
                    requirementDiagnostic.IsError
                        ? requirementDiagnostic.Message
                        : outcomeDiagnostic.IsError
                            ? outcomeDiagnostic.Message
                            : freezeDiagnostic.Message);
            }
        }

                [SerializeField] private MonoBehaviour downstreamRestoreBinding;
[SerializeField] private LocomotionOperatorConfig config;
        [SerializeField] private bool applyLocalYawRotation = true;

        private CharacterController _controller;
        private CoCoDiagnostic _lastDiagnostic;

        public CoCoOperatorDescriptor Descriptor => OperatorDescriptor;

        MonoBehaviour ICoCoTemporalDecoratorBinding.DownstreamRestoreBinding =>
            downstreamRestoreBinding;

        /// <summary>
        /// Restore projection (exception moment #2): the world adopts the
        /// ledger once, integration phase continues as-is. Applies for
        /// every restore kind — preview, confirm, cancel and correction.
        /// </summary>
        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            if (!context.IsValid ||
                !context.Reader.TryRead(
                    LocoContractIds.StateSlotId,
                    out LocomotionState state))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.MissingDescriptor,
                    "Locomotion restore projection requires one valid restore context with a committed locomotion slot.");
                return false;
            }

            LocomotionStateMath.ProjectToWorld(state, transform);
            Physics.SyncTransforms();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
        public CoCoDiagnostic LastDiagnostic => _lastDiagnostic;

        private void Reset()
        {
            _controller = GetComponent<CharacterController>();
        }

        public bool TryExecute(
            in CoCoOperatorExecutionContext context,
            out CoCoOperatorOutcome outcome)
        {
            if (!context.TryGet(
                    SectionRequirement,
                    out CoCoOperationSectionEntry<ILocomotionSection> entry))
            {
                outcome = Reject("Locomotion Section is unavailable.");
                return false;
            }

            _controller = _controller != null
                ? _controller
                : GetComponent<CharacterController>();
            if (_controller == null)
            {
                outcome = Reject(
                    "LocomotionOperator requires a CharacterController.");
                return false;
            }

            LocoConfig resolved = ResolveConfig();

            // Previous committed facts + integration phase.
            LocomotionState state =
                context.PreviousContext.Layout != null &&
                context.PreviousContext.Layout.TryResolveSlot(
                    LocoContractIds.StateSlotId,
                    out CoCoStateSlot<LocomotionState> slot)
                    ? context.PreviousContext.Read(slot)
                    : default;

            // Initial anchoring (exception moment #1): adopt the world
            // position once so the ledger starts from where the actor is.
            if (!context.PreviousContext.HasCommittedFrame)
            {
                state = LocomotionStateMath.Anchor(state, transform);
            }

            ILocomotionSection section = entry.View;
            LocoSectionInput input = LocoSectionInput.From(section);

            // Ground sample (CL CheckGrounded, verbatim).
            bool grounded = Physics.CheckSphere(
                transform.position + resolved.GroundCheckOffset,
                resolved.GroundCheckRadius,
                resolved.GroundLayer);

            // Pure step (CL HandleGravity/Jump/Launch/SetRotation +
            // ApplyMovement's delta synthesis, verbatim order).
            LocomotionState next = LocomotionStateMath.Step(
                state,
                input,
                resolved,
                grounded,
                (float)context.TickFrame.DeltaTime,
                out float deltaX,
                out float deltaZ,
                out float deltaY,
                out bool teleport);

            // Engine-fact segment: hand the desire over; the engine alone
            // decides the landing (collision, steps, slopes), then we
            // sample what actually happened back into the candidate.
            if (teleport)
            {
                Vector3 target = new Vector3(
                    next.PositionX,
                    next.PositionY,
                    next.PositionZ);
                Quaternion rotation = input.TeleportRotation();
                transform.SetPositionAndRotation(target, rotation);
                Physics.SyncTransforms();
            }
            else
            {
                if (applyLocalYawRotation &&
                    input.LookX * input.LookX + input.LookZ * input.LookZ > 0.01f)
                {
                    transform.rotation = Quaternion.Euler(0f, next.Rotation, 0f);
                }

                _controller.Move(new Vector3(deltaX, deltaY, deltaZ));
            }

            next = LocomotionStateMath.Sample(next, transform, grounded);

            // Facts enter the candidate slot; the commit barrier promotes
            // them (or the reject discards everything — the engine fact
            // stays on the transform and the same delta replays).
            if (!context.TryWriteOutcome(
                    LocoContractIds.StateSlotId,
                    next))
            {
                outcome = Reject("Locomotion slot write was rejected.");
                return false;
            }

            outcome = CoCoOperatorOutcome.Success;
            return true;
        }

        private LocoConfig ResolveConfig()
        {
            return config != null
                ? config.ToConfig()
                : LocoConfig.Default;
        }

        private CoCoOperatorOutcome Reject(string message)
        {
            _lastDiagnostic = CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Operator,
                CoCoDiagnosticCode.OperatorExecutionFailed,
                message);
            return CoCoOperatorOutcome.Rejected(_lastDiagnostic);
        }
    }

    /// <summary>
    /// Resolved (non-serialized) configuration fed to the pure step.
    /// </summary>
    public struct LocoConfig
    {
        public float Gravity;
        public float BaseGravityMultiplier;
        public bool IsUsingGravity;
        public int GroundLayer;
        public float GroundCheckRadius;
        public Vector3 GroundCheckOffset;
        public float RotationSmoothTime;

        public static LocoConfig Default => new LocoConfig
        {
            Gravity = -9.81f,
            BaseGravityMultiplier = 2f,
            IsUsingGravity = true,
            GroundLayer = ~0,
            GroundCheckRadius = 0.2f,
            GroundCheckOffset = new Vector3(0f, 0.1f, 0f),
            RotationSmoothTime = 0.1f,
        };
    }

    /// <summary>Section view copied into a plain struct for the step.</summary>
    public struct LocoSectionInput
    {
        public float MoveX, MoveZ;
        public float ForcedX, ForcedZ;
        public bool UseGravity;
        public float GravityScale;
        public bool JumpRequested;
        public bool LaunchForced;
        public float VerticalImpulse;
        public float LookX, LookZ;
        public bool InstantRotation;
        public bool TeleportRequested;
        public float TeleportX, TeleportY, TeleportZ;
        public float TeleportQX, TeleportQY, TeleportQZ, TeleportQW;

        public static LocoSectionInput From(ILocomotionSection section) =>
            new LocoSectionInput
            {
                MoveX = section.MoveX,
                MoveZ = section.MoveZ,
                ForcedX = section.ForcedX,
                ForcedZ = section.ForcedZ,
                UseGravity = section.UseGravity,
                GravityScale = section.GravityScale,
                JumpRequested = section.JumpRequested,
                LaunchForced = section.LaunchForced,
                VerticalImpulse = section.VerticalImpulse,
                LookX = section.LookX,
                LookZ = section.LookZ,
                InstantRotation = section.InstantRotation,
                TeleportRequested = section.TeleportRequested,
                TeleportX = section.TeleportX,
                TeleportY = section.TeleportY,
                TeleportZ = section.TeleportZ,
                TeleportQX = section.TeleportRotationX,
                TeleportQY = section.TeleportRotationY,
                TeleportQZ = section.TeleportRotationZ,
                TeleportQW = section.TeleportRotationW,
            };

        public Quaternion TeleportRotation() =>
            TeleportQX == 0f && TeleportQY == 0f && TeleportQZ == 0f && TeleportQW == 0f
                ? Quaternion.identity
                : new Quaternion(TeleportQX, TeleportQY, TeleportQZ, TeleportQW);
    }

    /// <summary>
    /// Pure step (D80 verbatim carry of the CL algorithm) plus the
    /// anchor/sample fact helpers. Order and formulas match
    /// CharacterLocomotion.Update: CheckGrounded → HandleGravity →
    /// Jump/Launch → SetRotation → ApplyMovement synthesis.
    /// </summary>
    public static class LocomotionStateMath
    {
        public static LocomotionState Step(
            in LocomotionState prev,
            in LocoSectionInput input,
            in LocoConfig config,
            bool grounded,
            float deltaTime,
            out float deltaX,
            out float deltaZ,
            out float deltaY,
            out bool teleport)
        {
            var next = prev;

            // ===== HandleGravity (CL verbatim) =====
            if (input.UseGravity && config.IsUsingGravity)
            {
                if (grounded && next.VerticalVelocity < 0.01f)
                {
                    next.VerticalVelocity = -2f; // ground stick
                }
                else
                {
                    next.VerticalVelocity +=
                        config.Gravity *
                        config.BaseGravityMultiplier *
                        input.GravityScale *
                        deltaTime;
                }
            }
            else
            {
                next.VerticalVelocity = 0f; // SetGravityEnable(false) side effect
            }

            // ===== Jump / Launch (CL verbatim) =====
            if (input.LaunchForced)
            {
                next.VerticalVelocity = input.VerticalImpulse;
            }
            else if (input.JumpRequested && grounded)
            {
                next.VerticalVelocity = input.VerticalImpulse;
            }

            // ===== SetRotation / SetRotationInstant (CL math, Mathf direct) =====
            float dirSq = input.LookX * input.LookX + input.LookZ * input.LookZ;
            if (dirSq > 0.01f)
            {
                float targetAngle =
                    Mathf.Atan2(input.LookX, input.LookZ) * Mathf.Rad2Deg;
                if (input.InstantRotation)
                {
                    next.Rotation = targetAngle;
                    next.RotationVelocity = 0f;
                }
                else
                {
                    float velocity = next.RotationVelocity;
                    next.Rotation = Mathf.SmoothDampAngle(
                        next.Rotation,
                        targetAngle,
                        ref velocity,
                        config.RotationSmoothTime,
                        Mathf.Infinity,
                        deltaTime);
                    next.RotationVelocity = velocity;
                }
            }

            // ===== Teleport override (delta bypass) =====
            teleport = input.TeleportRequested;
            if (teleport)
            {
                next.PositionX = input.TeleportX;
                next.PositionY = input.TeleportY;
                next.PositionZ = input.TeleportZ;
            }

            // ===== ApplyMovement synthesis (CL verbatim priority) =====
            float forcedSq =
                input.ForcedX * input.ForcedX + input.ForcedZ * input.ForcedZ;
            float velocityX = forcedSq > 0.001f ? input.ForcedX : input.MoveX;
            float velocityZ = forcedSq > 0.001f ? input.ForcedZ : input.MoveZ;
            deltaX = velocityX * deltaTime;
            deltaZ = velocityZ * deltaTime;
            deltaY = next.VerticalVelocity * deltaTime;

            return next;
        }

        /// <summary>Initial anchoring (exception moment #1): the ledger
        /// adopts the world transform once.</summary>
        public static LocomotionState Anchor(
            in LocomotionState state,
            Transform transform)
        {
            var anchored = state;
            Vector3 position = transform.position;
            anchored.PositionX = position.x;
            anchored.PositionY = position.y;
            anchored.PositionZ = position.z;
            Quaternion rotation = transform.rotation;
            anchored.RotationQX = rotation.x;
            anchored.RotationQY = rotation.y;
            anchored.RotationQZ = rotation.z;
            anchored.RotationQW = rotation.w;
            return anchored;
        }

        /// <summary>Fact sampling: the engine's actual output becomes the
        /// register values (single writer — this module only).</summary>
        public static LocomotionState Sample(
            in LocomotionState state,
            Transform transform,
            bool grounded)
        {
            var sampled = state;
            Vector3 position = transform.position;
            sampled.PositionX = position.x;
            sampled.PositionY = position.y;
            sampled.PositionZ = position.z;
            Quaternion rotation = transform.rotation;
            sampled.RotationQX = rotation.x;
            sampled.RotationQY = rotation.y;
            sampled.RotationQZ = rotation.z;
            sampled.RotationQW = rotation.w;
            sampled.IsGrounded = grounded;
            return sampled;
        }

        /// <summary>Restore projection (exception moment #2): the world
        /// adopts the ledger once, integration phase continues as-is.</summary>
        public static void ProjectToWorld(
            in LocomotionState state,
            Transform transform)
        {
            transform.SetPositionAndRotation(
                new Vector3(state.PositionX, state.PositionY, state.PositionZ),
                new Quaternion(
                    state.RotationQX,
                    state.RotationQY,
                    state.RotationQZ,
                    state.RotationQW));
        }
    }
}

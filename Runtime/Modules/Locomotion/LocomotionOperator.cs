using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;
using UnityEngine;

// Host 调用移动 Operator，把本步移动合同作用到角色控制器，并把可恢复的角色状态提交到 Context。
namespace CoCoFlow.Runtime.Modules.Locomotion
{
    /// <summary>把连续移动操作作用到角色控制器，并记录引擎实际采用的位姿和速度。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("CoCoFlow/Locomotion/Locomotion Operator")]
    public sealed class LocomotionOperator :
        MonoBehaviour,
        IHostPersistenceRestoreOperator,
        IHostTemporalRestoreOperator
    {
        private static readonly OperationRequirement OperationRequirement;
        [OperatorDefinition]
        private static HostOperatorDefinition Definition { get; }

        [SerializeField] private LocomotionOperatorConfig config;
        [SerializeField] private bool applyLocalYawRotation = true;

        private CharacterController _controller;
        private CoCoDiagnostic _lastDiagnostic;

        public OperatorDescriptor Descriptor => Definition.Descriptor;
        public CoCoDiagnostic LastDiagnostic => _lastDiagnostic;

        #region Public API

        /// <summary>从恢复的移动槽重建角色位置和控制状态，无法取得有效快照时返回 false 和诊断。</summary>
        public bool TryRestore(
            in HostRestoreContext context,
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

        /// <summary>从本步操作与上一提交读取移动状态，驱动角色控制器并报告新的移动结果。装配或写入失败时返回 false。</summary>
        public bool TryExecute(
            in OperatorExecutionContext context,
            out OperatorOutcome outcome)
        {
            if (!context.TryGet(
                    OperationRequirement,
                    out OperationEntry<ILocomotionOperation> entry))
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

            LocomotionState state =
                context.PreviousContext.Layout != null &&
                context.PreviousContext.Layout.TryResolveSlot(
                    LocoContractIds.StateSlotId,
                    out StateSlot<LocomotionState> slot)
                    ? context.PreviousContext.Read(slot)
                    : default;

            if (!context.PreviousContext.HasCommittedFrame)
            {
                state = LocomotionStateMath.Anchor(state, transform);
            }

            ILocomotionOperation section = entry.View;
            LocoOperationInput input = LocoOperationInput.From(section);

            bool grounded = Physics.CheckSphere(
                transform.position + resolved.GroundCheckOffset,
                resolved.GroundCheckRadius,
                resolved.GroundLayer);

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

            if (!context.TryWriteOutcome(
                    LocoContractIds.StateSlotId,
                    next))
            {
                outcome = Reject("Locomotion slot write was rejected.");
                return false;
            }

            outcome = OperatorOutcome.Success;
            return true;
        }

        #endregion

        #region Internal Logic

        static LocomotionOperator()
        {
            var builder = new HostOperatorBuilder();
            OperationRequirement = builder.Require<ILocomotionOperation, LocomotionOperationViewFactory>(
                LocoContractIds.OperationId, LocoContractIds.OperationSemanticFingerprint, new LocomotionOperationViewFactory());
            builder.OwnOutcome(LocoContractIds.StateBlockId, LocoContractIds.StateSlotId,
                ContextProjection.Temporal | ContextProjection.Durable,
                ContextRestorePolicy.Stored, default(LocomotionState), 1);
            Definition = builder.Freeze<LocomotionOperator>(LocoContractIds.OperatorId);
        }

        private void Reset()
        {
            _controller = GetComponent<CharacterController>();
        }

        private LocoConfig ResolveConfig()
        {
            return config != null
                ? config.ToConfig()
                : LocoConfig.Default;
        }

        private OperatorOutcome Reject(string message)
        {
            _lastDiagnostic = CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Operator,
                CoCoDiagnosticCode.OperatorExecutionFailed,
                message);
            return OperatorOutcome.Rejected(_lastDiagnostic);
        }

        #endregion
    }

    /// <summary>保存一次移动计算采用的重力、转向与着地参数。</summary>
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

    /// <summary>将本步操作视图中的各项移动意图复制成供移动计算使用的值。</summary>
    public struct LocoOperationInput
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

        /// <summary>按显式字段逐项读取本步移动视图，产生交给移动积分的独立输入值。</summary>
        public static LocoOperationInput From(ILocomotionOperation section) =>
            new LocoOperationInput
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

        /// <summary>将本步传送所带的四个旋转分量组合为 Unity 四元数。</summary>
        public Quaternion TeleportRotation() =>
            TeleportQX == 0f && TeleportQY == 0f && TeleportQZ == 0f && TeleportQW == 0f
                ? Quaternion.identity
                : new Quaternion(TeleportQX, TeleportQY, TeleportQZ, TeleportQW);
    }

    /// <summary>计算角色的下一步速度和朝向，并在存储状态与 Unity Transform 之间转换。</summary>
    public static class LocomotionStateMath
    {
        /// <summary>用上步状态、本步意图、参数和步长计算新的移动状态，供调用方实际移动角色。</summary>
        public static LocomotionState Step(
            in LocomotionState prev,
            in LocoOperationInput input,
            in LocoConfig config,
            bool grounded,
            float deltaTime,
            out float deltaX,
            out float deltaZ,
            out float deltaY,
            out bool teleport)
        {
            var next = prev;

            if (input.UseGravity && config.IsUsingGravity)
            {
                if (grounded && next.VerticalVelocity < 0.01f)
                {
                    next.VerticalVelocity = -2f;
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
                next.VerticalVelocity = 0f;
            }

            if (input.LaunchForced)
            {
                next.VerticalVelocity = input.VerticalImpulse;
            }
            else if (input.JumpRequested && grounded)
            {
                next.VerticalVelocity = input.VerticalImpulse;
            }

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

            teleport = input.TeleportRequested;
            if (teleport)
            {
                next.PositionX = input.TeleportX;
                next.PositionY = input.TeleportY;
                next.PositionZ = input.TeleportZ;
            }

            float forcedSq =
                input.ForcedX * input.ForcedX + input.ForcedZ * input.ForcedZ;
            float velocityX = forcedSq > 0.001f ? input.ForcedX : input.MoveX;
            float velocityZ = forcedSq > 0.001f ? input.ForcedZ : input.MoveZ;
            deltaX = velocityX * deltaTime;
            deltaZ = velocityZ * deltaTime;
            deltaY = next.VerticalVelocity * deltaTime;

            return next;
        }

        /// <summary>以场景中的当前位姿建立移动状态的空间基准，供首次步进或校正使用。</summary>
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

        /// <summary>从移动后的 Transform 及着地结果读取实际位置与旋转，返回用于提交的状态。</summary>
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

        /// <summary>将已保存移动状态中的位置与旋转应用到目标 Transform。</summary>
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

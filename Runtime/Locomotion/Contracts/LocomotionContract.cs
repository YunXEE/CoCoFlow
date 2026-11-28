using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// State 通过移动合同写入平移、跳跃和施力，移动 Operator 按预定义字段和标识应用本步输出。
namespace CoCoFlow.Runtime.Locomotion.Contracts
{
    /// <summary>让 State 逐步写入移动速度、跳跃或施力，由 LocomotionOperator 消费。</summary>
    [Operation(Mode.Continuous)]
    public interface ILocomotionOperation : IOperation
    {
                float MoveX { get; set; }

                float MoveZ { get; set; }

                float ForcedX { get; set; }

                float ForcedZ { get; set; }

                bool UseGravity { get; set; }

                float GravityScale { get; set; }

                bool JumpRequested { get; set; }

                bool LaunchForced { get; set; }

                float VerticalImpulse { get; set; }

                float LookX { get; set; }

                float LookZ { get; set; }

                bool InstantRotation { get; set; }

                bool TeleportRequested { get; set; }

        float TeleportX { get; set; }
        float TeleportY { get; set; }
        float TeleportZ { get; set; }

        float TeleportRotationX { get; set; }
        float TeleportRotationY { get; set; }
        float TeleportRotationZ { get; set; }
        float TeleportRotationW { get; set; }

        /// <summary>将本步跳跃标记和垂直冲量写入移动输出，由移动 Operator 在地面等约束下处理。</summary>
        void Jump(float verticalImpulse);
        /// <summary>将强制起跳标记和指定垂直冲量写入本步移动输出。</summary>
        void Launch(float verticalImpulse);
    }

    /// <summary>保存角色已提交的速度、着地与外力信息，供移动和恢复读取。</summary>
    public struct LocomotionState
    {

        public float VerticalVelocity;
        public float RotationVelocity;
        public float Rotation;

        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float RotationQX;
        public float RotationQY;
        public float RotationQZ;
        public float RotationQW;
        public bool IsGrounded;
    }

    /// <summary>为移动操作、Operator 和可恢复状态槽提供固定标识。</summary>
    public static class LocoContractIds
    {
        private const ulong High = 0x434F434F4C4F434FUL;

        public const ulong OperationSemanticFingerprint = 0x4C4F434F00001001UL;

        public static OperationId OperationId { get; }
        public static OperatorId OperatorId { get; }
        public static StateBlockId StateBlockId { get; }
        public static StateSlotId StateSlotId { get; }

        #region Internal Logic

        static LocoContractIds()
        {
            if (!OperationId.TryCreate(
                    High,
                    1UL,
                    out OperationId operationId) ||
                !OperatorId.TryCreate(
                    High,
                    2UL,
                    out OperatorId operatorId) ||
                !StateBlockId.TryCreate(
                    High,
                    3UL,
                    out StateBlockId blockId) ||
                !StateSlotId.TryCreate(
                    High,
                    4UL,
                    out StateSlotId slotId))
            {
                throw new InvalidOperationException(
                    "Locomotion contract ids must be valid.");
            }

            OperationId = operationId;
            OperatorId = operatorId;
            StateBlockId = blockId;
            StateSlotId = slotId;
        }

        #endregion
    }

    /// <summary>约定移动 Operation 各字段在帧布局中的索引。</summary>
    public static class LocomotionOperationFields
    {
        public const int ForcedX = 0;
        public const int ForcedZ = 1;
        public const int GravityScale = 2;
        public const int InstantRotation = 3;
        public const int JumpRequested = 4;
        public const int LaunchForced = 5;
        public const int LookX = 6;
        public const int LookZ = 7;
        public const int MoveX = 8;
        public const int MoveZ = 9;
        public const int TeleportRequested = 10;
        public const int TeleportRotationW = 11;
        public const int TeleportRotationX = 12;
        public const int TeleportRotationY = 13;
        public const int TeleportRotationZ = 14;
        public const int TeleportX = 15;
        public const int TeleportY = 16;
        public const int TeleportZ = 17;
        public const int UseGravity = 18;
        public const int VerticalImpulse = 19;
    }

    /// <summary>通过框架给定的字段绑定读写本步移动、跳跃和施力数据。</summary>
    internal sealed class LocomotionOperationView : ILocomotionOperation
    {
        private readonly OperationReader _reader;
        private readonly OperationField<float>[] _floats;
        private readonly OperationField<bool>[] _bools;

        internal LocomotionOperationView(
            OperationReader reader,
            OperationField<float>[] floats,
            OperationField<bool>[] bools)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _floats = floats ?? throw new ArgumentNullException(nameof(floats));
            _bools = bools ?? throw new ArgumentNullException(nameof(bools));
        }

        private float F(int index) => _reader.Read(_floats[index]);
        private bool B(int index) => _reader.Read(_bools[index]);

        public float MoveX { get => F(0); set => _reader.Write(_floats[0], value); }
        public float MoveZ { get => F(1); set => _reader.Write(_floats[1], value); }
        public float ForcedX { get => F(2); set => _reader.Write(_floats[2], value); }
        public float ForcedZ { get => F(3); set => _reader.Write(_floats[3], value); }
        public bool UseGravity { get => B(0); set => _reader.Write(_bools[0], value); }
        public float GravityScale { get => F(4); set => _reader.Write(_floats[4], value); }
        public bool JumpRequested { get => B(1); set => _reader.Write(_bools[1], value); }
        public bool LaunchForced { get => B(2); set => _reader.Write(_bools[2], value); }
        public float VerticalImpulse { get => F(5); set => _reader.Write(_floats[5], value); }
        public float LookX { get => F(6); set => _reader.Write(_floats[6], value); }
        public float LookZ { get => F(7); set => _reader.Write(_floats[7], value); }
        public bool InstantRotation { get => B(3); set => _reader.Write(_bools[3], value); }
        public bool TeleportRequested { get => B(4); set => _reader.Write(_bools[4], value); }
        public float TeleportX { get => F(8); set => _reader.Write(_floats[8], value); }
        public float TeleportY { get => F(9); set => _reader.Write(_floats[9], value); }
        public float TeleportZ { get => F(10); set => _reader.Write(_floats[10], value); }
        public float TeleportRotationX { get => F(11); set => _reader.Write(_floats[11], value); }
        public float TeleportRotationY { get => F(12); set => _reader.Write(_floats[12], value); }
        public float TeleportRotationZ { get => F(13); set => _reader.Write(_floats[13], value); }
        public float TeleportRotationW { get => F(14); set => _reader.Write(_floats[14], value); }

        public void Jump(float verticalImpulse)
        {
            JumpRequested = true;
            VerticalImpulse = verticalImpulse;
        }

        public void Launch(float verticalImpulse)
        {
            LaunchForced = true;
            VerticalImpulse = verticalImpulse;
        }
    }

    /// <summary>依据布局创建绑定到当前帧的移动视图，供 State 和 Operator 共用。</summary>
    internal sealed class LocomotionOperationViewFactory :
        IOperationViewFactory<ILocomotionOperation>
    {
        private static readonly int[] FloatDenseIndices =
        {
            LocomotionOperationFields.MoveX,
            LocomotionOperationFields.MoveZ,
            LocomotionOperationFields.ForcedX,
            LocomotionOperationFields.ForcedZ,
            LocomotionOperationFields.GravityScale,
            LocomotionOperationFields.VerticalImpulse,
            LocomotionOperationFields.LookX,
            LocomotionOperationFields.LookZ,
            LocomotionOperationFields.TeleportX,
            LocomotionOperationFields.TeleportY,
            LocomotionOperationFields.TeleportZ,
            LocomotionOperationFields.TeleportRotationX,
            LocomotionOperationFields.TeleportRotationY,
            LocomotionOperationFields.TeleportRotationZ,
            LocomotionOperationFields.TeleportRotationW,
        };

        private static readonly int[] BoolDenseIndices =
        {
            LocomotionOperationFields.UseGravity,
            LocomotionOperationFields.JumpRequested,
            LocomotionOperationFields.LaunchForced,
            LocomotionOperationFields.InstantRotation,
            LocomotionOperationFields.TeleportRequested,
        };

        private OperationField<float>[] _floats;
        private OperationField<bool>[] _bools;

        public ILocomotionOperation Create(
            in OperationViewContext<ILocomotionOperation> context)
        {
            _floats = ResolveFields<float>(context, FloatDenseIndices);
            _bools = ResolveFields<bool>(context, BoolDenseIndices);
            return new LocomotionOperationView(
                context.Reader,
                _floats,
                _bools);
        }

        private static OperationField<TValue>[] ResolveFields<TValue>(
            in OperationViewContext<ILocomotionOperation> context,
            int[] denseIndices)
            where TValue : unmanaged
        {
            var fields = new OperationField<TValue>[denseIndices.Length];
            for (int index = 0; index < fields.Length; index++)
            {
                if (!context.TryGetField(denseIndices[index], out fields[index]))
                {
                    throw new InvalidOperationException(
                        "Locomotion Section field could not be pre-resolved.");
                }
            }

            return fields;
        }
    }

    /// <summary>登记移动合同的显式布局与视图工厂，使方法型合同可在 Player 使用。</summary>
    public sealed class LocomotionOperationSchema
    {

        internal LocomotionOperationViewFactory Operation { get; }

        public static StateSlotId StateSlot => LocoContractIds.StateSlotId;

        #region Public API

        /// <summary>准备移动合同的显式字段布局及手写视图工厂，供标准注册装配。</summary>
        public LocomotionOperationSchema()
        {
            Operation = new LocomotionOperationViewFactory();
        }

        /// <summary>为移动操作的固定 ID 和连续模式创建要件，校验失败返回 false 并提供诊断。</summary>
        public static bool TryCreateOperationRequirement(
            out OperationRequirement requirement,
            out CoCoDiagnostic diagnostic) =>
            OperationRequirement.TryCreate<ILocomotionOperation>(
                LocoContractIds.OperationId,
                Mode.Continuous,
                out requirement,
                out diagnostic);

        #endregion
    }
}

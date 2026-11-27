using System;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.StateGraph;

// State 写入离散机位请求，CameraOperator 将目标、优先级和调度状态交给 CameraDirector 并保存可恢复快照。
namespace CoCoFlow.Runtime.Camera.Contracts
{

    // ReSharper disable once InconsistentNaming
    /// <summary>让 State 在本步请求机位、模式、优先级及调度状态，由 CameraOperator 应用。</summary>
    [Operation(Mode.Discrete)]
    public interface CameraOperation : IOperation
    {
        int RigId { get; set; }
        bool Active { get; set; }
        int Priority { get; set; }
        int CameraId { get; set; }
        bool SchedulingEnabled { get; set; }
    }

    /// <summary>保存一个机位的选择及优先级，供摄像机恢复使用。</summary>
    public struct CamRigState
    {
        public int RigId;
        public bool Active;
        public int Priority;
        public int CameraId;
    }

    /// <summary>保存已提交的输出摄像机与机位调度状态，供时间恢复重建场景。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct CamSnapshotState
    {
        public const int RigCapacity = 64;
        private const int WordsPerRig = 4;

        public float PositionX, PositionY, PositionZ;
        public float RotationX, RotationY, RotationZ, RotationW;
        private int _schedulingEnabled;
        private int _rigCount;
        private unsafe fixed int _rigData[RigCapacity * WordsPerRig];

        public int RigCount => _rigCount;

        public bool HasValidHeader =>
            _rigCount >= 0 && _rigCount <= RigCapacity &&
            (_schedulingEnabled == 0 || _schedulingEnabled == 1);

        public bool SchedulingEnabled
        {
            get => _schedulingEnabled != 0;
            set => _schedulingEnabled = value ? 1 : 0;
        }

        #region Public API

        /// <summary>在快照仍有容量时追加一项机位状态，无法追加时返回 false。</summary>
        public bool TryAddRig(in CamRigState rig)
        {
            if (_rigCount < 0 || _rigCount >= RigCapacity)
                return false;

            unsafe
            {
                fixed (int* data = _rigData)
                {
                    int offset = _rigCount * WordsPerRig;
                    data[offset] = rig.RigId;
                    data[offset + 1] = rig.Active ? 1 : 0;
                    data[offset + 2] = rig.Priority;
                    data[offset + 3] = rig.CameraId;
                }
            }

            _rigCount++;
            return true;
        }

        /// <summary>按索引读取快照中的机位状态，索引越界时返回 false。</summary>
        public bool TryGetRig(int index, out CamRigState rig)
        {
            rig = default;
            if (_rigCount < 0 || _rigCount > RigCapacity ||
                index < 0 || index >= _rigCount)
                return false;

            unsafe
            {
                fixed (int* data = _rigData)
                {
                    int offset = index * WordsPerRig;
                    if (data[offset + 1] != 0 && data[offset + 1] != 1)
                        return false;
                    rig = new CamRigState
                    {
                        RigId = data[offset],
                        Active = data[offset + 1] != 0,
                        Priority = data[offset + 2],
                        CameraId = data[offset + 3]
                    };
                }
            }

            return true;
        }

        #endregion
    }

    /// <summary>为摄像机输入、输出及可恢复槽提供跨步稳定的标识。</summary>
    public static class CamContractIds
    {
        private const ulong High = 0x434F434F43414D45UL;
        public const ulong Fingerprint = 1UL;
        public static IntentId IntentId { get; }
        public static OperationId OperationId { get; }
        public static OperatorId OperatorId { get; }
        public static StateBlockId SnapshotBlockId { get; }
        public static StateSlotId SnapshotSlotId { get; }

        #region Internal Logic

        static CamContractIds()
        {
            if (!IntentId.TryCreate(High, 1, out var intent) ||
                !OperationId.TryCreate(High, 2, out var operation) ||
                !OperatorId.TryCreate(High, 3, out var executor) ||
                !StateBlockId.TryCreate(High, 4, out var block) ||
                !StateSlotId.TryCreate(High, 5, out var slot))
                throw new InvalidOperationException("Camera contract identifiers are invalid.");
            IntentId = intent;
            OperationId = operation;
            OperatorId = executor;
            SnapshotBlockId = block;
            SnapshotSlotId = slot;
        }

        #endregion
    }
}

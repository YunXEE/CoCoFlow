using System;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Camera.Contracts
{
    /// <summary>Write all five properties for one Rig request in the current Tick.</summary>
    // The author-facing name is CameraOperation by the approved API contract.
    // ReSharper disable once InconsistentNaming
    public interface CameraOperation : ICoCoOperationSection
    {
        int RigId { get; set; }
        bool Active { get; set; }
        int Priority { get; set; }
        int CameraId { get; set; }
        bool SchedulingEnabled { get; set; }
    }

    public sealed class CameraOperationView : CameraOperation
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<bool> _active, _schedulingEnabled;
        private readonly CoCoOperationSectionField<int> _cameraId, _priority, _rigId;

        internal CameraOperationView(in CoCoOperationSectionViewContext<CameraOperation> context)
        {
            _reader = context.Reader;
            // Framework field order is ordinal by property name.
            if (!context.TryGetField(0, out _active) ||
                !context.TryGetField(1, out _cameraId) ||
                !context.TryGetField(2, out _priority) ||
                !context.TryGetField(3, out _rigId) ||
                !context.TryGetField(4, out _schedulingEnabled))
                throw new InvalidOperationException("CameraOperation fields could not be resolved.");
        }

        public int RigId { get => _reader.Read(_rigId); set => _reader.Write(_rigId, value); }
        public bool Active { get => _reader.Read(_active); set => _reader.Write(_active, value); }
        public int Priority { get => _reader.Read(_priority); set => _reader.Write(_priority, value); }
        public int CameraId { get => _reader.Read(_cameraId); set => _reader.Write(_cameraId, value); }
        public bool SchedulingEnabled
        {
            get => _reader.Read(_schedulingEnabled);
            set => _reader.Write(_schedulingEnabled, value);
        }
    }

    public sealed class CameraOperationViewFactory : ICoCoOperationSectionViewFactory<CameraOperation>
    {
        public CameraOperation Create(in CoCoOperationSectionViewContext<CameraOperation> context) =>
            new CameraOperationView(context);
    }

    public struct CamRigState
    {
        public int RigId;
        public bool Active;
        public int Priority;
        public int CameraId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct CamSnapshotState
    {
        public const int RigCapacity = 64; // Fixed capacity: 64 rigs.
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
    }

    public static class CamContractIds
    {
        private const ulong High = 0x434F434F43414D45UL; // COCOCAME
        public const ulong Fingerprint = 1UL;
        public static CoCoIntentId IntentId { get; }
        public static CoCoOperationSectionId OperationId { get; }
        public static CoCoOperatorId OperatorId { get; }
        public static CoCoStateBlockId SnapshotBlockId { get; }
        public static CoCoStateSlotId SnapshotSlotId { get; }

        static CamContractIds()
        {
            if (!CoCoIntentId.TryCreate(High, 1, out var intent) ||
                !CoCoOperationSectionId.TryCreate(High, 2, out var operation) ||
                !CoCoOperatorId.TryCreate(High, 3, out var executor) ||
                !CoCoStateBlockId.TryCreate(High, 4, out var block) ||
                !CoCoStateSlotId.TryCreate(High, 5, out var slot))
                throw new InvalidOperationException("Camera contract identifiers are invalid.");
            IntentId = intent;
            OperationId = operation;
            OperatorId = executor;
            SnapshotBlockId = block;
            SnapshotSlotId = slot;
        }
    }
}

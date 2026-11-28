// 场景中的 CameraIntent 采样摄像机与调度状态，玩法通过 Intent 属性读取该步快照。
namespace CoCoFlow.Runtime.Camera.Contracts
{
    /// <summary>携带 CameraIntent 从实际输出摄像机采样的位置与欧拉角。</summary>
    public struct CameraIntentData
    {
        public float PositionX, PositionY, PositionZ;
        public float RotationX, RotationY, RotationZ;
    }

}

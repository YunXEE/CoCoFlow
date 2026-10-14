using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Camera.Contracts
{
    public struct CameraIntentData
    {
        public float PositionX, PositionY, PositionZ;
        public float RotationX, RotationY, RotationZ;
    }

    public struct CamIntentReducer : ICoCoIntentReducer<CameraIntentData>
    {
        public CameraIntentData Reduce(in CameraIntentData current, in CameraIntentData candidate) => candidate;
    }

    public sealed class CamIntentReducerFactory : ICoCoIntentReducerFactory<CameraIntentData, CamIntentReducer>
    {
        public CamIntentReducer Create(CoCoGraphInstanceId graphInstanceId) => default;
    }
}

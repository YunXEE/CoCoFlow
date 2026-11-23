using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Camera.Contracts;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CoCoFlow/Camera/Camera Intent")]
    public sealed class CameraIntent : MonoBehaviour, IIntentFrameSource<CameraIntentData>
    {
        [SerializeField] private CameraDirector director;

        public bool TrySample(in TickFrame tickFrame, out CameraIntentData intent)
        {
            intent = default;
            if (!isActiveAndEnabled || director == null || director.OutputCamera == null) return false;
            Transform output = director.OutputCamera.transform;
            Vector3 position = output.position;
            Vector3 rotation = output.eulerAngles;
            intent = new CameraIntentData
            {
                PositionX = position.x, PositionY = position.y, PositionZ = position.z,
                RotationX = rotation.x, RotationY = rotation.y, RotationZ = rotation.z
            };
            return true;
        }
    }

}

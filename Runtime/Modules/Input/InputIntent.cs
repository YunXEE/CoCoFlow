using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Input
{
    [DisallowMultipleComponent]
    public sealed class InputIntent : MonoBehaviour,
        ICoCoIntentFrameSource<RawInputIntent>
    {
        [SerializeField] private InputReader reader;

        public void SetReader(InputReader value)
        {
            reader = value;
        }

        public bool TrySample(
            in CoCoTickFrame tickFrame,
            out RawInputIntent intent)
        {
            intent = default;
            return isActiveAndEnabled &&
                   reader != null &&
                   reader.TryReadLatest(out intent);
        }
    }
}

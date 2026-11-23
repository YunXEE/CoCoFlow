using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Tests.GameplayBinding
{
    public sealed class EnemyIntent : MonoBehaviour, IIntentFrameSource<EnemyIntentData>
    {
        public bool TrySample(in TickFrame tickFrame, out EnemyIntentData intent)
        {
            intent = new EnemyIntentData("Sentinel-42");
            return true;
        }
    }
}

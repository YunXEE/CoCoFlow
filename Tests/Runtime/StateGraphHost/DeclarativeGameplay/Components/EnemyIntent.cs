using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Tests.GameplayBinding
{
    public sealed class EnemyIntent : MonoBehaviour, ICoCoIntentFrameSource<EnemyIntentData>
    {
        public bool TrySample(in CoCoTickFrame tickFrame, out EnemyIntentData intent)
        {
            intent = new EnemyIntentData("Sentinel-42");
            return true;
        }
    }
}

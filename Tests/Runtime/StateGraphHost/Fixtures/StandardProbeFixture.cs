using CoCoFlow.Runtime.Input.Contracts;

using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;

// 标准 Host 测试用这些属性声明输入与操作，核对标准绑定在状态回调前完成注入。
namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>按状态属性提供的输出视图写入移动与施力，供标准 Host 集成测试观察。</summary>
    [State("StandardProbe")]
    public sealed class StandardProbeLogic : StateLogic, IStateUpdate
    {
        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        public static List<RawInputRecord> Received;

        /// <summary>用本步视图写移动和施力，并记录实际注入的玩家输入。</summary>
        public void Update(StateExecutionContext context)
        {
            Movement.MoveZ = 2f;
            Movement.Launch(5f);
            if (Received != null)
            {
                for (int index = 0; index < Input.Count; index++)
                {
                    if (Input.TryGet(index, out RawInputRecord record))
                    {
                        Received.Add(record);
                    }
                }
            }
        }
    }
    /// <summary>通过标准属性注入取得输入和操作，记录当前实例以检查各 Host 的独立性。</summary>
    [State("PropertyProbe")]
    public sealed class PropertyProbeLogic : StateLogic, IStateUpdate
    {
        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        public static PropertyProbeLogic Last;

        /// <summary>记录接收属性注入的实例，写入移动合同以验证对应 Operator 的实际消费。</summary>
        public void Update(StateExecutionContext context)
        {
            Last = this;
            Movement.MoveZ = 2f;
            Movement.Launch(5f);
            if (StandardProbeLogic.Received == null) return;
            for (int index = 0; index < Input.Count; index++)
            {
                if (Input.TryGet(index, out RawInputRecord record)) StandardProbeLogic.Received.Add(record);
            }
        }
    }

}

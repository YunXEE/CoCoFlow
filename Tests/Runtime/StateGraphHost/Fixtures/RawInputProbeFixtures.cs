using CoCoFlow.Runtime.Input.Contracts;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// 输入集成测试把真实来源采样交给这些状态与归并器，核对有序变化、当前值和隔离。
namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>保存输入测试在实际玩法回调中观测到的采样记录。</summary>
    public sealed class RawInputProbeCapture
    {
        public static List<RawInputRecord> Records;
        public static bool RejectAfterReading;
    }

    /// <summary>选取本次来源贡献作为最终玩家输入，供确定性采样检查。</summary>
    public struct RawInputProbeReducer : IIntentReducer<InputIntentContract>
    {
        /// <summary>将一个来源的贡献合入当前输入，返回传给下一个来源或本步 Logic 的结果。</summary>
        public InputIntentContract Reduce(
            in InputIntentContract current,
            in InputIntentContract candidate) => candidate;
    }

    /// <summary>为每个测试实例创建不共享可变状态的输入归并器。</summary>
    public sealed class RawInputProbeReducerFactory :
        IIntentReducerFactory<InputIntentContract, RawInputProbeReducer>
    {
        /// <summary>返回本实例用于选取最新输入贡献的无状态归并器。</summary>
        public RawInputProbeReducer Create(
            GraphInstanceId graphInstanceId) => default;
    }

    /// <summary>通过绑定的输入句柄取得实际采样并把结果交给用例检查。</summary>
    public sealed class RawInputProbeLogic :
        StateLogic,
        IStateUpdate
    {

        #region Public API

        /// <summary>保存实际输入布局解析的句柄，供后续状态更新读取采样。</summary>
        public RawInputProbeLogic(
            StateFactoryContext context,
            IntentHandle<InputIntentContract> intent)
        {
            _intent = intent;
        }

        /// <summary>从已冻结的输入帧按句柄读取玩家数据并记录，缺少输入时不产生观测值。</summary>
        public void Update(StateExecutionContext context)
        {
            if (_intent.IsValid &&
                context.Intents != null &&
                context.Intents.TryGet(_intent, out InputIntentContract intent))
            {
                if (RawInputProbeCapture.Records == null)
                {
                    return;
                }

                for (int index = 0; index < intent.Count; index++)
                {
                    if (intent.TryGet(index, out RawInputRecord record))
                    {
                        RawInputProbeCapture.Records.Add(record);
                    }
                }
                if (RawInputProbeCapture.RejectAfterReading)
                    throw new System.InvalidOperationException("Input probe rejects this sampled tick.");

            }
        }

        #endregion

        #region Internal Logic

        private readonly IntentHandle<InputIntentContract> _intent;

        #endregion
    }

    /// <summary>用作原始输入采样状态的可编辑节点配置。</summary>
    [System.Serializable]
    public sealed class RawInputProbeConfig : StateConfig { }

    /// <summary>为输入采样测试提供固定的冻结配置类型。</summary>
    public readonly struct RawInputProbeConfigSchema : IFrozenConfigSchema { }

    /// <summary>核对输入测试的节点配置存在，生成与其 schema 对应的冻结合同。</summary>
    public sealed class RawInputProbeConfigFreezer :
        IConfigFreezer<RawInputProbeConfig, RawInputProbeConfigSchema>
    {
        /// <summary>确认采样状态的配置存在，缺失时提供对应的节点诊断。</summary>
        public bool TryFreeze(
            RawInputProbeConfig source,
            FrozenConfigWriter<RawInputProbeConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = source != null
                ? CoCoDiagnostic.None
                : CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "probe config required");
            return source != null;
        }
    }

    /// <summary>提供编译采样测试节点时共用的配置 schema。</summary>
    public static class RawInputProbeSchemas
    {

        #region Public API

        public static readonly FrozenConfigSchema<RawInputProbeConfigSchema> State;

        #endregion

        #region Internal Logic

        static RawInputProbeSchemas()
        {
            var builder =
                new FrozenConfigSchemaBuilder<RawInputProbeConfigSchema>();
            _ = builder.TryFreeze(
                out FrozenConfigSchema<RawInputProbeConfigSchema> schema,
                out _);
            State = schema;
        }

        #endregion
    }

    /// <summary>为输入测试提供每次激活独立的可恢复记忆。</summary>
    public sealed class RawInputProbeMemory : StateActivationMemory { }

    /// <summary>在输入测试的激活记忆与历史记录的槽值之间捕获和还原。</summary>
    public sealed class RawInputProbeMemoryBinding :
        IActivationMemoryStateBinding<RawInputProbeMemory, int>
    {
        public const ulong Fingerprint = 51457UL;

        public ulong SemanticFingerprint => Fingerprint;

        /// <summary>从测试激活记忆取得要保存的槽值，缺少对象时返回失败和诊断。</summary>
        public bool TryCapture(
            RawInputProbeMemory memory,
            out int state,
            out CoCoDiagnostic diagnostic)
        {
            if (memory == null)
            {
                state = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Probe memory is required for Graph State capture.");
                return false;
            }

            state = 0;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>将保存值写入候选记忆，缺少可写对象时拒绝恢复。</summary>
        public bool TryPrepareRestore(
            in int state,
            RawInputProbeMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            if (candidateMemory == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Probe candidate memory is required for restore.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }
}

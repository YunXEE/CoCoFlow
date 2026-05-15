using Fusion;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Network
{
    /// <summary>
    /// Fusion 网络配置辅助类。
    /// <para>
    /// NetworkProjectConfig 是 Fusion 运行时的全局配置文件，需在 Unity Editor 中创建
    /// (Assets &gt; Create &gt; Fusion &gt; Network Project Config)，然后指定到
    /// <c>NetworkProjectConfig.Global</c> 或通过 <see cref="NetworkRunner"/> 启动参数传入。
    /// </para>
    /// <para>以下汇总了 CoCoFrame 项目推荐的关键配置值。</para>
    /// </summary>
    /// <remarks>
    /// <b>配置文件位置：</b>
    /// <c>Assets/Resources/Fusion/NetworkProjectConfig.asset</c>（建议路径）
    /// <br/>
    /// <b>注意：</b>此处只提供文档和运行时校验，实际的 .asset 资源需在 Unity Editor 中手动创建。
    /// </remarks>
    [HelpURL("https://doc.photonengine.com/fusion/current/start-up/configuration")]
    public static class NetworkConfigHelper
    {
        // ──────────────────────────────────────────────
        //  Tick Rate（SimulationConfig）
        // ──────────────────────────────────────────────

        /// <summary>
        /// 推荐的 Simulation Tick Rate（30 Hz）。
        /// <para>
        /// 定义 Fusion 每秒执行固定更新（FixedUpdateNetwork）的次数。
        /// 30 Hz 是典型动作游戏的均衡值，兼顾响应速度和 CPU 开销。
        /// 对精度要求更高的射击类游戏可提至 60 Hz，但会加倍带宽和运算量。
        /// </para>
        /// <para>
        /// 配置位置：<c>NetworkProjectConfig.Simulation.TickRate</c>
        /// </para>
        /// </summary>
        public const int RecommendedTickRate = 30;

        /// <summary>
        /// 推荐的网络输⼊延迟（Input Delay Multiplier），单位：tick。
        /// <para>
        /// 控制客户端在应用输入前等待的 tick 数量。
        /// 推荐值 1-2，更高的值增加延迟但减少预测失配造成的抖动。
        /// </para>
        /// </summary>
        public const int RecommendedInputDelayTicks = 1;

        // ──────────────────────────────────────────────
        //  Interpolation（NetworkTransform）
        // ──────────────────────────────────────────────

        /// <summary>
        /// 推荐的数据源模式：快照（Snapshots）。
        /// <para>
        /// Fusion 支持三种 <c>InterpolationDataSource</c> 模式：
        /// </para>
        /// <list type="bullet">
        ///   <item><description><b>Snapshots</b>（推荐）— 基于状态快照插值，适用于角色、载具。</description></item>
        ///   <item><description><b>AutoFromInput</b> — 对自身对象使用输入预测，对他⼈使用快照。</description></item>
        ///   <item><description><b>NoInterpolation</b> — 不插值，用于 UI 或精确同步。</description></item>
        /// </list>
        /// <para>
        /// 对 <see cref="NetworkTransform"/> 组件，将
        /// <c>InterpolationDataSource</c> 设为 Snapshots 以获得平滑的他人位置显示。
        /// </para>
        /// </summary>
        public const InterpolationDataSources RecommendedInterpolationSource
            = InterpolationDataSources.Snapshots;

        /// <summary>
        /// 推荐的插值前移量（Interpolation Look Ahead），单位：tick。
        /// <para>
        /// 通过提前未来一小段时间来补偿网络延迟造成的视觉滞后。
        /// 典型值 1-3，需要根据实际网络质量微调。
        /// </para>
        /// </summary>
        public const float RecommendedInterpolationLookAheadTicks = 2f;

        // ──────────────────────────────────────────────
        //  NetworkObject Prefab Registration
        // ──────────────────────────────────────────────

        /// <summary>
        /// 必须在 <c>NetworkProjectConfig.PrefabTable</c> 中注册的预制体列表。
        /// <para>
        /// Fusion 使用预制体表格将 <see cref="NetworkObject"/> 预制体映射到
        /// Network Object ID。未注册的预制体在运行时无法生成（Spawn）。
        /// </para>
        /// <para>
        /// <b>需要注册的预制体（按模块分类）：</b>
        /// </para>
        /// <list type="table">
        ///   <item>
        ///     <term><c>NetPlayer.prefab</c></term>
        ///     <description>玩家角色，包含 NetworkObject、NetworkCharacterController、NetworkTransform</description>
        ///   </item>
        ///   <item>
        ///     <term><c>NetEnemy.prefab</c></term>
        ///     <description>敌方单位，包含 NetworkObject、NetworkTransform、NetEnemyController</description>
        ///   </item>
        /// </list>
        /// <para>
        /// 注册位置：选中 <c>NetworkProjectConfig.asset</c> → Inspector → Prefabs → + 按钮。
        /// </para>
        /// </summary>
        public static readonly string[] RequiredPrefabs =
        {
            "NetPlayer.prefab",
            "NetEnemy.prefab"
        };

        // ──────────────────────────────────────────────
        //  Scene Management
        // ──────────────────────────────────────────────

        /// <summary>
        /// 推荐使用 <see cref="NetworkSceneManagerDefault"/> 作为场景管理器。
        /// <para>
        /// Fusion 的默认场景管理器支持单场景和场景切换。
        /// 若项目需要自定义加载流程（如加载界面、地址资产），可继承
        /// <c>NetworkSceneManagerBase</c> 实现自定义管理器。
        /// </para>
        /// </summary>
        public const string RecommendedSceneManagerType = nameof(NetworkSceneManagerDefault);

        // ──────────────────────────────────────────────
        //  Runtime Validation
        // ──────────────────────────────────────────────

        /// <summary>
        /// 在运行时校验当前 Fusion 配置是否符合 CoCoFrame 推荐值，
        /// 并将差异输出到 <see cref="CoCoLog"/>。
        /// <para>
        /// 建议在 <see cref="NetManager.Awake"/> 或游戏启动流程中调用一次。
        /// </para>
        /// </summary>
        /// <param name="runner">当前运行的 <see cref="NetworkRunner"/> 实例。</param>
        /// <example>
        /// <code>
        /// NetworkConfigHelper.ValidateConfig(NetworkRunner.Instances[0]);
        /// </code>
        /// </example>
        public static void ValidateConfig(NetworkRunner runner)
        {
            if (runner == null)
            {
                CoCoLog.Warning("[NetworkConfigHelper] ValidateConfig 收到 null runner，跳过校验");
                return;
            }

            var config = runner.Config;
            if (config == null)
            {
                CoCoLog.Warning("[NetworkConfigHelper] runner.Config 为空，跳过校验");
                return;
            }

            var sim = config.Simulation;
            if (sim == null)
            {
                CoCoLog.Warning("[NetworkConfigHelper] runner.Config.Simulation 为空，跳过校验");
                return;
            }

            // 校验 TickRate
            if (sim.TickRate != RecommendedTickRate)
            {
                CoCoLog.Warning(
                    $"[NetworkConfigHelper] 推荐 TickRate={RecommendedTickRate}，"
                  + $"当前={sim.TickRate}。请在 NetworkProjectConfig.Simulation 中调整。");
            }
            else
            {
                CoCoLog.Log("[NetworkConfigHelper] TickRate 校验通过");
            }

            // 校验输入延迟
            if (sim.InputDelayTicks != RecommendedInputDelayTicks)
            {
                CoCoLog.Warning(
                    $"[NetworkConfigHelper] 推荐 InputDelayTicks={RecommendedInputDelayTicks}，"
                  + $"当前={sim.InputDelayTicks}。");
            }

            // 校验预制体注册（至少检查数量）
            var prefabTable = config.PrefabTable;
            int prefabCount = prefabTable?.GetEnumerator() != null
                ? CountPrefabs(prefabTable)
                : 0;

            if (prefabCount < RequiredPrefabs.Length)
            {
                CoCoLog.Warning(
                    $"[NetworkConfigHelper] 预制体注册数量={prefabCount}，"
                  + $"推荐至少注册 {RequiredPrefabs.Length} 个：{string.Join(", ", RequiredPrefabs)}");
            }
            else
            {
                CoCoLog.Log($"[NetworkConfigHelper] 预制体注册数量校验通过（{prefabCount} 个）");
            }
        }

        /// <summary>
        /// 打印完整的推荐配置摘要到 <see cref="CoCoLog"/>。
        /// <para>
        /// 用于开发阶段确认当前项目设置。
        /// </para>
        /// </summary>
        public static void PrintRecommendedSettings()
        {
            CoCoLog.Log($@"
╔══════════════════════════════════════════════╗
║        CoCoFrame Fusion 推荐配置摘要         ║
╠══════════════════════════════════════════════╣
║  TickRate:                {RecommendedTickRate,4} Hz              ║
║  InputDelayTicks:         {RecommendedInputDelayTicks,4}                  ║
║  InterpolationSource:     Snapshots          ║
║  LookAheadTicks:          {RecommendedInterpolationLookAheadTicks,4}                 ║
║  SceneManager:            {RecommendedSceneManagerType,-19} ║
║  Required Prefabs:        {RequiredPrefabs.Length,4} ({string.Join(", ", RequiredPrefabs)})  ║
╚══════════════════════════════════════════════╝");
        }

        /// <summary>
        /// 统计 <see cref="NetworkPrefabTable"/> 中的预制体条目数。
        /// </summary>
        private static int CountPrefabs(NetworkPrefabTable table)
        {
            int count = 0;
            // NetworkPrefabTable 没有直接公开 Count 属性（不同 Fusion 版本 API 有差异），
            // 此处通过迭代器统计以确保兼容性。
            using var enumerator = table.GetEnumerator();
            while (enumerator.MoveNext())
                count++;
            return count;
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;

namespace CoCoFlow.Runtime.Modules.Rendering
{
    public enum GraphicsQualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public class GraphicQualityManager : MonoBehaviour
    {
        [Header("未来拓展：不同画质对应的 URP 配置文件")]
        [Tooltip("按下标对应 Low, Medium, High")]
        public RenderPipelineAsset[] urpAssets;

        private void Awake()
        {
            // TODO: 这里未来应该从你的存档/配置系统(如 JSON/PlayerPrefs)中读取
            // 目前默认设为 Medium 画质进行测试
            GraphicsQualityTier currentTier = GraphicsQualityTier.Medium;

            InitializeQuality(currentTier);
        }

        /// <summary>
        /// 核心初始化入口，仅在游戏启动或进入主菜单时调用
        /// </summary>
        public void InitializeQuality(GraphicsQualityTier tier)
        {
            Debug.Log($"[QualityPipeline] 正在应用画质分级: {tier}");

            ApplyLODSettings(tier);

            // 预留的拓展接口
            ApplyURPAsset(tier);
            ApplyTextureStreaming(tier);
            ApplyPostProcessingSettings(tier);
        }

        #region 核心功能：LOD 跨度动态调度

        private void ApplyLODSettings(GraphicsQualityTier tier)
        {
            // lodBias 决定了 LOD Group 切换的距离阈值。
            // 值越小，越容易切换到低模。值越大，高模保留得越久。
            switch (tier)
            {
                case GraphicsQualityTier.Low:
                    QualitySettings.lodBias = 0.5f;   // 砍半，很近就变低模
                    break;
                case GraphicsQualityTier.Medium:
                    QualitySettings.lodBias = 1.0f;   // 按照美术原始设定
                    break;
                case GraphicsQualityTier.High:
                    QualitySettings.lodBias = 1.5f;   // 延长高模的显示距离
                    break;
            }

            // [可选拓展] 强制限制最大 LOD 层级。比如低画质下，连贴脸都不加载 LOD0。
            // QualitySettings.maximumLODLevel = tier == GraphicsQualityTier.Low ? 1 : 0;
        }

        #endregion

        #region 预留的未来接口 (Future Hooks)

        private void ApplyURPAsset(GraphicsQualityTier tier)
        {
            // 未来如果你需要切换阴影分辨率、抗锯齿方案，
            // 最好的做法是做 3 个不同的 UniversalRenderPipelineAsset 拖到面板里，在这里一键替换。
            int index = (int)tier;
            if (urpAssets != null && index < urpAssets.Length && urpAssets[index] != null)
            {
                GraphicsSettings.defaultRenderPipeline = urpAssets[index];
                QualitySettings.renderPipeline = urpAssets[index];
            }
        }

        private void ApplyTextureStreaming(GraphicsQualityTier tier)
        {
            // 未来这里可以配合 Addressables 决定是否加载高清贴图 Label
            // 或者利用 QualitySettings.masterTextureLimit 强制降级全局贴图分辨率
        }

        private void ApplyPostProcessingSettings(GraphicsQualityTier tier)
        {
            // 未来通过 EventBus 广播画质改变，
            // 场景里的 Volume 监听后，决定是否关闭 Volumetric Fog (体积雾) 等吃显卡的特效
        }

        #endregion
    }
}

using System;
using UnityEditor;

namespace CoCoFlow.Editor.Common
{
    /// <summary>
    /// 双语语言枚举（D10）。切换 UI 归 P05 Assistant-Utility 面板；P02 仅提供偏好与查询。
    /// </summary>
    public enum CoCoEditorLanguage
    {
        English,
        SimplifiedChinese
    }

    /// <summary>
    /// Editor 双语层（D10/D12/D14）。
    /// 偏好经 EditorPrefs 持久化；订阅/退订只允许经窗口 OnEnable/OnDisable 对称调用。
    /// v1 无语言切换 deferral 机制（D14 延后至有真实消费者的任务）。
    /// </summary>
    public static class CoCoEditorLocalization
    {
        /// <summary>EditorPrefs 键。默认 English。</summary>
        public const string LanguagePreferenceKey = "Yunxee.CoCoFlow.Editor.Language";

        private static readonly string[] LanguageNames =
        {
            CoCoEditorLanguage.English.ToString(),
            CoCoEditorLanguage.SimplifiedChinese.ToString()
        };

        /// <summary>当前语言。直接读 EditorPrefs，无缓存，避免域重载后失效。</summary>
        public static CoCoEditorLanguage CurrentLanguage
        {
            get
            {
                string stored = EditorPrefs.GetString(
                    LanguagePreferenceKey,
                    CoCoEditorLanguage.English.ToString());

                return stored == CoCoEditorLanguage.SimplifiedChinese.ToString()
                    ? CoCoEditorLanguage.SimplifiedChinese
                    : CoCoEditorLanguage.English;
            }
        }

        /// <summary>是否中文（私有便捷属性，不在冻结签名表——BUG-044 修正）。</summary>
        private static bool IsChinese =>
            CurrentLanguage == CoCoEditorLanguage.SimplifiedChinese;

        /// <summary>
        /// 语言变更通知。订阅者须在 OnEnable 订阅、OnDisable 退订（生命周期契约见方案 §3.2）。
        /// </summary>
        public static event Action LanguageChanged;

        /// <summary>内联双语对查询：按当前语言取一侧文本。</summary>
        public static string Text(string english, string simplifiedChinese)
        {
            return IsChinese ? simplifiedChinese : english;
        }

        /// <summary>
        /// 设置语言（P05 Assistant-Utility 调用；P02 期间无 UI 调用方）。
        /// 写入偏好后触发 <see cref="LanguageChanged"/>。
        /// </summary>
        public static void SetLanguage(CoCoEditorLanguage language)
        {
            EditorPrefs.SetString(LanguagePreferenceKey, LanguageNames[(int)language]);
            LanguageChanged?.Invoke();
        }
    }
}

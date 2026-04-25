using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Editor.Core
{
    public class CoCoLoggerWindow : EditorWindow, IEventListener<CoCoLogEvent>
    {
        // ==========================================
        // 颜色配置区
        // ==========================================
        private readonly Dictionary<string, string> _moduleColors = new Dictionary<string, string>
        {
            { "Core" , "#FFA500" },  // 橙色
            { "Animation" , "#800080" },    //紫色
            { "Camera", "#00FF00" },    //绿色
            { "Input", "#00D0FF" },   // 亮蓝色
            { "Map" , "#808000"},   //橄榄色
            { "Network", "#FF0040" },   //红色
            { "Persistence" , "#FFC0CB"},   //粉红色
            { "Rendering" , "#A0522D"},    //茶色

            // UI 相关子模块统一使用蓝色
            { "UI", "#0066FF" },
            { "Widgets", "#0066FF" },
            { "Panels", "#0066FF" },

            // 其他模块
            { "Global", "#CCCCCC" }   // 兜底默认色
        };

        // 日志数据与状态
        private readonly List<CoCoLogEvent> _logs = new List<CoCoLogEvent>();
        private Dictionary<string, bool> _moduleFilters = new Dictionary<string, bool>();

        // 性能保护：最大日志数量，防止挂机太久把内存撑爆
        private const int MaxLogs = 1000;

        // GUI 状态
        private Vector2 _scrollPosition;
        private GUIStyle _richTextStyle;
        private bool _autoScroll = true;

        [MenuItem("CoCoFlow/Logger Console %l")] // 快捷键 Ctrl/Cmd + L
        public static void ShowWindow()
        {
            var window = GetWindow<CoCoLoggerWindow>("CoCoFlow Logger");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnEnable()
        {
            // 订阅事件总线
            EventBus.Subscribe(this);
            // 预先注册配置表中的模块过滤状态
            foreach (var key in _moduleColors.Keys)
            {
                if (!_moduleFilters.ContainsKey(key))
                    _moduleFilters[key] = true;
            }
        }

        private void OnDisable()
        {
            // 窗口关闭时退订，防止内存泄漏
            EventBus.Unsubscribe(this);
        }

        // ==========================================
        // 核心回调：接收来自 EventBus 的日志
        // ==========================================
        public void OnEvent(ref CoCoLogEvent eventData)
        {
            // 记录日志
            _logs.Add(eventData);

            // 动态添加未知的模块过滤状态
            if (!_moduleFilters.ContainsKey(eventData.ModuleName))
            {
                _moduleFilters[eventData.ModuleName] = true;
            }

            // 性能保护：超出限制则移除最旧的日志
            if (_logs.Count > MaxLogs)
            {
                _logs.RemoveAt(0);
            }

            // 触发 Editor UI 重绘
            Repaint();
        }

        // ==========================================
        // 渲染 UI
        // ==========================================
        private void OnGUI()
        {
            InitStyles();

            DrawToolbar();
            DrawFilters();
            DrawLogList();
        }

        private void InitStyles()
        {
            if (_richTextStyle == null)
            {
                _richTextStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = 12
                };
            }
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _logs.Clear();
            }

            _autoScroll = GUILayout.Toggle(_autoScroll, "Auto Scroll", EditorStyles.toolbarButton, GUILayout.Width(80));

            GUILayout.FlexibleSpace();

            // 【修复报错】使用基于公开 API 的自定义对齐样式
            GUIStyle toolbarCountStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label($"Logs: {_logs.Count} / {MaxLogs}", toolbarCountStyle);

            GUILayout.EndHorizontal();
        }

        private void DrawFilters()
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("Module Filters", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();

            // 将 Dictionary 键提取出来遍历，避免在迭代时修改字典报错
            var keys = new List<string>(_moduleFilters.Keys);

            // 简单的流式布局：每行放几个 Toggle
            int count = 0;
            foreach (var module in keys)
            {
                // 获取颜色用于着色显示 Toggle
                string hexColor = GetModuleColor(module);

                // 绘制带颜色的勾选框
                GUI.contentColor = ColorUtility.TryParseHtmlString(hexColor, out Color c) ? c : Color.white;
                _moduleFilters[module] = GUILayout.Toggle(_moduleFilters[module], module, GUILayout.Width(80));
                GUI.contentColor = Color.white; // 恢复默认颜色

                count++;
                if (count % 6 == 0) // 每行 6 个模块换行（可根据窗口宽度自适应调整，这里从简）
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawLogList()
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            for (int i = 0; i < _logs.Count; i++)
            {
                var log = _logs[i];

                // 如果该模块被取消勾选，则跳过渲染
                if (_moduleFilters.TryGetValue(log.ModuleName, out bool isVisible) && !isVisible)
                {
                    continue;
                }

                string hexColor = GetModuleColor(log.ModuleName);
                string timeStr = log.Timestamp.ToString("HH:mm:ss");

                // 使用 Rich Text 格式化字符串，利用你要求的标准范例格式
                // 范例：[CoCoFlow: 模块名称]类名称：实际内容
                string formattedMsg = $"<color=#888888>[{timeStr}]</color> <color={hexColor}>[CoCoFlow: {log.ModuleName}]</color><b>{log.ClassName}</b>: {log.Message}";

                GUILayout.Label(formattedMsg, _richTextStyle);
            }

            GUILayout.EndScrollView();

            // 自动滚动到底部
            if (_autoScroll && Event.current.type == EventType.Repaint)
            {
                var lastRect = GUILayoutUtility.GetLastRect();
                _scrollPosition.y = lastRect.y + lastRect.height;
            }
        }

        private string GetModuleColor(string moduleName)
        {
            if (_moduleColors.TryGetValue(moduleName, out string colorStr))
            {
                return colorStr;
            }
            return "#FFFFFF"; // 如果字典里没配，默认白色
        }
    }
}

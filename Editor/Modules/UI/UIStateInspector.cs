using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CoCoFlow.Runtime.Modules.UI;

namespace CoCoFlow.Editor.Modules.UI
{
    public class UIStateMonitor : EditorWindow
    {
        private Vector2 _scrollPosition;
        private UIManager _uiManager;

        // GUI 样式缓存
        private GUIStyle _headerStyle;
        private GUIStyle _itemStyle;
        private GUIStyle _activeItemStyle;

        [MenuItem("CoCoFlow/UI/UI State Monitor")]
        public static void ShowWindow()
        {
            var window = GetWindow<UIStateMonitor>("UI Monitor");
            window.minSize = new Vector2(350, 500);
            window.Show();
        }

        private void OnEnable()
        {
            // 注册 Editor 更新事件，实现窗口的实时刷新（心跳）
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.Space(10);
            GUILayout.Label("CoCoFlow UI 实时监控台", _headerStyle);
            EditorGUILayout.Space(10);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("请进入 Play Mode 以监控运行时 UI 状态。", MessageType.Info);
                return;
            }

            // 动态寻找 UIManager
            if (_uiManager == null)
            {
                _uiManager = FindFirstObjectByType<UIManager>();
            }

            if (_uiManager == null)
            {
                EditorGUILayout.HelpBox("当前场景未找到 UIManager 实例。", MessageType.Warning);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawPanelStackSection();
            EditorGUILayout.Space(15);

            DrawSceneUISection();
            EditorGUILayout.Space(15);

            DrawIndicatorSection();

            EditorGUILayout.EndScrollView();

            // 强制重新绘制以保持数据实时性
            Repaint();
        }

        /// <summary>
        /// 绘制 Panel 堆栈状态 (通过反射获取内部 _panelStack)
        /// </summary>
        private void DrawPanelStackSection()
        {
            GUILayout.Label("📚 面板栈 (Panel Stack)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            // 使用反射获取 UIManager 的私有栈
            FieldInfo stackField = typeof(UIManager).GetField("_panelStack", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stackField != null)
            {
                var stack = stackField.GetValue(_uiManager) as Stack<UIPanelBase>;
                if (stack == null || stack.Count == 0)
                {
                    GUILayout.Label("  [Stack Empty] 没有任何面板被 Push", EditorStyles.label);
                }
                else
                {
                    // Stack 的枚举顺序是从顶到底
                    int index = stack.Count - 1;
                    foreach (var panel in stack)
                    {
                        bool isTop = (index == stack.Count - 1);
                        GUIStyle style = isTop ? _activeItemStyle : _itemStyle;

                        EditorGUILayout.BeginHorizontal();

                        // 1. 显示层级和名字
                        GUILayout.Label($"[{index}] {panel.gameObject.name}", style, GUILayout.Width(200));

                        // 2. 动态拼装当前面板的 Config 状态标签
                        List<string> tags = new List<string>();

                        tags.Add(panel.Config.HasFlag(UIPanelConfig.HideLowerPanels) ? "HideLowers" : "Overlay");

                        if (panel.Config.HasFlag(UIPanelConfig.PauseGame)) tags.Add("Pause");
                        if (panel.Config.HasFlag(UIPanelConfig.TakeInputFocus)) tags.Add("Input");
                        if (panel.Config.HasFlag(UIPanelConfig.ShowCursor)) tags.Add("Cursor");

                        string statusText = string.Join(" | ", tags);

                        // 3. 显示状态标签（你可以自己调个好看的颜色）
                        GUI.contentColor = isTop ? Color.cyan : Color.gray;
                        GUILayout.Label(statusText);
                        GUI.contentColor = Color.white; // 记得重置颜色

                        EditorGUILayout.EndHorizontal();

                        index--;
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("无法通过反射读取 _panelStack，请检查变量名。", MessageType.Error);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制世界 UI (Scene UI) 状态
        /// </summary>
        private void DrawSceneUISection()
        {
            GUILayout.Label("🌍 世界 UI (Scene UI)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            var sceneUIs = FindObjectsByType<UISceneBase>(FindObjectsSortMode.None);
            if (sceneUIs.Length == 0)
            {
                GUILayout.Label("  当前没有激活的 Scene UI", EditorStyles.label);
            }
            else
            {
                foreach (var sceneUI in sceneUIs)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"- {sceneUI.SceneUIName}", _itemStyle);

                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        EditorGUIUtility.PingObject(sceneUI.gameObject);
                        Selection.activeGameObject = sceneUI.gameObject;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制指示器 (Indicator) 状态与日志
        /// </summary>
        private void DrawIndicatorSection()
        {
            GUILayout.Label("📊 指示器 (Indicators & HUD)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            var indicators = FindObjectsByType<UIIndicatorBase>(FindObjectsSortMode.None);
            if (indicators.Length == 0)
            {
                GUILayout.Label("  当前没有激活的 Indicator", EditorStyles.label);
            }
            else
            {
                foreach (var ind in indicators)
                {
                    EditorGUILayout.BeginVertical("HelpBox");

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(ind.gameObject.name, EditorStyles.boldLabel);
                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        EditorGUIUtility.PingObject(ind.gameObject);
                    }
                    EditorGUILayout.EndHorizontal();

                    // 显示 Indicator 的数据日志
                    EditorGUILayout.LabelField("最后接收数据:", ind.LastReceivedDataLog, EditorStyles.wordWrappedLabel);

                    EditorGUILayout.EndVertical();
                }
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 初始化界面文本样式
        /// </summary>
        private void InitStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter
                };

                _itemStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 12
                };

                _activeItemStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    normal = { textColor = new Color(0.2f, 0.8f, 0.2f) } // 顶部面板用绿色高亮
                };
            }
        }
    }
}

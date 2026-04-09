using UnityEngine;
using UnityEditor;
using System.IO;
using CoCoFlow.Runtime.Modules.Persistence;

namespace CoCoFlow.Editor.Modules.Persistence
{
    public class SaveLoadEditor : EditorWindow
    {
        // 在 Unity 顶部菜单栏创建入口
        [MenuItem("CoCoFlow/Persistence/存档加载编辑器")]
        public static void ShowWindow()
        {
            // 弹出一个小窗口
            GetWindow<SaveLoadEditor>("存档加载编辑器");
        }

        private void OnGUI()
        {
            GUILayout.Label("环境: Editor Debug 模式", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 1. 配置槽位上限
            SaveLoadSystem.MaxSaveSlots = EditorGUILayout.IntField("支持的最大槽位数量", SaveLoadSystem.MaxSaveSlots);

            // 2. 指定下次读盘的槽位 (使用 EditorPrefs 保证你关了 Unity 明天再开，设置还在)
            int currentDebugSlot = EditorPrefs.GetInt("CoCo_DebugSaveSlot", 0);
            int newDebugSlot = EditorGUILayout.IntSlider("当前测试槽位", currentDebugSlot, 0, SaveLoadSystem.MaxSaveSlots - 1);
            
            if (newDebugSlot != currentDebugSlot)
            {
                EditorPrefs.SetInt("CoCo_DebugSaveSlot", newDebugSlot);
                SaveLoadSystem.CurrentSlotIndex = newDebugSlot; // 同步给运行时系统
            }

            EditorGUILayout.Space();

            // 3. 一键清空测试文件 (加上红底色醒目提醒)
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("一键清除所有测试存档 (*.json)", GUILayout.Height(35)))
            {
                ClearAllTestSaves();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space();
            
            // 4. 辅助功能：快捷打开文件夹
            if (GUILayout.Button("在系统文件管理器中打开存档目录"))
            {
                string dir = Path.Combine(Application.dataPath, "CoCoFlow/Test/Saves");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                EditorUtility.RevealInFinder(dir);
            }
        }

        private void ClearAllTestSaves()
        {
            string dir = Path.Combine(Application.dataPath, "CoCoFlow/Test/Saves");
            
            if (!Directory.Exists(dir))
            {
                Debug.Log("未找到存档目录，无需清理。");
                return;
            }

            string[] files = Directory.GetFiles(dir, "*.json");
            if (files.Length == 0)
            {
                Debug.Log("目录下没有存档文件。");
                return;
            }

            // 弹窗二次确认防误触
            if (EditorUtility.DisplayDialog("警告", $"确定要删除该目录下的 {files.Length} 个存档文件吗？此操作不可逆！", "删！", "取消"))
            {
                foreach (string file in files)
                {
                    File.Delete(file);
                }
                
                // 刷新 AssetDatabase，让 Unity 和 Rider 知道文件已经被删了
                AssetDatabase.Refresh();
                Debug.Log($"[CoCoFlow] 成功清理了 {files.Length} 个测试存档！");
            }
        }
    }
}

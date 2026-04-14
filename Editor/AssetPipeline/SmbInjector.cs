using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Editor.AssetPipeline
{
    public class SmbInjector : EditorWindow
    {
        private AnimatorController _targetController;
        private bool _clearExistingFirst = false;

        [MenuItem("CoCoFlow/AssetPipeline/SMB 注入器")]
        public static void ShowWindow()
        {
            var window = GetWindow<SmbInjector>("SMB 注入器");
            window.minSize = new Vector2(400, 250);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            GUILayout.Label("CoCoFlow 动画状态机 SMB 注入管线", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("该工具将遍历目标 Animator Controller 的所有层级和子状态机，一键注入 AnimationEventSmb。", MessageType.Info);

            EditorGUILayout.Space(10);
            _targetController = (AnimatorController)EditorGUILayout.ObjectField("目标 Animator Controller", _targetController, typeof(AnimatorController), false);

            EditorGUILayout.Space(10);
            _clearExistingFirst = EditorGUILayout.ToggleLeft("注入前先清除旧的 AnimationEventSmb", _clearExistingFirst);

            EditorGUILayout.Space(20);

            if (_targetController == null)
            {
                EditorGUILayout.HelpBox("请指定一个 Animator Controller 以继续。", MessageType.Warning);
                GUI.enabled = false;
            }

            if (GUILayout.Button("一键执行全面注入", GUILayout.Height(40)))
            {
                ExecuteInjection();
            }

            GUI.enabled = true;
        }

        private void ExecuteInjection()
        {
            if (_targetController == null) return;

            int injectedCount = 0;
            int clearedCount = 0;

            try
            {
                // 记录 Undo，防止操作失误导致状态机报废
                Undo.RecordObject(_targetController, "Inject SMBs");

                // 遍历所有的动画层 (Layers)
                foreach (var layer in _targetController.layers)
                {
                    ProcessStateMachine(layer.stateMachine, ref injectedCount, ref clearedCount);
                }
            }
            finally
            {
                // 保存修改
                EditorUtility.SetDirty(_targetController);
                AssetDatabase.SaveAssets();

                string msg = $"注入完成！\n目标: {_targetController.name}\n成功注入了 {injectedCount} 个状态。";
                if (_clearExistingFirst) msg += $"\n清理了 {clearedCount} 个旧的 SMB。";

                EditorUtility.DisplayDialog("处理完毕", msg, "确定");
            }
        }

        // 递归处理状态机 (应对 Sub-State Machines)
        private void ProcessStateMachine(AnimatorStateMachine stateMachine, ref int injectedCount, ref int clearedCount)
        {
            // 1. 处理当前层级的所有独立 State
            foreach (var childState in stateMachine.states)
            {
                ProcessState(childState.state, ref injectedCount, ref clearedCount);
            }

            // 2. 递归处理内部嵌套的子状态机
            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                ProcessStateMachine(childStateMachine.stateMachine, ref injectedCount, ref clearedCount);
            }
        }

        // 核心注入逻辑
        private void ProcessState(AnimatorState state, ref int injectedCount, ref int clearedCount)
        {
            // 清理模式：干掉已经存在的 AnimationEventSmb
            if (_clearExistingFirst)
            {
                var behaviours = state.behaviours;
                for (int i = behaviours.Length - 1; i >= 0; i--)
                {
                    if (behaviours[i] is AnimationEventSmb)
                    {
                        // 在 Unity 底层，销毁 SMB 需要用 Object.DestroyImmediate 并带上 true 允许销毁资产参数
                        DestroyImmediate(behaviours[i], true);
                        clearedCount++;
                    }
                }
            }

            // 防重复检测：如果没开启清理模式，且已经有该 SMB，则跳过
            if (!_clearExistingFirst)
            {
                foreach (var b in state.behaviours)
                {
                    if (b is AnimationEventSmb) return;
                }
            }

            // 注入新的 SMB
            state.AddStateMachineBehaviour<AnimationEventSmb>();
            injectedCount++;
        }
    }
}

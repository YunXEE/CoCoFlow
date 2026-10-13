using System;
using System.Linq;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed partial class CoCoStateGraphEditorWindow
    {
        private bool DrawFieldConfig(VisualElement parent, CoCoStateGraphStateRecord state)
        {
            if (controller.Catalog == null ||
                !controller.Catalog.TryGetStateDescriptor(ToStateDescriptorId(state.StateDescriptorId), out var descriptor) ||
                descriptor.AuthoringConfigType != typeof(CoCoFieldConfig)) return false;
            parent.Add(new IMGUIContainer(() =>
            {
                var logic = (CoCoStateLogic)Activator.CreateInstance(descriptor.LogicType);
                var config = state.Config as CoCoFieldConfig ?? new CoCoFieldConfig();
                config.Apply(logic);
                EditorGUILayout.LabelField("State Config", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!CoCoStateGraphAuthoringOperations.CanEdit(out _)))
                foreach (string path in CoCoStateFields.ConfigPaths(descriptor.LogicType))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    object value = DrawConfigValue(path, CoCoStateFields.ReadPath(logic, path));
                    bool changed = EditorGUI.EndChangeCheck();
                    bool reset = GUILayout.Button("Reset", GUILayout.Width(45));
                    EditorGUILayout.EndHorizontal();
                    if (!changed && !reset) continue;
                    config = new CoCoFieldConfig { Values = config.Values.ToArray() };
                    if (reset) config.Values = config.Values.Where(entry => !entry.StartsWith(path + "=", StringComparison.Ordinal)).ToArray();
                    else config.Set(path, value);
                    serializedAsset.Update();
                    FindStateConfigProperty(controller.Session.SelectedLayerId, ToStateId(state.StateId)).managedReferenceValue = config;
                    serializedAsset.ApplyModifiedProperties();
                    controller.NotifyConfigChanged();
                }
            }));
            return true;
        }

        private static object DrawConfigValue(string label, object value)
        {
            if (value is float f) return EditorGUILayout.FloatField(label, f);
            if (value is double d) return EditorGUILayout.DoubleField(label, d);
            if (value is bool b) return EditorGUILayout.Toggle(label, b);
            if (value is Enum e) return EditorGUILayout.EnumPopup(label, e);
            return Convert.ChangeType(EditorGUILayout.DelayedTextField(label,
                Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)),
                value.GetType(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

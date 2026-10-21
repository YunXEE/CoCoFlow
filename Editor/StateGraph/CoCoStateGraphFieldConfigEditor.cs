using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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
            var config = state.Config as CoCoFieldConfig ?? new CoCoFieldConfig();
            CoCoStateLogic logic = null;
            string error = null;
            try
            {
                logic = (CoCoStateLogic)Activator.CreateInstance(descriptor.LogicType);
                config.Apply(logic);
            }
            catch (Exception exception) { error = exception.GetBaseException().Message; }
            var inputs = new Dictionary<string, (string Text, string Error)>();
            parent.Add(new IMGUIContainer(() =>
            {
                EditorGUILayout.LabelField("State Config", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!CoCoStateGraphAuthoringOperations.CanEdit(out _)))
                {
                    if (GUILayout.Button(state.Config is CoCoFieldConfig ? "Reset Config" : "Initialize Config"))
                    {
                        config = new CoCoFieldConfig();
                        logic = (CoCoStateLogic)Activator.CreateInstance(descriptor.LogicType);
                        error = null;
                        inputs.Clear();
                        GUI.FocusControl(null);
                        SaveFieldConfig(state, config);
                        GUIUtility.ExitGUI();
                        return;
                    }
                    if (error != null) { EditorGUILayout.HelpBox(error, MessageType.Error); return; }
                    foreach (string path in CoCoStateFields.ConfigPaths(descriptor.LogicType))
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.BeginVertical();
                        bool changed = false;
                        bool structure = false;
                        object current = CoCoStateFields.ReadPath(logic, path);
                        Type type = descriptor.LogicType;
                        foreach (string member in path.Split('.')) type = type.GetField(member).FieldType;
                        object value = DrawConfigValue(path, type, current, inputs, ref changed, ref structure);
                        EditorGUILayout.EndVertical();
                        bool reset = GUILayout.Button("Reset", GUILayout.Width(45));
                        EditorGUILayout.EndHorizontal();
                        if (!changed && !reset) continue;
                        config = new CoCoFieldConfig { Values = config.Values.ToArray() };
                        if (reset) config.Values = config.Values.Where(entry => !CoCoFieldConfig.ContainsPath(entry, path)).ToArray();
                        else config.Set(path, value);
                        logic = (CoCoStateLogic)Activator.CreateInstance(descriptor.LogicType);
                        config.Apply(logic);
                        if (reset || structure)
                        {
                            inputs.Clear();
                            GUI.FocusControl(null);
                        }
                        SaveFieldConfig(state, config);
                        if (reset || structure) GUIUtility.ExitGUI();
                    }
                }
            }));
            return true;
        }

        private void SaveFieldConfig(CoCoStateGraphStateRecord state, CoCoFieldConfig config)
        {
            serializedAsset.Update();
            FindStateConfigProperty(controller.Session.SelectedLayerId, ToStateId(state.StateId)).managedReferenceValue = config;
            serializedAsset.ApplyModifiedProperties();
            controller.NotifyConfigChanged();
        }

        private static object DrawConfigValue(string label, Type type, object value,
            Dictionary<string, (string Text, string Error)> inputs, ref bool changed, ref bool structure)
        {
            if (!CoCoStateFields.IsConfigType(type))
            {
                EditorGUILayout.HelpBox("Unsupported Config type: " + type.Name, MessageType.Error);
                return value;
            }
            Type element = CoCoStateFields.ConfigElementType(type);
            if (element != null)
            {
                if (value == null)
                {
                    if (!GUILayout.Button("Initialize " + label)) return null;
                    changed = structure = true;
                    return CoCoStateFields.CreateConfigCollection(type, 0);
                }
                var items = (IList)value;
                EditorGUILayout.LabelField(label, "Count: " + items.Count);
                int remove = -1;
                int move = -1;
                int direction = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Element " + i);
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("Up")) { move = i; direction = -1; }
                    using (new EditorGUI.DisabledScope(i + 1 == items.Count))
                        if (GUILayout.Button("Down")) { move = i; direction = 1; }
                    if (GUILayout.Button("Remove")) remove = i;
                    EditorGUILayout.EndHorizontal();
                    items[i] = DrawConfigValue(label + "[" + i + "]", element, items[i], inputs, ref changed, ref structure);
                }
                bool add = GUILayout.Button("Add " + label);
                if (!add && remove < 0 && move < 0) return items;
                var ordered = items.Cast<object>().ToList();
                if (add) ordered.Add(Activator.CreateInstance(element));
                if (remove >= 0) ordered.RemoveAt(remove);
                if (move >= 0)
                {
                    object moving = ordered[move];
                    ordered[move] = ordered[move + direction];
                    ordered[move + direction] = moving;
                }
                var result = CoCoStateFields.CreateConfigCollection(type, ordered.Count);
                for (int i = 0; i < ordered.Count; i++) result[i] = ordered[i];
                changed = structure = true;
                return result;
            }
            if (!CoCoStateFields.IsConfigScalar(type))
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    field.SetValue(value, DrawConfigValue(label + "." + field.Name, field.FieldType,
                        field.GetValue(value), inputs, ref changed, ref structure));
                return value;
            }
            if (value is bool || value is Enum)
            {
                EditorGUI.BeginChangeCheck();
                object result = value is bool b ? EditorGUILayout.Toggle(label, b) :
                    EditorGUILayout.EnumPopup(label, (Enum)value);
                changed |= EditorGUI.EndChangeCheck();
                return result;
            }
            if (!inputs.TryGetValue(label, out var input))
                input = (value is float f ? f.ToString("R", CultureInfo.InvariantCulture) :
                    value is double d ? d.ToString("R", CultureInfo.InvariantCulture) :
                    Convert.ToString(value, CultureInfo.InvariantCulture), null);
            string text = EditorGUILayout.DelayedTextField(label, input.Text);
            if (text != input.Text)
            {
                input = (text, null);
                try
                {
                    value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                    changed = true;
                }
                catch (Exception exception) when (exception is FormatException || exception is OverflowException)
                {
                    input.Error = exception.Message;
                }
            }
            inputs[label] = input;
            if (input.Error != null) EditorGUILayout.HelpBox(input.Error, MessageType.Error);
            return value;
        }
    }
}

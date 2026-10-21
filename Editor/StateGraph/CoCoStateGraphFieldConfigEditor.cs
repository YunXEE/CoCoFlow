using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Linq;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditorInternal;
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
            var fields = new ConfigFieldGUI();
            parent.Add(new IMGUIContainer(() =>
            {
                using (new EditorGUI.DisabledScope(!CoCoStateGraphAuthoringOperations.CanEdit(out _)))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("State Config", EditorStyles.boldLabel);
                    bool resetConfig = GUILayout.Button(state.Config is CoCoFieldConfig ? "Reset" : "Initialize",
                        EditorStyles.miniButton, GUILayout.Width(65));
                    EditorGUILayout.EndHorizontal();
                    if (resetConfig)
                    {
                        config = new CoCoFieldConfig();
                        logic = (CoCoStateLogic)Activator.CreateInstance(descriptor.LogicType);
                        error = null;
                        fields.Clear();
                        GUI.FocusControl(null);
                        SaveFieldConfig(state, config);
                        GUIUtility.ExitGUI();
                        return;
                    }
                    if (error != null) { EditorGUILayout.HelpBox(error, MessageType.Error); return; }
                    foreach (string path in CoCoStateFields.ConfigPaths(descriptor.LogicType))
                    {
                        fields.BeginField();
                        object current = CoCoStateFields.ReadPath(logic, path);
                        Type type = descriptor.LogicType;
                        foreach (string member in path.Split('.')) type = type.GetField(member).FieldType;
                        Rect position = EditorGUILayout.GetControlRect(false, fields.GetHeight(path, type, current));
                        object value = fields.DrawValue(position, path, FieldLabel(path), type, current);
                        if (!fields.Changed) continue;
                        config = new CoCoFieldConfig { Values = config.Values.ToArray() };
                        config.Set(path, value);
                        logic = (CoCoStateLogic)Activator.CreateInstance(descriptor.LogicType);
                        config.Apply(logic);
                        if (fields.StructureChanged)
                        {
                            fields.ClearInputs();
                            GUI.FocusControl(null);
                        }
                        SaveFieldConfig(state, config);
                        if (fields.StructureChanged) GUIUtility.ExitGUI();
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

        private static GUIContent FieldLabel(string path)
        {
            string name = path.Substring(path.LastIndexOf('.') + 1);
            return new GUIContent(ObjectNames.NicifyVariableName(name), path);
        }

        private sealed class ConfigFieldGUI
        {
            private readonly Dictionary<string, (string Text, string Error)> _inputs = new();
            private readonly Dictionary<string, bool> _expanded = new();
            private readonly Dictionary<string, ReorderableList> _lists = new();
            private static float Line => EditorGUIUtility.singleLineHeight;
            private static float Gap => EditorGUIUtility.standardVerticalSpacing;
            internal bool Changed { get; private set; }
            internal bool StructureChanged { get; private set; }

            internal void BeginField() { Changed = StructureChanged = false; }
            internal void ClearInputs() => _inputs.Clear();
            internal void Clear() { _inputs.Clear(); _lists.Clear(); }
            private bool Expanded(string path) => !_expanded.TryGetValue(path, out bool expanded) || expanded;

            internal float GetHeight(string path, Type type, object value)
            {
                if (!CoCoStateFields.IsConfigType(type)) return Line * 2;
                if (CoCoStateFields.ConfigElementType(type) != null)
                    return value == null || !Expanded(path) ? Line :
                        Line + Gap + GetList(path, type, (IList)value).GetHeight();
                if (!CoCoStateFields.IsConfigScalar(type))
                    return !Expanded(path) ? Line : Line + type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Sum(field => Gap + GetHeight(path + "." + field.Name, field.FieldType, field.GetValue(value)));
                return _inputs.TryGetValue(path, out var input) && input.Error != null ? Line * 3 + Gap : Line;
            }

            internal object DrawValue(Rect position, string path, GUIContent label, Type type, object value)
            {
                if (!CoCoStateFields.IsConfigType(type))
                {
                    EditorGUI.HelpBox(position, "Unsupported Config type: " + type.Name, MessageType.Error);
                    return value;
                }
                var row = new Rect(position.x, position.y, position.width, Line);
                if (CoCoStateFields.ConfigElementType(type) != null)
                {
                    if (value == null)
                    {
                        Rect button = EditorGUI.PrefixLabel(row, label);
                        if (!GUI.Button(button, "Initialize", EditorStyles.miniButton)) return null;
                        Changed = StructureChanged = true;
                        return CoCoStateFields.CreateConfigCollection(type, 0);
                    }
                    var items = (IList)value;
                    row.width -= 35;
                    _expanded[path] = EditorGUI.Foldout(row, Expanded(path), label, true);
                    EditorGUI.LabelField(new Rect(row.xMax, row.y, 35, Line), items.Count.ToString(), EditorStyles.miniLabel);
                    if (!Expanded(path)) return items;
                    var list = GetList(path, type, items);
                    list.DoList(new Rect(position.x, position.y + Line + Gap, position.width, list.GetHeight()));
                    return list.list;
                }
                if (!CoCoStateFields.IsConfigScalar(type))
                {
                    _expanded[path] = EditorGUI.Foldout(row, Expanded(path), label, true);
                    if (!Expanded(path)) return value;
                    float y = row.yMax + Gap;
                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        string child = path + "." + field.Name;
                        object current = field.GetValue(value);
                        float height = GetHeight(child, field.FieldType, current);
                        field.SetValue(value, DrawValue(new Rect(position.x + 15, y, position.width - 15, height),
                            child, FieldLabel(child), field.FieldType, current));
                        y += height + Gap;
                    }
                    return value;
                }
                object result;
                if (value is float f) result = EditorGUI.DelayedFloatField(row, label, f);
                else if (value is double d) result = EditorGUI.DelayedDoubleField(row, label, d);
                else if (value is int i) result = EditorGUI.DelayedIntField(row, label, i);
                else if (value is bool b) result = EditorGUI.Toggle(row, label, b);
                else if (value is Enum e) result = EditorGUI.EnumPopup(row, label, e);
                else result = DrawNumericText(new Rect(row.x, row.y, row.width, position.height), path, label, type, value);
                Changed |= !Equals(value, result);
                return result;
            }

            private object DrawNumericText(Rect position, string path, GUIContent label, Type type, object value)
            {
                if (!_inputs.TryGetValue(path, out var input))
                    input = (Convert.ToString(value, CultureInfo.InvariantCulture), null);
                string text = EditorGUI.DelayedTextField(new Rect(position.x, position.y, position.width, Line), label, input.Text);
                if (text != input.Text)
                {
                    input = (text, null);
                    try { value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture); }
                    catch (Exception exception) when (exception is FormatException || exception is OverflowException)
                    { input.Error = exception.Message; }
                }
                _inputs[path] = input;
                if (input.Error != null)
                    EditorGUI.HelpBox(new Rect(position.x, position.y + Line + Gap, position.width, Line * 2), input.Error, MessageType.Error);
                return value;
            }

            private ReorderableList GetList(string path, Type type, IList items)
            {
                if (_lists.TryGetValue(path, out var list)) { list.list = items; return list; }
                Type element = CoCoStateFields.ConfigElementType(type);
                list = new ReorderableList(items, element, true, false, true, true) { headerHeight = 0 };
                list.elementHeightCallback = index => GetHeight(path + "[" + index + "]", element, list.list[index]) + Gap * 2;
                list.drawElementCallback = (rect, index, active, focused) =>
                {
                    rect.y += Gap;
                    list.list[index] = DrawValue(rect, path + "[" + index + "]", new GUIContent("Element " + index), element, list.list[index]);
                };
                list.onAddCallback = _ =>
                {
                    list.list = Resize(type, list.list, list.count + 1, -1);
                    list.index = list.count - 1;
                    Changed = StructureChanged = true;
                };
                list.onRemoveCallback = _ =>
                {
                    int index = list.index < 0 ? list.count - 1 : list.index;
                    list.list = Resize(type, list.list, list.count - 1, index);
                    list.index = Math.Min(index, list.count - 1);
                    Changed = StructureChanged = true;
                };
                list.onReorderCallback = _ => { Changed = StructureChanged = true; };
                _lists.Add(path, list);
                return list;
            }

            private static IList Resize(Type type, IList items, int count, int remove)
            {
                var result = CoCoStateFields.CreateConfigCollection(type, count);
                for (int source = 0, target = 0; source < items.Count; source++)
                    if (source != remove) result[target++] = items[source];
                return result;
            }
        }
    }
}

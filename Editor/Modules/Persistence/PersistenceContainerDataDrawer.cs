using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    internal static class PersistenceContainerDataDrawer
    {
        internal static object Draw(Type type, object value, string label)
        {
            if (PersistenceDataTypeValidator.IsLeaf(type)) return DrawLeaf(type, value, label);
            if (PersistenceDataTypeValidator.TryGetElementType(type, out Type element))
                return DrawCollection(type, element, value, label);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (FieldInfo field in PersistenceDataTypeValidator.GetFields(type))
                    field.SetValue(value, Draw(field.FieldType, field.GetValue(value), ObjectNames.NicifyVariableName(field.Name)));
            }
            return value;
        }

        private static object DrawLeaf(Type type, object value, string label)
        {
            if (type == typeof(string))
            {
                bool wasNull = value == null;
                bool isNull = EditorGUILayout.Toggle(label + " (null)", wasNull);
                if (isNull) return null;
                return EditorGUILayout.TextField(label, (string)value ?? string.Empty);
            }
            if (type == typeof(bool)) return EditorGUILayout.Toggle(label, (bool)value);
            if (type.IsEnum)
                return type.IsDefined(typeof(FlagsAttribute), false)
                    ? EditorGUILayout.EnumFlagsField(label, (Enum)value)
                    : EditorGUILayout.EnumPopup(label, (Enum)value);
            string text = type == typeof(char) && (char)value == '\0'
                ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
            string next = EditorGUILayout.DelayedTextField(label, text);
            if (next == text) return value;
            try
            {
                return type == typeof(char) ? (next.Length == 0 ? '\0' : next[0])
                    : Convert.ChangeType(next, type, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException)
            {
                EditorGUILayout.HelpBox($"{label}: enter a valid {type.Name} value.", MessageType.Warning);
                return value;
            }
        }

        private static object DrawCollection(Type type, Type element, object value, string label)
        {
            if (EditorGUILayout.Toggle(label + " (null)", value == null)) return null;
            var list = (IList)(value ?? (type.IsArray ? Array.CreateInstance(element, 0) : Activator.CreateInstance(type)));
            int count = Mathf.Max(0, EditorGUILayout.DelayedIntField(label + " Size", list.Count));
            if (type.IsArray && count != list.Count)
            {
                Array resized = Array.CreateInstance(element, count);
                Array.Copy((Array)list, resized, Math.Min(count, list.Count));
                list = resized;
            }
            else if (!type.IsArray)
            {
                while (list.Count > count) list.RemoveAt(list.Count - 1);
                while (list.Count < count) list.Add(element.IsValueType ? Activator.CreateInstance(element) : null);
            }
            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < list.Count; i++)
                    list[i] = Draw(element, list[i], label + " [" + i + "]");
            }
            return list;
        }
    }
}

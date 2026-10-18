using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    internal static class PersistenceContainerCodec
    {
        private static readonly JsonSerializer LeafSerializer = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.None
        };

        internal static JObject Encode(Type type, object value)
        {
            PersistenceDataTypeValidator.Validate(type);
            if (value == null || value.GetType() != type)
                throw new ArgumentException($"Expected a value of {type.FullName}.");
            return (JObject)Write(type, value, 0);
        }

        internal static object Decode(Type type, JObject data)
        {
            PersistenceDataTypeValidator.Validate(type);
            return Read(type, data, type.FullName, 0);
        }

        internal static object Clone(Type type, object value) => Decode(type, Encode(type, value));

        internal static object ParseInitialData(Type type, string json)
        {
            return Decode(type, JObject.Parse(string.IsNullOrEmpty(json) ? "{}" : json));
        }

        internal static string FormatInitialData(Type type, object value)
        {
            return Encode(type, value).ToString(Formatting.None);
        }

        internal static void ValidateSection(PersistenceContainerSection section)
        {
            if (section.FormatVersion != PersistenceContainerSection.CurrentFormatVersion)
                throw new JsonSerializationException($"Unsupported Container format version {section.FormatVersion}.");
            if (section.Records == null)
                throw new JsonSerializationException("Container records must be an array.");
            var keys = new HashSet<(Type, string)>();
            foreach (PersistenceContainerRecord record in section.Records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Id))
                    throw new JsonSerializationException("Container record requires a nonempty ID.");
                Type type = PersistenceDataTypeRegistry.Resolve(record.TypeId);
                if (!keys.Add((type, record.Id)))
                    throw new JsonSerializationException($"Duplicate Container key '{record.TypeId}:{record.Id}'.");
                Decode(type, record.Data);
            }
        }

        private static JToken Write(Type type, object value, int depth)
        {
            CheckDepth(depth);
            if (value == null) return JValue.CreateNull();
            if (PersistenceDataTypeValidator.IsLeaf(type)) return JToken.FromObject(value, LeafSerializer);
            if (PersistenceDataTypeValidator.TryGetElementType(type, out Type element))
            {
                var array = new JArray();
                foreach (object item in (IEnumerable)value) array.Add(Write(element, item, depth + 1));
                return array;
            }
            var data = new JObject();
            foreach (FieldInfo field in PersistenceDataTypeValidator.GetFields(type))
                data.Add(field.Name, Write(field.FieldType, field.GetValue(value), depth + 1));
            return data;
        }

        private static object Read(Type type, JToken token, string path, int depth)
        {
            CheckDepth(depth);
            if (token == null || token.Type == JTokenType.Null)
            {
                if (!type.IsValueType) return null;
                throw new JsonSerializationException($"{path}: null is not valid for {type.Name}.");
            }
            if (PersistenceDataTypeValidator.IsLeaf(type))
            {
                bool valid = type == typeof(string) || type == typeof(char)
                    ? token.Type == JTokenType.String
                    : type == typeof(bool) ? token.Type == JTokenType.Boolean
                    : type.IsEnum ? token.Type == JTokenType.Integer || token.Type == JTokenType.String
                    : type == typeof(float) || type == typeof(double)
                        ? token.Type == JTokenType.Integer || token.Type == JTokenType.Float ||
                          token.Type == JTokenType.String && new[] { "NaN", "Infinity", "-Infinity" }.Contains((string)token)
                        : token.Type == JTokenType.Integer;
                if (!valid) throw new JsonSerializationException($"{path}: invalid JSON value for {type.Name}.");
                return token.ToObject(type, LeafSerializer);
            }
            if (PersistenceDataTypeValidator.TryGetElementType(type, out Type element))
            {
                if (!(token is JArray values)) throw new JsonSerializationException($"{path}: expected an array.");
                if (type.IsArray)
                {
                    Array array = Array.CreateInstance(element, values.Count);
                    for (int i = 0; i < values.Count; i++) array.SetValue(Read(element, values[i], path + $"[{i}]", depth + 1), i);
                    return array;
                }
                var list = (IList)Activator.CreateInstance(type);
                for (int i = 0; i < values.Count; i++) list.Add(Read(element, values[i], path + $"[{i}]", depth + 1));
                return list;
            }
            if (!(token is JObject data)) throw new JsonSerializationException($"{path}: expected an object.");
            object result = Activator.CreateInstance(type);
            foreach (FieldInfo field in PersistenceDataTypeValidator.GetFields(type))
                if (data.TryGetValue(field.Name, StringComparison.Ordinal, out JToken child))
                    field.SetValue(result, Read(field.FieldType, child, path + "." + field.Name, depth + 1));
            return result;
        }

        private static void CheckDepth(int depth)
        {
            if (depth > 64) throw new JsonSerializationException("Container data exceeds the JSON nesting limit.");
        }
    }

    internal sealed class PersistenceContainerSectionJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(PersistenceContainerSection);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return new PersistenceContainerSection();
            JObject data = JObject.Load(reader);
            if (!data.TryGetValue("formatVersion", out JToken version))
            {
                if (data.Count == 0 || data["records"] is JArray empty && empty.Count == 0)
                    return new PersistenceContainerSection();
                if (data["records"] is JArray legacy && legacy.Count > 0 &&
                    legacy.All(item => item is JObject record && record["containerId"]?.Type == JTokenType.String))
                {
                    Debug.LogWarning("[Persistence] Old Container gameplay data was discarded; Graph data is preserved. No migration was performed.");
                    return new PersistenceContainerSection();
                }
                throw new JsonSerializationException("Unrecognized Container section without a format version.");
            }
            if (version.Type != JTokenType.Integer || (int)version != PersistenceContainerSection.CurrentFormatVersion)
                throw new JsonSerializationException($"Unsupported Container format version '{version}'.");
            if (!(data["records"] is JArray records))
                throw new JsonSerializationException("Container records must be an array.");
            var section = new PersistenceContainerSection();
            foreach (JToken item in records)
            {
                if (!(item is JObject record) || record["typeId"]?.Type != JTokenType.String ||
                    record["id"]?.Type != JTokenType.String || !(record["data"] is JObject payload))
                    throw new JsonSerializationException("Container record requires typeId, id and object data.");
                section.Records.Add(new PersistenceContainerRecord
                {
                    TypeId = (string)record["typeId"], Id = (string)record["id"], Data = payload
                });
            }
            PersistenceContainerCodec.ValidateSection(section);
            return section;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            var section = (PersistenceContainerSection)value;
            PersistenceContainerCodec.ValidateSection(section);
            writer.WriteStartObject();
            writer.WritePropertyName("formatVersion");
            writer.WriteValue(section.FormatVersion);
            writer.WritePropertyName("records");
            serializer.Serialize(writer, section.Records);
            writer.WriteEndObject();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoCoFlow.Editor.Core
{
    /// <summary>Setup manifest 文档句柄：解析后的根对象 + 变更标记（自窗口私有嵌套迁出，原样）。</summary>
    internal sealed class ManifestDocument
    {
        public ManifestDocument(JsonObject root)
        {
            Root = root;
        }

        public JsonObject Root { get; }
        public bool Changed { get; set; }
    }

    internal abstract class JsonValue
    {
        public abstract string ToJson(int indent);

        protected static string Indent(int count)
        {
            return new string(' ', count);
        }

        protected static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }
    }

    internal sealed class JsonObject : JsonValue
    {
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, JsonValue> _values = new Dictionary<string, JsonValue>();

        public void Set(string key, JsonValue value)
        {
            if (!_values.ContainsKey(key))
                _keys.Add(key);

            _values[key] = value;
        }

        public bool TryGetString(string key, out string value)
        {
            if (_values.TryGetValue(key, out var jsonValue) && jsonValue is JsonString jsonString)
            {
                value = jsonString.Value;
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetArray(string key, out JsonArray value)
        {
            if (_values.TryGetValue(key, out var jsonValue) && jsonValue is JsonArray jsonArray)
            {
                value = jsonArray;
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetObject(string key, out JsonObject value)
        {
            if (_values.TryGetValue(key, out var jsonValue) && jsonValue is JsonObject jsonObject)
            {
                value = jsonObject;
                return true;
            }

            value = null;
            return false;
        }

        public override string ToJson(int indent)
        {
            if (_keys.Count == 0)
                return "{}";

            var builder = new StringBuilder();
            builder.AppendLine("{");
            for (var i = 0; i < _keys.Count; i++)
            {
                var key = _keys[i];
                builder.Append(Indent(indent + 2));
                builder.Append(Quote(key));
                builder.Append(": ");
                builder.Append(_values[key].ToJson(indent + 2));
                if (i < _keys.Count - 1)
                    builder.Append(',');
                builder.AppendLine();
            }
            builder.Append(Indent(indent));
            builder.Append('}');
            return builder.ToString();
        }
    }

    internal sealed class JsonArray : JsonValue
    {
        public readonly List<JsonValue> Items = new List<JsonValue>();

        public override string ToJson(int indent)
        {
            if (Items.Count == 0)
                return "[]";

            var builder = new StringBuilder();
            builder.AppendLine("[");
            for (var i = 0; i < Items.Count; i++)
            {
                builder.Append(Indent(indent + 2));
                builder.Append(Items[i].ToJson(indent + 2));
                if (i < Items.Count - 1)
                    builder.Append(',');
                builder.AppendLine();
            }
            builder.Append(Indent(indent));
            builder.Append(']');
            return builder.ToString();
        }
    }

    internal sealed class JsonString : JsonValue
    {
        public JsonString(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public override string ToJson(int indent)
        {
            return Quote(Value);
        }
    }

    internal sealed class JsonRaw : JsonValue
    {
        public JsonRaw(string value)
        {
            Value = value;
        }

        private string Value { get; }

        public override string ToJson(int indent)
        {
            return Value;
        }
    }

    internal sealed class JsonParser
    {
        private readonly string _text;
        private int _index;

        public JsonParser(string text)
        {
            _text = text;
        }

        public JsonValue Parse()
        {
            SkipWhitespace();
            var value = ParseValue();
            SkipWhitespace();
            if (_index != _text.Length)
                throw Error("Unexpected trailing characters.");

            return value;
        }

        private JsonValue ParseValue()
        {
            SkipWhitespace();
            if (_index >= _text.Length)
                throw Error("Unexpected end of JSON.");

            var c = _text[_index];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return new JsonString(ParseString());
            if (c == '-' || char.IsDigit(c)) return new JsonRaw(ParseNumber());
            if (MatchLiteral("true")) return new JsonRaw("true");
            if (MatchLiteral("false")) return new JsonRaw("false");
            if (MatchLiteral("null")) return new JsonRaw("null");

            throw Error("Unexpected JSON token '" + c + "'.");
        }

        private JsonObject ParseObject()
        {
            Expect('{');
            var obj = new JsonObject();
            SkipWhitespace();
            if (TryConsume('}'))
                return obj;

            while (true)
            {
                SkipWhitespace();
                var key = ParseString();
                SkipWhitespace();
                Expect(':');
                var value = ParseValue();
                obj.Set(key, value);
                SkipWhitespace();

                if (TryConsume('}'))
                    return obj;

                Expect(',');
            }
        }

        private JsonArray ParseArray()
        {
            Expect('[');
            var array = new JsonArray();
            SkipWhitespace();
            if (TryConsume(']'))
                return array;

            while (true)
            {
                array.Items.Add(ParseValue());
                SkipWhitespace();

                if (TryConsume(']'))
                    return array;

                Expect(',');
            }
        }

        private string ParseString()
        {
            Expect('"');
            var builder = new StringBuilder();

            while (_index < _text.Length)
            {
                var c = _text[_index++];
                if (c == '"')
                    return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (_index >= _text.Length)
                    throw Error("Unexpected end of string escape.");

                var escaped = _text[_index++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        builder.Append(ParseUnicodeEscape());
                        break;
                    default:
                        throw Error("Invalid string escape '\\" + escaped + "'.");
                }
            }

            throw Error("Unterminated string.");
        }

        private char ParseUnicodeEscape()
        {
            if (_index + 4 > _text.Length)
                throw Error("Incomplete unicode escape.");

            var hex = _text.Substring(_index, 4);
            _index += 4;
            return (char)Convert.ToInt32(hex, 16);
        }

        private string ParseNumber()
        {
            var start = _index;
            if (_text[_index] == '-')
                _index++;

            while (_index < _text.Length && char.IsDigit(_text[_index]))
                _index++;

            if (_index < _text.Length && _text[_index] == '.')
            {
                _index++;
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                    _index++;
            }

            if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
            {
                _index++;
                if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-'))
                    _index++;

                while (_index < _text.Length && char.IsDigit(_text[_index]))
                    _index++;
            }

            return _text.Substring(start, _index - start);
        }

        private bool MatchLiteral(string literal)
        {
            if (_index + literal.Length > _text.Length)
                return false;

            if (string.Compare(_text, _index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                return false;

            _index += literal.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                _index++;
        }

        private void Expect(char expected)
        {
            SkipWhitespace();
            if (_index >= _text.Length || _text[_index] != expected)
                throw Error("Expected '" + expected + "'.");
            _index++;
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            if (_index >= _text.Length || _text[_index] != expected)
                return false;

            _index++;
            return true;
        }

        private Exception Error(string message)
        {
            return new InvalidDataException(message + " At character " + _index + ".");
        }
    }
}

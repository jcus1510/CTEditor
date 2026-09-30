using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CTEditor.Project
{
    /// <summary>
    /// Un objeto JSON que conserva el orden de sus claves (los archivos del proyecto se leen mejor así y los cambios se ven
    /// limpios en git). Valores: JsonObject, List&lt;object&gt;, string, double, bool o null.
    /// </summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

        public int Count => _keys.Count;
        public IEnumerable<string> Keys => _keys;
        public bool Has(string key) => _values.ContainsKey(key);

        public object this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : null;
            set
            {
                if (!_values.ContainsKey(key)) _keys.Add(key);
                _values[key] = value;
            }
        }

        public JsonObject Set(string key, object value) { this[key] = value; return this; }

        public bool Remove(string key)
        {
            if (!_values.Remove(key)) return false;
            _keys.Remove(key);
            return true;
        }

        public string GetString(string key, string fallback = null) => this[key] is string s ? s : fallback;
        public double GetNumber(string key, double fallback = 0) => this[key] is double d ? d : fallback;
        public int GetInt(string key, int fallback = 0) => this[key] is double d ? (int)Math.Round(d) : fallback;
        public float GetFloat(string key, float fallback = 0) => this[key] is double d ? (float)d : fallback;
        public bool GetBool(string key, bool fallback = false) => this[key] is bool b ? b : fallback;
        public JsonObject GetObject(string key) => this[key] as JsonObject;
        public List<object> GetArray(string key) => this[key] as List<object>;

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var k in _keys) yield return new KeyValuePair<string, object>(k, _values[k]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// JSON pequeño y sin dependencias (funciona igual en Unity y en dotnet test). Números como double; los enteros se
    /// escriben sin decimales. Los errores dicen línea y columna, en español.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var p = new Parser(text ?? "");
            p.SkipWhite();
            var v = p.Value();
            p.SkipWhite();
            if (!p.End) throw p.Error("sobra texto después del final");
            return v;
        }

        public static JsonObject ParseObject(string text) =>
            Parse(text) as JsonObject ?? throw new FormatException("JSON: se esperaba un objeto { ... }.");

        public static string Write(object value, bool indented = true)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, indented, 0);
            if (indented) sb.Append('\n');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v, bool ind, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: WriteNumber(sb, d); break;
                case float f: WriteNumber(sb, f); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case JsonObject o:
                    if (o.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, ind, depth + 1);
                        WriteString(sb, kv.Key);
                        sb.Append(ind ? ": " : ":");
                        WriteValue(sb, kv.Value, ind, depth + 1);
                    }
                    NewLine(sb, ind, depth);
                    sb.Append('}');
                    break;
                case IEnumerable list:
                {
                    var items = new List<object>();
                    foreach (var x in list) items.Add(x);
                    if (items.Count == 0) { sb.Append("[]"); break; }
                    // Short lists of plain values stay on one line ([1, 2, 3]).
                    bool flat = items.Count <= 16 && items.TrueForAll(x => !(x is JsonObject) && (x is string || !(x is IEnumerable)));
                    sb.Append('[');
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (i > 0) sb.Append(flat && ind ? ", " : ",");
                        if (!flat) NewLine(sb, ind, depth + 1);
                        WriteValue(sb, items[i], ind, depth + 1);
                    }
                    if (!flat) NewLine(sb, ind, depth);
                    sb.Append(']');
                    break;
                }
                default: throw new ArgumentException($"JSON: no sé escribir un valor de tipo {v.GetType().Name}.");
            }
        }

        private static void NewLine(StringBuilder sb, bool ind, int depth)
        {
            if (!ind) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append('0'); return; }
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; }
            public bool End => _i >= _s.Length;

            public void SkipWhite()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
                if (_i == 0 && _s.Length > 0 && _s[0] == '﻿') { _i++; SkipWhite(); }
            }

            public FormatException Error(string what)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                    if (_s[k] == '\n') { line++; col = 1; } else col++;
                return new FormatException($"JSON: {what} (línea {line}, columna {col}).");
            }

            public object Value()
            {
                if (End) throw Error("falta un valor");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return String();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                    default:
                        if (c == '-' || char.IsDigit(c)) return Number();
                        throw Error($"carácter inesperado «{c}»");
                }
            }

            private void Word(string w)
            {
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) throw Error($"se esperaba «{w}»");
                _i += w.Length;
            }

            private void Expect(char c)
            {
                SkipWhite();
                if (End || _s[_i] != c) throw Error($"se esperaba «{c}»");
                _i++;
            }

            private JsonObject Object()
            {
                var o = new JsonObject();
                _i++;
                SkipWhite();
                if (!End && _s[_i] == '}') { _i++; return o; }
                while (true)
                {
                    SkipWhite();
                    if (End || _s[_i] != '"') throw Error("se esperaba el nombre de una clave entre comillas");
                    var key = String();
                    Expect(':');
                    SkipWhite();
                    o[key] = Value();
                    SkipWhite();
                    if (End) throw Error("falta «}»");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return o; }
                    throw Error("se esperaba «,» o «}»");
                }
            }

            private List<object> Array()
            {
                var list = new List<object>();
                _i++;
                SkipWhite();
                if (!End && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    SkipWhite();
                    list.Add(Value());
                    SkipWhite();
                    if (End) throw Error("falta «]»");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw Error("se esperaba «,» o «]»");
                }
            }

            private string String()
            {
                _i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("falta cerrar las comillas");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("escape incompleto");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("escape \\u incompleto");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw Error($"escape desconocido «\\{e}»");
                    }
                }
            }

            private double Number()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (!End && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E' || _s[_i] == '+' || _s[_i] == '-')) _i++;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw Error("número no válido");
                return d;
            }
        }
    }
}

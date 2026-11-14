using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FanControl.Dashboard
{
    /// <summary>
    /// Minimal JSON reader and writer. The plugin cannot use System.Web.Extensions (.NET Framework only, so it would
    /// not load in Fan Control's .NET 10 build) and must stay a single DLL, so no NuGet package either.
    /// Reading gives Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, bool, null, and numbers as int, long or double.
    /// Writing produces the same text JavaScriptSerializer did, so existing settings files stay byte-compatible.
    /// </summary>
    static class Json
    {
        const int MaxDepth = 128;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var r = new Reader(text);
            r.SkipWhitespace();
            var value = r.ReadValue(0);
            r.SkipWhitespace();
            if (!r.End) throw r.Error("unexpected text after the JSON value");
            return value;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("JSON nesting too deep");
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is Enum) { sb.Append(Convert.ToInt64(v, Inv).ToString(Inv)); return; }
            if (v is double) { WriteDouble(sb, (double)v); return; }
            if (v is float) { WriteDouble(sb, (float)v); return; }
            if (v is decimal) { sb.Append(((decimal)v).ToString(Inv)); return; }
            if (v is int || v is long || v is short || v is byte || v is uint || v is ulong || v is ushort || v is sbyte)
            {
                sb.Append(Convert.ToString(v, Inv)); return;
            }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(e.Key, Inv));
                    sb.Append(':');
                    WriteValue(sb, e.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            throw new NotSupportedException("cannot write " + v.GetType().Name + " as JSON");
        }

        static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new NotSupportedException("JSON has no NaN or infinity");
            sb.Append(d.ToString("r", Inv));
        }

        /// <summary>Same escaping as JavaScriptSerializer: quotes, backslash, control characters and &lt; &gt; &amp; '.</summary>
        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '<': case '>': case '&': case '\'':
                        sb.Append("\\u").Append(((int)c).ToString("x4", Inv)); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Reader
        {
            readonly string _s;
            int _i;

            public Reader(string s) { _s = s; }

            public bool End { get { return _i >= _s.Length; } }

            public FormatException Error(string what) { return new FormatException("Invalid JSON at position " + _i + ": " + what); }

            public void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n' || _s[_i] == '﻿')) _i++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("nesting too deep");
                if (End) throw Error("unexpected end");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("unexpected character '" + c + "'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("expected " + word);
                _i += word.Length;
            }

            Dictionary<string, object> ReadObject(int depth)
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // {
                SkipWhitespace();
                if (!End && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    SkipWhitespace();
                    if (End || _s[_i] != '"') throw Error("expected a property name");
                    string key = ReadString();
                    SkipWhitespace();
                    if (End || _s[_i] != ':') throw Error("expected ':'");
                    _i++;
                    SkipWhitespace();
                    d[key] = ReadValue(depth + 1); // last one wins, like most readers
                    SkipWhitespace();
                    if (End) throw Error("unexpected end in object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return d; }
                    throw Error("expected ',' or '}'");
                }
            }

            List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (!End && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (End) throw Error("unexpected end in array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw Error("expected ',' or ']'");
                }
            }

            string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("unterminated escape");
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
                            int code;
                            if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, Inv, out code)) throw Error("bad \\u escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw Error("bad escape \\" + e);
                    }
                }
            }

            object ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (!End && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                string n = _s.Substring(start, _i - start);
                bool integral = n.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
                if (integral)
                {
                    int iv; if (int.TryParse(n, NumberStyles.AllowLeadingSign, Inv, out iv)) return iv;
                    long lv; if (long.TryParse(n, NumberStyles.AllowLeadingSign, Inv, out lv)) return lv;
                }
                double dv;
                if (double.TryParse(n, NumberStyles.Float, Inv, out dv)) return dv;
                throw Error("bad number '" + n + "'");
            }
        }
    }
}

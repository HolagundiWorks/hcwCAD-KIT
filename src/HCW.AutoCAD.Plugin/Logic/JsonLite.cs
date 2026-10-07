using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// A small JSON reader, so the plugin can read a bridge file without a JSON library (rule D3: AutoCAD ships its own Newtonsoft.Json and a second copy
    /// would clash). Values come back as Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool or null. A syntax error throws a FormatException
    /// that says where. No CAD types are used here.
    /// </summary>
    public static class JsonLite
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1;                 // a byte order mark
            Skip(text, ref i);
            var v = Value(text, ref i, 0);
            Skip(text, ref i);
            if (i < text.Length) throw Err(text, i, "unexpected text after the end of the value");
            return v;
        }

        private const int MaxDepth = 64;

        private static FormatException Err(string t, int i, string what)
        {
            int line = 1, col = 1;
            for (int k = 0; k < i && k < t.Length; k++) { if (t[k] == '\n') { line++; col = 1; } else col++; }
            return new FormatException("JSON error at line " + line + ", column " + col + ": " + what);
        }

        private static void Skip(string t, ref int i) { while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\r' || t[i] == '\n')) i++; }

        private static object Value(string t, ref int i, int depth)
        {
            if (depth > MaxDepth) throw Err(t, i, "nested too deeply");
            if (i >= t.Length) throw Err(t, i, "the file ends where a value was expected");
            char c = t[i];
            if (c == '{') return Obj(t, ref i, depth);
            if (c == '[') return Arr(t, ref i, depth);
            if (c == '"') return Str(t, ref i);
            if (c == '-' || (c >= '0' && c <= '9')) return Num(t, ref i);
            if (string.CompareOrdinal(t, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(t, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(t, i, "null", 0, 4) == 0) { i += 4; return null; }
            throw Err(t, i, "unexpected character '" + c + "'");
        }

        private static Dictionary<string, object> Obj(string t, ref int i, int depth)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; Skip(t, ref i);
            if (i < t.Length && t[i] == '}') { i++; return d; }
            while (true)
            {
                Skip(t, ref i);
                if (i >= t.Length || t[i] != '"') throw Err(t, i, "a property name in quotes was expected");
                string key = Str(t, ref i);
                Skip(t, ref i);
                if (i >= t.Length || t[i] != ':') throw Err(t, i, "':' was expected");
                i++; Skip(t, ref i);
                d[key] = Value(t, ref i, depth + 1);
                Skip(t, ref i);
                if (i >= t.Length) throw Err(t, i, "the file ends inside an object");
                if (t[i] == ',') { i++; continue; }
                if (t[i] == '}') { i++; return d; }
                throw Err(t, i, "',' or '}' was expected");
            }
        }

        private static List<object> Arr(string t, ref int i, int depth)
        {
            var l = new List<object>();
            i++; Skip(t, ref i);
            if (i < t.Length && t[i] == ']') { i++; return l; }
            while (true)
            {
                Skip(t, ref i);
                l.Add(Value(t, ref i, depth + 1));
                Skip(t, ref i);
                if (i >= t.Length) throw Err(t, i, "the file ends inside an array");
                if (t[i] == ',') { i++; continue; }
                if (t[i] == ']') { i++; return l; }
                throw Err(t, i, "',' or ']' was expected");
            }
        }

        private static string Str(string t, ref int i)
        {
            var b = new StringBuilder();
            i++;
            while (true)
            {
                if (i >= t.Length) throw Err(t, i, "the file ends inside a string");
                char c = t[i++];
                if (c == '"') return b.ToString();
                if (c < 0x20) throw Err(t, i - 1, "a control character inside a string");
                if (c != '\\') { b.Append(c); continue; }
                if (i >= t.Length) throw Err(t, i, "the file ends inside a string");
                char e = t[i++];
                switch (e)
                {
                    case '"': b.Append('"'); break;
                    case '\\': b.Append('\\'); break;
                    case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'u':
                        int code;
                        if (i + 4 > t.Length || !int.TryParse(t.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw Err(t, i, "a bad \\u escape");
                        b.Append((char)code); i += 4; break;
                    default: throw Err(t, i - 1, "a bad escape '\\" + e + "'");
                }
            }
        }

        private static double Num(string t, ref int i)
        {
            int start = i;
            if (t[i] == '-') i++;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.' || t[i] == 'e' || t[i] == 'E' || t[i] == '+' || t[i] == '-')) i++;
            double v;
            if (!double.TryParse(t.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw Err(t, start, "a bad number");
            return v;
        }

        // ---- typed access ----

        public static object Get(object obj, string key) { var d = obj as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) ? v : null; }
        public static string Text(object obj, string key) { var v = Get(obj, key); return v == null ? null : (v is string ? (string)v : v is double ? ((double)v).ToString("R", CultureInfo.InvariantCulture) : v.ToString()); }
        public static double? Number(object obj, string key)
        {
            var v = Get(obj, key);
            if (v is double) return (double)v;
            double d;
            return v is string && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : (double?)null;
        }
        public static bool? Bool(object obj, string key) { var v = Get(obj, key); return v is bool ? (bool)v : (bool?)null; }
        public static List<object> List(object obj, string key) => Get(obj, key) as List<object> ?? new List<object>();
    }
}

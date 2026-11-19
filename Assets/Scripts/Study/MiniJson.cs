using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Minimal JSON reader and writer (objects, arrays, strings, numbers, true/false/null).
    /// Used instead of JsonUtility so that plan files with large integers and optional fields
    /// parse predictably, and so that the parsing code can be tested outside the Unity runtime.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }
            var reader = new Reader(json);
            object value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw new FormatException("Unexpected trailing characters at position " + reader.Position);
            }
            return value;
        }

        public static string Serialize(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, bool pretty, int indent)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case IDictionary<string, object> dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> kv in dict)
                    {
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        NewLine(sb, pretty, indent + 1);
                        WriteString(sb, kv.Key);
                        sb.Append(pretty ? ": " : ":");
                        Write(sb, kv.Value, pretty, indent + 1);
                    }
                    if (!first)
                    {
                        NewLine(sb, pretty, indent);
                    }
                    sb.Append('}');
                    return;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in list)
                    {
                        if (!first)
                        {
                            sb.Append(pretty ? ", " : ",");
                        }
                        first = false;
                        Write(sb, item, pretty, indent + 1);
                    }
                    sb.Append(']');
                    return;
                }
                case float f:
                    sb.Append(FormatNumber(f));
                    return;
                case double d:
                    sb.Append(FormatNumber(d));
                    return;
                case IFormattable formattable:
                    sb.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                    return;
                default:
                    WriteString(sb, value.ToString());
                    return;
            }
        }

        private static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                return "null";
            }
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void NewLine(StringBuilder sb, bool pretty, int indent)
        {
            if (!pretty)
            {
                return;
            }
            sb.Append('\n');
            sb.Append(' ', indent * 2);
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
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ typed access helpers
        public static Dictionary<string, object> AsObject(object o, string what)
        {
            if (o is Dictionary<string, object> d)
            {
                return d;
            }
            throw new FormatException(what + " must be a JSON object");
        }

        public static List<object> GetList(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out object v) && v is List<object> l ? l : new List<object>();
        }

        public static string GetString(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (!d.TryGetValue(key, out object v) || v == null)
            {
                return fallback;
            }
            return v is string s ? s : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static double GetNumber(Dictionary<string, object> d, string key, double fallback = 0.0)
        {
            if (!d.TryGetValue(key, out object v) || v == null)
            {
                return fallback;
            }
            switch (v)
            {
                case double dd: return dd;
                case long ll: return ll;
                case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p): return p;
                default: return fallback;
            }
        }

        public static long GetLong(Dictionary<string, object> d, string key, long fallback = 0)
        {
            if (!d.TryGetValue(key, out object v) || v == null)
            {
                return fallback;
            }
            switch (v)
            {
                case long ll: return ll;
                case double dd: return (long)Math.Round(dd);
                default: return fallback;
            }
        }

        public static bool GetBool(Dictionary<string, object> d, string key, bool fallback = false)
        {
            return d.TryGetValue(key, out object v) && v is bool b ? b : fallback;
        }

        // ------------------------------------------------------------------ reader
        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s)
            {
                _s = s;
            }

            public bool AtEnd => _i >= _s.Length;
            public int Position => _i;

            public void SkipWhitespace()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                {
                    _i++;
                }
                if (_i == 0 && _s.Length > 0 && _s[0] == (char)0xFEFF)
                {
                    _i++;
                    SkipWhitespace();
                }
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw new FormatException("Unexpected end of JSON");
                }
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                {
                    throw new FormatException("Invalid token at position " + _i);
                }
                _i += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                _i++;
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == '}')
                {
                    _i++;
                    return d;
                }
                while (true)
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':')
                    {
                        throw new FormatException("Expected ':' at position " + _i);
                    }
                    _i++;
                    d[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new FormatException("Unterminated object");
                    }
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] == '}')
                    {
                        _i++;
                        return d;
                    }
                    throw new FormatException("Expected ',' or '}' at position " + _i);
                }
            }

            private List<object> ReadArray()
            {
                var l = new List<object>();
                _i++;
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == ']')
                {
                    _i++;
                    return l;
                }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new FormatException("Unterminated array");
                    }
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] == ']')
                    {
                        _i++;
                        return l;
                    }
                    throw new FormatException("Expected ',' or ']' at position " + _i);
                }
            }

            private string ReadString()
            {
                if (AtEnd || _s[_i] != '"')
                {
                    throw new FormatException("Expected string at position " + _i);
                }
                _i++;
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd)
                    {
                        break;
                    }
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
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default:
                            throw new FormatException("Invalid escape at position " + _i);
                    }
                }
                throw new FormatException("Unterminated string");
            }

            private object ReadNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0)
                {
                    _i++;
                }
                string token = _s.Substring(start, _i - start);
                if (token.Length == 0)
                {
                    throw new FormatException("Invalid value at position " + start);
                }
                if (token.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 &&
                    long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                {
                    return l;
                }
                return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Leopard2A7V;

// Minimal JSON reader for the embedded module table. Objects become
// Dictionary<string, object>, arrays become List<object>, numbers become
// double, and the remaining literals map to string / bool / null. It only
// needs to read authored files, so the parser is strict: any malformed
// content throws instead of guessing.
public static class LpMiniJson
{
    public static object Parse(string text)
    {
        if (text == null) throw new ArgumentNullException("text");
        int pos = 0;
        object value = ReadValue(text, ref pos);
        SkipSpace(text, ref pos);
        if (pos != text.Length) throw new FormatException("unexpected trailing content at offset " + pos);
        return value;
    }

    public static Dictionary<string, object> Object(object node)
    {
        return node as Dictionary<string, object>;
    }

    public static List<object> Array(object node)
    {
        return node as List<object>;
    }

    public static string String(Dictionary<string, object> obj, string key)
    {
        if (obj != null && obj.TryGetValue(key, out object value) && value is string) return (string)value;
        return null;
    }

    public static double Number(Dictionary<string, object> obj, string key)
    {
        if (obj != null && obj.TryGetValue(key, out object value) && value is double) return (double)value;
        return double.NaN;
    }

    private static object ReadValue(string text, ref int pos)
    {
        SkipSpace(text, ref pos);
        if (pos >= text.Length) throw new FormatException("unexpected end of JSON");
        char c = text[pos];
        switch (c)
        {
            case '{': return ReadObject(text, ref pos);
            case '[': return ReadArray(text, ref pos);
            case '"': return ReadString(text, ref pos);
            case 't': Expect(text, ref pos, "true"); return true;
            case 'f': Expect(text, ref pos, "false"); return false;
            case 'n': Expect(text, ref pos, "null"); return null;
            default:
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(text, ref pos);
                throw new FormatException("unexpected character '" + c + "' at offset " + pos);
        }
    }

    private static Dictionary<string, object> ReadObject(string text, ref int pos)
    {
        Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
        pos++; // '{'
        SkipSpace(text, ref pos);
        if (pos < text.Length && text[pos] == '}') { pos++; return result; }
        while (true)
        {
            SkipSpace(text, ref pos);
            if (pos >= text.Length || text[pos] != '"') throw new FormatException("expected a key at offset " + pos);
            string key = ReadString(text, ref pos);
            SkipSpace(text, ref pos);
            if (pos >= text.Length || text[pos] != ':') throw new FormatException("expected ':' at offset " + pos);
            pos++;
            result[key] = ReadValue(text, ref pos);
            SkipSpace(text, ref pos);
            if (pos >= text.Length) throw new FormatException("unterminated object");
            if (text[pos] == ',') { pos++; continue; }
            if (text[pos] == '}') { pos++; return result; }
            throw new FormatException("expected ',' or '}' at offset " + pos);
        }
    }

    private static List<object> ReadArray(string text, ref int pos)
    {
        List<object> result = new List<object>();
        pos++; // '['
        SkipSpace(text, ref pos);
        if (pos < text.Length && text[pos] == ']') { pos++; return result; }
        while (true)
        {
            result.Add(ReadValue(text, ref pos));
            SkipSpace(text, ref pos);
            if (pos >= text.Length) throw new FormatException("unterminated array");
            if (text[pos] == ',') { pos++; continue; }
            if (text[pos] == ']') { pos++; return result; }
            throw new FormatException("expected ',' or ']' at offset " + pos);
        }
    }

    private static string ReadString(string text, ref int pos)
    {
        pos++; // '"'
        StringBuilder builder = new StringBuilder();
        while (pos < text.Length)
        {
            char c = text[pos++];
            if (c == '"') return builder.ToString();
            if (c == '\\')
            {
                if (pos >= text.Length) break;
                char escape = text[pos++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > text.Length) throw new FormatException("truncated unicode escape");
                        builder.Append((char)Convert.ToInt32(text.Substring(pos, 4), 16));
                        pos += 4;
                        break;
                    default: throw new FormatException("unsupported escape '\\" + escape + "'");
                }
            }
            else builder.Append(c);
        }
        throw new FormatException("unterminated string");
    }

    private static double ReadNumber(string text, ref int pos)
    {
        int start = pos;
        while (pos < text.Length)
        {
            char c = text[pos];
            if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') pos++;
            else break;
        }
        if (!double.TryParse(text.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException("invalid number '" + text.Substring(start, pos - start) + "'");
        return value;
    }

    private static void Expect(string text, ref int pos, string literal)
    {
        if (pos + literal.Length > text.Length || text.Substring(pos, literal.Length) != literal)
            throw new FormatException("expected '" + literal + "' at offset " + pos);
        pos += literal.Length;
    }

    private static void SkipSpace(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            char c = text[pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') pos++;
            else break;
        }
    }
}

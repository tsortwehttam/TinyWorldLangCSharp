using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TinyWorldLang.Values;

namespace TinyWorldLang.Conformance
{
    internal sealed class CelSpecCase
    {
        public string Id { get; }
        public string Expression { get; }
        public Value Expected { get; }

        public CelSpecCase(string id, string expression, Value expected)
        {
            Id = id;
            Expression = expression;
            Expected = expected;
        }

        public override string ToString() => Id;
    }

    internal static class CelSpecTextProtoReader
    {
        public static IReadOnlyList<CelSpecCase> LoadDirectory(string directory)
        {
            var cases = new List<CelSpecCase>();
            foreach (var path in Directory.GetFiles(directory, "*.textproto"))
                cases.AddRange(LoadFile(path));
            return cases;
        }

        private static IEnumerable<CelSpecCase> LoadFile(string path)
        {
            string source = File.ReadAllText(path);
            string fileName = ReadFirstStringField(source, "name") ?? Path.GetFileNameWithoutExtension(path);

            foreach (var section in FindBlocks(source, "section"))
            {
                string sectionName = ReadFirstStringField(section, "name") ?? "section";
                foreach (var test in FindBlocks(section, "test"))
                {
                    if (test.IndexOf("eval_error", StringComparison.Ordinal) >= 0)
                        continue;

                    string? testName = ReadFirstStringField(test, "name");
                    string? expr = ReadFirstStringField(test, "expr");
                    if (testName == null || expr == null)
                        continue;

                    if (!TryReadExpected(test, out var expected))
                        continue;

                    yield return new CelSpecCase($"{fileName}/{sectionName}/{testName}", expr, expected);
                }
            }
        }

        private static bool TryReadExpected(string body, out Value value)
        {
            if (TryReadBoolField(body, "bool_value", out var b))
            {
                value = Value.Bool(b);
                return true;
            }
            if (TryReadLongField(body, "int64_value", out var i))
            {
                value = Value.Int(i);
                return true;
            }
            if (TryReadDoubleField(body, "double_value", out var d))
            {
                value = Value.Double(d);
                return true;
            }
            string? s = ReadFirstStringField(body, "string_value");
            if (s != null)
            {
                value = Value.String(s);
                return true;
            }
            if (body.IndexOf("null_value", StringComparison.Ordinal) >= 0)
            {
                value = Value.Null;
                return true;
            }

            value = Value.Null;
            return false;
        }

        private static IEnumerable<string> FindBlocks(string source, string keyword)
        {
            int i = 0;
            while (i < source.Length)
            {
                int start = IndexOfWord(source, keyword, i);
                if (start < 0) yield break;
                int brace = start + keyword.Length;
                while (brace < source.Length && char.IsWhiteSpace(source[brace])) brace++;
                if (brace >= source.Length || source[brace] != '{')
                {
                    i = start + keyword.Length;
                    continue;
                }

                int end = FindMatchingBrace(source, brace);
                yield return source.Substring(brace + 1, end - brace - 1);
                i = end + 1;
            }
        }

        private static int FindMatchingBrace(string source, int open)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = open; i < source.Length; i++)
            {
                char c = source[i];
                if (quote != '\0')
                {
                    if (c == '\\') { i++; continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }

                if (c == '"' || c == '\'') { quote = c; continue; }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            throw new InvalidDataException("unterminated textproto block");
        }

        private static int IndexOfWord(string source, string word, int start)
        {
            while (true)
            {
                int i = source.IndexOf(word, start, StringComparison.Ordinal);
                if (i < 0) return -1;
                bool before = i == 0 || !(char.IsLetterOrDigit(source[i - 1]) || source[i - 1] == '_');
                int afterIndex = i + word.Length;
                bool after = afterIndex >= source.Length ||
                    !(char.IsLetterOrDigit(source[afterIndex]) || source[afterIndex] == '_');
                if (before && after) return i;
                start = i + word.Length;
            }
        }

        private static string? ReadFirstStringField(string source, string field)
        {
            int i = IndexOfWord(source, field, 0);
            if (i < 0) return null;
            i += field.Length;
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            if (i >= source.Length || source[i] != ':') return null;
            i++;
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            if (i >= source.Length || (source[i] != '"' && source[i] != '\'')) return null;
            return ReadQuoted(source, ref i);
        }

        private static bool TryReadBoolField(string source, string field, out bool value)
        {
            value = false;
            string? token = ReadBareField(source, field);
            if (token == "true") { value = true; return true; }
            if (token == "false") return true;
            return false;
        }

        private static bool TryReadLongField(string source, string field, out long value)
        {
            value = 0;
            string? token = ReadBareField(source, field);
            return token != null && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadDoubleField(string source, string field, out double value)
        {
            value = 0;
            string? token = ReadBareField(source, field);
            return token != null && double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string? ReadBareField(string source, string field)
        {
            int i = IndexOfWord(source, field, 0);
            if (i < 0) return null;
            i += field.Length;
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            if (i >= source.Length || source[i] != ':') return null;
            i++;
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            int start = i;
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] != '}') i++;
            return source.Substring(start, i - start);
        }

        private static string ReadQuoted(string source, ref int i)
        {
            char quote = source[i++];
            var chars = new List<char>();
            while (i < source.Length)
            {
                char c = source[i++];
                if (c == quote) return new string(chars.ToArray());
                if (c != '\\') { chars.Add(c); continue; }
                if (i >= source.Length) throw new InvalidDataException("unterminated escape");
                char e = source[i++];
                switch (e)
                {
                    case 'n': chars.Add('\n'); break;
                    case 'r': chars.Add('\r'); break;
                    case 't': chars.Add('\t'); break;
                    case 'b': chars.Add('\b'); break;
                    case 'f': chars.Add('\f'); break;
                    case '\\': chars.Add('\\'); break;
                    case '"': chars.Add('"'); break;
                    case '\'': chars.Add('\''); break;
                    default: chars.Add(e); break;
                }
            }
            throw new InvalidDataException("unterminated string");
        }
    }
}

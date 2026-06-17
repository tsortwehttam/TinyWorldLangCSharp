using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TinyWorldLang.Values;

namespace TinyWorldLang.Parsing
{
    /// <summary>
    /// A hand-written, single-pass parser for TWL world source. No codegen, no
    /// dependencies — just a character scanner producing <see cref="Statement"/>s.
    /// </summary>
    public sealed class TwlParser
    {
        private readonly string _src;
        private int _pos;
        private int _line = 1;

        private TwlParser(string source) => _src = source ?? throw new ArgumentNullException(nameof(source));

        public static ParsedWorld Parse(string source)
        {
            var p = new TwlParser(source);
            var statements = new List<Statement>();
            while (true)
            {
                p.SkipTrivia();
                if (p.AtEnd) break;
                statements.Add(p.ParseStatement());
            }
            return new ParsedWorld(statements);
        }

        private bool AtEnd => _pos >= _src.Length;
        private char Cur => _src[_pos];

        private char Advance()
        {
            char c = _src[_pos++];
            if (c == '\n') _line++;
            return c;
        }

        // ---- Statement ----

        private Statement ParseStatement()
        {
            int line = _line;
            string first = ParseIdentifier("subject/type");

            // A module-level function declaration: `fun NAME(p1, p2) = EXPR;`.
            if (first == "fun")
                return ParseFunctionDecl(line);

            if (Reserved.Contains(first))
                throw new TwlLoadException($"'{first}' is reserved and cannot be used as an entity name", line);

            SkipTrivia();
            string relation = ParseIdentifier("relation");

            if (TryBuiltinRelation(relation, out var builtin))
            {
                SkipTrivia();
                string right = ParseEntityIdentifier("entity");
                ExpectSemicolon();
                return new StructuralFact(first, builtin, right, line);
            }

            SkipTrivia();

            // A parameter rule: `TYPE RELATION(p1, p2) = EXPR;`.
            if (!AtEnd && Cur == '(')
            {
                var ps = ParseParameterList();
                SkipTrivia();
                if (AtEnd || Cur != '=')
                    throw new TwlLoadException("expected '=' after a parameter list", line);
                Advance(); // consume '='
                string body = ScanExpressionToSemicolon().Trim();
                if (body.Length == 0)
                    throw new TwlLoadException("parameter rule has an empty expression", line);
                return new ComputedFact(first, relation, body, line, ps);
            }

            if (!AtEnd && Cur == '=')
            {
                Advance(); // consume '='
                string expr = ScanExpressionToSemicolon().Trim();
                if (expr.Length == 0)
                    throw new TwlLoadException("computed fact has an empty expression", line);
                return new ComputedFact(first, relation, expr, line);
            }

            Value value = ParseValue();
            ExpectSemicolon();
            return new StoredFact(first, relation, value, line);
        }

        private FunctionDecl ParseFunctionDecl(int line)
        {
            string name = ParseIdentifier("function name");
            if (Reserved.Contains(name))
                throw new TwlLoadException($"'{name}' is reserved and cannot be used as a function name", line);
            SkipTrivia();
            if (AtEnd || Cur != '(')
                throw new TwlLoadException("expected '(' after a function name", line);
            var ps = ParseParameterList();
            SkipTrivia();
            if (AtEnd || Cur != '=')
                throw new TwlLoadException("expected '=' after a function's parameter list", line);
            Advance(); // consume '='
            string body = ScanExpressionToSemicolon().Trim();
            if (body.Length == 0)
                throw new TwlLoadException("function has an empty expression", line);
            return new FunctionDecl(name, ps, body, line);
        }

        /// <summary>Parse <c>( p1, p2, ... )</c>; assumes the cursor is at the '('.</summary>
        private IReadOnlyList<string> ParseParameterList()
        {
            Advance(); // consume '('
            var ps = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            SkipTrivia();
            if (!AtEnd && Cur != ')')
            {
                while (true)
                {
                    string p = ParseIdentifier("parameter name");
                    if (!seen.Add(p))
                        throw new TwlLoadException($"duplicate parameter '{p}'", _line);
                    ps.Add(p);
                    SkipTrivia();
                    if (!AtEnd && Cur == ',') { Advance(); SkipTrivia(); continue; }
                    break;
                }
            }
            SkipTrivia();
            if (AtEnd || Cur != ')')
                throw new TwlLoadException("expected ')' to close a parameter list", _line);
            Advance(); // consume ')'
            return ps;
        }

        private static bool TryBuiltinRelation(string name, out BuiltinRelation rel)
        {
            switch (name)
            {
                case "instanceof": rel = BuiltinRelation.InstanceOf; return true;
                case "extends": rel = BuiltinRelation.Extends; return true;
                case "sameas": rel = BuiltinRelation.SameAs; return true;
                default: rel = default; return false;
            }
        }

        // ---- Values ----

        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "instanceof", "extends", "sameas", "true", "false", "null",
            "self", "now", "instances", "sortBy", "one", "rand", "math", "cel", "fun",
        };

        private Value ParseValue()
        {
            SkipTrivia();
            if (AtEnd) throw new TwlLoadException("expected a value", _line);

            char c = Cur;
            if (c == '"') return Value.String(ScanStringSource());

            if (c == '{') return ScanRecord();

            if (c == '-' || char.IsDigit(c)) return ScanNumber();

            // identifier-like: true/false, null (error), or an entity id
            string id = ParseIdentifier("value");
            switch (id)
            {
                case "true": return Value.Bool(true);
                case "false": return Value.Bool(false);
                case "null":
                    throw new TwlLoadException("null is never a stored value", _line);
                default:
                    if (Reserved.Contains(id))
                        throw new TwlLoadException($"'{id}' is reserved and cannot be used as an entity name", _line);
                    return Value.Entity(id);
            }
        }

        /// <summary>
        /// Scan a stored record literal <c>{ field: value, field: value }</c>. Field
        /// values are literal <see cref="Value"/>s (strings, numbers, bools, entities,
        /// or nested records) — parsed recursively. A trailing comma is allowed.
        /// </summary>
        private Value ScanRecord()
        {
            Advance(); // consume '{'
            var fields = new Dictionary<string, Value>(StringComparer.Ordinal);
            SkipTrivia();
            if (!AtEnd && Cur != '}')
            {
                while (true)
                {
                    string key = ParseIdentifier("field name");
                    SkipTrivia();
                    if (AtEnd || Cur != ':')
                        throw new TwlLoadException("expected ':' after a record field name", _line);
                    Advance(); // consume ':'
                    Value v = ParseValue(); // recursive; SkipTrivia handled inside
                    if (fields.ContainsKey(key))
                        throw new TwlLoadException($"duplicate field '{key}' in record", _line);
                    fields[key] = v;
                    SkipTrivia();
                    if (!AtEnd && Cur == ',') { Advance(); SkipTrivia(); if (!AtEnd && Cur == '}') break; continue; }
                    break;
                }
            }
            SkipTrivia();
            if (AtEnd || Cur != '}')
                throw new TwlLoadException("expected '}' to close a record", _line);
            Advance(); // consume '}'
            return Value.Record(fields);
        }

        private Value ScanNumber()
        {
            int start = _pos;
            if (Cur == '-') Advance();
            bool isDouble = false;
            while (!AtEnd && (char.IsDigit(Cur) || Cur == '.'))
            {
                if (Cur == '.') isDouble = true;
                Advance();
            }
            string text = _src.Substring(start, _pos - start);
            if (isDouble)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw new TwlLoadException($"invalid number '{text}'", _line);
                return Value.Double(d);
            }
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                throw new TwlLoadException($"invalid integer '{text}'", _line);
            return Value.Int(i);
        }

        /// <summary>
        /// Scan a <c>"..."</c> string, returning the *raw* source between the quotes
        /// with backslash escapes still intact. The template renderer decodes them so
        /// it can tell an escaped <c>\{</c> from a tag-opening <c>{{</c>.
        /// </summary>
        private string ScanStringSource()
        {
            Advance(); // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw new TwlLoadException("unterminated string", _line);
                char c = Advance();
                if (c == '"') break; // unescaped closing quote
                if (c == '\\')
                {
                    if (AtEnd) throw new TwlLoadException("unterminated escape", _line);
                    char next = Advance();
                    // Validate the escape now (a stray backslash is an error per spec),
                    // but keep the escape sequence raw for the renderer to decode.
                    switch (next)
                    {
                        case '"': case '{': case '\\': case 'n': case 't':
                            sb.Append('\\').Append(next);
                            break;
                        default:
                            throw new TwlLoadException($"invalid escape '\\{next}'", _line);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Scan a CEL expression up to the terminating <c>;</c>, skipping over any
        /// <c>;</c> that sits inside a CEL string literal (single or double quoted).
        /// Comments (<c>//</c> to end of line, <c>/* ... */</c>) are trivia here too,
        /// so they may appear anywhere inside a multi-line expression; a single <c>/</c>
        /// is left intact as the division operator.
        /// </summary>
        private string ScanExpressionToSemicolon()
        {
            var sb = new StringBuilder();
            char quote = '\0';
            while (true)
            {
                if (AtEnd) throw new TwlLoadException("expected ';' to end computed fact", _line);
                char c = Cur;
                if (quote != '\0')
                {
                    sb.Append(Advance());
                    if (c == '\\' && !AtEnd) { sb.Append(Advance()); continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '/' && _pos + 1 < _src.Length)
                {
                    char n = _src[_pos + 1];
                    if (n == '/')
                    {
                        while (!AtEnd && Cur != '\n') Advance();
                        continue;
                    }
                    if (n == '*')
                    {
                        Advance(); Advance();
                        while (!AtEnd && !(Cur == '*' && _pos + 1 < _src.Length && _src[_pos + 1] == '/'))
                            Advance();
                        if (AtEnd) throw new TwlLoadException("unterminated block comment", _line);
                        Advance(); Advance();
                        sb.Append(' '); // keep tokens from gluing across a /* */ comment
                        continue;
                    }
                }
                if (c == '"' || c == '\'') { quote = c; sb.Append(Advance()); continue; }
                if (c == ';') { Advance(); break; }
                sb.Append(Advance());
            }
            return sb.ToString();
        }

        // ---- Identifiers & trivia ----

        private string ParseIdentifier(string what)
        {
            SkipTrivia();
            if (AtEnd) throw new TwlLoadException($"expected {what}", _line);
            char c = Cur;
            if (!(char.IsLetter(c) || c == '_'))
                throw new TwlLoadException($"expected {what}, found '{c}'", _line);
            int start = _pos;
            while (!AtEnd && (char.IsLetterOrDigit(Cur) || Cur == '_')) Advance();
            return _src.Substring(start, _pos - start);
        }

        private string ParseEntityIdentifier(string what)
        {
            string id = ParseIdentifier(what);
            if (Reserved.Contains(id))
                throw new TwlLoadException($"'{id}' is reserved and cannot be used as an entity name", _line);
            return id;
        }

        private void ExpectSemicolon()
        {
            SkipTrivia();
            if (AtEnd || Cur != ';')
                throw new TwlLoadException("expected ';'", _line);
            Advance();
        }

        private void SkipTrivia()
        {
            while (!AtEnd)
            {
                char c = Cur;
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { Advance(); continue; }
                if (c == '/' && _pos + 1 < _src.Length)
                {
                    char n = _src[_pos + 1];
                    if (n == '/')
                    {
                        while (!AtEnd && Cur != '\n') Advance();
                        continue;
                    }
                    if (n == '*')
                    {
                        Advance(); Advance();
                        while (!AtEnd && !(Cur == '*' && _pos + 1 < _src.Length && _src[_pos + 1] == '/'))
                            Advance();
                        if (AtEnd) throw new TwlLoadException("unterminated block comment", _line);
                        Advance(); Advance();
                        continue;
                    }
                }
                break;
            }
        }
    }
}

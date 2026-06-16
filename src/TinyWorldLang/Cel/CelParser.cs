using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TinyWorldLang.Values;

namespace TinyWorldLang.Cel
{
    /// <summary>
    /// Hand-written lexer + precedence-climbing parser for the CEL subset TWL uses.
    /// Produces a <see cref="CelExpr"/> tree; no codegen.
    /// </summary>
    internal sealed class CelParser
    {
        private readonly List<Tok> _toks;
        private int _i;

        private CelParser(List<Tok> toks) => _toks = toks;

        public static CelExpr Parse(string source)
        {
            var toks = Lex(source);
            var p = new CelParser(toks);
            var expr = p.ParseTernary();
            if (p.Peek.Type != TokType.End)
                throw new CelException($"unexpected '{p.Peek.Text}' in expression");
            return expr;
        }

        // ---------- Parser ----------

        private Tok Peek => _toks[_i];
        private Tok Next() => _toks[_i++];

        private bool Accept(TokType t)
        {
            if (Peek.Type == t) { _i++; return true; }
            return false;
        }

        private bool AcceptOp(string op)
        {
            if (Peek.Type == TokType.Op && Peek.Text == op) { _i++; return true; }
            return false;
        }

        private void Expect(TokType t, string what)
        {
            if (Peek.Type != t) throw new CelException($"expected {what}");
            _i++;
        }

        private CelExpr ParseTernary()
        {
            var cond = ParseBinary(0);
            if (AcceptOp("?"))
            {
                var a = ParseTernary();
                if (!AcceptOp(":")) throw new CelException("expected ':' in conditional");
                var b = ParseTernary();
                return new CelTernary(cond, a, b);
            }
            return cond;
        }

        // Precedence levels, low to high.
        private static readonly string[][] Levels =
        {
            new[] { "||" },
            new[] { "&&" },
            new[] { "==", "!=" },
            new[] { "<", "<=", ">", ">=", "in" },
            new[] { "+", "-" },
            new[] { "*", "/", "%" },
        };

        private CelExpr ParseBinary(int level)
        {
            if (level >= Levels.Length) return ParseUnary();
            var left = ParseBinary(level + 1);
            while (true)
            {
                string? matched = null;
                foreach (var op in Levels[level])
                {
                    if (Peek.Type == TokType.Op && Peek.Text == op) { matched = op; break; }
                    if (op == "in" && Peek.Type == TokType.Ident && Peek.Text == "in") { matched = op; break; }
                }
                if (matched == null) break;
                _i++;
                var right = ParseBinary(level + 1);
                left = new CelBinary(matched, left, right);
            }
            return left;
        }

        private CelExpr ParseUnary()
        {
            if (Peek.Type == TokType.Op && (Peek.Text == "!" || Peek.Text == "-"))
            {
                string op = Next().Text;
                return new CelUnary(op, ParseUnary());
            }
            return ParsePostfix();
        }

        private CelExpr ParsePostfix()
        {
            var expr = ParsePrimary();
            while (true)
            {
                if (AcceptOp("."))
                {
                    if (Peek.Type != TokType.Ident) throw new CelException("expected name after '.'");
                    string name = Next().Text;
                    if (Peek.Type == TokType.Punct && Peek.Text == "(")
                        expr = new CelCall(expr, name, ParseArgs());
                    else
                        expr = new CelMember(expr, name);
                }
                else if (Peek.Type == TokType.Punct && Peek.Text == "[")
                {
                    _i++;
                    var index = ParseTernary();
                    if (!(Peek.Type == TokType.Punct && Peek.Text == "]"))
                        throw new CelException("expected ']'");
                    _i++;
                    expr = new CelIndex(expr, index);
                }
                else break;
            }
            return expr;
        }

        private CelExpr ParsePrimary()
        {
            var t = Peek;
            switch (t.Type)
            {
                case TokType.Number:
                    _i++;
                    return new CelLit(t.NumberValue);
                case TokType.String:
                    _i++;
                    return new CelLit(Value.String(t.Text));
                case TokType.Ident:
                    _i++;
                    if (t.Text == "true") return new CelLit(Value.Bool(true));
                    if (t.Text == "false") return new CelLit(Value.Bool(false));
                    if (t.Text == "null") return new CelLit(Value.Null);
                    // global call?
                    if (Peek.Type == TokType.Punct && Peek.Text == "(")
                        return new CelCall(null, t.Text, ParseArgs());
                    return new CelIdent(t.Text);
                case TokType.Punct when t.Text == "(":
                    _i++;
                    var inner = ParseTernary();
                    if (!(Peek.Type == TokType.Punct && Peek.Text == ")"))
                        throw new CelException("expected ')'");
                    _i++;
                    return inner;
                case TokType.Punct when t.Text == "[":
                    return ParseListLiteral();
                default:
                    throw new CelException($"unexpected '{t.Text}'");
            }
        }

        private CelExpr ParseListLiteral()
        {
            _i++; // '['
            var items = new List<CelExpr>();
            if (!(Peek.Type == TokType.Punct && Peek.Text == "]"))
            {
                do { items.Add(ParseTernary()); } while (AcceptPunct(","));
            }
            if (!(Peek.Type == TokType.Punct && Peek.Text == "]"))
                throw new CelException("expected ']'");
            _i++;
            return new CelListExpr(items);
        }

        private List<CelExpr> ParseArgs()
        {
            _i++; // '('
            var args = new List<CelExpr>();
            if (!(Peek.Type == TokType.Punct && Peek.Text == ")"))
            {
                do { args.Add(ParseTernary()); } while (AcceptPunct(","));
            }
            if (!(Peek.Type == TokType.Punct && Peek.Text == ")"))
                throw new CelException("expected ')'");
            _i++;
            return args;
        }

        private bool AcceptPunct(string p)
        {
            if (Peek.Type == TokType.Punct && Peek.Text == p) { _i++; return true; }
            return false;
        }

        // ---------- Lexer ----------

        private enum TokType { Ident, Number, String, Op, Punct, End }

        private readonly struct Tok
        {
            public readonly TokType Type;
            public readonly string Text;
            public readonly Value NumberValue;
            public Tok(TokType type, string text, Value number = default)
            { Type = type; Text = text; NumberValue = number; }
        }

        private static List<Tok> Lex(string s)
        {
            var toks = new List<Tok>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    toks.Add(new Tok(TokType.Ident, s.Substring(start, i - start)));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    bool isDouble = false;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
                    {
                        if (s[i] == '.') isDouble = true;
                        i++;
                    }
                    string num = s.Substring(start, i - start);
                    Value v = isDouble
                        ? Value.Double(double.Parse(num, CultureInfo.InvariantCulture))
                        : Value.Int(long.Parse(num, CultureInfo.InvariantCulture));
                    toks.Add(new Tok(TokType.Number, num, v));
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < s.Length && s[i] != c)
                    {
                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            char e = s[i + 1];
                            sb.Append(e switch
                            {
                                'n' => '\n',
                                't' => '\t',
                                '\\' => '\\',
                                '"' => '"',
                                '\'' => '\'',
                                _ => e,
                            });
                            i += 2;
                            continue;
                        }
                        sb.Append(s[i++]);
                    }
                    if (i >= s.Length) throw new CelException("unterminated string literal");
                    i++; // closing quote
                    toks.Add(new Tok(TokType.String, sb.ToString()));
                    continue;
                }

                // multi-char operators
                string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
                if (two == "&&" || two == "||" || two == "==" || two == "!=" ||
                    two == "<=" || two == ">=")
                {
                    toks.Add(new Tok(TokType.Op, two));
                    i += 2;
                    continue;
                }

                switch (c)
                {
                    case '+': case '-': case '*': case '/': case '%':
                    case '<': case '>': case '!': case '?': case ':': case '.':
                        toks.Add(new Tok(TokType.Op, c.ToString()));
                        i++;
                        break;
                    case '(': case ')': case '[': case ']': case ',':
                        toks.Add(new Tok(TokType.Punct, c.ToString()));
                        i++;
                        break;
                    default:
                        throw new CelException($"unexpected character '{c}'");
                }
            }
            toks.Add(new Tok(TokType.End, "<end>"));
            return toks;
        }
    }
}

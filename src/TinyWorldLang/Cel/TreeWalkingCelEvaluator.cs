using System;
using System.Collections.Generic;
using System.Globalization;
using TinyWorldLang.Eval;
using TinyWorldLang.Values;

namespace TinyWorldLang.Cel
{
    /// <summary>
    /// The default CEL backend: a pure tree-walking interpreter. No Reflection.Emit,
    /// no Expression.Compile — safe under IL2CPP/AOT and on consoles/WebGL.
    /// </summary>
    /// <remarks>
    /// Covers the subset the spec documents: <c>map/filter/exists/all</c>, <c>in</c>,
    /// <c>?:</c>, member/index access, the <c>math.*</c> helpers, <c>one/sortBy/
    /// instances/rand/size/int/double/string/bool</c>, and <c>now</c> date methods.
    /// Correctness is meant to be pinned to the official cel-spec conformance corpus
    /// (see tests/TinyWorldLang.Conformance).
    /// </remarks>
    public sealed class TreeWalkingCelEvaluator : ICelEvaluator
    {
        public static readonly TreeWalkingCelEvaluator Instance = new TreeWalkingCelEvaluator();

        public ICelProgram Compile(string source) => new Program(CelParser.Parse(source));

        private sealed class Program : ICelProgram
        {
            private readonly CelExpr _root;
            public Program(CelExpr root) => _root = root;
            public Value Evaluate(ICelContext context) => Eval(_root, new Scope(null), context);
        }

        /// <summary>A lexical scope for <c>self</c> and macro loop variables.</summary>
        private sealed class Scope
        {
            private readonly Scope? _parent;
            private string? _name;
            private Value _value;

            public Scope(Scope? parent) => _parent = parent;

            public Scope Bind(string name, Value value) =>
                new Scope(this) { _name = name, _value = value };

            public bool TryGet(string name, out Value value)
            {
                for (var s = this; s != null; s = s._parent)
                {
                    if (s._name == name) { value = s._value; return true; }
                }
                value = Value.Null;
                return false;
            }
        }

        private static Value Eval(CelExpr e, Scope scope, ICelContext ctx)
        {
            switch (e)
            {
                case CelLit lit: return lit.Value;
                case CelIdent id: return EvalIdent(id.Name, scope, ctx);
                case CelListExpr list: return EvalList(list, scope, ctx);
                case CelUnary u: return EvalUnary(u, scope, ctx);
                case CelBinary b: return EvalBinary(b, scope, ctx);
                case CelTernary t: return EvalTernary(t, scope, ctx);
                case CelMember m: return EvalMember(m, scope, ctx);
                case CelIndex ix: return EvalIndex(ix, scope, ctx);
                case CelCall c: return EvalCall(c, scope, ctx);
                default: throw new CelException("unknown expression node");
            }
        }

        private static Value EvalIdent(string name, Scope scope, ICelContext ctx)
        {
            if (scope.TryGet(name, out var v)) return v;
            if (name == "self") return ctx.Self;
            if (name == "now") return Value.Timestamp(ctx.Now);
            // Any other bare identifier is an entity (the spec: "any id is an entity").
            return ctx.CanonicalEntity(name);
        }

        private static Value EvalList(CelListExpr list, Scope scope, ICelContext ctx)
        {
            var items = new List<Value>(list.Items.Count);
            foreach (var item in list.Items) items.Add(Eval(item, scope, ctx));
            return Value.List(items);
        }

        private static Value EvalUnary(CelUnary u, Scope scope, ICelContext ctx)
        {
            var v = Eval(u.Operand, scope, ctx);
            switch (u.Op)
            {
                case "!":
                    if (v.Kind != ValueKind.Bool) throw new CelException("'!' requires a bool");
                    return Value.Bool(!v.AsBool);
                case "-":
                    if (v.Kind == ValueKind.Int) return Value.Int(-v.AsInt);
                    if (v.Kind == ValueKind.Double) return Value.Double(-v.AsDouble);
                    throw new CelException("unary '-' requires a number");
                default:
                    throw new CelException($"unknown unary operator '{u.Op}'");
            }
        }

        private static Value EvalTernary(CelTernary t, Scope scope, ICelContext ctx)
        {
            var cond = Eval(t.Cond, scope, ctx);
            if (cond.Kind != ValueKind.Bool) throw new CelException("condition of '?:' must be a bool");
            return Eval(cond.AsBool ? t.IfTrue : t.IfFalse, scope, ctx);
        }

        private static Value EvalBinary(CelBinary b, Scope scope, ICelContext ctx)
        {
            // Short-circuit logical operators.
            if (b.Op == "&&" || b.Op == "||")
            {
                var l = Eval(b.Left, scope, ctx);
                if (l.Kind != ValueKind.Bool) throw new CelException($"'{b.Op}' requires bool operands");
                if (b.Op == "&&" && !l.AsBool) return Value.Bool(false);
                if (b.Op == "||" && l.AsBool) return Value.Bool(true);
                var r = Eval(b.Right, scope, ctx);
                if (r.Kind != ValueKind.Bool) throw new CelException($"'{b.Op}' requires bool operands");
                return r;
            }

            var left = Eval(b.Left, scope, ctx);

            if (b.Op == "in")
            {
                var coll = Eval(b.Right, scope, ctx);
                if (coll.Kind != ValueKind.List) throw new CelException("'in' requires a list on the right");
                foreach (var item in coll.AsList)
                    if (item.Equals(left)) return Value.Bool(true);
                return Value.Bool(false);
            }

            var right = Eval(b.Right, scope, ctx);

            switch (b.Op)
            {
                case "==": return Value.Bool(left.Equals(right));
                case "!=": return Value.Bool(!left.Equals(right));
                case "<": case "<=": case ">": case ">=":
                    return Compare(b.Op, left, right);
                case "+": case "-": case "*": case "/": case "%":
                    return Arith(b.Op, left, right);
                default:
                    throw new CelException($"unknown operator '{b.Op}'");
            }
        }

        private static Value Compare(string op, Value l, Value r)
        {
            int cmp;
            if (l.IsNumber && r.IsNumber) cmp = l.NumericValue.CompareTo(r.NumericValue);
            else if (l.Kind == ValueKind.String && r.Kind == ValueKind.String) cmp = string.CompareOrdinal(l.AsString, r.AsString);
            else throw new CelException($"cannot compare {l.Kind} and {r.Kind} with '{op}'");
            switch (op)
            {
                case "<": return Value.Bool(cmp < 0);
                case "<=": return Value.Bool(cmp <= 0);
                case ">": return Value.Bool(cmp > 0);
                default: return Value.Bool(cmp >= 0);
            }
        }

        private static Value Arith(string op, Value l, Value r)
        {
            if (l.IsNull || r.IsNull)
                throw new CelException("arithmetic on a missing value (null) — guard it with one(rel, fallback)");

            // String concatenation for '+'.
            if (op == "+" && l.Kind == ValueKind.String && r.Kind == ValueKind.String)
                return Value.String(l.AsString + r.AsString);

            if (!l.IsNumber || !r.IsNumber)
                throw new CelException($"'{op}' requires numbers, got {l.Kind} and {r.Kind}");

            // The spec's no-mixing rule: int and double never combine in one operation.
            if (l.Kind != r.Kind)
                throw new CelException("cannot mix integer and double — convert with int(...) or double(...)");

            if (l.Kind == ValueKind.Int)
            {
                long a = l.AsInt, c = r.AsInt;
                switch (op)
                {
                    case "+": return Value.Int(a + c);
                    case "-": return Value.Int(a - c);
                    case "*": return Value.Int(a * c);
                    case "/":
                        if (c == 0) throw new CelException("integer division by zero");
                        return Value.Int(a / c);
                    default:
                        if (c == 0) throw new CelException("integer modulo by zero");
                        return Value.Int(a % c);
                }
            }
            else
            {
                double a = l.AsDouble, c = r.AsDouble;
                switch (op)
                {
                    case "+": return Value.Double(a + c);
                    case "-": return Value.Double(a - c);
                    case "*": return Value.Double(a * c);
                    case "/": return Value.Double(a / c);
                    default: return Value.Double(a % c);
                }
            }
        }

        private static Value EvalMember(CelMember m, Scope scope, ICelContext ctx)
        {
            // session.<stream> -> a list of event records.
            if (m.Target is CelIdent sid && sid.Name == "session" && !scope.TryGet("session", out _))
            {
                var events = ctx.SessionStream(m.Name);
                var records = new List<Value>(events.Count);
                foreach (var ev in events) records.Add(Value.Record(ev.Fields));
                return Value.List(records);
            }

            var target = Eval(m.Target, scope, ctx);
            switch (target.Kind)
            {
                case ValueKind.Entity:
                    return SetToList(ctx.Relation(target, m.Name));
                case ValueKind.Record:
                    return target.AsRecord.TryGetValue(m.Name, out var f) ? f : Value.Null;
                case ValueKind.Null:
                    throw new CelException($"cannot read '.{m.Name}' on null (a missing entity)");
                default:
                    throw new CelException($"cannot read '.{m.Name}' on {target.Kind}");
            }
        }

        private static Value EvalIndex(CelIndex ix, Scope scope, ICelContext ctx)
        {
            var target = Eval(ix.Target, scope, ctx);
            var index = Eval(ix.Index, scope, ctx);
            if (target.Kind != ValueKind.List) throw new CelException("indexing requires a list");
            if (index.Kind != ValueKind.Int) throw new CelException("list index must be an integer");
            var list = target.AsList;
            long i = index.AsInt;
            if (i < 0 || i >= list.Count) throw new CelException("list index out of range");
            return list[(int)i];
        }

        private static Value EvalCall(CelCall c, Scope scope, ICelContext ctx)
        {
            if (c.Target == null) return EvalGlobalCall(c, scope, ctx);

            // Namespaced/method calls whose receiver is a special identifier.
            if (c.Target is CelIdent tid)
            {
                if (tid.Name == "math" && !scope.TryGet("math", out _))
                    return EvalMath(c, scope, ctx);
                if (tid.Name == "now" && !scope.TryGet("now", out _))
                    return EvalNowMethod(c, scope, ctx);
            }

            // Collection macros: receiver.macro(var, body).
            switch (c.Name)
            {
                case "map": case "filter": case "exists": case "all":
                    return EvalMacro(c, scope, ctx);
            }

            throw new CelException($"unknown method '.{c.Name}(...)'");
        }

        private static Value EvalGlobalCall(CelCall c, Scope scope, ICelContext ctx)
        {
            switch (c.Name)
            {
                case "one":
                {
                    var set = AsList(Eval(c.Args[0], scope, ctx), "one");
                    if (set.Count > 0) return set[0];
                    return c.Args.Count > 1 ? Eval(c.Args[1], scope, ctx) : Value.Null;
                }
                case "size":
                {
                    var v = Eval(c.Args[0], scope, ctx);
                    if (v.Kind == ValueKind.List) return Value.Int(v.AsList.Count);
                    if (v.Kind == ValueKind.String) return Value.Int(v.AsString.Length);
                    throw new CelException("size() requires a list or string");
                }
                case "int": return ToInt(Eval(c.Args[0], scope, ctx));
                case "double": return ToDouble(Eval(c.Args[0], scope, ctx));
                case "string": return Value.String(Eval(c.Args[0], scope, ctx).ToString());
                case "bool":
                {
                    var v = Eval(c.Args[0], scope, ctx);
                    if (v.Kind == ValueKind.Bool) return v;
                    if (v.Kind == ValueKind.String)
                    {
                        if (v.AsString == "true") return Value.Bool(true);
                        if (v.AsString == "false") return Value.Bool(false);
                    }
                    throw new CelException("bool() requires a bool or \"true\"/\"false\"");
                }
                case "instances":
                {
                    string type = c.Args[0] is CelIdent id ? id.Name : Eval(c.Args[0], scope, ctx).AsEntity;
                    return Value.List(new List<Value>(ctx.Instances(type)));
                }
                case "sortBy": return EvalSortBy(c, scope, ctx);
                case "rand": return Value.Double(ctx.Rand(Eval(c.Args[0], scope, ctx)));
                default:
                    throw new CelException($"unknown function '{c.Name}(...)'");
            }
        }

        private static Value EvalSortBy(CelCall c, Scope scope, ICelContext ctx)
        {
            var list = AsList(Eval(c.Args[0], scope, ctx), "sortBy");
            var relVal = Eval(c.Args[1], scope, ctx);
            if (relVal.Kind != ValueKind.String) throw new CelException("sortBy's second argument must be a relation name string");
            string rel = relVal.AsString;

            // Key each element by one(element.rel); empty keys as null (sorts first).
            var keyed = new List<(Value key, Value element)>(list.Count);
            foreach (var el in list)
            {
                Value key = Value.Null;
                if (el.Kind == ValueKind.Entity)
                {
                    var set = ctx.Relation(el, rel);
                    key = set.One();
                }
                keyed.Add((key, el));
            }
            keyed.Sort((x, y) => CanonicalComparer.Instance.Compare(x.key, y.key));
            var result = new List<Value>(keyed.Count);
            foreach (var pair in keyed) result.Add(pair.element);
            return Value.List(result);
        }

        private static Value EvalMacro(CelCall c, Scope scope, ICelContext ctx)
        {
            if (c.Args.Count != 2 || !(c.Args[0] is CelIdent varIdent))
                throw new CelException($"{c.Name}() takes a variable and an expression, e.g. list.{c.Name}(x, ...)");

            var source = AsList(Eval(c.Target!, scope, ctx), c.Name);
            string var = varIdent.Name;
            var body = c.Args[1];

            switch (c.Name)
            {
                case "map":
                {
                    var outp = new List<Value>(source.Count);
                    foreach (var item in source)
                        outp.Add(Eval(body, scope.Bind(var, item), ctx));
                    return Value.List(outp);
                }
                case "filter":
                {
                    var outp = new List<Value>();
                    foreach (var item in source)
                        if (AsBool(Eval(body, scope.Bind(var, item), ctx), "filter")) outp.Add(item);
                    return Value.List(outp);
                }
                case "exists":
                {
                    foreach (var item in source)
                        if (AsBool(Eval(body, scope.Bind(var, item), ctx), "exists")) return Value.Bool(true);
                    return Value.Bool(false);
                }
                default: // all
                {
                    foreach (var item in source)
                        if (!AsBool(Eval(body, scope.Bind(var, item), ctx), "all")) return Value.Bool(false);
                    return Value.Bool(true);
                }
            }
        }

        private static Value EvalMath(CelCall c, Scope scope, ICelContext ctx)
        {
            Value A() => Eval(c.Args[0], scope, ctx);
            Value B() => Eval(c.Args[1], scope, ctx);
            double D(Value v) => v.IsNumber ? v.NumericValue : throw new CelException($"math.{c.Name} requires numbers");

            switch (c.Name)
            {
                case "greatest": { var a = A(); var b = B(); return CanonicalComparer.Instance.Compare(a, b) >= 0 ? a : b; }
                case "least": { var a = A(); var b = B(); return CanonicalComparer.Instance.Compare(a, b) <= 0 ? a : b; }
                case "abs": { var a = A(); return a.Kind == ValueKind.Int ? Value.Int(Math.Abs(a.AsInt)) : Value.Double(Math.Abs(D(a))); }
                case "sign": { var a = A(); return Value.Int(Math.Sign(D(a))); }
                case "floor": return Value.Double(Math.Floor(D(A())));
                case "ceil": return Value.Double(Math.Ceiling(D(A())));
                case "round": return Value.Double(Math.Round(D(A()), MidpointRounding.AwayFromZero));
                case "trunc": return Value.Double(Math.Truncate(D(A())));
                case "sqrt": return Value.Double(Math.Sqrt(D(A())));
                default:
                    throw new CelException($"unknown math helper 'math.{c.Name}'");
            }
        }

        private static Value EvalNowMethod(CelCall c, Scope scope, ICelContext ctx)
        {
            // Date components, read in UTC (the spec's default).
            var t = ctx.Now.ToUniversalTime();
            switch (c.Name)
            {
                case "getFullYear": return Value.Int(t.Year);
                case "getMonth": return Value.Int(t.Month - 1); // JS-style: 0-based
                case "getDate": return Value.Int(t.Day);
                case "getHours": return Value.Int(t.Hour);
                case "getMinutes": return Value.Int(t.Minute);
                case "getSeconds": return Value.Int(t.Second);
                case "getDayOfWeek": return Value.Int((int)t.DayOfWeek);
                default:
                    throw new CelException($"unknown now method 'now.{c.Name}'");
            }
        }

        // ---- helpers ----

        private static Value SetToList(ValueSet set)
        {
            var items = new List<Value>(set.Count);
            foreach (var v in set) items.Add(v);
            return Value.List(items);
        }

        private static IReadOnlyList<Value> AsList(Value v, string who)
        {
            if (v.Kind == ValueKind.List) return v.AsList;
            if (v.IsNull) return Array.Empty<Value>();
            // A scalar acts as a one-element collection for convenience.
            return new[] { v };
        }

        private static bool AsBool(Value v, string who)
        {
            if (v.Kind == ValueKind.Bool) return v.AsBool;
            throw new CelException($"{who}() predicate must be a bool");
        }

        private static Value ToInt(Value v)
        {
            switch (v.Kind)
            {
                case ValueKind.Int: return v;
                case ValueKind.Double: return Value.Int((long)Math.Truncate(v.AsDouble));
                case ValueKind.String:
                    if (long.TryParse(v.AsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                        return Value.Int(i);
                    throw new CelException($"int(\"{v.AsString}\") is not an integer");
                default: throw new CelException($"int() cannot convert {v.Kind}");
            }
        }

        private static Value ToDouble(Value v)
        {
            switch (v.Kind)
            {
                case ValueKind.Double: return v;
                case ValueKind.Int: return Value.Double(v.AsInt);
                case ValueKind.String:
                    if (double.TryParse(v.AsString, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        return Value.Double(d);
                    throw new CelException($"double(\"{v.AsString}\") is not a number");
                default: throw new CelException($"double() cannot convert {v.Kind}");
            }
        }
    }
}

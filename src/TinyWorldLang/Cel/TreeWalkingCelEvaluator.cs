using System;
using System.Collections.Generic;
using System.Globalization;
using TinyWorldLang.Eval;
using TinyWorldLang.Values;

namespace TinyWorldLang.Cel
{
    /// <summary>
    /// The default CEL backend: a pure tree-walking interpreter with no runtime codegen,
    /// safe under IL2CPP/AOT and on consoles/WebGL.
    /// </summary>
    /// <remarks>
    /// Covers the subset the spec documents: <c>map/filter/exists/all/reduce</c>, <c>in</c>,
    /// <c>?:</c>, <c>has(...)</c>, <c>cel.bind(...)</c>, member/index access, the
    /// <c>math.*</c> helpers, <c>one/sortBy/instances/rand/size/sum/min/max/argmin/
    /// argmax/int/double/string/bool</c>, and <c>now</c> date methods.
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
            if (ctx.TryGetLocal(name, out var local)) return local;
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
            if (l.IsNumber && r.IsNumber) cmp = Value.CompareNumeric(l, r);
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
                if (tid.Name == "cel" && !scope.TryGet("cel", out _))
                    return EvalCelMacro(c, scope, ctx);
            }

            // Collection macros: receiver.macro(var, body).
            switch (c.Name)
            {
                case "map": case "filter": case "exists": case "all":
                    return EvalMacro(c, scope, ctx);
                case "reduce":
                    return EvalReduce(c, scope, ctx);
            }

            // A parameter rule call on an entity receiver: receiver.rel(a, b).
            // Method calls dispatch on entity receivers only.
            var target = Eval(c.Target!, scope, ctx);
            if (target.Kind == ValueKind.Entity)
            {
                var args = EvalArgs(c.Args, scope, ctx);
                return SetToList(ctx.CallRule(target, c.Name, args));
            }

            throw new CelException($"unknown method '.{c.Name}(...)' on {target.Kind}");
        }

        private static Value EvalGlobalCall(CelCall c, Scope scope, ICelContext ctx)
        {
            switch (c.Name)
            {
                case "one":
                {
                    RequireArgCount(c, 1, 2);
                    var set = AsList(Eval(c.Args[0], scope, ctx), "one");
                    if (set.Count > 0) return set[0];
                    return c.Args.Count > 1 ? Eval(c.Args[1], scope, ctx) : Value.Null;
                }
                case "size":
                {
                    RequireArgCount(c, 1);
                    var v = Eval(c.Args[0], scope, ctx);
                    if (v.Kind == ValueKind.List) return Value.Int(v.AsList.Count);
                    if (v.Kind == ValueKind.String) return Value.Int(v.AsString.Length);
                    throw new CelException("size() requires a list or string");
                }
                case "int": RequireArgCount(c, 1); return ToInt(Eval(c.Args[0], scope, ctx));
                case "double": RequireArgCount(c, 1); return ToDouble(Eval(c.Args[0], scope, ctx));
                case "string": RequireArgCount(c, 1); return Value.String(Eval(c.Args[0], scope, ctx).ToString());
                case "bool":
                {
                    RequireArgCount(c, 1);
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
                    RequireArgCount(c, 1);
                    string type = c.Args[0] is CelIdent id ? id.Name : Eval(c.Args[0], scope, ctx).AsEntity;
                    return Value.List(new List<Value>(ctx.Instances(type)));
                }
                case "sortBy": RequireArgCount(c, 2); return EvalSortBy(c, scope, ctx);
                case "rand": RequireArgCount(c, 1); return Value.Double(ctx.Rand(Eval(c.Args[0], scope, ctx)));
                case "has":
                {
                    // CEL's presence test: true when the argument resolves to something
                    // present. An absent relation is the empty set -> false; a member
                    // access that fails to resolve (e.g. navigating through a null) is
                    // also false rather than an error, giving safe-navigation ergonomics.
                    RequireArgCount(c, 1);
                    try
                    {
                        var v = Eval(c.Args[0], scope, ctx);
                        if (v.IsNull) return Value.Bool(false);
                        if (v.Kind == ValueKind.List) return Value.Bool(v.AsList.Count > 0);
                        return Value.Bool(true);
                    }
                    catch (CelException) { return Value.Bool(false); }
                }
                case "sum": RequireArgCount(c, 1); return Sum(AsList(Eval(c.Args[0], scope, ctx), "sum"));
                case "max": RequireArgCount(c, 1); return Extreme(AsList(Eval(c.Args[0], scope, ctx), "max"), wantMax: true, "max");
                case "min": RequireArgCount(c, 1); return Extreme(AsList(Eval(c.Args[0], scope, ctx), "min"), wantMax: false, "min");
                case "argmax": return EvalArgExtreme(c, scope, ctx, wantMax: true);
                case "argmin": return EvalArgExtreme(c, scope, ctx, wantMax: false);
                default:
                {
                    // Not a built-in: a module-level function call, name(a, b).
                    var args = EvalArgs(c.Args, scope, ctx);
                    return ctx.CallFunction(c.Name, args);
                }
            }
        }

        private static IReadOnlyList<Value> EvalArgs(IReadOnlyList<CelExpr> exprs, Scope scope, ICelContext ctx)
        {
            var args = new List<Value>(exprs.Count);
            foreach (var a in exprs) args.Add(Eval(a, scope, ctx));
            return args;
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

        // list.reduce(acc, x, initial, body) — a left fold. `acc` is the accumulator,
        // `x` the current element; the body computes the next accumulator. An empty
        // list returns the initial value unchanged. The accumulator may be a list, so
        // a fold can carry several running values at once (e.g. a count plus a total).
        private static Value EvalReduce(CelCall c, Scope scope, ICelContext ctx)
        {
            if (c.Args.Count != 4 || !(c.Args[0] is CelIdent accIdent) || !(c.Args[1] is CelIdent elemIdent))
                throw new CelException("reduce() takes (accumulator, element, initial, body), e.g. list.reduce(acc, x, 0, acc + x)");

            var source = AsList(Eval(c.Target!, scope, ctx), "reduce");
            string accName = accIdent.Name, elemName = elemIdent.Name;
            var body = c.Args[3];

            var acc = Eval(c.Args[2], scope, ctx); // initial
            foreach (var item in source)
                acc = Eval(body, scope.Bind(accName, acc).Bind(elemName, item), ctx);
            return acc;
        }

        // argmax/argmin(list, x, key) — the element of `list` whose `key` is greatest
        // (or least). Binds `x` to each element to evaluate the key. Empty list -> null;
        // ties keep the first element in list order. Keys are ranked in canonical order,
        // so this generalizes beyond numbers (e.g. "most recent event" by an integer seq).
        private static Value EvalArgExtreme(CelCall c, Scope scope, ICelContext ctx, bool wantMax)
        {
            string name = wantMax ? "argmax" : "argmin";
            if (c.Args.Count != 3 || !(c.Args[1] is CelIdent varIdent))
                throw new CelException($"{name}(list, x, key) takes a list, a variable, and a key expression");

            var list = AsList(Eval(c.Args[0], scope, ctx), name);
            if (list.Count == 0) return Value.Null;

            string var = varIdent.Name;
            var keyExpr = c.Args[2];
            Value best = list[0];
            Value bestKey = Eval(keyExpr, scope.Bind(var, best), ctx);
            for (int i = 1; i < list.Count; i++)
            {
                var key = Eval(keyExpr, scope.Bind(var, list[i]), ctx);
                int cmp = CanonicalComparer.Instance.Compare(key, bestKey);
                if (wantMax ? cmp > 0 : cmp < 0) { best = list[i]; bestKey = key; }
            }
            return best;
        }

        // sum(list) — numeric total. Empty -> int 0. Honors the no-mixing rule: a list
        // mixing integers and doubles is an error (convert with int(...)/double(...)).
        private static Value Sum(IReadOnlyList<Value> list)
        {
            if (list.Count == 0) return Value.Int(0);
            foreach (var v in list)
                if (!v.IsNumber) throw new CelException("sum() requires a list of numbers");

            bool isDouble = list[0].Kind == ValueKind.Double;
            foreach (var v in list)
                if ((v.Kind == ValueKind.Double) != isDouble)
                    throw new CelException("sum() cannot mix integer and double — convert with int(...) or double(...)");

            if (isDouble)
            {
                double total = 0;
                foreach (var v in list) total += v.AsDouble;
                return Value.Double(total);
            }
            long acc = 0;
            foreach (var v in list) acc += v.AsInt;
            return Value.Int(acc);
        }

        // max(list)/min(list) — numeric extreme, preserving the winner's kind. Integers
        // and doubles compare by mathematical value (the spec's comparison rule), so a
        // mixed list is allowed here. Empty -> null (like one() on an empty set).
        private static Value Extreme(IReadOnlyList<Value> list, bool wantMax, string who)
        {
            if (list.Count == 0) return Value.Null;
            foreach (var v in list)
                if (!v.IsNumber) throw new CelException($"{who}() requires a list of numbers");

            Value best = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                int cmp = Value.CompareNumeric(list[i], best);
                if (wantMax ? cmp > 0 : cmp < 0) best = list[i];
            }
            return best;
        }

        // The CEL "bindings" extension: cel.bind(var, init, expr) evaluates `init`,
        // binds it to `var`, and returns `expr` evaluated with that binding in scope.
        // A local alias for a subexpression — names it once instead of repeating it.
        private static Value EvalCelMacro(CelCall c, Scope scope, ICelContext ctx)
        {
            if (c.Name != "bind")
                throw new CelException($"unknown cel.{c.Name}(...); only cel.bind(var, init, expr) is supported");
            if (c.Args.Count != 3 || !(c.Args[0] is CelIdent varIdent))
                throw new CelException("cel.bind(var, init, expr) takes a variable, an initial value, and an expression");

            var init = Eval(c.Args[1], scope, ctx);
            return Eval(c.Args[2], scope.Bind(varIdent.Name, init), ctx);
        }

        private static Value EvalMath(CelCall c, Scope scope, ICelContext ctx)
        {
            Value A() => Eval(c.Args[0], scope, ctx);
            Value B() => Eval(c.Args[1], scope, ctx);
            double D(Value v) => v.IsNumber ? v.NumericValue : throw new CelException($"math.{c.Name} requires numbers");

            switch (c.Name)
            {
                case "greatest": { RequireArgCount(c, 2); var a = A(); var b = B(); return CanonicalComparer.Instance.Compare(a, b) >= 0 ? a : b; }
                case "least": { RequireArgCount(c, 2); var a = A(); var b = B(); return CanonicalComparer.Instance.Compare(a, b) <= 0 ? a : b; }
                case "abs": { RequireArgCount(c, 1); var a = A(); return a.Kind == ValueKind.Int ? Value.Int(Math.Abs(a.AsInt)) : Value.Double(Math.Abs(D(a))); }
                case "sign": { RequireArgCount(c, 1); var a = A(); return Value.Int(Math.Sign(D(a))); }
                case "floor": RequireArgCount(c, 1); return Value.Double(Math.Floor(D(A())));
                case "ceil": RequireArgCount(c, 1); return Value.Double(Math.Ceiling(D(A())));
                case "round": RequireArgCount(c, 1); return Value.Double(Math.Round(D(A()), MidpointRounding.AwayFromZero));
                case "trunc": RequireArgCount(c, 1); return Value.Double(Math.Truncate(D(A())));
                case "sqrt": RequireArgCount(c, 1); return Value.Double(Math.Sqrt(D(A())));
                default:
                    throw new CelException($"unknown math helper 'math.{c.Name}'");
            }
        }

        private static Value EvalNowMethod(CelCall c, Scope scope, ICelContext ctx)
        {
            RequireArgCount(c, 0);
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

        private static void RequireArgCount(CelCall c, int count)
        {
            if (c.Args.Count != count)
                throw new CelException($"{CallName(c)} takes {count} argument{(count == 1 ? "" : "s")}");
        }

        private static void RequireArgCount(CelCall c, int min, int max)
        {
            if (c.Args.Count < min || c.Args.Count > max)
                throw new CelException($"{CallName(c)} takes {min} to {max} arguments");
        }

        private static string CallName(CelCall c) =>
            c.Target == null
                ? $"{c.Name}(...)"
                : $".{c.Name}(...)";

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

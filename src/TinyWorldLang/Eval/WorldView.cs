using System;
using System.Collections.Generic;
using TinyWorldLang.Cel;
using TinyWorldLang.Model;
using TinyWorldLang.Parsing;
using TinyWorldLang.Rand;
using TinyWorldLang.Templates;
using TinyWorldLang.Values;

namespace TinyWorldLang.Eval
{
    /// <summary>
    /// The query surface over one evaluation: <c>(world, session, env)</c>. Read a
    /// relation's current state by entity name or across a type, or render a string
    /// value's template. Computed values are memoized for the lifetime of the view.
    /// </summary>
    /// <remarks>
    /// A <see cref="WorldView"/> is the materialization of the spec's pure function
    /// <c>eval(world, session, env)</c>. It holds no long-lived state beyond the
    /// per-view memo; create a fresh one per query batch.
    /// </remarks>
    public sealed class WorldView
    {
        private readonly World _world;
        private readonly Session _session;
        private readonly Env _env;
        private readonly ICelEvaluator _cel;
        private readonly Func<string, string>? _escaper;

        // Per-relation compiled programs and per-(entity,relation) memoized results.
        private readonly Dictionary<ComputedFact, ICelProgram> _compiled = new Dictionary<ComputedFact, ICelProgram>();
        private readonly Dictionary<FunctionDecl, ICelProgram> _compiledFns = new Dictionary<FunctionDecl, ICelProgram>();
        private readonly Dictionary<(string, string), ValueSet> _memo = new Dictionary<(string, string), ValueSet>();
        private readonly HashSet<(string, string)> _inProgress = new HashSet<(string, string)>();

        // Guards parameter-rule / function recursion: a countdown call cycles through
        // changing arguments, so an (entity, name, args) key cannot catch it — bound
        // the descent instead and fail with a clear error past the limit.
        private const int MaxCallDepth = 256;
        private int _callDepth;

        internal WorldView(World world, Session session, Env env, ICelEvaluator cel, Func<string, string>? escaper)
        {
            _world = world;
            _session = session;
            _env = env;
            _cel = cel;
            _escaper = escaper;
        }

        // -------- Public query API --------
        //
        // One general way to query: get entity handles, then filter/project with
        // standard LINQ-to-objects (no IQueryable / expression trees, so it stays
        // AOT-safe). A relation is read as a Field; string values present rendered.
        //
        //   view.Entities("Person")
        //       .Where(p => p["age"].AsInt > 40)
        //       .Select(p => new { p.Name, Bio = p["persona"].Text });

        /// <summary>A handle to the entity named <paramref name="name"/> (resolved to canonical).</summary>
        public Entity Entity(string name) => new Entity(this, _world.Canonical(name));

        /// <summary>Every entity in the world (mentioned as a subject or an <c>instanceof</c> subject).</summary>
        public IEnumerable<Entity> Entities()
        {
            foreach (var e in _world.AllEntities()) yield return new Entity(this, e);
        }

        /// <summary>Every entity that is an instance of <paramref name="type"/>, directly or via a subtype.</summary>
        public IEnumerable<Entity> Entities(string type)
        {
            foreach (var e in _world.Instances(_world.Canonical(type))) yield return new Entity(this, e);
        }

        public bool IsInstanceOf(string entity, string type) =>
            _world.IsInstanceOf(_world.Canonical(entity), _world.Canonical(type));

        // -------- Internal surface used by Entity / Field --------

        internal Entity EntityHandle(string canonicalName) => new Entity(this, canonicalName);

        internal ValueSet ResolveSet(string canonicalEntity, string relation) => Resolve(canonicalEntity, relation);

        /// <summary>Render a single value to presentation text; a string value is rendered as a template.</summary>
        internal string RenderValueText(Value v, string owner)
        {
            if (v.IsNull) return "";
            if (v.Kind == ValueKind.String) return Renderer().Render(v.AsString, owner);
            return v.ToString();
        }

        /// <summary>Declared relation names for an entity (stored + applicable computed), canonical order. No evaluation.</summary>
        internal IReadOnlyList<string> RelationNamesFor(string canonicalEntity)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in _world.StoredRelationNames(canonicalEntity)) names.Add(r);
            foreach (var r in _world.ComputedRelationNames())
                if (HasApplicableRule(canonicalEntity, r)) names.Add(r);
            return new List<string>(names);
        }

        private bool HasApplicableRule(string entity, string relation)
        {
            foreach (var rule in _world.RulesFor(relation))
                if (rule.Parameters.Count == 0 && _world.Specificity(entity, rule.Type) >= 0) return true;
            return false;
        }

        // -------- Relation resolution + tiebreak --------

        private ValueSet Resolve(string entity, string relation)
        {
            var key = (entity, relation);
            if (_memo.TryGetValue(key, out var cached)) return cached;

            // Tiebreak rule 1: any stored fact wins; computed rules are suppressed.
            if (_world.HasStoredFacts(entity, relation))
            {
                var stored = _world.StoredFacts(entity, relation);
                _memo[key] = stored;
                return stored;
            }

            var rule = SelectRule(entity, relation, 0);
            if (rule == null)
            {
                _memo[key] = ValueSet.Empty;
                return ValueSet.Empty;
            }

            if (!_inProgress.Add(key))
                throw new TwlEvalException($"cyclic computed relation '{relation}' on '{entity}'");
            try
            {
                var program = Compile(rule);
                var ctx = new Context(this, Value.Entity(entity));
                Value result;
                try
                {
                    result = program.Evaluate(ctx);
                }
                catch (CelException ex)
                {
                    throw new TwlEvalException(
                        $"in rule '{rule.Type} {rule.Relation}' on '{entity}': {ex.Message}",
                        ex);
                }
                var set = Coerce(result);
                _memo[key] = set;
                return set;
            }
            finally
            {
                _inProgress.Remove(key);
            }
        }

        /// <summary>
        /// Tiebreak rules 2 &amp; 3: most specific type wins; equal specificity is an
        /// error. Only rules of the requested <paramref name="arity"/> are considered,
        /// so <c>legal(x)</c> and <c>legal(x, y)</c> are independent (overload by arity).
        /// </summary>
        private ComputedFact? SelectRule(string entity, string relation, int arity)
        {
            ComputedFact? best = null;
            int bestDist = int.MaxValue;
            bool ambiguous = false;
            string? bestType = null;

            foreach (var rule in _world.RulesFor(relation))
            {
                if (rule.Parameters.Count != arity) continue; // arity is part of selection
                int dist = _world.Specificity(entity, rule.Type);
                if (dist < 0) continue; // entity is not an instance of this rule's type
                if (dist < bestDist)
                {
                    bestDist = dist; best = rule; bestType = rule.Type; ambiguous = false;
                }
                else if (dist == bestDist && rule.Type != bestType)
                {
                    ambiguous = true;
                }
            }

            if (ambiguous)
                throw new TwlLoadException(
                    $"ambiguous rule for '{relation}' on '{entity}': two equally specific types apply — add a rule on the more specific type");
            return best;
        }

        /// <summary>Coerce a rule's result into a relation set (flatten one level; drop nulls).</summary>
        private static ValueSet Coerce(Value result)
        {
            if (result.IsNull) return ValueSet.Empty;
            if (result.Kind == ValueKind.List)
            {
                var items = new List<Value>();
                foreach (var v in result.AsList)
                {
                    if (v.IsNull) continue;
                    if (v.Kind == ValueKind.List) throw new TwlEvalException("nested lists are not allowed in a relation result");
                    if (v.Kind == ValueKind.Record || v.Kind == ValueKind.Timestamp)
                        throw new TwlEvalException($"a {v.Kind} cannot be stored in a relation");
                    items.Add(v);
                }
                return ValueSet.From(items);
            }
            if (result.Kind == ValueKind.Record || result.Kind == ValueKind.Timestamp)
                throw new TwlEvalException($"a {result.Kind} cannot be stored in a relation");
            return ValueSet.From(new[] { result });
        }

        private ICelProgram Compile(ComputedFact rule)
        {
            if (!_compiled.TryGetValue(rule, out var p))
            {
                try { p = _cel.Compile(rule.Expression); }
                catch (CelException ex) { throw new TwlLoadException($"in rule '{rule.Type} {rule.Relation}': {ex.Message}", rule.Line); }
                _compiled[rule] = p;
            }
            return p;
        }

        private ICelProgram CompileFunction(FunctionDecl fn)
        {
            if (!_compiledFns.TryGetValue(fn, out var p))
            {
                try { p = _cel.Compile(fn.Expression); }
                catch (CelException ex) { throw new TwlLoadException($"in function '{fn.Name}': {ex.Message}", fn.Line); }
                _compiledFns[fn] = p;
            }
            return p;
        }

        // -------- Parameter rule + function calls --------

        /// <summary>
        /// A parameter rule call (<c>e.rel(a, b)</c>): dispatch on the receiver's type,
        /// bind the arguments to the rule's parameters, then Coerce the result into a
        /// relation set like any rule.
        /// </summary>
        internal ValueSet ResolveCall(string entity, string relation, IReadOnlyList<Value> args)
        {
            var rule = SelectRule(entity, relation, args.Count);
            if (rule == null)
                throw new TwlEvalException(
                    $"no rule '{relation}' taking {args.Count} argument(s) applies to '{entity}'");

            EnterCall();
            try
            {
                var program = Compile(rule);
                var ctx = new Context(this, Value.Entity(entity), BuildLocals(rule.Parameters, args));
                Value result;
                try { result = program.Evaluate(ctx); }
                catch (CelException ex)
                {
                    throw new TwlEvalException(
                        $"in rule '{rule.Type} {rule.Relation}(...)' on '{entity}': {ex.Message}", ex);
                }
                return Coerce(result);
            }
            finally { ExitCall(); }
        }

        /// <summary>
        /// A module function call (<c>name(a, b)</c>): no receiver, no dispatch; the
        /// body's value is returned unchanged (no set coercion).
        /// </summary>
        internal Value ResolveFunction(string name, IReadOnlyList<Value> args)
        {
            var fn = _world.LookupFunction(name, args.Count);
            if (fn == null)
                throw new TwlEvalException($"unknown function '{name}' taking {args.Count} argument(s)");

            EnterCall();
            try
            {
                var program = CompileFunction(fn);
                var ctx = new Context(this, Value.Null, BuildLocals(fn.Parameters, args));
                try { return program.Evaluate(ctx); }
                catch (CelException ex)
                {
                    throw new TwlEvalException($"in function '{name}': {ex.Message}", ex);
                }
            }
            finally { ExitCall(); }
        }

        private static IReadOnlyDictionary<string, Value> BuildLocals(IReadOnlyList<string> names, IReadOnlyList<Value> args)
        {
            var locals = new Dictionary<string, Value>(names.Count, StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++) locals[names[i]] = args[i];
            return locals;
        }

        private void EnterCall()
        {
            if (++_callDepth > MaxCallDepth)
            {
                _callDepth--;
                throw new TwlEvalException(
                    $"call depth exceeded {MaxCallDepth} — a function or parameter rule is recursing without bound");
            }
        }

        private void ExitCall() => _callDepth--;

        private TemplateRenderer Renderer() => new TemplateRenderer(Resolve, _escaper);

        // -------- CEL context --------

        private sealed class Context : ICelContext
        {
            private readonly WorldView _view;
            private readonly IReadOnlyDictionary<string, Value>? _locals;

            public Context(WorldView view, Value self, IReadOnlyDictionary<string, Value>? locals = null)
            {
                _view = view;
                Self = self;
                _locals = locals;
            }

            public Value Self { get; }
            public DateTimeOffset Now => _view._env.Now;

            public bool TryGetLocal(string name, out Value value)
            {
                if (_locals != null && _locals.TryGetValue(name, out value)) return true;
                value = Value.Null;
                return false;
            }

            public ValueSet CallRule(Value receiver, string name, IReadOnlyList<Value> args)
            {
                if (receiver.Kind != ValueKind.Entity)
                    throw new CelException($"cannot call '.{name}(...)' on {receiver.Kind}");
                return _view.ResolveCall(receiver.AsEntity, name, args);
            }

            public Value CallFunction(string name, IReadOnlyList<Value> args) =>
                _view.ResolveFunction(name, args);

            public ValueSet Relation(Value entity, string relation)
            {
                if (entity.Kind != ValueKind.Entity)
                    throw new CelException($"cannot read relation '{relation}' on {entity.Kind}");
                return _view.Resolve(entity.AsEntity, relation);
            }

            public IReadOnlyList<Value> Instances(string type)
            {
                string canon = _view._world.Canonical(type);
                var list = new List<Value>();
                foreach (var e in _view._world.Instances(canon)) list.Add(Value.Entity(e));
                return list;
            }

            public double Rand(Value key) => StableHash.ToUnitInterval(StableHash.Hash(_view._env.Seed, key));

            public IReadOnlyList<Event> SessionStream(string name) => _view._session.Stream(name);

            public Value CanonicalEntity(string name) => Value.Entity(_view._world.Canonical(name));
        }
    }
}

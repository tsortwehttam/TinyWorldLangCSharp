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
        private readonly Dictionary<(string, string), ValueSet> _memo = new Dictionary<(string, string), ValueSet>();
        private readonly HashSet<(string, string)> _inProgress = new HashSet<(string, string)>();

        internal WorldView(World world, Session session, Env env, ICelEvaluator cel, Func<string, string>? escaper)
        {
            _world = world;
            _session = session;
            _env = env;
            _cel = cel;
            _escaper = escaper;
        }

        // -------- Public query API --------

        /// <summary>Current value set of <paramref name="relation"/> on the entity named <paramref name="entity"/>.</summary>
        public ValueSet Get(string entity, string relation) =>
            Resolve(_world.Canonical(entity), relation);

        /// <summary>Convenience: the single value (canonical-first) of a relation, or <c>null</c> when empty.</summary>
        public Value GetOne(string entity, string relation) => Get(entity, relation).One();

        /// <summary>
        /// The relation evaluated for every instance of <paramref name="type"/>,
        /// keyed by canonical entity name.
        /// </summary>
        public IReadOnlyDictionary<string, ValueSet> Query(string type, string relation)
        {
            var result = new Dictionary<string, ValueSet>(StringComparer.Ordinal);
            foreach (var e in _world.Instances(_world.Canonical(type)))
                result[e] = Resolve(e, relation);
            return result;
        }

        /// <summary>Render the canonical-first value of a relation as a template against its owner.</summary>
        public string Render(string entity, string relation)
        {
            string owner = _world.Canonical(entity);
            var set = Resolve(owner, relation);
            if (set.IsEmpty) return "";
            var v = set.One();
            if (v.Kind != ValueKind.String) return v.ToString();
            return Renderer().Render(v.AsString, owner);
        }

        public bool IsInstanceOf(string entity, string type) =>
            _world.IsInstanceOf(_world.Canonical(entity), _world.Canonical(type));

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

            var rule = SelectRule(entity, relation);
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
                Value result = program.Evaluate(ctx);
                var set = Coerce(result);
                _memo[key] = set;
                return set;
            }
            finally
            {
                _inProgress.Remove(key);
            }
        }

        /// <summary>Tiebreak rules 2 &amp; 3: most specific type wins; equal specificity is an error.</summary>
        private ComputedFact? SelectRule(string entity, string relation)
        {
            ComputedFact? best = null;
            int bestDist = int.MaxValue;
            bool ambiguous = false;
            string? bestType = null;

            foreach (var rule in _world.RulesFor(relation))
            {
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

        private TemplateRenderer Renderer() => new TemplateRenderer(Resolve, _escaper);

        // -------- CEL context --------

        private sealed class Context : ICelContext
        {
            private readonly WorldView _view;
            public Context(WorldView view, Value self) { _view = view; Self = self; }

            public Value Self { get; }
            public DateTimeOffset Now => _view._env.Now;

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

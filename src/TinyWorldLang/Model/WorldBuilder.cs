using System;
using System.Collections.Generic;
using TinyWorldLang.Parsing;
using TinyWorldLang.Values;

namespace TinyWorldLang.Model
{
    /// <summary>Turns a <see cref="ParsedWorld"/> into a loaded, validated <see cref="World"/>.</summary>
    internal static class WorldBuilder
    {
        public static World Build(ParsedWorld parsed)
        {
            // 1. Resolve identity (sameas) with union-find, then canonicalize every name.
            var uf = new UnionFind();
            foreach (var s in parsed.Statements)
            {
                if (s is StructuralFact sf && sf.Relation == BuiltinRelation.SameAs)
                    uf.Union(sf.Left, sf.Right);
            }
            string Canon(string n) => uf.Canonical(n);
            var canonical = uf.CanonicalMap();

            // 2. Collect structural edges + stored facts + rules, all canonicalized.
            var directTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var supertypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var grouped = new Dictionary<string, Dictionary<string, List<Value>>>(StringComparer.Ordinal);
            var rules = new Dictionary<string, List<ComputedFact>>(StringComparer.Ordinal);
            var functions = new Dictionary<(string, int), FunctionDecl>();
            var entities = new HashSet<string>(StringComparer.Ordinal);

            foreach (var stmt in parsed.Statements)
            {
                switch (stmt)
                {
                    case StructuralFact st:
                        ApplyStructural(st, Canon, directTypes, supertypes);
                        entities.Add(Canon(st.Left));
                        entities.Add(Canon(st.Right));
                        break;

                    case StoredFact fact:
                        string subject = Canon(fact.Subject);
                        Value value = Canonicalize(fact.Value, Canon);
                        AddStored(grouped, subject, fact.Relation, value);
                        entities.Add(subject);
                        CollectEntities(value, entities);
                        break;

                    case ComputedFact rule:
                        string type = Canon(rule.Type);
                        var canonRule = new ComputedFact(type, rule.Relation, rule.Expression, rule.Line, rule.Parameters);
                        if (!rules.TryGetValue(rule.Relation, out var list))
                            rules[rule.Relation] = list = new List<ComputedFact>();
                        list.Add(canonRule);
                        entities.Add(type);
                        break;

                    case FunctionDecl fn:
                        var key = (fn.Name, fn.Parameters.Count);
                        if (functions.ContainsKey(key))
                            throw new TwlLoadException(
                                $"function '{fn.Name}' is declared twice with {fn.Parameters.Count} parameter(s)", fn.Line);
                        functions[key] = fn;
                        break;
                }
            }

            // 3. extends must form no cycles.
            DetectExtendsCycles(supertypes);

            // 3b. A function name must not collide with a relation read without a call
            //     (a stored relation or a 0-arity computed rule), to avoid confusion
            //     between `e.f` (a relation read) and `f(...)` (a function call).
            ValidateFunctionNames(functions, grouped, rules);

            // 4. Materialize stored value sets.
            var stored = new Dictionary<string, Dictionary<string, ValueSet>>(StringComparer.Ordinal);
            foreach (var entityKv in grouped)
            {
                var rels = new Dictionary<string, ValueSet>(StringComparer.Ordinal);
                foreach (var relKv in entityKv.Value)
                    rels[relKv.Key] = ValueSet.From(relKv.Value);
                stored[entityKv.Key] = rels;
            }

            return new World(stored, rules, functions, new TypeGraph(directTypes, supertypes), canonical, entities);
        }

        private static void ValidateFunctionNames(
            Dictionary<(string, int), FunctionDecl> functions,
            Dictionary<string, Dictionary<string, List<Value>>> grouped,
            Dictionary<string, List<ComputedFact>> rules)
        {
            // Relation names read without a call: stored relations + 0-arity rules.
            var plainRelations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rels in grouped.Values)
                foreach (var name in rels.Keys)
                    plainRelations.Add(name);
            foreach (var kv in rules)
                foreach (var rule in kv.Value)
                    if (rule.Parameters.Count == 0)
                        plainRelations.Add(rule.Relation);

            foreach (var fn in functions.Values)
                if (plainRelations.Contains(fn.Name))
                    throw new TwlLoadException(
                        $"function '{fn.Name}' clashes with a relation of the same name — " +
                        "a name cannot be both a function and a stored/0-arity relation", fn.Line);
        }

        private static void ApplyStructural(
            StructuralFact st, Func<string, string> canon,
            Dictionary<string, HashSet<string>> directTypes,
            Dictionary<string, HashSet<string>> supertypes)
        {
            string left = canon(st.Left), right = canon(st.Right);
            switch (st.Relation)
            {
                case BuiltinRelation.InstanceOf:
                    Add(directTypes, left, right);
                    break;
                case BuiltinRelation.Extends:
                    if (left != right) // a self-edge after a sameas merge is degenerate, not a cycle
                        Add(supertypes, left, right);
                    break;
                case BuiltinRelation.SameAs:
                    break; // already handled by union-find
            }
        }

        private static Value Canonicalize(Value v, Func<string, string> canon)
        {
            if (v.Kind == ValueKind.Entity) return Value.Entity(canon(v.AsEntity));
            if (v.Kind == ValueKind.Record)
            {
                var fields = new Dictionary<string, Value>(StringComparer.Ordinal);
                foreach (var kv in v.AsRecord) fields[kv.Key] = Canonicalize(kv.Value, canon);
                return Value.Record(fields);
            }
            return v;
        }

        /// <summary>Register every entity name reachable from a stored value (including record fields).</summary>
        private static void CollectEntities(Value v, HashSet<string> entities)
        {
            if (v.Kind == ValueKind.Entity) entities.Add(v.AsEntity);
            else if (v.Kind == ValueKind.Record)
                foreach (var kv in v.AsRecord) CollectEntities(kv.Value, entities);
        }

        private static void AddStored(
            Dictionary<string, Dictionary<string, List<Value>>> grouped,
            string entity, string relation, Value value)
        {
            if (!grouped.TryGetValue(entity, out var rels))
                grouped[entity] = rels = new Dictionary<string, List<Value>>(StringComparer.Ordinal);
            if (!rels.TryGetValue(relation, out var vals))
                rels[relation] = vals = new List<Value>();
            vals.Add(value);
        }

        private static void Add(Dictionary<string, HashSet<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var set))
                map[key] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(value);
        }

        private static void DetectExtendsCycles(Dictionary<string, HashSet<string>> supertypes)
        {
            var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=unseen,1=on-stack,2=done
            void Visit(string node)
            {
                state[node] = 1;
                if (supertypes.TryGetValue(node, out var supers))
                {
                    foreach (var s in supers)
                    {
                        state.TryGetValue(s, out var st);
                        if (st == 1)
                            throw new TwlLoadException($"'extends' forms a cycle through '{s}'");
                        if (st == 0) Visit(s);
                    }
                }
                state[node] = 2;
            }
            foreach (var node in supertypes.Keys)
            {
                state.TryGetValue(node, out var st);
                if (st == 0) Visit(node);
            }
        }

        /// <summary>Minimal union-find keyed by name; canonical representative is the ordinal-least name.</summary>
        private sealed class UnionFind
        {
            private readonly Dictionary<string, string> _parent = new Dictionary<string, string>(StringComparer.Ordinal);

            private string Find(string x)
            {
                if (!_parent.TryGetValue(x, out var p)) { _parent[x] = x; return x; }
                while (p != x)
                {
                    _parent[x] = _parent[p]; // path halving
                    x = _parent[x];
                    p = _parent[x];
                }
                return x;
            }

            public void Union(string a, string b)
            {
                string ra = Find(a), rb = Find(b);
                if (ra == rb) return;
                // Point the larger toward the ordinal-smaller so the root is the canonical name.
                if (string.CompareOrdinal(ra, rb) <= 0) _parent[rb] = ra;
                else _parent[ra] = rb;
            }

            public string Canonical(string n) => Find(n);

            public Dictionary<string, string> CanonicalMap()
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in _parent.Keys) map[key] = Find(key);
                return map;
            }
        }
    }
}

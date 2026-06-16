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

            foreach (var stmt in parsed.Statements)
            {
                switch (stmt)
                {
                    case StructuralFact st:
                        ApplyStructural(st, Canon, directTypes, supertypes);
                        break;

                    case StoredFact fact:
                        AddStored(grouped, Canon(fact.Subject), fact.Relation, Canonicalize(fact.Value, Canon));
                        break;

                    case ComputedFact rule:
                        var canonRule = new ComputedFact(Canon(rule.Type), rule.Relation, rule.Expression, rule.Line);
                        if (!rules.TryGetValue(rule.Relation, out var list))
                            rules[rule.Relation] = list = new List<ComputedFact>();
                        list.Add(canonRule);
                        break;
                }
            }

            // 3. extends must form no cycles.
            DetectExtendsCycles(supertypes);

            // 4. Materialize stored value sets.
            var stored = new Dictionary<string, Dictionary<string, ValueSet>>(StringComparer.Ordinal);
            foreach (var entityKv in grouped)
            {
                var rels = new Dictionary<string, ValueSet>(StringComparer.Ordinal);
                foreach (var relKv in entityKv.Value)
                    rels[relKv.Key] = ValueSet.From(relKv.Value);
                stored[entityKv.Key] = rels;
            }

            return new World(stored, rules, new TypeGraph(directTypes, supertypes), canonical);
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

        private static Value Canonicalize(Value v, Func<string, string> canon) =>
            v.Kind == ValueKind.Entity ? Value.Entity(canon(v.AsEntity)) : v;

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

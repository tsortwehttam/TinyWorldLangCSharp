using System;
using System.Collections.Generic;
using TinyWorldLang.Parsing;
using TinyWorldLang.Values;

namespace TinyWorldLang.Model
{
    /// <summary>
    /// An immutable, loaded TWL world: stored facts, computed rules, and the
    /// type/identity graph. Built once and cached; evaluation never mutates it.
    /// </summary>
    public sealed class World
    {
        // (canonical entity, relation) -> stored values
        private readonly Dictionary<string, Dictionary<string, ValueSet>> _stored;
        // relation -> rules that produce it (each tagged with its declaring type)
        private readonly Dictionary<string, List<ComputedFact>> _rules;
        // (name, arity) -> module-level function
        private readonly Dictionary<(string, int), FunctionDecl> _functions;
        private readonly TypeGraph _types;
        private readonly Dictionary<string, string> _canonical; // raw name -> canonical name
        private readonly string[] _entities;

        internal World(
            Dictionary<string, Dictionary<string, ValueSet>> stored,
            Dictionary<string, List<ComputedFact>> rules,
            Dictionary<(string, int), FunctionDecl> functions,
            TypeGraph types,
            Dictionary<string, string> canonical,
            HashSet<string> entities)
        {
            _stored = stored;
            _rules = rules;
            _functions = functions;
            _types = types;
            _canonical = canonical;
            var orderedEntities = new List<string>(entities);
            orderedEntities.Sort(StringComparer.Ordinal);
            _entities = orderedEntities.ToArray();
        }

        public static World Load(string source) => WorldBuilder.Build(TwlParser.Parse(source));

        /// <summary>Resolve a name to its canonical form (after <c>sameas</c> merges).</summary>
        public string Canonical(string name) =>
            _canonical.TryGetValue(name, out var c) ? c : name;

        /// <summary>The stored values for a relation on an entity, or empty if none.</summary>
        public ValueSet StoredFacts(string canonicalEntity, string relation)
        {
            if (_stored.TryGetValue(canonicalEntity, out var rels) &&
                rels.TryGetValue(relation, out var set))
                return set;
            return ValueSet.Empty;
        }

        public bool HasStoredFacts(string canonicalEntity, string relation) =>
            _stored.TryGetValue(canonicalEntity, out var rels) && rels.ContainsKey(relation);

        /// <summary>Rules declared for a relation, in declaration order.</summary>
        public IReadOnlyList<ComputedFact> RulesFor(string relation) =>
            _rules.TryGetValue(relation, out var list) ? list : (IReadOnlyList<ComputedFact>)Array.Empty<ComputedFact>();

        internal IEnumerable<ComputedFact> ComputedRules()
        {
            foreach (var list in _rules.Values)
                foreach (var rule in list)
                    yield return rule;
        }

        /// <summary>The module function with this name and arity, or null if none is declared.</summary>
        public FunctionDecl? LookupFunction(string name, int arity) =>
            _functions.TryGetValue((name, arity), out var fn) ? fn : null;

        internal IEnumerable<FunctionDecl> Functions() => _functions.Values;

        public IReadOnlyList<string> Instances(string canonicalType)
        {
            var result = new List<string>();
            foreach (var e in _types.Instances(canonicalType)) result.Add(e);
            return result;
        }

        public bool IsInstanceOf(string canonicalEntity, string canonicalType) =>
            _types.IsInstanceOf(canonicalEntity, canonicalType);

        public int Specificity(string canonicalEntity, string canonicalType) =>
            _types.DistanceFromEntity(canonicalEntity, canonicalType);

        /// <summary>All canonical entities mentioned in stored, structural, or rule type positions.</summary>
        public IReadOnlyCollection<string> KnownEntities() => _entities;

        /// <summary>Relation names with stored facts on an entity.</summary>
        public IEnumerable<string> StoredRelationNames(string canonicalEntity) =>
            _stored.TryGetValue(canonicalEntity, out var rels) ? rels.Keys : System.Linq.Enumerable.Empty<string>();

        /// <summary>Every relation name that some computed rule produces.</summary>
        public IEnumerable<string> ComputedRelationNames() => _rules.Keys;

        /// <summary>Every canonical entity mentioned in the loaded world.</summary>
        public IEnumerable<string> AllEntities()
        {
            foreach (var e in _entities) yield return e;
        }
    }
}

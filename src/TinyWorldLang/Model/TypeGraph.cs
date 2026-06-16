using System.Collections.Generic;

namespace TinyWorldLang.Model
{
    /// <summary>
    /// The <c>instanceof</c> / <c>extends</c> structure over canonical entity names,
    /// with the transitive closure and shortest-path distances the tiebreak needs.
    /// </summary>
    internal sealed class TypeGraph
    {
        // entity -> its direct instanceof types
        private readonly Dictionary<string, HashSet<string>> _directTypes;
        // type -> its direct supertypes (A extends B => B is a supertype of A)
        private readonly Dictionary<string, HashSet<string>> _supertypes;

        public TypeGraph(
            Dictionary<string, HashSet<string>> directTypes,
            Dictionary<string, HashSet<string>> supertypes)
        {
            _directTypes = directTypes;
            _supertypes = supertypes;
        }

        /// <summary>All entities that are an instance of <paramref name="type"/>, directly or via a subtype.</summary>
        public IEnumerable<string> Instances(string type)
        {
            foreach (var kv in _directTypes)
            {
                if (DistanceFromEntity(kv.Key, type) >= 0)
                    yield return kv.Key;
            }
        }

        public bool IsInstanceOf(string entity, string type) => DistanceFromEntity(entity, type) >= 0;

        /// <summary>
        /// Shortest "is-a" distance from <paramref name="entity"/> to <paramref name="type"/>,
        /// or -1 if the entity is not an instance of the type. A direct
        /// <c>instanceof</c> type is distance 0; each <c>extends</c> step adds 1.
        /// </summary>
        public int DistanceFromEntity(string entity, string type)
        {
            if (!_directTypes.TryGetValue(entity, out var directs)) return -1;

            // BFS over the supertype graph, seeded with the direct types at distance 0.
            var dist = new Dictionary<string, int>();
            var queue = new Queue<string>();
            foreach (var t in directs)
            {
                if (!dist.ContainsKey(t)) { dist[t] = 0; queue.Enqueue(t); }
            }
            int best = -1;
            while (queue.Count > 0)
            {
                string node = queue.Dequeue();
                int d = dist[node];
                if (node == type && (best < 0 || d < best)) best = d;
                if (_supertypes.TryGetValue(node, out var supers))
                {
                    foreach (var s in supers)
                    {
                        if (!dist.ContainsKey(s)) { dist[s] = d + 1; queue.Enqueue(s); }
                    }
                }
            }
            return best;
        }
    }
}

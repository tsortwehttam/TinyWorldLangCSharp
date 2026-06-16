using System.Collections;
using System.Collections.Generic;

namespace TinyWorldLang.Values
{
    /// <summary>
    /// The result of reading a relation: an unordered, deduplicated set of values,
    /// materialized in canonical order so iteration and <c>one()</c> are reproducible.
    /// </summary>
    /// <remarks>
    /// Reading a relation is never an error; an absent relation is simply the empty
    /// set. <see cref="One"/> returns the canonical-first value or <c>null</c> when
    /// empty, never throwing on "too many" — matching the spec's <c>one()</c>.
    /// </remarks>
    public sealed class ValueSet : IReadOnlyList<Value>
    {
        public static readonly ValueSet Empty = new ValueSet(new Value[0]);

        private readonly Value[] _items; // already deduped + canonically ordered

        private ValueSet(Value[] items) => _items = items;

        /// <summary>Build a set from arbitrary values, deduplicating and ordering canonically.</summary>
        public static ValueSet From(IEnumerable<Value> values)
        {
            var ordered = new List<Value>();
            foreach (var v in values)
            {
                if (v.IsNull) continue; // null is never stored
                bool dup = false;
                foreach (var existing in ordered)
                {
                    if (existing.Equals(v)) { dup = true; break; }
                }
                if (!dup) ordered.Add(v);
            }
            ordered.Sort(CanonicalComparer.Instance);
            return ordered.Count == 0 ? Empty : new ValueSet(ordered.ToArray());
        }

        public int Count => _items.Length;
        public bool IsEmpty => _items.Length == 0;
        public Value this[int index] => _items[index];

        /// <summary>The canonical-first value, or <c>null</c> when empty (the spec's <c>one()</c>).</summary>
        public Value One() => _items.Length == 0 ? Value.Null : _items[0];

        public IEnumerator<Value> GetEnumerator()
        {
            foreach (var v in _items) yield return v;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

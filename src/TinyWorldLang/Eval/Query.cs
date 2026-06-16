using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TinyWorldLang.Values;

namespace TinyWorldLang.Eval
{
    /// <summary>
    /// A handle to one entity within a <see cref="WorldView"/>. Index it by relation
    /// name to read a <see cref="Field"/>. These are cheap, allocate-on-demand views;
    /// nothing is evaluated until you read a field.
    /// </summary>
    public sealed class Entity
    {
        private readonly WorldView _view;

        /// <summary>The entity's canonical name (after any <c>sameas</c> merge).</summary>
        public string Name { get; }

        internal Entity(WorldView view, string name) { _view = view; Name = name; }

        /// <summary>True if this entity is an instance of <paramref name="type"/> (directly or via a subtype).</summary>
        public bool Is(string type) => _view.IsInstanceOf(Name, type);

        /// <summary>Read a relation as a field. Reading is never an error — an absent relation is an empty field.</summary>
        public Field this[string relation] => new Field(_view, Name, relation);

        /// <summary>Same as the indexer, for callers who prefer a method.</summary>
        public Field Relation(string relation) => this[relation];

        /// <summary>
        /// The names of relations declared for this entity (its stored relations plus
        /// any computed relation whose type it matches), in canonical order. Cheap:
        /// does not evaluate the relations — read each via the indexer to get values.
        /// </summary>
        public IReadOnlyList<string> Relations => _view.RelationNamesFor(Name);

        public override string ToString() => Name;
    }

    /// <summary>
    /// The current state of one relation on one entity: a set of values (canonical
    /// order, deduplicated), with ergonomic accessors. String values present
    /// <em>already rendered</em> against their owner; the raw template source is
    /// available only via <see cref="Source"/>.
    /// </summary>
    public readonly struct Field : IReadOnlyList<Value>
    {
        private readonly WorldView _view;
        private readonly string _owner;
        private readonly string _relation;

        internal Field(WorldView view, string owner, string relation)
        {
            _view = view;
            _owner = owner;
            _relation = relation;
        }

        private ValueSet Set => _view.ResolveSet(_owner, _relation);

        public int Count => Set.Count;
        public bool IsEmpty => Set.IsEmpty;
        public bool HasValue => !Set.IsEmpty;
        public Value this[int index] => Set[index];

        /// <summary>The canonical-first value, or null when empty (the spec's <c>one()</c>).</summary>
        public Value One() => Set.One();

        /// <summary>The whole set as raw values (canonical order).</summary>
        public ValueSet Values => Set;

        /// <summary>Entity-valued members as handles (non-entity values are skipped).</summary>
        public IReadOnlyList<Entity> Entities
        {
            get
            {
                var list = new List<Entity>();
                foreach (var v in Set)
                    if (v.Kind == ValueKind.Entity) list.Add(_view.EntityHandle(v.AsEntity));
                return list;
            }
        }

        // -------- single-value typed accessors (operate on the canonical-first value) --------

        public long AsInt => One().AsInt;
        public double AsDouble => One().AsDouble;
        public bool AsBool => One().AsBool;
        public string AsEntity => One().AsEntity;

        /// <summary>
        /// The presentation text of the canonical-first value. A string value is
        /// rendered as a template (host escaper applied); other kinds render to their
        /// plain text; an empty field renders to "".
        /// </summary>
        public string Text => _view.RenderValueText(One(), _owner);

        /// <summary>Presentation text for every value in the set (canonical order).</summary>
        public IEnumerable<string> Texts
        {
            get
            {
                foreach (var v in Set) yield return _view.RenderValueText(v, _owner);
            }
        }

        /// <summary>
        /// Escape hatch: the raw, unrendered template source of a string value. Prefer
        /// <see cref="Text"/> almost always — this exists for debugging and tooling.
        /// </summary>
        public string Source => One().AsString;

        public override string ToString() => Text;

        public IEnumerator<Value> GetEnumerator() => Set.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

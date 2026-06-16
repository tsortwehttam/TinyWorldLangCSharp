using System;
using System.Collections.Generic;

namespace TinyWorldLang.Values
{
    /// <summary>
    /// The single canonical order TWL uses wherever a set needs an order
    /// (<c>one</c>, <c>sortBy</c>, multi-value template rendering).
    /// </summary>
    /// <remarks>
    /// Values are grouped by kind — booleans, then numbers, then strings, then
    /// entities — and ordered within each kind: <c>false</c> before <c>true</c>,
    /// numbers by value, strings by Unicode code point (ordinal), entities by
    /// canonical name. A string sorts by its *unrendered* source. Code-point order
    /// is not dictionary order: every uppercase letter sorts before every lowercase
    /// one, so name sorting is case-sensitive.
    /// </remarks>
    public sealed class CanonicalComparer : IComparer<Value>
    {
        public static readonly CanonicalComparer Instance = new CanonicalComparer();

        private CanonicalComparer() { }

        // Booleans < numbers < strings < entities. Null sorts first (sortBy keys an
        // empty relation as null, "before all real values").
        private static int KindRank(ValueKind k) => k switch
        {
            ValueKind.Null => 0,
            ValueKind.Bool => 1,
            ValueKind.Int => 2,
            ValueKind.Double => 2, // numbers are one kind for ordering
            ValueKind.String => 3,
            ValueKind.Entity => 4,
            _ => 5,
        };

        public int Compare(Value a, Value b)
        {
            int ra = KindRank(a.Kind), rb = KindRank(b.Kind);
            if (ra != rb) return ra.CompareTo(rb);

            switch (a.Kind)
            {
                case ValueKind.Null:
                    return 0;
                case ValueKind.Bool:
                    return a.AsBool.CompareTo(b.AsBool); // false < true
                case ValueKind.Int:
                case ValueKind.Double:
                    return a.NumericValue.CompareTo(b.NumericValue);
                case ValueKind.String:
                    return string.CompareOrdinal(a.AsString, b.AsString);
                case ValueKind.Entity:
                    return string.CompareOrdinal(a.AsEntity, b.AsEntity);
                default:
                    return 0;
            }
        }
    }
}

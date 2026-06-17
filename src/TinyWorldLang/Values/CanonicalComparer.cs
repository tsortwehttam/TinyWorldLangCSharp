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

        // Booleans < numbers < strings < entities < timestamps < lists < records.
        // Null sorts first (sortBy keys an empty relation as null, "before all real
        // values"). Lists, timestamps, and records get distinct ranks and a total
        // order below so a relation of records has a stable canonical order.
        private static int KindRank(ValueKind k) => k switch
        {
            ValueKind.Null => 0,
            ValueKind.Bool => 1,
            ValueKind.Int => 2,
            ValueKind.Double => 2, // numbers are one kind for ordering
            ValueKind.String => 3,
            ValueKind.Entity => 4,
            ValueKind.Timestamp => 5,
            ValueKind.List => 6,
            ValueKind.Record => 7,
            _ => 8,
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
                    return Value.CompareNumeric(a, b);
                case ValueKind.String:
                    return string.CompareOrdinal(a.AsString, b.AsString);
                case ValueKind.Entity:
                    return string.CompareOrdinal(a.AsEntity, b.AsEntity);
                case ValueKind.Timestamp:
                    return a.AsTimestamp.CompareTo(b.AsTimestamp);
                case ValueKind.List:
                    return CompareLists(a.AsList, b.AsList);
                case ValueKind.Record:
                    return CompareRecords(a.AsRecord, b.AsRecord);
                default:
                    return 0;
            }
        }

        // Lexicographic by element; a proper prefix sorts before the longer list.
        private int CompareLists(IReadOnlyList<Value> a, IReadOnlyList<Value> b)
        {
            int n = a.Count < b.Count ? a.Count : b.Count;
            for (int i = 0; i < n; i++)
            {
                int c = Compare(a[i], b[i]);
                if (c != 0) return c;
            }
            return a.Count.CompareTo(b.Count);
        }

        // By the sorted key list first, then by the values at those keys in key order.
        private int CompareRecords(IReadOnlyDictionary<string, Value> a, IReadOnlyDictionary<string, Value> b)
        {
            var ak = SortedKeys(a);
            var bk = SortedKeys(b);
            int n = ak.Count < bk.Count ? ak.Count : bk.Count;
            for (int i = 0; i < n; i++)
            {
                int c = string.CompareOrdinal(ak[i], bk[i]);
                if (c != 0) return c;
            }
            if (ak.Count != bk.Count) return ak.Count.CompareTo(bk.Count);

            // Same key set: compare values in key order.
            for (int i = 0; i < ak.Count; i++)
            {
                int c = Compare(a[ak[i]], b[bk[i]]);
                if (c != 0) return c;
            }
            return 0;
        }

        private static System.Collections.Generic.List<string> SortedKeys(IReadOnlyDictionary<string, Value> r)
        {
            var keys = new System.Collections.Generic.List<string>(r.Keys);
            keys.Sort(string.CompareOrdinal);
            return keys;
        }
    }
}

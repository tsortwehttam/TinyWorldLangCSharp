using System;
using System.Collections.Generic;
using System.Globalization;

namespace TinyWorldLang.Values
{
    /// <summary>The kinds a <see cref="Value"/> can take.</summary>
    /// <remarks>
    /// For the purposes of the spec's "different kinds are never ==" rule, the
    /// *comparison* kinds are: Bool, Number (Int and Double together), String,
    /// Entity. <see cref="List"/> and <see cref="Timestamp"/> are evaluation-time
    /// intermediates produced by CEL, never stored in a relation's set.
    /// </remarks>
    public enum ValueKind
    {
        Null,
        Bool,
        Int,
        Double,
        String,
        Entity,
        List,
        Timestamp,
        Record,
    }

    /// <summary>
    /// An immutable TWL value. A relation's set is a collection of these.
    /// </summary>
    /// <remarks>
    /// Stored as a struct with explicit fields (no boxing of scalars) so the hot
    /// path stays allocation-light and AOT-friendly. The integer/double split is
    /// syntactic and fixed per the spec: <c>1968</c> is always Int, <c>1968.0</c>
    /// always Double; arithmetic never mixes them, but they compare by value.
    /// </remarks>
    public readonly struct Value : IEquatable<Value>
    {
        public ValueKind Kind { get; }

        private readonly bool _bool;
        private readonly long _int;
        private readonly double _double;
        private readonly object? _ref; // string source | entity name | IReadOnlyList<Value> | DateTimeOffset box

        private Value(ValueKind kind, bool b, long i, double d, object? r)
        {
            Kind = kind;
            _bool = b;
            _int = i;
            _double = d;
            _ref = r;
        }

        public static readonly Value Null = new Value(ValueKind.Null, false, 0, 0, null);

        public static Value Bool(bool b) => new Value(ValueKind.Bool, b, 0, 0, null);
        public static Value Int(long i) => new Value(ValueKind.Int, false, i, 0, null);
        public static Value Double(double d) => new Value(ValueKind.Double, false, 0, d, null);

        /// <summary>A string value, carrying its *unrendered* template source.</summary>
        public static Value String(string source) =>
            new Value(ValueKind.String, false, 0, 0, source ?? throw new ArgumentNullException(nameof(source)));

        /// <summary>An entity reference, carrying the entity's canonical name.</summary>
        public static Value Entity(string canonicalName) =>
            new Value(ValueKind.Entity, false, 0, 0, canonicalName ?? throw new ArgumentNullException(nameof(canonicalName)));

        public static Value List(IReadOnlyList<Value> items) =>
            new Value(ValueKind.List, false, 0, 0, items ?? throw new ArgumentNullException(nameof(items)));

        public static Value Timestamp(DateTimeOffset when) =>
            new Value(ValueKind.Timestamp, false, 0, 0, when);

        /// <summary>An evaluation-time record (a session event's fields). Never stored in a relation.</summary>
        public static Value Record(IReadOnlyDictionary<string, Value> fields) =>
            new Value(ValueKind.Record, false, 0, 0, fields ?? throw new ArgumentNullException(nameof(fields)));

        public IReadOnlyDictionary<string, Value> AsRecord => Kind == ValueKind.Record
            ? (IReadOnlyDictionary<string, Value>)_ref!
            : throw new InvalidOperationException($"value is {Kind}, not Record");

        public bool IsNull => Kind == ValueKind.Null;
        public bool IsNumber => Kind == ValueKind.Int || Kind == ValueKind.Double;

        public bool AsBool => Kind == ValueKind.Bool
            ? _bool
            : throw new InvalidOperationException($"value is {Kind}, not Bool");

        public long AsInt => Kind == ValueKind.Int
            ? _int
            : throw new InvalidOperationException($"value is {Kind}, not Int");

        public double AsDouble => Kind == ValueKind.Double
            ? _double
            : throw new InvalidOperationException($"value is {Kind}, not Double");

        /// <summary>Numeric value as a double, accepting either Int or Double. Intended for display/tests; comparisons stay exact.</summary>
        public double NumericValue => Kind switch
        {
            ValueKind.Int => _int,
            ValueKind.Double => _double,
            _ => throw new InvalidOperationException($"value is {Kind}, not a number"),
        };

        public string AsString => Kind == ValueKind.String
            ? (string)_ref!
            : throw new InvalidOperationException($"value is {Kind}, not String");

        public string AsEntity => Kind == ValueKind.Entity
            ? (string)_ref!
            : throw new InvalidOperationException($"value is {Kind}, not Entity");

        public IReadOnlyList<Value> AsList => Kind == ValueKind.List
            ? (IReadOnlyList<Value>)_ref!
            : throw new InvalidOperationException($"value is {Kind}, not List");

        public DateTimeOffset AsTimestamp => Kind == ValueKind.Timestamp
            ? (DateTimeOffset)_ref!
            : throw new InvalidOperationException($"value is {Kind}, not Timestamp");

        /// <summary>
        /// Spec equality: different *kinds* are never equal (and it is not an
        /// error — a mistyped comparison is just <c>false</c>). Int and Double are
        /// the same kind for this purpose and compare by mathematical value.
        /// </summary>
        public bool Equals(Value other)
        {
            if (IsNumber && other.IsNumber)
                return NumericEquals(this, other);
            if (Kind != other.Kind)
                return false;
            return Kind switch
            {
                ValueKind.Null => true,
                ValueKind.Bool => _bool == other._bool,
                ValueKind.String => StringOrdinal((string)_ref!, (string)other._ref!) == 0,
                ValueKind.Entity => StringOrdinal((string)_ref!, (string)other._ref!) == 0,
                ValueKind.Timestamp => AsTimestamp == other.AsTimestamp,
                ValueKind.List => ListEquals(AsList, other.AsList),
                _ => false,
            };
        }

        private static bool ListEquals(IReadOnlyList<Value> a, IReadOnlyList<Value> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }

        internal static bool NumericEquals(Value a, Value b)
        {
            if (!a.IsNumber || !b.IsNumber)
                throw new InvalidOperationException("both values must be numeric");
            if (a.Kind == ValueKind.Int && b.Kind == ValueKind.Int)
                return a.AsInt == b.AsInt;
            if (a.Kind == ValueKind.Double && b.Kind == ValueKind.Double)
                return a.AsDouble == b.AsDouble;
            return CompareNumeric(a, b) == 0;
        }

        internal static int CompareNumeric(Value a, Value b)
        {
            if (!a.IsNumber || !b.IsNumber)
                throw new InvalidOperationException("both values must be numeric");
            if (a.Kind == ValueKind.Int && b.Kind == ValueKind.Int)
                return a.AsInt.CompareTo(b.AsInt);
            if (a.Kind == ValueKind.Double && b.Kind == ValueKind.Double)
                return a.AsDouble.CompareTo(b.AsDouble);
            if (a.Kind == ValueKind.Int)
                return CompareLongToDouble(a.AsInt, b.AsDouble);
            return -CompareLongToDouble(b.AsInt, a.AsDouble);
        }

        internal static bool TryGetExactInt64(double d, out long value)
        {
            value = 0;
            if (double.IsNaN(d) || double.IsInfinity(d))
                return false;
            if (d < long.MinValue || d >= 9223372036854775808.0)
                return false;
            var truncated = Math.Truncate(d);
            if (truncated != d)
                return false;
            value = (long)d;
            return (double)value == d;
        }

        private static int CompareLongToDouble(long i, double d)
        {
            if (double.IsNaN(d))
                return 1;
            if (double.IsNegativeInfinity(d))
                return 1;
            if (double.IsPositiveInfinity(d))
                return -1;
            if (d < long.MinValue)
                return 1;
            if (d >= 9223372036854775808.0)
                return -1;

            long whole = (long)d;
            int cmp = i.CompareTo(whole);
            if (cmp != 0) return cmp;

            double wholeAsDouble = whole;
            if (d > wholeAsDouble) return -1;
            if (d < wholeAsDouble) return 1;
            return 0;
        }

        public override bool Equals(object? obj) => obj is Value v && Equals(v);

        public override int GetHashCode()
        {
            // Deliberately simple and stable within a process. Note: TWL's reproducible
            // randomness does NOT use this — see Rand.StableHash for cross-run stability.
            switch (Kind)
            {
                case ValueKind.Null: return 0;
                case ValueKind.Bool: return _bool ? 1 : 2;
                case ValueKind.Int: return ((double)_int).GetHashCode();
                case ValueKind.Double: return _double.GetHashCode();
                case ValueKind.String: return StringComparer.Ordinal.GetHashCode((string)_ref!);
                case ValueKind.Entity: return StringComparer.Ordinal.GetHashCode((string)_ref!) ^ 0x5bd1e995;
                case ValueKind.List:
                    unchecked
                    {
                        int h = 17;
                        foreach (var item in AsList) h = h * 31 + item.GetHashCode();
                        return h;
                    }
                default: return _ref?.GetHashCode() ?? 0;
            }
        }

        internal static int StringOrdinal(string a, string b) => string.CompareOrdinal(a, b);

        public override string ToString() => Kind switch
        {
            ValueKind.Null => "null",
            ValueKind.Bool => _bool ? "true" : "false",
            ValueKind.Int => _int.ToString(CultureInfo.InvariantCulture),
            ValueKind.Double => _double.ToString("R", CultureInfo.InvariantCulture),
            ValueKind.String => (string)_ref!,
            ValueKind.Entity => (string)_ref!,
            ValueKind.Timestamp => AsTimestamp.ToString("o", CultureInfo.InvariantCulture),
            ValueKind.List => "[" + string.Join(", ", ListStrings()) + "]",
            _ => "?",
        };

        private IEnumerable<string> ListStrings()
        {
            foreach (var v in AsList) yield return v.ToString();
        }
    }
}

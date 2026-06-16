using System;
using System.Globalization;
using System.Text;
using TinyWorldLang.Values;

namespace TinyWorldLang.Rand
{
    /// <summary>
    /// A stable, cross-platform 64-bit FNV-1a hash over a canonical byte encoding
    /// of a <see cref="Value"/> key plus the per-evaluation seed.
    /// </summary>
    /// <remarks>
    /// CRITICAL: do not reach for <see cref="object.GetHashCode"/> here.
    /// .NET randomizes <c>string.GetHashCode()</c> per process, which would make
    /// <c>rand(key)</c> differ every run and break the spec's reproducibility
    /// guarantee. The encoding below is fixed and versioned by its tag bytes.
    ///
    /// The key may be a list; parts are length-prefixed so that <c>["a","bc"]</c>
    /// and <c>["ab","c"]</c> hash differently (the spec's "gluing strings collides"
    /// caution). Entities hash by canonical name, which a <c>sameas</c> merge has
    /// already collapsed, keeping draws stable across a merge.
    /// </remarks>
    public static class StableHash
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        // Tag bytes keep kinds distinct so 1 (int) and "1" (string) never collide.
        private const byte TagSeed = 0x01;
        private const byte TagBool = 0x02;
        private const byte TagNumber = 0x03; // int and double share a tag: 1 and 1.0 are equal, so they key alike
        private const byte TagString = 0x05;
        private const byte TagEntity = 0x06;
        private const byte TagList = 0x07;
        private const byte TagNull = 0x08;

        /// <summary>Hash <paramref name="key"/> together with <paramref name="seed"/> into a 64-bit value.</summary>
        public static ulong Hash(ulong seed, Value key)
        {
            ulong h = FnvOffset;
            h = Mix(h, TagSeed);
            h = MixUInt64(h, seed);
            h = HashValue(h, key);
            return h;
        }

        private static ulong HashValue(ulong h, Value v)
        {
            switch (v.Kind)
            {
                case ValueKind.Null:
                    return Mix(h, TagNull);
                case ValueKind.Bool:
                    h = Mix(h, TagBool);
                    return Mix(h, v.AsBool ? (byte)1 : (byte)0);
                case ValueKind.Int:
                    h = Mix(h, TagNumber);
                    return MixInt64(h, v.AsInt);
                case ValueKind.Double:
                    // Equal numeric keys must hash alike, so integral doubles that are
                    // exactly representable as Int64 share the integer encoding.
                    h = Mix(h, TagNumber);
                    if (Value.TryGetExactInt64(v.AsDouble, out var i))
                        return MixInt64(h, i);
                    return MixUInt64(h, (ulong)BitConverter.DoubleToInt64Bits(v.AsDouble));
                case ValueKind.String:
                    h = Mix(h, TagString);
                    return HashBytes(h, Encoding.UTF8.GetBytes(v.AsString));
                case ValueKind.Entity:
                    h = Mix(h, TagEntity);
                    return HashBytes(h, Encoding.UTF8.GetBytes(v.AsEntity));
                case ValueKind.List:
                    h = Mix(h, TagList);
                    var list = v.AsList;
                    h = MixUInt64(h, (ulong)list.Count); // length-prefix the list itself
                    foreach (var item in list)
                        h = HashValue(h, item);
                    return h;
                default:
                    throw new ArgumentException(
                        $"value of kind {v.Kind} cannot be used as a rand() key",
                        nameof(v));
            }
        }

        private static ulong HashBytes(ulong h, byte[] bytes)
        {
            h = MixUInt64(h, (ulong)bytes.Length); // length-prefix so concatenations don't collide
            foreach (var b in bytes) h = Mix(h, b);
            return h;
        }

        private static ulong MixUInt64(ulong h, ulong x)
        {
            for (int i = 0; i < 8; i++)
            {
                h = Mix(h, (byte)(x & 0xFF));
                x >>= 8;
            }
            return h;
        }

        private static ulong MixInt64(ulong h, long x) => MixUInt64(h, unchecked((ulong)x));

        private static ulong Mix(ulong h, byte b)
        {
            h ^= b;
            h *= FnvPrime;
            return h;
        }

        /// <summary>Map a 64-bit hash to a double in [0, 1) using the top 53 bits (one ULP per step).</summary>
        public static double ToUnitInterval(ulong h)
        {
            // 53 bits of mantissa precision; divide by 2^53. Exactly representable, deterministic.
            const double inv = 1.0 / 9007199254740992.0; // 1 / 2^53
            return (h >> 11) * inv;
        }
    }
}

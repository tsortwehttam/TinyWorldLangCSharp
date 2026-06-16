using System.Linq;
using TinyWorldLang.Values;
using Xunit;

namespace TinyWorldLang.Tests
{
    public class ValueAndOrderTests
    {
        [Fact]
        public void IntAndDouble_AreEqualByValue_AndSameKindForOrdering()
        {
            Assert.True(Value.Int(1).Equals(Value.Double(1.0)));
            // They sort together as one numeric kind.
            var set = ValueSet.From(new[] { Value.Double(2.0), Value.Int(1), Value.Int(3) });
            Assert.Equal(new[] { 1.0, 2.0, 3.0 }, set.Select(v => v.NumericValue));
        }

        [Fact]
        public void LargeIntegers_AreComparedAndDeduplicatedExactly()
        {
            var a = Value.Int(9007199254740992L);
            var b = Value.Int(9007199254740993L);

            Assert.False(a.Equals(b));
            Assert.False(b.Equals(Value.Double(9007199254740992.0)));

            var set = ValueSet.From(new[] { b, a, Value.Double(9007199254740992.0) });
            Assert.Equal(2, set.Count);
            Assert.Equal(9007199254740992L, set[0].AsInt);
            Assert.Equal(9007199254740993L, set[1].AsInt);
        }

        [Fact]
        public void DifferentKinds_AreNeverEqual_NotAnError()
        {
            Assert.False(Value.Int(1).Equals(Value.String("1")));
            Assert.False(Value.Bool(true).Equals(Value.Entity("True")));
        }

        [Fact]
        public void CanonicalOrder_GroupsByKind_BoolsNumbersStringsEntities()
        {
            var set = ValueSet.From(new[]
            {
                Value.Entity("Zoe"),
                Value.String("adam"),
                Value.Int(5),
                Value.Bool(true),
                Value.Bool(false),
                Value.String("Zoe"),
            });
            // bools (false,true) < numbers < strings (code-point: "Zoe" before "adam") < entities
            Assert.Equal(ValueKind.Bool, set[0].Kind);
            Assert.False(set[0].AsBool);
            Assert.True(set[1].AsBool);
            Assert.Equal(ValueKind.Int, set[2].Kind);
            Assert.Equal("Zoe", set[3].AsString); // uppercase sorts before lowercase
            Assert.Equal("adam", set[4].AsString);
            Assert.Equal(ValueKind.Entity, set[5].Kind);
        }

        [Fact]
        public void Set_DeduplicatesAndDropsNull()
        {
            var set = ValueSet.From(new[] { Value.Int(1), Value.Int(1), Value.Null, Value.Double(1.0) });
            Assert.Single(set); // 1 and 1.0 are equal; null dropped
        }

        [Fact]
        public void One_OnEmpty_IsNull_AndNeverThrowsOnMany()
        {
            Assert.True(ValueSet.Empty.One().IsNull);
            var many = ValueSet.From(new[] { Value.Int(3), Value.Int(1), Value.Int(2) });
            Assert.Equal(1, many.One().AsInt); // canonical-first, silently
        }
    }
}

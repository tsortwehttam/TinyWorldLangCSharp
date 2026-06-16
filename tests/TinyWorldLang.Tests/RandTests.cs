using TinyWorldLang;
using TinyWorldLang.Eval;
using TinyWorldLang.Rand;
using TinyWorldLang.Values;
using Xunit;

namespace TinyWorldLang.Tests
{
    public class RandTests
    {
        [Fact]
        public void Rand_IsStableForSameKeyAndSeed()
        {
            var a = StableHash.ToUnitInterval(StableHash.Hash(42, Value.String("daily")));
            var b = StableHash.ToUnitInterval(StableHash.Hash(42, Value.String("daily")));
            Assert.Equal(a, b);
            Assert.InRange(a, 0.0, 1.0);
            Assert.True(a < 1.0);
        }

        [Fact]
        public void Rand_DiffersByKeyAndBySeed()
        {
            var k1 = StableHash.Hash(42, Value.String("a"));
            var k2 = StableHash.Hash(42, Value.String("b"));
            var k3 = StableHash.Hash(7, Value.String("a"));
            Assert.NotEqual(k1, k2);
            Assert.NotEqual(k1, k3);
        }

        [Fact]
        public void ListKey_IsNotGluedFromParts()
        {
            // ["a","bc"] must differ from ["ab","c"] (the spec's gluing-collision caution).
            var ab_c = StableHash.Hash(1, Value.List(new[] { Value.String("ab"), Value.String("c") }));
            var a_bc = StableHash.Hash(1, Value.List(new[] { Value.String("a"), Value.String("bc") }));
            Assert.NotEqual(ab_c, a_bc);
        }

        [Fact]
        public void IntAndDoubleKeys_OfEqualValue_HashSame()
        {
            // 1 == 1.0 under the spec, so they should key identically.
            Assert.Equal(StableHash.Hash(5, Value.Int(1)), StableHash.Hash(5, Value.Double(1.0)));
        }

        [Fact]
        public void Rand_InComputedRule_IsReproducible()
        {
            var world = TwlWorld.Load(@"
                Die extends Entity; D instanceof Die;
                Die roll = int(rand([self, ""roll""]) * 6.0) + 1;
            ");
            var r1 = world.Evaluate(env: new Env(System.DateTimeOffset.UnixEpoch, seed: 123)).GetOne("D", "roll").AsInt;
            var r2 = world.Evaluate(env: new Env(System.DateTimeOffset.UnixEpoch, seed: 123)).GetOne("D", "roll").AsInt;
            Assert.Equal(r1, r2);
            Assert.InRange(r1, 1, 6);
        }
    }
}

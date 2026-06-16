using TinyWorldLang;
using Xunit;

namespace TinyWorldLang.Tests
{
    // has(), cel.bind(), and the Mustache-faithful "false is falsy" section rule.
    public class HasBindAndBoolSectionTests
    {
        [Fact]
        public void Has_IsTrueForPresentAndFalseForAbsent()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X name ""Marty"";
                T hasName    = has(self.name);
                T hasMissing = has(self.nope);
            ");
            var view = world.Evaluate();
            Assert.True(view.Entity("X")["hasName"].AsBool);
            Assert.False(view.Entity("X")["hasMissing"].AsBool);
        }

        [Fact]
        public void Has_GivesSafeNavigation_OverNull()
        {
            // one(self.family) is null (no family); .name would error, but has() catches
            // the failed resolution and reports false instead.
            var world = TwlWorld.Load(@"
                Person extends Entity; Fam extends Entity;
                Marty instanceof Person;
                Person hasFamName = has(one(self.family).name);
            ");
            Assert.False(world.Evaluate().Entity("Marty")["hasFamName"].AsBool);
        }

        [Fact]
        public void CelBind_AliasesASubexpression()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X hp 40;
                T doubled = cel.bind(h, one(self.hp), h + h);
            ");
            Assert.Equal(80, world.Evaluate().Entity("X")["doubled"].AsInt);
        }

        [Fact]
        public void CelBind_NestsForMultipleLocals()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X a 3; X b 4;
                T sumSquares = cel.bind(a, one(self.a),
                                 cel.bind(b, one(self.b), a*a + b*b));
            ");
            Assert.Equal(25, world.Evaluate().Entity("X")["sumSquares"].AsInt);
        }

        [Fact]
        public void Section_OverRealBool_TreatsFalseAsFalsy()
        {
            // A single real bool now drives a {{# }} branch: true renders, false does not.
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Hero instanceof Person; Hero hurt 10;
                Coward instanceof Person; Coward hurt 90;
                Person wounded = one(self.hurt) >= 50;
                Person line = ""status:{{# wounded}} bleeding{{/}}"";
            ");
            var view = world.Evaluate();
            Assert.Equal("status:", view.Entity("Hero")["line"].Text);          // false -> nothing
            Assert.Equal("status: bleeding", view.Entity("Coward")["line"].Text); // true  -> rendered
        }

        [Fact]
        public void BoolRelation_IsReadableByHostAndBranchable()
        {
            // The duality is gone: the SAME relation reads as a bool and branches a template.
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Coward instanceof Person; Coward hurt 90;
                Person wounded = one(self.hurt) >= 50;
                Person line = ""{{# wounded}}ouch{{/}}"";
            ");
            var coward = world.Evaluate().Entity("Coward");
            Assert.True(coward["wounded"].AsBool);          // host reads it as a bool
            Assert.Equal("ouch", coward["line"].Text);      // and the template branches on it
        }

        [Fact]
        public void Cel_IsReservedAsAName()
        {
            Assert.ThrowsAny<TwlException>(() => TwlWorld.Load("T extends Entity; cel instanceof T;"));
        }
    }
}

using TinyWorldLang.Parsing;
using TinyWorldLang.Values;
using Xunit;

namespace TinyWorldLang.Tests
{
    public class ParserTests
    {
        [Fact]
        public void ComputedExpression_AllowsInlineComments_AndKeepsDivision()
        {
            // // and /* */ inside a multi-line expression are trivia; a lone / stays division.
            var world = TinyWorldLang.TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X a 6; X b 4;
                T total = one(self.a)        // the first part
                        + one(self.b)        /* and the second */ ;
                T half  = one(self.a) / 2;   // a single slash is still division
                T glue  = one(self.a)/* c */+ one(self.b);
            ");
            var view = world.Evaluate();
            Assert.Equal(10, view.Entity("X")["total"].AsInt);
            Assert.Equal(3, view.Entity("X")["half"].AsInt);
            Assert.Equal(10, view.Entity("X")["glue"].AsInt);
        }

        [Fact]
        public void ParsesStoredFacts_StructuralFacts_AndComputed()
        {
            var parsed = TwlParser.Parse(@"
                // a comment
                Person extends Entity;
                Marty instanceof Person;
                Marty bornYear 1968;
                Marty height 1.78;
                Marty active true;
                Person age = now.getFullYear() - one(self.bornYear);
            ");

            Assert.Collection(parsed.Statements,
                s => Assert.True(s is StructuralFact { Relation: BuiltinRelation.Extends, Left: "Person", Right: "Entity" }),
                s => Assert.True(s is StructuralFact { Relation: BuiltinRelation.InstanceOf }),
                s => Assert.Equal(ValueKind.Int, ((StoredFact)s).Value.Kind),
                s => Assert.Equal(ValueKind.Double, ((StoredFact)s).Value.Kind),
                s => Assert.Equal(ValueKind.Bool, ((StoredFact)s).Value.Kind),
                s => Assert.Equal("now.getFullYear() - one(self.bornYear)", ((ComputedFact)s).Expression));
        }

        [Fact]
        public void IntegerVsDouble_IsSyntactic()
        {
            var p = TwlParser.Parse("X a 1968; X b 1968.0;");
            Assert.Equal(ValueKind.Int, ((StoredFact)p.Statements[0]).Value.Kind);
            Assert.Equal(ValueKind.Double, ((StoredFact)p.Statements[1]).Value.Kind);
        }

        [Fact]
        public void StringValue_KeepsRawSource_AndAllowsSemicolonsAndNewlines()
        {
            var p = TwlParser.Parse("Biff persona \"A bully;\nsecond line\";");
            var fact = (StoredFact)p.Statements[0];
            Assert.Equal(ValueKind.String, fact.Value.Kind);
            Assert.Contains(";", fact.Value.AsString);
            Assert.Contains("\n", fact.Value.AsString);
        }

        [Fact]
        public void NullValue_IsRejected()
        {
            var ex = Assert.Throws<TwlLoadException>(() => TwlParser.Parse("X r null;"));
            Assert.Contains("null", ex.Message);
        }

        [Fact]
        public void ReservedEntityNames_AreRejectedInEntityPositions()
        {
            Assert.Throws<TwlLoadException>(() => TwlParser.Parse("self r 1;"));
            Assert.Throws<TwlLoadException>(() => TwlParser.Parse("X sameas now;"));
            Assert.Throws<TwlLoadException>(() => TwlParser.Parse("X r math;"));
        }

        [Fact]
        public void InvalidEscape_IsRejected()
        {
            Assert.Throws<TwlLoadException>(() => TwlParser.Parse("X s \"bad \\q escape\";"));
        }

        [Fact]
        public void ComputedExpression_WithStringContainingSemicolon_ScansToRealTerminator()
        {
            var p = TwlParser.Parse("Person label = \"a;b\" + \"c\";");
            Assert.Equal("\"a;b\" + \"c\"", ((ComputedFact)p.Statements[0]).Expression);
        }
    }
}

using System;
using System.Linq;
using TinyWorldLang;
using TinyWorldLang.Eval;
using Xunit;

namespace TinyWorldLang.Tests
{
    public class TemplateAndIntegrationTests
    {
        [Fact]
        public void PlainStringValue_RendersToItself()
        {
            var view = TwlWorld.Load("Marty firstName \"Marty\";").Evaluate();
            Assert.Equal("Marty", view.Entity("Marty")["firstName"].Text);
        }

        [Fact]
        public void Tag_InsertsRelationValue()
        {
            var view = TwlWorld.Load(@"
                Marty firstName ""Marty"";
                Marty greeting ""Hi, I'm {{firstName}}"";
            ").Evaluate();
            Assert.Equal("Hi, I'm Marty", view.Entity("Marty")["greeting"].Text);
        }

        [Fact]
        public void EscapedBraces_AreLiteral_NotTags()
        {
            var view = TwlWorld.Load("X t \"literal \\{\\{notATag}}\";").Evaluate();
            Assert.Equal("literal {{notATag}}", view.Entity("X")["t"].Text);
        }

        [Fact]
        public void Escaper_AppliesToDoubleTags_ButNotTriple()
        {
            var world = TwlWorld.Load(@"
                X raw ""<b>"";
                X escaped ""{{raw}}"";
                X unescaped ""{{{raw}}}"";
            ", escaper: s => s.Replace("<", "&lt;").Replace(">", "&gt;"));
            var view = world.Evaluate();
            Assert.Equal("&lt;b&gt;", view.Entity("X")["escaped"].Text);
            Assert.Equal("<b>", view.Entity("X")["unescaped"].Text);
        }

        [Fact]
        public void Section_RepeatsOverASet()
        {
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Family extends Entity;
                Person lastName = one(one(self.family).name);

                McFlyFam instanceof Family; McFlyFam name ""McFly"";
                Marty instanceof Person; Marty firstName ""Marty""; Marty family McFlyFam;
                Lorraine instanceof Person; Lorraine firstName ""Lorraine""; Lorraine family McFlyFam;
                Biff instanceof Person; Biff firstName ""Biff""; Biff dislikes McFlyFam;

                Person enemies = instances(Person).filter(p, self.dislikes.exists(fam, fam in p.family) && self != p);
                Biff persona ""A bully with disdain for:{{# enemies}} {{firstName}} {{lastName}};{{/}}"";
            ");
            var rendered = world.Evaluate().Entity("Biff")["persona"].Text;
            // Enemies are Marty and Lorraine (both McFly), in canonical (name) order.
            Assert.Contains("Lorraine McFly", rendered);
            Assert.Contains("Marty McFly", rendered);
        }

        [Fact]
        public void FullSpecExample_LoadsAndQueries()
        {
            var world = TwlWorld.Load(SpecExample);
            var view = world.Evaluate(env: new Env(new DateTimeOffset(1985, 10, 26, 0, 0, 0, TimeSpan.Zero)));

            Assert.Equal(1985 - 1968, view.Entity("Marty")["age"].AsInt);
            Assert.Equal("McFly", view.Entity("Marty")["lastName"].Text);

            // Query the relation across the whole type via LINQ.
            var ages = view.Entities("Person").ToDictionary(p => p.Name, p => p["age"].AsInt);
            Assert.Equal(1985 - 1949, ages["Lorraine"]);

            var persona = view.Entity("Biff")["persona"].Text;
            Assert.Contains("Marty McFly", persona);
            Assert.Contains("Lorraine McFly", persona);
        }

        // Mirrors SPEC.md's example section.
        private const string SpecExample = @"
            Person extends Entity;
            Family extends Entity;

            Person age = now.getFullYear() - one(self.bornYear);
            Person lastName = one(one(self.family).name);
            Person enemies = instances(Person).filter(p, self.dislikes.exists(fam, fam in p.family) && self != p);

            McFlyFam instanceof Family; McFlyFam name ""McFly"";

            Marty instanceof Person; Marty firstName ""Marty"";
            Marty bornYear 1968; Marty family McFlyFam;

            Lorraine instanceof Person; Lorraine firstName ""Lorraine"";
            Lorraine bornYear 1949; Lorraine family McFlyFam;

            Biff instanceof Person; Biff firstName ""Biff"";
            Biff bornYear 1937; Biff dislikes McFlyFam;
            Biff persona ""A bully with disdain for:
            {{# enemies}}
              - {{firstName}} {{lastName}}
            {{/}}"";
        ";
    }
}

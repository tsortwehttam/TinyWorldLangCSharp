using System;
using System.Linq;
using TinyWorldLang;
using TinyWorldLang.Eval;
using Xunit;

namespace TinyWorldLang.Tests
{
    /// <summary>
    /// Exercises the one general query surface: entity handles + standard LINQ,
    /// record-oriented ("fields from every entity matching a condition"), with string
    /// values auto-rendering.
    /// </summary>
    public class QueryApiTests
    {
        private static WorldView World() => TwlWorld.Load(@"
            Person extends Entity;
            Family extends Entity;
            Person age = now.getFullYear() - one(self.bornYear);
            Person lastName = one(one(self.family).name);

            McFlyFam instanceof Family; McFlyFam name ""McFly"";

            Marty instanceof Person; Marty firstName ""Marty"";
            Marty bornYear 1968; Marty family McFlyFam;
            Lorraine instanceof Person; Lorraine firstName ""Lorraine"";
            Lorraine bornYear 1949; Lorraine family McFlyFam;
            Doc instanceof Person; Doc firstName ""Doc""; Doc bornYear 1920;
        ").Evaluate(env: new Env(new DateTimeOffset(1985, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        [Fact]
        public void EveryEntityOfAType_WithProjectedFields()
        {
            // "give me a list of every Person with these fields"
            var people = World().Entities("Person")
                .Select(p => new
                {
                    p.Name,
                    First = p["firstName"].Text,  // string -> rendered
                    Age = p["age"].AsInt,         // computed int
                })
                .OrderBy(p => p.Name)
                .ToList();

            Assert.Equal(3, people.Count);
            Assert.Equal(new[] { "Doc", "Lorraine", "Marty" }, people.Select(p => p.Name));
            Assert.Equal(1985 - 1968, people.Single(p => p.Name == "Marty").Age);
        }

        [Fact]
        public void FieldsFromEntitiesMatchingACondition()
        {
            // "such-and-such fields from all entities that match some condition"
            // In 1985: Marty 17, Lorraine 36, Doc 65.
            var elders = World().Entities("Person")
                .Where(p => p["age"].AsInt >= 30)
                .Select(p => p["firstName"].Text)
                .OrderBy(name => name)
                .ToList();

            Assert.Equal(new[] { "Doc", "Lorraine" }, elders); // 65 and 36; Marty (17) excluded
        }

        [Fact]
        public void EntityRelations_EnumeratesItsFields_GenericallyAndAutoRenders()
        {
            // "every Person with all their relations" — a generic record, no field names hardcoded.
            var marty = World().Entity("Marty");

            Assert.Contains("firstName", marty.Relations);
            Assert.Contains("age", marty.Relations);     // computed relations show up too
            Assert.Contains("lastName", marty.Relations);

            var record = marty.Relations.ToDictionary(r => r, r => marty[r].Text);
            Assert.Equal("Marty", record["firstName"]);
            Assert.Equal("McFly", record["lastName"]); // rendered through one(one(self.family).name)
        }

        [Fact]
        public void StringValues_AutoRender_RawAvailableViaSource()
        {
            var view = TwlWorld.Load(@"
                Marty firstName ""Marty"";
                Marty greeting ""Hi, I'm {{firstName}}"";
            ").Evaluate();

            var greeting = view.Entity("Marty")["greeting"];
            Assert.Equal("Hi, I'm Marty", greeting.Text);     // default presentation: rendered
            Assert.Equal("Hi, I'm Marty", greeting.ToString()); // ToString renders too
            Assert.Equal("Hi, I'm {{firstName}}", greeting.Source); // escape hatch: raw template
        }

        [Fact]
        public void MultiValued_FieldExposesEntitiesAndAllTexts()
        {
            var view = TwlWorld.Load(@"
                Family extends Entity;
                McFlyFam instanceof Family;
                McFlyFam member Marty; McFlyFam member Lorraine;
            ").Evaluate();

            var members = view.Entity("McFlyFam")["member"];
            Assert.Equal(2, members.Count);
            Assert.Equal(new[] { "Lorraine", "Marty" }, members.Entities.Select(e => e.Name)); // canonical order
            Assert.Equal(new[] { "Lorraine", "Marty" }, members.Texts);
        }

        [Fact]
        public void Entities_WithoutType_ReturnsAllKnownEntities()
        {
            var view = TwlWorld.Load(@"
                Person extends Entity;
                Marty instanceof Person;
                Biff instanceof Person;
            ").Evaluate();

            var names = view.Entities().Select(e => e.Name).OrderBy(n => n).ToList();
            Assert.Contains("Marty", names);
            Assert.Contains("Biff", names);
        }
    }
}

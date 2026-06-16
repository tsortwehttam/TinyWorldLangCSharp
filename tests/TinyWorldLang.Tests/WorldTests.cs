using TinyWorldLang;
using Xunit;

namespace TinyWorldLang.Tests
{
    public class WorldTests
    {
        [Fact]
        public void StoredFact_WinsOverComputed()
        {
            var world = TwlWorld.Load(@"
                Thing extends Entity;
                Widget instanceof Thing;
                Thing color = ""computed"";
                Widget color ""stored"";
            ");
            var view = world.Evaluate();
            Assert.Equal("stored", view.Entity("Widget")["color"].Text);
        }

        [Fact]
        public void MoreSpecificType_WinsTiebreak()
        {
            var world = TwlWorld.Load(@"
                Animal extends Entity;
                Dog extends Animal;
                Rex instanceof Dog;
                Animal sound = ""generic"";
                Dog sound = ""woof"";
            ");
            Assert.Equal("woof", world.Evaluate().Entity("Rex")["sound"].Text);
        }

        [Fact]
        public void EquallySpecificRules_AreAmbiguousError()
        {
            var world = TwlWorld.Load(@"
                A extends Entity;
                B extends Entity;
                X instanceof A;
                X instanceof B;
                A v = 1;
                B v = 2;
            ");
            Assert.Throws<TwlLoadException>(() => world.Evaluate().Entity("X")["v"].One());
        }

        [Fact]
        public void ExtendsCycle_IsLoadError()
        {
            var ex = Assert.Throws<TwlLoadException>(() => TwlWorld.Load("A extends B; B extends A;"));
            Assert.Contains("cycle", ex.Message);
        }

        [Fact]
        public void SameAs_MergesEntities_AndSharesFacts()
        {
            var world = TwlWorld.Load(@"
                Robert sameas Bob;
                Bob age 30;
            ");
            var view = world.Evaluate();
            // Canonical name is the ordinal-least ("Bob"); both names resolve to it.
            Assert.Equal(30, view.Entity("Robert")["age"].AsInt);
            Assert.Equal(30, view.Entity("Bob")["age"].AsInt);
            Assert.Equal("Bob", view.Entity("Robert").Name);
        }

        [Fact]
        public void InstanceOf_IsTransitiveThroughExtends()
        {
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Wizard extends Person;
                Gandalf instanceof Wizard;
            ");
            var view = world.Evaluate();
            Assert.True(view.Entity("Gandalf").Is("Person"));
            Assert.True(view.Entity("Gandalf").Is("Entity"));
        }

        [Fact]
        public void UnknownRelation_IsEmpty_NotError()
        {
            var view = TwlWorld.Load("Marty instanceof Person;").Evaluate();
            Assert.True(view.Entity("Marty")["nothingHere"].IsEmpty);
        }
    }
}

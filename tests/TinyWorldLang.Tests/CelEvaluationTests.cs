using System;
using TinyWorldLang;
using TinyWorldLang.Eval;
using Xunit;

namespace TinyWorldLang.Tests
{
    public class CelEvaluationTests
    {
        private static Env At(int year) => new Env(new DateTimeOffset(year, 6, 1, 0, 0, 0, TimeSpan.Zero));

        [Fact]
        public void Arithmetic_WithNow_AndOne()
        {
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Marty instanceof Person; Marty bornYear 1968;
                Person age = now.getFullYear() - one(self.bornYear);
            ");
            Assert.Equal(2025 - 1968, world.Evaluate(env: At(2025)).GetOne("Marty", "age").AsInt);
        }

        [Fact]
        public void MissingValue_Propagates_AsError()
        {
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Marty instanceof Person; // no bornYear
                Person age = now.getFullYear() - one(self.bornYear);
            ");
            Assert.ThrowsAny<TwlException>(() => world.Evaluate(env: At(2025)).Get("Marty", "age"));
        }

        [Fact]
        public void MixingIntAndDouble_IsError()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T bad = 1 + 1.0;
            ");
            Assert.ThrowsAny<TwlException>(() => world.Evaluate().Get("X", "bad"));
        }

        [Fact]
        public void FilterExistsAndMembership_ComputeRelations()
        {
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Family extends Entity;
                McFlyFam instanceof Family;
                Marty instanceof Person; Marty family McFlyFam;
                Lorraine instanceof Person; Lorraine family McFlyFam;
                Biff instanceof Person; Biff dislikes McFlyFam;
                Person enemies = instances(Person).filter(p, self.dislikes.exists(fam, fam in p.family) && self != p);
            ");
            var view = world.Evaluate();
            var biffEnemies = view.Get("Biff", "enemies");
            Assert.Equal(2, biffEnemies.Count); // Marty and Lorraine
            Assert.True(view.Get("Marty", "enemies").IsEmpty);
        }

        [Fact]
        public void SortBy_OrdersByRelation_NullKeyFirst()
        {
            // A sortBy result is only meaningful consumed *inside* an expression: stored
            // straight into a multi-value relation it would become an (unordered) set and
            // re-sort canonically. So assert the ordering via list equality and one().
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Fam extends Entity; F instanceof Fam;
                A instanceof Person; A bornYear 1990;
                B instanceof Person; B bornYear 1970;
                C instanceof Person; // no bornYear -> null key sorts first
                F members A; F members B; F members C;
                Fam ordered = sortBy(self.members, ""bornYear"") == [C, B, A];
                Fam first = one(sortBy(self.members, ""bornYear""));
            ");
            var view = world.Evaluate();
            Assert.True(view.GetOne("F", "ordered").AsBool); // [C(null), B(1970), A(1990)]
            Assert.Equal("C", view.GetOne("F", "first").AsEntity);
        }

        [Fact]
        public void Ternary_MathAndConversions()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T; X hp 250;
                T clamped = math.least(math.greatest(one(self.hp), 0), 100);
                T half = int(double(one(self.hp)) / 2.0);
            ");
            var view = world.Evaluate();
            Assert.Equal(100, view.GetOne("X", "clamped").AsInt);
            Assert.Equal(125, view.GetOne("X", "half").AsInt);
        }

        [Fact]
        public void SessionEvents_AreReadableViaCel()
        {
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Biff instanceof Person;
                Person insultCount = size(session.events.filter(e, e.target == self && e.verb == ""insult""));
            ");
            var events = new[]
            {
                Ev(("actor", "Marty"), ("target", "Biff"), ("verb", "insult")),
                Ev(("actor", "Lorraine"), ("target", "Biff"), ("verb", "greet")),
                Ev(("actor", "George"), ("target", "Biff"), ("verb", "insult")),
            };
            var view = world.Evaluate(Session.OfEvents(events));
            Assert.Equal(2, view.GetOne("Biff", "insultCount").AsInt);
        }

        private static Event Ev(params (string, string)[] fields)
        {
            var dict = new System.Collections.Generic.Dictionary<string, TinyWorldLang.Values.Value>();
            foreach (var (k, v) in fields)
                dict[k] = k is "actor" or "target" ? TinyWorldLang.Values.Value.Entity(v) : TinyWorldLang.Values.Value.String(v);
            return new Event(dict);
        }
    }
}

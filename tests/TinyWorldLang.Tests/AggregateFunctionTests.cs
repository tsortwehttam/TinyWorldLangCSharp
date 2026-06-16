using System;
using System.Collections.Generic;
using TinyWorldLang;
using TinyWorldLang.Eval;
using TinyWorldLang.Values;
using Xunit;

namespace TinyWorldLang.Tests
{
    /// <summary>
    /// The list-aggregation family added for host-driven simulations:
    /// <c>sum/min/max/argmin/argmax</c> and the <c>reduce</c> fold.
    /// </summary>
    public class AggregateFunctionTests
    {
        [Fact]
        public void Sum_OfIntsAndDoubles_AndEmpty()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X n 1; X n 2; X n 3;
                T total  = sum(self.n);
                T none   = sum(self.missing);          // empty set -> 0
                T mapped = sum([1.5, 2.5]);
            ");
            var view = world.Evaluate();
            Assert.Equal(6, view.Entity("X")["total"].AsInt);
            Assert.Equal(0, view.Entity("X")["none"].AsInt);
            Assert.Equal(4.0, view.Entity("X")["mapped"].AsDouble);
        }

        [Fact]
        public void Sum_MixingIntAndDouble_IsError()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T bad = sum([1, 2.0]);
            ");
            Assert.ThrowsAny<TwlException>(() => world.Evaluate().Entity("X")["bad"].One());
        }

        [Fact]
        public void MinMax_OverList_AndEmptyIsNull()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X n 3; X n 7; X n 1;
                T hi = max(self.n);
                T lo = min(self.n);
                T empty = max(self.missing);   // empty -> null -> empty relation
            ");
            var view = world.Evaluate();
            Assert.Equal(7, view.Entity("X")["hi"].AsInt);
            Assert.Equal(1, view.Entity("X")["lo"].AsInt);
            Assert.True(view.Entity("X")["empty"].IsEmpty);
        }

        [Fact]
        public void ArgMax_And_ArgMin_OverSessionEvents_ByKey()
        {
            // The "most recent / earliest event" pattern: pick the event with the
            // greatest/least seq, then read a field off it.
            var world = TwlWorld.Load(@"
                Person extends Entity;
                Desk instanceof Person;
                Person latest   = argmax(session.events, e, e.seq).actor;
                Person earliest = argmin(session.events, e, e.seq).actor;
            ");
            var events = new[]
            {
                Ev(("actor", Value.Entity("Bob")),   ("seq", Value.Int(0))),
                Ev(("actor", Value.Entity("Carol")), ("seq", Value.Int(2))),
                Ev(("actor", Value.Entity("Alice")), ("seq", Value.Int(1))),
            };
            var view = world.Evaluate(Session.OfEvents(events));
            Assert.Equal("Carol", view.Entity("Desk")["latest"].AsEntity);   // seq 2
            Assert.Equal("Bob", view.Entity("Desk")["earliest"].AsEntity);  // seq 0
        }

        [Fact]
        public void ArgMax_EmptyList_IsNull()
        {
            var world = TwlWorld.Load(@"
                Person extends Entity; Desk instanceof Person;
                Person latest = argmax(session.events, e, e.seq);
            ");
            Assert.True(world.Evaluate(Session.Empty).Entity("Desk")["latest"].IsEmpty);
        }

        [Fact]
        public void Reduce_FoldsToASum_AndEmptyReturnsInitial()
        {
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X n 1; X n 2; X n 3; X n 4;
                T total = self.n.reduce(acc, x, 0, acc + x);
                T init  = self.missing.reduce(acc, x, 99, acc + x);
            ");
            var view = world.Evaluate();
            Assert.Equal(10, view.Entity("X")["total"].AsInt);
            Assert.Equal(99, view.Entity("X")["init"].AsInt);
        }

        [Fact]
        public void Reduce_WithListAccumulator_CarriesMultipleRunningValues()
        {
            // acc = [sum, count]; demonstrates a fold tracking more than one value.
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X n 1; X n 2; X n 3;
                T pair = self.n.reduce(acc, x, [0, 0], [acc[0] + x, acc[1] + 1]) == [6, 3];
            ");
            Assert.True(world.Evaluate().Entity("X")["pair"].AsBool);
        }

        [Fact]
        public void Reduce_ModelsACounterReset()
        {
            // A running total that resets to 0 after every 3rd element — the kind of
            // order-sensitive accumulation a plain weighted-count cannot express.
            // acc = [runningTotal, index]; reset runningTotal when (index+1) % 3 == 0.
            // The list accumulator is kept INSIDE the expression and indexed there: a
            // computed rule flattens a stored list into its set, so we never store it.
            // (A list literal is the source here because stored facts dedup to a set.)
            var world = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T running = [10, 10, 10, 10, 10].reduce(acc, x, [0, 0],
                    [ ((acc[1] + 1) % 3 == 0) ? 0 : (acc[0] + x), acc[1] + 1 ])[0];
            ");
            var view = world.Evaluate();
            // steps: 10, 20, reset->0, 10, 20
            Assert.Equal(20, view.Entity("X")["running"].AsInt);
        }

        private static Event Ev(params (string, Value)[] fields)
        {
            var dict = new Dictionary<string, Value>();
            foreach (var (k, v) in fields) dict[k] = v;
            return new Event(dict);
        }
    }
}

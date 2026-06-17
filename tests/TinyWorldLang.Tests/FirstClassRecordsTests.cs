using TinyWorldLang;
using TinyWorldLang.Parsing;
using TinyWorldLang.Values;
using Xunit;

namespace TinyWorldLang.Tests
{
    /// <summary>Feature 2: declarable, constructible, storable records.</summary>
    public class FirstClassRecordsTests
    {
        // -------- CEL record literals --------

        [Fact]
        public void RecordLiteral_BuildsAndReadsFields()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T rec = { name: ""Bob"", age: 30 };
                T recName = one(self.rec).name;
            ").Evaluate();

            var rec = view.Entity("X")["rec"].One();
            Assert.Equal(ValueKind.Record, rec.Kind);
            Assert.Equal("Bob", rec.AsRecord["name"].AsString);
            Assert.Equal(30, rec.AsRecord["age"].AsInt);
            Assert.Equal("Bob", view.Entity("X")["recName"].Text);
        }

        [Fact]
        public void RecordLiteral_DuplicateKey_IsEvalError()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T bad = { a: 1, a: 2 };
            ").Evaluate();
            var ex = Assert.Throws<TwlEvalException>(() => view.Entity("X")["bad"].One());
            Assert.Contains("duplicate field", ex.Message);
        }

        // -------- dedup + ordering in a relation --------

        [Fact]
        public void EqualRecords_CollapseInARelation()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T dups = [{ a: 1, b: 2 }, { b: 2, a: 1 }];
            ").Evaluate();
            // Same key set + equal values, regardless of field order -> one member.
            Assert.Equal(1, view.Entity("X")["dups"].Count);
        }

        [Fact]
        public void DifferentRecords_StayDistinct()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T diffs = [{ a: 1 }, { a: 2 }];
            ").Evaluate();
            Assert.Equal(2, view.Entity("X")["diffs"].Count);
        }

        [Fact]
        public void RecordValuedRelation_HasDeterministicOrder()
        {
            // Records ordered by value at the shared key, independent of input order.
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T sorted = [{ k: 2 }, { k: 1 }];
                T firstK = one(self.sorted).k;
            ").Evaluate();
            Assert.Equal(1, view.Entity("X")["firstK"].AsInt);
        }

        // -------- stored records, functions, parameter args --------

        [Fact]
        public void StoredRecordFact_RoundTrips()
        {
            var view = TwlWorld.Load(@"
                Person extends Entity;
                Kiss instanceof Action;
                Kiss signature { subject: Person, object: Person, example: ""hi"" };
            ").Evaluate();

            var sig = view.Entity("Kiss")["signature"].One();
            Assert.Equal(ValueKind.Record, sig.Kind);
            Assert.Equal("Person", sig.AsRecord["subject"].AsEntity);
            Assert.Equal("hi", sig.AsRecord["example"].AsString);
        }

        [Fact]
        public void Record_ReturnedFromAFunction()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                fun mk(n) = { val: n };
                T fromFn = one(self.fromFnHolder);
                T fromFnHolder = mk(5);
                T value = one(self.fromFnHolder).val;
            ").Evaluate();
            Assert.Equal(5, view.Entity("X")["value"].AsInt);
        }

        [Fact]
        public void Record_AsAParameterRuleArgument()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T pick(r) = r.field;
                T usePick = one(self.pick({ field: 99 }));
            ").Evaluate();
            Assert.Equal(99, view.Entity("X")["usePick"].AsInt);
        }

        // -------- parser --------

        [Fact]
        public void StoredRecord_Parses()
        {
            var p = TwlParser.Parse("Kiss signature { subject: Person, example: \"x\" };");
            var fact = Assert.IsType<StoredFact>(p.Statements[0]);
            Assert.Equal(ValueKind.Record, fact.Value.Kind);
            Assert.Equal("Person", fact.Value.AsRecord["subject"].AsEntity);
            Assert.Equal("x", fact.Value.AsRecord["example"].AsString);
        }

        [Fact]
        public void StoredRecord_DuplicateKey_IsLoadError()
        {
            Assert.Throws<TwlLoadException>(() => TwlParser.Parse("X r { a: 1, a: 2 };"));
        }

        // -------- value-level equality / hashing / ordering --------

        [Fact]
        public void RecordEquality_IsOrderIndependent_AndHashAgrees()
        {
            var a = Value.Record(new System.Collections.Generic.Dictionary<string, Value>
            { ["x"] = Value.Int(1), ["y"] = Value.String("z") });
            var b = Value.Record(new System.Collections.Generic.Dictionary<string, Value>
            { ["y"] = Value.String("z"), ["x"] = Value.Int(1) });

            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void CanonicalComparer_OrdersRecordsTotally()
        {
            Value R(params (string, Value)[] fs)
            {
                var d = new System.Collections.Generic.Dictionary<string, Value>();
                foreach (var (k, v) in fs) d[k] = v;
                return Value.Record(d);
            }
            var shorter = R(("a", Value.Int(1)));
            var longer = R(("a", Value.Int(1)), ("b", Value.Int(2)));
            // Proper key-prefix sorts before the longer record.
            Assert.True(CanonicalComparer.Instance.Compare(shorter, longer) < 0);
            Assert.True(CanonicalComparer.Instance.Compare(longer, shorter) > 0);
            // Records sort after entities (distinct kind rank).
            Assert.True(CanonicalComparer.Instance.Compare(Value.Entity("Z"), shorter) < 0);
        }
    }
}

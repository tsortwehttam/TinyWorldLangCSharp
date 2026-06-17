using TinyWorldLang;
using TinyWorldLang.Parsing;
using Xunit;

namespace TinyWorldLang.Tests
{
    /// <summary>Feature 1: module functions (<c>fun</c>) and parameter rules (<c>T rel(p) = …</c>).</summary>
    public class FunctionsAndParameterRulesTests
    {
        // -------- functions --------

        [Fact]
        public void Function_ReturnsScalar_AndIsReusable()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X a 6; X b 4;
                fun add(p, q) = p + q;
                T total = add(one(self.a), one(self.b));
            ").Evaluate();
            Assert.Equal(10, view.Entity("X")["total"].AsInt);
        }

        [Fact]
        public void Function_CanReturnAList_WhichTheRuleFlattens()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X a 6; X b 4;
                fun pair(p, q) = [p, q];
                T pairRel = pair(one(self.a), one(self.b));
            ").Evaluate();
            Assert.Equal(2, view.Entity("X")["pairRel"].Count);
        }

        [Fact]
        public void Function_RecursesWithinDepth()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                fun countdown(n) = n <= 0 ? 0 : countdown(n - 1);
                T zero = countdown(10);
            ").Evaluate();
            Assert.Equal(0, view.Entity("X")["zero"].AsInt);
        }

        [Fact]
        public void Function_OverRecursing_FailsWithCallDepthError()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                fun forever(n) = forever(n + 1);
                T boom = forever(0);
            ").Evaluate();
            var ex = Assert.Throws<TwlEvalException>(() => view.Entity("X")["boom"].One());
            Assert.Contains("call depth", ex.Message);
        }

        [Fact]
        public void Function_UnknownName_IsEvalError()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T r = nope(1);
            ").Evaluate();
            var ex = Assert.Throws<TwlEvalException>(() => view.Entity("X")["r"].One());
            Assert.Contains("unknown function 'nope'", ex.Message);
        }

        // -------- parameter rules --------

        [Fact]
        public void ParameterRule_DispatchesOnMostSpecificType()
        {
            var view = TwlWorld.Load(@"
                Animal extends Entity; Dog extends Animal;
                Rex instanceof Dog;
                Animal describe(x) = ""animal sees "" + string(x);
                Dog describe(x) = ""dog sees "" + string(x);
                Dog report = self.describe(1);
            ").Evaluate();
            Assert.Equal("dog sees 1", view.Entity("Rex")["report"].Text);
        }

        [Fact]
        public void ParameterRule_OverloadsByArity()
        {
            var view = TwlWorld.Load(@"
                Animal extends Entity; Rex instanceof Animal;
                Animal f(x) = ""one"";
                Animal f(x, y) = ""two"";
                Animal callOne = self.f(1);
                Animal callTwo = self.f(1, 2);
            ").Evaluate();
            Assert.Equal("one", view.Entity("Rex")["callOne"].Text);
            Assert.Equal("two", view.Entity("Rex")["callTwo"].Text);
        }

        [Fact]
        public void ParameterRule_ParameterShadowsSelf()
        {
            // The parameter named `self` shadows the receiver: the body returns the
            // argument (42), not the entity Rex — proven by AsInt succeeding.
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                T shadow(self) = self;
                T useShadow = one(self.shadow(42));
            ").Evaluate();
            Assert.Equal(42, view.Entity("X")["useShadow"].AsInt);
        }

        [Fact]
        public void ParameterRule_SameArgs_AreDeterministicWithinAView()
        {
            var view = TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X v 7;
                fun dbl(n) = n * 2;
                T a = dbl(one(self.v));
                T b = dbl(one(self.v));
            ").Evaluate();
            Assert.Equal(14, view.Entity("X")["a"].AsInt);
            Assert.Equal(view.Entity("X")["a"].AsInt, view.Entity("X")["b"].AsInt);
        }

        [Fact]
        public void ParameterRule_NoApplicableArity_IsEvalError()
        {
            var view = TwlWorld.Load(@"
                Animal extends Entity; Rex instanceof Animal;
                Animal f(x) = ""one"";
                Animal bad = self.f(1, 2);
            ").Evaluate();
            var ex = Assert.Throws<TwlEvalException>(() => view.Entity("Rex")["bad"].One());
            Assert.Contains("no rule 'f'", ex.Message);
        }

        // -------- load-time validation --------

        [Fact]
        public void Function_ClashingWithRelationName_IsLoadError()
        {
            var ex = Assert.Throws<TwlLoadException>(() => TwlWorld.Load(@"
                T extends Entity; X instanceof T;
                X trusted 1;
                fun trusted(x) = x;
            "));
            Assert.Contains("clashes", ex.Message);
        }

        [Fact]
        public void Function_DeclaredTwiceWithSameArity_IsLoadError()
        {
            Assert.Throws<TwlLoadException>(() => TwlWorld.Load(@"
                fun f(x) = x;
                fun f(y) = y;
            "));
        }

        [Fact]
        public void DuplicateParameterName_IsLoadError()
        {
            Assert.Throws<TwlLoadException>(() => TwlWorld.Load("fun f(x, x) = x;"));
        }

        // -------- parsing --------

        [Fact]
        public void Parses_FunctionDecl_AndParameterRule()
        {
            var parsed = TwlParser.Parse(@"
                fun add(a, b) = a + b;
                T rel(x) = x + 1;
                T plain = 1;
            ");
            var fn = Assert.IsType<FunctionDecl>(parsed.Statements[0]);
            Assert.Equal("add", fn.Name);
            Assert.Equal(new[] { "a", "b" }, fn.Parameters);
            Assert.Equal("a + b", fn.Expression);

            var rule = Assert.IsType<ComputedFact>(parsed.Statements[1]);
            Assert.Equal(new[] { "x" }, rule.Parameters);

            var plain = Assert.IsType<ComputedFact>(parsed.Statements[2]);
            Assert.Empty(plain.Parameters);
        }
    }
}

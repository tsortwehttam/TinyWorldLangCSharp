using TinyWorldLang.Cel;
using TinyWorldLang.Values;
using Xunit;

namespace TinyWorldLang.Conformance
{
    /// <summary>
    /// SKELETON conformance harness. These cases mirror sections of Google's
    /// language-neutral cel-spec simple suite
    /// (https://github.com/google/cel-spec/tree/master/tests/simple/testdata) — the
    /// canonical definition of "valid CEL expressions mean the same thing". The point
    /// is that our subset's correctness is checkable against an external standard, not
    /// our own judgment.
    ///
    /// GROWTH PATH (tracked, not yet done): vendor the .textproto files for the
    /// subset the spec declares (basic, comparisons, logic, lists, macros, string,
    /// math_ext), add a minimal textproto reader, and drive [Theory] cases from them
    /// so "did we miss something?" is answered by the corpus. Sections TWL excludes
    /// (e.g. protobuf messages, maps, timestamps beyond `now`) are intentionally out
    /// of scope and should be skipped, the way cel-spec allows.
    /// </summary>
    public class CelConformanceTests
    {
        private static Value Eval(string expr) =>
            TreeWalkingCelEvaluator.Instance.Compile(expr).Evaluate(new StandaloneCelContext());

        public static TheoryData<string, Value> Cases() => new TheoryData<string, Value>
        {
            // basic / literals
            { "1", Value.Int(1) },
            { "1.5", Value.Double(1.5) },
            { "true", Value.Bool(true) },
            { "\"abc\"", Value.String("abc") },

            // arithmetic
            { "2 + 3 * 4", Value.Int(14) },
            { "(2 + 3) * 4", Value.Int(20) },
            { "7 % 3", Value.Int(1) },
            { "1.0 / 4.0", Value.Double(0.25) },

            // comparisons (int/double compare by value)
            { "1 == 1.0", Value.Bool(true) },
            { "2 < 3", Value.Bool(true) },
            { "\"a\" < \"b\"", Value.Bool(true) },

            // logic (short-circuit)
            { "true && false", Value.Bool(false) },
            { "false || true", Value.Bool(true) },
            { "!false", Value.Bool(true) },

            // conditional
            { "2 > 1 ? 10 : 20", Value.Int(10) },

            // membership + lists
            { "3 in [1, 2, 3]", Value.Bool(true) },
            { "4 in [1, 2, 3]", Value.Bool(false) },
            { "size([1, 2, 3])", Value.Int(3) },

            // macros
            { "[1, 2, 3].map(x, x * 2) == [2, 4, 6]", Value.Bool(true) },
            { "[1, 2, 3].filter(x, x > 1) == [2, 3]", Value.Bool(true) },
            { "[1, 2, 3].exists(x, x == 2)", Value.Bool(true) },
            { "[1, 2, 3].all(x, x > 0)", Value.Bool(true) },

            // string concat
            { "\"a\" + \"bc\" == \"ab\" + \"c\"", Value.Bool(true) },

            // math helpers
            { "math.greatest(3, 7)", Value.Int(7) },
            { "math.least(3, 7)", Value.Int(3) },
            { "int(math.floor(3.9))", Value.Int(3) },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void MatchesExpected(string expr, Value expected)
        {
            Assert.True(Eval(expr).Equals(expected), $"`{expr}` => {Eval(expr)} (expected {expected})");
        }
    }
}

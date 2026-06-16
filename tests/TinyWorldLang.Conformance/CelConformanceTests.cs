using System;
using System.IO;
using TinyWorldLang.Cel;
using TinyWorldLang.Values;
using Xunit;

namespace TinyWorldLang.Conformance
{
    /// <summary>
    /// Data-driven conformance harness for the CEL subset TWL claims. The testdata
    /// keeps Google's simple-suite textproto shape, curated to the scalar/list
    /// expression forms this engine currently supports.
    /// </summary>
    public class CelConformanceTests
    {
        private static Value Eval(string expr) =>
            TreeWalkingCelEvaluator.Instance.Compile(expr).Evaluate(new StandaloneCelContext());

        public static TheoryData<string, string, Value> Cases()
        {
            var data = new TheoryData<string, string, Value>();
            string dir = Path.Combine(AppContext.BaseDirectory, "testdata");
            foreach (var c in CelSpecTextProtoReader.LoadDirectory(dir))
                data.Add(c.Id, c.Expression, c.Expected);
            return data;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void MatchesExpected(string id, string expr, Value expected)
        {
            var actual = Eval(expr);
            Assert.True(actual.Equals(expected), $"{id}: `{expr}` => {actual} (expected {expected})");
        }
    }
}

using System;

namespace TinyWorldLang
{
    /// <summary>Base type for all TWL errors (load-time and evaluation-time).</summary>
    public class TwlException : Exception
    {
        public TwlException(string message) : base(message) { }
        public TwlException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>A syntax or load-time error in the world source (parse, cycle, ambiguous rule, …).</summary>
    public sealed class TwlLoadException : TwlException
    {
        /// <summary>1-based line in the source, or 0 if not applicable.</summary>
        public int Line { get; }

        public TwlLoadException(string message, int line = 0)
            : base(line > 0 ? $"line {line}: {message}" : message)
        {
            Line = line;
        }
    }

    /// <summary>
    /// A per-entity evaluation error (missing value used in arithmetic, null member
    /// access, mixing int and double, …). Raised only when the offending rule is read.
    /// </summary>
    public sealed class TwlEvalException : TwlException
    {
        public TwlEvalException(string message) : base(message) { }
        public TwlEvalException(string message, Exception inner) : base(message, inner) { }
    }
}

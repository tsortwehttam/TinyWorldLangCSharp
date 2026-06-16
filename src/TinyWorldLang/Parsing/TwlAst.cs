using System.Collections.Generic;
using TinyWorldLang.Values;

namespace TinyWorldLang.Parsing
{
    /// <summary>A parsed TWL statement: either a stored fact or a computed rule.</summary>
    public abstract class Statement
    {
        public int Line { get; }
        protected Statement(int line) => Line = line;
    }

    /// <summary>
    /// A stored fact: <c>SUBJECT RELATION VALUE;</c>. Records one value directly on
    /// one subject entity.
    /// </summary>
    public sealed class StoredFact : Statement
    {
        public string Subject { get; }
        public string Relation { get; }
        public Value Value { get; }

        public StoredFact(string subject, string relation, Value value, int line) : base(line)
        {
            Subject = subject;
            Relation = relation;
            Value = value;
        }
    }

    /// <summary>
    /// A computed rule: <c>TYPE RELATION = EXPR;</c>. Applies to every instance of
    /// <see cref="Type"/> (and its subtypes). The CEL source is kept verbatim; the
    /// configured <c>ICelEvaluator</c> compiles it lazily.
    /// </summary>
    public sealed class ComputedFact : Statement
    {
        public string Type { get; }
        public string Relation { get; }
        public string Expression { get; }

        public ComputedFact(string type, string relation, string expression, int line) : base(line)
        {
            Type = type;
            Relation = relation;
            Expression = expression;
        }
    }

    /// <summary>The three built-in relations get their own statement kind for clarity.</summary>
    public enum BuiltinRelation { InstanceOf, Extends, SameAs }

    /// <summary>A built-in structural fact: <c>X instanceof T;</c>, <c>A extends B;</c>, <c>A sameas B;</c>.</summary>
    public sealed class StructuralFact : Statement
    {
        public string Left { get; }
        public BuiltinRelation Relation { get; }
        public string Right { get; }

        public StructuralFact(string left, BuiltinRelation relation, string right, int line) : base(line)
        {
            Left = left;
            Relation = relation;
            Right = right;
        }
    }

    /// <summary>The result of parsing a whole world source.</summary>
    public sealed class ParsedWorld
    {
        public IReadOnlyList<Statement> Statements { get; }
        public ParsedWorld(IReadOnlyList<Statement> statements) => Statements = statements;
    }
}

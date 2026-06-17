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
    /// A computed rule: <c>TYPE RELATION = EXPR;</c>, or a parameter rule
    /// <c>TYPE RELATION(p1, p2) = EXPR;</c>. Applies to every instance of
    /// <see cref="Type"/> (and its subtypes). The CEL source is kept verbatim; the
    /// configured <c>ICelEvaluator</c> compiles it lazily. A 0-arity rule (empty
    /// <see cref="Parameters"/>) is read as <c>e.relation</c>; a parameter rule is
    /// called as <c>e.relation(a, b)</c> with the arguments bound to the parameters.
    /// </summary>
    public sealed class ComputedFact : Statement
    {
        private static readonly IReadOnlyList<string> NoParameters = new string[0];

        public string Type { get; }
        public string Relation { get; }
        public string Expression { get; }

        /// <summary>The declared parameter names, or empty for a plain 0-arity rule.</summary>
        public IReadOnlyList<string> Parameters { get; }

        public ComputedFact(string type, string relation, string expression, int line,
            IReadOnlyList<string>? parameters = null) : base(line)
        {
            Type = type;
            Relation = relation;
            Expression = expression;
            Parameters = parameters ?? NoParameters;
        }
    }

    /// <summary>
    /// A module-level function: <c>fun NAME(p1, p2) = EXPR;</c>. Unlike a parameter
    /// rule it has no <c>self</c> and no type dispatch; it is called as
    /// <c>NAME(a, b)</c> and returns its value unchanged (no set coercion), so it may
    /// return a scalar, a list, or a record.
    /// </summary>
    public sealed class FunctionDecl : Statement
    {
        public string Name { get; }
        public IReadOnlyList<string> Parameters { get; }
        public string Expression { get; }

        public FunctionDecl(string name, IReadOnlyList<string> parameters, string expression, int line) : base(line)
        {
            Name = name;
            Parameters = parameters;
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

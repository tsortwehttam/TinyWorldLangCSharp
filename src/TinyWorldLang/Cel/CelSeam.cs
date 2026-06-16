using System;
using System.Collections.Generic;
using TinyWorldLang.Eval;
using TinyWorldLang.Values;

namespace TinyWorldLang.Cel
{
    /// <summary>
    /// The pluggable CEL backend. The core ships an AOT-safe tree-walking
    /// implementation (<see cref="TreeWalkingCelEvaluator"/>); a host on a JIT
    /// platform may inject a different one — for example a conformance-tested
    /// third-party engine — without the rest of TWL changing.
    /// </summary>
    public interface ICelEvaluator
    {
        /// <summary>Compile source into a reusable program. Throws <see cref="CelException"/> on syntax errors.</summary>
        ICelProgram Compile(string source);
    }

    /// <summary>A compiled CEL expression, evaluated repeatedly against different contexts.</summary>
    public interface ICelProgram
    {
        Value Evaluate(ICelContext context);
    }

    /// <summary>
    /// Everything a CEL program may reach into the world for. The evaluator owns
    /// macro-variable scoping itself; this only exposes <c>self</c>, <c>now</c>, and
    /// the world/session primitives.
    /// </summary>
    public interface ICelContext
    {
        Value Self { get; }
        DateTimeOffset Now { get; }

        /// <summary>Read relation <paramref name="relation"/> on <paramref name="entity"/> (the <c>e.rel</c> form).</summary>
        ValueSet Relation(Value entity, string relation);

        /// <summary>All entities (as entity values) that are instances of <paramref name="type"/>.</summary>
        IReadOnlyList<Value> Instances(string type);

        /// <summary>A stable draw in [0,1) for <paramref name="key"/> under the evaluation seed.</summary>
        double Rand(Value key);

        /// <summary>A named session stream, or empty if the host did not supply it.</summary>
        IReadOnlyList<Event> SessionStream(string name);

        /// <summary>Resolve a bare identifier to its canonical entity value.</summary>
        Value CanonicalEntity(string name);
    }

    /// <summary>A CEL syntax or evaluation error.</summary>
    public sealed class CelException : TwlException
    {
        public CelException(string message) : base(message) { }
    }
}

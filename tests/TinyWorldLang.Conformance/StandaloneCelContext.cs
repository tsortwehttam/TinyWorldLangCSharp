using System;
using System.Collections.Generic;
using TinyWorldLang.Cel;
using TinyWorldLang.Eval;
using TinyWorldLang.Values;

namespace TinyWorldLang.Conformance
{
    /// <summary>
    /// A world-free <see cref="ICelContext"/> for evaluating pure CEL expressions
    /// (no relation reads, no instances, no session). Used by the conformance runner
    /// to exercise the language core in isolation, the way cel-spec's simple suite
    /// does.
    /// </summary>
    internal sealed class StandaloneCelContext : ICelContext
    {
        public Value Self => Value.Null;
        public DateTimeOffset Now { get; } = DateTimeOffset.UnixEpoch;

        public ValueSet Relation(Value entity, string relation) =>
            throw new CelException("relation access is not available in standalone CEL conformance");

        public IReadOnlyList<Value> Instances(string type) => Array.Empty<Value>();

        public double Rand(Value key) => 0.0;

        public IReadOnlyList<Event> SessionStream(string name) => Array.Empty<Event>();

        public Value CanonicalEntity(string name) => Value.Entity(name);

        public bool TryGetLocal(string name, out Value value)
        {
            value = Value.Null;
            return false;
        }

        public ValueSet CallRule(Value receiver, string name, IReadOnlyList<Value> args) =>
            throw new CelException("parameter rules are not available in standalone CEL conformance");

        public Value CallFunction(string name, IReadOnlyList<Value> args) =>
            throw new CelException($"unknown function '{name}(...)'");
    }
}

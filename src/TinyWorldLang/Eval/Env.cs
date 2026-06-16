using System;
using System.Collections.Generic;
using TinyWorldLang.Values;

namespace TinyWorldLang.Eval
{
    /// <summary>
    /// The per-evaluation environment: <c>now</c>, the <c>rand</c> seed, a turn
    /// counter, and any extra host bindings. Ephemeral — supplied per query, gone
    /// after the call. The seed cannot be read or set from inside the world.
    /// </summary>
    public sealed class Env
    {
        public DateTimeOffset Now { get; }
        public ulong Seed { get; }
        public int Turn { get; }

        public Env(DateTimeOffset now, ulong seed = 0, int turn = 0)
        {
            Now = now.ToUniversalTime(); // frozen, UTC, for the whole evaluation
            Seed = seed;
            Turn = turn;
        }
    }

    /// <summary>
    /// One session event: a record of host-defined fields, each a scalar or entity
    /// reference. TWL does not define the shape — the host does, per game.
    /// </summary>
    public sealed class Event
    {
        private readonly IReadOnlyDictionary<string, Value> _fields;

        public Event(IReadOnlyDictionary<string, Value> fields) =>
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));

        public Value Field(string name) =>
            _fields.TryGetValue(name, out var v) ? v : Value.Null;

        public bool Has(string name) => _fields.ContainsKey(name);

        /// <summary>The event's fields, exposed to CEL as a record value.</summary>
        public IReadOnlyDictionary<string, Value> Fields => _fields;
    }

    /// <summary>
    /// The append-only session stream, exposed to CEL under the <c>session</c>
    /// namespace as named lists. The host owns and persists it; TWL only reads it.
    /// </summary>
    public sealed class Session
    {
        public static readonly Session Empty = new Session(new Dictionary<string, IReadOnlyList<Event>>());

        private readonly IReadOnlyDictionary<string, IReadOnlyList<Event>> _streams;

        public Session(IReadOnlyDictionary<string, IReadOnlyList<Event>> streams) =>
            _streams = streams ?? throw new ArgumentNullException(nameof(streams));

        /// <summary>Convenience for the common single-stream case (<c>session.events</c>).</summary>
        public static Session OfEvents(IReadOnlyList<Event> events) =>
            new Session(new Dictionary<string, IReadOnlyList<Event>> { ["events"] = events });

        public IReadOnlyList<Event> Stream(string name) =>
            _streams.TryGetValue(name, out var s) ? s : Array.Empty<Event>();

        public bool HasStream(string name) => _streams.ContainsKey(name);
    }
}

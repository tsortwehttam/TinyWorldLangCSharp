using System;
using TinyWorldLang.Cel;
using TinyWorldLang.Eval;
using TinyWorldLang.Model;

namespace TinyWorldLang
{
    /// <summary>
    /// The public entry point. Load a world once (it never changes), then create a
    /// <see cref="WorldView"/> per query batch with a session and environment.
    /// </summary>
    /// <example>
    /// <code>
    /// var world = TwlWorld.Load(source);            // parse + validate once, cache it
    /// var view  = world.Evaluate(session, env);     // (world, session, env) -> values
    ///
    /// // One general way to query: entity handles + standard LINQ.
    /// var threats = view.Entities("Person")
    ///     .Where(p => p["age"].AsInt > 40)
    ///     .Select(p => new { p.Name, Bio = p["persona"].Text });
    /// </code>
    /// </example>
    public sealed class TwlWorld
    {
        private readonly World _world;
        private readonly ICelEvaluator _cel;
        private readonly Func<string, string>? _escaper;

        private TwlWorld(World world, ICelEvaluator cel, Func<string, string>? escaper)
        {
            _world = world;
            _cel = cel;
            _escaper = escaper;
        }

        /// <summary>
        /// Parse and validate world source. <paramref name="cel"/> defaults to the
        /// AOT-safe tree-walking evaluator; a JIT host may inject another backend.
        /// <paramref name="escaper"/> is applied to <c>{{ }}</c> template output
        /// (default: none — plain text / JSON target).
        /// </summary>
        public static TwlWorld Load(
            string source,
            ICelEvaluator? cel = null,
            Func<string, string>? escaper = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var world = World.Load(source);
            return new TwlWorld(world, cel ?? TreeWalkingCelEvaluator.Instance, escaper);
        }

        /// <summary>Create a query view for one evaluation of this world.</summary>
        public WorldView Evaluate(Session? session = null, Env? env = null) =>
            new WorldView(_world, session ?? Session.Empty, env ?? new Env(DateTimeOffset.UtcNow), _cel, _escaper);

        /// <summary>The underlying loaded world (immutable).</summary>
        public World World => _world;
    }
}

# Tiny World Language (TWL)

**A small language for building simulations and emergent games — and its C# engine.**

TWL is a *source of truth* for a simulated world: what the world consists of at any given
moment. It is **not** a renderer, a game engine, or a content pack. A TWL world is a set of
**facts**; each fact links a subject entity to a value through a named relation. Some facts
are written down directly; others are *computed* from the rest of the world with
[CEL](https://cel.dev/) expressions.

```
Person extends Entity;
Family extends Entity;

Person age      = now.getFullYear() - one(self.bornYear);
Person lastName = one(one(self.family).name);

McFlyFam instanceof Family; McFlyFam name "McFly";

Marty instanceof Person; Marty firstName "Marty";
Marty bornYear 1968; Marty family McFlyFam;
```

This repository is the **C# implementation** plus an in-process LINQ query API for C#
consumers (Unity, MonoGame, console, desktop). The engine is a pure function —
`eval(world, session, env) → values` — so the same inputs always produce the same values,
and a host can stay stateless. Jump to [the language](#the-language) for the authoring
reference, or [the C# engine](#c-engine--query-api) to embed it.

---

## Contents

- [Overview](#overview)
- [The language](#the-language)
  - [Statements](#statements) · [Values](#values) · [Names](#names) ·
    [Entities and relations](#entities-and-relations) · [Order within a set](#order-within-a-set) ·
    [Types and identity](#types-and-identity) · [Computed facts](#computed-facts) ·
    [Evaluation model](#evaluation-model) · [Events](#events) · [Randomness](#randomness) ·
    [Tiebreaks](#tiebreaks) · [Templates](#templates) · [Example](#example)
- [C# engine & query API](#c-engine--query-api)
- [Design & platform notes](#design--platform-notes)
- [Project layout](#project-layout)
- [Building & testing](#building--testing)
- [Status & roadmap](#status--roadmap)
- [License](#license)

---

## Overview

A TWL world is a list of **facts**. A fact is a triple: a *subject* entity, a *relation*,
and a *value* — `Marty bornYear 1968;`. That is exactly an RDF subject-predicate-object
triple, so a TWL world is a small **triple store** / graph.

There are two kinds of fact:

- **Stored facts** record something directly: `Marty bornYear 1968;`
- **Computed facts** give a rule for a relation, applied to every entity of a type:
  `Person age = now.getFullYear() - one(self.bornYear);`

Every relation holds a **set** of values, and reading a relation that was never mentioned is
simply the empty set — never an error. The engine evaluates lazily and is a pure function of
three inputs:

| Input | What it is | Who owns it |
|---|---|---|
| **World** | the authored cartridge: types, rules, static facts | the author; immutable for the whole game |
| **Session** | an append-only stream of events accumulated during play | the host; TWL only reads it |
| **Env** | the per-evaluation environment: `now`, the `rand` seed, a turn counter | the host; ephemeral, per call |

Because TWL holds no state, the host owns everything that changes: it appends events to the
session and asks TWL what they imply. To resume a game you reload the world and session and
evaluate — there is nothing else to restore.

---

## The language

> The authoring reference. Worlds are written in this surface syntax; the C# engine below reads
> the result.

### Statements

A world is a list of statements, each ending in `;`. There are two kinds:

```
// line comment
/* block comment */
SUBJECT RELATION VALUE; // a stored fact
TYPE RELATION = EXPR; // a computed fact
```

- A stored fact records something directly: `Marty bornYear 1968;`
- A computed fact gives a rule for a relation, applied to every entity of a type: `Person age = now.getFullYear() - one(self.bornYear);`

### Values

A `VALUE` is one of:

- entity — a bare id: `Mac`, `McFlyFam`
- number — `123`, `1.5`. A literal with no decimal point is an integer (`123`, `1968`); one with a decimal point is a double (`1.5`, `1.0`). The type is syntactic and fixed: `1968` is always an integer, `1968.0` always a double. Arithmetic never mixes the two (see [Computed facts](#computed-facts))
- boolean — `true`, `false`
- string — `"..."` — a Mustache template (see [Templates](#templates)), rendered against the owning entity when read; may span multiple lines. Text with no `{{` tags renders to itself, so an ordinary value like `"Marty"` is just its literal text

There is one string type and it is always a template. Stored strings are author content.
Session text is never parsed as world source, but if a computed relation returns a session
string and that relation is rendered, it follows the same template-rendering rules as any
other string. Treat untrusted strings as untrusted output: use an escaper, avoid raw tags,
or keep player-authored text outside template-rendered relations.

A value runs from its opening `"` to the next unescaped `"`, so a `;`, newline, or `{{` inside is ordinary text and does not end the value or the statement. Backslash escapes: `\"` (literal `"`), `\{` (literal `{`, which cannot begin a tag — write `\{\{` for a literal `{{`), `\\` (literal backslash), `\n`, `\t`. A backslash before anything else is an error, so an accidental `\` is caught rather than swallowed.

`null` is never a stored value. It only ever turns up as the result of an expression — for example `one(...)` on an empty set.

### Names

An identifier is a letter or `_` followed by letters, digits, or `_`: `Marty`, `born_year`, `McFlyFam`.

The following words are reserved and cannot be used as entity names: `instanceof extends sameas true false null self now instances sortBy one rand math`.

All but `math` are TWL's own keywords and builtins. `math` is reserved because the math helpers are reached through it as a namespace (`math.greatest(a, b)`).

The other CEL names you use in expressions — the macros `map`, `filter`, `exists`, `all`, `in` and the functions `int`, `double`, `string`, `bool`, `size` — are not reserved: CEL keeps function-call names and plain identifiers separate, so an entity named `size` does not interfere with a `size(...)` call. (Still, authors should avoid naming entities after them so expressions stay readable.)

### Entities and relations

- Any id is an entity. You never declare entities — just mention them. An entity you have never mentioned still exists; every relation on it is simply the empty set.
- Every relation holds a set of values. `Marty family McFlyFam;` adds `McFlyFam` to Marty's `family` set; another `Marty family ... ;` adds more.
- Reading a relation always gives a set. A relation with nothing in it is the empty set `[]`, and reading it is never an error.
- Sets are unordered and deduplicated. Stating the same fact twice changes nothing.
- To read a single value, use `one(r)`: it gives the relation's single value, or `null` when the set is empty — for example `one(self.bornYear)`. To supply a default instead of `null`, pass it as a second argument: `one(self.bornYear, 1900)`. (Indexing a set directly, like `self.bornYear[0]`, errors on an empty set, so prefer `one`.)
  - `one` never errors on too many values. If the set holds more than one value it silently returns the canonical-first (see [Order within a set](#order-within-a-set)) — an unexpected second value, a typo, or a duplicated fact passes with no signal. Use `one` only where you expect at most one value; to enforce "exactly one", check `size(r)` yourself.

### Order within a set

A set has no order of its own, but some operations need one: `one`, `sortBy`, and rendering a multi-value relation in a template. For these, TWL always uses the same canonical order, so results are reproducible. Values are grouped by kind — booleans, then numbers, then strings, then entities — and ordered within each kind: `false` before `true`, numbers by value, strings by Unicode code point, entities by name. A string sorts by its unrendered source text, not its rendered output. So `one(s)` returns the first value in this order, and `sortBy(list, "rel")` orders `list` by each element's value for `rel`. Note code-point order is not dictionary order: every uppercase letter sorts before every lowercase one (`"Zoe"` before `"adam"`), and digits before letters — so sorting names is case-sensitive.

### Types and identity

Three built-in relations describe how the world is organized:

- `X instanceof T;` — X is a T.
- `A extends B;` — every A is also a B (subtypes).
- `A sameas B;` — A and B are the same entity.

These combine automatically. If `Marty instanceof Person` and `Person extends Entity`, then Marty is also an Entity.

`extends` must form no cycles: `A extends B; B extends A;` (or any longer loop) is a load-time error.

A `sameas` collapses two entities into one: they share all their facts, `==` treats them as equal, and the merged entity takes a single canonical name (the alphabetically-least of the names involved) — the name used for ordering. (A `sameas` loop is harmless — it just declares everything in the loop one entity — and collapses to a single entity rather than erroring.) Randomness keys on the entity's identity rather than its name, so a `sameas` merge leaves draws stable (see [Randomness](#randomness)).

### Computed facts

After `=` you write an expression in CEL (Common Expression Language). The rule runs for every entity that is an instance of the named type — including instances of its subtypes, since `instanceof` is transitive through `extends` (if `Wizard extends Person`, every `Wizard` is a `Person`) — and is evaluated only when its value is needed. Inside the expression you have:

- `self` — the current entity
- `now` — the current time (UTC), frozen for the whole evaluation so every rule sees the same instant. Its date-component methods (`getFullYear()`, `getMonth()`, …) read in UTC, so a sim keyed on local calendar dates can be off by a day near midnight or a year boundary unless the host supplies an appropriate UTC instant.
- `instances(T)` — the set of all entities that are instances of `T`, directly or through a subtype
- `sortBy(list, "rel")` — `list` ordered by the relation named `rel`. Each element is keyed by `one(element.rel)`: an element whose `rel` is empty keys as `null` and sorts before all real values; one with several values keys on its canonical-first
- `one(s)` — the single value of set `s`, or `null` when empty
- `one(s, fallback)` — the single value of set `s`, or `fallback` when empty
- `e.rel` — the set of values of relation `rel` on entity `e`
- `rand(key)` — a stable random number in `[0, 1)` for `key` (see [Randomness](#randomness))

Common expression forms: `list.map(x, expr)`, `list.filter(x, cond)`, `x in list`, `list.exists(x, cond)`, `list.all(x, cond)`, `size(list)`, `cond ? a : b`.

Math helpers are available under `math.`: `math.greatest(a, b)` (max), `math.least(a, b)` (min), `math.abs`, `math.sign`, `math.floor`, `math.ceil`, `math.round`, `math.trunc`, and `math.sqrt`. Clamp a value with `math.least(math.greatest(x, lo), hi)`.

`instances`, `sortBy`, `one`, and `rand` are TWL's own builtins; `self`, `now`, and `e.rel` are language forms. The expression forms and `math.*` helpers come from CEL itself — a host engine may register further functions of its own, so check the developer's documentation for any extras.

A rule's result becomes the relation's set. Every relation holds a set, so the expression's value is coerced to one: a list or set result contributes its elements (flattened one level — nested lists are not allowed); a single scalar (number, boolean, string, entity) becomes a one-element set; and `null` — including any `null` sitting inside a returned list — contributes nothing, so a rule that evaluates to `null` leaves the relation empty (`null` is never stored, per [Values](#values)). This is why `one()` on a rule that produced `null` gives `null` straight back: the set is empty.

Numbers are not converted automatically: do not mix integers and decimals in one arithmetic operation — `1 + 1.0` errors. Convert explicitly with `int(...)` or `double(...)`. Comparison is the exception: integers and doubles compare by mathematical value, so `1 == 1.0` is `true` and they sort together as one numeric kind (see [Order within a set](#order-within-a-set)).

Missing values propagate as errors, not silently. `one(s)` on an empty set is `null`, and `null` is not a usable operand: arithmetic like `now.getFullYear() - one(self.bornYear)` errors for any entity whose `bornYear` is missing, and a member access like `one(self.family).name` errors when `family` is empty (`null.name` — `null` is not an entity). The error is per-entity, raised only when that rule is evaluated, so it can hide until a specific entity is read. Guard any relation that may be absent with a fallback: `one(self.bornYear, 1900)`, `one(one(self.family).name, "")`.

Comparing values of different kinds is never equal, not an error: a number and a string, a boolean and an entity — any two different kinds — are `!=`, never `==`. So a mistyped comparison fails quietly as `false` rather than flagging the kind mismatch. (Integers and doubles are *not* different kinds for this purpose — they compare by value, as above.)

Example expressions:

```
Person age = now.getFullYear() - one(self.bornYear);
Person lastName = one(one(self.family).name);
Person enemies = instances(Person).filter(p, self.dislikes.exists(fam, fam in p.family) && self != p);
Person eldest = one(sortBy(self.children, "bornYear"));
```

### Evaluation model

TWL is a pure function: given the world, the session event stream, and a small per-turn environment, it computes values — `eval(world, session, env) → values`. The same inputs always produce the same values. All change over the course of a game happens outside TWL, in the host: the host owns the facts and the event stream and asks TWL what they imply.

Inputs come in two kinds plus an environment:

- World — the authored cartridge: types, `extends`, computed rules, and static facts. Immutable for the whole game; this is what the author ships. All facts are author-authored.
- Session — the event stream the host accumulates during play: an append-only, ordered list of events. Growing, but only the host writes it — TWL never does. Events are *data*, not facts: they live in their own `session` namespace and are read through CEL, never mixed into the fact graph (see [Events](#events)).
- Env — the per-evaluation environment: `now`, the `rand` seed (you cannot read or set it from inside the world), a turn counter, and whatever else the host passes for this one query. Not facts; reached through their own keywords and builtins. Ephemeral; gone after the call.

This split is deliberate. Facts are trusted author content; the session stream is where
untrusted, host-supplied content (including raw player text) lives. Because the two never
merge, player text is never parsed as world source. If you derive a string relation from the
session and render it, though, it is still a string value and is rendered as a template; keep
that path escaped or keep untrusted text outside rendered TWL strings. A "player action" is
just the latest event the host appended; there is no separate action layer.

`now` and `rand` are always available from the evaluation environment (defaulted if the host
does not pass one). Session streams default to empty lists when the host supplies no session
or no stream with that name.

This reproducibility holds for a given engine build. Floating-point results are bit-for-bit
stable for ordinary arithmetic and the built-in helpers (`math.floor`, `math.ceil`,
`math.round`, `math.trunc`, `math.abs`, `math.sign`, `math.sqrt`) on the tested runtimes, but
a host-supplied CEL backend or extra functions may differ in their last bits across platforms
or library versions — don't rely on cross-machine bit-identical output from those.

Because TWL holds no state, a server can be stateless: cache the world (it never changes), persist the session event stream between requests, and supply the environment per request. To resume a game, reload world and session and evaluate. There is nothing else to restore.

### Events

The session is an append-only stream of events. TWL does not define what an event is; its shape belongs to the host, per game. An event is a record with whatever fields the host gives it (`actor`, `verb`, `target`, `value`, `observers`, …); a field holds a scalar or an entity reference.

Events are exposed under the `session` namespace as named lists and read with ordinary CEL collection ops:

```
Person threats = session.events.filter(e, e.target == self).map(e, e.actor);
Person insultCount = size(session.events.filter(e, e.target == self && e.verb == "insult"));
Person lastSpoke = session.events.filter(e, e.actor == self);
```

Note that the host may append in occurrence order, so events are assumed to be chronological. Also, events are not deduplicated. Two identical actions are two distinct events. TWL is pure, so "react to an event" means "recompute": each evaluation reads the current stream and derives fresh values. The world never mutates itself; the host appends events to the session and may persist computed results however it likes.

### Randomness

`rand(key)` returns a number in `[0, 1)`. It is stable: the same key always gives the same draw, so worlds stay reproducible. The seed comes from the game each evaluation and cannot be read or set from inside the world.

The key can be any stable value — string, number, entity, or a list — and is hashed with the seed; equal keys give equal draws, so pass a key unique to each draw. Usually a list of the entity plus a label, `[self, "label"]`: the list keeps parts distinct (gluing strings collides — `"a" + "bc"` equals `"ab" + "c"`), and putting the entity itself in the key (not its name) keeps the draw stable across a `sameas` merge. A plain `rand("daily")` works for draws not tied to an entity.

```
Chest gold = int(rand([self, "gold"]) * 100.0); // 0..99
Die roll = int(rand([self, "roll"]) * 6.0) + 1; // 1..6
```

### Tiebreaks

A relation may have a stored fact and one or more computed rules. The value is chosen like this:

1. A stored fact always wins over a computed one. If a relation has any stored fact on an entity, every computed rule for that relation is suppressed for that entity — stored and computed never blend, so the result is exactly the stored set (one fact does not merge into or extend a computed set). (Stored facts are author content only; per-turn input arrives in the session stream, not as a fact, so it never participates in this tiebreak.)
2. Otherwise the rule on the most specific type wins (a subtype beats its parent). When a type is reachable by several inheritance paths, its specificity is the length of the *shortest* path.
3. If two rules are equally specific, or two unrelated types both apply (including a diamond where two sibling types each carry a rule), it is an error — resolve it with an explicit rule on the more specific type.

### Templates

Every string value (`"..."`) is a Mustache template, rendered against the entity that owns it. Names inside refer to that entity's relations. A value with no `{{` tags renders to its literal text, so a plain fact like `Marty firstName "Marty"` just yields `Marty`; to write a literal `{{`, escape it as `\{\{` (see [Values](#values)).

- `{{rel}}` — insert the value, escaped. TWL itself is a source of truth, not a renderer, so it does not assume HTML: the escaping is whatever escaper the host configures for its output (HTML for a web target, none for plain text or JSON, etc.).
- `{{{rel}}}` or `{{& rel}}` — insert the value raw, never escaped, whatever the host escaper is.
- `{{# rel}} ... {{/}}` — repeat the block for each value in the set (in canonical order); inside, `{{.}}` is the current value and names resolve on it.
- A relation with one value renders that value; an empty relation renders nothing. A relation with more than one value renders only its canonical-first value (see [Order within a set](#order-within-a-set)) — the rest are dropped silently, so use a `{{# rel}}` block to render them all.

A single boolean fact is still a one-value set, so `{{# flag}}` renders for both `true` and `false`. To branch on a condition, make a computed relation that is empty when the condition is false.

Names in a template resolve to the owning entity's relations. To render event-derived text,
first compute a relation from the stream (`Person lastLine = session.events.filter(e, e.actor
== self).map(e, e.text);`) and render that. Two cautions when that text is untrusted: every
string value is rendered as a template, so `{{...}}` inside event text can resolve against the
owning entity; and `{{{rel}}}` / `{{& rel}}` bypasses the host escaper entirely. Prefer
escaped `{{rel}}`, or keep raw player text outside TWL-rendered strings.

### Example

```
Person extends Entity;
Family extends Entity;

Person age = now.getFullYear() - one(self.bornYear);
Person lastName = one(one(self.family).name);
Person enemies = instances(Person).filter(p, self.dislikes.exists(fam, fam in p.family) && self != p);

McFlyFam instanceof Family; McFlyFam name "McFly";

Marty instanceof Person; Marty firstName "Marty";
Marty bornYear 1968; Marty family McFlyFam;

Lorraine instanceof Person; Lorraine firstName "Lorraine";
Lorraine bornYear 1949; Lorraine family McFlyFam;

Biff instanceof Person; Biff firstName "Biff";
Biff bornYear 1937; Biff dislikes McFlyFam;
Biff persona "A bully with disdain for:
{{# enemies}}
  - {{firstName}} {{lastName}}
{{/}}";
```

---

## C# engine & query API

The engine (`src/TinyWorldLang`) is a pure function — `eval(world, session, env) → values` —
so the same inputs always produce the same values and a host can stay stateless. It targets
**`netstandard2.0`** with **zero third-party runtime dependencies**, so it drops into Unity
(Mono **and** IL2CPP/AOT), MonoGame, consoles, WebGL, and desktop unchanged. No NuGet package
is published yet — reference the project directly, or drop the `src/TinyWorldLang/*.cs`
sources into your game project / Unity `Assets`. (Building the tests needs the .NET 8 SDK; the
engine itself does not.)

```csharp
using TinyWorldLang;        // TwlWorld, exceptions
using TinyWorldLang.Eval;   // Env, Session, Entity, Field

// Parse + validate once (throws TwlLoadException on errors); cache and reuse the world.
var world = TwlWorld.Load(source);

// eval(world, session, env) -> a WorldView; create a fresh view per query batch.
var view = world.Evaluate(
    Session.OfEvents(events),                              // append-only host events, read under `session`
    new Env(now: DateTimeOffset.UtcNow, seed: 42, turn: 3)); // now frozen; seed drives rand; turn a counter

// One way to query: entity handles + standard LINQ-to-objects.
int    age  = view.Entity("Marty")["age"].AsInt;          // a single typed value
string last = view.Entity("Marty")["lastName"].Text;      // string values are RENDERED templates

var elders = view.Entities("Person")                      // every instance of a type (incl. subtypes)
    .Where(p => p["age"].AsInt >= 50)
    .Select(p => new { p.Name, Bio = p["persona"].Text });
```

Reading a relation returns a `Field` — the relation's value set in [canonical order](#order-within-a-set),
and never an error (an absent relation is just an empty `Field`). `Field` exposes typed
single-value accessors (`AsInt`, `AsDouble`, `AsBool`, `AsEntity`, `One()`), rendered text
(`.Text`, `.Texts`, with `.Source` for the raw template source), and the set itself
(`.Entities` for entity-valued members, `.Values` for the raw set). Querying is
LINQ-**to-objects**, never `IQueryable` with expression trees — nothing is JIT-compiled at
query time, so it stays AOT-safe on consoles and WebGL.

The typed accessors are strict: they operate on the canonical-first value and throw
`TwlEvalException` if the field is empty or the value has the wrong kind. Use `IsEmpty`,
`Count`, `One()`, or `Values` when the shape is optional or multi-valued.

Every string value is a [Mustache template](#templates) rendered against its owning entity, so
`.Text` always returns the rendered result. Supply an escaper at load time for your output
target (it applies to `{{ }}` tags, not `{{{ }}}` raw tags):

```csharp
var world = TwlWorld.Load(source,
    escaper: s => s.Replace("<", "&lt;").Replace(">", "&gt;"));  // an HTML escaper, for example
```

Errors: `TwlException` is the base; `TwlLoadException` (syntax / load-time, with `.Line`)
surfaces from `Load`; `TwlEvalException` is **per-entity** and surfaces only when that entity's
rule is read, so missing data or another runtime failure can hide until a specific entity is queried. The CEL evaluator sits
behind a swappable `ICelEvaluator` seam — the default tree-walking backend is AOT-safe and
dependency-free; a host on a JIT-only platform may inject its own.

---

## Design & platform notes

The engine must run on **Unity (Mono + IL2CPP/AOT), WebGL, MonoGame, macOS desktop, and game
consoles**. That rules out the usual shortcuts and drives every structural decision:

| Constraint | Consequence |
|---|---|
| IL2CPP / AOT (consoles, WebGL, iOS) forbids runtime codegen | **No** `Reflection.Emit`, `Expression.Compile`, or `DynamicMethod`. The CEL engine is a **tree-walking interpreter**, and querying is LINQ-to-objects, not `IQueryable`. |
| Broadest runtime surface | Core targets **`netstandard2.0`** (covers Unity all-backends, MonoGame, current console toolchains). |
| Determinism / reproducibility (per the spec) | `rand` uses a **stable FNV-1a hash** over a canonical key encoding — never `string.GetHashCode()`, which .NET salts per process. |
| Keep it portable | Core has **zero third-party runtime dependencies** and does **no JSON** — hosts own (de)serialization with whatever they already have. |

**Why a query API, not GraphQL.** TWL's data model is literally a triple store
(`SUBJECT RELATION VALUE` is an RDF triple). GraphQL assumes a static, typed, tree-shaped
schema; TWL is an *open, dynamic triple graph* — the wrong shape. For an in-process C#
surface, the prior art that needs zero new documentation is **LINQ**. A portable *string*
query language is deliberately deferred; if one is ever wanted, a **SPARQL-subset** fits the
triple model and would lower onto the same entity/field primitive — never part of the core.

**Why our own CEL.** No production C# CEL library fits this matrix: the real ones
([Cel.NET](https://www.nuget.org/packages/Cel.NET/),
[cel-net](https://github.com/telus-oss/cel-net)) pull in Protobuf + gRPC + ANTLR and compile
expressions into delegates (runtime codegen) — unusable on IL2CPP/console/WebGL and not
`netstandard2.0`. So the core ships its **own AOT-safe evaluator for the CEL subset the spec
uses**, and honors "don't diverge from CEL" by pinning correctness to Google's official
[cel-spec conformance corpus](https://github.com/google/cel-spec/tree/master/tests/simple/testdata)
rather than to our own judgment. The evaluator sits behind the swappable
[`ICelEvaluator`](src/TinyWorldLang/Cel/CelSeam.cs) seam.

---

## Project layout

```
src/TinyWorldLang/            netstandard2.0, zero deps — the engine
  Values/                     Value, ValueKind, canonical ordering, ValueSet
  Parsing/                    TWL statement lexer/parser + AST
  Model/                      World, type/identity graph (instanceof/extends/sameas), tiebreak inputs
  Cel/                        ICelEvaluator seam + AST + tree-walking interpreter
  Eval/                       Env, Session/Event, WorldView, Entity/Field query handles, tiebreak, CEL context
  Templates/                  Mustache-subset renderer
  Rand/                       stable hash + unit-interval mapping
  TwlWorld.cs                 public entry point

tests/TinyWorldLang.Tests/        unit + integration tests (values, parser, world, CEL, rand, templates, query API)
tests/TinyWorldLang.Conformance/  data-driven harness pinning the CEL subset to cel-spec semantics
```

## Building & testing

```bash
dotnet test
```

Requires the .NET 8 SDK (test projects target `net8.0`; the engine itself is `netstandard2.0`).

## Status & roadmap

Working and tested: the TWL parser; the type/identity graph (`instanceof` transitivity,
`extends` cycle detection, `sameas` merge with canonical naming); stored-vs-computed and
specificity tiebreak; the LINQ query surface (`Entities` / `Entity` → `Field`, with string
values auto-rendering); canonical set ordering; reproducible `rand`; the template renderer;
and a CEL interpreter covering the spec's examples (`map`/`filter`/`exists`/`all`, `in`, `?:`,
member/index, `math.*`, `one`/`sortBy`/`instances`/`rand`/`size`/`int`/`double`/`string`/`bool`,
`now` date methods, and `session.*` event reads).

Known growth points (intentionally not done yet):

- **Full CEL conformance.** The conformance project is data-driven from upstream-shaped
  `.textproto`, but currently vendors a curated supported subset rather than the full
  `cel-spec` corpus. Treat untested CEL corners as unverified or explicitly out of scope.
- **Cross-platform float determinism** is by construction (basic IEEE ops, `double`), but is
  not yet locked by a golden-output test run across runtimes/architectures.
- This is **in-process (Layer 1) only**. An optional network surface and a portable string
  query language are out of scope for now.

## License

Licensed under the Apache License, Version 2.0 — see [`LICENSE`](LICENSE).

Copyright 2026 the Tiny World Language authors.

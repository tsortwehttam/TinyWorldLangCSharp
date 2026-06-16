# TinyWorldLang (C#)

A C# implementation of **Tiny World Language** (see [`SPEC.md`](SPEC.md)) — and the
**Layer 1 in-process query API** for it: load a world once, then query an entity's or
type's relation state by name, passing in a session and a per-evaluation environment
(`now`, `seed`, `turn`).

```csharp
var world = TwlWorld.Load(source);            // parse + validate once, cache it
var view  = world.Evaluate(session, env);     // eval(world, session, env) -> values

// One general way to query: entity handles + standard LINQ-to-objects.
// "such-and-such fields from every entity matching a condition"
var threats = view.Entities("Person")
    .Where(p => p["age"].AsInt > 40)
    .Select(p => new {
        p.Name,
        Bio    = p["persona"].Text,    // string fields are already rendered
        Family = p["family"].Entities, // multi-valued: the whole set as handles
    });

// a generic record: every relation on one entity
var biff = view.Entity("Biff");
foreach (var rel in biff.Relations)
    Console.WriteLine($"{rel} = {biff[rel].Text}");
```

### The query surface (and why not GraphQL)

TWL's data model is literally a **triple store**: `SUBJECT RELATION VALUE` is an RDF
subject-predicate-object triple. That is why GraphQL is the wrong fit — GraphQL assumes a
static, typed, tree-shaped schema, while TWL is an *open, dynamic triple graph*. The
prior art that fits triples is the RDF/graph family (SPARQL, Cypher, Datalog), not GraphQL.

For Layer 1 (in-process, C#), the prior art that needs **zero new documentation** is
**LINQ**: there is one entry point — `Entities()` / `Entity(name)` returning handles — and
everything else is `Where` / `Select` / `OrderBy` that C# developers already know. It is
LINQ-**to-objects** (`IEnumerable`), never `IQueryable` with expression trees, so there is
no `Expression.Compile` and it stays AOT-safe on consoles/WebGL.

- `Entity` — a handle: `Name`, `Is(type)`, `Relations`, and an indexer `e[relation]`.
- `Field` — one relation's current state: a `ValueSet` with ergonomic accessors
  (`One()`, `AsInt`, `Entities`, `IsEmpty`, …). **String values present already rendered**
  (`.Text` / `.ToString()`); the raw template source is available only via `.Source`.

A portable *string* query language is deliberately deferred. If one is ever wanted, a
**SPARQL-subset** is the natural choice (the data is RDF triples) and would lower onto this
same primitive — it would never be part of the core.

## Why this shape (the platform matrix)

The engine must run on **Unity (Mono + IL2CPP/AOT), WebGL, MonoGame, macOS desktop,
and game consoles**. That rules out the usual shortcuts, and drives every structural
decision here:

| Constraint | Consequence |
|---|---|
| IL2CPP / AOT (consoles, WebGL, iOS) forbids runtime codegen | **No** `Reflection.Emit`, `Expression.Compile`, or `DynamicMethod`. The CEL engine is a **tree-walking interpreter**. |
| Broadest runtime surface | Core targets **`netstandard2.0`** (covers Unity all-backends, MonoGame, current console toolchains). |
| Determinism / reproducibility (per the spec) | `rand` uses a **stable FNV-1a hash** over a canonical key encoding — never `string.GetHashCode()`, which .NET salts per process. |
| Keep it portable | Core has **zero third-party runtime dependencies** and does **no JSON** — hosts own (de)serialization with whatever they already have. |

### CEL: no off-the-shelf library fits

There is no production C# CEL library that satisfies this matrix: the real ones
([Cel.NET](https://www.nuget.org/packages/Cel.NET/),
[cel-net](https://github.com/telus-oss/cel-net)) pull in Protobuf + gRPC + ANTLR and
compile expressions into delegates (runtime codegen) — unusable on IL2CPP/console/WebGL
and not `netstandard2.0`.

So the core ships its **own AOT-safe evaluator for the CEL subset the spec uses**, and
honors "don't diverge from CEL" by pinning correctness to **Google's official
[cel-spec conformance corpus](https://github.com/google/cel-spec/tree/master/tests/simple/testdata)**
rather than to our own judgment. The evaluator sits behind the swappable
[`ICelEvaluator`](src/TinyWorldLang/Cel/CelSeam.cs) seam, so a JIT-only host *may* inject
a different backend without the rest of TWL changing.

## Layout

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

tests/TinyWorldLang.Tests/        unit + integration tests (values, parser, world, CEL, rand, templates, full SPEC example)
tests/TinyWorldLang.Conformance/  SKELETON harness pinning the CEL subset to cel-spec semantics
```

## What's implemented in this first cut

Working and tested: the TWL parser; the type/identity graph (`instanceof` transitivity,
`extends` cycle detection, `sameas` merge with canonical naming); stored-vs-computed and
specificity **tiebreak**; the **LINQ query surface** (`Entities` / `Entity` → `Field`, with
string values auto-rendering); canonical set ordering; reproducible `rand`; the template
renderer; and a CEL interpreter
covering the spec's examples (`map`/`filter`/`exists`/`all`, `in`, `?:`, member/index,
`math.*`, `one`/`sortBy`/`instances`/`rand`/`size`/`int`/`double`/`string`/`bool`, `now`
date methods, and `session.*` event reads).

### Known growth points (intentionally not done yet)

- **Full CEL conformance.** The conformance project is a *skeleton*: a handful of canonical
  cases plus the documented path to vendor the full `cel-spec` `.textproto` corpus and drive
  `[Theory]` cases from it. Until that lands, treat untested CEL corners as unverified.
- **Cross-platform float determinism** is by construction (basic IEEE ops, `double`), but is
  not yet locked by a golden-output test run across runtimes/architectures.
- This is **Layer 1 only** (in-process). The optional network surface (Layer 2) is out of scope.

## Build & test

```bash
dotnet test
```

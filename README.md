# TinyWorldLang (C#)

A C# implementation of **Tiny World Language** (see [`SPEC.md`](SPEC.md)) — and the
**Layer 1 in-process query API** for it: load a world once, then query an entity's or
type's relation state by name, passing in a session and a per-evaluation environment
(`now`, `seed`, `turn`).

```csharp
var world = TwlWorld.Load(source);            // parse + validate once, cache it
var view  = world.Evaluate(session, env);     // eval(world, session, env) -> values
var age   = view.GetOne("Marty", "age");      // query by name + relation
var bios  = view.Query("Person", "persona");  // a relation across a whole type
var text  = view.Render("Biff", "persona");   // render a string value's template
```

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
  Eval/                       Env, Session/Event, WorldView (query surface + tiebreak + CEL context)
  Templates/                  Mustache-subset renderer
  Rand/                       stable hash + unit-interval mapping
  TwlWorld.cs                 public entry point

tests/TinyWorldLang.Tests/        unit + integration tests (values, parser, world, CEL, rand, templates, full SPEC example)
tests/TinyWorldLang.Conformance/  SKELETON harness pinning the CEL subset to cel-spec semantics
```

## What's implemented in this first cut

Working and tested: the TWL parser; the type/identity graph (`instanceof` transitivity,
`extends` cycle detection, `sameas` merge with canonical naming); stored-vs-computed and
specificity **tiebreak**; the **query API** (`Get` / `GetOne` / `Query` / `Render`);
canonical set ordering; reproducible `rand`; the template renderer; and a CEL interpreter
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

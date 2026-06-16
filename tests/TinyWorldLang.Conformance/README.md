# CEL conformance harness

This project exists to answer one question with an external standard rather than our own
judgment: **does our CEL subset mean the same thing as CEL?**

The spec (the language reference in the top-level `README.md`) says expressions are CEL.
Because no AOT-safe C# CEL library fits the
platform matrix (see top-level `README.md`), the core ships its own tree-walking evaluator.
The way we keep that from quietly diverging is to pin it to Google's language-neutral
conformance corpus:

- https://github.com/google/cel-spec/tree/master/tests/simple/testdata

## Current state

`CelConformanceTests.cs` loads upstream-shaped `.textproto` files from `testdata/` and runs
the cases through `TreeWalkingCelEvaluator` with a world-free context.

The first vendored data file, `testdata/cel-spec-supported.textproto`, is a curated scalar
subset of Google's `tests/simple/testdata` corpus. It covers supported basics, int/double
math, comparisons, logic, list indexing/membership, macros, strings, and TWL's documented
`math.*` helpers.

This is still not full CEL coverage. It deliberately excludes areas TWL does not currently
claim: protobuf messages, maps, bytes, uints, wrappers, dynamic bindings, optional values,
full timestamp arithmetic, and string extension methods such as `startsWith`.

## Growth path

1. Grow `testdata/cel-spec-supported.textproto` by importing more cases from the upstream
   files whenever the evaluator supports the expression/result shape.
2. Add explicit skip metadata if we decide to vendor whole upstream files and filter them
   mechanically.
3. Treat any case the corpus covers that we fail as either a bug to fix or a section to formally
   declare out of scope in the language reference (top-level `README.md`).

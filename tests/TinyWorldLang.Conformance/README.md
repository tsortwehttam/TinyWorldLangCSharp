# CEL conformance harness (skeleton)

This project exists to answer one question with an external standard rather than our own
judgment: **does our CEL subset mean the same thing as CEL?**

The spec (the language reference in the top-level `README.md`) says expressions are CEL.
Because no AOT-safe C# CEL library fits the
platform matrix (see top-level `README.md`), the core ships its own tree-walking evaluator.
The way we keep that from quietly diverging is to pin it to Google's language-neutral
conformance corpus:

- https://github.com/google/cel-spec/tree/master/tests/simple/testdata

## Current state

`CelConformanceTests.cs` runs a small set of canonical cases (mirroring the `basic`,
`comparisons`, `logic`, `lists`, `macros`, `string`, and `math_ext` sections) through
`TreeWalkingCelEvaluator` with a world-free context. This is a *starting subset*, not full
coverage.

## Growth path

1. Vendor the `.textproto` files for the subset the spec declares (skip sections TWL does not
   support — protobuf messages, maps, full timestamp arithmetic, etc.; cel-spec is explicitly
   organized to allow implementing a prescribed subset).
2. Add a minimal textproto reader (or a one-time conversion to JSON via the Go/▢ tooling) so the
   data files drive `[Theory]` cases directly.
3. Treat any case the corpus covers that we fail as either a bug to fix or a section to formally
   declare out of scope in the language reference (top-level `README.md`).

# TWL — Design Goals & Constraints

The *why* behind the spec. Normative rules live in [`spec.md`](spec.md); this is the rationale they must not violate.

## Purpose

A scripting layer for **user-generated content in hackable worlds for social-simulation games**. Untrusted players author world logic; the runtime must execute it safely and deterministically.

## Origin

Started from an "Excel-ish / HyperFormula-compatible subset" description. Rejected: too fuzzy. Prose-defined subsets invite LLMs and implementers to *assume things into* the language — behavior gets reinterpreted, not specified. The whole design is a reaction to that fuzziness.

## Core principle — borrow correctness, don't author it

Pin behavior to **known, conformance-tested standards** used verbatim, not to prose that can be assumed-into. A small language should **compose existing well-specified things** rather than invent its own.

- Each sublanguage (CEL, Mustache, RDF/OWL) is **embedded whole behind a clean delimiter**, never reinterpreted.
- Correctness is **citable, not trusted** — borrowed from an upstream spec + its conformance suite.
- Anything TWL *must* configure or author is **named explicitly and kept minimal** (the Embedding profile + the single precedence rule), so the trust surface is small and auditable.
- These standard choices (CEL, Mustache, RDF/OWL, the conformance invariant) are **means, not ends** — swappable if a better-specified substrate serves the principles below.

## Constraints (ranked)

1. **Misinterpretation-resilient.** Nothing left to reinterpret. Two implementers (or two LLMs) converge on identical behavior. Ambiguity is a bug.
2. **UGC-safe.** No loops, no Turing-completeness, no side effects, no injection, bounded cost. Safe to feed untrusted content.
3. **Borrowed over invented.** Prefer deferring to an upstream spec over writing a new rule, even at an ergonomic cost.
4. **Small enough to hold in your head.** Short spec, ideally leveraging existing specs so there's little of TWL's own to learn. (Aspirational target was ~50 lines; precision pushed it higher — overage is the auditable boundary, not prose.)
5. **Deterministic.** Same graph → same output. Survives merge + RDF round-trip.

## Authoring-LLM consideration

A first-class use case is **LLMs writing TWL**. The spec is shaped so a model can't silently invent semantics: every behavioral claim cites a standard or is flagged as the one authored rule. Smaller models still fail at the embedding seams (CEL idioms, scalar/list, precedence) — which is exactly why those seams are pinned in the profile.

## Out of scope (for now)

- **Dynamics / mutation.** The spec is a static fact graph + derived views + templated text. State change over time (the actual "simulation") is a **separate layer, coming later**, and must honor the same constraints — UGC-safe, non-Turing-complete, borrowed-correctness.

## Litmus test for any change

> Is the new behavior *cited* from an upstream spec, or *authored* by TWL? If authored, is it in the named minimal surface, and does it fail loud rather than resolve silently?

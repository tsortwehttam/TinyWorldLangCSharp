# Tiny World Language (TWL)

TWL is a tiny language for authoring dynamic worlds as a fact graph. Everything is an entity; entities link by labeled, set-valued relations. TWL defines only a triplet grammar and entity/relation model. Computation, presentation, and inference are delegated verbatim:

- computation — CEL — `github.com/google/cel-spec`
- presentation — Mustache — `github.com/mustache/spec`
- model and inference — RDF 1.1 / RDFS / OWL

## Statements

```
// line, /* block */ comments
SUBJECT RELATION VALUE ;     // stored fact
TYPE    RELATION -> CEL ;    // derived fact
```

`VALUE` is one of:

- entity ref — bare id — `Mac`, `McFlyFam`
- number — `123`, `1.5` (CEL int64 / double)
- boolean — `true`, `false`
- string — `"..."` (a Mustache template)

`null` is a CEL result only, never a stored value (keeps the RDF round-trip lossless).

- Identifier = CEL id `[A-Za-z_][A-Za-z0-9_]*`, not a reserved word → usable inside CEL unchanged.
- Reserved: `instanceof extends sameas true false null self now instances sortBy`.

## Entities & relations

- Every relation is total, set-valued: `self.r` = the set of values, `[]` if none.
- Reading never errors; `self.r[0]` on an empty set errors (plain CEL).
- A set is unordered, idempotent, order-independent → one graph.
- Open-world: any id denotes an entity, no declaration; unknown → all relations `[]`.
- Sequence ops take a canonical order (see profile) — deterministic across merge + RDF round-trip.

## Ontology & inference

Three built-in relations alias RDF, carry its entailment, stored-only (not derivable) — so typing/precedence cannot loop:

- `instanceof` — rdf:type — `X instanceof A` ∧ `A extends B` ⇒ `X instanceof B`
- `extends` — rdfs:subClassOf — transitive
- `sameas` — owl:sameAs — `A sameas B` ⇒ one entity

`==` is taken after `sameas` merge. Other RDF/OWL vocab may be stored but entails nothing. Every TWL graph is RDF (lossless out); TWL omits rich datatypes, arbitrary IRIs, blank nodes.

## Computation (CEL)

Text after `->` is CEL, verbatim, evaluated lazily. For each `self instanceof TYPE`, `RELATION` = the CEL result.

- `self` — the current entity
- `now` — timestamp, frozen per top-level eval, UTC components
- `instances(T)` — entities `instanceof T` (entailed)
- `sortBy(list, k)` — list ordered by the relation named by string `k`
- `e.r` — the set of values of relation `r` on entity `e`

No implicit numeric coercion. Every operation is plain CEL, for example:

- traverse: `self.family.map(f, f.name[0])`
- select: `instances(Person).filter(p, McFlyFam in p.family)`
- order: `sortBy(self.children, "bornYear")`
- scalar: `self.bornYear[0]`

### Precedence

The one place TWL authors its own semantics: when a subject has several candidate values (stored + derived, or derived from several types), one must win. RDF only adds facts, never shadows them, so this can't be borrowed. Resolving a relation's value:

1. A stored fact wins over any derived one.
2. Else the most specific applicable `TYPE` (by `extends`) wins; `Entity` is the fallback.
3. Ties error, never silent: incomparable TYPEs = conflict; two defs of the same TYPE+RELATION = duplicate.

## Presentation (Mustache)

A VALUE-position string is a template, rendered against its owning entity when that relation is presented. (Strings inside a CEL expression are CEL strings.) Each single-id name resolves to a relation (stored/derived); dynamic content is a derived relation referenced by name.

- `{{ r }}` — interpolate, HTML-escaped (Mustache default).
- Raw text: `{{{ r }}}` / `{{& r }}` (e.g. LLM prompts)
- List case (Mustache-undefined): TWL renders the set in canonical order — single → its value, empty → `""`.
- `{{# r }}..{{/}}` — standard list section; binds each value as context (`{{.}}` = the value).
- Footgun: one bool fact is a one-element set, so `{{# flag }}` renders for `true` and `false`. Guard truthiness with a derived relation.

## Embedding profile (the entire configured boundary)

Everything TWL adds on top of the borrowed specs lives here, so the trust surface stays small enough to audit at a glance.

- CEL. The stock standard environment plus the macros `has all exists exists_one map filter`, and no extensions. The only additions are the Computation context above (`self`, `now`, `instances`, `sortBy`) and the entity type itself, whose field access is total — reading any relation yields a list, never CEL's native missing-field error.
- Ids and strings. The four host names are reserved as identifiers so an entity can't shadow them. A string value is lexed as a CEL string literal (its escapes, `r"..."`, and multiline `"""..."""`); whatever that decodes to is the Mustache template.
- Determinism. Where order matters, a set is sorted first by kind (`bool < number < string < entity`), then within a kind — `false` before `true`, numbers numerically, strings by code point, entities by canonical id. Under `sameas`, an entity's canonical id is the lexicographically least in its merged class. A `sortBy` key takes each element's canonical-first value, and an empty or mixed-type key is an error. Rendered to text: bool becomes `true`/`false`, int its decimal, double its shortest round-tripping decimal, null the empty string.
- RDF. Entities become IRIs under a host-fixed base; literals map to the matching xsd datatypes.

## Example

```
Entity name "Entity";
Person extends Entity;
Family extends Entity;

Person age      -> now.getFullYear() - self.bornYear[0];
Person lastName -> self.family.map(f, f.name[0]);
Person enemies  -> instances(Person).filter(p, McFlyFam in p.family && self != p);

McFlyFam instanceof Family;  McFlyFam name "McFly";

Marty instanceof Person;  Marty firstName "Marty";
Marty bornYear 1968;      Marty family McFlyFam;

Lorraine instanceof Person;  Lorraine firstName "Lorraine";
Lorraine family McFlyFam;

Biff instanceof Person;
Biff persona """A bully with disdain for:
{{# enemies }}
  - {{ firstName }} {{ lastName }}
{{/}}""";
```

## Core principles

- Pin behavior to known, conformance-tested standards. Compose existing well-specified things.
- Each sublanguage (CEL, Mustache, RDF/OWL) is embedded whole behind a clean delimiter, never reinterpreted.
- Correctness is citable, not trusted — borrowed from an upstream spec + its conformance suite.
- Anything TWL must configure or author is named explicitly and kept minimal, so the trust surface is small and auditable.
- Two implementers (or two LLMs) converge on identical behavior. Ambiguity in the spec is a bug.
- UGC-safe. No loops, no Turing-completeness, no side effects, no injection, bounded cost. Safe to feed untrusted content.
- Small enough to hold in your head. Short spec, ideally leveraging existing specs so there's little of TWL's own to learn.
- Deterministic. Same graph → same output. Survives merge + RDF round-trip.

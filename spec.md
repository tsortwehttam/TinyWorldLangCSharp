# Tiny World Language (TWL) — Implementer Specification

TWL is a language for authoring dynamic worlds as a fact graph. Everything is an
entity; entities link by labeled, set-valued relations. TWL defines a triplet
grammar and an entity/relation model; computation, presentation, and inference
are delegated to existing specifications, embedded unmodified:

- computation — CEL — `github.com/google/cel-spec`
- presentation — Mustache — `github.com/mustache/spec`
- model and inference — RDF 1.1 / RDFS / OWL

The authoring surface is described in `authoring.md`. This document is the
complete normative specification, including the embedding profile that fixes
every behavior an implementation must reproduce.

## Statements

```
// line, /* block */ comments
SUBJECT RELATION VALUE ;     // stored fact
TYPE    RELATION = CEL ;    // derived fact
```

`VALUE` is one of:

- entity ref — bare id — `Mac`, `McFlyFam`
- number — `123`, `1.5` (CEL int64 / double)
- boolean — `true`, `false`
- string — `"..."` (a Mustache template)

`null` is a CEL result only, never a stored value (keeps the RDF round-trip
lossless).

- Identifier = CEL id `[A-Za-z_][A-Za-z0-9_]*`, not a reserved word → usable
  inside CEL unchanged.
- Reserved: `instanceof extends sameas true false null self now instances sortBy one rand`.

## Entities & relations

- Every relation is total, set-valued: `self.r` = the set of values, `[]` if
  none.
- Reading never errors; `self.r[0]` on an empty set errors (plain CEL). Use
  `one(self.r)` for the single value of a relation — it yields `null` rather than
  erroring when the set is empty, or `one(self.r, fallback)` to yield a default
  instead of `null`.
- A set is unordered, idempotent, order-independent → one graph.
- Open-world: any id denotes an entity, no declaration; unknown → all relations
  `[]`.
- Sequence ops take a canonical order (see profile) — deterministic across merge
  + RDF round-trip.

## Ontology & inference

Three built-in relations alias RDF, carry its entailment, stored-only (not
derivable) — so typing/precedence cannot loop:

- `instanceof` — rdf:type — `X instanceof A` ∧ `A extends B` ⇒ `X instanceof B`
- `extends` — rdfs:subClassOf — transitive
- `sameas` — owl:sameAs — `A sameas B` ⇒ one entity

`==` is taken after `sameas` merge. Other RDF/OWL vocab may be stored but entails
nothing. Every TWL graph is RDF (lossless out); TWL omits rich datatypes,
arbitrary IRIs, blank nodes.

## Computation (CEL)

Text after `=` is CEL, verbatim, evaluated lazily. For each
`self instanceof TYPE`, `RELATION` = the CEL result.

- `self` — the current entity
- `now` — timestamp, frozen per top-level eval, UTC components
- `instances(T)` — entities `instanceof T` (entailed)
- `sortBy(list, k)` — list ordered by the relation named by string `k`
- `one(s)` — the single value of set `s` in canonical order, or `null` if `s` is
  empty
- `one(s, fallback)` — as `one(s)`, but yields `fallback` instead of `null` when
  `s` is empty
- `e.r` — the set of values of relation `r` on entity `e`
- `rand(x)` — a deterministic double in `[0, 1)` from the evaluation seed and the
  canonical encoding of `x` (see profile); pass a key unique to the draw, e.g.
  `rand([self, "hp"])`

No implicit numeric coercion. Every operation is plain CEL, for example:

- traverse: `self.family.map(f, one(f.name))`
- select: `instances(Person).filter(p, McFlyFam in p.family)`
- order: `sortBy(self.children, "bornYear")`
- scalar: `one(self.bornYear)`

### Precedence

When a subject has several candidate values (stored + derived, or derived from
several types), one must win. Resolving a relation's value:

1. A stored fact wins over any derived one.
2. Else the most specific applicable `TYPE` (by `extends`) wins; `Entity` is the
   fallback.
3. Ties error, never silent: incomparable TYPEs = conflict; two defs of the same
   TYPE+RELATION = duplicate.

## Evaluation & frames

An evaluation resolves derived relations against the graph in an environment the
host supplies and freezes for that evaluation:

- `now` — the current timestamp.
- a random seed — consumed by `rand` (see profile); never read directly, so
  untrusted content cannot set it.

Beyond the persisted graph, the host may overlay **transient facts** for a single
evaluation — for example per-agent inputs or a turn counter. These are ordinary
stored facts: read through relations, subject to precedence (a transient stored
input overrides a derived default), and discarded after the evaluation rather
than persisted. Which entity and relations carry them is host convention; TWL
reserves no names for them.

TWL performs no iteration or mutation. Advancing a world over time is the host's
loop: it evaluates designated derived relations, applies their results as stored
facts, supplies the next environment and transient facts, and re-evaluates.
Output is a deterministic function of the graph evaluated (transient facts
included) and the environment.

## Presentation (Mustache)

A VALUE-position string is a template, rendered against its owning entity when
that relation is presented. (Strings inside a CEL expression are CEL strings.)
Each single-id name resolves to a relation (stored/derived); dynamic content is a
derived relation referenced by name.

- `{{ r }}` — interpolate, HTML-escaped (Mustache default).
- Raw text: `{{{ r }}}` / `{{& r }}` (e.g. LLM prompts)
- List case (Mustache-undefined): TWL renders the set in canonical order —
  single → its value, empty → `""`.
- `{{# r }}..{{/}}` — standard list section; binds each value as context
  (`{{.}}` = the value).
- Footgun: one bool fact is a one-element set, so `{{# flag }}` renders for `true`
  and `false`. Guard truthiness with a derived relation.

## Embedding profile (the configured boundary)

Everything TWL adds on top of the borrowed specs lives here, so the trust surface
stays small enough to audit at a glance.

- CEL. The stock standard environment plus the macros
  `has all exists exists_one map filter` and the CEL `math` extension, and no
  other extensions. The only additions
  are the Computation context above (`self`, `now`, `instances`, `sortBy`,
  `one`) and the entity type itself, whose field access is total — reading any
  relation yields a list, never CEL's native missing-field error.
- Ids and strings. The host names are reserved as identifiers so an entity can't
  shadow them. A string value is lexed as a CEL string literal (its escapes,
  `r"..."`, and multiline `"""..."""`); whatever that decodes to is the Mustache
  template.
- Determinism. Where order matters, a set is sorted first by kind
  (`bool < number < string < entity`), then within a kind — `false` before
  `true`, numbers numerically, strings by code point, entities by canonical id.
  Under `sameas`, an entity's canonical id is the lexicographically least in its
  merged class. `one(s)` yields the canonical-first value of `s`, or `null` when
  `s` is empty; `one(s, d)` yields `d` instead of `null` for an empty `s`. A
  `sortBy` key takes each element's canonical-first value, and an
  empty or mixed-type key is an error. Rendered to text: bool becomes
  `true`/`false`, int its decimal, double its shortest round-tripping decimal,
  null the empty string.
- PRNG. `rand(x)` is deterministic: canonically encode the pair `(seed, x)` to
  bytes — entities as their canonical id, scalars as their rendered text form
  (above), lists element-wise in order, each element tagged by kind and
  length-prefixed — take `SHA-256`, then read the leading 53 bits big-endian as
  an integer `n` and return `n × 2⁻⁵³`, a double in `[0, 1)`. The seed is the
  host-provided evaluation seed. Because the encoding uses canonical ids and
  values, draws are stable across `sameas` merge and the RDF round-trip. For an
  integer in `[0, k)` use `int(rand(x) * k)` with `k` a double (e.g. `6.0`).
- RDF. Entities become IRIs under a host-fixed base; literals map to the matching
  xsd datatypes.

## Conformance

- CEL, Mustache, and RDF/RDFS/OWL are embedded exactly as their specifications
  define them. An implementation must not extend or reinterpret them beyond the
  embedding profile above.
- Correctness for each embedded language is established by its upstream
  conformance suite.
- The only semantics TWL defines itself are: the statement grammar, total
  set-valued relations, the three built-in relations' RDF aliasing, value
  precedence, the evaluation environment (`now`, seed) and frame model, the
  `rand` construction, and the determinism rules in the embedding profile.

## Example

```
Entity name "Entity";
Person extends Entity;
Family extends Entity;

Person age      = now.getFullYear() - one(self.bornYear);
Person lastName = self.family.map(f, one(f.name));
Person enemies  = instances(Person).filter(p, McFlyFam in p.family && self != p);

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

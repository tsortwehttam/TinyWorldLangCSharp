# Tiny World Language (TWL)

Authors dynamic worlds as a fact graph. Everything is an **entity**; entities link by labeled, **set-valued relations**. TWL defines only a triplet grammar + entity/relation model. Computation, presentation, and inference are delegated verbatim:

| concern | spec |
|---|---|
| computation | CEL — `github.com/google/cel-spec` |
| presentation | Mustache — `github.com/mustache/spec` |
| model + inference | RDF 1.1 / RDFS / OWL |

**Borrowed, not authored.** No operators, control flow, or datatypes of TWL's own; each sublanguage is embedded whole behind one delimiter. The only *configured* surface is the [Embedding profile](#embedding-profile-the-entire-configured-boundary); the only *authored* rule is [Precedence](#precedence). Everything else is cited, not trusted.

## Safety (untrusted UGC)

Non-Turing-complete, terminating, side-effect-free. Fixed budget — max traversal depth, derived-recursion depth (cycle-detected), result length, output size; exceeding = error. Interpolated values are inserted as text, never re-parsed (no tag injection). Regex is RE2 (linear).

## Conformance (honest scope)

- **CEL** — the pinned standard environment passes unmodified.
- **Mustache** — interpolation, sections, inverted sections, comments, set-delimiter pass unmodified. **Excluded by profile:** lambdas (host code), partials (recursion), and parent-scope fallback / dotted names — total relations bind every name to the context entity and never climb scope. A stated divergence: the price of total relations.

## Statements

```
// line, /* block */ comments
SUBJECT RELATION VALUE ;     // stored fact
TYPE    RELATION -> CEL ;    // derived fact
```

`VALUE` is one of:

| kind | form |
|---|---|
| entity ref | bare id — `Mac`, `McFlyFam` |
| number | `123`, `1.5` (CEL int64 / double) |
| boolean | `true`, `false` |
| string | `"..."` (a Mustache template) |

`null` is a CEL result only, never a stored value (keeps the RDF round-trip lossless).

- **Identifier** = CEL id `[A-Za-z_][A-Za-z0-9_]*`, not a reserved word → usable inside CEL unchanged. Human names are `name` facts, not ids.
- **Reserved**: `instanceof extends sameas true false null self now instances sortBy`.

## Entities & relations

- Every relation is **total, set-valued**: `self.r` = the set of values, `[]` if none. Reading never errors; `self.r[0]` on an empty set errors (plain CEL).
- A set is unordered, idempotent, order-independent → one graph.
- **Open-world**: any id denotes an entity, no declaration; unknown → all relations `[]`. A typo is a new entity (lint outside the language).
- Sequence ops take a **canonical order** (see profile) — deterministic across merge + RDF round-trip. For authoring order, model it as a fact and `sortBy`.

## Ontology & inference

Three built-in relations alias RDF, carry its entailment, **stored-only (not derivable)** — so typing/precedence cannot loop:

| TWL | RDF | entailment |
|---|---|---|
| `instanceof` | rdf:type | `X instanceof A` ∧ `A extends B` ⇒ `X instanceof B` |
| `extends` | rdfs:subClassOf | (transitive) |
| `sameas` | owl:sameAs | `A sameas B` ⇒ one entity |

`==` is taken after `sameas` merge. Other RDF/OWL vocab may be stored but entails nothing. Every TWL graph is RDF (lossless out); TWL omits rich datatypes, arbitrary IRIs, blank nodes.

## Computation (CEL)

Text after `->` is CEL, verbatim. For each `self instanceof TYPE`, `RELATION` = the CEL result. Lazy. Context (no new syntax):

| name | meaning |
|---|---|
| `self` | the current entity |
| `now` | timestamp, frozen per top-level eval, UTC components |
| `instances(T)` | entities `instanceof T` (entailed) |
| `sortBy(list, k)` | list ordered by the relation named by string `k` |
| `e.r` | the set of values of relation `r` on entity `e` |

No implicit numeric coercion. Every op is plain CEL:

```
traverse  self.family.map(f, f.name[0])
select    instances(Person).filter(p, McFlyFam in p.family)
order     sortBy(self.children, "bornYear")
scalar    self.bornYear[0]
```

### Precedence

The one rule TWL authors itself (RDF is monotonic — override can't be borrowed):

1. A stored fact overrides a derived one (same subject + relation).
2. Most specific applicable `TYPE` (by `extends`) wins; use `Entity` as the global fallback.
3. Incomparable applicable TYPEs = conflict error; two defs of the same TYPE+RELATION = duplicate error. No silent resolution.

## Presentation (Mustache)

A VALUE-position string is a template, rendered against its owning entity when that relation is presented. (Strings inside a CEL expression are CEL strings.) Each single-id name resolves to a relation (stored/derived); dynamic content is a derived relation referenced by name.

- `{{ r }}` — interpolate, HTML-escaped (Mustache default). Raw text: `{{{ r }}}` / `{{& r }}` (e.g. LLM prompts). List case (Mustache-undefined): TWL renders the set in canonical order — single → its value, empty → `""`.
- `{{# r }}..{{/}}` — standard list section; binds each value as context (`{{.}}` = the value).
- **Footgun:** one bool fact is a one-element set, so `{{# flag }}` renders for `true` *and* `false`. Guard truthiness with a derived relation.

## Embedding profile (the entire configured boundary)

- **CEL** — pinned standard env + macros `has all exists exists_one map filter`, no extensions. Entity is a host type with *total* field access (not native select). Host adds vars `self now`, funcs `instances sortBy`.
- **Ids / strings** — host names reserved as ids (no shadowing). A VALUE string token uses CEL string-literal grammar (escapes, `r"..."`, multiline `"""..."""`); its decoded value is the verbatim Mustache template.
- **Determinism** — canonical order by kind then value: `bool < number < string < entity`; within kind: `false<true`, numeric, code-point, by canonical id. `sameas` canonical id = lex-least in the class. `sortBy` key = canonical-first value; empty/mixed-type = error. Literal→text: bool→`true`/`false`, int→decimal, double→shortest round-trip, null→`""`.
- **RDF** — entities → IRIs under a host-fixed base; literals → xsd datatypes.

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

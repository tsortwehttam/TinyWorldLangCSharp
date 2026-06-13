# TWL (minimal, normative)

Condensed for prompt context. Full rationale + example: `spec.md`.

A fact graph. Everything is an **entity**; entities link by labeled, **set-valued relations**. TWL is only a triplet grammar + entity/relation model. Computation = CEL, presentation = Mustache, model/inference = RDF/RDFS/OWL — all embedded verbatim. Only the [Profile](#profile) is configured; only [Precedence](#precedence) is authored.

**Safety:** non-Turing-complete, terminating, side-effect-free. Budget caps traversal depth, derived-recursion depth (cycle-detected), result length, output size; exceed = error. Interpolated values are text, never re-parsed. Regex = RE2.

## Grammar

```
// line, /* block */ comments
SUBJECT RELATION VALUE ;     // stored fact
TYPE    RELATION -> CEL ;    // derived fact
```

`VALUE`: entity ref (bare id) | number `123`/`1.5` (CEL int64/double) | `true`/`false` | string `"..."` (a Mustache template). `null` is a CEL result only, never stored.

**Identifier** = CEL id `[A-Za-z_][A-Za-z0-9_]*`, not reserved. **Reserved**: `instanceof extends sameas true false null self now instances sortBy`.

## Relations

- Total, set-valued: `self.r` = the set, `[]` if none. Reading never errors; `self.r[0]` on empty errors (plain CEL).
- Unordered, idempotent, order-independent.
- Open-world: any id is an entity, no declaration; unknown → all `[]`.
- Sequence ops use canonical order (Profile); deterministic across merge + RDF round-trip. Authoring order = a fact, then `sortBy`.

## Ontology (stored-only, not derivable)

| TWL | RDF | entailment |
|---|---|---|
| `instanceof` | rdf:type | `X instanceof A` ∧ `A extends B` ⇒ `X instanceof B` |
| `extends` | rdfs:subClassOf | transitive |
| `sameas` | owl:sameAs | one entity |

`==` after `sameas` merge. Other RDF/OWL vocab stored but entails nothing. Every TWL graph is RDF (lossless out).

## Computation (CEL after `->`, verbatim)

For each `self instanceof TYPE`, `RELATION` = CEL result; lazy. Context: `self` (current entity) · `now` (timestamp, frozen per top-level eval, UTC) · `instances(T)` (entities `instanceof T`, entailed) · `sortBy(list, k)` (ordered by relation named `k`) · `e.r` (set of values of `r` on `e`). No implicit numeric coercion.

```
self.family.map(f, f.name[0])                                   // traverse
instances(Person).filter(p, McFlyFam in p.family)              // select
sortBy(self.children, "bornYear")                              // order
self.bornYear[0]                                               // scalar
```

### Precedence (the only authored rule)

1. Stored > derived (same subject+relation).
2. Most specific applicable `TYPE` (by `extends`) wins; `Entity` = global fallback.
3. Incomparable applicable TYPEs = conflict error; two defs of same TYPE+RELATION = duplicate error.

## Presentation (Mustache)

A VALUE-position string is a template rendered against its owning entity when presented (CEL-internal strings are not templates). Names resolve to relations (stored/derived); dynamic content = a derived relation by name.

- `{{ r }}` interpolate, HTML-escaped; raw via `{{{ r }}}` / `{{& r }}`. List case: render set in canonical order — single → its value, empty → `""`.
- `{{# r }}..{{/}}` standard list section, binds each value (`{{.}}` = value).
- One bool fact is a one-element set → `{{# flag }}` renders for true *and* false; guard via a derived relation.

Conformance: CEL standard env passes unmodified. Mustache interpolation/sections/inverted/comments/delimiters pass; lambdas, partials, and parent-scope fallback / dotted names are excluded (total relations never climb scope).

## Profile

- **CEL**: pinned standard env + macros `has all exists exists_one map filter`, no extensions. Entity = host type with *total* field access. Host vars `self now`; funcs `instances sortBy`.
- **Ids/strings**: host names reserved as ids. VALUE string = CEL string-literal grammar (`\` escapes, `r"..."`, `"""..."""`); decoded value is the verbatim template.
- **Determinism**: canonical order by kind then value — `bool < number < string < entity`; within: `false<true`, numeric, code-point, by canonical id. `sameas` canonical id = lex-least. `sortBy` key = canonical-first; empty/mixed-type = error. Literal→text: bool→`true`/`false`, int→decimal, double→shortest round-trip, null→`""`.
- **RDF**: entities → IRIs under a host-fixed base; literals → xsd datatypes.

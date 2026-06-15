# Tiny World Language — Authoring Guide

Tiny World Language is a language for building simulations and emergent games. TWL is not a renderer, a game engine, or a content pack. It is a source of truth for a simulation's world, defining what that world consists of at any given moment.

A TWL world is a set of facts. Each fact links a subject entity to a value through a named relation. Some facts are written down directly; others are computed from the rest of the world.

## Statements

A world is a list of statements, each ending in `;`. There are two kinds:

```
// line comment
/* block comment */
SUBJECT RELATION VALUE; // a stored fact
TYPE RELATION = EXPR; // a computed fact
```

- A stored fact records something directly: `Marty bornYear 1968;`
- A computed fact gives a rule for a relation, applied to every entity of a type: `Person age = now.getFullYear() - one(self.bornYear);`

## Values

A `VALUE` is one of:

- entity — a bare id: `Mac`, `McFlyFam`
- number — `123`, `1.5`. A literal with no decimal point is an integer (`123`, `1968`); one with a decimal point is a double (`1.5`, `1.0`). The type is syntactic and fixed: `1968` is always an integer, `1968.0` always a double. Arithmetic never mixes the two (see [Computed facts](#computed-facts))
- boolean — `true`, `false`
- string — `"..."` — a Mustache template (see [Templates](#templates)), rendered against the owning entity when read; may span multiple lines. Text with no `{{` tags renders to itself, so an ordinary value like `"Marty"` is just its literal text

There is one string type and it is always a template. This is safe because every stored string is author content — untrusted player text lives in the session event stream, never in facts (see [The evaluation model](#the-evaluation-model)), so it is never parsed as world source and never rendered as a template.

A value runs from its opening `"` to the next unescaped `"`, so a `;`, newline, or `{{` inside is ordinary text and does not end the value or the statement. Backslash escapes: `\"` (literal `"`), `\{` (literal `{`, which cannot begin a tag — write `\{\{` for a literal `{{`), `\\` (literal backslash), `\n`, `\t`. A backslash before anything else is an error, so an accidental `\` is caught rather than swallowed.

`null` is never a stored value. It only ever turns up as the result of an expression — for example `one(...)` on an empty set.

## Names

An identifier is a letter or `_` followed by letters, digits, or `_`: `Marty`, `born_year`, `McFlyFam`.

The following words are reserved and cannot be used as entity names: `instanceof extends sameas true false null self now instances sortBy one rand math`.

All but `math` are TWL's own keywords and builtins. `math` is reserved because the math helpers are reached through it as a namespace (`math.greatest(a, b)`).

The other CEL names you use in expressions — the macros `map`, `filter`, `exists`, `all`, `in` and the functions `int`, `double`, `string`, `bool`, `size` — are not reserved: CEL keeps function-call names and plain identifiers separate, so an entity named `size` does not interfere with a `size(...)` call. (Still, authors should avoid naming entities after them so expressions stay readable.)

## Entities and relations

- Any id is an entity. You never declare entities — just mention them. An entity you have never mentioned still exists; every relation on it is simply the empty set.
- Every relation holds a set of values. `Marty family McFlyFam;` adds `McFlyFam` to Marty's `family` set; another `Marty family ... ;` adds more.
- Reading a relation always gives a set. A relation with nothing in it is the empty set `[]`, and reading it is never an error.
- Sets are unordered and deduplicated. Stating the same fact twice changes nothing.
- To read a single value, use `one(r)`: it gives the relation's single value, or `null` when the set is empty — for example `one(self.bornYear)`. To supply a default instead of `null`, pass it as a second argument: `one(self.bornYear, 1900)`. (Indexing a set directly, like `self.bornYear[0]`, errors on an empty set, so prefer `one`.)
  - `one` never errors on too many values. If the set holds more than one value it silently returns the canonical-first (see [Order within a set](#order-within-a-set)) — an unexpected second value, a typo, or a duplicated fact passes with no signal. Use `one` only where you expect at most one value; to enforce "exactly one", check `size(r)` yourself.

## Order within a set

A set has no order of its own, but some operations need one: `one`, `sortBy`, and rendering a multi-value relation in a template. For these, TWL always uses the same canonical order, so results are reproducible. Values are grouped by kind — booleans, then numbers, then strings, then entities — and ordered within each kind: `false` before `true`, numbers by value, strings by Unicode code point, entities by name. A string sorts by its unrendered source text, not its rendered output. So `one(s)` returns the first value in this order, and `sortBy(list, "rel")` orders `list` by each element's value for `rel`. Note code-point order is not dictionary order: every uppercase letter sorts before every lowercase one (`"Zoe"` before `"adam"`), and digits before letters — so sorting names is case-sensitive.

## Types and identity

Three built-in relations describe how the world is organized:

- `X instanceof T;` — X is a T.
- `A extends B;` — every A is also a B (subtypes).
- `A sameas B;` — A and B are the same entity.

These combine automatically. If `Marty instanceof Person` and `Person extends Entity`, then Marty is also an Entity.

`extends` must form no cycles: `A extends B; B extends A;` (or any longer loop) is a load-time error.

A `sameas` collapses two entities into one: they share all their facts, `==` treats them as equal, and the merged entity takes a single canonical name (the alphabetically-least of the names involved) — the name used for ordering. (A `sameas` loop is harmless — it just declares everything in the loop one entity — and collapses to a single entity rather than erroring.) Randomness keys on the entity's identity rather than its name, so a `sameas` merge leaves draws stable (see [Randomness](#randomness)).

## Computed facts

After `=` you write an expression in CEL (Common Expression Language). The rule runs for every entity that is an instance of the named type — including instances of its subtypes, since `instanceof` is transitive through `extends` (if `Wizard extends Person`, every `Wizard` is a `Person`) — and is evaluated only when its value is needed. Inside the expression you have:

- `self` — the current entity
- `now` — the current time (UTC), frozen for the whole evaluation so every rule sees the same instant. Its date-component methods (`getFullYear()`, `getMonth()`, …) read in UTC unless you pass a timezone, so a sim keyed on local calendar dates can be off by a day near midnight or a year boundary.
- `instances(T)` — the set of all entities that are instances of `T`, directly or through a subtype
- `sortBy(list, "rel")` — `list` ordered by the relation named `rel`. Each element is keyed by `one(element.rel)`: an element whose `rel` is empty keys as `null` and sorts before all real values; one with several values keys on its canonical-first
- `one(s)` — the single value of set `s`, or `null` when empty
- `one(s, fallback)` — the single value of set `s`, or `fallback` when empty
- `e.rel` — the set of values of relation `rel` on entity `e`
- `rand(key)` — a stable random number in `[0, 1)` for `key` (see [Randomness](#randomness))

Common expression forms: `list.map(x, expr)`, `list.filter(x, cond)`, `x in list`, `list.exists(x, cond)`, `list.all(x, cond)`, `size(list)`, `cond ? a : b`.

Math helpers are available under `math.`: `math.greatest(a, b)` (max), `math.least(a, b)` (min), `math.abs`, `math.sign`, `math.floor`, `math.ceil`, `math.round`, `math.trunc`, `math.sqrt`, and more. Clamp a value with `math.least(math.greatest(x, lo), hi)`.

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

## Evaluation model

TWL is a pure function: given the world, the session event stream, and a small per-turn environment, it computes values — `eval(world, session, env) → values`. The same inputs always produce the same values. All change over the course of a game happens outside TWL, in the host: the host owns the facts and the event stream and asks TWL what they imply.

Inputs come in two kinds plus an environment:

- World — the authored cartridge: types, `extends`, computed rules, and static facts. Immutable for the whole game; this is what the author ships. All facts are author-authored.
- Session — the event stream the host accumulates during play: an append-only, ordered list of events. Growing, but only the host writes it — TWL never does. Events are *data*, not facts: they live in their own `session` namespace and are read through CEL, never mixed into the fact graph (see [Events](#events)).
- Env — the per-evaluation environment: `now`, the `rand` seed (you cannot read or set it from inside the world), a turn counter, and whatever else the host passes for this one query. Not facts; reached through their own keywords and builtins. Ephemeral; gone after the call.

This split is deliberate. Facts are trusted author content; the session stream is where untrusted, host-supplied content (including raw player text) lives. Because the two never merge, player text is never parsed as world source and never rendered as a template — provenance is structural, not a convention each host must remember. A "player action" is just the latest event the host appended; there is no separate action layer.

A rule that reads `now`, `rand`, or the session stream only works if the host provides that piece — ask the developer what their cartridge supplies.

This reproducibility holds for a given engine build. Floating-point results are bit-for-bit stable for ordinary arithmetic and the exactly-rounded helpers (`math.floor`, `math.ceil`, `math.round`, `math.trunc`, `math.abs`, `math.sign`, `math.sqrt`), but the transcendental helpers (and any host-registered functions) may differ in their last bits across platforms or library versions — don't rely on cross-machine bit-identical output from those.

Because TWL holds no state, a server can be stateless: cache the world (it never changes), persist the session event stream between requests, and supply the environment per request. To resume a game, reload world and session and evaluate. There is nothing else to restore.

## Events

The session is an append-only stream of events. TWL does not define what an event is; its shape belongs to the host, per game. An event is a record with whatever fields the host gives it (`actor`, `verb`, `target`, `value`, `observers`, …); a field holds a scalar or an entity reference.

Events are exposed under the `session` namespace as named lists and read with ordinary CEL collection ops:

```
Person threats = session.events.filter(e, e.target == self).map(e, e.actor);
Person insultCount = size(session.events.filter(e, e.target == self && e.verb == "insult"));
Person lastSpoke = session.events.filter(e, e.actor == self);
```

Note that the host may append in occurrence order, so events are assumed to be chronological. Also, events are not deduplicated. Two identical actions are two distinct events. TWL is pure, so "react to an event" means "recompute": each evaluation reads the current stream and derives fresh values. The world never mutates itself; the host appends events to the session and may persist computed results however it likes.

## Randomness

`rand(key)` returns a number in `[0, 1)`. It is stable: the same key always gives the same draw, so worlds stay reproducible. The seed comes from the game each evaluation and cannot be read or set from inside the world.

The key can be any stable value — string, number, entity, or a list — and is hashed with the seed; equal keys give equal draws, so pass a key unique to each draw. Usually a list of the entity plus a label, `[self, "label"]`: the list keeps parts distinct (gluing strings collides — `"a" + "bc"` equals `"ab" + "c"`), and putting the entity itself in the key (not its name) keeps the draw stable across a `sameas` merge. A plain `rand("daily")` works for draws not tied to an entity.

```
Chest gold = int(rand([self, "gold"]) * 100.0); // 0..99
Die roll = int(rand([self, "roll"]) * 6.0) + 1; // 1..6
```

## Tiebreaks

A relation may have a stored fact and one or more computed rules. The value is chosen like this:

1. A stored fact always wins over a computed one. If a relation has any stored fact on an entity, every computed rule for that relation is suppressed for that entity — stored and computed never blend, so the result is exactly the stored set (one fact does not merge into or extend a computed set). (Stored facts are author content only; per-turn input arrives in the session stream, not as a fact, so it never participates in this tiebreak.)
2. Otherwise the rule on the most specific type wins (a subtype beats its parent). When a type is reachable by several inheritance paths, its specificity is the length of the *shortest* path.
3. If two rules are equally specific, or two unrelated types both apply (including a diamond where two sibling types each carry a rule), it is an error — resolve it with an explicit rule on the more specific type.

## Templates

Every string value (`"..."`) is a Mustache template, rendered against the entity that owns it. Names inside refer to that entity's relations. A value with no `{{` tags renders to its literal text, so a plain fact like `Marty firstName "Marty"` just yields `Marty`; to write a literal `{{`, escape it as `\{\{` (see [Values](#values)).

- `{{rel}}` — insert the value, escaped. TWL itself is a source of truth, not a renderer, so it does not assume HTML: the escaping is whatever escaper the host configures for its output (HTML for a web target, none for plain text or JSON, etc.).
- `{{{rel}}}` or `{{& rel}}` — insert the value raw, never escaped, whatever the host escaper is.
- `{{# rel}} ... {{/}}` — repeat the block for each value in the set (in canonical order); inside, `{{.}}` is the current value and names resolve on it.
- A relation with one value renders that value; an empty relation renders nothing. A relation with more than one value renders only its canonical-first value (see [Order within a set](#order-within-a-set)) — the rest are dropped silently, so use a `{{# rel}}` block to render them all.

A single boolean fact is still a one-value set, so `{{# flag}}` renders for both `true` and `false`. To branch on a condition, make a computed relation that is empty when the condition is false.

Names in a template resolve to the owning entity's relations. To render event-derived text, first compute a relation from the stream (`Person lastLine = session.events.filter(e, e.actor == self).map(e, e.text);`) and render that. The value is inserted as text, not re-parsed — a `{{...}}` sitting inside event text is inert, so player text cannot inject template syntax. Two cautions when that text is untrusted: `{{rel}}` escapes it with the host escaper, but `{{{rel}}}` / `{{& rel}}` bypasses the escaper entirely, so emitting raw player text is the host's escaping responsibility.

## Example

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

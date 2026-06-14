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
- number — `123`, `1.5`. A literal with no decimal point is an **integer** (`123`, `1968`); one with a decimal point is a **double** (`1.5`, `1.0`). The type is syntactic and fixed: `1968` is always an integer, `1968.0` always a double. This matters because arithmetic never mixes the two (see [Computed facts](#computed-facts))
- boolean — `true`, `false`
- string — `"..."` — literal text, taken exactly as written; may span multiple lines
- template — `` `...` `` — a Mustache template (see [Templates](#templates)); rendered against the owning entity when read, may span multiple lines

A double-quoted string is **never** interpreted as a template, so `{{` and `}}` in it (or in any player-supplied value) are just characters. Only a backtick template is rendered. This keeps untrusted text inert by default — a player whose name is `{{family}}` cannot make a template do anything.

Both forms use the same escapes. A value runs from its opening delimiter to the next *unescaped* matching delimiter, so a `;`, newline, or the other quote character inside is ordinary text and does not end the value or the statement. Backslash escapes: `\"` (literal `"`), `` \` `` (literal backtick), `\\` (literal backslash), `\n`, `\t`. A backslash before anything else is an error, so an accidental `\` is caught rather than swallowed. There is no escape for `{{` *inside a template* — to render literal braces, put them in a `"..."` value and interpolate it.

`null` is never a stored value. It only ever turns up as the result of an expression — for example `one(...)` on an empty set.

## Names

An identifier is a letter or `_` followed by letters, digits, or `_`: `Marty`, `born_year`, `McFlyFam`.

The following words are reserved and cannot be used as entity names: `instanceof extends sameas true false null self now instances sortBy one rand math`.

All but `math` are TWL's own keywords and builtins. `math` is reserved because the math helpers are reached through it as a namespace (`math.greatest(a, b)`); an entity named `math` would be read as that entity instead of the namespace, breaking those calls.

The other CEL names you use in expressions — the macros `map`, `filter`, `exists`, `all`, `in` and the functions `int`, `double`, `string`, `bool`, `size` — are **not** reserved: CEL keeps function-call names and plain identifiers separate, so an entity named `size` does not interfere with a `size(...)` call. Still, avoid naming entities after them so expressions stay readable.

## Entities and relations

- Any id is an entity. You never declare entities — just mention them. An entity you have never mentioned still exists; every relation on it is simply the empty set.
- Every relation holds a set of values. `Marty family McFlyFam;` adds `McFlyFam` to Marty's `family` set; another `Marty family ... ;` adds more.
- Reading a relation always gives a set. A relation with nothing in it is the empty set `[]`, and reading it is never an error.
- Sets are unordered and deduplicated. Stating the same fact twice changes nothing.
- To read a single value, use `one(r)`: it gives the relation's single value, or `null` when the set is empty — for example `one(self.bornYear)`. To supply a default instead of `null`, pass it as a second argument: `one(self.bornYear, 1900)`. (Indexing a set directly, like `self.bornYear[0]`, errors on an empty set, so prefer `one`.)
  - **`one` never errors on too many values.** If the set holds more than one value it silently returns the canonical-first (see [Order within a set](#order-within-a-set)) — an unexpected second value, a typo, or a duplicated fact passes with no signal. Use `one` only where you expect at most one value; to enforce "exactly one", check `size(r)` yourself.

## Order within a set

A set has no order of its own, but some operations need one: `one`, `sortBy`, and rendering a multi-value relation in a template. For these, TWL always uses the same canonical order, so results are reproducible. Values are grouped by kind — booleans, then numbers, then strings, then entities — and ordered within each kind: `false` before `true`, numbers by value, strings by Unicode code point, entities by name. A stored template sorts as a string, by its unrendered source text. So `one(s)` returns the first value in this order, and `sortBy(list, "rel")` orders `list` by each element's value for `rel`. Note code-point order is not dictionary order: every uppercase letter sorts before every lowercase one (`"Zoe"` before `"adam"`), and digits before letters — so sorting names is case-sensitive.

## Types and identity

Three built-in relations describe how the world is organized:

- `X instanceof T;` — X is a T.
- `A extends B;` — every A is also a B (subtypes).
- `A sameas B;` — A and B are the same entity.

These combine automatically. If `Marty instanceof Person` and `Person extends Entity`, then Marty is also an Entity.

`extends` must form no cycles: `A extends B; B extends A;` (or any longer loop) is a load-time error. (A `sameas` loop is harmless — it just declares everything in the loop one entity — and collapses to a single entity rather than erroring.)

A `sameas` collapses two entities into one: they share all their facts, `==` treats them as equal, and the merged entity takes a single canonical name (the alphabetically-least of the names involved) — the name used for ordering. Randomness keys on the entity's identity rather than its name, so a `sameas` merge leaves draws stable (see [Randomness](#randomness)).

## Computed facts

After `=` you write an expression in CEL (Common Expression Language). The rule runs for every entity that is an instance of the named type — including instances of its subtypes, since `instanceof` is transitive through `extends` (if `Wizard extends Person`, every `Wizard` is a `Person`) — and is evaluated only when its value is needed. Inside the expression you have:

- `self` — the current entity
- `now` — the current time (UTC), frozen for the whole evaluation so every rule sees the same instant. Its date-component methods (`getFullYear()`, `getMonth()`, …) read in UTC unless you pass a timezone, so a sim keyed on local calendar dates can be off by a day near midnight or a year boundary.
- `instances(T)` — the set of all entities that are instances of `T`, directly or through a subtype
- `sortBy(list, "rel")` — `list` ordered by the relation named `rel`. Each element is keyed by `one(element.rel)`: an element whose `rel` is empty keys as `null` and sorts before all real values; one with several values keys on its canonical-first
- `one(s)` — the single value of set `s`, or `null` when empty (and the canonical-first value, with no error, if `s` has more than one — see [Reading a single value](#entities-and-relations))
- `one(s, fallback)` — the single value of set `s`, or `fallback` when empty
- `e.rel` — the set of values of relation `rel` on entity `e`
- `rand(key)` — a stable random number in `[0, 1)` for `key` (see [Randomness](#randomness))

Common expression forms: `list.map(x, expr)`, `list.filter(x, cond)`, `x in list`, `list.exists(x, cond)`, `list.all(x, cond)`, `size(list)`, `cond ? a : b`.

Math helpers are available under `math.`: `math.greatest(a, b)` (max), `math.least(a, b)` (min), `math.abs`, `math.sign`, `math.floor`, `math.ceil`, `math.round`, `math.trunc`, `math.sqrt`, and more. Clamp a value with `math.least(math.greatest(x, lo), hi)`.

`instances`, `sortBy`, `one`, and `rand` are TWL's own builtins; `self`, `now`, and `e.rel` are language forms. The expression forms and `math.*` helpers come from CEL itself — a host engine may register further functions of its own, so check the developer's documentation for any extras.

**A rule's result becomes the relation's set.** Every relation holds a set, so the expression's value is coerced to one: a list or set result contributes its elements (flattened one level — nested lists are not allowed); a single scalar (number, boolean, string, entity) becomes a one-element set; and `null` — including any `null` sitting inside a returned list — contributes nothing, so a rule that evaluates to `null` leaves the relation empty (`null` is never stored, per [Values](#values)). This is why `one()` on a rule that produced `null` gives `null` straight back: the set is empty.

Numbers are not converted automatically: do not mix integers and decimals in one *arithmetic* operation — `1 + 1.0` errors. Convert explicitly with `int(...)` or `double(...)`. Comparison is the exception: integers and doubles compare by mathematical value, so `1 == 1.0` is `true` and they sort together as one numeric kind (see [Order within a set](#order-within-a-set)).

**Missing values propagate as errors, not silently.** `one(s)` on an empty set is `null`, and `null` is not a usable operand: arithmetic like `now.getFullYear() - one(self.bornYear)` errors for any entity whose `bornYear` is missing, and a member access like `one(self.family).name` errors when `family` is empty (`null.name` — `null` is not an entity). The error is per-entity, raised only when that rule is evaluated, so it can hide until a specific entity is read. Guard any relation that may be absent with a fallback: `one(self.bornYear, 1900)`, `one(one(self.family).name, "")`.

**Comparing values of different kinds is never equal**, not an error: a number and a string, a boolean and an entity — any two different kinds — are `!=`, never `==`. So a mistyped comparison fails quietly as `false` rather than flagging the kind mismatch. (Integers and doubles are *not* different kinds for this purpose — they compare by value, as above.)

Example expressions:

```
Person age = now.getFullYear() - one(self.bornYear);
Person lastName = one(one(self.family).name);
Person enemies = instances(Person).filter(p, self.dislikes.exists(fam, fam in p.family) && self != p);
Person eldest = one(sortBy(self.children, "bornYear"));
```

## The evaluation model

TWL never changes anything. It is a pure function: given a set of facts and a small per-turn environment, it computes values — `eval(facts, env) → values`. The same facts and environment always produce the same values. All change over the course of a game happens outside TWL, in the host: the host decides what facts exist and asks TWL what they imply.

Facts come in three layers, by lifetime:

- **World** — the authored cartridge: types, `extends`, computed rules, and static facts. Immutable for the whole game; this is what the author ships.
- **Session** — facts the host has accumulated during play: the running event history, plus any computed result the host chose to write back as a new fact. Mutable and growing, but only the host writes it — TWL never does.
- **Turn** — the environment for a single evaluation: `now`, the `rand` seed (you cannot read or set it from inside the world), a turn counter, and whatever else the host passes for this one query. Ephemeral; gone after the call.

Each evaluation runs against `world ∪ session`, with the turn environment supplied alongside. Per-turn inputs — a player's action, a turn counter, a sensor reading — arrive as ordinary facts and are read like any other relation (`self.action`); they live in the turn layer, so they vanish after the call unless the host also writes them into the session. `now`, `rand`, and its seed are the only non-fact turn values, reached through their own keyword and builtin. A rule that reads any of these only works if the host provides that piece — ask the developer what their cartridge supplies.

This reproducibility holds for a given engine build. Floating-point results are bit-for-bit stable for ordinary arithmetic and the exactly-rounded helpers (`math.floor`, `math.ceil`, `math.round`, `math.trunc`, `math.abs`, `math.sign`, `math.sqrt`), but the transcendental helpers (and any host-registered functions) may differ in their last bits across platforms or library versions — don't rely on cross-machine bit-identical output from those.

### Stateless hosting

Because TWL holds no state, a server can be stateless: cache the world (it never changes), persist the session fact-set between requests (facts are just triples — serialize them), and supply the turn environment per request. To resume a game, reload `world ∪ session` and evaluate. There is nothing else to restore.

### The host is the only writer

The world never mutates itself; the host appends facts to the session. This is the trust boundary. Player or LLM text is never parsed as TWL — it only ever enters the world as a **value** the host places into a fact it constructs. A double-quoted string stays inert (it is never a template — see [Values](#values)), so stored player text is data, never code. The one place this guarantee can be undone is a host that lets untrusted text choose a fact's *subject or relation* rather than only its *value*; keep player text in value position.

### Events and history

A common shape — a conversation sim, an action log — is an append-only stream of events. TWL does not define what an event is; its shape belongs to the host, per game. An event is just an entity with whatever relations the host gives it (`actor`, `verb`, `target`, `value`, `observers`, …), and the history is a relation holding the set of those event entities. The host appends a new event after each turn (for an LLM game, often by wrapping the model's output), and reads back a computed/template relation to assemble the next prompt.

Two things to know:

- **Order is not chronology.** Canonical order ([Order within a set](#order-within-a-set)) sorts events by id, not by time. For chronological history the host stamps each event with a monotonic sequence number (or timestamp) and rules read it in order: `sortBy(self.events, "seq")`. The host owns that counter.
- **Visibility is open, and composes.** Which entity sees which events is up to the cartridge and host. The host may attach the audience explicitly (e.g. an `observers` relation on each event), and/or the author may compute a per-entity view in the cartridge — `Character events = instances(Event).filter(e, self in e.observers);`. Either works; use whichever, or both.

## Randomness

`rand(key)` returns a number in `[0, 1)`. It is stable: the same key always gives the same draw, so worlds stay reproducible. The seed comes from the game each evaluation and cannot be read or set from inside the world, so player text can never steer the randomness.

The key can be any stable value — string, number, entity, or a list — and is hashed with the seed; equal keys give equal draws, so pass a key unique to each draw. Usually a list of the entity plus a label, `[self, "label"]`: the list keeps parts distinct (gluing strings collides — `"a" + "bc"` equals `"ab" + "c"`), and putting the entity itself in the key (not its name) keeps the draw stable across a `sameas` merge. A plain `rand("daily")` works for draws not tied to an entity.

```
Chest gold = int(rand([self, "gold"]) * 100.0); // 0..99
Die roll = int(rand([self, "roll"]) * 6.0) + 1; // 1..6
```

## Tiebreaks

A relation may have a stored fact and one or more computed rules. The value is chosen like this:

1. A stored fact always wins over a computed one. If a relation has **any** stored fact on an entity, every computed rule for that relation is suppressed for that entity — stored and computed never blend, so the result is exactly the stored set (one fact does not merge into or extend a computed set). (A per-turn input is a stored fact, so it overrides a computed default.)
2. Otherwise the rule on the most specific type wins (a subtype beats its parent). When a type is reachable by several inheritance paths, its specificity is the length of the *shortest* path.
3. If two rules are equally specific, or two unrelated types both apply (including a diamond where two sibling types each carry a rule), it is an error — resolve it with an explicit rule on the more specific type.

## Templates

A backtick value (`` `...` ``) is a Mustache template, rendered against the entity that owns it. Names inside refer to that entity's relations. (A double-quoted `"..."` value is literal text and is never rendered.)

- `{{rel}}` — insert the value, escaped. TWL itself is a source of truth, not a renderer, so it does not assume HTML: the escaping is whatever escaper the host configures for its output (HTML for a web target, none for plain text or JSON, etc.).
- `{{{rel}}}` or `{{& rel}}` — insert the value raw, never escaped, whatever the host escaper is.
- `{{# rel}} ... {{/}}` — repeat the block for each value in the set (in canonical order); inside, `{{.}}` is the current value and names resolve on it.
- A relation with one value renders that value; an empty relation renders nothing. A relation with **more than one** value renders only its canonical-first value (see [Order within a set](#order-within-a-set)) — the rest are dropped silently, so use a `{{# rel}}` block to render them all.

A single boolean fact is still a one-value set, so `{{# flag}}` renders for both `true` and `false`. To branch on a condition, make a computed relation that is empty when the condition is false.

## Full example

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
Biff persona `A bully with disdain for:
{{# enemies}}
  - {{firstName}} {{lastName}}
{{/}}`;
```

# Tiny World Language — Authoring Guide

A TWL world is a set of **facts**. Each fact links a subject entity to a value
through a named relation. Some facts are written down directly; others are
**computed** from the rest of the world. This guide is everything you need to
write worlds — no prior knowledge of the engine is assumed.

## Statements

A world is a list of statements, each ending in `;`. There are two kinds:

```
// line comment
/* block comment */

SUBJECT  RELATION  VALUE ;     // a stored fact
TYPE     RELATION  = EXPR ;   // a computed fact
```

- A **stored fact** records something directly:
  `Marty bornYear 1968 ;`
- A **computed fact** gives a rule for a relation, applied to every entity of a
  type: `Person age = now.getFullYear() - one(self.bornYear) ;`

## Values

A `VALUE` is one of:

- **entity** — a bare id: `Mac`, `McFlyFam`
- **number** — `123`, `1.5`
- **boolean** — `true`, `false`
- **string** — `"..."` — a text template (see [Templates](#templates))

## Names

An identifier is a letter or `_` followed by letters, digits, or `_`:
`Marty`, `born_year`, `McFlyFam`.

These words are reserved and cannot be used as entity names:
`instanceof  extends  sameas  true  false  null  self  now  instances  sortBy  one  rand`.

## Entities and relations

- Any id is an entity. You never declare entities — just mention them.
- Every relation holds a **set** of values. `Marty family McFlyFam ;` adds
  `McFlyFam` to Marty's `family` set; another `Marty family ... ;` adds more.
- Reading a relation always gives a set. A relation with nothing in it is the
  empty set `[]`, and reading it is never an error.
- Sets are unordered and deduplicated. Stating the same fact twice changes
  nothing.
- To read a single value, use `one(r)`: it gives the value of a single-valued
  relation, or `null` when the set is empty — for example `one(self.bornYear)`.
  To supply a default instead of `null`, pass it as a second argument:
  `one(self.bornYear, 1900)`.

## Types and identity

Three built-in relations describe how the world is organized:

- `X instanceof T ;` — X is a T.
- `A extends B ;` — every A is also a B (subtypes).
- `A sameas B ;` — A and B are the same entity.

These combine automatically. If `Marty instanceof Person` and
`Person extends Entity`, then Marty is also an Entity.

```
Entity   name "Entity" ;
Person   extends Entity ;
Family   extends Entity ;
McFlyFam instanceof Family ;
Marty    instanceof Person ;
```

## Computed facts

After `=` you write an expression in **CEL** (Common Expression Language). The
rule runs for every entity that is an instance of the named type. Inside the
expression you have:

- `self` — the current entity
- `now` — the current time (UTC)
- `instances(T)` — the set of all entities that are instances of `T`
- `sortBy(list, "rel")` — `list` ordered by the relation named `rel`
- `one(s)` — the single value of set `s`, or `null` when empty
- `one(s, fallback)` — the single value of set `s`, or `fallback` when empty
- `e.rel` — the set of values of relation `rel` on entity `e`
- `rand(key)` — a stable random number in `[0, 1)` for `key` (see
  [Randomness and per-turn facts](#randomness-and-per-turn-facts))

Common expression forms: `list.map(x, expr)`, `list.filter(x, cond)`,
`x in list`, `list.exists(x, cond)`, `list.all(x, cond)`, `size(list)`,
`cond ? a : b`.

Math helpers are available under `math.`: `math.greatest(a, b)` (max),
`math.least(a, b)` (min), `math.abs`, `math.floor`, `math.ceil`, `math.round`,
`math.sqrt`, and more. Clamp a value with `math.least(math.greatest(x, lo), hi)`.

Numbers are not converted automatically: do not mix integers and decimals in one
operation.

```
Person age      = now.getFullYear() - one(self.bornYear) ;
Person lastName = self.family.map(f, one(f.name)) ;
Person enemies  = instances(Person).filter(p, McFlyFam in p.family && self != p) ;
Person eldest   = sortBy(self.children, "bornYear") ;
```

## Randomness and per-turn facts

Some facts come from the running game rather than being written into the world.
Each turn the game can provide things like the current time (`now`), player or
agent inputs, and a turn counter. Inputs arrive as ordinary facts, so you read
them like any other relation — if the game records an action as `Mac input Jump ;`,
a rule can read `self.input`.

For randomness, use `rand(key)`. It returns a number from `0` up to (but not
including) `1`, and it is **stable**: the same key always gives the same number,
so worlds stay reproducible. Pass a key unique to the draw you want — usually the
entity plus a label — and different labels give independent draws:

```
Chest gold = int(rand([self, "gold"]) * 100.0) ;      // 0..99
Die   roll = int(rand([self, "roll"]) * 6.0) + 1 ;    // 1..6
```

## Which value wins

A relation may have a stored fact and one or more computed rules. The value is
chosen like this:

1. A stored fact always wins over a computed one.
2. Otherwise the rule on the most specific type wins (a subtype beats its
   parent).
3. If two rules are equally specific, or two unrelated types both apply, it is an
   error — resolve it by being explicit.

## Templates

A string value is a **Mustache** template, rendered against the entity that owns
it. Names inside refer to that entity's relations.

- `{{ rel }}` — insert the value (HTML-escaped).
- `{{{ rel }}}` or `{{& rel }}` — insert the value raw, unescaped.
- `{{# rel }} ... {{/}}` — repeat the block for each value in the set; inside,
  `{{.}}` is the current value and names resolve on it.
- A relation with one value renders that value; an empty relation renders
  nothing.

A single boolean fact is still a one-value set, so `{{# flag }}` renders for both
`true` and `false`. To branch on a condition, make a computed relation that is
empty when the condition is false.

```
Biff persona """A bully with disdain for:
{{# enemies }}
  - {{ firstName }} {{ lastName }}
{{/}}""" ;
```

## Full example

```
Entity name "Entity" ;
Person extends Entity ;
Family extends Entity ;

Person age      = now.getFullYear() - one(self.bornYear) ;
Person lastName = self.family.map(f, one(f.name)) ;
Person enemies  = instances(Person).filter(p, McFlyFam in p.family && self != p) ;

McFlyFam instanceof Family ;  McFlyFam name "McFly" ;

Marty instanceof Person ;  Marty firstName "Marty" ;
Marty bornYear 1968 ;      Marty family McFlyFam ;

Lorraine instanceof Person ;  Lorraine firstName "Lorraine" ;
Lorraine family McFlyFam ;

Biff instanceof Person ;
Biff persona """A bully with disdain for:
{{# enemies }}
  - {{ firstName }} {{ lastName }}
{{/}}""" ;
```

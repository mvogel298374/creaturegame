# The Generation Seams — Developer Guide

> **Audience:** new team members / junior engineers working on battle mechanics.
> **Scope:** the interfaces that isolate *generation-specific* rules from the engine —
> what each governs, the Gen 1 domain logic behind them, the patterns that make them
> work, and how you add a new rule or a whole new generation.
>
> **See also:** `STATE_MODEL.md` (the `BattleState` companion to this doc),
> `DESIGN_GUIDES.md` (Gen 1 mechanics + the high-level "Generation Architecture
> Principle"), `DEV_STANDARDS.md` (coding conventions).

---

## 1. TL;DR

Pokémon mechanics change between generations — the type chart, the crit formula, how
long sleep lasts, whether crits ignore stat boosts, the XP formula, and so on. We never
want `if (generation == 1)` scattered through the engine. Instead, **every
generation-variable rule lives behind an interface, and a generation is just a set of
implementations you swap in.**

| Seam | Governs | Gen 1 impl | Default singleton |
|:-----|:--------|:-----------|:------------------|
| `ITypeChart` | The type-effectiveness matrix (Fire beats Grass, etc.) | `Gen1TypeChart` | `Gen1TypeChart.Instance` |
| `IBattleRules` | All other battle math that varies by gen: crit formula & multiplier, damage variance, stat-stage tables, accuracy scale, freeze/thaw, sleep & binding durations, status-damage denominators, stat selection, XP formula | `Gen1BattleRules` | `Gen1BattleRules.Instance` |
| `IStatCalculator` | Stat formulas: HP & other stats, DV/IV randomisation, Stat-Exp/EV scaling | `Gen1StatCalculator` | `Gen1StatCalculator.Instance` |

> **Not a generation seam, but related:** `IRandomSource` (see `STATE_MODEL.md` / the
> RNG section of `TODO.md`) controls *randomness*, not generation. It's orthogonal —
> `Gen1BattleRules` takes an `IRandomSource` so its rolls can be seeded, but the *rules*
> and the *RNG* are independent concerns behind independent seams.

**The one rule to remember:** *never query a generation enum or a `"gen1"` string inside
battle logic.* If something differs between generations, it belongs on one of these
interfaces.

---

## 2. The domain: *why* generations differ

To understand these seams you need to know what actually changed across the games. Here
are the differences the interfaces are built to absorb (Gen 1 → later) — this is the
business logic the abstraction exists for:

### Type chart (`ITypeChart`)
Gen 1 (RBY) had only **15 types** — Dark, Steel, and Fairy didn't exist yet — and several
matchups that were later changed or were outright bugs. `Gen1TypeChart` preserves them
faithfully:

- **Ghost → Psychic = 0× (immune).** A famous *bug*: it was meant to be super-effective,
  but in RBY Psychic is immune to Ghost. We keep the bug because the goal is an accurate
  Gen 1 clone.
- **Poison → Bug = 2×** and **Bug → Poison = 2×** (both became 1× / 0.5× later).
- **Bug → Psychic = 2×** (nerfed to 1× in Gen 2+) — this is *why* Gen 1 Psychic types are
  so dominant: almost nothing hits them hard.
- **Ice → Fire = 1×** (became 0.5× in Gen 2+).

These aren't "balance choices" we made — they're historical facts of the 1996 game, and
the seam is what lets a future `Gen2TypeChart` fix them without touching damage code.

### Battle rules (`IBattleRules`) — the big one
Things that genuinely differ generation to generation, each a member on the interface:

| Concept | Gen 1 | Later gens |
|:--------|:------|:-----------|
| **Crit chance** | `floor(BaseSpeed/2)/256`; high-crit moves ×8. Uses *base* Speed, ignoring stat stages — fast Pokémon crit constantly. | Stage-based (a fixed ladder), unrelated to Speed. |
| **Crit ignores stat stages** | **Yes** — a Gen 1 crit recomputes from raw stats, *discarding* your Swords Dance boosts *and* the Burn penalty (sometimes a crit hits *weaker*!). | No. |
| **Crit multiplier** | 2.0× | 2.0× (Gen 1–5), 1.5× (Gen 6+). |
| **Damage variance** | random `217–255` ÷ 255 | random `85–100` ÷ 100 |
| **Sleep duration** | 1–7 turns | 2–5 turns |
| **Binding (Wrap etc.)** | 2–5 turns, locks the victim out | reworked later |
| **Accuracy scale** | internal `0–255`; a roll of 255 always misses — the **1/256 miss bug** (even 100%-accurate moves whiff ~0.4% of the time) | `0–100`, no bug |
| **Freeze** | permanent until hit by a damaging Fire move that can burn | 20%/turn random thaw; any Fire move thaws |
| **Burn/Poison damage** | 1/16 max HP per turn | 1/8 in Gen 6+ |
| **Special stat** | one combined **Special** stat for both offense and defense | split into Sp. Atk / Sp. Def (Gen 2) |
| **XP on faint** | `floor(baseExp × level / 7)` (wild) | divided by party size; trainer bonus |

The Special-stat split is a good illustration of the seam doing its job. Rather than the
damage formula knowing about generations, `IBattleRules` exposes **`GetOffensiveStat`**
and **`GetDefensiveStat`**:

```csharp
// Gen1BattleRules: Physical → Attack/Defense; Special → the single combined Special stat.
public int GetOffensiveStat(Creature attacker, AttackType moveType) =>
    moveType == AttackType.Physical ? attacker.Attributes.Attack : attacker.Attributes.Special;
```

A `Gen2BattleRules` would return `SpAtk` / `SpDef` here, and `DamageCalculator` — which
just calls `rules.GetOffensiveStat(...)` — wouldn't change at all.

### Stat calculation (`IStatCalculator`)
The formulas that turn base stats + per-individual values into a creature's actual stats:

- **Gen 1** uses **DVs** (Determinant Values, 0–15, four of them; the HP DV is *derived*
  from the low bits of the other four) and **Stat Exp** (0–65535, scaled by
  `floor(sqrt)/4`). Formula:
  `floor(((Base + DV)×2 + floor(sqrt(StatExp))/4) × Level / 100) + Level + 10` for HP,
  `+ 5` for other stats.
- **Gen 3+** replaces these with **IVs** (0–31, six independent) and **EVs** (capped at
  252 per stat, ÷4). A `Gen3StatCalculator` swaps the formula and the `RandomiseDvs`
  logic; nothing that *calls* the calculator changes.

---

## 3. The code shape

### Strategy interfaces + a default singleton
Each seam is a small interface with a Gen 1 implementation exposed as a static
`Instance` singleton, used as the default everywhere:

```csharp
public interface ITypeChart
{
    double GetMultiplier(DamageType attackType, DamageType defenderType);
}

public sealed class Gen1TypeChart : ITypeChart
{
    public static readonly Gen1TypeChart Instance = new();
    // sparse dictionary of non-1.0 matchups; missing entry ⇒ 1.0
}
```

`IBattleRules` is broader (≈20 members) but the same idea; `Gen1BattleRules.Instance` is
the default, and it takes an optional `IRandomSource` so its random rolls are seedable:

```csharp
public Gen1BattleRules(IRandomSource? rng = null) => _rng = rng ?? SystemRandomSource.Instance;
public double RollDamageVariance() => _rng.Next(217, 256) / 255.0;   // Gen 1 spread
```

### How the engine consumes them
The seams are passed *in*, never reached for globally. `Battle` receives an
`IBattleRules` (defaulting to the Gen 1 singleton) and threads it — plus the
`ITypeChart` — into `AttackAction` and `DamageCalculator`:

```csharp
public Battle(Creature player, Creature enemy, ITypeChart typeChart,
              IBattleInput playerInput, IBattleInput enemyInput,
              IReadOnlyList<Attack>? movePool = null,
              IBattleRules? rules = null, /* … */)
{
    _typeChart = typeChart;
    _rules     = rules ?? Gen1BattleRules.Instance;
}
```

`DamageCalculator` shows all three concerns composing without a single generation check:

```csharp
isCrit          = rng.NextDouble() < rules.GetCritChance(attacker, move);   // IBattleRules + IRandomSource
int attackStat  = rules.GetOffensiveStat(attacker, move.AttackType);        // IBattleRules
double typeMult = typeChart.GetMultiplier(move.DamageType, defender.Type1); // ITypeChart
double variance = rules.RollDamageVariance();                               // IBattleRules
```

`Creature` holds its `IStatCalculator` (default `Gen1StatCalculator.Instance`) and calls
it from `CalculateStats()` / `RandomiseDvs`.

---

## 4. Patterns & practices — and *why*

### 4.1 Strategy pattern, one per axis of variation
Each interface is a **Strategy**: a family of interchangeable algorithms behind one
contract. We split them by *axis of change* — type relationships (`ITypeChart`), battle
math (`IBattleRules`), stat formulas (`IStatCalculator`) — because those axes move
independently across generations. A new gen mixes and matches (it might reuse a stat
formula but change the type chart).

### 4.2 "Never branch on the generation" — the invariant
The whole value of the seams evaporates the moment someone writes
`if (gen == Gen.One)` inside `DamageCalculator`. That scatters generation knowledge into
code that should be generation-agnostic, and the next gen becomes a hunt for every such
branch. **The rule, enforced by code review: a generation difference is expressed by
*which implementation is injected*, never by an inspection inside the logic.** If you
find yourself wanting a gen check, that's the signal to add a member to a seam instead.

### 4.3 Singleton default + constructor injection
Each Gen 1 impl is stateless (or effectively so) and exposed as `.Instance`. Callers take
the interface as an optional constructor/parameter defaulting to that singleton:
`rules ?? Gen1BattleRules.Instance`. This gives us the best of both — **zero ceremony for
the common path** (everything is Gen 1 today, so you write nothing) and **full
substitutability** for tests and future gens. It mirrors how `IRandomSource` and
`BattleState` are wired; consistency across the codebase is deliberate.

### 4.4 Sparse data, explicit quirks
`Gen1TypeChart` stores only non-`1.0` matchups and returns `1.0` for anything missing —
compact, and the table reads as "the interesting cases." Every historical quirk/bug is
**commented as such** (e.g. `// Gen 1 bug: Ghost is immune to Psychic`). When you port a
chart, that comment is the spec: it tells the next person the value is intentional, not a
typo.

### 4.5 Interface as living documentation
`IBattleRules`' XML doc comments state the value *for each generation*
("Gen 1: 1–7. Gen 2+: 2–5."). The interface doubles as the spec for what a future
implementer must provide. Keep these comments accurate — they're how a `Gen2BattleRules`
author knows what each member is supposed to return.

### 4.6 Testing against the seams
Because rules are injected, tests substitute their own implementations to remove
randomness or force outcomes — e.g. `AlwaysHitRules` / `AlwaysCritRules` delegate to
`Gen1BattleRules` for everything except the one behaviour under test. (With
`IRandomSource` now available, seeded sources can replace some of these shims — see
`TODO.md`.) The lesson: **a good seam is also a test seam.**

---

## 5. Rules for contributors

### Adding a new *rule* that varies by generation
1. Ask the decision question: **"Is this the same in every generation?"** If yes, it's
   ordinary engine logic — don't put it on a seam. If no, continue.
2. Add a member to **`IBattleRules`** (or `ITypeChart` if it's purely a type
   relationship, or `IStatCalculator` if it's a stat formula). Give it an XML comment
   stating the value per generation.
3. Implement it in `Gen1BattleRules` with the Gen 1 value.
4. Make the caller consume it through the interface — keep the call site
   generation-agnostic. Never add a gen check at the call site.
5. If the new member needs randomness, take the existing `IRandomSource` rather than
   touching `Random.Shared`.

### Adding a whole new generation (e.g. Gen 2)
Implement the three interfaces — `Gen2TypeChart`, `Gen2BattleRules`, `Gen2StatCalculator`
— and select them where battles/creatures are constructed. The engine itself does not
change. Expect the bulk of Gen 2 to be: the Special stat split (touches
`IStatCalculator`, `Attributes`, and `GetOffensiveStat`/`GetDefensiveStat`), the
stage-based crit formula, the corrected type chart, and the `0–100` accuracy scale. See
the multi-generation roadmap in `TODO.md`.

### Where generation logic must **not** go
Not in `DamageCalculator`, `AttackAction`, `StatusResolver`, or `Battle` as a conditional.
Not as a `Generation` enum check anywhere in the engine. Those files read the *current*
rules through the interfaces and stay blind to which generation supplied them.

---

## 6. Worked example: how a single attack pulls from all three seams

A level-50 creature uses a Special move into a Psychic-type target:

1. **`IStatCalculator`** already produced the combatants' stats at creation
   (`CalculateStats` → Gen 1 formula with DVs + Stat Exp).
2. **`IBattleRules.GetCritChance`** rolls the crit using *base* Speed (Gen 1 rule), via
   `IRandomSource` so it's reproducible under a seed.
3. **`IBattleRules.GetOffensiveStat`/`GetDefensiveStat`** return the combined **Special**
   stat for both sides (Gen 1) — a `Gen2BattleRules` would return Sp. Atk / Sp. Def here.
4. **`ITypeChart.GetMultiplier`** supplies the type multiplier, preserving Gen 1 quirks
   (so e.g. a Bug move into Psychic returns the Gen 1 `2.0×`).
5. **`IBattleRules.RollDamageVariance`** applies the Gen 1 `217–255/255` spread.

Five generation-variable decisions, zero generation checks — each resolved by the
injected implementation. That's the seams working.

---

## 7. Future direction

- **Gen 2 sprint:** the first real exercise of these seams. The Special split and the DB
  schema work are scoped in `TODO.md` (Multi-Generation section).
- **Generation selection:** today everything defaults to the Gen 1 singletons. When more
  than one generation exists, a single composition point (where `Battle` and `Creature`
  are built) will choose the implementation set — still no branching inside the engine.
- **Keep the seams honest:** if a future mechanic tempts you toward a generation check,
  that temptation is the design telling you to add an interface member instead.

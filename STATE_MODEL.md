# The Creature State Model — Developer Guide

> **Audience:** new team members / junior engineers picking up the battle engine.
> **Scope:** how a `Creature`'s state is structured, *why* it's split the way it is
> (both the engineering reasoning and the Pokémon Gen 1 domain logic), and the rules
> you must follow when extending it.
>
> **See also:** `DESIGN_GUIDES.md` (Gen 1 mechanics), `DEV_STANDARDS.md` (coding
> conventions), `CLAUDE.md` (architecture overview).

---

## 1. TL;DR

A `Creature` holds two kinds of state:

| Kind | Examples | Lives where | Survives a battle? |
|:-----|:---------|:------------|:-------------------|
| **Permanent** | name, level, base stats, DVs, Stat Exp, XP, current HP | directly on `Creature` | yes — this is what a save file will persist |
| **Transient** | status condition, stat stages, sleep/confusion counters, charging flags | `Creature.Battle` (a `BattleState`) | no — wiped at the start of every fight |

The transient half is a separate object, `BattleState`, held as `Creature.Battle`.
**Resetting between battles is a single line — `Battle = new BattleState()` — not a
field-by-field wipe.** That one design choice is the whole point of this model, and
section 4 explains why.

---

## 2. The domain: what "transient battle state" actually means

In Pokémon, most of what happens to a creature *during* a fight is meaningless once
the fight is over. If a Pokémon is asleep, confused, has its Attack boosted by Swords
Dance, or is mid-charge on Dig, none of that should carry into the *next* battle as if
nothing happened. That volatile, in-combat data is exactly what `BattleState` holds.

Here is every field, what it represents in Gen 1 terms, and why it's transient.

| Field | Type | Gen 1 meaning | Why transient |
|:------|:-----|:--------------|:--------------|
| `Status` | `StatusCondition` | The **major status**: `Sleep`, `Freeze`, `Paralysis`, `Burn`, `Poison`, `BadPoison`. A creature has at most one at a time. | Reset each fight in this sim — see the **important nuance** below. |
| `SleepTurns` | `int` | Turns remaining asleep. Gen 1 sleep lasts **1–7 turns** (`IBattleRules.RollSleepTurns`); decremented at the start of the sleeper's turn until it wakes. | Tied to the current `Sleep` status. |
| `ConfusedTurns` | `int` | Turns remaining **confused**. Confusion is a *volatile* status (separate from the major status); each turn there's a 50% chance the creature hits itself instead of acting. | Volatile by definition — clears on switch/battle end in canonical Gen 1. |
| `ToxicCounter` | `int` (starts at **1**) | **Bad Poison (Toxic)** escalation. Damage is `counter/16` of max HP and the counter climbs every turn (1/16, 2/16, 3/16…), so Toxic ramps up. | Resets to 1 so a fresh battle starts the ramp over. |
| `Stages` | `StatStages` | The **stat-stage modifiers** in `[-6, +6]` for Attack, Defense, Special, Speed, Accuracy, Evasion — set by moves like Swords Dance (+2 Atk) or Growl (−1 Atk). | Gen 1 stat stages exist **only within a battle**. |
| `IsRecharging` | `bool` | The **Hyper Beam** recharge: after a damaging Hyper Beam, the user spends the next turn doing nothing. | Only meaningful between two consecutive turns of one battle. |
| `IsFlinched` | `bool` | Set when a **faster** attacker lands a flinching move (e.g. Bite, Stomp); the victim loses that turn. Cleared as soon as it's checked. | Lasts a fraction of a single turn. |
| `HasLeechSeed` | `bool` | Whether **Leech Seed** is attached — each turn the seeded creature loses HP and the seeder gains it. | Volatile; clears on switch/battle end. |
| `BindingTurnsRemaining` | `int` | **Wrap / Bind / Clamp / Fire Spin** trap counter (Gen 1: **2–5 turns**). While > 0 the creature can't act and takes chip damage. | Trap only exists during the fight. |
| `IsTwoTurnCharging` | `bool` | Whether the creature is **mid two-turn move** (Dig, Fly, Solar Beam, Razor Wind, Skull Bash, Sky Attack). Set on the charge turn, cleared on the release turn. | Spans exactly two turns of one battle. |
| `ChargingMove` | `PokemonAttack?` | *Which* move is being charged, so the release turn knows what to fire without re-asking the player. | Pairs with `IsTwoTurnCharging`. |

### ⚠️ Important nuance: status persistence

In **canonical Gen 1**, the major status conditions (`Sleep`/`Freeze`/`Paralysis`/
`Burn`/`Poison`) *persist out of battle* until cured, while the volatile ones
(confusion, Leech Seed, stat stages, binding, flinch, recharge, two-turn) clear when
the Pokémon switches out or the battle ends.

**This codebase currently treats *all* of them as transient** — `ResetBattleState()`
clears `Status` too, so every battle begins with a clean major status. That's a
deliberate simplification for today's "each encounter is independent" model. Note one
asymmetry that follows from it: **current HP is *not* in `BattleState`**, so damage
*does* carry across battles, but a burn does not.

When the **Game Loop / save system** lands (see `TODO.md`), expect the permanent/
transient boundary to move: major `Status` (and possibly more) will likely be promoted
to the persistent half so a poisoned creature stays poisoned between encounters. The
model is structured to make that an easy change — see section 7.

---

## 3. The code shape

### `BattleState` — the transient bag (`creaturegame/Creature/BattleState.cs`)

```csharp
public sealed class BattleState
{
    public StatusCondition Status { get; set; } = StatusCondition.None;
    public int SleepTurns { get; set; }
    public int ConfusedTurns { get; set; }
    public int ToxicCounter { get; set; } = 1;   // Gen 1 Toxic starts at 1
    public StatStages Stages { get; set; } = new StatStages();
    public bool IsRecharging { get; set; }
    public bool IsFlinched { get; set; }
    public bool HasLeechSeed { get; set; }
    public int BindingTurnsRemaining { get; set; }
    public bool IsTwoTurnCharging { get; set; }
    public PokemonAttack? ChargingMove { get; set; }
}
```

The defaults baked into this class **are** the reset values. There is no separate list
of "what to reset to" — the field initializers are the single source of truth.

### `Creature` — owns a `BattleState`

```csharp
public BattleState Battle { get; set; } = new();

public void ResetBattleState() => Battle = new BattleState();
```

There is **no delegating facade** on `Creature` — every transient field is read and
written directly through `Battle`, e.g. `creature.Battle.Status`, `creature.Battle.Stages`.
That is deliberate: it makes a new per-battle field impossible to add accidentally onto
`Creature` (the only place it can live is `BattleState`). See section 4.3 for the history.

### How a battle uses it

`Battle.StartFightAsync()` resets both sides up front, then the same `Creature`
instances are driven through the turn loop:

```csharp
PlayerCreature.ResetBattleState();   // fresh BattleState
EnemyCreature.ResetBattleState();
// … turn loop reads/writes creature.Battle.Status, creature.Battle.Stages, etc. …
```

---

## 4. Patterns & practices — and *why* we chose them

This is the part to internalize; the field list above you can always look up.

### 4.1 Extract Class
The transient fields were a *cluster of related data* tangled into `Creature`
alongside unrelated permanent identity. Pulling them into their own type
(`BattleState`) is the classic **Extract Class** refactoring. The win: `Creature` now
has a named seam between "who this creature *is*" and "what's happening to it *right
now*," which makes the next point possible.

### 4.2 Reset by replacement, not by mutation  ← the key idea
The old code reset state field by field:

```csharp
// OLD — a hand-maintained list. Add a field above and forget a line here → bug.
public void ResetBattleState()
{
    Status = StatusCondition.None;
    SleepTurns = 0;
    ToxicCounter = 1;
    Stages.Clear();
    // … 7 more lines you must remember to keep in sync …
}
```

This is a **latent bug generator**. Every new transient field is a new line you have
to remember to add here; miss one and stale state silently leaks into the next battle.
That's not hypothetical — the earlier `StatStages` struct→class bug was this exact
shape.

The new version:

```csharp
public void ResetBattleState() => Battle = new BattleState();
```

Throwing away the whole object and making a fresh one means **a forgotten field is
structurally impossible**. The reset can never drift out of sync with the field list,
because it doesn't enumerate the fields at all. *Prefer replacing an object over
mutating it back to defaults whenever "defaults" is the entire goal.*

### 4.3 Delegating-property facade (a deliberate interim step, now removed)
When `BattleState` was first extracted, `Creature` kept thin `get/set` properties that
forwarded to `Battle` (e.g. `public StatusCondition Status { get => Battle.Status; set => Battle.Status = value; }`).
That facade was a deliberate interim step: extracting the class and migrating every call
site at once would have been one large diff, so the facade let the *storage* move while the
*access* (`creature.Status`) stayed unchanged — a small, provably behavior-preserving first
refactor.

It has since been **removed**. The ~220 call sites across the engine and the test suite were
migrated to `creature.Battle.X` in a single compiler-driven pass with the full test suite as
the safety net (identical pass count before and after). The motivation was the one cost the
facade carried: it didn't *force* future fields into `BattleState` — a careless dev could
still add a transient field directly on `Creature`. With the facade gone, that is now
structurally impossible: a per-battle field can only be added to `BattleState`.

This is the textbook arc for a compatibility facade: it buys you a safe, test-verified
refactor up front, and you retire it once the call-site migration is done.

### 4.4 Permanent / transient split for persistence
Separating the two halves isn't just tidy — it's **forward design for the save
system**. When `save.db` arrives, serialization becomes "persist the `Creature`, skip
`Battle`." The boundary you see here *is* the serialization boundary. (And per the
nuance in section 2, that boundary is the natural place to later promote major
`Status` to persistent.)

### 4.5 Behavior-preserving refactor discipline
A refactor must not change behavior. The safety net is the test suite: we ran the full
suite before and after and required identical results, and added a **contract test**
(`ResetBattleState_ReplacesWholeBattleState_ClearingEveryTransientField`) that pins
down the new guarantee. Rule of thumb: *a refactor with no new test proving the
intended property isn't finished.*

### 4.6 Consistency with the project's "seam" philosophy
This mirrors how the rest of the engine is built. Generation-variable rules live behind
`IBattleRules`; the type chart behind `ITypeChart`; randomness behind `IRandomSource`;
now transient combat state behind `BattleState`. The recurring principle: **give each
concern a clear, named boundary so it can change in one place.** When in doubt, follow
the existing seams rather than inventing a new pattern.

---

## 5. Rules for contributors

**Adding a new per-battle effect** (a new volatile flag/counter — say a "must
recharge for 2 turns" or a "is protected this turn"):

1. Add the field to **`BattleState`**, with the correct default as its initializer.
   *Never* add a transient field directly to `Creature`.
2. Read and write it as `creature.Battle.MyFlag` everywhere. There is no delegating
   facade on `Creature` — that is the whole point, so don't add one back.
3. **Do not touch `ResetBattleState()`** — the fresh-object reset covers your field for
   free. (If you find yourself editing it, you're doing something unusual; stop and
   ask.)
4. Extend the contract test to assert your field resets.

**Adding permanent data** (a new base stat, an ability, a persisted counter): that goes
on `Creature` (or the relevant DB model), *not* in `BattleState`.

**Deciding which half a field belongs in** — ask: *"If the creature walked into the
next battle, should this value still be there?"* Yes → permanent (`Creature`). No →
transient (`BattleState`). (Remember the section-2 nuance: canonical Gen 1 would put
major `Status` on the "yes" side; today we don't, but that's the known seam to revisit.)

---

## 6. Lifecycle of a single turn (worked example)

Player's creature uses **Body Slam** (10% chance to paralyze) into an enemy already at
+2 Attack from Swords Dance:

1. `Battle.StartFightAsync` already called `ResetBattleState()` on both — both
   `Battle` objects are fresh (`Stages` at 0, `Status` None, etc.).
2. Earlier, the enemy used Swords Dance → `enemy.Battle.Stages.RaiseAttack(2)`.
3. This turn, `AttackAction` computes damage; `DamageCalculator` reads
   `attacker.Battle.Stages.Attack` and `attacker.Battle.Status` (for the Burn penalty) —
   directly off `BattleState`.
4. Body Slam's 10% effect rolls (through `IRandomSource`); on success it sets
   `enemy.Battle.Status = Paralysis`.
5. `StatusResolver` will, on the enemy's future turns, read that `Status` to maybe skip
   the turn (25% full-paralysis chance) and quarter its Speed in the turn-order sort.
6. When this battle ends and a new one starts, `ResetBattleState()` discards both
   `Battle` objects — the paralysis and the +2 Attack vanish; the next fight is clean.

---

## 7. Future direction

- **Save system / Game Loop:** persist `Creature` minus `Battle`. Expect major
  `Status` (and the HP/transient asymmetry noted in section 2) to be revisited — likely
  by moving `Status` to the persistent half or introducing a finer-grained split.
- **Facade removal — done.** The delegating properties have been deleted and all call
  sites migrated to `creature.Battle.X`, so new transient fields can *only* be added to
  `BattleState`. No behavior change (see section 4.3).
- **Multi-generation:** if a later generation needs different transient state (e.g.
  abilities that persist differently), `BattleState` is the place that varies — keep it
  behind the same seam discipline as `IBattleRules`.

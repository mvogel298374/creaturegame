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
| `IStatCalculator` | Stat formulas: HP & other stats, DV/IV randomisation, Stat-Exp/EV scaling **and award** | `Gen1StatCalculator` | `Gen1StatCalculator.Instance` |
| `IEvolutionRules` | Evolution triggers: which condition (level / stone / trade) fires, and how a generation/game-mode interprets it (Gen 1 here = level fires at threshold, trade→level 37 for this roguelite, stone on item-use) | `Gen1EvolutionRules` | `Gen1EvolutionRules.Instance` |

These are the **battle-and-progression** seams. `IEvolutionRules` is a progression seam (consulted between
battles by `BattleRunner`, not in the damage path), but it follows the identical pattern — interface +
`Gen1*.Instance` default, faithful data on `PokemonEvolution`, the gen/mode rule on the seam. A new generation
implements all four; the engine and loop never change. (Older sections below that say "the three seams" predate
it — the count is four.)

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
| **XP on faint** | wild `floor(baseExp × level / 7)`; **trainer-owned ×1.5** (Gen 1 already split wild vs trainer) | Gen 5+ divides among participants; the trainer ×1.5 persists |

> **Roguelite XP curve (not a seam change).** The Gen-1 *wild* formula above is preserved verbatim in
> `Gen1BattleRules.CalculateXpAwarded`. On top of it, the run layer applies a **level-aware XP multiplier** —
> `RunRules.XpMultiplierForLevel(winnerLevel)` threaded `GameSessionManager → RunDirector → BattleRunEvent →
> Battle` and applied to the seam's result at faint time. This is deliberate roguelite pacing, **not** a
> generation rule: it lives in **`RunRules`** — a separate "game-balance dials" bag, explicitly *not* part of
> `IBattleRules` (which stays a faithful Gen-1 impl) — and is a no-op (`RunRules.Default`, 1.0 at every level)
> everywhere except the web run. The web run passes a soft, **linear-by-level** ramp between two anchors
> (`XpMultiplierEarly = 1.5` at level 1 → `XpMultiplierLate = 4.5` at level 100): low levels — already fast
> under Gen-1's cheap early thresholds — get only a light nudge (no sharp multi-level jumps), while the glacial
> high-level grind gets the bigger lift, landing ~3× (2.98×) around the default level-50 start. The **design
> target** (from the reward brief) is that a biome (~4–6 encounters) advances the creature roughly **0.8–1.5
> levels** rather than the slow Gen-1 crawl, and — because it's level-aware — that it stays in that ballpark
> across the picker's whole 5–100 start range. That levels-per-biome figure is a *tuning goal validated by
> playtest*, not a pinned invariant (it also rides on species base-XP and `EncounterFactory.ScaleWildLevel`);
> `RunRulesTests` pins the curve's *shape* (anchors/monotonicity/clamp), not the pacing outcome. The two anchors
> are the tuning dials (trivially exposable as sliders); provisional, retune by playtest.
>
> The Gen-1 trainer bonus **is** modelled: Elite/Boss nodes are "trainer-analog" tiers, so `CalculateXpAwarded`
> applies the Gen-1 trainer **×1.5** for them (`trainerOwned = true`) — a wild battle gets none. This lives in
> the **seam** (it's genuine Gen-1 formula math, `a = 1.5`), separate from the roguelite `RunRules` curve which
> then scales the result; the run layer only supplies the *tier → trainer-owned* fact. So a Boss win pays
> `floor(1.5 × baseExp × level / 7)` × the level's `RunRules` multiplier.
>
> Two scoping notes: (1) "trainer-analog" here borrows only the Gen-1 *flee-block* and *XP-bonus* consequences
> of trainer ownership — **not** its catch-blocking one; the Boss stays catchable by design (`ENCOUNTER_DESIGN.md`
> Phase 4 catch channel). (2) Because Boss caps every biome and Elite is a common interior tier — both already
> higher-level than Wild, so already the biggest base-XP contributors — the ×1.5 *stacks* on top of the
> `RunRules` curve, pushing a typical biome (a Wild/Elite/Boss mix) to the **upper end of (or a touch above)**
> the 0.8–1.5-levels target. That's the intended "beefier boss reward" and part of the provisional tuning.

> **Innate party XP share (not a seam change).** A second roguelite dial on the same `RunRules` bag:
> `RunRules.BenchXpShare`. After a win the active creature is paid its full award (the Gen-1 seam result × the
> XP-curve multiplier above, unchanged), then **every living bench member additionally earns `floor(activeAward ×
> BenchXpShare)` XP + the defeated foe's full Stat-Exp**, and runs the same level-up / move-learn loop; fainted
> members earn nothing. This keeps a drafted roster swappable between biomes. It is **not** the Gen-1 participant
> split (which divides one pool among only the creatures sent out) — it is a wider, always-on Exp-All-style grant,
> a deliberate roguelite deviation, so it lives in **`RunRules`** and never touches `IBattleRules`. Property
> default `0.0` (off — so `RunRules.Default`, every test, and any party-less `Battle` stay a pure no-op); the web
> run picks one of three presets in `GameSessionManager.RunTuningByDifficulty` (Easy `0.75` / **Normal `0.5`** /
> Hard `0.25`) per the player's Easy/Normal/Hard difficulty choice at run-start, beside the matching XP-curve
> anchors. It only fires when a party is threaded into `Battle`. Note the share is taken off the *curve-scaled* `activeAward` (the multiplier compounds
> into it), so a low-level bench member can jump several levels off one late-run win — an intended, generous
> catch-up for underleveled drafts (user-confirmed keep-as-is, not the tamer pre-curve base-XP option). Fainted
> exclusion is by current HP (`IsAlive()`), so an unhealed KO'd member is skipped too. Provisional, retune by
> playtest. Covered by `PartyExpShareTests` (incl. a both-dials curve × share case).

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

The *awarding* of training is on the seam too — `AwardStatExp(victor, defeated)` owns the
gen-variable **gain rule and cap**: Gen 1 adds the defeated species' **base stats**, capped
65535 per stat, and the gain is realized into actual stats only on the next stat recompute (a
level-up — never mid-level); a `Gen3StatCalculator` would instead add the defeated species'
**EV yield** under the 252/stat & 510-total caps. The call site (`Battle`'s win branch) just
calls `AwardStatExp` and stays generation-blind.

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

### 5.0 The generation-agnostic checklist (run before every feature lands)

Most seam violations are not "I wrote `if (gen == 1)`" — they're **subtler leaks** that pass
tests and only surface as rework when Gen 2 starts. Every feature that touches battle math,
stats, or move data must clear this checklist. The three leaks below are the ones that have
actually bitten us; treat each pattern on the left as a **review blocker**.

| 🚩 Red flag in a diff | Why it's a leak | ✅ The fix |
|:----------------------|:----------------|:----------|
| A **magic number** that is a game rule (`* 1.5`, `< 50`, `/ 16`, `217`, `40`) inline in engine code | The value changes between generations; hardcoding it scatters Gen 1 knowledge into generation-blind code | Add a named member to `IBattleRules` (e.g. `StabMultiplier`, `ConfusionSelfHitPercent`) with a per-gen XML doc; read it at the call site |
| Reading **`creature.Attributes.Attack` / `.Special` / `.Defense` directly** for damage/effect math | The Special split (Gen 2) means "the offensive stat" is generation-dependent | Go through `rules.GetOffensiveStat(...)` / `GetDefensiveStat(...)` |
| Reading a **move/species DB column directly** (`attack.EffectChance`, a stat column) where the *layout* could differ by gen | Later gens may store the same concept in a different shape (e.g. per-effect chances) | Ask the rules for the value (`rules.GetSecondaryEffectChance(move, kind)`); the Gen 1 impl can be a thin pass-through that documents the generic shape |
| A **move-specific success condition or damage modifier** written inline in an `AttackAction` damage-category branch (`if (Source.Level < Target.Level)`, `Defense / 2`, a flinch/halve/double) | The *condition itself* is almost always gen-variable even when the move exists in every gen — the OHKO success rule, the Self-Destruct halving, crash damage, etc. all changed between gens. Inline, it's invisible Gen 1 knowledge and frequently just **wrong** (copied from a modern wiki) | Put the rule on `IBattleRules` (`OneHitKoSucceeds`, `SelfDestructDefenseDivisor`, …) and **never mutate `creature.Attributes` to fake a modifier** — pass it into `DamageCalculator` (e.g. `defenseDivisor`) instead |
| A **constant that is genuinely the same in every generation** (4 move slots, the damage-formula `+2`) | — | Leave it inline; **don't** over-abstract. A seam member you'll never vary is noise. |

**The litmus question for every constant and every field read you add:**
> *"When we build Gen 2, will this value or this data layout change?"*
> **Yes →** behind a seam. **No →** inline is correct.

If "yes" but you're not building Gen 2 yet: still add the seam member, implement the Gen 1
value, and — when the *data* layout is what differs — make the Gen 1 implementation a
**documented stub** that shows the generic shape (see `GetSecondaryEffectChance`: Gen 1 reads
one column for every effect kind; the `kind` parameter exists purely so a later gen can branch).
This is cheap now and removes a future archaeology dig.

**Definition of done (gen-agnostic):**
- [ ] No new game-rule magic numbers in `DamageCalculator` / `AttackAction` / `StatusResolver` / `Battle` — they're on a seam.
- [ ] No new direct `Attributes.Attack/Special/Defense` reads in damage/effect math — routed through `GetOffensiveStat`/`GetDefensiveStat`.
- [ ] No new direct gen-shaped DB-column reads at a battle call site — routed through a rules accessor.
- [ ] No `if (generation == …)` / `"gen1"` checks anywhere in the engine.
- [ ] Every new `IBattleRules`/`ITypeChart`/`IStatCalculator` member has a per-generation XML doc.

> Doing this pass **as part of the feature** (not as a later cleanup) is the whole point — the
> debt compounds invisibly until a generation switch forces it all at once.

### 5.0.1 Leaks we've actually shipped (so you recognise the shape)

The checklist above is abstract; these are real leaks that passed review and tests, sat in the
codebase, and were only caught in a later seam audit. **Both involve a move that exists in every
generation — which is exactly why the gen-variable rule inside it slipped past.** When you add a
damage-category branch, assume its success/modifier logic is one of these in disguise:

- **One-hit KO success (OHKO).** The branch read `if (Source.Level < Target.Level)` and a comment
  *claimed* it was "the Gen 1 rule." It is not — that's the **Gen 2+** rule. Gen 1 OHKO moves fail
  when the **target out-speeds the user** (a Speed comparison). So the leak was *also a fidelity bug*:
  inline gen-knowledge tends to be copied from a modern source and is wrong for Gen 1. Fix:
  `IBattleRules.OneHitKoSucceeds(user, target)`.
- **Self-Destruct / Explosion Defense-halving.** The branch did
  `Target.Attributes.Defense = Target.Attributes.Defense / 2;` … calc … then restored it. Two leaks
  in one: a **game-rule magic number** (`/2`, dropped in Gen 5+) *and* **mutating the creature's real
  stats** to fake a modifier (fragile, and meaningless once Special splits). Fix:
  `IBattleRules.SelfDestructDefenseDivisor` passed into `DamageCalculator(..., defenseDivisor:)` —
  the calculator applies the modifier; nobody mutates `Attributes`.

**The tell both share:** the test only asserted the *outcome* ("the target faints", "it deals full
HP"), never the *quirk* ("damage is higher because Defense was halved", "it fails on Speed, not
level"). A test that doesn't exercise the gen-variable bit will keep a leak green forever. When you
add one of these, **write the assertion against the quirk itself.**

### 5.0.2 Item effects — data vs seam (and a deferred gen-variable point)
The in-battle item effects (`IItemEffect` / `ItemEffects`, see `ARCHITECTURE.md` §2.11) clear the
checklist by keeping their **numbers in data, not on a seam**: a Potion's 20 HP, an X-item's +1 stage,
Ether's +10 PP all live on the `Item` row (Gen-1 values set at import in `ItemMapper.ApplyGen1Gameplay`),
read generically by the effect — so they're the data-layer's job (like a move's power), not an
`IBattleRules` member. Two seam judgments worth recording:
- **`ItemAction.ItemPriority` (items resolve before moves)** is an inline constant, not a rules member —
  judged generation-invariant (item-first holds across mainline gens; it's turn-order, not battle math).
  If a later gen ever changes item turn-order, move it to `IBattleRules`.
- **⚠️ Deferred gen-variable rule (the §5.0.1 shape, caught not shipped):** in **Gen 1 no item cures
  confusion** (it's volatile, cleared only by switching) — the cure effects deliberately never touch
  `ConfusedTurns`. But **Gen 2+ Full Heal / Full Restore *do* cure confusion.** This is currently a
  documented *absence* (Gen-1-correct), not a seam. When Gen 2 lands, this becomes an `IBattleRules`
  decision (e.g. "does a status-cure item also clear confusion") — don't re-implement it inline.

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

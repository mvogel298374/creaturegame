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
implements all four; the engine and loop never change.

> **Not a generation seam, but related:** `IRandomSource` (see `STATE_MODEL.md`) controls *randomness*, not generation. It's orthogonal —
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
Things that genuinely differ generation to generation, each a member on the interface. A *Later gens* cell without
a pokecrystal or pokeemerald cite is **unverified** (§4.5):

| Concept | Gen 1 | Later gens |
|:--------|:------|:-----------|
| **Crit chance** | `floor(BaseSpeed/2)/256`; high-crit moves ×8. Uses *base* Speed, ignoring stat stages — fast Pokémon crit constantly. Focus Energy / Dire Hit change the order of the halving and the multiplier (`GEN_DIFFERENCES.md` → Stats). | Stage-based (a fixed ladder), unrelated to Speed. |
| **Crit ignores stat stages** | **Yes** — a Gen 1 crit recomputes from raw stats, *discarding* your Swords Dance boosts *and* the Burn penalty (sometimes a crit hits *weaker*!). | No. |
| **Crit multiplier** | 2.0× | 2.0× (Gen 1–5), 1.5× (Gen 6+). |
| **Damage variance** | random `217–255` ÷ 255 | random `85–100` ÷ 100 |
| **Sleep duration** | 1–7 turns | 2–5 turns |
| **Binding (Wrap etc.)** | 2–5 turns, locks the victim out | reworked later |
| **Accuracy scale** | internal `0–255`; a roll of 255 always misses — the **1/256 miss bug** (even 100%-accurate moves whiff ~0.4% of the time) | `0–100`, no bug |
| **X Accuracy** (`XAccuracyBypassesAccuracyCheck`) | sets `BattleState.UsingXAccuracy`: the user's moves skip the accuracy roll (no RNG draw), but not the OHKO speed check or Mist; Haze and a switch-out clear it; a second use is consumed | Gen 2 same bypass (a substatus flag) — the bool covers only the bypass; Gen 2's refusal of a second use is `RepeatFlagItemRefused`; Gen 3+ a +1 accuracy stage — the imported item row's data |
| **Repeat Dire Hit / Guard Spec / X Accuracy** (`RepeatFlagItemRefused`) | `false`: the item is spent and nothing happens (pokered `engine/items/item_effects.asm:1544-1550`, `:1614-1636` set the bit unchecked) | Gen 2 `true`: refused, "won't have any effect", not spent (pokecrystal `engine/items/item_effects.asm:2089-2125`; no Gen 2 profile exercises it yet) |
| **Dire Hit / Guard Spec narration** (`BoosterItemAnnouncesEffect`) | `false`: only "<PLAYER> used <ITEM>!" (pokered `PrintItemUseTextAndRemoveItem`); the Focus Energy / Mist event is not emitted for the item | Gen 2 `false` (pokecrystal `item_effects.asm:2113-2125`, `UseItemText`); Gen 3+ `true` for Dire Hit — one combined "used <ITEM> to get pumped!" line (pokeemerald `src/battle_message.c:253`, `:1013`); Gen 3 Guard Spec text unverified |
| **X-item at +6** (`CappedXItemRefused`) | `false`: spent before the raise (pokered `:1656` → `:1663`); the cap announcement follows `StatStageCapAnnouncement` | Gen 2 `false` too (pokecrystal `XItemEffect` uses the item first, `:2127-2129`); Gen 3+ `true`: the item effect fails, and is not spent, unless the stage is below max (pokeemerald `src/pokemon.c:4761`, `:4854-4860`) |
| **Roll order** (`HitRollOrder`) | `CritAndDamageThenAccuracy` (pokered `engine/battle/core.asm:3140-3150`): [multi-hit count] → crit → damage roll (skipped when the damage is ≤ 1 — `DamageRollSkippedBelowTwo`) → hit test (Dream Eater's sleep check, then the accuracy roll; Swift / X Accuracy skip only the roll). Type immunity is decided inside the damage calc, so a 0× target shows "doesn't affect" whatever the roll. OHKO: crit, then the speed check (a slower user stops, no further draws), then damage roll and accuracy. Counter: crit, its qualification check (a failure stops before accuracy), accuracy. Fixed damage / Super Fang / Psywave: accuracy only (Psywave's damage after). Thunder Wave on an immune target stops before the accuracy roll (`StatusMoveImmunityBeforeAccuracy`); other status moves: the Pre-hit checks, then accuracy, then the effect's own draws | Gen 2 orders rolls per move script (pokecrystal `data/moves/effects.asm` `NormalHit`: `critical … damagevariation, checkhit`; `MultiHit` puts `checkhit` first), so no single value describes all of Gen 2. Gen 3+ rolls accuracy first (pokeemerald `data/battle_scripts_1.s` `BattleScript_EffectHit`). Under `AccuracyThenCritAndDamage` Dream Eater's sleep check precedes every draw, as Gen 3 does (`battle_scripts_1.s:425`); Gen 3's failure message (`BattleScript_DreamEaterNoEffect`) is not modelled |
| **Multi-hit strikes** (`MultiHitRepeatsFirstStrike`) | `true`: crit, damage and accuracy are rolled once and every strike deals the first strike's damage and crit (pokered `core.asm:3227` loop) | Gen 2 `false`: each strike rolls its own crit and damage (pokecrystal `MultiHit` loops `critical … damagevariation`) |
| **Direct damage vs immunity** (`FixedDamageChecksTypeImmunity`) | `false`: Seismic Toss / Night Shade / Sonic Boom / Dragon Rage / Psywave / Super Fang and Counter skip `AdjustDamageForMoveType` (pokered `core.asm:3136-3142`), so Seismic Toss hits a Ghost and Night Shade a Normal-type | Gen 2 `true` for the static-damage moves: `StaticDamage` → `resettypematchup`, "If there is immunity in play, the move automatically misses" (pokecrystal `engine/battle/effect_commands.asm:1473`); Super Fang, Psywave and Counter in Gen 2 are unverified |
| **Damage roll at 1 damage** (`DamageRollSkippedBelowTwo`) | `true`: damage ≤ 1 is not randomized and draws nothing (pokered `RandomizeDamage`) | Gen 2 `true` (pokecrystal `BattleCommand_DamageVariation`, `engine/battle/effect_commands.asm:1496-1508`, "No point in reducing 1 or 0 damage"); Gen 3 `false`: always draws (pokeemerald `ApplyRandomDmgMultiplier`, `src/battle_script_commands.c:1639-1641`) |
| **Status-move immunity vs accuracy** (`StatusMoveImmunityBeforeAccuracy`) | `true`: a non-damaging move's 0× matchup ends it before the accuracy roll — Thunder Wave on a Ground-type draws nothing (pokered `engine/battle/move_effects/paralyze.asm:13-29`) | Gen 2 `false`: accuracy first, so a miss shows "missed" (pokecrystal `DoParalyze`, `data/moves/effects.asm:1050`: `checkhit` before `paralyze`); Gen 3 `true` (pokeemerald `BattleScript_EffectParalyze`, `data/battle_scripts_1.s:1007`: `typecalc` before `accuracycheck`) |
| **Self-inflicted damage behind a Substitute** (`SelfInflictedDamageStrikesFoeSubstitute`, `StrikeSubstituteByte`) | `true`: a confusion self-hit or a crash by a Substitute holder leaves the user unharmed and strikes the **foe's** Substitute byte (pokered `engine/battle/core.asm:4849-4902` `AttackSubstitute` picks the byte by `hWhoseTurn`; routed there at `:4686-4687` / `:4805-4806`). The byte wraps at 8 bits, breaks only on an underflow, and damage above 255 breaks without writing it. `SelfInflictedDamage.Apply` is the shared path; ordinary recoil never reaches it | Gen 2 `false`: `c = TRUE` skips every Substitute and takes the user's HP (pokecrystal `engine/battle/effect_commands.asm:520-521`, `:627-628`, `:2249-2253`, `:3448`) |
| **OHKO against a Substitute** (`OhkoDamageToSubstitute`) | 65535: an OHKO always breaks a standing Substitute, whatever the target's HP, and leaves its byte unwritten (pokered `engine/battle/move_effects/one_hit_ko.asm` sets `wDamage` to `$FFFF`) | Gen 2 65535 (pokecrystal `BattleCommand_OHKO`, `engine/battle/effect_commands.asm:5449-5452`) |
| **Substitute lines** (`SubstituteBreakAnnouncement`, `SubstituteSetupAnnouncement`, `SubstituteFailureAnnouncement`) | `TookDamageThenBroke`: every hit prints "The SUBSTITUTE took damage for <TARGET>!" first, and a break adds "<TARGET>'s SUBSTITUTE broke!" (pokered `engine/battle/core.asm:4857`, `:4884`). `ItCreated`: setup prints "It created a SUBSTITUTE!" (`_SubstituteText`). `NamedReason`: "<USER> has a SUBSTITUTE!" or "Too weak to make a SUBSTITUTE!" (`engine/battle/move_effects/substitute.asm` `.alreadyHasSubstitute`, `.notEnoughHP`). `SubstituteStrikes` maps all three to their events, and ordinary and self-inflicted hits share it | Gen 2 `TookDamageThenFaded`, "<TARGET>'s SUBSTITUTE faded!" (pokecrystal `engine/battle/effect_commands.asm:3498-3525`, `SubFadedText`); `UserMade`, "<USER> made a SUBSTITUTE!" (`MadeSubstituteText`); `NamedReason`, the same two failure lines (`HasSubstituteText`, `TooWeakSubText`), though Gen 2 is also too weak at exactly the cost, where Gen 1 succeeds and leaves 0 HP (pokecrystal `engine/battle/move_effects/substitute.asm` `.too_weak_to_sub`); that condition is still inline in `SubstituteEffect` (`TODO.md` audit entry) |
| **Pre-hit checks** (`PrimaryEffectChecksPrecedeHitTest`, `PureStatusMoveCanAffect`, `SubstituteStopsPureStatusMove`, `MistStopsStatDrop`, `MistBlockAnnouncement`, `SubstituteStopsThaw`, `PrimaryEffectFailureAnnouncement`) | `true`: a move that deals no damage runs these before its accuracy roll, so a stopped move draws no roll (`AttackAction.StatusMoveStopsBeforeHitTest`, `StatDownStopsBeforeHitTest`). Sleep and paralysis ignore a Substitute (pokered `SleepEffect`, `ParalyzeEffect_` never call `CheckTargetSubstitute`, `engine/battle/effects.asm:1436`). Poison: Substitute, then status, then Poison-type, each "didn't affect" (`PoisonEffect`). Paralysis: status, then Thunder Wave's Ground check (`paralyze.asm`), with no same-type check, so Glare lands on a Normal-type (`PureStatusMoveCanAffect`; the same-type rule in `CanReceiveStatus` is for secondaries only). Confusion and stat-down moves: a Substitute or a miss prints "But it failed!" (`ConfusionEffect`, `ConfusionEffectFailed`, `StatModifierDownEffect`, `MoveMissed`). Mist stops only a primary stat-down, after the Substitute check and past X Accuracy, printing "But it failed!", and never a secondary (`engine/battle/core.asm:5266-5307`). A Substitute stops a Fire hit from thawing (`FreezeBurnParalyzeEffect`) | Gen 2 `false`: `checkhit` runs before the status, Substitute and Mist checks (pokecrystal `data/moves/effects.asm` `DoSleep`, `DoPoison`, `DoParalyze`, `DoConfuse`, `AttackDown`). A Substitute stops every status move (`BattleCommand_SleepTarget`, `BattleCommand_Paralyze`, `engine/battle/effect_commands.asm`). Mist also stops secondary drops, with "protected by the mist" (`CheckMist`, `ProtectedByMistText`). A Substitute stops a thaw too (`BattleCommand_BurnTarget`) |
| **Crit, one-hit KO and effectiveness lines** (`SoakedHitPrintsCritAndEffectiveness`, `EffectivenessLineRepeatsOnEveryStrike`, `HitLineWording`) | `true`: a hit a Substitute soaks still prints its crit line and then its effectiveness line, after the Substitute lines (`SoakedHitReport`; pokered `engine/battle/core.asm:3227-3229`, `:5627-5629`); a self-hit or crash prints neither (`:3690`). `Short`: "Critical hit!", "One-hit KO!" (`PrintsOneHitKoLine`), "It's super effective!", "It's not very effective..." (`data/text/text_2.asm`). The crit and OHKO lines share one slot (`wCriticalHitOrOHKO`): an OHKO never prints "Critical hit!", its line needs the Speed check to pass (`engine/battle/move_effects/one_hit_ko.asm`), and it prints before its real effectiveness because the OHKO routine returns into the normal damage path (`:4467-4471`, `:3144-3147`). A fixed-damage move prints no effectiveness: it skips that step and the byte resets each move (`:3085`, `:5480`). The crit line prints only for a multi-hit's first strike, because `PrintCriticalOHKOText` clears its flag (`:3796-3811`) and later strikes don't re-roll: `DamageDealt.PrintsCritLine` is false on a repeated strike under `MultiHitRepeatsFirstStrike`, while `IsCrit` stays true because the strike deals crit damage. `EffectivenessLineRepeatsOnEveryStrike` `true`: effectiveness repeats on every strike (`PrintsEffectivenessLine`) | Gen 2 `true` (pokecrystal `data/moves/effects.asm` `NormalHit`: `applydamage`, `criticaltext`, `supereffectivetext`; `applydamage` tail-jumps to `DoSubstituteDamage`, `engine/battle/effect_commands.asm:3374`, `:3451`, so the script continues); `Long`: "A critical hit!", "It's a one-hit KO!", "It's super-effective!", "It's not very effective…" (`data/text/battle.asm`). Gen 2 `EffectivenessLineRepeatsOnEveryStrike` false: effectiveness only on a multi-hit's first strike (`supereffectivelooptext`) |
| **Jump Kick crash on immunity** (`CrashOnTypeImmunity`) | `true`: Jump Kick / Hi Jump Kick crash on a 0× target as on a miss (pokered `PrintMoveFailureText` crashes on any failure) | Gen 2 `false` (pokecrystal `engine/battle/effect_commands.asm:2225-2227`, `ret z` when the type modifier is 0); Gen 3 `false` (pokeemerald `data/battle_scripts_1.s:859`, no crash on `MOVE_RESULT_DOESNT_AFFECT_FOE`). A miss crashes in every generation |
| **Freeze** | permanent until hit by a damaging Fire move that can burn; the thaw is a side effect after the damage, skipped when the hit faints the target (Gen 1–3, cited: pokered `effects.asm` `CheckDefrost`, pokecrystal `effect_commands.asm:3938`, pokeemerald `MOVEEND_DEFROST` `src/battle_script_commands.c:4257-4263` (`hp != 0`); inline in `AttackAction`). Explosion / Self-Destruct faint the user on a miss in Gen 1–3 (pokecrystal `Selfdestruct`: `selfdestruct` after `checkhit`; pokeemerald `BattleScript_EffectExplosion`: `setatkhptozero` first), so that is inline too | 20%/turn random thaw; any Fire move thaws |
| **Psywave range** (`PsywaveDamageRange(source, side)`, `RollPsywaveDamage`) | `[1, b)` for the player, `[0, b)` for the opponent, b = L + ⌊L/2⌋: only the player's loop rejects 0 (pokered `core.asm` `ApplyAttackToEnemyPokemon` vs `ApplyAttackToPlayerPokemon`), so only the foe can deal 0 | Gen 2 `[1, b)` for both sides (pokecrystal `engine/battle/effect_commands.asm` `BattleCommand_ConstantDamage` `.psywave_loop`); Gen 3+ 50–150% of level (unverified) |
| **AI stat-down handicap** (`StatDownHandicapApplies`, `RollStatDownHandicapFails`) | the opponent's foe-targeting stat-down moves, primary and secondary, fail on a roll < 64/256 in a non-link battle (we have none) (pokered `engine/battle/effects.asm:539-557` `StatModifierDownEffect`). The roll comes first: before the Substitute check, and before accuracy for a primary move, which then prints "But it failed!" (`MoveFailed`); a failed secondary drop is silent (`:712-716`). `AttackAction` passes the attacker's `BattleSide`. `StatDownHandicapApplies` takes the move and `isSecondary` though Gen 1 ignores them, so Gen 2's per-effect exemptions fit without a signature change | Gen 2 keeps it (pokecrystal `effect_commands.asm` `BattleCommand_StatDown` `.ComputerMiss`) but exempts `EFFECT_ACCURACY_DOWN_HIT` and Lock-On, and extends the same roll to the Paralyze/Poison/Sleep moves — a sibling member when Gen 2 is built |
| **Burn/Poison damage** | 1/16 max HP per turn | 1/8 in Gen 6+ |
| **Residual (status tick) timing** | **no end-of-turn phase** — each creature's Burn/Poison/Leech Seed tick fires right after *its own* action, and a faint jumps straight to the faint handler (so a residual double-faint can't happen). Partly modelled today: `FaintEndsTurnImmediately` covers only the direct-hit-faint case. **Seam candidate, not yet built** — `TODO.md` → Known Gaps; rationale → `GEN_DIFFERENCES.md` → Status Quirks | true end-of-turn phase after both actions (Gen 2+) |
| **Special stat** | one combined **Special** stat for both offense and defense | split into Sp. Atk / Sp. Def (Gen 2) |
| **XP on faint** (`Experience` → `ExperienceFormula`) | one gen-blind integer pipeline, every step floored: `y = Split(baseExp, s)` → `x = ⌊y × L_foe / LevelDivisor⌋` → each listed boost in order, `x + ⌊x/2⌋`. The terms are the `ExperienceRules` record: Gen 1 is `LevelDivisor` 7, `Split = Yields`, `Boosts = [Trainer]` (pokered `engine/battle/experience.asm` `:65-68`, `BoostExp :328`; the traded boost `:82` never applies, as there is no OT). Gen 2 has the same order and adds Lucky Egg after Trainer (pokecrystal `engine/battle/core.asm` `GiveExperiencePoints`) | each generation sets its own terms; a term no built generation uses is added with that generation |
| **XP participant divisor** (`ExperienceRules.Split`, `ExperienceFormula.YieldDivisor`) | `Yields`: the foe's base-exp yield **and its five base stats** are each divided by `s`, the Pokémon sent out that have **not** fainted, **before** the formula, so a remainder is lost (pokered `DivideExpDataByNumMonsGainingExp :294-326`; skipped when `s ≤ 1`). Wild L10 Pidgey (55) with two participants: 38 each, not ⌊78 / 2⌋ = 39. The same divisor reaches `IStatCalculator.AwardStatExp`. The caller excludes fainted participants first; a mutual KO can leave a count of 0, and then nobody is paid | `None` pays every participant the whole award and yield (`TestAltProfile` uses it as the seam's probe) |
| **Already-confused announcement** (`RedundantConfusionAnnouncement`) | `FailedGeneric`: a dedicated confusion move (Confuse Ray, Supersonic) on a confused target prints "But it failed!" (pokered `ConditionalPrintButItFailed`). The counter is never re-rolled (pokered checks the CONFUSED bit first), which is gen-agnostic and inline in `ConfuseEffect`; a secondary confusion on a damaging move fails silently and never consults this | `AlreadyConfused`, "… is already confused!" (Gen 3+, unverified); the engine arm and `ConfusionAlready` event exist and are pinned by a test |
| **Confusion self-hit** (`ConfusionSelfHitPercent`, `ConfusionSelfHitPower`, `ConfusionSelfHitRollsVariance`, `ConfusionSelfHitUsesFoeReflect`) | 50%; a power-40 typeless physical hit with the user as both attacker and defender: its staged Attack (Burn halves it) against its staged Defense, no crit, no STAB, no type chart, and **no damage variance**. **The foe's Reflect doubles the Defense; the user's own does nothing** (pokered `engine/battle/core.asm:3672-3714` `HandleSelfConfusionDamage` puts the user's Defense in the foe's slot, then `GetDamageVarsForPlayerAttack` checks the foe's Reflect bit, `:4044-4055`; enemy side `:5771-5815`, `:4161`). `DamageCalculator.CalculateConfusionDamage(user, foe, rules)` runs the shared stat-and-base step of the damage formula (`ComputeBaseDamage`); `StatusResolver.CanAct` gets the foe from `Battle` (`attack.Target`), and with no foe no screen applies. | Gen 2: the user's **own** Reflect and no variance; stages and Burn still apply (pokecrystal `engine/battle/effect_commands.asm:602-611`, `:2864-2898`, `:4778-4797`). A Gen 2 bug lets a type-boosting item and a selected Self-Destruct/Explosion affect the self-hit (`:2902`); not seamed until a Gen 2 sprint |
| **Stat-cap announcement** (`StatStageCapAnnouncement`) | `NothingHappened`: a primary stat move on a stat at ±6 prints "Nothing happened!" (pokered `PrintNothingHappenedText`); a secondary drop stays silent. The ±6 clamp itself is gen-agnostic | a named "won't rise/drop anymore!" line (Gen 3+, unverified). No enum member exists for it: one is added with its engine arm when a generation needs it |
| **Failure announcements** (`OneHitKoFailureAnnouncement`, `ForceFleeTrainerFailureAnnouncement` → `MoveFailureAnnouncement`, mapped to an event by `MoveFailureEvents.For`) | both `Unaffected` (`MoveUnaffected`, with a `Cause`). A failed OHKO prints `_UnaffectedText` "<TARGET>'s unaffected!" (pokered `core.asm:3718-3734` `PrintMoveFailureText`, `wCriticalHitOrOHKO` = `$ff`); it is not a miss and draws no accuracy roll. Roar/Whirlwind in a trainer battle prints `_IsUnaffectedText` "<TARGET> is unaffected!" (`effects.asm:847-854`). The other members: `Missed` (`MoveMissed`), `DoesntAffect` (`MoveHadNoEffect`, "It doesn't affect <TARGET>!", `_DoesntAffectMonText`, type immunity only) and `DidntAffect` (`MoveDidntAffect`, "It didn't affect <TARGET>!", `_DidntAffectText`, `effects.asm:1416-1423`: the failed wild Roar and the two status-move rows below) and `Failed` (`MoveFailed`, "But it failed!": row "Heal failure"). Leech Seed's "evaded the attack!" and a missed confusion move's "But it failed!" are not printed yet (`TODO.md` §3.1, "Residual-effect failure lines") | Gen 2 OHKO the same (pokecrystal `GetFailureResultText :2211-2214`); Gen 2 Roar force-switches instead (`BattleCommand_ForceSwitch`), so its member changes with that mechanic |
| **Leech Seed drain** (`LeechSeedDrainDenominator`) | 1/16 max HP, the same number as Poison but its own member | 1/8 from Gen 2 while Poison stays 1/16 (unverified) |
| **Counter** (`CounterQualifies`) | pokered `HandleCounterMove` (`core.asm:4547-4607`, per `requirements-review` 2026-10-29) keys on the foe's last **selected** move: Counter misses if that move is Counter, has 0 power, or isn't Normal or Fighting type, and otherwise doubles `wDamage`, the last damage value both sides share. **The engine approximates this**: it keys on the type of the last damage the user *took* (`LastDamageType`), which every damaging move records and Bide's typeless unleash doesn't; the value persists until overwritten, so Counter can answer an earlier turn's hit. The gap is `TODO.md` §3.1 ("Counter keys on the foe's selected move"). Its −5 priority is move data; immunity is `FixedDamageChecksTypeImmunity` | keyed on the move's physical category (Gen 2+, unverified); that needs the engine to record the last hit's `AttackType` |
| **Disable's target move** (inline in `DisableEffect`, **not yet a seam**) | a random move with PP left; the duration is the seam's `RollDisableTurns` | Gen 3+ disables the target's last-used move (unverified); a seam member is added when a generation needs it |
| **Conversion** (inline in `ConversionEffect`, **not yet a seam**) | the user copies the target's types | Gen 2+ changes the user's type to one of its own moves' types (unverified) — a different mechanic, so a seam is designed with that generation |
| **Rage** (`RageAttackStagesPerHit`) | +1 Attack stage each time a damaging move hits the raging user, with no cap beyond the normal +6 | reworked in Gen 2 (unverified) |
| **Recover / Soft-Boiled** (`RecoverHealFraction`) | 1/2 max HP | — |
| **Heal failure** (`HealFails`, `HealFailureAnnouncement`) | Recover, Soft-Boiled and Rest fail when pokered's two-byte compare reads zero (`engine/battle/move_effects/heal.asm` `HealEffect_`): `cp [hl]` on the high bytes only sets the carry (borrow = HP's high byte below max's), then `sbc [hl]` subtracts the low bytes with it and `jp z` fails. So the move fails at full HP and, when the high bytes differ, whenever HP's low byte is max's low byte + 1 (mod 256): 255 or 511 below max (max 300 at HP 45 fails). With equal high bytes only full HP fails, so max 511 at HP 256 (255 below) still heals. Modelled exactly in `Gen1BattleRules.HealFails`. A failure prints "But it failed!" (`.failed` → `PrintButItFailedText_`): `Failed` → `MoveFailed`. `HealEffect.FailsAndAnnounces` is the one check both effects use | full HP only, both bytes compared (pokecrystal `effect_commands.asm` `BattleCommand_Heal`, `CompareBytes`, `c = 2`); prints "HP is full!" (`HPIsFullText`), a member added with Gen 2 |
| **Status by type** (`CanReceiveStatus`) | pokered `FreezeBurnParalyzeEffect` (`effects.asm:194-212`, per `requirements-review` 2026-10-29) skips a secondary freeze, burn or paralysis whenever the move's type matches either target type: Ice moves can't freeze Ice-types, Fire moves can't burn Fire-types, Electric and Normal moves can't paralyze Electric- and Normal-types (Body Slam). Poison-types can't be poisoned. **The engine is narrower**: it models Poison, burn-vs-Fire and Normal→Normal paralysis only, so Ice Beam can freeze an Ice-type and Thunderbolt can paralyze an Electric-type. The gap is `TODO.md` §3.1 ("Same-type secondary status immunity") | more immunities (unverified) |
| **Status-move type check** (`PureStatusMoveChecksTypeImmunity`) | only Thunder Wave (a Ground-type is immune); Confuse Ray, Glare, Growl, sleep moves and Disable ignore type. The implementation keys on the move, not a target direction, so **any move returning true must be foe-directed** (the result is applied against the target's type) | status moves respect immunity generally from Gen 2 (unverified) |
| **Roar / Whirlwind vs a non-escapable foe** (`ForceFleeFailsVsTrainer`) | `true`: in a trainer battle the move fails, "… is unaffected!" (pokered `IsUnaffectedText`). Non-escapable is a run-layer fact (`MoveEffectContext.BattleEscapable`: Elite and Boss encounters, the trainer analog). The trainer-battle line is the seam's `ForceFleeTrainerFailureAnnouncement` (row "Failure announcements") | forces a switch instead (Gen 2+, unverified); no force-switch path exists, and one is built with that generation |
| **Roar / Whirlwind in a wild battle** (`ForceFleeSucceeds`, `ForceFleeWildFailureAnnouncement`) | succeeds outright when the user's level is at least the target's; otherwise only when `rand[0, user + target] >= target / 4` (integer quarter). The same roll for either side's user (pokered `effects.asm:810-888` `SwitchAndTeleportEffect`). Lv10 against Lv50: a roll of 12 or more out of 0–60 succeeds. A failure prints `_DidntAffectText` "It didn't affect <TARGET>!" (`PrintDidntAffectText`, `:837`): `DidntAffect` → `MoveDidntAffect` | the same roll (pokecrystal `effect_commands.asm:4976-4991`); a failure prints "But it failed!", a member added with Gen 2 |
| **Status-move no-op line** (`StatusMoveNoOpAnnouncement`) | `DidntAffect`: a pure status move on a foe that already has a major status (other than a sleep move on a sleeping foe, which prints "already asleep!"), or any pure status move `PureStatusMoveCanAffect` rejects (Poison Powder / Poison Gas / Toxic on a Poison-type), prints `_DidntAffectText` (`SleepEffect` `effects.asm:42-51`, `PoisonEffect` `:87-98` → `:155-162`, `paralyze.asm` `ParalyzeEffect_` `.didntAffect`). Thunder Wave on an unstatused Ground-type is type immunity instead (`.doesntAffect`), printed by the type-immunity path. The status check comes before the Ground check and the hit test (see Pre-hit checks). A damaging move's secondary status stays silent | unverified |
| **Status-move miss line** (`StatusMoveMissAnnouncement`) | `DidntAffect`: a 0-power move skips the main `MoveHitTest` (`core.asm:3145`); the Sleep, Poison and Paralyze effects roll it themselves and print `_DidntAffectText` on a miss (`effects.asm:53-57`, `:108-113`, `paralyze.asm` `.hitTest`). `AttackAction.Connects` uses it for a missed `BaseDamage == 0` move with a `StatusEffect`; a missed confusion or stat-down move uses `PrimaryEffectFailureAnnouncement` (see Pre-hit checks) | unverified |
| **Rampage turns** (`RampageRepeatAnnouncement`, `RampageEndAnnouncesConfusion`, `RampageEndOverwritesConfusion`) | `ThrashingAbout`, `false`, `true`. A repeat turn of Thrash or Petal Dance prints `ThrashingAboutText` "<USER> is thrashing about!" and jumps to `PlayerCalcMoveDamage` (`core.asm:3134`), past `DisplayUsedMoveText` (`:3118`), so there is no "used" line; the hit still animates (pokered `.ThrashingAboutCheck`, `core.asm:3531-3552`). `RampageMechanic.OnCommit` emits `RampageContinues` and returns `LockInFlow.ProceedUnannounced`, and `AttackAction` skips only `MoveUsed`. When the counter reaches 0 the lock ends, `CONFUSED` is set with no text, and a fresh 2–5 counter is written even over a running one (`:3545-3550`). `OnTurnEnd` sets it after the final attack, not before it as in pokered: nothing reads it in between | Gen 2 `UsedMove`, `false`, `true`: `checkrampage` comes before `usedmovetext`, so "used" prints every turn (pokecrystal `data/moves/effects.asm:811-816`); the end confuses silently with a fresh counter (`engine/battle/effect_commands.asm:4888-4920`) |
| **Roar / Whirlwind accuracy** (`ForceFleeSkipsAccuracy`) | `true`: `SWITCH_AND_TELEPORT_EFFECT` is in `ResidualEffects1`, which skips the accuracy test (`core.asm:3124-3127`), so Whirlwind's 85 accuracy is never rolled. The other `ResidualEffects1` moves are unchecked (`TODO.md` §3.1) | `false`: the move can miss (`wAttackMissed`, `effect_commands.asm:4970-4972`) |
| **A flee ends the turn** (`FleeEndsTurnImmediately`) | `true`: `MainInBattleLoop` returns as soon as `wEscapedFromBattle` is set, after either side's action (`core.asm:419-421`, `:430-432`, `:443-445`, `:457-459`). `Battle` breaks out of the action loop and skips the turn counters and residuals, so the other side never acts and the Roar user's own poison never ticks. Not modelled: when the slower side flees, Gen 1 has already ticked the faster side's residual; that is the per-creature residual order (row "Residual (status tick) timing") | `true`: `Battle_PlayerFirst`/`Battle_EnemyFirst` `ret nz` on `wForcedSwitch` (pokecrystal `core.asm`) |

> **Roguelite XP tuning (not a seam change).** `RunRules` is a separate game-balance-dials bag, not part of
> `IBattleRules`. Two dials sit on it: a level-aware **XP multiplier** and a **bench XP share** paid to living
> members who never took the field. Both are no-ops by default (`RunRules.Default`) and only active on the web
> run. The **participant split** is a term of the seam (`ExperienceRules.Split`) — `Battle` only decides *who*
> participated, which is gen-invariant, and passes the count in. The bench share is not a Gen-1 mechanic at all
> (no cartridge pays a creature that wasn't sent out). **Composition in `Battle`** (user ruling 2026-10-31: the
> XP is the gen-exact base × the game's value): each participant's award is `ExperienceFormula.Award(…, s)` × the
> `RunRules` multiplier; the bench bonus, `⌊ExperienceFormula.Award(…, 1) × multiplier × BenchXpShare⌋` XP with the
> full Stat Exp yield, goes to every living member, participants on top of their split (user ruling 2026-10-31).
> Stat Exp has no game multiplier. Neither system reads the other, so a generation swap and a
> balance retune stay independent.
> Full tuning values and rationale → `STATE_MODEL.md`;
> `docs/TODO_ARCHIVE.md` → *Reward Visibility & XP Pacing* (the multiplier anchors and pacing target) and →
> *Participation XP* (the participant-split decision).

The Special-stat split is a good illustration of the seam doing its job. Rather than the
damage formula knowing about generations, `IBattleRules` exposes **`GetOffensiveStat`**
and **`GetDefensiveStat`**:

```csharp
// Gen1BattleRules: Physical → Attack/Defense; Special → the single combined Special stat.
public int GetOffensiveStat(Creature attacker, AttackType moveType) =>
    moveType == AttackType.Physical ? attacker.Attributes.Attack : attacker.Attributes.Special;
```

A `Gen2BattleRules` would return `SpAtk` / `SpDef` here, and `DamageCalculator` — which
just calls `rules.GetOffensiveStat(...)` — wouldn't change at all. The **stat stage** applied beside each stat is
chosen the same way, by **`OffensiveStage`** / **`DefensiveStage`** (a `StageStat`, read with
`StatStages.Of`): Gen 1 returns Attack/Defense for Physical and the single Special stage for both sides on
Special (pokered `af51989` `ram/wram.asm:550`/`:572`, one Special mod per side); Gen 2 would return
SpAtk/SpDef once `StatStages` grows those stages (the state split: `TODO.md` §4.4).

### Stat calculation (`IStatCalculator`)
The formulas that turn base stats + per-individual values into a creature's actual stats:

- **Gen 1** uses **DVs** (Determinant Values, 0–15, four of them; the HP DV is *derived*
  from the low bits of the other four) and **Stat Exp** (0–65535, scaled by
  `min(255, ceil(sqrt(StatExp))) / 4`, integer division — pokered `home/move_mon.asm` `CalcStat`). Formula,
  integer arithmetic throughout:
  `((Base + DV)×2 + min(255, ceil(sqrt(StatExp)))/4) × Level / 100 + Level + 10` for HP,
  `+ 5` for other stats.
- **Gen 3+** replaces these with **IVs** (0–31, six independent) and **EVs** (capped at
  252 per stat, ÷4). A `Gen3StatCalculator` swaps the formula and the `RandomiseDvs`
  logic; nothing that *calls* the calculator changes. (Unverified: Gen 2 splits the Special *stat* but keeps
  one shared Special DV, which becomes two IVs only in Gen 3. The Special split also touches `Attributes` and
  `GetOffensiveStat` / `GetDefensiveStat`.)

The *awarding* of training is on the seam too — `AwardStatExp(victor, defeated, yieldDivisor)` owns the
gen-variable **gain rule and cap**: Gen 1 adds the defeated species' **base stats**, each floored by the
participant divisor that `ExperienceFormula.YieldDivisor` derives from `IBattleRules.Experience` (1 for the
bench share), capped 65535 per stat, and the gain is realized into actual stats only on the next stat recompute (a
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
public double RollDamageVariance() => _rng.Next(VarianceMinRoll, VarianceMaxRoll + 1) / 255.0;   // 217–255
public (double Min, double Max) DamageVarianceRange => (VarianceMinRoll / 255.0, VarianceMaxRoll / 255.0);
```

The roll and its range read the same constants, so the dev damage-range display
(`DamageCalculator.EstimateRange`) can never disagree with the real roll. `Psywave` follows
the same pattern: `RollPsywaveDamage` and `PsywaveDamageRange(source, side)` share one helper.

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
int atkStage    = stages.Of(rules.OffensiveStage(move.AttackType));         // IBattleRules
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
substitutability** for tests and future gens. `IBattleRules` is the exception on the web run path: its rolls
need the run seed, so a run builds its own instance through `GenerationProfile.BuildBattleRules(session.Rng)`
(like `BuildStatCalculator`); the unseeded `Gen1BattleRules.Instance` is only the fallback for direct callers,
tests and draw-free reads.

> ⚠️ **The flip side, once a second generation exists.** That same default means a composition path which
> *forgets* to thread a seam does not crash and does not fail a test — **it silently runs Gen 1**. The defaults
> are still right for the library and its tests, but the **web composition root must pass every seam
> explicitly**, and only a second profile can prove it did. See `GENERATION_PROFILE.md` §4.2 and its
> `TestAltProfile` harness. It mirrors how `IRandomSource` and
`BattleState` are wired; consistency across the codebase is deliberate.

### 4.4 Sparse data, explicit quirks
`Gen1TypeChart` stores only non-`1.0` matchups and returns `1.0` for anything missing —
compact, and the table reads as "the interesting cases." Every historical quirk/bug is
**commented as such** (e.g. `// Gen 1 bug: Ghost is immune to Psychic`). When you port a
chart, that comment is the spec: it tells the next person the value is intentional, not a
typo.

### 4.5 Code comments state Gen 1 only
A seam member's doc comment is one line saying what the member returns, plus at most the Gen 1 value. **No code
comment states what a later generation does** (user ruling 2026-10-29): those claims went unchecked and drifted —
the Gen 2+ type-chart claims were wrong. Later-generation facts live in markdown, in §2's *Later gens* column and
`GEN_DIFFERENCES.md`, marked *unverified* until they are checked against that generation's disassembly (pokecrystal
for Gen 2) in its own sprint. In code, a later generation exists only as a seam implementation or an enum member
with a live engine path — never as a placeholder comment or an unhandled member.

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
| A **magic number** that is a game rule (`* 1.5`, `< 50`, `/ 16`, `217`, `40`) inline in engine code | The value changes between generations; hardcoding it scatters Gen 1 knowledge into generation-blind code | Add a named member to `IBattleRules` (e.g. `StabMultiplier`, `ConfusionSelfHitPercent`) with a one-line Gen 1 XML doc; read it at the call site |
| Reading **`creature.Attributes.Attack` / `.Special` / `.Defense` directly** for damage/effect math | The Special split (Gen 2) means "the offensive stat" is generation-dependent | Go through `rules.GetOffensiveStat(...)` / `GetDefensiveStat(...)`, and pick the matching stage with `OffensiveStage(...)` / `DefensiveStage(...)`, never a Physical-vs-Special branch on `Stages` |
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
This is cheap now and removes a future archaeology dig. What the later generation does goes in §2, not in a code
comment (§4.5).

**Definition of done (gen-agnostic):**
- [ ] No new game-rule magic numbers in `DamageCalculator` / `AttackAction` / `StatusResolver` / `Battle` — they're on a seam.
- [ ] No new direct `Attributes.Attack/Special/Defense` reads in damage/effect math — routed through `GetOffensiveStat`/`GetDefensiveStat`.
- [ ] No new direct gen-shaped DB-column reads at a battle call site — routed through a rules accessor.
- [ ] No `if (generation == …)` / `"gen1"` checks anywhere in the engine.
- [ ] Every new `IBattleRules`/`ITypeChart`/`IStatCalculator` member has a one-line XML doc (Gen 1 only, §4.5).

> Doing this pass **as part of the feature** (not as a later cleanup) is the whole point — the
> debt compounds invisibly until a generation switch forces it all at once.

### 5.0.1 Leaks we've actually shipped (so you recognise the shape)

A damage-category branch on a move that exists in every generation is the highest-risk spot — the
gen-variable rule inside it slips past because the move itself looks gen-invariant. Two shipped
examples: **OHKO success** was inline as `Source.Level < Target.Level` (the Gen 2+ rule, wrongly
labeled "Gen 1" — real Gen 1 fails on a **Speed** comparison), and **Self-Destruct's Defense-halving**
mutated `Target.Attributes.Defense` in place instead of passing a `defenseDivisor` into
`DamageCalculator`. Both are now `IBattleRules` members (`OneHitKoSucceeds`,
`SelfDestructDefenseDivisor`). The shared lesson: **test the quirk, not just the outcome** — a test
that only asserts "the target faints" will keep a leak green forever.

### 5.0.2 Item effects — data vs seam
In-battle item effects (`IItemEffect` / `ItemEffects`, see `ARCHITECTURE.md` §2.11) keep their **numbers
in data, not on a seam**: a Potion's 20 HP, an X-item's +1 stage, Ether's +10 PP all live on the `Item`
row, read generically by the effect. Seam judgments on record:
- **`ItemAction.ItemPriority`** (items resolve before moves) is an inline constant — gen-invariant across
  mainline games, so not a rules member unless a future gen changes item turn-order.
- **Confusion cure is gen-variable, not yet modeled.** Gen 1 items never cure confusion (only switching
  does); Gen 2+ Full Heal/Full Restore do. Currently a documented Gen-1-correct absence; becomes an
  `IBattleRules` decision when Gen 2 lands.
- **Full Restore at full HP.** Gen 1 `ItemUseMedicine` turns FULL_RESTORE into FULL_HEAL when the target is at
  full HP and has a major status — the status is cured and the item consumed, with no HP-gain event; with no status
  it is refused. Confusion (volatile) alone never counts. The rule is item-data-driven in
  `HealingItemEffect.CanApply` and is not expected to vary by generation; only the confusion cure does (decision
  above).
- **X Accuracy is the one item whose *kind* of effect is gen-variable.** The row keeps the Gen 3+ shape (+1
  accuracy stage); `BattleBoostItemEffect` asks `IBattleRules.XAccuracyBypassesAccuracyCheck` (reached via
  `ItemEffectContext.Rules`) and, when true, sets the bypass flag instead, with no event of its own (Gen 1 prints
  only the "used" line, which `ItemUsed` already narrates). Gen 1 consumes a second use while the flag is set; the
  Gen 2 "won't have any effect" refusal is a gen-variable absence, not yet modeled. `TestAltProfile` returns
  false, so the stage path stays covered.
- **Item target scope is gen-invariant, NOT a seam — but it splits by category, not uniformly.** Healing,
  StatusCure, PpRestore, and Revive act on *persistent* per-Pokémon data (current HP, status, PP, or a
  faint) that exists for every party member regardless of who's active, so the real games show a full
  party-selection screen for these — any *living* member, except Revive/Max Revive which need a *fainted*
  one — unchanged since Gen 1. **BattleStatBoost is the exception, not a fifth member of that group:** Gen 1
  stores stat stages (and the Focus Energy / Mist volatiles) only for the currently active battler, so
  X-items/Guard Spec/Dire Hit never show a party screen at all — they apply immediately to whoever's on the
  field. (A first pass at this fidelity fix wrongly generalized X-items into the party-target group too;
  `requirements-review` caught it before it shipped — see `docs/TODO_ARCHIVE.md` → *In-Battle Item
  Party-Targeting*.) `HealingItemEffect`/`StatusCureItemEffect`/`PpRestoreItemEffect`/`ReviveItemEffect` all
  resolve their target via `ItemEffectContext.ResolvedTarget`; `BattleBoostItemEffect` deliberately reads
  `ctx.User` directly instead.

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
Implement all four interfaces — `Gen2TypeChart`, `Gen2BattleRules`, `Gen2StatCalculator`,
`Gen2EvolutionRules` — and select them where battles/creatures are constructed. The engine itself does not
change. Expect the bulk of Gen 2 to be: the Special stat split (touches
`IStatCalculator`, `Attributes`, `StatStages`, `GetOffensiveStat`/`GetDefensiveStat` and
`OffensiveStage`/`DefensiveStage`), the
stage-based crit formula, the corrected type chart, and the `0–100` accuracy scale. See
the multi-generation roadmap in `TODO.md` §4.4.

**What a new generation must also supply on the *data* side** (import-time, not a seam — the engine reads the
result from the DB; rationale → `ARCHITECTURE.md` §2.6, mapping → `DATA_IMPORT.md` §4.2):
- a **`past_stats` resolution target** — `SpeciesStatResolver.BaseStatsAsOf(pokeData, generation)` already takes
  the generation as a parameter; the new generation just passes its number;
- a **`GenerationImportScope` entry** — `GenerationImportScope.For(generation)` throws `NotSupportedException`
  for anything but Gen 1; it holds the learnset version group, max move id and max species id the importer reads;
- a **base-experience table and a catch-rate table** — `SpeciesBaseExperience.For(generation, speciesId)` and
  `SpeciesCatchRate.For(generation, speciesId)` throw `NotSupportedException` for a generation with no table, so a
  missing table fails the import loudly instead of importing modern values (PokeAPI has no history for either);
- an **item price table** — `ItemMapper.Gen1Prices` is not keyed by generation, so a second generation's import
  would give every shared item its Gen 1 price (only an item missing from the table fails). `Item.Cost` drives the
  run's rarity, shop prices and starting bag (`ENCOUNTER_DESIGN.md` §5.1); key the table by generation with the
  per-generation item data (`TODO.md` §4.4);
- an **item description table** — `ItemMapper.Gen1Descriptions` has the same weakness: a second generation's import
  would give every shared item its Gen 1 text, including Dire Hit's bug warning and "but not confusion" (only an item
  missing from the table fails). Key it by generation alongside the prices;
- a **high-crit move list** — `MoveImport.Gen1HighCritMoves` is Gen 1's list, and `IsHighCrit` holds one generation's
  answer per move. Verify each generation's list at its own disassembly rather than PokeAPI's modern `crit_rate`;
- a **move description table** — `MoveImport.Gen1Descriptions` overrides PokeAPI's text for the moves Gen 1 gets different,
  and `Description` holds one generation's text per move. A second generation's import would give shared moves Gen 1's
  text. Key it by generation and audit each generation's moves at its own disassembly;
- for **Gen 2**, the Special split needs **two stat columns** (a model change; `SingleSpecialAsOf` throws for
  generation != 1 until then) — the `GetOffensiveStat`/`GetDefensiveStat` seam is where the engine picks between
  them (the Special-model decision is part of adding the generation);
- the remaining importer Gen-1-isms (types, moves, evolutions, game availability, `Program.cs`) are still to be
  generation-scoped — `TODO.md` §4.4.

The species base-stat / base-experience fix (2026-10-03) touched none of the four seams and passes the §5.0
checklist: no new engine constants, no direct stat reads, no generation checks in the engine.

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
   stat for both sides (Gen 1), and **`OffensiveStage`/`DefensiveStage`** the single Special stage beside it — a
   `Gen2BattleRules` would return Sp. Atk / Sp. Def here.
4. **`ITypeChart.GetMultiplier`** supplies the type multiplier, preserving Gen 1 quirks
   (so e.g. a Bug move into Psychic returns the Gen 1 `2.0×`).
5. **`IBattleRules.RollDamageVariance`** applies the Gen 1 `217–255/255` spread (its bounds are exposed as `DamageVarianceRange`).

Five generation-variable decisions, zero generation checks — each resolved by the
injected implementation. That's the seams working.

---

## 7. Future direction

- **Gen 2 sprint:** the first real exercise of these seams. The Special split and the DB
  schema work are scoped in `TODO.md` §4.4.
- **Generation selection — ✅ this composition point now exists (2026-07-29).** It is
  `creaturegame/Generations/`: a `GenerationProfile` bundling the seams, a `GenerationProfiles`
  registry, and the generation threaded as a per-run parameter into `GameSessionManager`.
  The engine still never branches. Design + staging → `GENERATION_PROFILE.md`. **Note the
  seams are only half of it** — that doc extends the model to content, region and
  presentation, which the four interfaces here do not cover.
- **Keep the seams honest:** if a future mechanic tempts you toward a generation check,
  that temptation is the design telling you to add an interface member instead.

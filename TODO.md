# Battle Sim – TODO List

> **See also:** `CLAUDE.md` (session setup, architecture, commands) · `AI_CONTEXT.md` (agent profiles) · `DESIGN_GUIDES.md` (mechanics rules) · `DEV_STANDARDS.md` (coding conventions)

## Priority 1 – Type Chart ✅ DONE
- [x] Create `ITypeChart` interface
- [x] Implement `Gen1TypeChart` with full 17-type Gen 1 matrix (Ghost/Psychic bug, Poison super vs Bug, no Steel/Dark/Fairy)
- [x] Wire into `DamageCalculator` via interface (swappable per-generation)
- [x] `AttackAction` and `Battle` accept `ITypeChart` — one injection point to swap generation
- [x] 7 type chart tests added and passing

## Priority 2 – PP Tracking ✅ DONE
- [x] Switch `Creature.MoveSet` from `List<Attack>` to `List<PokemonAttack>`
- [x] `AttackAction` decrements `PowerPointsCurrent`, checks > 0 before executing
- [x] Handle Struggle when all PP = 0

## Priority 3 – Move Priority Fix ✅ DONE
- [x] `AttackAction` constructor: read `move.Priority` instead of hardcoding 0

## Priority 4 – Status Condition Application ✅ DONE
- [x] Add `StatusEffect` (`StatusCondition`) property to `Attack`; add EF migration
- [x] Add `meta.ailment` mapping to `PokeApiMove`; import `ailment.name` → `StatusEffect`, `ailment_chance` → `EffectChance` in `MoveImport`
- [x] `AttackAction.ExecuteAsync()`: after damage, roll `EffectChance` and set `Target.Status` if target has no status and move has a `StatusEffect`
- [x] Set `SleepTurns` (1–7, random) when applying Sleep
- [x] Tests: status applied on hit; not applied when target already statused; secondary effect chance respected

## Priority 5 – Status Effects in Battle Loop ✅ DONE
- [x] Pre-turn: Sleep skips action and decrements `SleepTurns`; wakes when counter hits 0
- [x] Pre-turn: Freeze skips action; thaws on any Fire-type move hitting the frozen target
- [x] Pre-turn: Paralysis — 25% chance to skip action
- [x] Stat modifiers: Burn halves physical Attack in `DamageCalculator`; Paralysis quarters Speed in turn ordering
- [x] End-of-turn: Burn deals 1/16 max HP; Poison deals 1/16 max HP
- [x] Pseudo-status — Confusion: `Creature.ConfusedTurns` counter; 50% chance to hurt itself each turn (40 base power, typeless); clears when counter expires (2–5 turns, Gen 1)

## Move Effects Layer (generation-agnostic)
Move effects that are properties of the move itself rather than generation rules.
Haze is the first concrete case; others follow the same pattern.
- [ ] `MoveEffect` enum on `Attack`; `Attack.Effect` property; EF migration
- [ ] `AttackAction` switches on `Effect` after damage/status: `ClearAllStatus` (Haze) clears `Status`, `SleepTurns`, `ConfusedTurns` on both Pokémon
- [ ] Future effects to add as moves require: `Flinch`, stat-stage changes (`SwordsDance`, `Growl`, etc.), `Recharge` (Hyper Beam), `Leech Seed`, `Substitute`

## Priority 6 – Move Selection (Player Input)
- [ ] Implement `ConsoleInput : IBattleInput` — numbered move menu, shows PP and type
- [ ] Wire `ConsoleInput` into `Program.cs` for the player side; enemy keeps `AutoSelectInput`

## Priority 7 – Experience & Catch System
- [ ] `Battle.cs` awards XP to winner on faint (Gen 1 formula)
- [ ] Basic catch mechanic using `PokemonSpecies.CatchRate`

## Priority 8 – Learnset System
- [ ] `PokemonLearnset` DB table: species ID → move ID → level learned
- [ ] Import learnsets from PokeAPI (`/pokemon/{id}/moves`)
- [ ] `Creature.InitializeFromSpecies()` populates starting moveset by level

## Priority 9 – AI Move Selection
Design: `IBattleInput` is already the seam. AI implementations score available moves via
`IMoveEvaluator` and pick using a selection strategy.

**Evaluator dimensions (score each available move):**
- Expected damage — base power × type effectiveness × STAB × stat ratio
- Type effectiveness bonus — super-effective moves strongly preferred
- Priority move value — prefer Quick Attack / high-priority when own HP is low or opponent near KO
- Status move value — Thunder Wave is high-value early; worthless if target already has a status
- PP conservation — small penalty for moves with ≤ 5 PP remaining
- Opponent HP threshold — any move finishes a near-KO target; don't waste a precision pick

**Selection strategies (how scores become a choice):**
- `RandomMoveInput` — ignores evaluators; pure random (wild Pokémon / lowest AI tier)
- `WeightedAIInput(IMoveEvaluator)` — probabilistic, weighted by score (average trainer)
- `GreedyAIInput(IMoveEvaluator)` — always picks highest score (Elite Four / boss tier)

**Composition:**
- `CompositeEvaluator(IEnumerable<(IMoveEvaluator evaluator, double weight)>)` — weighted sum;
  trainer "personality" = different weights (aggressive vs. defensive vs. status-heavy)

**Implementation tasks:**
- [ ] `DamageEvaluator : IMoveEvaluator`
- [ ] `TypeEffectivenessEvaluator : IMoveEvaluator`
- [ ] `StatusMoveEvaluator : IMoveEvaluator`
- [ ] `CompositeEvaluator : IMoveEvaluator`
- [ ] `RandomMoveInput : IBattleInput`
- [ ] `GreedyAIInput : IBattleInput`
- [ ] `WeightedAIInput : IBattleInput`

## Priority 10 – Web UI
Deprecate `Program.cs` as the primary entry point and replace it with a proper web front-end.
The battle engine is already decoupled from I/O via `IBattleInput` and `IBattleAction`,
so this is largely an infrastructure and presentation layer addition.

- [ ] Add an ASP.NET Core project to the solution as the web host
- [ ] Expose battle state over SignalR (real-time push suits the turn-based loop naturally)
- [ ] Implement `WebInput : IBattleInput` backed by the SignalR connection — player sends
      their chosen move index; server resolves the turn and broadcasts the result
- [ ] Build a minimal browser UI: creature HP bars, move menu, battle log
- [ ] `Program.cs` console runner becomes a dev/debug tool, not the primary entry point

---

## Tech Debt / Cleanup

### Done
- [x] Remove dead scaffolding: `Body`, `Brain`, `BodyPart`, `Special`, `Dragon`, `CreatureType`, `Attributes.SetAttributesByCreatureType`
- [x] Remove unused `using System.Net.NetworkInformation` from `Attributes.cs`
- [x] Fix `.gitignore` — add local tool config exclusions (`.claude/settings.local.json`, `.ai/`), untrack committed build artifacts and IDE files
- [x] Add `global.json` to pin .NET SDK to 9.0.200
- [x] Adopt EF Core migrations — `InitialCreate` scaffolded for both `MovesDbContext` and `PokemonDbContext`; `EnsureDatabaseCreated()` now calls `Database.Migrate()` instead of raw `ALTER TABLE` hacks
- [x] Remove hardcoded placeholder `Traits` from `Creature` constructor; `Trait.cs` / `TraitType.cs` scaffolding retained for future Abilities layer
- [x] Add `.editorconfig` for consistent indentation (4 spaces), charset (UTF-8), and editor-side line ending preference
- [x] Add `.gitattributes` to normalise line endings in the repo (LF in git, auto CRLF on Windows checkout)
- [x] Add `README.md` at repo root
- [x] Remove `Creature.Attack()` direct-damage method — bypasses `DamageCalculator`/type chart and has a naming collision with the `Attack` class; `AttackAction` is the correct path
- [x] Remove redundant `Attributes.GetCurrentHealth()` call from `IsAlive()` — now reads `Attributes.HP` directly; `IsAlive()` itself retained as a meaningful predicate

### Pending
- [x] Resolve `Creature` class/namespace name collision — renamed namespace to `creaturegame.Creatures`; all 16 files updated, fully-qualified `Creature.Creature` references eliminated
- [x] Remove redundant `Attributes.GetSpeed()` wrapper — no callers; deleted
- [x] Decide on `.idea/` strategy — keep fully excluded; solo project, no run configs to share, dataSources.local.xml contains local paths
- [x] Consolidate or clarify relationship between `AI_CONTEXT.md` / `DESIGN_GUIDES.md` / `DEV_STANDARDS.md` and `CLAUDE.md` — all files now carry See Also cross-reference tables; CLAUDE.md has a Key Files section and delegates TODO state to TODO.md
- [x] Decide on `Traits` scaffolding — removed `Trait.cs` / `TraitType.cs`; Gen 1 has no Abilities; reintroduce as a proper Abilities layer when that priority is reached

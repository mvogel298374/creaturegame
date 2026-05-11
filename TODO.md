# Battle Sim – TODO List

## Priority 1 – Type Chart ✅ DONE
- [x] Create `ITypeChart` interface
- [x] Implement `Gen1TypeChart` with full 17-type Gen 1 matrix (Ghost/Psychic bug, Poison super vs Bug, no Steel/Dark/Fairy)
- [x] Wire into `DamageCalculator` via interface (swappable per-generation)
- [x] `AttackAction` and `Battle` accept `ITypeChart` — one injection point to swap generation
- [x] 7 type chart tests added and passing

## Priority 2 – PP Tracking
- [ ] Switch `Creature.MoveSet` from `List<Attack>` to `List<PokemonAttack>`
- [ ] `AttackAction` decrements `PowerPointsCurrent`, checks > 0 before executing
- [ ] Handle Struggle when all PP = 0

## Priority 3 – Move Priority Fix
- [ ] `AttackAction` constructor: read `move.Priority` instead of hardcoding 0

## Priority 4 – Status Condition Application
- [ ] Moves with status effects apply `StatusCondition` to target (Burn, Paralysis, Poison, Sleep, Freeze)
- [ ] Paralysis: ¼ Speed modifier, 25% chance to skip turn
- [ ] Burn: ½ Attack modifier, 1/16 max HP end-of-turn damage
- [ ] Poison: 1/16 max HP end-of-turn damage
- [ ] Sleep: skip turns (1–7 turns in Gen 1), wake on random turn
- [ ] Freeze: skip turns, thaw on Fire move hit

## Priority 5 – Status Effects in Battle Loop
- [ ] `Battle.cs` end-of-turn processing for Burn/Poison damage
- [ ] Apply Speed/Attack modifiers from status in `DamageCalculator` and turn order

## Priority 6 – Move Selection (Player Input)
- [ ] Replace `MoveSet[0]` hardcode in `Battle.cs` with a move menu abstraction
- [ ] `IBattleInput` interface: `ChooseMoveAsync(Creature)` → supports console, AI, future UI

## Priority 7 – Experience & Catch System
- [ ] `Battle.cs` awards XP to winner on faint (Gen 1 formula)
- [ ] Basic catch mechanic using `PokemonSpecies.CatchRate`

## Priority 8 – Learnset System
- [ ] `PokemonLearnset` DB table: species ID → move ID → level learned
- [ ] Import learnsets from PokeAPI (`/pokemon/{id}/moves`)
- [ ] `Creature.InitializeFromSpecies()` populates starting moveset by level

## Tech Debt / Cleanup
- [ ] Remove dead scaffolding: `Body`, `Brain`, `BodyPart`, `Special`, `Dragon`, `CreatureType`, `Attributes.SetAttributesByCreatureType`
- [ ] Remove unused `using System.Net.NetworkInformation` from `Attributes.cs`
- [ ] Adopt EF Core migrations before schema grows further
- [ ] Decide: repurpose `Traits` as Pokémon Abilities layer, or remove

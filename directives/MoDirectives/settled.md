# Settled decisions

Closed or waived by me. Don't raise them again as findings unless their revisit condition is met.

## Closed

1. **Dropped events during a reconnect are not a desync bug.** The engine emits a turn as one burst and then
   waits for input, so a disconnect lands while nothing is emitting. If a fix is ever wanted, it is a
   one-shot resync on rebind, never an event buffer.
2. **The RNG seam is closed.** Every roll is seeded from the run seed, in tests and in production. Replacing
   the always-hit and always-crit test doubles with seeds was declined: they override the seam directly,
   which is the clean way to force an outcome.

## Waived

3. **The abandoned-run cancel race in `SignalRInput`.** A cancel that lands before a prompt installs its
   completion source leaks the abandoned run's task. *"I do not care about the whole abandoned run situation
   until I have a more transient game state."* **Revisit when** runs persist.
4. **No test pins the contents of `items.db`.** The importer only upserts, so a re-import over an old local
   database keeps a removed item. Production ships the committed, clean database. **If revisited:** a
   live-database test asserting the item names equal the importer's roster exactly, like
   `PokemonEvolutionDataContractTests`.
5. **The encounter map's orphan waypoint.** The dead-end fallback in `BiomeChoiceEvent.PickOptions` can offer
   a biome with no drawn edge, but only with one playable biome or fewer. A real run's map is always
   connected, so it cannot happen.

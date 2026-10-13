# Engine practice

Lessons Claude derived from the move batches. They serve `MoDirectives/generation-seams.md`.

1. **Seam the condition, not just the number.** A success condition or damage modifier on a move branch is
   usually generation-variable even when the move exists in every generation. Put it on the rules seam now,
   with a stub for the current generation, even before a second generation can exercise it. The test:
   "would this change in another generation?" Never edit a creature's stats to fake a modifier; pass it into
   the damage calculation. The only exception is a whole mechanic that differs between generations, with no
   shared condition to extract.
2. **Read-only code consults the seam too.** AI scoring and any other heuristic asks the rules seam for a
   generation-variable fact instead of re-deriving it.
3. **One path for damage taken.** Every hook that reacts to damage goes through `DealDamageToTarget`. Test it
   with a non-standard damage category, not only standard damage.
4. **Undo temporary changes in the in-battle reset.** A mechanic that changes permanent state for a battle
   (copied moves, types, identity) is undone in `ResetBattleState`, so a mid-battle reset like Haze undoes it,
   not only the end of battle.
5. **Read a guard at impact.** A guard that gates an effect is captured before the same action changes what
   it reads. Always test the breaking or last hit.
6. **Pin self-inflicted status.** For a move that puts a status on its user, pin that its status-effect data
   is empty and assert the foe's status is untouched.
7. **A new battle event is wired in four places:** the event type, the console emitter, the SignalR emitter
   and the client timeline (with a timeline test). Reuse an existing event where one fits.
8. **Event fields reach the client by hand.** The server projection is checked by
   `WebEventContractTests`; the client type, the timeline mapping and the rendering are not. Add a field to
   all of them.
9. **Zero-effect damage is still damage.** A standard damaging move against an immune type deals 0 damage
   rather than reporting "no effect". Test such a move against a defender that isn't immune.
10. **Refactor a central method behind the full suite.** Build the abstraction, use the full suite and
    repeated runs as the oracle, and review for identical event order and random draws.
11. **Seeded in tests is not seeded in production.** When a seam is proven in tests, check the production
    composition root that builds it too. The production run once used an unseeded rules instance while every
    test passed.

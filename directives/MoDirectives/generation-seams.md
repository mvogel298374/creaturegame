# Generation seam rules

These hold for every generation the game models.

1. **Challenge me on fidelity.** When I report a bug that may be correct behaviour for the generation being
   modelled, check it against the code, the generation-differences doc and the tests that pin the behaviour.
   Tell me plainly, citing them, before changing the engine.
2. **Primary sources outrank our docs.** Verify any generation fact against a primary source for that
   generation, such as the pret disassembly of its games (pokered for Gen 1, pokecrystal for Gen 2). Repo
   docs and code comments are secondary sources, and they have been wrong before.
3. **Every generation-specific behaviour sits behind an interface.** Any rule, number, formula or data shape
   that can differ between generations is reached through a generation seam; the engine never branches on
   the generation. This is done inside the feature itself, never left for a later cleanup. Enforced by the
   Generation seam guardrail.

# UI practice

Lessons Claude derived from frontend work.

1. **Group screen-corner controls under one anchor.** Buttons and panels that can land in the same screen
   region share one positioned flex container. Never place each one with its own absolute offsets. Before
   adding anything to a corner, check what already occupies it (nameplates, HP bars, the party strip,
   centred overlays).

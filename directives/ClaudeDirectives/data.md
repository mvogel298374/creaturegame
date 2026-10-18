# Data practice

Lessons Claude derived from data and asset work. They serve `MoDirectives/data.md`.

1. **Historical values first, overrides last.** PokeAPI returns modern values. Take the target generation's
   value from the move's historical values (`past_values`). Whatever those can't express, such as secondary
   effect chances and targets, goes in one short override block in the importer, each entry verified against
   a primary source. No hardcoding per move anywhere else.
2. **Look at an asset before using it.** Download the real source and render the sprite or tile, and any
   combination of them, before choosing it. Never pick one from memory or an assumed sheet layout.
3. **Check embedded images after a hand edit.** Hand-editing a file that embeds base64 images can corrupt a
   single character without any error. Compare the payloads with the last good version.

# Data practice

Lessons Claude derived from data and asset work. They serve `MoDirectives/data.md`.

1. **Historical values first, overrides last.** PokeAPI returns modern values. Take the target generation's
   value from the move's historical values (`past_values`). Whatever those can't express, such as secondary
   effect chances and targets, goes in one short override block in the importer, each entry verified against
   a primary source. No hardcoding per move anywhere else.
2. **Flush SQLite MCP writes.** A write through the SQLite MCP tools lands in the database's `-wal` side file,
   which git and the build both ignore. Run `PRAGMA wal_checkpoint(TRUNCATE)`, confirm `git status` shows the
   `.db` changed, and diff the rows against `HEAD` so only the intended rows changed.
3. **Look at an asset before using it.** Download the real source and render the sprite or tile, and any
   combination of them, before choosing it. Never pick one from memory or an assumed sheet layout.
4. **Check embedded images after a hand edit.** Hand-editing a file that embeds base64 images can corrupt a
   single character without any error. Compare the payloads with the last good version.

# Data rules

1. **Data changes go through the importer.** Change the importer's mapping and re-run it in full. Never edit
   a database in place.
2. **At runtime the game reads only our own data.** The server and the frontend read our databases and
   static files only. PokeAPI is used by the importer alone, so the game works offline.

# creaturegame

A Gen 1 Pokémon battle clone with a roguelite run layer, written in C# / .NET 9 with a React + Phaser 3 web client.

The battles come first: the engine reproduces Gen 1's mechanics, quirks and bugs included, and every rule that differs between generations sits behind an interface, so a later generation is a new set of implementations rather than a rewrite. Around the battles sits a roguelite run: a seeded route across Kanto's biomes, a run-scoped party, bag and wallet, rewards, shops and new party members picked up along the way.

> **Disclaimer.** This is an unofficial, non-commercial fan project, provided entirely for free — no money is charged and the author gains no monetary benefit from it (no sales, donations, ads, or other revenue). It is not affiliated with, endorsed by, or sponsored by Nintendo, Game Freak, or The Pokémon Company. Pokémon and all associated names, data, sprites, and audio are their trademarks and copyright. The repository holds the Gen 1 game data the importer derives from the community-run [PokéAPI](https://pokeapi.co) (`pokemon.db`, `moves.db`, `items.db`); **sprites and cries are not stored here** — the importer downloads them into local, git-ignored folders. The MIT license (see [`LICENSE`](LICENSE)) covers only the original source code here.

---

## What you can play today

A run starts on the Title Screen: pick a difficulty (Easy / Normal / Hard, which sets the XP pace) and a starter, and give it a nickname.

- **The route.** Each run draws a seeded, connected map of Kanto's biomes, shown as a grid Town Map. In each biome you climb a ladder of 4–6 events — wild battles, Elite and Boss trainer fights, shops, Treasure and Mystery nodes — capped by a free Poké Center, then choose the next biome. Foes scale with depth.
- **The battles.** All 165 Gen 1 moves, status conditions, stat stages, crits, PP and Struggle, a party with switching, and an in-battle bag (healing, status cures, PP restores, Revives, X-items) with party targeting. XP, level-up, learnsets with the forget-a-move prompt, Stat Exp and level evolution all work.
- **The economy.** Gold from wins, a shop priced in Gen 1's item order, and a pick-1-of-3 reward after a win or a Treasure/Mystery node: items by rarity, a gold bag, or a move a party member can learn by TM.
- **New party members.** A themed draft after some wins and a chance to recruit a defeated boss, both with a nickname prompt.
- **Resilience.** Refreshing, reopening the tab or a network blip mid-run resumes the same run, including any open prompt, within a 60-second grace window.
- **Presentation.** Gen 1 species sprites and cries, Phaser battle animations, and a Gen 1 "Kanto Sage" skin on the run's prompts and map.
- **Dev Mode** (development builds only): the foe's full stat sheet and damage ranges in the log and menus.

The run state — party, bag and wallet — ends with the run. There are no saves that survive a server restart, by design for the current phase.

The full current-state spec is [`docs/PRODUCT_SPEC.md`](docs/PRODUCT_SPEC.md).

---

## Where it's heading

The ranked, active list is [`docs/TODO.md`](docs/TODO.md). In short:

- **Gen 1 fidelity.** A set of filed Gen 1 discrepancies, each checked against the pret/pokered disassembly before it is fixed: the damage and accuracy core, Gen 1's per-creature residual order (there is no end-of-turn phase in Gen 1), the status-check order, and smaller move quirks.
- **Reliability.** Automatic E2E coverage in CI.
- **Features.** Throwing Poké Balls in battle, stone evolutions, progressive difficulty, the rest of the Gen 1 skin, and move-specific attack animations.
- **Later.** A second generation through the existing seams, then ideas from autobattlers and Pokémon Infinite Fusion (fusion, generated sprites and stats).

---

## Projects

| Project | Purpose |
|---|---|
| `creaturegame` | The battle engine and run layer, a class library with no entry point |
| `creaturegame.Web` | ASP.NET Core host — REST API, SignalR hub, and the Vite + React + Phaser 3 frontend under `ClientApp/` |
| `PokeApiConnector` | One-shot importer: Gen 1 data from PokéAPI into SQLite, plus sprite and cry downloads |
| `tests/creaturegame.Tests` | xUnit unit and integration tests, including per-move Gen 1 contract suites |

---

## Quick start

**Prerequisites:** the .NET SDK 9.0.200 (pinned in [`global.json`](global.json)) and Node.js.

```powershell
# 1. Download sprites and cries (once per clone; the databases are already committed)
dotnet run --project PokeApiConnector -- assets

# 2. Start the dev stack: backend on :5100, Vite on :5173, then open the browser
.\dev.ps1
.\stop-dev.ps1          # stop it again

# 3. Run the fast test suites (.NET xUnit, TypeScript typecheck, Vitest)
.\test.ps1 -Dotnet -Web
```

The browser suite (Playwright, a few minutes) runs separately: `.\e2e.ps1` (see [`creaturegame.Web/ClientApp/e2e/README.md`](creaturegame.Web/ClientApp/e2e/README.md)). A bare `.\test.ps1` also runs it whenever the dev stack is up.

To rebuild the databases from PokéAPI, run the importer with no arguments: `dotnet run --project PokeApiConnector`. The game itself never calls PokéAPI and works offline.

> If `dotnet` on your PATH is a runtime only, call the SDK directly, e.g. `& "$HOME\.dotnet\dotnet.exe"`.

**Deploying.** Fly.io deploys from the [`Dockerfile`](Dockerfile) only when a tag matching `v*-rc*` is pushed; pushing to `master` never deploys. `.\build-release.ps1` produces a local single-process release build.

---

## Architecture

The *why* behind the design is in [`ARCHITECTURE.md`](ARCHITECTURE.md); its §5 indexes every doc in the repo.

```
PokéAPI → PokeApiConnector → pokemon.db / moves.db / items.db → EF Core → Creature → Battle
                                                                                      │ BattleEvent
React + Phaser  ←──  SignalR (/hubs/battle)  ←──  SignalRBattleEventEmitter  ←────────┘
```

- **Event-sourced engine.** The engine does no IO. It emits `BattleEvent`s through an emitter; the web host turns them into SignalR messages, and the client expands each one into reducer actions and a Phaser animation timeline.
- **Generation seams.** `IBattleRules` (stat stages, crit and accuracy formulas, status rules, roll order), `ITypeChart` and `IStatCalculator` hold every generation-variable rule, and the engine never branches on the generation. A `GenerationProfile` bundles the seams with the generation's content, region and visual theme, chosen once per run. A test-only alternate profile keeps the seams honest. → [`docs/GENERATION_SEAMS.md`](docs/GENERATION_SEAMS.md), [`docs/GENERATION_PROFILE.md`](docs/GENERATION_PROFILE.md)
- **Run layer over battle layer.** A `RunDirector` drives the biome map and its events; each battle is one `Battle` inside it. → [`docs/GAME_LOOP.md`](docs/GAME_LOOP.md), [`docs/ENCOUNTER_DESIGN.md`](docs/ENCOUNTER_DESIGN.md)
- **Seeded randomness.** Every roll comes from the run seed through the rules seam, so a run is reproducible and a test can force any outcome.
- **Gen 1 corrections at import.** PokéAPI serves modern values; the importer maps them back to Gen 1 (base stats, single Special, move data, item prices), so the runtime reads only our own data. → [`docs/DATA_IMPORT.md`](docs/DATA_IMPORT.md)

### Gen 1 fidelity

The engine reproduces the era's quirks rather than fixing them — the Ghost → Psychic 0× type bug, the 1/256 miss on 100%-accurate moves, the DV-based HP formula, permanent Freeze, Gen 1's crit → damage → accuracy roll order. Gen 1 facts are checked against the pret/pokered disassembly, which outranks this repo's own docs. The mechanic-by-mechanic breakdown is in [`docs/GEN_DIFFERENCES.md`](docs/GEN_DIFFERENCES.md).

---

## Contributing conventions

- C# 13 / .NET 9, nullable reference types on; format with `dotnet csharpier format .`
- New engine rules that could differ by generation go through a seam, never an inline value
- Code carries almost no comments; design reasoning lives in the docs
- Tests drive the real engine; test names say what they test
- [`CLAUDE.md`](CLAUDE.md) holds the doc layers, the docs tree and the commands; [`docs/DEV_STANDARDS.md`](docs/DEV_STANDARDS.md) holds the full engineering standards

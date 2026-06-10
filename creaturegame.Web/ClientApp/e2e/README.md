# End-to-end tests (Playwright)

Browser-driven tests that exercise the real app: title → starter select → battle → attack cadence →
status/XP/level-up → the endless chain (win = "a new challenger approaches", faint = run-over/game-over).
They run against the Vite dev server (`:5173`), which proxies `/api`, `/hubs` (SignalR), `/sprites`,
`/audio` to the .NET backend (`:5100`).

## Determinism (client-only)

The test client controls only **starter species, starting level** (`startBattle(page, species, level?)`),
**and its own move each turn** — the enemy (species, level, moves) is server-side random. So the specs lean
on a few patterns instead of a seed:

- **Force a level-up** → start at level 5 (`startBattle(page, 'CHARIZARD', 5)`) and `attackUntilLog(page,
  /grew to level/)`.
- **Force a status/stat effect** → use a *player* move that applies it (e.g. Bulbasaur's Sleep Powder /
  Growth) with a **retry-until-lands** loop (Gen 1 accuracy can whiff; the L50 starter survives the misses).
- **Drive the chain** → `playToNextEncounter` (one win → intermission) / `playToRunEnd` (play to player
  faint → game-over).
- **Assert via DOM + the mitt bridge** (`bridgeEvents(page)` reads `window.__cgEvents`), never canvas pixels
  or wall-clock durations.

Enemy-inflicted status and type-immunity specifics (Confuse Ray / Glare / Thunder Wave) are covered at the
unit/integration layer — forcing them in E2E needs the backend seed hook (Tech Debt #3), not built yet.

## Specs

- `smoke` / `starter-select` — title + the 151-species select screen, level slider, confirm → battle.
- `battle` — entry, the move-menu grid, a chosen move resolves (lunge-before-hit ordering), and a won battle
  is an **intermission** (faint → "a new challenger approaches"), not a terminal "wins!".
- `cadence` — HP doesn't snap to its end-of-turn value at choose-time.
- `endless-chain` — win → intermission + a fresh enemy + carried XP; QUIT → title; play-to-faint → run-over
  summary + game-over screen.
- `level-up` — a low-level win fills XP, levels up with the fanfare (`playLevelUpSound`) + stat panel, and the
  panel stays up until the next input.
- `status` — Sleep Powder sleeps the enemy (badge on its nameplate + log line).
- `stat-stage` — Growth raises Bulbasaur's Special. `learnset` — the starter's moves come from its learnset.

## Prerequisites

**The full stack must be running** — the backend serves species/move data and drives
battles over SignalR. From the repo root:

```powershell
./dev.ps1          # starts backend (:5100) + Vite (:5173)
```

(Playwright's `webServer` will reuse the running Vite, or start one if needed, but it
does **not** start the .NET backend — so `dev.ps1` or `dotnet run --project creaturegame.Web`
must be up.)

## Running

```powershell
cd creaturegame.Web/ClientApp
npm run test:e2e          # headless, all specs
npm run test:e2e:ui       # Playwright UI mode (watch/inspect)
npx playwright test battle.spec.ts            # one file
npx playwright test -g "cadence"              # by title
npx playwright show-report                    # last HTML report
```

### In the IDE

- **JetBrains Rider / WebStorm (2023.3+)** auto-recognize Playwright tests — open any
  spec and use the green gutter arrows to run/debug a single test, a file, or the whole
  suite; results appear in the test runner tree. A shared **`E2E (Playwright)`** run
  configuration is checked in at `.run/E2E_Playwright.run.xml` (runs `npm run test:e2e`),
  so it shows in the Run/Debug dropdown out of the box. Specs import directly from
  `@playwright/test` so the gutter detection works reliably.
- **VS Code** — install the *Playwright Test for VSCode* extension; it picks up
  `playwright.config.ts` automatically.

## How it works

- Loading the app with **`?e2e=1`** (what `startBattle` does) puts it in test mode —
  `src/testEnv.ts` reads the param at load. In test mode the app:
  - exposes the Phaser bridge and **records every bridge event** on `window.__cgEvents`
    (so specs assert animation ordering, e.g. lunge-before-hit), and
  - **collapses animation delays** (and shortens the animation-complete wait) so battles
    play through fast while step ordering is preserved.
  Specs therefore import straight from `@playwright/test` (no custom fixture), which keeps
  IDE gutter-detection reliable.
- `helpers.ts` is a small page-object layer (`startBattle`, `chooseMove`, `logLines`,
  `hpWidth`, `bridgeEvents`, `playToEnd`) so specs read as intent.
- Selectors lean on stable semantic classes already in the app (`.btn-new-game`,
  `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`).

## Division of labour with unit tests

`expandEvent` (the pure event→steps mapping) is covered exhaustively by **Vitest**
(`src/battle/timeline.test.ts`) — text, sequencing, the immunity line, etc. These E2E
specs verify the **wiring** end-to-end (DOM, SignalR, the bridge), not every string.

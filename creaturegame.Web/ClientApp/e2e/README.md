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
- **Reach a deep run state** → `walkSeedsUntil(page, reached, opts)`. Anything gated on a *party larger than
  one* (both switch specs, the acquisition offer) or on *clearing a whole biome* (the Poké Center) is several
  battles deep behind the draft cadence × roll, and **a seed alone is not determinism**: the seed fixes the
  server's RNG stream, but the client's move sequence is what draws from it, so under load a polling loop's
  clicks land on different turns and play out a different run. So the helper walks a list of seeds and keeps
  the first run that gets there, clearing every between-node modal on the way. `playCurrentRunUntil` is the
  same loop without the restart — use it to carry on with the run you already reached instead of paying for a
  second walk. **Two tests that each need the same expensive reach should share one where they can**: the
  second walk is not a fresh coin-flip of the first's odds, it demonstrably fails where the first succeeded.
  Where they *can't* share (answering a prompt consumes it), give them disjoint seed lists instead.
- **Pick the lead by what the state actually needs, and measure it.** Two plausible heuristics are each wrong
  on their own. *Fewest levels to climb* gives a creature too frail to get there (CATERPIE, 21 max HP, died
  every run). *Highest BST* — correct for surviving a whole biome, which is why `poke-center` uses MEWTWO — is
  wrong for anything gated on a **level-up**: XP required per level grows cubically with level while XP earned
  grows only linearly with the level-matched enemy, so DRAGONAIR @ L54 won four battles and gained *no level at
  all*. A level-up reach must be low-level to cross at all (`evolution` CHARMANDER @ L15, `move-replacement`
  VICTREEBEL @ L12).
- **Choose the draft policy** → `drafts: 'accept' | 'decline' | 'leave'` on either loop. `accept` is the only
  way a party grows past one; `decline` keeps the run flowing while staying a party of one (which matters more
  than it looks — *every* creature that levels is eligible for the level-up prompts, so a drafted creature can
  raise the very modal a spec is waiting on and break its identity assertions); `leave` is for the spec whose
  target *is* that modal.
- **Assert via DOM + the mitt bridge** (`bridgeEvents(page)` reads `window.__cgEvents`), never canvas pixels
  or wall-clock durations.

Enemy-inflicted status and type-immunity specifics (Confuse Ray / Glare / Thunder Wave) are covered at the
unit/integration layer — forcing them in E2E needs the backend seed hook (Tech Debt #3), not built yet.

**Deliberate gap — the out-of-PP menu (In-Combat Switching, Stage C).** At 0 PP, FIGHT spends the turn as
Struggle on the spot while BAG and SWITCH stay reachable (Gen 1 keeps the whole menu open). There is **no E2E
for it and that is on purpose**: draining a full moveset to 0 PP takes tens of turns across battles, PP is
restored at every Poké Center, and no starter's low-level moveset is small enough to burn out reliably — so
any spec would be long and flaky for a behaviour that is already pinned deterministically at the layers below
(`SignalRInputTests` out-of-PP FIGHT→Struggle + SWITCH-still-honoured, `BattleVoluntarySwitchTests`
`TurnStarted.CanSwitch` true at 0 PP, and Vitest `moveMenu.test.ts` for the client `hasUsableMove` predicate).
Revisit only if a backend test hook makes the state forceable.

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
- `forced-switch` — a lead faint with a live bench raises the **blocking** send-in modal; the battle continues.
- `voluntary-switch` — the SWITCH turn-action: greyed while the starter is alone, a **dismissable** picker
  (BACK spends no turn, the creature already out is a disabled `· OUT` card), and a swap mid-battle.
- **Between-encounter prompts** — every one is a blocking modal parked on a server-side await, so each spec
  asserts *both* answers and that the run flows on either way: `evolution` (Allow / Cancel), `poke-center`
  (Heal / Skip), `move-replacement` (forget / decline, two-step confirm), `acquisition` (ADD / DECLINE on the
  themed draft).
  > Where two answers each need their own reach and can't share one, give the two tests **disjoint seed
  > lists** — the second walk is otherwise re-running the first's runs against a backend that has since
  > accumulated abandoned ones, which is when it exhausts every seed the first just succeeded on.
  > Note what "the run flows on" means for a prompt raised by a **level-up**: level-ups are paid out on a
  > *win*, so what follows is the between-encounter flow (reward choice → next node), **not** another turn.
  > Asserting FIGHT re-enables straight after the answer is wrong and fails against a perfectly healthy run.

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
  `hpWidth`, `bridgeEvents`, `playToEnd`, `walkSeedsUntil`, `playCurrentRunUntil`, `chooseBestMove`,
  `isShowing`) so specs read as intent.
- Selectors lean on stable semantic classes already in the app (`.btn-new-game`,
  `.species-card`, `.move-btn`, `.log-line`, `.bar-fill`, `.nameplate--*`).

## Division of labour with unit tests

`expandEvent` (the pure event→steps mapping) is covered exhaustively by **Vitest**
(`src/battle/timeline.test.ts`) — text, sequencing, the immunity line, etc. These E2E
specs verify the **wiring** end-to-end (DOM, SignalR, the bridge), not every string.

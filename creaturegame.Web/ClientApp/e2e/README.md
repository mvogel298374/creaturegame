# End-to-end tests (Playwright)

Browser-driven tests that exercise the real app: title → starter select → battle →
attack cadence → faint → winner. They run against the Vite dev server (`:5173`), which
proxies `/api`, `/hubs` (SignalR), `/sprites`, `/audio` to the .NET backend (`:5100`).

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

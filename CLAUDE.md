# CLAUDE.md

A Gen 1 Pokémon battle clone with a roguelite run layer: .NET 9 engine, ASP.NET + SignalR host, React/Phaser
frontend, SQLite data imported from PokeAPI. "I" and "Mo" in the directives mean the user.

## The four doc layers

This file is where everything starts. The docs are layered, and a higher layer wins over a lower one.

| Layer | Where | What it holds | Who changes it |
|:--|:--|:--|:--|
| 1. Base | `CLAUDE.md` (this file) | The layer map, the docs tree, commands, the model spread | Claude keeps it current (via `docs-cleanup`); Mo approves |
| 2. Mo directives | `directives/MoDirectives/` + `directives/Guardrails/` | Mo's rules, and the guardrails that enforce them | **Immutable to Claude.** Only Mo changes them, or dictates the change |
| 3. Claude directives | `directives/ClaudeDirectives/` | Practice Claude derived from its own work and failures | **Debatable.** Claude proposes and argues changes; Mo has the final say |
| 4. Knowledge | `ARCHITECTURE.md`, `docs/`, `.claude/` | Game and project knowledge: design, mechanics, state, process reference | Updated in the same commit as the work it describes |

Layer 2 is always loaded:

@directives/MoDirectives/authority.md
@directives/MoDirectives/planning.md
@directives/MoDirectives/product.md
@directives/MoDirectives/generation-seams.md
@directives/MoDirectives/code-style.md
@directives/MoDirectives/data.md
@directives/MoDirectives/testing.md
@directives/MoDirectives/user-communication.md
@directives/MoDirectives/settled.md
@directives/Guardrails/GUARDRAILS.md

Layer 3 loads `environment.md` always, and the rest by trigger (tree below):

@directives/ClaudeDirectives/environment.md

## Docs tree

Read a file **when the task matches its trigger**, not up front.

```
CLAUDE.md                          layer 1, always loaded
directives/
  MoDirectives/  *.md              layer 2, always loaded (imported above)
  Guardrails/GUARDRAILS.md         layer 2, always loaded
  ClaudeDirectives/
    environment.md                 layer 3, always loaded
    testing.md                     writing, running or debugging tests
    engine.md                      battle, stat, move or event work
    data.md                        importer, database or asset work
    ui.md                          frontend layout work
ARCHITECTURE.md                    the why behind a design; system map; engine at a glance (§1.1); decision log
README.md                          getting started, for humans
docs/
  TODO.md                          starting or finishing any task; the authoritative active list
  TODO_ARCHIVE.md                  only to recover the history of a finished item
  PRODUCT_SPEC.md                  what the game does today (present tense, no history)
  DEFINITION_OF_READY.md           before any implementation (G1) and in /plan work
  DEFINITION_OF_DONE.md            finishing a feature; the rubric pr-review checks
  DEV_STANDARDS.md                 /dev work: .NET/EF conventions, error handling, comment rule in full
  DESIGN_GUIDES.md                 /plan work: Gen 1 mechanics, type balancing, move-import mapping
  GEN_DIFFERENCES.md               a generation fact or quirk (secondary source; pokered outranks it)
  GAME_AVAILABILITY.md             game-version availability lookups
  GENERATION_SEAMS.md              adding a gen-variable rule or a generation; §5.0 checklist
  GENERATION_PROFILE.md            the generation switch: seams + content + region + theme
  STATE_MODEL.md                   touching battle state (Creature permanent/transient split)
  GAME_LOOP.md                     the run/roguelite loop and its event model
  ENCOUNTER_DESIGN.md              encounters, enemy tiers, acquisition channels
  DATA_IMPORT.md                   changing imported data; PokeAPI → model mapping
  RECONNECT_RESILIENCE.md          reconnect/refresh behaviour, replay, abandon timer
  SPRITE_PRESENTATION.md           sprite/cry rendering or a Phaser visual-FX pass
  FRONTEND_PLAN.md                 React + SignalR + Phaser structure and intent
.claude/
  AI_CONTEXT.md                    the /plan /dev /sync /test profiles; tooling reference
  agents/*.md                      the gate and helper subagents (models below)
creaturegame.Web/ClientApp/e2e/README.md   working on the Playwright suite
```

`docs/` mixes game knowledge (design, mechanics, state) with process reference (TODO, DoR, DoD, standards). Both
are layer 4. A new doc goes in this tree and in `ARCHITECTURE.md` §5 in the same commit.

## Projects

- **creaturegame**: the core battle engine, a class library with no entry point.
- **creaturegame.Web**: the ASP.NET Core host (REST, SignalR hub, static files) with the Vite + React + TypeScript
  frontend under `ClientApp/`.
- **PokeApiConnector**: the one-shot importer from PokeAPI into `pokemon.db`, `moves.db`, `items.db`.
- **tests/creaturegame.Tests**: xUnit, under `Unit/` and `Integration/`; namespaces match folders.

Single branch `master`, remote `origin` (GitHub). Fly.io deploys only on tags matching `v*-rc*`
(`.github/workflows/fly-deploy.yml`); pushing to `master` never deploys.

## Models

| Who | Model | Job |
|:--|:--|:--|
| Main session | Opus | Default for all work |
| `opus-engineer` | Opus | Delegated hard implementation (a new seam, battle math, a central refactor) |
| `pr-review` | Opus | Technical review (G3) |
| `requirements-review` | Sonnet | Gen 1 / roguelite fidelity review (G3) |
| `docs-cleanup` | Sonnet | Docs gate (G2) |
| `format-gate` | Haiku | CSharpier check (G3) |
| `test-runner` | Haiku | Fast suites, relays the summary (G3) |

Also available, currently unused: Fable. Each agent's model is pinned in its frontmatter.

## Profiles

`/plan` (design), `/dev` (implementation), `/sync` (data schema review), `/test` (verification) set the working
profile; definitions are in `.claude/AI_CONTEXT.md`. With no prefix, blend them; if the request could be either
design or implementation, ask which.

## Commands

`dotnet` means the SDK 9.0.200 pinned in `global.json`. On this machine the system `dotnet` is runtime-only, so
use `& "C:\Users\USER\.dotnet\dotnet.exe" …`.

```powershell
dotnet build                          # build all projects
dotnet run --project PokeApiConnector # import data from PokeAPI
.\dev.ps1 -NoBrowser                  # start backend :5100 + Vite :5173; exits 0 when both answer, 1 on failure
.\stop-dev.ps1                        # stop the dev stack
```

`.\dev.ps1` runs directly through the PowerShell tool with a ≥90 s timeout (or in the background); never wrap it in
another `pwsh` or `Start-Process`. On failure it names the side that didn't answer, or the foreign process on a dev
port (it never kills those); logs are in `.dev/backend.log` and `.dev/frontend.log`.

**Tests**: all runs go through `test.ps1`.
```powershell
.\test.ps1 -Dotnet -Web       # the fast suites: .NET xUnit + tsc typecheck + Vitest (the gate scope)
.\test.ps1 -Dotnet            # .NET only
.\test.ps1 -Filter <Name>     # .NET subset; a bare word is FullyQualifiedName~, a full --filter passes as-is
```
E2E (Playwright, ~4 min, serial, needs the stack, the only suite with real flakes) runs only on Mo's request (G6):
`.\e2e.ps1 -Spec <file>` for the narrowest run, `-Grep`, `-LastFailed`, `-Headed`, `-StartStack`, `-Bail`. Bare
`.\test.ps1` includes E2E whenever the stack is up, so it is off-limits without a request.

**Formatting and hooks** (once per clone: `dotnet tool restore`, `git config core.hooksPath .githooks`):
```powershell
dotnet csharpier format .     # format C#; never hand-align
dotnet csharpier check .      # what the hook runs
```

**EF Core migrations** need `DOTNET_ROOT` pointed at the SDK:
```powershell
$env:DOTNET_ROOT = "$HOME\.dotnet"; $env:PATH = "$HOME\.dotnet;$HOME\.dotnet\tools;$env:PATH"
dotnet ef migrations add <Name> --project creaturegame --context <ContextName> --output-dir DB/Migrations/<Moves|Pokemon|Items>
dotnet ef migrations remove --project creaturegame --context <ContextName>
```

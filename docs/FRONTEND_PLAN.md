# Frontend Plan: React + Phaser 3 + SignalR

> **See also:** `CLAUDE.md` · `TODO.md` · `DESIGN_GUIDES.md` · `DEV_STANDARDS.md`

## Overview

The frontend is a React + Phaser 3 single-page application hosted by a new ASP.NET Core
project (`creaturegame.Web`). SignalR carries real-time battle state between the server and
the browser. The existing battle engine is unchanged — only its input/output seams are adapted.

---

## Data Architecture Principle

**All runtime data comes from our own database and static file server. There are no live
calls to PokeAPI or any other external service at runtime.**

PokeAPI is used exactly once: by `PokeApiConnector`, a one-shot import tool that populates
our SQLite databases and downloads sprite assets to disk. After that, PokeAPI is out of the
picture entirely. This means:

- Species data → `pokemon.db` → `GET /api/species`
- Move data → `moves.db` → queried server-side during battle setup
- Sprites → `wwwroot/sprites/{front|back}/{id}.png` (downloaded by importer) → served as static files

This isolation is intentional. Our DB is the canonical source of truth, not PokeAPI. We
can edit, extend, or replace any data without touching the importer, and the game continues
to work offline.

**PokeApiConnector additions required before Phase 1:**
- Download front and back battle sprites for all imported species (IDs 1–151)
- Save to `creaturegame.Web/wwwroot/sprites/front/{id}.png` and `.../back/{id}.png`
- These files are committed to the repo (they are tiny — ~2 KB each, ~600 KB total)

---

**Tech stack:**

| Layer | Choice |
|:--|:--|
| App framework | React 18 + TypeScript (Vite) |
| 2D game canvas | Phaser 3 |
| Real-time comms | SignalR (`@microsoft/signalr`) |
| Routing | React Router v6 |
| Web host | ASP.NET Core (new `creaturegame.Web` project) |

---

## Screen Flow

```
Title Screen
    ↓  "New Game"
Starter Selection   ←──  GET /api/species (loads all 151 from DB)
    ↓  confirm pick   →  POST /api/game/start { speciesId }
Battle Screen       ←──  SignalR hub (BattleHub) drives the turn loop
    ↓  win or lose
Result Screen
    ↓  "Play Again"
Title Screen
```

---

## Screen Designs

### 1. Title Screen
- Pure React. No Phaser needed.
- Game logo, subtitle, "New Game" button.
- Optional: a Phaser scene behind it cycling Gen 1 sprite silhouettes — low priority, easy to add later.

### 2. Starter Selection
- Pure React.
- Grid of species cards fetched from `GET /api/species` (reads from our `pokemon.db`).
- Each card: front sprite (`/sprites/front/{id}.png` — our static file), name, types (coloured badges), base stat total.
- Click a card → confirm button lights up. Confirm → `POST /api/game/start { speciesId }`.
- Server creates the player's `Creature`, stores it in a `GameSession`, returns session state.
- On success, React Router navigates to `/battle`.

### 3. Battle Screen
Split into a React shell and a Phaser canvas region.

```
┌─────────────────────────────────────────────────────────┐
│  Dragonite   [BRN]   Lv.50                              │
│  ████████████████░░░░  HP 132 / 160                     │
│                                                         │
│              [ENEMY SPRITE]                             │
│                            [PLAYER SPRITE]              │
│                                                         │
│  Bulbasaur          Lv.50                               │
│  █████████░░░░░░░░░░  HP 88 / 120                       │
│─────────────────────────────────────────────────────────│
│  Bulbasaur used Razor Leaf!                             │
│  It's super effective!                                  │
│─────────────────────────────────────────────────────────│
│  [ FIGHT ]                          [ STATS ]           │
└─────────────────────────────────────────────────────────┘
```

**The bottom menu has two states:**

**State 1 — Action menu** (shown at the start of each turn, while awaiting input):
```
│  [ FIGHT ]                          [ STATS ]           │
```
- FIGHT → transitions to State 2 (move selection)
- STATS → opens the Stats Overlay (see Screen 3a)
- Both buttons disabled while the turn is resolving / animations playing

**State 2 — Move selection** (after clicking FIGHT):
```
│  [ Razor Leaf  PP 14/15 ] [ Quick Attack  PP 29/30 ]    │
│  [   Growl     PP 39/40 ] [   Vine Whip   PP 34/35 ]    │
│  [ ← Back ]                                             │
```
- Clicking a move sends `ChooseMove` to the server and transitions back to resolving state
- "← Back" returns to Action menu without sending a move

**Responsibility split:**

| Element | Owner | Reason |
|:--|:--|:--|
| Sprites, hit flash, bounce, faint animation | Phaser | Frame animation + tween API |
| HP bar drain | Phaser tween on a Graphics object | Smooth interpolation over time |
| Name plates, level, status badge | React | Simple DOM, easy to style |
| Battle text log | React | Scrolling text, standard HTML |
| Action menu + move buttons | React | Standard buttons, easy to disable/enable |

### 3a. Stats Overlay

Triggered by the STATS button in the battle action menu. Renders as a full-panel overlay
on top of the battle screen. Read-only — no action is taken. Closed with a CLOSE button or
Escape, returning to the action menu.

**Visual design principle:** Classic Pokémon layout sensibility (sprite left, info right;
stat rows with labels and values) with modern affordances (horizontal stat bars, type
badges with colour, clean typography). Generation-agnostic: any field absent from the
current generation's model is simply omitted — no blank placeholders.

```
┌─────────────────────────────────────────────────────────┐
│  BULBASAUR                                    No. 001   │
│  ─────────────────────────────────────────────────────  │
│                                                         │
│  [SPRITE]      Type  [GRASS] [POISON]                   │
│  96 × 96       Lv. 50        Status  [  OK  ]           │
│                                                         │
│                HP   █████████████████░░░  120 / 120     │
│                EXP  ██████████░░░░░░░░░░  125k / 133k   │
│                                                         │
│  ── STATS ───────────────────────────────────────────   │
│                                                         │
│  HP          120   ████████████████░░░░░░░░░░           │
│  Attack       69   ███████████░░░░░░░░░░░░░░░           │
│  Defense      65   ██████████░░░░░░░░░░░░░░░░           │
│  Special      85   █████████████░░░░░░░░░░░░░  ← Gen 1 │
│  Speed        65   ██████████░░░░░░░░░░░░░░░░           │
│                                                         │
│  ── MOVES ───────────────────────────────────────────   │
│                                                         │
│  Razor Leaf    [GRASS]   55 Pwr   95 Acc   PP 15 / 15   │
│  Quick Attack  [NORMAL]  40 Pwr  100 Acc   PP 30 / 30   │
│  Growl         [NORMAL]   — Pwr  100 Acc   PP 40 / 40   │
│  Vine Whip     [GRASS]   35 Pwr  100 Acc   PP 35 / 35   │
│                                                         │
│                                           [  CLOSE  ]   │
└─────────────────────────────────────────────────────────┘
```

**Gen-agnostic display rules:**

| Field | Rule |
|:--|:--|
| Special (Gen 1) | Show single "Special" stat row when `SpAtk == SpDef` and model has no separate fields |
| Sp. Atk / Sp. Def (Gen 2+) | Show as two separate rows when the model carries distinct values |
| DVs (Gen 1, 0–15) | Show as "DV" next to stat value if present on the model |
| IVs (Gen 2+, 0–31) | Show as "IV" next to stat value if present on the model |
| EVs / Stat Exp | Show a secondary bar or small number if present; omit if all zero |
| Status badge | Always shown; displays "OK" in green when `StatusCondition.None` |
| EXP bar | Omit if `GrowthRate` is not set or experience system is not active for this run |
| Type 2 | Omit the second badge if `Type2` is null |
| Move power | Show "—" for status moves with `BaseDamage == 0` |

**Stat bars:** Each bar is scaled relative to 255 (the Gen 1 max for any stat at level 100
with max DVs and Stat Exp). This gives a consistent visual reference across all generations.

### 4. Result Screen
- Pure React overlay (no Phaser scene change needed).
- "You won!" / "You lost!" + "Play Again" button back to title.

---

## Project Structure

### .NET side — new project `creaturegame.Web`

```
creaturegame.Web/
  Program.cs                   ← ASP.NET Core host, DI, SignalR, CORS
  Hubs/
    BattleHub.cs               ← SignalR hub, receives ChooseMove from client
  Services/
    GameSessionManager.cs      ← maps connectionId → GameSession
    GameSession.cs             ← holds Battle, SignalRInput, SignalREventEmitter
  Controllers/
    SpeciesController.cs       ← GET /api/species  (reads pokemon.db — our DB, not PokeAPI)
    GameController.cs          ← POST /api/game/start
  Events/
    BattleEvent.cs             ← discriminated union of all battle event types (see below)
    IBattleEventEmitter.cs     ← interface injected into Battle/AttackAction/StatusResolver
    ConsoleBattleEventEmitter  ← existing Console.WriteLine behaviour, preserves dev tool
    SignalRBattleEventEmitter  ← pushes typed events through hub to client
  wwwroot/
    sprites/
      front/                   ← {id}.png — downloaded by PokeApiConnector at import time
      back/                    ← {id}.png — downloaded by PokeApiConnector at import time
```

### React side — `creaturegame.Web/ClientApp`

```
src/
  pages/
    TitleScreen.tsx
    StarterSelection.tsx
    BattleScreen.tsx           ← owns SignalR connection, passes events down
  components/
    battle/
      BattleCanvas.tsx         ← mounts Phaser, bridges React↔Phaser events
      HpBar.tsx
      NamePlate.tsx
      BattleLog.tsx
      ActionMenu.tsx           ← top-level menu: FIGHT | STATS (State 1)
      MoveMenu.tsx             ← 4 move buttons + Back (State 2)
      StatsOverlay.tsx         ← full-screen stats panel, triggered by STATS button
      StatBar.tsx              ← single horizontal stat bar (reused for each stat row)
      TypeBadge.tsx            ← coloured type pill (GRASS, POISON, etc.)
  phaser/
    scenes/
      BattleScene.ts           ← sprites, tweens, animations
    BattleGame.ts              ← Phaser.Game config
  hooks/
    useBattleHub.ts            ← SignalR connection + event subscriptions
    useBattleState.ts          ← reducer for all mutable battle state
  types/
    BattleEvents.ts            ← TypeScript mirror of C# BattleEvent types
    Snapshots.ts               ← TypeScript mirror of CreatureSnapshot / MoveSnapshot
```

---

## Battle Engine Output Abstraction

This is a prerequisite step before any frontend work. Currently `Console.WriteLine` is
scattered throughout `Battle`, `AttackAction`, and `StatusResolver`. The web layer needs
structured events, not strings.

### BattleEvent types (C# records)

```csharp
public abstract record BattleEvent;

// Turn lifecycle
public record TurnStarted(int TurnNumber, BattleStateSnapshot State) : BattleEvent;
public record TurnEnded(BattleStateSnapshot State) : BattleEvent;

// Actions
public record MoveUsed(string AttackerName, string MoveName) : BattleEvent;
public record MoveMissed(string AttackerName, string MoveName) : BattleEvent;
public record DamageDealt(string TargetName, int Damage, double TypeEffectiveness, bool IsCrit, int HpAfter, int HpMax) : BattleEvent;
public record RecoilDamage(string SourceName, int Damage, int HpAfter) : BattleEvent;

// Status
public record StatusApplied(string TargetName, StatusCondition Status) : BattleEvent;
public record StatusDamage(string TargetName, int Damage, StatusCondition Source, int HpAfter) : BattleEvent;
public record StatusCleared(string TargetName, StatusCondition WasStatus, string Reason) : BattleEvent;
public record ActionBlocked(string CreatureName, StatusCondition Reason) : BattleEvent;

// Battle end
public record CreatureFainted(string Name) : BattleEvent;
public record BattleEnded(string WinnerName) : BattleEvent;

// Snapshot sent at turn start so client always has authoritative state
public record BattleStateSnapshot(
    CreatureSnapshot Player,
    CreatureSnapshot Enemy
);

// Full creature display data — everything the Stats Overlay needs
public record CreatureSnapshot(
    string  Name,
    int     SpeciesId,          // used to derive sprite URL: /sprites/front/{id}.png
    int     Level,
    int     Hp,
    int     MaxHp,
    int     Experience,
    int     ExperienceToNextLevel,
    StatusCondition Status,
    DamageType  Type1,
    DamageType? Type2,          // null → omit second type badge

    // Stats — all present for Gen 1; Gen 2+ adds SpAtk/SpDef separately
    int StatHp,
    int StatAttack,
    int StatDefense,
    int? StatSpecial,           // Gen 1 unified Special; null in Gen 2+
    int? StatSpAtk,             // Gen 2+ only; null in Gen 1
    int? StatSpDef,             // Gen 2+ only; null in Gen 1
    int StatSpeed,

    // Optional IV/DV display (null = omit from overlay)
    int? DvAttack,  int? DvDefense, int? DvSpecial, int? DvSpeed,

    IReadOnlyList<MoveSnapshot> Moves
);

public record MoveSnapshot(
    string      Name,
    DamageType  Type,
    int         BaseDamage,     // 0 = status move → display "—"
    int         Accuracy,
    int         PpCurrent,
    int         PpMax
);
```

### IBattleEventEmitter

```csharp
public interface IBattleEventEmitter
{
    void Emit(BattleEvent evt);
}
```

Injected into `Battle`, `AttackAction`, and `StatusResolver` (same pattern as `IBattleRules`).
Every `Console.WriteLine` is replaced by a typed `_emitter.Emit(new SomeEvent(...))`.

`ConsoleBattleEventEmitter` formats events as human-readable strings — preserves the
existing console runner behaviour exactly. `SignalRBattleEventEmitter` serialises events
as JSON and pushes them through the hub.

---

## React ↔ Phaser Bridge

Phaser mounts inside a React `<div>` ref. After mounting, all communication is via
Phaser's built-in EventEmitter. React never touches Phaser internals; Phaser never
touches React state.

```
React (receives SignalR events)
  │
  │  scene.events.emit('battleEvent', payload)
  ▼
Phaser BattleScene (plays animations)
  │
  │  this.game.events.emit('animationComplete')
  ▼
React (re-enables move menu, advances log)
```

**React → Phaser:** after receiving a `DamageDealt` event, React calls:
```ts
scene.events.emit('damageDealt', { target: 'enemy', hpAfter: 87, hpMax: 160 });
```

**Phaser → React:** after all tweens for a turn resolve:
```ts
// Phaser BattleScene
this.game.events.emit('turnAnimationComplete');
```
```ts
// React useBattleHub hook
gameRef.current.events.on('turnAnimationComplete', () => setAwaitingInput(true));
```

### Animation queue

A turn may produce multiple sequential events (MoveUsed → DamageDealt → StatusApplied).
These must play in order, not simultaneously. React queues events and feeds them to Phaser
one at a time, waiting for `turnAnimationComplete` between each.

---

## SignalR Hub Contract

### Server → Client

| Message | Payload | When |
|:--|:--|:--|
| `TurnStarted` | `BattleStateSnapshot` + available moves | Start of each turn — client shows move menu |
| `BattleEvent` | Any `BattleEvent` subtype + discriminator | During turn resolution, one per event |
| `BattleEnded` | `{ winnerName }` | When a creature faints |

### Client → Server

| Message | Payload | When |
|:--|:--|:--|
| `ChooseMove` | `{ moveIndex: number }` | Player clicks a move button |

### IBattleInput → SignalR (server side)

`SignalRInput` implements `IBattleInput`. It holds a `TaskCompletionSource<PokemonAttack>`
that suspends `ChooseMoveAsync` until `BattleHub.ChooseMove` resolves it:

```csharp
public class SignalRInput : IBattleInput
{
    private TaskCompletionSource<PokemonAttack> _tcs = new();

    public void SubmitMove(PokemonAttack move) => _tcs.SetResult(move);

    public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
    {
        _tcs = new TaskCompletionSource<PokemonAttack>();
        // also push TurnStarted event to client so it knows which moves are available
        return _tcs.Task;
    }
}
```

The battle loop continues running on the server exactly as today. The only change is that
`AutoSelectInput` is replaced by `SignalRInput` for the player side.

---

## Implementation Phases

### Phase 1 — .NET web project & plumbing
- Extend `PokeApiConnector` to download front/back sprites for IDs 1–151 into `wwwroot/sprites/`
  (one-time import; after this, no external API calls are needed at runtime)
- Add `creaturegame.Web` project (ASP.NET Core, no views, API only)
- Configure static file serving for `wwwroot/sprites/`
- Add SignalR, configure CORS for Vite dev server (`localhost:5173`)
- Stub `BattleHub`, `SpeciesController` (reads `pokemon.db`), `GameController`
- Verify hub connection from a browser console test

### Phase 2 — React app skeleton
- Vite + React + TypeScript in `creaturegame.Web/ClientApp`
- React Router: `/` → Title, `/select` → StarterSelection, `/battle` → Battle
- Install `@microsoft/signalr`, `phaser`
- Connect to SignalR hub, log received events to console

### Phase 3 — Battle engine output abstraction
- Define `BattleEvent` record hierarchy and `IBattleEventEmitter`
- Replace all `Console.WriteLine` in `Battle`, `AttackAction`, `StatusResolver` with `_emitter.Emit(...)`
- `ConsoleBattleEventEmitter` preserves console runner
- Wire `SignalRBattleEventEmitter` to push events through the hub
- All 63 existing tests still pass

### Phase 4 — Title screen
- Simple React component, "New Game" navigates to `/select`
- No backend work needed

### Phase 5 — Starter selection
- `GET /api/species` returns list of `PokemonSpecies` from `pokemon.db` (our DB)
- React constructs sprite URL as `/sprites/front/{id}.png` from the species `Id` field
- React grid of species cards (name, type badges, locally-served sprite)
- `POST /api/game/start { speciesId }` creates `GameSession` keyed by SignalR connectionId
- Navigate to `/battle` on success

### Phase 6 — Battle screen shell (React, no Phaser yet)
- `useBattleHub` hook: connect, subscribe to `TurnStarted` / `BattleEvent` / `BattleEnded`
- `useBattleState` reducer: tracks both `CreatureSnapshot`s, menu state, overlay visibility
- React renders HP bars (static width for now), text log
- **Action menu (State 1):** FIGHT and STATS buttons; both disabled while turn is resolving
  - FIGHT → transitions bottom panel to move selection (State 2)
  - STATS → sets `statsOpen = true`, renders `StatsOverlay` for the player's creature
- **Move selection (State 2):** 4 move buttons + Back button
  - Clicking a move sends `ChooseMove` to server, transitions to resolving state
  - Back returns to State 1 without sending a move
- **`StatsOverlay`:** renders full creature panel from `CreatureSnapshot` in `useBattleState`
  - Stat bars scaled to 255; gen-agnostic rules applied (Special vs SpAtk/SpDef, null type2, etc.)
  - Type badges from `TypeBadge` component with colour per type
  - Closes on button click or Escape, returns to action menu
- Full turn loop working end-to-end, output is text only

### Phase 7 — Phaser canvas
- `BattleCanvas.tsx` mounts Phaser game, exposes event bridge
- `BattleScene.ts`: `preload` loads sprites from `/sprites/front/{id}.png` and `/sprites/back/{id}.png`
  (our static files — no external URL); `create` positions them
- Sprites visible, inanimate

### Phase 8 — Animations
Wire each `BattleEvent` type to a Phaser animation:

| Event | Animation |
|:--|:--|
| `MoveUsed` | Attacker sprite bobs toward enemy (short tween) |
| `DamageDealt` | Target flashes white 2× ; HP bar drains to new value |
| `MoveMissed` | Brief screen-side dodge tween on target |
| `StatusApplied` | Coloured overlay flash on target (red=burn, yellow=para, etc.) |
| `ActionBlocked` | Target shakes in place, no movement |
| `CreatureFainted` | Sprite slides down + alpha 0 tween |

Each animation calls `game.events.emit('turnAnimationComplete')` when done.
React only re-enables the move menu after receiving that signal.

### Phase 9 — End-to-end polish
- `BattleEnded` → Result overlay in React (win/lose + "Play Again")
- Effectiveness label in text log ("It's super effective!" from `TypeEffectiveness` field)
- Status badge on name plate updates with `StatusApplied` / `StatusCleared`
- `Program.cs` console runner retained as dev/debug tool unchanged

---

## Out of Scope for This Plan
These belong to later priorities in `TODO.md` but slot in naturally after this:
- AI enemy (Priority 10) — just swap `AutoSelectInput` with `GreedyAIInput` on the enemy side
- Multiple battles / roguelike loop — `GameSession` extended with run state
- Sound effects — Phaser has an Audio manager; Gen 1 SFX are publicly available

---

## See Also

| File | Role |
|:-----|:-----|
| `CLAUDE.md` | Session setup, build commands |
| `TODO.md` | Authoritative task list — Priority 11 references this plan |
| `DESIGN_GUIDES.md` | Gen 1 mechanics |
| `DEV_STANDARDS.md` | .NET coding conventions |

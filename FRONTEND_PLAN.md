# Frontend Plan: React + Phaser 3 + SignalR

> **See also:** `CLAUDE.md` · `TODO.md` · `DESIGN_GUIDES.md` · `DEV_STANDARDS.md`

## Overview

The frontend is a React + Phaser 3 single-page application hosted by a new ASP.NET Core
project (`creaturegame.Web`). SignalR carries real-time battle state between the server and
the browser. The existing battle engine is unchanged — only its input/output seams are adapted.

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
- Grid of species cards fetched from `GET /api/species`.
- Each card: front sprite, name, types (coloured badges), base stat total.
- Click a card → confirm button lights up. Confirm → `POST /api/game/start { speciesId }`.
- Server creates the player's `Creature`, stores it in a `GameSession`, returns session state.
- On success, React Router navigates to `/battle`.

### 3. Battle Screen
Split into a React shell and a Phaser canvas region.

```
┌─────────────────────────────────────────────────────────┐
│  Enemy nameplate   [status badge]   Lv.50               │
│  ████████████████████░░░  HP 132/160                    │
│                                                         │
│              [ENEMY SPRITE]                             │
│                            [PLAYER SPRITE]              │
│                                                         │
│  Player nameplate  [status badge]   Lv.50               │
│  █████████████░░░░░░░░░░░  HP 88/120                    │
│─────────────────────────────────────────────────────────│
│  Bulbasaur used Razor Leaf!                             │
│  It's super effective!                                  │
│─────────────────────────────────────────────────────────│
│  [ Razor Leaf  PP 14/15 ] [ Quick Attack  PP 29/30 ]    │
│  [   Growl     PP 39/40 ] [   Vine Whip   PP 34/35 ]    │
└─────────────────────────────────────────────────────────┘
```

**Responsibility split:**

| Element | Owner | Reason |
|:--|:--|:--|
| Sprites, hit flash, bounce, faint animation | Phaser | Frame animation + tween API |
| HP bar drain | Phaser tween on a Graphics object | Smooth interpolation over time |
| Name plates, level, status badge | React | Simple DOM, easy to style |
| Battle text log | React | Scrolling text, standard HTML |
| Move menu buttons (4 + PP) | React | Standard buttons, easy to disable/enable |

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
    SpeciesController.cs       ← GET /api/species
    GameController.cs          ← POST /api/game/start
  Events/
    BattleEvent.cs             ← discriminated union of all battle event types (see below)
    IBattleEventEmitter.cs     ← interface injected into Battle/AttackAction/StatusResolver
    ConsoleBattleEventEmitter  ← existing Console.WriteLine behaviour, preserves dev tool
    SignalRBattleEventEmitter  ← pushes typed events through hub to client
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
      MoveMenu.tsx
      BattleLog.tsx
  phaser/
    scenes/
      BattleScene.ts           ← sprites, tweens, animations
    BattleGame.ts              ← Phaser.Game config
  hooks/
    useBattleHub.ts            ← SignalR connection + event subscriptions
    useBattleState.ts          ← reducer for all mutable battle state
  types/
    BattleEvents.ts            ← TypeScript mirror of C# BattleEvent types
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
    string PlayerName, int PlayerHp, int PlayerMaxHp, StatusCondition PlayerStatus,
    string EnemyName,  int EnemyHp,  int EnemyMaxHp,  StatusCondition EnemyStatus,
    IReadOnlyList<MoveSnapshot> PlayerMoves
);
public record MoveSnapshot(string Name, int PpCurrent, int PpMax);
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
- Add `creaturegame.Web` project (ASP.NET Core, no views, API only)
- Add SignalR, configure CORS for Vite dev server (`localhost:5173`)
- Stub `BattleHub`, `SpeciesController`, `GameController`
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
- `GET /api/species` returns list of `PokemonSpecies` from DB
- React grid of species cards (name, type badges, sprite URL)
- `POST /api/game/start { speciesId }` creates `GameSession` keyed by SignalR connectionId
- Navigate to `/battle` on success

### Phase 6 — Battle screen shell (React, no Phaser yet)
- `useBattleHub` hook: connect, subscribe to `TurnStarted` / `BattleEvent` / `BattleEnded`
- `useBattleState` reducer: tracks both creatures' HP, status, player moves + PP
- React renders HP bars (static width for now), move menu (disabled until `TurnStarted`), text log
- Player clicks move → `connection.invoke('ChooseMove', index)` → move menu disables
- Full turn loop working end-to-end, output is text only

### Phase 7 — Phaser canvas
- `BattleCanvas.tsx` mounts Phaser game, exposes event bridge
- `BattleScene.ts`: `preload` loads back/front sprites; `create` positions them
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

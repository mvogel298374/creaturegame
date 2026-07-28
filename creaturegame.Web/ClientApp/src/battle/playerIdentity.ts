import type { Payload } from './timeline';

/// The one place that answers "which creature is the player right now?" from an incoming battle event.
///
/// `expandEvent` splits every event into player-side vs enemy-side by NAME, so this ref is what decides whose
/// moves, damage and status the UI attributes to whom. Getting it wrong doesn't just mislabel a nameplate — it
/// sends the player's own hits to the enemy side of the screen.
///
/// Four events change the answer, and they are genuinely different situations:
///  - `BattleStarted`      — a new encounter names its lead outright.
///  - `CreatureSwitchedIn` — someone took the field mid-battle (forced faint-switch or the voluntary SWITCH).
///  - `LeadChanged`        — the lead was reassigned OUT of battle (between-biome swap, post-mutual-KO
///                           promotion); nobody enters the field, so no `CreatureSwitchedIn` announces it.
///  - `CreatureEvolved`    — the creature was RENAMED in place; nobody enters or leaves.
///
/// Kept as a pure function (type-only imports, zero runtime deps) for the same reason `battleReducer` was
/// extracted from the hook: the rule is decision logic Vitest can pin exactly, while the hook around it is
/// SignalR + refs that only a DOM harness could drive.
export function nextPlayerName(eventType: string, payload: Payload, current: string): string {
  switch (eventType) {
    case 'BattleStarted':
      return payload.playerName as string;
    case 'CreatureSwitchedIn':
    case 'LeadChanged':
      return payload.name as string;
    // Guarded on the OLD name matching the current player. Evolution is party-wide — BattleRunEvent's
    // EvolutionOrder offers to every member that levelled, not just the on-field one — so an unguarded retarget
    // would hand the player identity to a BENCH creature the moment one evolved.
    case 'CreatureEvolved':
      return payload.fromName === current ? (payload.toName as string) : current;
    default:
      return current;
  }
}

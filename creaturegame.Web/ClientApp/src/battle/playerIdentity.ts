import type { Payload } from './timeline';

/// The one place that answers "which creature is the player right now?" from an incoming battle event.
///
/// `expandEvent` splits every event into player-side vs enemy-side by creature ID, so this ref is what decides
/// whose moves, damage and status the UI attributes to whom. Getting it wrong doesn't just mislabel a
/// nameplate — it sends the player's own hits to the enemy side of the screen.
///
/// Keyed on the id, never the display name: two creatures can share a name (a wild PIDGEY against your PIDGEY, or
/// a same-named bench twin), and a name comparison can't tell them apart. See ARCHITECTURE.md §2.2.
///
/// Four events change the answer, and they are genuinely different situations:
///  - `BattleStarted`      — a new encounter names its lead outright.
///  - `CreatureSwitchedIn` — someone took the field mid-battle (forced faint-switch or the voluntary SWITCH).
///  - `LeadChanged`        — the lead was reassigned OUT of battle (between-biome swap, post-mutual-KO
///                           promotion); nobody enters the field, so no `CreatureSwitchedIn` announces it.
///  - `TurnStarted`        — the server restating who is on the field; the self-correction for a reconnect replay.
///
/// `CreatureEvolved` is deliberately NOT one of them any more: an evolution renames the creature in place, but its
/// id is stable across the rename, so "who is the player" is unchanged whether the on-field creature or a bench
/// member evolved (the old name-keyed version needed a guard to avoid handing the player identity to a bench
/// creature).
///
/// Kept as a pure function (type-only imports, zero runtime deps) for the same reason `battleReducer` was
/// extracted from the hook: the rule is decision logic Vitest can pin exactly, while the hook around it is
/// SignalR + refs that only a DOM harness could drive.
export function nextPlayerId(eventType: string, payload: Payload, current: number): number {
  switch (eventType) {
    case 'BattleStarted':
      return payload.playerId as number;
    case 'CreatureSwitchedIn':
    case 'LeadChanged':
      return payload.id as number;
    // The authority's own statement of who is on the field this turn. Normally redundant with the events above;
    // its job is the reconnect replay, which re-sends only the CACHED BattleStarted (naming the original lead) plus
    // the latest TurnStarted — so after a mid-battle switch-in and a page refresh, this is what puts the player id
    // back on the creature that is actually fighting.
    case 'TurnStarted':
      return payload.playerId as number;
    default:
      return current;
  }
}

import type { Payload } from './timeline';

/// Does this incoming event merely re-send state the client already has on screen?
///
/// On EVERY hub (re)connect the server replays its cached state-establishing events (ARCHITECTURE.md §2.7). After a
/// refresh the client's state is fresh, so the replay is exactly what it needs. After a transport blip the client's
/// React state never unmounted, and applying the replay again is actively wrong: a second `BattleStarted` bumps the
/// encounter index ("A new challenger approaches!", enemy re-slides, HP flashes), and a second `BiomeEntered` /
/// `BiomeNodePlanRevealed` duplicates the route path and resets the node pin. The server can't tell the two cases
/// apart, so the client does — by comparing the event against what its own state already holds.
///
/// `TurnStarted` is deliberately NOT here: re-applying it is the designed self-correction for a replay.
///
/// Pure, with type-only imports, so Vitest pins the rule without React or SignalR (same shape as `nextPlayerId`).
///
/// The biome/plan half reads ARRIVAL-order trackers, not the reducer state: events are queued behind animations
/// (§2.8), so state lags arrival and a genuine node plan can land while `mapNodePlan` still holds the previous
/// biome's. The hook keeps the trackers current via `afterAcceptedEvent`.
export interface ReplayView {
  /** The enemy of the battle on screen (reducer state — a replay can never arrive before its original applied). */
  enemyId: number;
  /** The biome of the last BiomeEntered the hook accepted, or null before any. */
  arrivedBiomeId: string | null;
  /** The biome whose node plan the hook last accepted, or null (reset by each accepted BiomeEntered). */
  planBiomeId: string | null;
}

export function isReplayOfKnownState(eventType: string, payload: Payload, view: ReplayView): boolean {
  switch (eventType) {
    // Creature ids are unique per instance (CreatureIdSource), so the same enemy id is the same encounter.
    case 'BattleStarted':
      return view.enemyId !== 0 && payload.enemyId === view.enemyId;
    // A hop always goes to a different biome, so re-entering the one just entered can only be a replay.
    case 'BiomeEntered':
      return view.arrivedBiomeId !== null && payload.biomeId === view.arrivedBiomeId;
    // A plan already accepted for the current biome can only be sent again by a replay.
    case 'BiomeNodePlanRevealed':
      return view.planBiomeId !== null && view.planBiomeId === view.arrivedBiomeId;
    default:
      return false;
  }
}

/// Advances the arrival trackers for an event the hook accepted (did not skip).
export function afterAcceptedEvent(eventType: string, payload: Payload, view: ReplayView): ReplayView {
  if (eventType === 'BiomeEntered') return { ...view, arrivedBiomeId: payload.biomeId as string, planBiomeId: null };
  if (eventType === 'BiomeNodePlanRevealed') return { ...view, planBiomeId: view.arrivedBiomeId };
  return view;
}

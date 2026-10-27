import type { Payload } from './timeline';
import type { PromptKey } from '../hooks/battleReducer';

// Skips a reconnect replay of state the client already holds (ARCHITECTURE.md §2.7; RECONNECT_RESILIENCE.md R1d-e).
export interface ReplayView {
  /** The enemy of the battle on screen (reducer state — a replay can never arrive before its original applied). */
  enemyId: number;
  /** The biome of the last BiomeEntered the hook accepted, or null before any. */
  arrivedBiomeId: string | null;
  /** The biome whose node plan the hook last accepted, or null (reset by each accepted BiomeEntered). */
  planBiomeId: string | null;
  /** The logged prompt the hook last accepted and the player has not answered, or null. */
  openPrompt: string | null;
}

// Prompts whose intro writes to the log, by the reducer slot their answer clears (RECONNECT_RESILIENCE.md R1d-e).
const LOGGED_PROMPTS: Partial<Record<string, PromptKey>> = {
  RecoveryOffered: 'recovery',
  MoveReplacementRequired: 'moveReplacement',
  EvolutionOffered: 'evolution',
};

const loggedPromptKey = (eventType: string) =>
  Object.hasOwn(LOGGED_PROMPTS, eventType) ? LOGGED_PROMPTS[eventType] : undefined;

export function isReplayOfKnownState(eventType: string, payload: Payload, view: ReplayView): boolean {
  if (loggedPromptKey(eventType)) return view.openPrompt === eventType;
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
  if (loggedPromptKey(eventType)) return { ...view, openPrompt: eventType };
  return view;
}

/// The player answered the prompt in `key`: a later replay of it is the accepted race and must show again.
export function afterPromptAnswered<V extends { openPrompt: string | null }>(view: V, key: PromptKey): V {
  return view.openPrompt !== null && loggedPromptKey(view.openPrompt) === key ? { ...view, openPrompt: null } : view;
}

/// The answer's send was rejected and the modal restored: the prompt is open again.
export function afterPromptRestored<V extends { openPrompt: string | null }>(view: V, key: PromptKey): V {
  const eventType = Object.keys(LOGGED_PROMPTS).find(t => LOGGED_PROMPTS[t] === key);
  return eventType ? { ...view, openPrompt: eventType } : view;
}

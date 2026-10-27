import { expandEvent, type Expansion, type Payload } from './timeline';
import { isReplayOfKnownState, afterAcceptedEvent } from './replayDedupe';
import { nextPlayerId } from './playerIdentity';
import {
  arrivalBossName, nextBossArrival, nextBossNodeActive, NO_BOSS_ARRIVAL, type BossArrival,
} from './bossTrainer';

// useBattleHub's per-event bookkeeping, pure so Vitest drives the real sequence (RECONNECT_RESILIENCE.md R1d-d).
export interface Intake {
  tracker: { arrivedBiomeId: string | null; planBiomeId: string | null };
  playerId: number;
  encounterIndex: number;
  bossNodeActive: boolean;
  bossArrival: BossArrival;
}

export const FRESH_INTAKE: Intake = {
  tracker: { arrivedBiomeId: null, planBiomeId: null },
  playerId: 0,
  encounterIndex: 0,
  bossNodeActive: false,
  bossArrival: NO_BOSS_ARRIVAL,
};

// Null when the event only re-sends state the client already holds (replayDedupe.ts).
export function intakeEvent(
  intake: Intake,
  eventType: string,
  payload: Payload,
  screen: { enemyId: number; biomeName: string },
): { intake: Intake; expansion: Expansion } | null {
  const replayView = { ...intake.tracker, enemyId: screen.enemyId };
  if (isReplayOfKnownState(eventType, payload, replayView)) return null;
  const { arrivedBiomeId, planBiomeId } = afterAcceptedEvent(eventType, payload, replayView);
  const next: Intake = {
    tracker: { arrivedBiomeId, planBiomeId },
    // Retargeted BEFORE the event expands, so the newly-named creature's own moves are sided correctly.
    playerId: nextPlayerId(eventType, payload, intake.playerId),
    encounterIndex: intake.encounterIndex + (eventType === 'BattleStarted' ? 1 : 0),
    bossNodeActive: nextBossNodeActive(eventType, payload, intake.bossNodeActive),
    bossArrival: nextBossArrival(eventType, payload, intake.bossArrival),
  };
  const expansion = expandEvent(eventType, payload, {
    playerId: next.playerId,
    encounterIndex: next.encounterIndex,
    bossTrainerName: arrivalBossName(next.bossArrival),
    isBossBattle: next.bossNodeActive,
    biomeName: screen.biomeName,
  });
  return { intake: next, expansion };
}

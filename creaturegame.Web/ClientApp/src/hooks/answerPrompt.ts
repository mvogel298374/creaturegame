// Hide the modal, send the answer, restore the modal if the send is rejected (ARCHITECTURE.md §2.7).
import type { Action } from '../battle/timeline';
import type { BattleState, PromptKey } from './battleReducer';
import { intakePromptAnswered, intakePromptRestored, type Intake } from '../battle/eventIntake';

export interface PromptAnswer<K extends PromptKey = PromptKey> {
  key: K;
  // The prompt as it was when the player answered (null when none is open — nothing to restore).
  prompt: BattleState[K];
  hide: Action;
  method: string;
  dispatch: (action: Action) => void;
  // Undefined when there is no live connection object (torn down) — the answer is simply not sent.
  invoke: () => Promise<unknown> | undefined;
  onRestore?: () => void;
}

export function submitPromptAnswer<K extends PromptKey>(
  { key, prompt, hide, method, dispatch, invoke, onRestore }: PromptAnswer<K>,
): void {
  dispatch(hide);
  invoke()?.catch(err => {
    console.error(`[SignalR] ${method} failed:`, err);
    if (!prompt) return;
    // The mapped Action type can't be narrowed through a generic K; key and prompt are tied by PromptAnswer<K>.
    dispatch({ type: 'RESTORE_PROMPT', key, value: prompt } as Action);
    onRestore?.();
  });
}

// useBattleHub's answer path: the open-prompt tracker clears on the answer and re-arms on a restore (R1d-e).
export function submitTrackedPromptAnswer<K extends PromptKey>(
  intakeRef: { current: Intake },
  answer: Omit<PromptAnswer<K>, 'onRestore'>,
): void {
  intakeRef.current = intakePromptAnswered(intakeRef.current, answer.key);
  submitPromptAnswer({
    ...answer,
    onRestore: () => { intakeRef.current = intakePromptRestored(intakeRef.current, answer.key); },
  });
}

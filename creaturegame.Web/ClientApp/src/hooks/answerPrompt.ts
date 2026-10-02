// Answering a blocking prompt: hide the modal at once (the run is parked server-side on it), send the answer, and
// bring the modal BACK if the send is rejected. SignalR rejects an invoke made while the connection is
// reconnecting, so without the restore the answer was lost AND the modal gone — the server stayed parked on a
// prompt the player could no longer see or answer (design note: ARCHITECTURE.md §2.7). A pure function over an
// injected dispatch/invoke so it is unit-testable without React or SignalR.
import type { Action } from '../battle/timeline';
import type { BattleState, PromptKey } from './battleReducer';

export interface PromptAnswer {
  key: PromptKey;
  // The prompt as it was when the player answered (null when none is open — nothing to restore).
  prompt: BattleState[PromptKey];
  hide: Action;
  method: string;
  dispatch: (action: Action) => void;
  // Undefined when there is no live connection object (torn down) — the answer is simply not sent.
  invoke: () => Promise<unknown> | undefined;
}

export function submitPromptAnswer({ key, prompt, hide, method, dispatch, invoke }: PromptAnswer): void {
  dispatch(hide);
  invoke()?.catch(err => {
    console.error(`[SignalR] ${method} failed:`, err);
    if (prompt) dispatch({ type: 'RESTORE_PROMPT', key, value: prompt });
  });
}

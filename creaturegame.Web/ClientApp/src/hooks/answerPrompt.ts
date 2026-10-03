// Answering a blocking prompt: hide the modal at once (the run is parked server-side on it), send the answer, and
// bring the modal BACK if the send is rejected. SignalR rejects an invoke made while the connection is
// reconnecting, so without the restore the answer was lost AND the modal gone — the server stayed parked on a
// prompt the player could no longer see or answer (design note: ARCHITECTURE.md §2.7). A pure function over an
// injected dispatch/invoke so it is unit-testable without React or SignalR.
import type { Action } from '../battle/timeline';
import type { BattleState, PromptKey } from './battleReducer';

export interface PromptAnswer<K extends PromptKey = PromptKey> {
  key: K;
  // The prompt as it was when the player answered (null when none is open — nothing to restore).
  prompt: BattleState[K];
  hide: Action;
  method: string;
  dispatch: (action: Action) => void;
  // Undefined when there is no live connection object (torn down) — the answer is simply not sent.
  invoke: () => Promise<unknown> | undefined;
}

export function submitPromptAnswer<K extends PromptKey>({ key, prompt, hide, method, dispatch, invoke }: PromptAnswer<K>): void {
  dispatch(hide);
  invoke()?.catch(err => {
    console.error(`[SignalR] ${method} failed:`, err);
    // The mapped Action type can't be narrowed through a generic K; key and prompt are tied by PromptAnswer<K>.
    if (prompt) dispatch({ type: 'RESTORE_PROMPT', key, value: prompt } as Action);
  });
}

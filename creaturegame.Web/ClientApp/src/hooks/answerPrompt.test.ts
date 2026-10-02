import { describe, it, expect, vi, afterEach } from 'vitest';
import { submitPromptAnswer } from './answerPrompt';
import { battleReducer, initialState } from './battleReducer';
import type { Action } from '../battle/timeline';

const reward = { source: 'Treasure', options: [] };

afterEach(() => vi.restoreAllMocks());

// Flush the rejected-promise continuation.
const settle = () => new Promise(resolve => setTimeout(resolve, 0));

describe('submitPromptAnswer', () => {
  it('hides the prompt at once and does not restore it when the answer is sent', async () => {
    const dispatch = vi.fn<(a: Action) => void>();

    submitPromptAnswer({
      key: 'rewardChoice',
      prompt: reward,
      hide: { type: 'HIDE_REWARD_CHOICE' },
      method: 'ChooseReward',
      dispatch,
      invoke: () => Promise.resolve(),
    });
    await settle();

    expect(dispatch.mock.calls.map(c => c[0].type)).toEqual(['HIDE_REWARD_CHOICE']);
  });

  it('restores the prompt when the send is rejected (answered while the connection was down)', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const dispatch = vi.fn<(a: Action) => void>();

    submitPromptAnswer({
      key: 'rewardChoice',
      prompt: reward,
      hide: { type: 'HIDE_REWARD_CHOICE' },
      method: 'ChooseReward',
      dispatch,
      invoke: () => Promise.reject(new Error("not in the 'Connected' State")),
    });
    await settle();

    expect(dispatch.mock.calls.map(c => c[0])).toEqual([
      { type: 'HIDE_REWARD_CHOICE' },
      { type: 'RESTORE_PROMPT', key: 'rewardChoice', value: reward },
    ]);
  });

  it('does not restore anything when no prompt was open', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const dispatch = vi.fn<(a: Action) => void>();

    submitPromptAnswer({
      key: 'rewardChoice',
      prompt: null,
      hide: { type: 'HIDE_REWARD_CHOICE' },
      method: 'ChooseReward',
      dispatch,
      invoke: () => Promise.reject(new Error('closed')),
    });
    await settle();

    expect(dispatch.mock.calls.map(c => c[0].type)).toEqual(['HIDE_REWARD_CHOICE']);
  });

  it('only hides (no send, no restore) when there is no live connection', async () => {
    const dispatch = vi.fn<(a: Action) => void>();

    submitPromptAnswer({
      key: 'rewardChoice',
      prompt: reward,
      hide: { type: 'HIDE_REWARD_CHOICE' },
      method: 'ChooseReward',
      dispatch,
      invoke: () => undefined,
    });
    await settle();

    expect(dispatch.mock.calls.map(c => c[0].type)).toEqual(['HIDE_REWARD_CHOICE']);
  });
});

describe('battleReducer — RESTORE_PROMPT', () => {
  it('re-opens a hidden prompt, so a lost answer can be given again', () => {
    const hidden = battleReducer(
      { ...initialState, rewardChoice: reward },
      { type: 'HIDE_REWARD_CHOICE' },
    );
    expect(hidden.rewardChoice).toBeNull();

    const restored = battleReducer(hidden, { type: 'RESTORE_PROMPT', key: 'rewardChoice', value: reward });

    expect(restored.rewardChoice).toBe(reward);
  });

  it('never overwrites a newer prompt of the same kind that arrived in the meantime', () => {
    const newer = { source: 'Boss', options: [] };
    const s = { ...initialState, rewardChoice: newer };

    const next = battleReducer(s, { type: 'RESTORE_PROMPT', key: 'rewardChoice', value: reward });

    expect(next).toBe(s);
    expect(next.rewardChoice).toBe(newer);
  });
});

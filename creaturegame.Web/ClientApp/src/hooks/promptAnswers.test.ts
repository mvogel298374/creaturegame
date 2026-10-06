import { describe, it, expect } from 'vitest';
import { PROMPT_ANSWERS } from './promptAnswers';
import { battleReducer, initialState, type BattleState } from './battleReducer';
import type { Action } from '../battle/timeline';

describe('PROMPT_ANSWERS — each answer pairs its prompt slot, hide action and hub method', () => {
  it.each([
    ['forgetMove', 'moveReplacement', 'HIDE_MOVE_REPLACEMENT', 'ForgetMove'],
    ['respondEvolution', 'evolution', 'HIDE_EVOLUTION_PROMPT', 'RespondEvolution'],
    ['respondRecovery', 'recovery', 'HIDE_RECOVERY', 'RespondRecovery'],
    ['chooseBiome', 'biomeChoice', 'HIDE_BIOME_CHOICE', 'ChooseBiome'],
    ['chooseReward', 'rewardChoice', 'HIDE_REWARD_CHOICE', 'ChooseReward'],
    ['respondMoveTeachTarget', 'moveTeachTarget', 'HIDE_MOVE_TEACH_TARGET', 'RespondMoveTeachTarget'],
    ['leaveShop', 'shop', 'HIDE_SHOP', 'LeaveShop'],
    ['respondAcquisition', 'acquisition', 'HIDE_ACQUISITION', 'RespondAcquisition'],
    ['chooseLead', 'leadChoice', 'HIDE_LEAD_CHOICE', 'ChooseLead'],
    ['respondSwitchIn', 'switchIn', 'HIDE_SWITCH_IN', 'RespondSwitchIn'],
  ] as const)('%s → slot %s, hide %s, hub method %s', (name, key, hideType, method) => {
    expect(PROMPT_ANSWERS[name]).toEqual({ key, hide: { type: hideType }, method });
  });

  it('covers exactly ten answers', () => {
    expect(Object.keys(PROMPT_ANSWERS)).toHaveLength(10);
  });

  it('every hide action clears the very slot its route names', () => {
    const open = (key: keyof BattleState): BattleState => ({ ...initialState, [key]: {} }) as BattleState;
    for (const { key, hide } of Object.values(PROMPT_ANSWERS)) {
      expect(battleReducer(open(key), hide as Action)[key]).toBeNull();
    }
  });
});

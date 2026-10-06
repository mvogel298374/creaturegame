import type { Action } from '../battle/timeline';
import type { PromptKey } from './battleReducer';

export interface PromptRoute<K extends PromptKey = PromptKey> {
  key: K;
  hide: Action;
  method: string;
}

export const PROMPT_ANSWERS = {
  forgetMove: { key: 'moveReplacement', hide: { type: 'HIDE_MOVE_REPLACEMENT' }, method: 'ForgetMove' },
  respondEvolution: { key: 'evolution', hide: { type: 'HIDE_EVOLUTION_PROMPT' }, method: 'RespondEvolution' },
  respondRecovery: { key: 'recovery', hide: { type: 'HIDE_RECOVERY' }, method: 'RespondRecovery' },
  chooseBiome: { key: 'biomeChoice', hide: { type: 'HIDE_BIOME_CHOICE' }, method: 'ChooseBiome' },
  chooseReward: { key: 'rewardChoice', hide: { type: 'HIDE_REWARD_CHOICE' }, method: 'ChooseReward' },
  respondMoveTeachTarget: {
    key: 'moveTeachTarget',
    hide: { type: 'HIDE_MOVE_TEACH_TARGET' },
    method: 'RespondMoveTeachTarget',
  },
  leaveShop: { key: 'shop', hide: { type: 'HIDE_SHOP' }, method: 'LeaveShop' },
  respondAcquisition: { key: 'acquisition', hide: { type: 'HIDE_ACQUISITION' }, method: 'RespondAcquisition' },
  chooseLead: { key: 'leadChoice', hide: { type: 'HIDE_LEAD_CHOICE' }, method: 'ChooseLead' },
  respondSwitchIn: { key: 'switchIn', hide: { type: 'HIDE_SWITCH_IN' }, method: 'RespondSwitchIn' },
} as const satisfies Record<string, PromptRoute>;

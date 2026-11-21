import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useState, type ReactElement } from 'react';
import { dismissHandlers, type ModalDismiss } from './Modal';
import { isEscapePress } from '../../hooks/useEscapeKey';
import { EvolutionPromptModal } from './EvolutionPromptModal';
import { ShopModal } from './ShopModal';
import { MoveReplacementModal, moveReplacementOnB } from './MoveReplacementModal';
import { AcquisitionModal, acquisitionOnB } from './AcquisitionModal';
import { NicknameModal } from './NicknameModal';
import { RewardChoiceModal } from './RewardChoiceModal';
import { LeadChoiceModal } from './LeadChoiceModal';
import { SwitchInModal } from './SwitchInModal';
import { MimicChoiceModal } from './MimicChoiceModal';
import type { AcquisitionPrompt } from '../../hooks/useBattleHub';
import type { PartyMember } from '../../battle/timeline';

vi.mock('react', async orig => ({ ...(await orig<typeof import('react')>()), useState: vi.fn() }));
const stubState = vi.mocked(useState as (init: unknown) => [unknown, (v: unknown) => void]);

const dismissOf = (el: unknown) => (el as ReactElement<{ dismiss: ModalDismiss }>).props.dismiss;
const pressB = (el: unknown) => dismissHandlers(dismissOf(el)).key!();

beforeEach(() => {
  stubState.mockReset();
  stubState.mockImplementation(init => [init, vi.fn()]);
});

describe('Escape plays Gen 1 B on the prompts that have a negative answer', () => {
  it('a held Escape answers once: auto-repeat is not a press', () => {
    expect(isEscapePress({ key: 'Escape', repeat: false })).toBe(true);
    expect(isEscapePress({ key: 'Escape', repeat: true })).toBe(false);
    expect(isEscapePress({ key: 'Enter', repeat: false })).toBe(false);
  });

  it('the B form answers on Escape and never on a backdrop click', () => {
    const sent: string[] = [];
    const { key, backdrop } = dismissHandlers({ onBKey: () => sent.push('b') });
    expect(backdrop).toBeNull();
    key!();
    expect(sent).toEqual(['b']);
  });

  it('a blocking prompt takes neither Escape nor a backdrop click', () => {
    expect(dismissHandlers('blocking')).toEqual({ key: null, backdrop: null });
  });

  it('an escapable overlay still closes on Escape and on a backdrop click', () => {
    const close = () => {};
    expect(dismissHandlers({ onEscape: close })).toEqual({ key: close, backdrop: close });
  });

  it('Escape on the evolution prompt cancels the evolution, once', () => {
    const answers: boolean[] = [];
    const el = EvolutionPromptModal({
      prompt: { fromName: 'CHARMANDER', toName: 'CHARMELEON', fromSpeciesId: 4, toSpeciesId: 5 },
      onRespond: a => answers.push(a),
    });
    const { key } = dismissHandlers(dismissOf(el));
    key!();
    expect(answers).toEqual([false]);
  });

  it('Escape in the shop leaves it', () => {
    let left = 0;
    const el = ShopModal({ prompt: { items: [], balance: 0 }, onBuy: () => {}, onLeave: () => left++ });
    dismissHandlers(dismissOf(el)).key!();
    expect(left).toBe(1);
  });

  it('Escape on the forget list opens "Stop learning?" without answering', () => {
    expect(moveReplacementOnB(null)).toEqual({ slot: null });
  });

  it('Escape on a learn-move confirm is NO, back to the list', () => {
    expect(moveReplacementOnB({ slot: null })).toBeNull();
    expect(moveReplacementOnB({ slot: 2 })).toBeNull();
  });

  it('Escape on the acquisition offer declines; on the swap picker and the release confirm it steps back', () => {
    expect(acquisitionOnB('offer')).toBeNull();
    expect(acquisitionOnB('swap')).toBe('offer');
    expect(acquisitionOnB({ slot: 1 })).toBe('swap');
  });

  describe('each step of the multi-step prompts takes B', () => {
    const lead = { name: 'BULBASAUR', speciesId: 1, level: 5, isLead: true } as PartyMember;
    const bench = { name: 'RATTATA', speciesId: 19, level: 3, isLead: false } as PartyMember;
    const offer: AcquisitionPrompt = {
      source: 'Draft', speciesId: 25, name: 'PIKACHU', level: 5, types: ['Electric'], maxHp: 20,
      partyFull: true, party: [lead, bench],
    };
    const renderAcquisition = (phase: unknown) => {
      const setPhase = vi.fn();
      const answers: unknown[][] = [];
      stubState.mockReturnValueOnce([phase, setPhase]);
      const el = AcquisitionModal({ prompt: offer, onRespond: (...a) => answers.push(a) });
      return { el, setPhase, answers };
    };

    it('the acquisition offer declines', () => {
      const { el, setPhase, answers } = renderAcquisition('offer');
      pressB(el);
      expect(answers).toEqual([[false, null, null]]);
      expect(setPhase).not.toHaveBeenCalled();
    });

    it('the swap picker goes back to the offer', () => {
      const { el, setPhase, answers } = renderAcquisition('swap');
      pressB(el);
      expect(setPhase).toHaveBeenCalledWith('offer');
      expect(answers).toEqual([]);
    });

    it('the release confirm is NO, back to the swap picker', () => {
      const { el, setPhase, answers } = renderAcquisition({ slot: 1 });
      pressB(el);
      expect(setPhase).toHaveBeenCalledWith('swap');
      expect(answers).toEqual([]);
    });

    it('the nickname step means no nickname on B, and a backdrop click does nothing', () => {
      const done: unknown[] = [];
      const el = NicknameModal({ speciesName: 'PIKACHU', onDone: n => done.push(n) });
      expect(dismissHandlers(dismissOf(el)).backdrop).toBeNull();
      pressB(el);
      expect(done).toEqual([null]);
    });

    const renderLearn = (pending: unknown) => {
      const setPending = vi.fn();
      const forgot: unknown[] = [];
      stubState.mockReturnValueOnce([pending, setPending]);
      const el = MoveReplacementModal({
        prompt: { creatureName: 'PIKACHU', newMoveName: 'thunderbolt', currentMoves: ['a', 'b', 'c', 'd'] } as never,
        onForget: s => forgot.push(s),
      });
      return { el, setPending, forgot };
    };

    it('the forget list opens "Stop learning?" and sends nothing', () => {
      const { el, setPending, forgot } = renderLearn(null);
      pressB(el);
      expect(setPending).toHaveBeenCalledWith({ slot: null });
      expect(forgot).toEqual([]);
    });

    it('a learn-move confirm is NO and sends nothing', () => {
      const { el, setPending, forgot } = renderLearn({ slot: 2 });
      pressB(el);
      expect(setPending).toHaveBeenCalledWith(null);
      expect(forgot).toEqual([]);
    });
  });

  it('the required prompts stay blocking', () => {
    const none = () => {};
    const required = [
      RewardChoiceModal({ prompt: { source: 'Trainer', options: [] }, onChoose: none }),
      LeadChoiceModal({ party: [], onChoose: none }),
      SwitchInModal({ prompt: { party: [], faintedName: 'PIKACHU' }, onChoose: none }),
      MimicChoiceModal({ prompt: { creatureName: 'DITTO', moves: [] }, onChoose: none }),
    ];
    required.forEach(el => expect(dismissOf(el)).toBe('blocking'));
  });
});

import { describe, it, expect } from 'vitest';
import { defaultOverviewSlot, showOverviewPicker, overviewSlotUrl } from './overviewPicker';
import type { PartyMember } from './timeline';

function member(overrides: Partial<PartyMember> = {}): PartyMember {
  return {
    speciesId: 1,
    name: 'MON',
    level: 5,
    hp: 10,
    maxHp: 10,
    status: 'None',
    isLead: false,
    ...overrides,
  };
}

describe('defaultOverviewSlot', () => {
  it('opens on the lead, even when the lead is not slot 0', () => {
    const party = [member({ name: 'BENCH' }), member({ name: 'LEAD', isLead: true })];
    expect(defaultOverviewSlot(party)).toBe(1);
  });

  it('falls back to slot 0 when no member is marked lead', () => {
    expect(defaultOverviewSlot([member({ name: 'A' }), member({ name: 'B' })])).toBe(0);
  });

  it('falls back to slot 0 for an empty party', () => {
    expect(defaultOverviewSlot([])).toBe(0);
  });
});

describe('showOverviewPicker', () => {
  it('hides for a party of one', () => {
    expect(showOverviewPicker([member()])).toBe(false);
  });

  it('hides for an empty party', () => {
    expect(showOverviewPicker([])).toBe(false);
  });

  it('shows once there are two or more members', () => {
    expect(showOverviewPicker([member(), member()])).toBe(true);
  });
});

describe('overviewSlotUrl', () => {
  it('targets the slot endpoint once the party view is populated', () => {
    expect(overviewSlotUrl('game-1', [member(), member()], 1)).toBe('/api/game/game-1/player/1');
  });

  // The bug this guards: slot 0 is not always the starter once a party is wired, so an empty local party view
  // (a resume gap) must not be read as "show slot 0" — that could show a benched creature's private sheet.
  it('falls back to the lead-resolving no-slot endpoint when the party view is empty', () => {
    expect(overviewSlotUrl('game-1', [], 0)).toBe('/api/game/game-1/player');
  });
});

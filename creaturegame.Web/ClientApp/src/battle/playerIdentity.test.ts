import { describe, it, expect } from 'vitest';
import { nextPlayerName } from './playerIdentity';

// This ref drives expandEvent's player/enemy side split, so a wrong answer doesn't just mislabel the nameplate —
// it attributes the player's own moves and damage to the enemy side of the screen.
describe('nextPlayerName — which creature is "the player"', () => {
  it('BattleStarted names the encounter lead outright', () => {
    expect(nextPlayerName('BattleStarted', { playerName: 'SQUIRTLE' }, 'CHARMANDER')).toBe('SQUIRTLE');
  });

  it('CreatureSwitchedIn retargets onto whoever took the field', () => {
    expect(nextPlayerName('CreatureSwitchedIn', { name: 'PIKACHU' }, 'CHARMANDER')).toBe('PIKACHU');
  });

  // A lead swap moves nobody onto the field, so no CreatureSwitchedIn follows to do this.
  it('LeadChanged retargets on an out-of-battle lead reassignment', () => {
    expect(nextPlayerName('LeadChanged', { name: 'BLASTOISE' }, 'CHARMANDER')).toBe('BLASTOISE');
  });

  // The defect this helper was extracted for: without it the nameplate and action prompt kept reading
  // "What will CHARMANDER do?" under a CHARMELEON sprite until the next BattleStarted reset it.
  it('CreatureEvolved follows the rename when it is the player that evolved', () => {
    expect(
      nextPlayerName('CreatureEvolved', { fromName: 'CHARMANDER', toName: 'CHARMELEON' }, 'CHARMANDER'),
    ).toBe('CHARMELEON');
  });

  // Evolution is party-wide (BattleRunEvent's EvolutionOrder offers to every member that levelled), so this event
  // arrives for bench members too. An unguarded retarget would hand the player identity to a benched creature.
  it('CreatureEvolved leaves the player alone when a BENCH member evolved', () => {
    expect(
      nextPlayerName('CreatureEvolved', { fromName: 'ODDISH', toName: 'GLOOM' }, 'CHARMANDER'),
    ).toBe('CHARMANDER');
  });

  it('any other event leaves the current player unchanged', () => {
    expect(nextPlayerName('MoveUsed', { attackerName: 'PIDGEY' }, 'CHARMANDER')).toBe('CHARMANDER');
  });
});

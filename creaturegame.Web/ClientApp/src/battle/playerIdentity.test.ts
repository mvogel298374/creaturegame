import { describe, it, expect } from 'vitest';
import { nextPlayerId } from './playerIdentity';

// This ref drives expandEvent's player/enemy side split, so a wrong answer doesn't just mislabel the nameplate —
// it attributes the player's own moves and damage to the enemy side of the screen. Keyed on the creature id, so
// same-named creatures (a wild PIDGEY vs your PIDGEY, a bench twin) can't be confused.
describe('nextPlayerId — which creature is "the player"', () => {
  it('BattleStarted names the encounter lead outright', () => {
    expect(nextPlayerId('BattleStarted', { playerName: 'SQUIRTLE', playerId: 9 }, 4)).toBe(9);
  });

  it('CreatureSwitchedIn retargets onto whoever took the field', () => {
    expect(nextPlayerId('CreatureSwitchedIn', { name: 'PIKACHU', id: 12 }, 4)).toBe(12);
  });

  // A lead swap moves nobody onto the field, so no CreatureSwitchedIn follows to do this.
  it('LeadChanged retargets on an out-of-battle lead reassignment', () => {
    expect(nextPlayerId('LeadChanged', { name: 'BLASTOISE', id: 15 }, 4)).toBe(15);
  });

  // The name-keyed version needed a guard here (an evolution renames the creature in place, and arrives for bench
  // members too, so an unguarded retarget handed the player identity to a bench creature). The id is stable across
  // a rename, so no event about an evolution can change who the player is — and a same-named bench twin evolving
  // is no longer a special case.
  it('CreatureEvolved never changes who the player is — whichever creature evolved', () => {
    const evolved = { fromName: 'PIDGEY', toName: 'PIDGEOTTO', creatureId: 4 };
    expect(nextPlayerId('CreatureEvolved', evolved, 4)).toBe(4); // the on-field creature evolved
    expect(nextPlayerId('CreatureEvolved', { ...evolved, creatureId: 8 }, 4)).toBe(4); // a same-named bench twin did
  });

  // The reconnect replay sends the cached BattleStarted (the ORIGINAL lead) then the latest TurnStarted; without
  // this, a page refresh after a mid-battle switch-in leaves the ref on the outgoing creature, so the switched-in
  // creature's lunge/shake/HP are attributed to the enemy side until the next BattleStarted.
  it('TurnStarted restates the player — the self-correction for a stale replayed BattleStarted', () => {
    let id = 0;
    id = nextPlayerId('BattleStarted', { playerName: 'CHARMANDER', playerId: 4 }, id); // replay: original lead
    expect(id).toBe(4);
    id = nextPlayerId('TurnStarted', { playerName: 'SQUIRTLE', playerId: 9, enemyId: 6 }, id); // who's fighting now
    expect(id).toBe(9);
  });

  it('any other event leaves the current player unchanged', () => {
    expect(nextPlayerId('MoveUsed', { attackerName: 'PIDGEY', attackerId: 2 }, 4)).toBe(4);
  });
});

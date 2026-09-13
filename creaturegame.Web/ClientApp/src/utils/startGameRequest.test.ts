import { describe, expect, it } from 'vitest';
import { buildStartGameRequest } from './startGameRequest';

// docs/TODO.md — Creature Naming, DoR #6: "Vitest coverage for StarterSelection's request body" — the nickname
// threaded into the POST body (present when supplied, omitted on cancel/blank), and the pre-existing seed
// forwarding rule this extraction inherited. Flagged by `pr-review` (2026-09-14) as a cheap fix needing no new
// test infra.
describe('buildStartGameRequest', () => {
  const base = {
    speciesId: 1,
    level: 50,
    difficulty: 'Normal',
    generation: 'One',
  };

  it('includes the nickname when one is supplied', () => {
    const body = buildStartGameRequest({ ...base, nickname: 'Sparky', seedParam: null });
    expect(body.nickname).toBe('Sparky');
  });

  it('omits the nickname key entirely when null (cancelled or left blank)', () => {
    const body = buildStartGameRequest({ ...base, nickname: null, seedParam: null });
    expect(body).not.toHaveProperty('nickname');
  });

  it('forwards a finite integer seed from the URL param', () => {
    const body = buildStartGameRequest({ ...base, nickname: null, seedParam: '42' });
    expect(body.seed).toBe(42);
  });

  it.each([null, '', '   ', 'not-a-number', '1.5'])(
    'omits the seed key for an absent/blank/non-integer param (%j)',
    seedParam => {
      const body = buildStartGameRequest({ ...base, nickname: null, seedParam });
      expect(body).not.toHaveProperty('seed');
    },
  );

  it('always carries speciesId/level/difficulty/generation', () => {
    const body = buildStartGameRequest({ ...base, nickname: null, seedParam: null });
    expect(body).toMatchObject(base);
  });
});

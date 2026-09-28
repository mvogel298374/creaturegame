import { describe, it, expect } from 'vitest';
import timelineSource from './timeline.ts?raw';
import reducerSource from '../hooks/battleReducer.ts?raw';
import identitySource from './playerIdentity.ts?raw';
import hubSource from '../hooks/useBattleHub.ts?raw';

// Creature Identity (ARCHITECTURE.md §2.2): the client routes events to a side / HUD by creature ID. Two creatures can share
// a display name, so comparing names to decide "whose is this?" is the bug class this guard exists to keep out —
// the code compiles, every existing test passes, and it only misbehaves when two names collide.
//
// This is a source scan, deliberately blunt: it can't prove routing is right (the same-name tests in
// timeline.test.ts / battleReducer.test.ts do that), it only stops a name comparison from quietly coming back.
// If a comparison it flags is genuinely display-only, say so by routing it through a helper with a different
// name — don't weaken the pattern.
//
// Known limits (it is regex over source, not a type check): it catches ===/!==/==/!= against a *Name, but
// not .includes(name), switch (name), or a Map keyed by name. Those shapes have no precedent in the routing
// code today; if one appears, extend the pattern here rather than trusting the review to notice.
const stripComments = (src: string): string =>
  src
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:'"`])\/\/.*$/gm, '$1'); // `//` comments, but not the `//` inside a URL/string

// `x.fooName === …`, `… === x.fooName`, `fooName !== …` (and the loose `==` / `!=`) — any equality test against
// something named *Name.
const NAME_COMPARISON = /[\w.]*[Nn]ame\s*[!=]==?|[!=]==?\s*[\w.]*[Nn]ame\b/g;

describe('client routing never compares display names', () => {
  const sources: Array<[string, string]> = [
    ['battle/timeline.ts', timelineSource],
    ['hooks/battleReducer.ts', reducerSource],
    ['battle/playerIdentity.ts', identitySource],
    ['hooks/useBattleHub.ts', hubSource], // holds playerIdRef — where a name compare would sneak back in
  ];

  it.each(sources)('%s has no name equality comparison', (file, source) => {
    const hits = stripComments(source).match(NAME_COMPARISON) ?? [];
    expect(hits, `${file} compares a display name — route on the creature id instead`).toEqual([]);
  });

  it('the pattern actually catches the comparisons it exists for (guard is not vacuous)', () => {
    // The exact shapes the old routing used — if the regex stops matching these, the guard above proves nothing.
    for (const bad of [
      "name === ctx.playerName ? 'player' : 'enemy'",
      'if (action.name === state.playerName) return state;',
      'state.party.find(m => m.name === action.name)',
      'lead.name !== state.playerName',
      'action.fromName !== state.playerName',
      'if (m.name == state.playerName)', // loose equality
      'if (a.enemyName != b)',
    ]) {
      expect(bad.match(NAME_COMPARISON), bad).not.toBeNull();
    }
  });

  it('the comment stripper leaves real code alone and drops commentary', () => {
    expect(stripComments("const a = 1; // name === x\nconst b = 'http://x';")).toContain("'http://x'");
    expect(stripComments('/* name === x */ const c = 2;')).not.toContain('name ===');
  });
});

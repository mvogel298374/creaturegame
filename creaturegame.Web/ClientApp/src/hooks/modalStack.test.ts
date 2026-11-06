import { describe, it, expect } from 'vitest';
import { isTopmost } from './modalStack';

describe('isTopmost — only the last overlay in DOM order owns the keyboard', () => {
  const map = { name: 'run map' };
  const prompt = { name: 'prompt' };
  const settings = { name: 'settings' };

  it('a lone pinned map is topmost', () => {
    expect(isTopmost([map], map)).toBe(true);
  });

  it('the map under a prompt is not topmost; the prompt is', () => {
    expect(isTopmost([map, prompt], map)).toBe(false);
    expect(isTopmost([map, prompt], prompt)).toBe(true);
  });

  it('Settings over a dismissable prompt: only Settings is topmost', () => {
    expect(isTopmost([prompt, settings], settings)).toBe(true);
    expect(isTopmost([prompt, settings], prompt)).toBe(false);
  });

  it('an owner that is unmounted or absent from the stack is never topmost', () => {
    expect(isTopmost([map], null)).toBe(false);
    expect(isTopmost([map], undefined)).toBe(false);
    expect(isTopmost([], map)).toBe(false);
    expect(isTopmost([map], prompt)).toBe(false);
  });
});

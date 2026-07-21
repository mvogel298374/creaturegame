import { describe, it, expect } from 'vitest';
import { setMasterVolume, getMasterVolume } from './AudioEngine';

// Only the pure volume bookkeeping is exercised here — the synth functions need a real AudioContext
// (unavailable under Vitest's node environment) and are verified live in the browser instead.
describe('AudioEngine master volume', () => {
  it('clamps above 1 down to 1', () => {
    setMasterVolume(2);
    expect(getMasterVolume()).toBe(1);
  });

  it('clamps below 0 up to 0', () => {
    setMasterVolume(-1);
    expect(getMasterVolume()).toBe(0);
  });

  it('passes through an in-range value unchanged', () => {
    setMasterVolume(0.4);
    expect(getMasterVolume()).toBe(0.4);
  });
});

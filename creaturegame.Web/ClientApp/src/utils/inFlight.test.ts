import { describe, it, expect } from 'vitest';
import { createInFlightGuard } from './inFlight';

describe('createInFlightGuard', () => {
  it('drops a second call while the first is still running', async () => {
    const guard = createInFlightGuard();
    let release!: () => void;
    let runs = 0;
    const task = () => { runs++; return new Promise<void>(r => { release = r; }); };

    const first = guard(task);
    const second = guard(task);
    release();

    expect(await first).toBe(true);
    expect(await second).toBe(false);
    expect(runs).toBe(1);
  });

  it('accepts a new call once the previous one has finished', async () => {
    const guard = createInFlightGuard();
    let runs = 0;
    const task = async () => { runs++; };

    await guard(task);
    expect(await guard(task)).toBe(true);
    expect(runs).toBe(2);
  });

  it('accepts a new call after the previous one rejected', async () => {
    const guard = createInFlightGuard();

    await expect(guard(async () => { throw new Error('boom'); })).rejects.toThrow('boom');
    expect(await guard(async () => {})).toBe(true);
  });
});

import { describe, it, expect } from 'vitest';
import { friendlyFetchError } from './fetchError';

// The backend-unreachable path is invisible to the Playwright E2E suite (it always runs with the backend
// up), so these branches are only covered here.
describe('friendlyFetchError', () => {
  it('turns a fetch TypeError (server unreachable) into a "start the backend" hint', () => {
    const msg = friendlyFetchError(new TypeError('NetworkError when attempting to fetch resource.'));
    expect(msg).toContain('backend is running');
  });

  it('turns an HTTP status Error into a "server returned an error" message with the status', () => {
    const msg = friendlyFetchError(new Error('HTTP 500'));
    expect(msg).toContain('HTTP 500');
    expect(msg).toContain('server returned an error');
  });

  it('passes a plain Error through by its message', () => {
    expect(friendlyFetchError(new Error('boom'))).toBe('boom');
  });

  it('stringifies a non-Error value', () => {
    expect(friendlyFetchError('weird')).toBe('weird');
    expect(friendlyFetchError(42)).toBe('42');
  });
});

import { useEffect, type RefObject } from 'react';
import { isTopmost, overlayStack } from './modalStack';

// A held key's auto-repeat is not a new press, so a held Escape answers once.
export function isEscapePress(e: Pick<KeyboardEvent, 'key' | 'repeat'>): boolean {
  return e.key === 'Escape' && !e.repeat;
}

// Null opts out (a blocking prompt); otherwise fires only while `owner` is the active modal.
export function useEscapeKey(onEscape: (() => void) | null, owner: RefObject<Element>) {
  useEffect(() => {
    if (!onEscape) return;
    const onKey = (e: KeyboardEvent) => {
      if (isEscapePress(e) && isTopmost(overlayStack(), owner.current)) onEscape();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onEscape, owner]);
}

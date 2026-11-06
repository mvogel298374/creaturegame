import { useEffect, type RefObject } from 'react';
import { isTopmost, overlayStack } from './modalStack';

// Null opts out (a blocking prompt); otherwise fires only while `owner` is the active modal.
export function useEscapeKey(onEscape: (() => void) | null, owner: RefObject<Element>) {
  useEffect(() => {
    if (!onEscape) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isTopmost(overlayStack(), owner.current)) onEscape();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onEscape, owner]);
}

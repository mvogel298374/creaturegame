import { useEffect } from 'react';

// Escape-to-close, in one place. Pass null to opt out — a modal that must be answered rather than dismissed
// (see Modal's `dismiss` prop) passes null, so "this overlay ignores Escape" is a decision the caller states
// rather than an accident of which component happened to remember a keydown listener.
export function useEscapeKey(onEscape: (() => void) | null) {
  useEffect(() => {
    if (!onEscape) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onEscape(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onEscape]);
}

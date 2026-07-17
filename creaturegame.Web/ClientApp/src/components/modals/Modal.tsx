import type { ReactNode, RefObject } from 'react';
import { useEscapeKey } from '../../hooks/useEscapeKey';
import '../../pages/BattleScreen.css';

// Whether this overlay can be dismissed without answering it.
//
// 'blocking' is the right answer for every run prompt, and the reason is structural rather than stylistic:
// each one parks a server-side await (the run loop sits on a TCS until the player answers), so there is no
// "close" that a prompt could perform — dismissing it would strand the run with nothing to send back. The
// negative choices these modals offer (DECLINE, CANCEL, SKIP, Leave) are *answers*, not dismissals.
//
// { onEscape } is for an overlay that only draws state the client already has, so leaving it costs nothing.
export type ModalDismiss = 'blocking' | { onEscape: () => void };

// The shared overlay every prompt renders through: a dimmed full-field backdrop (.modal-overlay) wrapping a
// card. `dismiss` makes the escapable/blocking decision an explicit, reviewable prop — previously each modal
// hand-rolled its own <div className="modal-overlay"> and Escape existed only where someone had added it.
//
// `overlay` appends a backdrop modifier (e.g. 'modal-overlay--corner'); `card` is the card's own class.
// `cardRef` exposes the card for a modal that needs to reach into its own content (RouteChoiceMap focuses the
// first offered waypoint on open), so the query stays scoped to the modal rather than the whole document.
export function Modal({ label, dismiss, card, overlay, role = 'alertdialog', cardRef, children }: {
  label: string;
  dismiss: ModalDismiss;
  card: string;
  overlay?: string;
  role?: 'alertdialog' | 'dialog';
  cardRef?: RefObject<HTMLDivElement>;
  children: ReactNode;
}) {
  const onEscape = typeof dismiss === 'object' ? dismiss.onEscape : null;
  useEscapeKey(onEscape);
  return (
    <div
      className={overlay ? `modal-overlay ${overlay}` : 'modal-overlay'}
      onClick={onEscape ? e => { if (e.target === e.currentTarget) onEscape(); } : undefined}
    >
      <div ref={cardRef} className={card} role={role} aria-modal="true" aria-label={label}>
        {children}
      </div>
    </div>
  );
}

import { useRef, type ReactNode, type RefObject } from 'react';
import { useEscapeKey } from '../../hooks/useEscapeKey';
import { useModalFocus } from '../../hooks/useModalFocus';
import '../buttons.css';
import './Modal.css';
import './ModalFrame.css';

// How an overlay takes Escape and a backdrop click: FRONTEND_PLAN.md → Modal focus ("Dismiss").
export type ModalDismiss = 'blocking' | { onEscape: () => void } | { onBKey: () => void };

export function dismissHandlers(dismiss: ModalDismiss): { key: (() => void) | null; backdrop: (() => void) | null } {
  if (dismiss === 'blocking') return { key: null, backdrop: null };
  if ('onBKey' in dismiss) return { key: dismiss.onBKey, backdrop: null };
  return { key: dismiss.onEscape, backdrop: dismiss.onEscape };
}

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
  const { key, backdrop } = dismissHandlers(dismiss);
  const overlayRef = useRef<HTMLDivElement>(null);
  useEscapeKey(key, overlayRef);
  useModalFocus(label);
  return (
    <div
      ref={overlayRef}
      className={overlay ? `modal-overlay ${overlay}` : 'modal-overlay'}
      onClick={backdrop ? e => { if (e.target === e.currentTarget) backdrop(); } : undefined}
    >
      <div ref={cardRef} className={card} role={role} aria-modal="true" aria-label={label} tabIndex={-1}>
        {children}
      </div>
    </div>
  );
}

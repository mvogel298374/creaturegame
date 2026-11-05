import { useEffect, useRef } from 'react';

// Design: FRONTEND_PLAN.md → Modal focus.
const inerted = new Set<Element>();

const topOverlay = () => Array.from(document.querySelectorAll('.modal-overlay')).at(-1);

const tabbable = (root: Element) =>
  Array.from(root.querySelectorAll<HTMLElement>('a[href], button, input, select, textarea, [tabindex]')).filter(
    el => el.tabIndex >= 0 && !el.matches(':disabled') && el.getClientRects().length > 0
  );

function onTab(e: KeyboardEvent) {
  if (e.key !== 'Tab') return;
  const top = topOverlay();
  if (!top) return;
  const controls = tabbable(top);
  const first = controls[0];
  const last = controls.at(-1);
  const active = document.activeElement as HTMLElement | null;
  const wraps = e.shiftKey ? active === first : active === last;
  if (active && controls.includes(active) && !wraps) return;
  e.preventDefault();
  (e.shiftKey ? last : first)?.focus();
}

function syncModals() {
  inerted.forEach(el => el.removeAttribute('inert'));
  inerted.clear();
  const top = topOverlay();
  if (!top) {
    document.removeEventListener('keydown', onTab);
    return;
  }
  document.addEventListener('keydown', onTab);
  for (let node = top; node !== document.body && node.parentElement; node = node.parentElement) {
    for (const sibling of Array.from(node.parentElement.children)) {
      if (sibling === node) continue;
      sibling.setAttribute('inert', '');
      inerted.add(sibling);
    }
  }
  if (!top.contains(document.activeElement)) {
    (tabbable(top)[0] ?? (top.firstElementChild as HTMLElement | null))?.focus();
  }
}

export function useModalFocus(label: string) {
  const opener = useRef(document.activeElement);
  useEffect(syncModals, [label]);
  useEffect(
    () => () => {
      syncModals();
      if (opener.current instanceof HTMLElement && opener.current.isConnected) opener.current.focus();
    },
    []
  );
}

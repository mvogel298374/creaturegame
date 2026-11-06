// Design: FRONTEND_PLAN.md → Modal focus.
export const overlayStack = () => Array.from(document.querySelectorAll('.modal-overlay, [data-modal-root]'));

export const isTopmost = <T>(stack: readonly T[], owner: T | null | undefined) =>
  owner != null && stack.at(-1) === owner;

// True only under an E2E harness (Playwright), enabled by loading the app with a
// `?e2e=1` query param (or setting window.__CG_E2E__ before load). Read once at module
// load — the initial document URL carries the param, so it's set before any app code
// runs. Defaults to false, so production behaviour is untouched.
//
// Enables two test seams: the bridge is exposed + recorded on window (see PhaserBridge),
// and animation/step delays are shortened (see timeline) so specs run fast and assert
// event ordering rather than wall-clock durations.
function detectE2E(): boolean {
  if (typeof window === 'undefined') return false;
  if ((window as unknown as { __CG_E2E__?: boolean }).__CG_E2E__ === true) return true;
  return new URLSearchParams(window.location.search).has('e2e');
}

export const E2E: boolean = detectE2E();

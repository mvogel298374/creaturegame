// Drops a call that arrives while the previous one is still running; resolves false when dropped.
export function createInFlightGuard() {
  let busy = false;
  return async (task: () => Promise<void>): Promise<boolean> => {
    if (busy) return false;
    busy = true;
    try {
      await task();
      return true;
    } finally {
      busy = false;
    }
  };
}

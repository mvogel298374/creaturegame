// fetch() rejects with a TypeError (e.g. "NetworkError when attempting to fetch resource") when the server is
// unreachable — backend not running, port closed, or the connection dropped. Turn that (and HTTP error
// statuses, thrown as `Error("HTTP <status>")`) into a message that points at the real problem instead of
// dumping the raw exception.
export function friendlyFetchError(e: unknown): string {
  if (e instanceof TypeError)
    return "Couldn't reach the game server. Make sure the backend is running, then reload.";
  if (e instanceof Error && e.message.startsWith('HTTP '))
    return `The game server returned an error (${e.message}). Please try again.`;
  return e instanceof Error ? e.message : String(e);
}

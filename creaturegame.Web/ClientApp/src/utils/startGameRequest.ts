// The POST /api/game/start body: pure so both the seed-parsing quirk and the nickname's presence/absence can
// be pinned by Vitest without touching window.location or fetch (StarterSelection.tsx is otherwise untestable
// without a jsdom/RTL harness this repo doesn't have — docs/TODO.md, Creature Naming, DoR #6).
export interface StartGameRequestBody {
  speciesId: number;
  level: number;
  difficulty: string;
  generation: string;
  seed?: number;
  nickname?: string;
  forceDraft?: boolean;
}

export function buildStartGameRequest(params: {
  speciesId: number;
  level: number;
  difficulty: string;
  generation: string;
  nickname: string | null;
  seedParam: string | null;
  // The optional ?forceDraft=1 URL flag (E2E only): asks the server to skip the themed-draft cadence so a run
  // reaches a party of two without a seed walk. A request, not a grant — the server honours it only under Dev
  // Mode (ARCHITECTURE.md §2.7), so it is inert against a production backend.
  forceDraftParam?: string | null;
}): StartGameRequestBody {
  const body: StartGameRequestBody = {
    speciesId: params.speciesId,
    level: params.level,
    difficulty: params.difficulty,
    generation: params.generation,
  };

  // An optional ?seed=<int> in the URL forces the run's seed (deterministic replay / E2E); the backend
  // otherwise picks a random one. Only a finite integer is forwarded — anything else falls through to the
  // server's random seed.
  const seed = params.seedParam !== null && params.seedParam.trim() !== ''
    ? Number(params.seedParam)
    : NaN;
  if (Number.isInteger(seed)) body.seed = seed;

  // A cancelled/blank nickname omits the key entirely — the server's own NicknameRules.Normalize falls back
  // to the species-default name either way, so there is nothing to send.
  if (params.nickname) body.nickname = params.nickname;

  // Only the exact value "1" opts in; absent (the normal case) omits the key entirely.
  if (params.forceDraftParam === '1') body.forceDraft = true;

  return body;
}

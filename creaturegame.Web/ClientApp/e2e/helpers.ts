import { expect, type Page, type Locator } from '@playwright/test';

/**
 * Page-object helpers for the battle flow. Centralises selectors and the multi-step
 * navigation so specs read as intent ("start a battle", "choose a move") rather than
 * a pile of clicks. Selectors lean on stable semantic classes already in the app
 * (.btn-new-game, .species-card, .move-btn, .log-line, .bar-fill, .nameplate--*).
 */

export type BridgeEvent = { name: string; t: number };

/**
 * Title → starter select → confirm → battle, returning once it's the player's turn.
 *
 * Pass a `seed` to pin a **fully deterministic** run: the backend threads one seed through every
 * nondeterministic step (enemy species/DVs/moves, the biome offer, every battle roll, the AI's choices), so a
 * seeded run replays identically. This is the lever a spec uses to stop depending on coin-flip battle outcomes.
 * react-router drops the query string on nav from the title, so a seeded run lands **directly on /select** (the
 * `reward-drop.spec` pattern) — the level slider lives on that screen, so a custom `level` still works there.
 */
export async function startBattle(
  page: Page,
  species = 'CHARIZARD',
  level?: number,
  seed?: number
): Promise<void> {
  // ?e2e=1 puts the app in test mode (bridge recording + collapsed animation delays).
  if (seed !== undefined) {
    await page.goto(`/select?e2e=1&seed=${seed}`);
    await page.locator('.species-card').first().waitFor({ state: 'visible', timeout: 10_000 });
  } else {
    await page.goto('/?e2e=1');
    await page.locator('.btn-new-game').click();
  }

  // The level slider is on the select screen, which both entry paths reach.
  if (level !== undefined) await setStartLevel(page, level);

  // Match the card by its EXACT name (the .card-name element) so a prefix like MEW doesn't also grab
  // MEWTWO (a strict-mode violation). The search box narrows the grid first.
  await page.locator('.select-search').fill(species);
  const card = page.locator('.species-card', {
    has: page.locator('.card-name', { hasText: new RegExp(`^${species}$`, 'i') }),
  });
  await expect(card).toBeVisible();
  await card.click();
  await page.getByRole('button', { name: /CONFIRM/i }).click();

  // Biome mode: the run opens on the map-based route choice — click the first offered biome waypoint — before
  // the first battle. It arrives a beat after CONFIRM (connect + emit), so wait for it. Then the entry
  // animation plays and the action menu enables for the first turn — unless the first node is a reward node
  // (Treasure/Mystery), whose choice modal blocks first, so clear that before waiting on the fight menu.
  await page.locator('.region-node--offered').first().click({ timeout: 15_000 });
  await expect(async () => {
    await leaveShopIfPresent(page);
    await dismissRewardChoiceIfPresent(page);
    expect(await fightButton(page).isEnabled().catch(() => false)).toBe(true);
  }).toPass({ timeout: 20_000 });
}

/** Answers a route-choice map if one is up (clicks the first offered biome waypoint). Returns whether it acted.
 * The run opens on one, and one follows each Poké Center, so the play loop calls this too. */
export async function chooseBiomeIfPresent(page: Page): Promise<boolean> {
  const firstOffered = page.locator('.region-node--offered').first();
  if (await firstOffered.isVisible().catch(() => false)) {
    await firstOffered.click();
    return true;
  }
  return false;
}

/** Leaves a Shop node's buy modal if one is up (a shop is a blocking between-node modal, like the reward/biome
 * choice). A shop-first node opens with an empty wallet, so there's nothing to buy — the play loop and
 * startBattle just leave to keep the run flowing (shop *purchasing* is covered by shop.spec + unit tests).
 * Returns whether it acted. */
export async function leaveShopIfPresent(page: Page): Promise<boolean> {
  const leave = page.locator('.shop-leave-btn');
  if (await leave.isVisible().catch(() => false)) {
    await leave.click();
    return true;
  }
  return false;
}

/** Answers a reward-choice modal if one is up by taking the gold bag (always offered, so a deterministic
 * pick). Every rolled reward — a battle win, a Treasure/Mystery node — now blocks on this pick-one-of-N until
 * answered, so the play loop and startBattle both clear it to keep the run flowing. Returns whether it acted. */
export async function dismissRewardChoiceIfPresent(page: Page): Promise<boolean> {
  const goldCard = page.locator('.reward-card--gold').first();
  if (await goldCard.isVisible().catch(() => false)) {
    await goldCard.click();
    return true;
  }
  return false;
}

/** Set the starter level slider (defaults to 50 if left untouched). Driven by keyboard so the React
 * controlled input actually updates — Home jumps to the min (5), then ArrowRight steps up. */
async function setStartLevel(page: Page, level: number): Promise<void> {
  const slider = page.locator('input[type="range"]');
  await slider.focus();
  await slider.press('Home'); // → min (5)
  for (let i = 5; i < level; i++) await slider.press('ArrowRight');
}

export const fightButton = (page: Page): Locator =>
  page.getByRole('button', { name: /^FIGHT/i });

/** Opens the FIGHT grid and picks a move (by name, or the first available). */
export async function chooseMove(page: Page, moveName?: string): Promise<string> {
  await fightButton(page).click();
  const move = moveName
    ? page.locator('.move-btn', { hasText: moveName })
    : page.locator('.move-btn:not([disabled])').first();
  await expect(move).toBeEnabled();
  const label = (await move.locator('.move-name').textContent())?.trim() ?? '';
  await move.click();
  return label;
}

/**
 * Opens the FIGHT grid and picks the move most likely to actually win — highest `base power × type
 * effectiveness × STAB`, read straight off the cues the menu already renders (`.move-pow`, `.move-eff`,
 * `.move-stab`).
 *
 * `chooseMove`'s first-available pick is fine for a spec that just needs *a* turn to resolve, but it is a
 * losing strategy for any spec that has to survive several battles: the first slot is usually the oldest and
 * weakest move, and often a status move that deals no damage at all. That's not a theoretical concern — the
 * deep-run specs were failing every seed on it (a level-40 CHARIZARD dutifully SCRATCHing a GYARADOS that was
 * answering with SURF). Raising the starting level does **not** compensate, because wild encounters are
 * self-referential: `ScaleTargetBst` is `playerBst + depth×10` and the level window keys off the player's own
 * level, so the foe re-scales to whatever the player is. Playing better is the only lever there is.
 */
export async function chooseBestMove(page: Page): Promise<string> {
  await fightButton(page).click();
  const grid = page.locator('.move-grid');
  await expect(grid.locator('.move-btn:not([disabled])').first()).toBeEnabled();

  const scored = await grid.locator('.move-btn').evaluateAll(nodes =>
    nodes.map((node, i) => {
      const el = node as HTMLButtonElement;
      const numberIn = (sel: string) => {
        const raw = el.querySelector(sel)?.textContent?.replace('×', '').trim();
        const n = raw ? Number.parseFloat(raw) : NaN;
        return Number.isFinite(n) ? n : null;
      };
      return {
        i,
        disabled: el.disabled,
        // No .move-pow means a status move (no base power) — score 0, so it's only ever the fallback.
        power: numberIn('.move-pow') ?? 0,
        // No .move-eff means neutral: the engine sends 1.0 for both 1× and non-damaging.
        eff: numberIn('.move-eff') ?? 1,
        stab: el.querySelector('.move-stab') !== null,
      };
    })
  );

  const usable = scored.filter(m => !m.disabled);
  const best = usable.reduce(
    (a, b) => (b.power * b.eff * (b.stab ? 1.5 : 1) > a.power * a.eff * (a.stab ? 1.5 : 1) ? b : a),
    usable[0]
  );

  const chosen = grid.locator('.move-btn').nth(best.i);
  const label = (await chosen.locator('.move-name').textContent())?.trim() ?? '';
  await chosen.click();
  return label;
}

export const logLines = (page: Page): Promise<string[]> =>
  page.locator('.log-line').allTextContents().then(xs => xs.map(s => s.trim()));

export const lastLog = async (page: Page): Promise<string> =>
  (await logLines(page)).at(-1) ?? '';

/** HP-bar fill width (e.g. "84.375%") for the named side. Scoped to the HP row because the player
 * nameplate also has an XP bar that shares the .bar-fill class. */
export const hpWidth = (page: Page, side: 'player' | 'enemy'): Promise<string> =>
  page.locator(`.nameplate--${side} .hp-row .bar-fill`).evaluate(el => (el as HTMLElement).style.width);

/** Player XP-bar fill width. */
export const xpWidth = (page: Page): Promise<string> =>
  page.locator('.nameplate--player .bar-fill--xp').evaluate(el => (el as HTMLElement).style.width);

/** Recorded Phaser bridge events (name + ms timestamp), in emit order. */
export const bridgeEvents = (page: Page): Promise<BridgeEvent[]> =>
  page.evaluate(() => (window as unknown as { __cgEvents?: BridgeEvent[] }).__cgEvents ?? []);

/** Waits until a log line matching the pattern appears. */
export async function waitForLog(page: Page, re: RegExp, timeout = 15_000): Promise<void> {
  await expect(page.locator('.log-line').filter({ hasText: re }).first()).toBeVisible({ timeout });
}

type BridgeEventWindow = { __cgEvents?: BridgeEvent[] };

/**
 * Attack each turn (first available move) until `done(log)` holds or maxTurns elapses; returns the log.
 * The endless chain has no terminal "wins!" — a battle win is an intermission — so callers stop on the
 * chain's own lines ("A new challenger approaches!" / "Run over").
 */
async function attackUntil(
  page: Page,
  done: (log: string[]) => boolean,
  maxTurns: number
): Promise<string[]> {
  for (let i = 0; i < maxTurns; i++) {
    if (done(await logLines(page))) break;
    // Between-node blocking modals must be cleared to keep the chain flowing: leave a Shop (nothing to buy in
    // an auto-played run) and take the gold bag at a reward node (battle win / Treasure / Mystery).
    await leaveShopIfPresent(page);
    await dismissRewardChoiceIfPresent(page);
    if (await fightButton(page).isEnabled().catch(() => false)) {
      // A turn/battle can end mid-choice (the move we picked is lethal); the click on the
      // now-disabled button then fails — swallow it and let the next iteration re-check.
      await chooseMove(page).catch(() => {});
    }
    await page.waitForTimeout(150);
  }
  return logLines(page);
}

/** Attack until a log line matches `re` (or maxTurns elapses). */
export const attackUntilLog = (page: Page, re: RegExp, maxTurns = 80): Promise<string[]> =>
  attackUntil(page, log => log.some(l => re.test(l)), maxTurns);

/** Play one battle to a win — the chain's "A new challenger approaches!" intermission — or to the run's
 * end if the player faints first. */
export const playToNextEncounter = (page: Page, maxTurns = 80): Promise<string[]> =>
  attackUntil(
    page,
    log => log.some(l => /A new challenger approaches!/.test(l) || /Run over/.test(l)),
    maxTurns
  );

/** Play battle after battle until the player faints and the run ends. Caller should raise the test
 * timeout — a natural run-to-loss can span several battles. */
export const playToRunEnd = (page: Page, maxTurns = 300): Promise<string[]> =>
  attackUntilLog(page, /Run over/, maxTurns);

/**
 * Attack each turn until the "grew to level N!" line appears, then stop — for the level-up specs, run on a
 * fixed seed via `startBattle(…, seed)` so a low-level start reliably wins into a level-up. Unlike the generic
 * play loop this does NOT dismiss the post-win reward-choice modal: the level-up specs assert the level-up
 * panel + reward-modal interaction themselves, so we leave the modal standing where they expect it.
 */
export async function playToLevelUp(page: Page, maxTurns = 60): Promise<string[]> {
  const grew = (log: string[]) => log.some(l => /grew to level \d+!/.test(l));
  for (let i = 0; i < maxTurns; i++) {
    if (grew(await logLines(page))) break;
    // Leave a Shop node if one blocks before the level-up win (nothing to buy in an auto-played run). The
    // post-win reward modal is deliberately NOT dismissed here — the level-up specs assert it themselves.
    await leaveShopIfPresent(page);
    if (await fightButton(page).isEnabled().catch(() => false)) {
      // A lethal hit ends the turn mid-choice and disables the button; swallow and let the loop re-check.
      await chooseMove(page).catch(() => {});
    }
    await page.waitForTimeout(150);
  }
  return logLines(page);
}

/**
 * Start fresh runs until one reaches a log line matching `re`, returning that run's log. Wild enemies are
 * BST- and level-matched, so a single battle is roughly a coin-flip — a win-dependent target (an
 * intermission, a level-up) can be lost on the way. We just reload and try another run (`startBattle`'s
 * `page.goto` resets the SPA + the recorded bridge events, so the returned run is clean). Each run plays
 * until `re` appears OR the run ends ("Run over"); a run that ends without the target triggers a restart.
 * Throws after `attempts` runs so a genuine regression fails loudly instead of hanging.
 */
export async function reachLog(
  page: Page,
  re: RegExp,
  opts: { species?: string; level?: number; attempts?: number } = {}
): Promise<string[]> {
  const { species = 'CHARIZARD', level = 5, attempts = 8 } = opts;
  for (let i = 0; i < attempts; i++) {
    await startBattle(page, species, level);
    const log = await attackUntil(
      page,
      l => l.some(x => re.test(x)) || l.some(x => /Run over/.test(x)),
      200
    );
    if (log.some(x => re.test(x))) return log;
    // Run ended without the target (the player fainted first) — reload and try a fresh run.
  }
  throw new Error(`reachLog: never reached /${re.source}/ in ${attempts} runs`);
}

/**
 * How the play loop answers a themed-draft offer:
 * - `accept` — press ADD. The only way the party grows past one, so anything party-dependent needs it.
 * - `decline` — press DECLINE. For a spec that must stay a party of one: the run still flows (the offer parks
 *   a server-side await, so leaving it standing would stall the loop), but no second creature is ever added —
 *   which matters because *every* creature that levels is eligible for the level-up prompts, so a drafted
 *   creature can raise the very modal the spec is waiting for and fail its identity assertion.
 * - `leave` — don't touch it. For a spec whose target IS this modal.
 */
export type DraftPolicy = 'accept' | 'decline' | 'leave';

/** Options shared by the run-playing loop and the seed walker. */
export type PlayOpts = {
  maxTurns?: number;
  drafts?: DraftPolicy;
};

/**
 * Plays the run **already in progress** — answering the draft per `drafts`, keeping the current lead at a biome
 * boundary, and clearing every between-node modal — until `reached(page)` holds, the run ends, or `maxTurns`
 * elapses. Returns whether the state was reached.
 *
 * Split out from `walkSeedsUntil` so a spec can carry on with the *same* run after answering a prompt, instead
 * of paying for a second seed walk. The evolution spec needs exactly that: Gen 1's B-cancel re-offers at the
 * next level-up, so CANCEL and ALLOW can share one expensive reach.
 */
export async function playCurrentRunUntil(
  page: Page,
  reached: (page: Page) => Promise<boolean>,
  opts: PlayOpts = {}
): Promise<boolean> {
  const { maxTurns = 400, drafts = 'accept' } = opts;

  for (let i = 0; i < maxTurns; i++) {
    if (await reached(page)) return true;

    // Answer the draft per policy.
    //
    // A spec whose target IS this modal must pass `leave`, and not merely rely on the `reached` probe above
    // winning the race: both probes run in the same iteration a few ms apart, so a modal that renders between
    // them gets answered by the loop before the spec ever sees it. That is not hypothetical — it is exactly how
    // the acquisition spec failed, with "VAPOREON joined the party!" in the log and no modal left to assert on.
    const acquire = page.locator('.acquire-modal');
    if (drafts !== 'leave' && (await acquire.isVisible().catch(() => false))) {
      const answer =
        drafts === 'accept'
          ? acquire.locator('.action-btn--fight')
          : acquire.getByRole('button', { name: 'DECLINE', exact: true });
      await answer.click().catch(() => {});
      continue;
    }
    // Keep the current lead at a biome boundary so the run keeps flowing.
    const keepLead = page.locator('.lead-modal[aria-label="Choose your lead"] .lead-card--current');
    if (await keepLead.isVisible().catch(() => false)) {
      await keepLead.click().catch(() => {});
      continue;
    }
    await leaveShopIfPresent(page);
    await dismissRewardChoiceIfPresent(page);
    await chooseBiomeIfPresent(page);

    if ((await logLines(page)).some(l => /Run over/.test(l))) return false;
    if (await fightButton(page).isEnabled().catch(() => false)) {
      // Play to win, not just to take a turn — see `chooseBestMove`. These reaches are several battles deep,
      // so a weak-move auto-player simply loses every run before the state under test exists.
      await chooseBestMove(page).catch(() => {});
    }
    await page.waitForTimeout(80);
  }
  return reached(page);
}

/**
 * Plays seeded runs until one reaches the state `reached(page)` describes; returns the seed that got there,
 * or `null` if every run ended first.
 *
 * The shared driver behind every party-dependent spec. A party larger than one is several battles deep and
 * gated on the themed-draft cadence × roll, so the target state can be lost on the way — and **a seed is not
 * by itself determinism**: the seed fixes the *server's* RNG stream, but the client's move sequence is what
 * draws from it, so under load the polling loop's clicks land on different turns and play out a different run
 * (a lone pinned seed passed standalone and then lost its run at battle 2 inside the full suite). Walking a
 * list of seeds is the `reachLog` retry idiom made cheap and repeatable rather than a fresh coin-flip.
 *
 * `level` defaults to 30 rather than the starter minimum on purpose: a level-5 lone starter frequently wipes
 * before the draft cadence comes round (an Elite's Vaporeon ended every one of eight seeded runs on a
 * standalone pass), which burns the whole seed list on runs that never reach the state under test. A higher
 * lead survives the early nodes, so what the seeds actually vary is the draft cadence — the thing we're
 * waiting on — not whether the run lives. Specs that need the lead to *faint* pass `level: 5` explicitly.
 */
export async function walkSeedsUntil(
  page: Page,
  reached: (page: Page) => Promise<boolean>,
  opts: PlayOpts & {
    seeds?: number[];
    species?: string;
    level?: number;
  } = {}
): Promise<number | null> {
  const {
    seeds = [1, 2, 3, 4, 5, 6, 7, 8],
    species = 'CHARIZARD',
    level = 30,
    ...playOpts
  } = opts;

  for (const seed of seeds) {
    await startBattle(page, species, level, seed);
    if (await playCurrentRunUntil(page, reached, playOpts)) return seed;
    // That run wiped (or never got there) — try the next seeded run.
  }
  return null;
}

/** True when the locator is on the page right now. The `reached` predicates are instantaneous probes, not
 * waits — the seed-walk loop is what does the waiting. */
export const isShowing = (locator: Locator): Promise<boolean> =>
  locator.isVisible().catch(() => false);

export type { BridgeEventWindow };

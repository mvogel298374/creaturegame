using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Evolution;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// The battle node (loop-event): build the next foe scaled to run depth, run the <see cref="Battle"/> to a
/// faint, then resolve the post-win consequences — depth++, the level-up evolution offer, and capturing the
/// carried major status for the next encounter. Returns whether the player survived. Evolution stays inside
/// this win resolution rather than as its own node: it is an immediate consequence of <em>this</em> battle's
/// level-up, not an independently sequenced event (<c>GAME_LOOP.md §5</c>).
/// </summary>
internal sealed class BattleRunEvent(
    Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> enemySupplier,
    EncounterTier tier,
    ITypeChart typeChart,
    IBattleInput enemyInput,
    IReadOnlyList<Attack> movePool,
    IBattleRules? rules,
    Bag? playerBag,
    Func<Creature, Task<EvolutionOutcome?>>? checkEvolution,
    Wallet? wallet,
    Func<RewardContext, IRandomSource, RewardChoice> rewardSupplier,
    RunRules? runRules,
    Func<DraftContext, IRandomSource, Task<Creature?>>? draftSupplier,
    Func<BossCatchContext, IRandomSource, Task<Creature?>>? bossCatchSupplier
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var s = ctx.State;
        var player = s.Player;

        // Announce the node so the encounter map can advance its position pin. Elite/Boss always fire (and the
        // client titles a text banner for them, as before); a plain wild node fires only in biome mode — it
        // drives the map pin but the client filters WildBattle out of the text log, so the wild encounter still
        // slides the foe in with no banner. The legacy endless chain (no current biome, no map) stays silent.
        string nodeKind = tier switch
        {
            EncounterTier.Elite => nameof(RunNodeKind.EliteBattle),
            EncounterTier.Boss => nameof(RunNodeKind.BossBattle),
            _ => nameof(RunNodeKind.WildBattle),
        };
        if (tier != EncounterTier.Normal || s.CurrentBiome is not null)
            ctx.Emitter?.Emit(new RunNodeEntered(nodeKind));

        // RunDepth is the progression depth — 0 for the first node, climbing per node traversed (wins +
        // interaction visits; = BattlesWon in the legacy chain). The supplier scales the next foe (BST band,
        // level) to it, themes it to the current biome (null in the legacy chain), and maps this node's
        // EncounterTier to an archetype; see EncounterFactory.CreateEnemyAsync.
        var enemy = await enemySupplier(player, s.RunDepth, s.CurrentBiome, tier);
        // Remember every species faced in this biome — the "fought-only" pool the themed draft may offer from
        // (ENCOUNTER_DESIGN.md §4). Recorded on encounter (win, loss, or flee all count as "faced"); the set is
        // cleared when the next biome is entered. Empty in the legacy chain (no biome), so no draft can fire.
        s.FoughtSpeciesInBiome.Add(enemy.SpeciesId);
        int levelBefore = player.Level;
        var battle = new Battle(
            player,
            enemy,
            typeChart,
            ctx.PlayerInput,
            enemyInput,
            movePool: movePool,
            rules: rules,
            emitter: ctx.Emitter,
            rng: ctx.Rng,
            playerEntryStatus: player.CarriedStatus,
            playerBag: playerBag,
            // Roar/Whirlwind escape a plain wild battle but fail vs the trainer-analog tiers (Elite/Boss).
            escapable: tier == EncounterTier.Normal,
            // Those same trainer-analog tiers (Elite/Boss) are "trainer-owned" for XP — the Gen-1 trainer ×1.5
            // bonus (applied in the seam); a plain wild battle gets none.
            trainerBattle: tier != EncounterTier.Normal,
            runRules: runRules,
            // Party-aware battle (Phase 4 Stage 3): when the lead faints and a bench member is alive, Battle sends
            // in a replacement against this same enemy instead of ending the run. `player` is the party's Lead, so
            // a switch reassigns Party.Lead (⇒ RunState.Player) and the run continues on the survivor.
            playerParty: s.Party
        );
        await battle.StartFightAsync();

        // A forced switch-on-faint (Phase 4 Stage 3) may have changed the active creature mid-battle: Battle
        // reassigns Party.Lead when it sends in a replacement, so the finisher is the *current* lead, not the
        // `player` that started the fight (which may now be fainted on the bench). Re-read it for every post-battle
        // consequence (win/loss, carried status, evolution). When no switch happened, `active` == `player`.
        var active = s.Player;

        // Roar/Whirlwind ended the encounter (a side fled) — neither a win nor a loss. The player survives, so
        // carry its status into the next event and advance the run; no XP/evolution (nothing fainted).
        if (battle.EndedInFlee)
        {
            active.CarriedStatus = CaptureCarriedStatus(active);
            return new FledOutcome(PlayerFled: active.Battle.HasFled);
        }

        // The battle ends when one side faints. With a party, Battle keeps sending in survivors, so reaching here
        // with a fainted active creature means the WHOLE party is down → the run is over (read by the director's
        // while-loop); otherwise it is a win (whoever finished is the active creature).
        if (!active.IsAlive())
            return new BattleOutcome(false);
        s.BattlesWon++;
        await GrantBattleRewardAsync(enemy, s, ctx);

        // Evolution check — Gen 1 attempts evolution on a level-up, so only when this battle actually raised the
        // finisher's level. A declined evolution re-offers at the next level-up.
        // KNOWN DEFECT (TODO.md → "Switched-in creature is the active creature"): the ReferenceEquals gate skips
        // evolution for a switched-in finisher, so a creature that came in and levelled up does NOT evolve. That
        // is NOT a design decision — it is an implementation convenience, because `levelBefore` belongs to the
        // creature that STARTED the battle and there is nothing to compare a different creature against. A
        // switched-in creature is the active creature and must evolve on the same terms (user ruling 2026-07-15).
        // Fix = capture the pre-battle level per creature that takes the field, and drop this gate.
        if (ReferenceEquals(active, player) && active.Level > levelBefore)
            await TryEvolveAsync(active, ctx);

        // Default: the finisher's major status carries into its next encounter, stored ON the creature (the
        // multi-creature carry model — each party member keeps its own ailment while benched); a Poké Center heal
        // clears it. The generation decides the out-of-battle form (Gen 1 reverts Toxic to Poison).
        active.CarriedStatus = CaptureCarriedStatus(active);

        // Acquisition (ENCOUNTER_DESIGN.md §4): the last beat of a win, and at most one offer per win. A Boss win
        // routes to the boss-catch channel — a small chance to add the boss you just beat (Stage 2); every other
        // win routes to the themed draft — cadence × n% × the fought-only pool (Stage 1c). Both raise the same
        // reusable blocking AcquisitionOffered (only the source + how the offered creature is chosen differ); each
        // supplier owns its whole policy and returns a built creature only when it fires, else null (the common
        // case). A headless / AI input declines by default, so neither channel stalls the chain or builds a party.
        if (tier == EncounterTier.Boss)
            await OfferBossCatchAsync(enemy, s, ctx);
        else
            await OfferDraftAsync(s, ctx);

        return new BattleOutcome(true);
    }

    // Rolls the themed draft for this win and, if the supplier offers a creature, raises the acquisition offer
    // (blocking; the client shows the modal) and deposits the result into the party. Silent when no supplier is
    // configured (tests / the legacy chain) or the roll declined to offer (null) — no RNG is drawn on a
    // non-cadence win, so the seeded stream only moves when a draft actually rolls.
    private async Task OfferDraftAsync(RunState s, RunContext ctx)
    {
        if (draftSupplier is null)
            return;
        var offered = await draftSupplier(
            new DraftContext(
                s.Player,
                s.RunDepth,
                s.CurrentBiome,
                s.FoughtSpeciesInBiome,
                s.BattlesWon
            ),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        if (offered is null)
            return;
        await AcquisitionResolution.OfferAndDepositAsync(offered, "ThemedDraft", s.Party, ctx);
    }

    // Rolls the boss catch for this Boss win and, if the supplier offers the defeated boss, raises the acquisition
    // offer (blocking; the client shows the modal) and deposits it into the party — the same offer + roster
    // plumbing the draft uses, only the source ("BossCatch") + the single offered option differ. Silent when no
    // supplier is configured (tests / the legacy chain) or the small catch roll declined (null). Only reached on a
    // Boss win, so a plain wild/elite win never draws the catch roll and can't perturb the seeded stream.
    private async Task OfferBossCatchAsync(Creature boss, RunState s, RunContext ctx)
    {
        if (bossCatchSupplier is null)
            return;
        var offered = await bossCatchSupplier(
            new BossCatchContext(boss),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        if (offered is null)
            return;
        await AcquisitionResolution.OfferAndDepositAsync(offered, "BossCatch", s.Party, ctx);
    }

    // Rolls this win's reward and — if anything rolled — offers it as a pick-one-of-N choice (blocking; the
    // client raises the modal), then applies the chosen option. Silent when nothing was rolled
    // (RewardChoice.None is the common case for a wild win — a chance at a drop, not a guarantee; a Boss always
    // rolls). Headless/AI inputs auto-pick option 0, so the chain never stalls.
    private Task GrantBattleRewardAsync(Creature enemy, RunState s, RunContext ctx)
    {
        var choice = rewardSupplier(
            new RewardContext(
                NodeKindForTier(tier),
                enemy.Level,
                s.RunDepth,
                PlayerCondition.From(s.Player)
            ),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        return RewardResolution.OfferAndApplyAsync(choice, "Battle", wallet, playerBag, ctx);
    }

    private static RunNodeKind NodeKindForTier(EncounterTier tier) =>
        tier switch
        {
            EncounterTier.Elite => RunNodeKind.EliteBattle,
            EncounterTier.Boss => RunNodeKind.BossBattle,
            _ => RunNodeKind.WildBattle,
        };

    // Offers, then applies, a pending evolution if the resolver reports one. The player can cancel (Gen 1
    // B-cancel) — the prompt blocks awaiting the decision; on cancel the creature is untouched and re-offered
    // at the next level-up. The from-identity is captured before EvolveTo (which overwrites name/species/stats)
    // so the events carry both forms for the sprite morph.
    private async Task TryEvolveAsync(Creature player, RunContext ctx)
    {
        if (checkEvolution is null)
            return;
        if (await checkEvolution(player) is not { } evolution)
            return;

        string fromName = player.Name;
        int fromSpeciesId = player.SpeciesId;
        var newForm = evolution.NewForm;
        string toName = newForm.Name.ToUpper(); // matches how EvolveTo names the creature

        ctx.Emitter?.Emit(new EvolutionOffered(fromName, toName, fromSpeciesId, newForm.Id));
        bool allow = await ctx.PlayerInput.ConfirmEvolutionAsync(
            new EvolutionPromptContext(player, newForm.Id, toName)
        );
        if (!allow)
        {
            ctx.Emitter?.Emit(new EvolutionCancelled(fromName));
            return;
        }

        player.EvolveTo(newForm);
        player.Learnset = evolution.NewLearnset;

        ctx.Emitter?.Emit(
            new CreatureEvolved(fromName, player.Name, fromSpeciesId, player.SpeciesId)
        );

        // Evolution grants no moves itself, but the evolved form may learn one at the current level.
        await MoveLearning.LearnMovesForLevelAsync(
            player,
            player.Level,
            ctx.Emitter,
            ctx.PlayerInput
        );
    }

    // Major status carries into the next encounter; the generation decides what each status becomes out of
    // battle (Gen 1 reverts Toxic to regular Poison) via IBattleRules.CarryStatusOutOfBattle. Volatile
    // conditions (confusion, stat stages, …) live only in BattleState and are dropped by the per-battle reset
    // — they are never captured here. The sleep counter carries so a sleeping creature keeps counting down.
    private CarriedStatus? CaptureCarriedStatus(Creature c)
    {
        var status = (rules ?? Gen1BattleRules.Instance).CarryStatusOutOfBattle(c.Battle.Status);
        if (status == StatusCondition.None)
            return null;
        int sleepTurns = status == StatusCondition.Sleep ? c.Battle.SleepTurns : 0;
        return new CarriedStatus(status, sleepTurns);
    }
}

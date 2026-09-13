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

        // The supplier scales the next foe to RunDepth/biome/tier — see EncounterFactory.CreateEnemyAsync.
        var enemy = await enemySupplier(player, s.RunDepth, s.CurrentBiome, tier);
        // The fought-only pool the themed draft offers from (ENCOUNTER_DESIGN.md §4); cleared on biome change.
        s.FoughtSpeciesInBiome.Add(enemy.SpeciesId);
        // Per-member pre-battle level snapshot (keyed by reference — Creature overrides neither Equals nor
        // GetHashCode) so the post-win evolution check fires for ANY creature that levelled — STATE_MODEL.md §2.
        var preLevel = s.Party.Members.ToDictionary(m => m, m => m.Level);
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

        // A fainted active creature normally means the whole party is down. The exception is a mutual KO
        // (battle.PlayerWon true despite the finisher fainting) — TODO_ARCHIVE.md → "Mutual KO ends the run
        // even with a live bench". Counted here, above the guard, so a trade-kill still shows in the run summary.
        if (battle.PlayerWon)
            s.BattlesWon++;

        if (!active.IsAlive() && !(battle.PlayerWon && await PromoteSurvivorAsync(s, ctx, active)))
            return new BattleOutcome(false);
        await GrantBattleRewardAsync(enemy, s, ctx);

        // Every creature that levelled this battle evolves on the same terms (STATE_MODEL.md §2) — active
        // first, then the bench in roster order. A creature added mid-battle (a draft) isn't in the snapshot.
        bool anyEvolved = false;
        foreach (var member in EvolutionOrder(s.Party, active))
        {
            if (member.IsAlive() && preLevel.TryGetValue(member, out int lvl) && member.Level > lvl)
                anyEvolved |= await TryEvolveAsync(member, ctx);
        }

        // The party strip is fed only by PartyUpdated snapshots (+ the connect-time /party hydrate), and an
        // evolution renames the creature. The nameplate/HUD retarget on CreatureEvolved directly, the strip row
        // does not — so without this the roster keeps the PRE-evolution name until some unrelated later event
        // happens to resync it, disagreeing with the nameplate right beside it. The win's own level-up snapshot
        // (Battle) can't cover this: it is emitted before the evolution runs, so it carries the old name.
        // Pushed once after the loop, coalescing a multi-creature batch into a single repaint.
        if (anyEvolved)
            ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(s.Party)));

        // Skipped when the finisher fainted (the mutual-KO path) — a corpse has no ailment worth carrying, and
        // every revive path clears CarriedStatus anyway. Guarded explicitly rather than relying on that.
        if (active.IsAlive())
            active.CarriedStatus = CaptureCarriedStatus(active);

        // At most one acquisition offer per win, routed by tier (ENCOUNTER_DESIGN.md §4) — Boss catches, every
        // other win themed-drafts. XP/reward is already applied, so the catch is pure upside.
        if (tier == EncounterTier.Boss)
            await OfferBossCatchAsync(enemy, s, ctx);
        else
            await OfferDraftAsync(s, ctx);

        return new BattleOutcome(true);
    }

    /// <summary>A mutual KO left the finisher fainted but the party still standing: the player picks who leads
    /// on (reusing the forced-switch prompt — its modal already disables fainted members), and the run
    /// continues. A lead reassignment, not a send-in: no entry status, no <c>CreatureSwitchedIn</c>. Returns
    /// false when the whole party is down. Full rationale: TODO_ARCHIVE.md → "Mutual KO ends the run even with
    /// a live bench".</summary>
    private static async Task<bool> PromoteSurvivorAsync(
        RunState s,
        RunContext ctx,
        Creature fainted
    )
    {
        var party = s.Party;
        // Nobody left to promote ⇒ the whole party is down and the run really is over. Checked BEFORE the prompt:
        // a modal with nothing to pick from would park a blocking await the player can never answer.
        if (party.FirstLiveIndex() < 0)
            return false;

        ctx.Emitter?.Emit(new SwitchInOffered(PartyProjection.Snapshot(party), fainted.Name));
        int index = await ctx.PlayerInput.ChooseSwitchInAsync(new SwitchInContext(party));

        party.SetLead(party.CorrectSwitchInPick(index));
        ctx.Emitter?.Emit(new LeadChanged(party.Lead.Name, party.Lead.SpeciesId));
        ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(party)));
        return true;
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

    // Evolution offer order: the on-field finisher first (the creature the player just watched level up), then the
    // other party members in roster order. Keeps a bench member's evolution prompt from jumping ahead of the
    // active creature's, so the surfacing reads in the order the player expects.
    private static IEnumerable<Creature> EvolutionOrder(Party party, Creature active)
    {
        yield return active;
        foreach (var m in party.Members)
            if (!ReferenceEquals(m, active))
                yield return m;
    }

    // Offers, then applies, a pending evolution if the resolver reports one. The player can cancel (Gen 1
    // B-cancel) — the prompt blocks awaiting the decision; on cancel the creature is untouched and re-offered
    // at the next level-up. The from-identity is captured before EvolveTo (which overwrites name/species/stats)
    // so the events carry both forms for the sprite morph. Returns true only when the creature actually changed
    // form, which is what gates the caller's roster repaint (a cancel leaves the strip already correct).
    private async Task<bool> TryEvolveAsync(Creature player, RunContext ctx)
    {
        if (checkEvolution is null)
            return false;
        if (await checkEvolution(player) is not { } evolution)
            return false;

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
            return false;
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
        return true;
    }

    // Major status carries into the next encounter; the generation decides what each status becomes out of
    // battle (Gen 1 reverts Toxic to regular Poison). Volatile conditions (confusion, stat stages, …) live only
    // in BattleState and are dropped by the per-battle reset — they are never captured. Shares the single capture
    // rule with Battle's voluntary switch-out (CarriedStatus.Capture).
    private CarriedStatus? CaptureCarriedStatus(Creature c) =>
        CarriedStatus.Capture(rules ?? Gen1BattleRules.Instance, c);
}

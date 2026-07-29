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
        // Snapshot every party member's pre-battle level (keyed by reference) so the post-win evolution check can
        // fire for ANY creature that levelled this battle — the active lead, a forced switch-in that finished, or a
        // bench member raised by the innate Exp-Share — each compared against its own starting level, not a single
        // local that only describes the creature that started the fight.
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

        // The battle ends when one side faints. With a party, Battle keeps sending in survivors on a LOSING faint,
        // so reaching here with a fainted active creature normally means the WHOLE party is down → the run is over
        // (read by the director's while-loop).
        //
        // The one exception is a MUTUAL KO — Self-Destruct/Explosion, Struggle recoil, or end-of-turn
        // Burn/Poison/Leech taking both sides down on the same turn. Battle's enemy-faint check runs first, so
        // that is a WIN (battle.PlayerWon) even though the finisher is fainted, and the forced faint-switch is
        // correctly skipped — there is no enemy left to send anyone in against. The run must not end while the
        // bench still holds a live creature (the Phase 4 Stage 3 rule: a run ends only when the WHOLE party is
        // down), so the player PICKS who leads on and we take the win path below. Promoting here, before the
        // reward/draft rolls, also keeps them reading a live lead (PlayerCondition / draft scaling).
        // Counted here, ABOVE the guard, so a trade-kill that takes the last creature with it still shows in the
        // run summary — under the mutual-KO ruling it IS a win, and the run ending doesn't unmake it. Keyed on
        // PlayerWon rather than on reaching the win path, which is what keeps an ordinary loss from counting.
        if (battle.PlayerWon)
            s.BattlesWon++;

        if (!active.IsAlive() && !(battle.PlayerWon && await PromoteSurvivorAsync(s, ctx, active)))
            return new BattleOutcome(false);
        await GrantBattleRewardAsync(enemy, s, ctx);

        // Evolution check — Gen 1 attempts evolution on a level-up, so only for creatures that actually gained a
        // level this battle. Every such creature evolves on the same terms — the active lead, a forced switch-in
        // that finished, or a bench member raised by the innate Exp-Share (user ruling 2026-07-15: a switched-in
        // creature IS the active creature; there is no second-class participant). Each member is compared against
        // its own pre-battle level captured above, active first (the creature the player just watched), then the
        // bench in roster order. A declined evolution re-offers at the next level-up; a creature added mid-battle
        // (a draft) isn't in the snapshot and is skipped.
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

        // Default: the finisher's major status carries into its next encounter, stored ON the creature (the
        // multi-creature carry model — each party member keeps its own ailment while benched); a Poké Center heal
        // clears it. The generation decides the out-of-battle form (Gen 1 reverts Toxic to Poison).
        //
        // Skipped when the finisher FAINTED — the mutual-KO path (its survivor is now the lead, but `active` is
        // still the creature that actually fought, which is the one whose status this describes). A corpse has no
        // ailment worth carrying: it cannot take the field again until something revives it, and every revive path
        // clears CarriedStatus anyway. Guarded explicitly rather than left to write an inert value, so this does
        // not silently depend on that invariant holding elsewhere.
        if (active.IsAlive())
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

    /// <summary>
    /// A mutual KO left the finisher fainted but the party still standing: the player <b>picks</b> who leads on,
    /// and the run continues. Returns false when the whole party is down (a real wipe), which the caller reads as
    /// the end of the run.
    /// <para><b>Why the forced-switch prompt and not the between-biome lead choice</b> (user ruling 2026-07-28,
    /// after <c>requirements-review</c> challenged an earlier silent auto-promotion): "your active creature
    /// fainted, a bench member must take over" is exactly the forced faint-switch's situation, and its prompt is
    /// already the right shape — non-dismissable, fainted members greyed out, titled with the name that just
    /// dropped. The between-biome <c>LeadChoiceOffered</c> assumes a post-Poké-Center party where every member is
    /// pickable, so its modal doesn't disable a downed one.</para>
    /// <para>The <em>result</em>, though, is a lead reassignment rather than a send-in — nobody takes the field
    /// here, so there is no entry status and no volatile reset (each member already carries its own status), and
    /// no <c>CreatureSwitchedIn</c>. Hence <c>LeadChanged</c> + <c>PartyUpdated</c>, the out-of-battle lead-swap
    /// wire. A stale / out-of-range / fainted pick is corrected to the first live member, mirroring
    /// <c>Battle</c>'s own guard, so a malformed client can never promote a corpse and strand the run.</para>
    /// </summary>
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

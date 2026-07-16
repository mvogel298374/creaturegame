using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// The between-biome lead choice (interaction-event, Phase 4 Stage 1d): at a biome boundary — after the Poké
/// Center, before the route choice — offer the party and let the player pick which member leads into the next
/// biome. Only reached when the party holds more than one creature (the director gates it). Reassigns
/// <see cref="Creatures.Party.Lead"/> (⇒ <see cref="RunState.Player"/>) when the pick differs from the current
/// lead; keeping the current lead (or a stale / out-of-range pick) is a no-op. Automated / AI inputs keep the
/// current lead via the <see cref="IBattleInput.ChooseLeadAsync"/> default, so a headless run never stalls.
/// <para>Touches nothing in the battle engine — this is a between-biome choice, not in-battle switching. A swap
/// never touches either creature's <see cref="Creature.CarriedStatus"/>: the outgoing lead keeps whatever it is
/// carrying while it benches (still ailed if the preceding Poké Center was declined), and the incoming lead
/// enters on its own carried status. Nothing transfers between them, so the previous lead's status can never leak
/// onto the switch-in.</para>
/// </summary>
internal sealed class LeadChoiceEvent : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var party = ctx.State.Party;

        ctx.Emitter?.Emit(new LeadChoiceOffered(PartyProjection.Snapshot(party)));
        int index = await ctx.PlayerInput.ChooseLeadAsync(new LeadChoiceContext(party));

        // Apply only a real change: an in-range index that isn't already the lead. Keeping the current lead or a
        // stale / out-of-range pick leaves the roster untouched and emits nothing (a pure no-op).
        if (index >= 0 && index < party.Count && index != party.LeadIndex)
        {
            party.SetLead(index);
            // No status reconciliation needed: under the multi-creature carry model each creature carries its own
            // out-of-battle status (Creature.CarriedStatus), so the incoming lead enters on its own status and the
            // outgoing lead keeps its ailment while benched. The next battle sources playerEntryStatus from the
            // new lead directly, so the previous lead's status can never leak onto the switch-in.
            ctx.Emitter?.Emit(new LeadChanged(party.Lead.Name, party.Lead.SpeciesId));
            ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(party)));
        }

        return new LeadChoiceOutcome();
    }
}

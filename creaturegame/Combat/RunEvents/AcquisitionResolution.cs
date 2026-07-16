using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// Shared acquisition-offer resolution used by every acquisition channel (themed draft and boss catch):
/// raise the blocking offer — the client shows the modal, so the player accepts / declines / (on a full party)
/// picks a member to swap out — then deposit the result into the <see cref="Party"/> and announce it. A
/// <em>decline</em> (the automated / AI default) is a pure sequencing no-op: the roster is unchanged and only an
/// <see cref="AcquisitionDeclined"/> line is emitted. An accept a full party can't honour (no valid replace slot)
/// falls back to a decline, so a stale pick never strands the run. On a deposit a <see cref="CreatureAcquired"/>
/// plus a fresh <see cref="PartyUpdated"/> snapshot follow. Both channels reuse this — only the offered creature
/// and the <paramref name="source"/> label differ.
/// </summary>
internal static class AcquisitionResolution
{
    public static async Task OfferAndDepositAsync(
        Creature offered,
        string source,
        Party party,
        RunContext ctx
    )
    {
        ctx.Emitter?.Emit(
            new AcquisitionOffered(
                source,
                offered.SpeciesId,
                offered.Name,
                offered.Level,
                offered.Types,
                offered.Attributes.MaxHP,
                party.IsFull,
                PartyProjection.Snapshot(party)
            )
        );

        var decision = await ctx.PlayerInput.ChooseAcquisitionAsync(
            new AcquisitionContext(offered, party, source)
        );

        if (!decision.Accept)
        {
            ctx.Emitter?.Emit(new AcquisitionDeclined(offered.Name));
            return;
        }

        if (party.IsFull)
        {
            // Full roster → the accept must name a bench slot to swap out; an out-of-range / missing slot (a
            // stale pick) is tolerated as a decline rather than stranding the run. The lead slot is refused too:
            // swapping the active creature mid-chain is a lead change, which is Stage 1d's between-biome
            // ChooseLeadAsync flow — this offer must never reassign the lead (the client hides it as a target,
            // and the server enforces the same rule so a malformed / regressed client can't slip it through).
            if (
                decision.ReplaceSlot is not int slot
                || slot < 0
                || slot >= party.Count
                || slot == party.LeadIndex
            )
            {
                ctx.Emitter?.Emit(new AcquisitionDeclined(offered.Name));
                return;
            }
            string replacedName = party.Members[slot].Name;
            party.Replace(slot, offered);
            ctx.Emitter?.Emit(
                new CreatureAcquired(offered.Name, offered.SpeciesId, true, replacedName)
            );
        }
        else
        {
            party.Add(offered);
            ctx.Emitter?.Emit(new CreatureAcquired(offered.Name, offered.SpeciesId, false, null));
        }

        ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(party)));
    }
}

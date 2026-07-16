namespace creaturegame.Combat;

/// <summary>
/// The Poké Center node (interaction-event): offer a full restore, then heal or leave the player as-is per the
/// choice (interactive input blocks on the accept/skip; automated inputs accept by default, so the chain still
/// heals headless/in tests). Accepting cures HP/PP/status — so nothing carries; declining keeps the carried
/// status the battle event captured.
/// </summary>
internal sealed class RecoveryRunEvent : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var s = ctx.State;
        var player = s.Player;

        ctx.Emitter?.Emit(new RecoveryOffered(player.Name, player.SpeciesId, s.BattlesWon));
        bool accept = await ctx.PlayerInput.ConfirmRecoveryAsync(
            new RecoveryContext(player, s.BattlesWon)
        );
        if (accept)
        {
            // A Poké Center restores the WHOLE party, not just the lead — benched members keep permanent HP
            // across biomes, so this tops every owned creature back up (HP/PP/status). FullHeal also clears each
            // member's own persisted CarriedStatus (the multi-creature carry model), so nothing carries onward.
            foreach (var member in s.Party.Members)
                member.FullHeal();
            ctx.Emitter?.Emit(new PlayerRecovered(player.Name, player.Attributes.HP));
            // The lead-only PlayerRecovered above can't carry the bench's restored HP — so push a fresh party
            // snapshot too (the 1a/1b deferral: whole-party heal is state-correct, this makes the benched
            // members' heal visible on the wire for the party panel). A no-op-looking single-member party still
            // keeps the panel in lockstep.
            ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(s.Party)));
        }
        else
        {
            ctx.Emitter?.Emit(new RecoveryDeclined(player.Name));
        }

        return new RecoveryOutcome(accept);
    }
}

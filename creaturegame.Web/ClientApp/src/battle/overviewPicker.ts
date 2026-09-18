import type { PartyMember } from './timeline';

// The rules behind the CHECK POKEMON party-member picker (CreatureOverview.tsx): which slot opens by default,
// whether the picker row renders at all, and which URL a slot resolves to. Kept pure (no React) for the same
// reason playerIdentity.ts was extracted: Vitest can pin the rule directly, without a DOM harness.

// The lead's own slot, or 0 (the starter/first-member fallback the server applies too — see
// GameSessionManager.PartyMemberAt) when the party is empty or carries no lead marker.
export function defaultOverviewSlot(party: PartyMember[]): number {
  return Math.max(0, party.findIndex(m => m.isLead));
}

// The picker only earns its place once there's more than one member to choose between.
export function showOverviewPicker(party: PartyMember[]): boolean {
  return party.length > 1;
}

// While the party view is empty — e.g. mid-resume, before hydrateParty's PartyUpdated/party fetch has landed —
// slot 0 is not a safe stand-in for "the starter": a party may already be wired server-side with a different
// creature in the lead. Fall back to the no-slot endpoint, which resolves the live lead server-side instead.
export function overviewSlotUrl(gameId: string, party: PartyMember[], slot: number): string {
  return party.length === 0 ? `/api/game/${gameId}/player` : `/api/game/${gameId}/player/${slot}`;
}

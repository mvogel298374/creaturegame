// The base-power strength cue for a damaging move in the FIGHT menu: the raw power number, bucketed onto a
// cool→hot ramp (steel → ember) so relative strength reads at a glance. The ramp is deliberately DISTINCT from
// the effectiveness pill's green/amber/red (which signals matchup, not power) — so a strength pill is never
// mistaken for a type cue. Fixed-damage / status moves report power 0 (or undefined) → no pill, mirroring how
// STAB/effectiveness stay silent for them. The tier thresholds are a display bucketing, not a Gen 1 rule.
//
// Kept as a pure module (no React / Phaser) so it can be unit-tested directly, like bag.ts / regionMap.ts.
export function powerPill(power: number | undefined): { label: string; cls: string } | null {
  if (!power || power <= 0) return null; // status / fixed-damage move → no cue
  const cls =
    power >= 110 ? 'move-pow--max'
    : power >= 80 ? 'move-pow--strong'
    : power >= 50 ? 'move-pow--mid'
    : 'move-pow--weak';
  return { label: String(power), cls };
}

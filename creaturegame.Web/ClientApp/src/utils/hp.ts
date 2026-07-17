// The HP-bar colour band, shared by every place that draws a party member's health (the field strip, the
// lead choice, the forced switch-in). Mirrors the thresholds HpBar uses for the main nameplates.
export function hpState(hp: number, maxHp: number): 'high' | 'mid' | 'low' {
  const pct = maxHp > 0 ? (hp / maxHp) * 100 : 0;
  return pct > 50 ? 'high' : pct > 25 ? 'mid' : 'low';
}

// Clamped fill width (0–100%) for an HP bar, guarding a zero/absent maxHp.
export function hpPercent(hp: number, maxHp: number): number {
  return maxHp > 0 ? Math.max(0, Math.min(100, (hp / maxHp) * 100)) : 0;
}

// Snapshot of the live player creature for the CHECK POKEMON overview — mirrors PlayerOverviewDto on the
// backend (GET /api/game/{gameId}/player). Gen 1 model: five stats with a single Special.
export interface StatRow {
  label: string;   // HP / ATK / DEF / SPC / SPD
  value: number;   // actual computed stat (for HP, the max)
  dv: number;      // 0–15
  statExp: number; // Stat-Exp (the Gen 1 EV analogue), 0–65535
}

export interface MoveRow {
  name: string;
  type: string;
  category: string; // Physical | Special | Status
  power: number;    // 0 = no base power (status / fixed-damage) → render as "—"
  accuracy: number;
  ppCurrent: number;
  ppMax: number;
  description: string | null;
}

export interface PlayerOverview {
  name: string;
  speciesId: number;
  level: number;
  type1: string;
  type2: string | null;
  status: string;
  hp: number;
  maxHp: number;
  xpThisLevel: number;
  xpToNextLevel: number;
  baseStatTotal: number;
  generation: number;
  stats: StatRow[];
  moves: MoveRow[];
  // Later-generation fields, gated on `generation` (all null/hidden in Gen 1):
  heldItem: string | null;  // Gen 2+
  ability: string | null;   // Gen 3+
  nature: string | null;    // Gen 3+
  teraType: string | null;  // Gen 9
}

// Snapshot of a party member for the CHECK POKEMON overview — mirrors PlayerOverviewDto on the
// backend (GET /api/game/{gameId}/player/{slot}). Gen 1 model: five stats with a single Special.
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

export interface StageRow {
  stat: string;  // Attack / Defense / Special / Speed / Accuracy / Evasion
  stage: number; // −6…+6, never 0 (only non-zero stages are sent)
}

export interface PlayerOverview {
  name: string;
  speciesName: string; // the species' own name, independent of a nickname (may equal `name`)
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
  stats: StatRow[];
  moves: MoveRow[];
  stages: StageRow[] | null; // Dev Mode enemy overview only; null for every party member
}

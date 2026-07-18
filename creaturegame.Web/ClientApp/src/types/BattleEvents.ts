export interface MoveInfo {
  name: string;
  type: string;
  ppCurrent: number;
  ppMax: number;
  disabled?: boolean;
  // True when the move gets the Same-Type Attack Bonus for the player (damaging move matching the player's
  // current type). Computed by the engine; drives the subtle STAB highlight in the move menu.
  stab?: boolean;
  // Type-effectiveness multiplier vs the current enemy (0, 0.25, 0.5, 1, 2, 4); 1 for neutral/non-damaging.
  // Computed by the engine via the type chart; drives the ×N effectiveness pill in the move menu.
  effectiveness?: number;
  // The move's raw base power (Gen-1 move data). Drives the strength pill in the move menu. Fixed-damage /
  // status moves have no base power and report 0 (no pill shown) — mirroring stab/effectiveness's "no cue".
  power?: number;
}
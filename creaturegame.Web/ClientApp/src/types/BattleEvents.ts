export interface MoveInfo {
  name: string;
  type: string;
  ppCurrent: number;
  ppMax: number;
  disabled?: boolean;
  // True when the move gets the Same-Type Attack Bonus for the player (damaging move matching the player's
  // current type). Computed by the engine; drives the subtle STAB highlight in the move menu.
  stab?: boolean;
}
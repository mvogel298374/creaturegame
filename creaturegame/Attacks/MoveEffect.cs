namespace creaturegame.Attacks;

public enum StageStat   { Attack, Defense, Special, Speed, Accuracy, Evasion }
public enum StageTarget { Self, Foe }
public enum MoveEffect  { None, Haze, Flinch, Recharge, LeechSeed, Binding, TwoTurn, Metronome, Confuse, MultiHit, PayDay, Crash, Recoil, Rampage, Disable, Mist }
public enum DamageCategory { Standard, Fixed, LevelBased, Drain, OHKO, SelfDestruct, SuperFang }

public record StatEffect(StageStat Stat, int Delta, StageTarget Target, int Chance);

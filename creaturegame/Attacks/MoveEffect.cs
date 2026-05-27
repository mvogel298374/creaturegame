namespace creaturegame.Attacks;

public enum StageStat   { Attack, Defense, Special, Speed, Accuracy, Evasion }
public enum StageTarget { Self, Foe }
public enum MoveEffect  { None, Haze, Flinch }

public record StatEffect(StageStat Stat, int Delta, StageTarget Target, int Chance);

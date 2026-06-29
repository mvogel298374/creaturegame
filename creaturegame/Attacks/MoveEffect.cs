namespace creaturegame.Attacks;

public enum StageStat
{
    Attack,
    Defense,
    Special,
    Speed,
    Accuracy,
    Evasion,
}

public enum StageTarget
{
    Self,
    Foe,
}

public enum MoveEffect
{
    None,
    Haze,
    Flinch,
    Recharge,
    LeechSeed,
    Binding,
    TwoTurn,
    Metronome,
    Confuse,
    MultiHit,
    PayDay,
    Crash,
    Recoil,
    Rampage,
    Disable,
    Mist,
    Counter,
    Rage,
    Heal,
    Mimic,
    Reflect,
    LightScreen,
    FocusEnergy,
    Bide,
    MirrorMove,
    DreamEater,
    Splash,
    Rest,
    Substitute,
    Transform,
    Conversion,

    // Roar / Whirlwind: in a wild battle, the targeted creature flees and the battle ends (Gen 1). Appended
    // last so existing persisted Effect values (moves.db) stay stable — no data migration for other moves.
    ForceFlee,
}

public enum DamageCategory
{
    Standard,
    Fixed,
    LevelBased,
    Drain,
    OHKO,
    SelfDestruct,
    SuperFang,
    Psywave,
}

public record StatEffect(StageStat Stat, int Delta, StageTarget Target, int Chance);

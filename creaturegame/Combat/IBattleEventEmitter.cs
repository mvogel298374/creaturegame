namespace creaturegame.Combat;

public interface IBattleEventEmitter
{
    void Emit(BattleEvent evt);
}

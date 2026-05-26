namespace creaturegame.Web.Hubs;

public interface IBattleClient
{
    Task OnBattleEvent(string eventType, object payload);
}
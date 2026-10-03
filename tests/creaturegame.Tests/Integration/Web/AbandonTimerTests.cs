using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

public class AbandonTimerTests
{
    private static readonly TimeSpan Never = TimeSpan.FromHours(1);

    private static Task PendingTurn(ActiveBattle battle)
    {
        var player = TestCreatures.Make("Player");
        var ctx = new TurnContext
        {
            Attacker = player,
            Defender = TestCreatures.Make("Enemy"),
            TypeChart = Gen1TypeChart.Instance,
            Rules = Gen1BattleRules.Instance,
            TurnNumber = 1,
        };
        return battle.Input.ChooseTurnActionAsync(ctx);
    }

    [Fact]
    public async Task OnGraceElapsed_CurrentTimer_CancelsTheInput()
    {
        var battle = new ActiveBattle();
        var turn = PendingTurn(battle);
        var timer = battle.ScheduleAbandon(Never);

        battle.OnGraceElapsed(timer);

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await turn);
    }

    [Fact]
    public void OnGraceElapsed_TimerCancelledByReconnect_DoesNotCancelTheInput()
    {
        var battle = new ActiveBattle();
        var turn = PendingTurn(battle);
        var timer = battle.ScheduleAbandon(Never);
        battle.CancelAbandon();

        battle.OnGraceElapsed(timer);

        Assert.False(turn.IsCompleted);
    }

    [Fact]
    public void OnGraceElapsed_TimerReplacedByANewOne_DoesNotCancelTheInput()
    {
        var battle = new ActiveBattle();
        var turn = PendingTurn(battle);
        var stale = battle.ScheduleAbandon(Never);
        battle.ScheduleAbandon(Never);

        battle.OnGraceElapsed(stale);

        Assert.False(turn.IsCompleted);
    }
}

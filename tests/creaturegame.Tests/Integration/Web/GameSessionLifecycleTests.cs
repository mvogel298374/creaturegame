using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Generations;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;
using Microsoft.Extensions.Time.Testing;

namespace creaturegame.Tests.Integration.Web;

public class GameSessionLifecycleTests
{
    private static readonly TimeSpan Never = TimeSpan.FromHours(1);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private static Creature PlayerWithAMove()
    {
        var player = TestCreatures.Make("Player");
        player.AddAttack(
            new Attack
            {
                Id = 1,
                Name = "Tackle",
                PowerPointsMax = 35,
            }
        );
        return player;
    }

    private static Task<TurnChoice> PendingTurn(ActiveBattle battle, Creature? player = null)
    {
        var ctx = new TurnContext
        {
            Attacker = player ?? TestCreatures.Make("Player"),
            Defender = TestCreatures.Make("Enemy"),
            TypeChart = Gen1TypeChart.Instance,
            Rules = Gen1BattleRules.Instance,
            TurnNumber = 1,
        };
        return battle.Input.ChooseTurnActionAsync(ctx);
    }

    private static string Register(GameSessionManager manager) =>
        manager.RegisterSession(
            new Creature("TESTMON"),
            [],
            new Bag(),
            new Wallet(),
            [],
            new SeededRandomSource(1),
            [],
            Difficulty.Normal,
            Generation.One,
            new Dictionary<int, IReadOnlyList<int>>()
        );

    // A run attached on conn-1 whose task is parked at its first DB read until disposed.
    private sealed class StartedRun : IDisposable
    {
        public FakeTimeProvider Clock { get; } = new();
        public RecordingHubContext Hub { get; } = new();
        public ManualResetEventSlim DbGate { get; } = new(initialState: false);
        public GameSessionManager Manager { get; }
        public string GameId { get; }
        public ActiveBattle Battle { get; }

        public StartedRun()
        {
            Manager = new GameSessionManager(
                Hub,
                BlockedEncounterFactory.Create(DbGate),
                timeProvider: Clock
            );
            GameId = Register(Manager);
            Assert.True(Manager.AttachConnection(GameId, "conn-1"));
            Battle = Manager.ActiveBattleFor(GameId)!;
        }

        public void Dispose() => DbGate.Set();
    }

    [Fact]
    public void PendingSession_PastTheTtl_IsEvictedByTheNextRegistration()
    {
        var clock = new FakeTimeProvider();
        var manager = new GameSessionManager(
            hubContext: null!,
            NoDbEncounterFactory.Create(),
            timeProvider: clock
        );
        string stale = Register(manager);

        clock.Advance(GameSessionManager.PendingSessionTtl + OneSecond);
        Register(manager);

        Assert.Null(manager.GetPlayerCreature(stale));
        Assert.False(manager.AttachConnection(stale, "conn-1"));
    }

    [Fact]
    public void PendingSession_AtExactlyTheTtl_IsKept()
    {
        var clock = new FakeTimeProvider();
        var manager = new GameSessionManager(
            hubContext: null!,
            NoDbEncounterFactory.Create(),
            timeProvider: clock
        );
        string session = Register(manager);

        clock.Advance(GameSessionManager.PendingSessionTtl);
        Register(manager);

        Assert.NotNull(manager.GetPlayerCreature(session));
    }

    [Fact]
    public async Task DetachConnection_AbandonsTheRun_OnceTheGraceRunsOut()
    {
        using var run = new StartedRun();
        var turn = PendingTurn(run.Battle);

        run.Manager.DetachConnection("conn-1");
        run.Clock.Advance(GameSessionManager.ReconnectGrace - OneSecond);
        Assert.False(turn.IsCompleted);

        run.Clock.Advance(OneSecond);
        await Assert.ThrowsAsync<TaskCanceledException>(() => turn.WaitAsync(Settle));
    }

    [Fact]
    public async Task Reconnect_InsideTheGrace_CancelsTheAbandon()
    {
        using var run = new StartedRun();
        var turn = PendingTurn(run.Battle, PlayerWithAMove());

        run.Manager.DetachConnection("conn-1");
        run.Clock.Advance(GameSessionManager.ReconnectGrace / 2);
        Assert.True(run.Manager.AttachConnection(run.GameId, "conn-2"));
        run.Clock.Advance(GameSessionManager.ReconnectGrace);

        // A fired abandon would have cancelled the handshake, so this answer could no longer complete it.
        run.Manager.SetMoveChoice("conn-2", 0);
        Assert.IsType<MoveTurnChoice>(await turn.WaitAsync(Settle));
    }

    [Fact]
    public async Task Reconnect_ReplaysAnOpenPrompt_UntilTheRunConsumesItsAnswer()
    {
        using var run = new StartedRun();
        run.Battle.Emitter!.Emit(new RecoveryOffered("Player", 1, 1, 0));
        var recovery = run.Battle.Input.ConfirmRecoveryAsync(null!);

        Assert.True(run.Manager.AttachConnection(run.GameId, "conn-2"));
        Assert.Equal("RecoveryOffered", run.Hub.EventsFor("conn-2")[^1].Type);

        run.Battle.Input.SetRecoveryChoice(true);
        await recovery.WaitAsync(Settle);
        Assert.True(run.Manager.AttachConnection(run.GameId, "conn-3"));
        Assert.DoesNotContain(run.Hub.EventsFor("conn-3"), e => e.Type == "RecoveryOffered");
    }

    [Fact]
    public async Task DetachConnection_FromAConnectionAlreadyReplaced_IsIgnored()
    {
        using var run = new StartedRun();
        var turn = PendingTurn(run.Battle, PlayerWithAMove());
        Assert.True(run.Manager.AttachConnection(run.GameId, "conn-2"));

        run.Manager.DetachConnection("conn-1");
        run.Clock.Advance(GameSessionManager.ReconnectGrace + OneSecond);

        run.Manager.SetMoveChoice("conn-2", 0);
        Assert.IsType<MoveTurnChoice>(await turn.WaitAsync(Settle));
    }

    [Fact]
    public async Task SetItemChoice_WithAnUnknownItemId_AdvancesTheTurnWithTheFirstUsableMove()
    {
        using var run = new StartedRun();
        var player = PlayerWithAMove();
        var turn = PendingTurn(run.Battle, player);

        run.Manager.SetItemChoice(
            "conn-1",
            itemId: 9999,
            targetMoveSlot: null,
            targetPartySlot: null
        );

        var choice = Assert.IsType<MoveTurnChoice>(await turn.WaitAsync(Settle));
        Assert.Same(player.MoveSet[0], choice.Move);
    }

    [Fact]
    public async Task RunLoopThrowing_SendsRunFaultedOnce_AndDropsTheSession()
    {
        using var run = new StartedRun();

        run.DbGate.Set();
        await run.Battle.RunTask!.WaitAsync(Settle);

        Assert.Single(run.Hub.EventsFor("conn-1"), e => e.Type == "RunFaulted");
        Assert.Null(run.Manager.GetGeneration(run.GameId));
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

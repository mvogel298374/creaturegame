using creaturegame.Combat;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Unit;

public class EmitterSendFailureTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Emit_AFaultedSend_IsReportedWithItsEventType()
    {
        var boom = new InvalidOperationException("connection gone");
        var hub = new RecordingHubContext { FailSendsWith = boom };
        var reported = new TaskCompletionSource<(string Type, Exception Error)>();
        var emitter = new SignalRBattleEventEmitter(
            hub,
            () => "conn-1",
            onSendFailed: (type, ex) => reported.TrySetResult((type, ex))
        );

        emitter.Emit(new BattleEnded("Player", 1));

        var (type, error) = await reported.Task.WaitAsync(Wait);
        Assert.Equal("BattleEnded", type);
        Assert.Same(boom, error);
    }

    [Fact]
    public async Task SendRunFaulted_AFaultedSend_IsReported()
    {
        var hub = new RecordingHubContext { FailSendsWith = new InvalidOperationException() };
        var reported = new TaskCompletionSource<string>();
        var emitter = new SignalRBattleEventEmitter(
            hub,
            () => "conn-1",
            onSendFailed: (type, _) => reported.TrySetResult(type)
        );

        emitter.SendRunFaulted();

        Assert.Equal("RunFaulted", await reported.Task.WaitAsync(Wait));
    }

    [Fact]
    public async Task Emit_ASuccessfulSend_IsNotReported()
    {
        var hub = new RecordingHubContext();
        var reported = false;
        var emitter = new SignalRBattleEventEmitter(
            hub,
            () => "conn-1",
            onSendFailed: (_, _) => reported = true
        );

        emitter.Emit(new BattleEnded("Player", 1));
        await Task.Delay(50);

        Assert.False(reported);
        Assert.Single(hub.EventsFor("conn-1"));
    }
}

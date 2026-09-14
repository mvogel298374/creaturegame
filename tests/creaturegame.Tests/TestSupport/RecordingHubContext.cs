using creaturegame.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Tests.TestSupport;

/// <summary>A recording <c>IHubContext</c>: <c>Client(id)</c> hands back a client that appends every
/// <c>OnBattleEvent</c> to a per-connection list, synchronously — so emit order is observable in program
/// order. Only the member <c>SignalRBattleEventEmitter</c> uses is implemented; everything else throws.
/// Shared by <c>GenerationProfileTests</c> (the presentation-echo timing) and <c>SessionResumeTests</c> (the
/// <c>AttachConnection</c> return-value / unknown-gameId-rejection coverage) — both need a working hub context
/// because <c>AttachConnection</c>'s successful-attach path emits synchronously.</summary>
public sealed class RecordingHubContext : IHubContext<BattleHub, IBattleClient>
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<(string Type, object Payload)>> _events = new();

    public IReadOnlyList<(string Type, object Payload)> EventsFor(string connectionId)
    {
        lock (_lock)
        {
            return _events.TryGetValue(connectionId, out var list) ? list.ToList() : [];
        }
    }

    private void Record(string connectionId, string type, object payload)
    {
        lock (_lock)
        {
            if (!_events.TryGetValue(connectionId, out var list))
                _events[connectionId] = list = [];
            list.Add((type, payload));
        }
    }

    public IHubClients<IBattleClient> Clients => new RecordingClients(this);

    public IGroupManager Groups =>
        throw new NotSupportedException("Groups are not used by the emitter.");

    private sealed class RecordingClients(RecordingHubContext owner) : IHubClients<IBattleClient>
    {
        public IBattleClient Client(string connectionId) =>
            new RecordingClient(owner, connectionId);

        public IBattleClient All => throw new NotSupportedException();

        public IBattleClient AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();

        public IBattleClient Clients(IReadOnlyList<string> connectionIds) =>
            throw new NotSupportedException();

        public IBattleClient Group(string groupName) => throw new NotSupportedException();

        public IBattleClient GroupExcept(
            string groupName,
            IReadOnlyList<string> excludedConnectionIds
        ) => throw new NotSupportedException();

        public IBattleClient Groups(IReadOnlyList<string> groupNames) =>
            throw new NotSupportedException();

        public IBattleClient User(string userId) => throw new NotSupportedException();

        public IBattleClient Users(IReadOnlyList<string> userIds) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingClient(RecordingHubContext owner, string connectionId)
        : IBattleClient
    {
        public Task OnBattleEvent(string eventType, object payload)
        {
            owner.Record(connectionId, eventType, payload);
            return Task.CompletedTask;
        }
    }
}

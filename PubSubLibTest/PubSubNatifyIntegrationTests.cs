using Natify;
using PubSubLib;
using PubSubLib.Messages;

namespace PubSubLibTest;

public class PubSubNatifyIntegrationTests : IDisposable
{
    private const string NatsUrl = "nats://localhost:4222";

    private readonly NatifyServer _natifyServer;
    private readonly NatifyClientFast _natifyClient;
    private readonly IPubSubNatifyClient _client;
    private readonly IPubSub _pubSub;

    public PubSubNatifyIntegrationTests()
    {
        _natifyServer = new NatifyServer(NatsUrl, "Router", "RouterGroup", "PubSubServer");
        _natifyClient = new NatifyClientFast(NatsUrl, "PubSubServer", "ServerGroup", "VN", "Router");
        _client = IPubSubNatifyClient.Create(_natifyServer, "VN");
        _pubSub = IPubSub.Create<Player>(new PubSubConfig { GridSize = 100f });
        _pubSub.AddNatify(_natifyClient);
        Thread.Sleep(2000);
    }

    public void Dispose()
    {
        _client.Dispose();
        _pubSub.Dispose();
    }

    private static Vector2 V(float x, float y) => new Vector2 { x = x, y = y };

    [Fact]
    public void SendAddWatcher_UnitInRange_ReceivesSyncEnter()
    {
        var player = new Player();
        var unit = _pubSub.CreateUnit<Player>(1, "hero", V(50, 50), player);

        var signal = new ManualResetEventSlim();
        SyncEnterMsg? received = null;
        _client.OnSyncEnter(msg =>
        {
            received = msg;
            signal.Set();
        });

        _client.SendAddWatcher(new AddWatcherCmd
        {
            WatcherId = 100, PosX = 0, PosY = 0, Radius = 200f
        });

        Assert.True(signal.Wait(5000), "Timeout waiting for SyncEnter");
        Assert.NotNull(received);
        Assert.Equal(100, received.WatcherId);
        Assert.Single(received.Units);
        Assert.Equal(1, received.Units[0].Id);
    }

    [Fact]
    public void ServerAddUnit_InWatcherRange_ReceivesBatchEnter()
    {
        _pubSub.AddWatcher(300, V(0, 0), 200f);
        Thread.Sleep(300);

        var signal = new ManualResetEventSlim();
        BatchEnterMsg? received = null;
        _client.OnBatchEnter(msg =>
        {
            received = msg;
            signal.Set();
        });

        var player = new Player();
        var unit = _pubSub.CreateUnit<Player>(3, "item", V(50, 50), player);

        Assert.True(signal.Wait(5000), "Timeout waiting for BatchEnter");
        Assert.NotNull(received);
        Assert.Contains(300L, received.WatcherIds);
        Assert.Equal(3, received.UnitId);
    }

    [Fact]
    public void SendPublishEvent_ReceivesUnitEvent()
    {
        _pubSub.AddWatcher(400, V(0, 0), 200f);
        var player = new Player();
        var unit = _pubSub.CreateUnit<Player>(4, "hero", V(50, 50), player);
        Thread.Sleep(300);

        var signal = new ManualResetEventSlim();
        UnitEventMsg? received = null;
        _client.OnUnitEvent(msg =>
        {
            received = msg;
            signal.Set();
        });

        _client.SendPublishEvent(new PublishEventCmd
        {
            UnitId = 4, UnitType = "hero",
            EventName = "attack",
            Data = Google.Protobuf.ByteString.CopyFrom(new byte[] { 99 })
        });

        Assert.True(signal.Wait(5000), "Timeout waiting for UnitEvent");
        Assert.NotNull(received);
        Assert.Contains(400L, received.WatcherIds);
        Assert.Equal(4, received.UnitId);
        Assert.Equal("attack", received.EventName);
    }
}

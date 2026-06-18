using Google.Protobuf;
using Natify;
using PubSubLib;
using PubSubLib.Messages;
using PubSubLib.Router;

namespace PubSubLibTest;

// ===== Proto Roundtrip Tests =====

public class PubSubNatifyProtoTests
{
    [Fact]
    public void Proto_BatchEnterMsg_Roundtrip()
    {
        var msg = new BatchEnterMsg
        {
            UnitId = 42,
            UnitType = "hero",
            PosX = 1.5f,
            PosY = 2.5f,
            Data = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
            Version = 5
        };
        msg.WatcherIds.AddRange(new[] { 1L, 2L });

        var bytes = msg.ToByteArray();
        var parsed = BatchEnterMsg.Parser.ParseFrom(bytes);

        Assert.Equal(42, parsed.UnitId);
        Assert.Equal("hero", parsed.UnitType);
        Assert.Equal(1.5f, parsed.PosX);
        Assert.Equal(2.5f, parsed.PosY);
        Assert.Equal(2, parsed.WatcherIds.Count);
        Assert.Equal(1L, parsed.WatcherIds[0]);
        Assert.Equal(3, parsed.Data.Length);
        Assert.Equal(5, parsed.Version);
    }

    [Fact]
    public void Proto_BatchLeaveMsg_Roundtrip()
    {
        var msg = new BatchLeaveMsg { UnitId = 7, UnitType = "mob" };
        msg.WatcherIds.AddRange(new[] { 3L });

        var bytes = msg.ToByteArray();
        var parsed = BatchLeaveMsg.Parser.ParseFrom(bytes);

        Assert.Equal(7, parsed.UnitId);
        Assert.Equal("mob", parsed.UnitType);
        Assert.Single(parsed.WatcherIds);
        Assert.Equal(3L, parsed.WatcherIds[0]);
    }

    [Fact]
    public void Proto_SyncEnterMsg_Roundtrip()
    {
        var msg = new SyncEnterMsg { WatcherId = 5 };
        msg.Units.Add(new UnitEnterItem
        {
            Id = 10, Type = "npc", PosX = 10f, PosY = 20f,
            Data = ByteString.CopyFrom(new byte[] { 0xFF }),
            Version = 3
        });

        var bytes = msg.ToByteArray();
        var parsed = SyncEnterMsg.Parser.ParseFrom(bytes);

        Assert.Equal(5, parsed.WatcherId);
        Assert.Single(parsed.Units);
        Assert.Equal(10, parsed.Units[0].Id);
        Assert.Equal("npc", parsed.Units[0].Type);
        Assert.Equal(10f, parsed.Units[0].PosX);
        Assert.Equal(1, parsed.Units[0].Data.Length);
        Assert.Equal(3, parsed.Units[0].Version);
    }

    [Fact]
    public void Proto_SyncLeaveMsg_Roundtrip()
    {
        var msg = new SyncLeaveMsg { WatcherId = 8 };
        msg.Keys.Add(new TypeGroup { Type = "hero", UnitIds = { 1, 2, 3 } });
        msg.Keys.Add(new TypeGroup { Type = "mob", UnitIds = { 4 } });

        var bytes = msg.ToByteArray();
        var parsed = SyncLeaveMsg.Parser.ParseFrom(bytes);

        Assert.Equal(8, parsed.WatcherId);
        Assert.Equal(2, parsed.Keys.Count);
        Assert.Equal("hero", parsed.Keys[0].Type);
        Assert.Equal(3, parsed.Keys[0].UnitIds.Count);
        Assert.Equal("mob", parsed.Keys[1].Type);
        Assert.Single(parsed.Keys[1].UnitIds);
    }

    [Fact]
    public void Proto_UnitEventMsg_Roundtrip()
    {
        var msg = new UnitEventMsg
        {
            UnitId = 99,
            UnitType = "boss",
            EventName = "rage",
            Data = ByteString.CopyFrom(new byte[] { 100 })
        };
        msg.WatcherIds.AddRange(new[] { 1L, 2L, 3L });

        var bytes = msg.ToByteArray();
        var parsed = UnitEventMsg.Parser.ParseFrom(bytes);

        Assert.Equal(99, parsed.UnitId);
        Assert.Equal("boss", parsed.UnitType);
        Assert.Equal("rage", parsed.EventName);
        Assert.Equal(3, parsed.WatcherIds.Count);
        Assert.Equal(1, parsed.Data.Length);
    }

    [Fact]
    public void Proto_PubSubEvent_Wrapper_Roundtrip()
    {
        var inner = new BatchEnterMsg
        {
            UnitId = 1, UnitType = "x", PosX = 0, PosY = 0
        };
        inner.WatcherIds.Add(1);
        var evt = new PubSubEvent { BatchEnter = inner };

        var bytes = evt.ToByteArray();
        var parsed = PubSubEvent.Parser.ParseFrom(bytes);

        Assert.Equal(PubSubEvent.EvtOneofCase.BatchEnter, parsed.EvtCase);
        Assert.Equal(1, parsed.BatchEnter.UnitId);
        Assert.Equal("x", parsed.BatchEnter.UnitType);
    }

    [Fact]
    public void Proto_PubSubCommand_Wrapper_Roundtrip()
    {
        var cmd = new PubSubCommand
        {
            AddWatcher = new AddWatcherCmd
            {
                WatcherId = 42, PosX = 5f, PosY = 10f, Radius = 200f
            }
        };

        var bytes = cmd.ToByteArray();
        var parsed = PubSubCommand.Parser.ParseFrom(bytes);

        Assert.Equal(PubSubCommand.CmdOneofCase.AddWatcher, parsed.CmdCase);
        Assert.Equal(42, parsed.AddWatcher.WatcherId);
        Assert.Equal(5f, parsed.AddWatcher.PosX);
        Assert.Equal(10f, parsed.AddWatcher.PosY);
        Assert.Equal(200f, parsed.AddWatcher.Radius);
    }

    [Fact]
    public void Proto_AddWatcherCmd_Roundtrip()
    {
        var cmd = new AddWatcherCmd { WatcherId = 1, PosX = 3f, PosY = 4f, Radius = 100f };
        var parsed = AddWatcherCmd.Parser.ParseFrom(cmd.ToByteArray());
        Assert.Equal(1, parsed.WatcherId);
        Assert.Equal(3f, parsed.PosX);
        Assert.Equal(100f, parsed.Radius);
    }

    [Fact]
    public void Proto_RemoveWatcherCmd_Roundtrip()
    {
        var cmd = new RemoveWatcherCmd { WatcherId = 7 };
        var parsed = RemoveWatcherCmd.Parser.ParseFrom(cmd.ToByteArray());
        Assert.Equal(7, parsed.WatcherId);
    }

    [Fact]
    public void Proto_MoveWatcherCmd_Roundtrip()
    {
        var cmd = new MoveWatcherCmd { WatcherId = 2, PosX = 10f, PosY = 20f, Radius = 50f };
        var parsed = MoveWatcherCmd.Parser.ParseFrom(cmd.ToByteArray());
        Assert.Equal(2, parsed.WatcherId);
        Assert.Equal(50f, parsed.Radius);
    }

    [Fact]
    public void Proto_PingUnitsCmd_Roundtrip()
    {
        var cmd = new PingUnitsCmd { WatcherId = 3 };
        var g1 = new TypeGroup { Type = "hero" };
        g1.UnitIds.AddRange(new long[] { 10, 20 });
        g1.Versions.AddRange(new int[] { 0, 1 });
        cmd.Units.Add(g1);
        var g2 = new TypeGroup { Type = "mob" };
        g2.UnitIds.Add(99);
        g2.Versions.Add(2);
        cmd.Units.Add(g2);

        var parsed = PingUnitsCmd.Parser.ParseFrom(cmd.ToByteArray());

        Assert.Equal(3, parsed.WatcherId);
        Assert.Equal(2, parsed.Units.Count);
        Assert.Equal("hero", parsed.Units[0].Type);
        Assert.Equal(2, parsed.Units[0].UnitIds.Count);
        Assert.Equal(10, parsed.Units[0].UnitIds[0]);
        Assert.Equal(20, parsed.Units[0].UnitIds[1]);
        Assert.Equal(2, parsed.Units[0].Versions.Count);
        Assert.Equal(0, parsed.Units[0].Versions[0]);
        Assert.Equal(1, parsed.Units[0].Versions[1]);
        Assert.Equal("mob", parsed.Units[1].Type);
        Assert.Single(parsed.Units[1].UnitIds);
        Assert.Equal(99, parsed.Units[1].UnitIds[0]);
        Assert.Single(parsed.Units[1].Versions);
        Assert.Equal(2, parsed.Units[1].Versions[0]);
    }

    [Fact]
    public void Proto_PublishEventCmd_Roundtrip()
    {
        var cmd = new PublishEventCmd
        {
            UnitId = 5, UnitType = "hero",
            EventName = "fireball",
            Data = ByteString.CopyFrom(new byte[] { 1, 2 })
        };
        var parsed = PublishEventCmd.Parser.ParseFrom(cmd.ToByteArray());
        Assert.Equal(5, parsed.UnitId);
        Assert.Equal("hero", parsed.UnitType);
        Assert.Equal("fireball", parsed.EventName);
        Assert.Equal(2, parsed.Data.Length);
    }
}

// ===== Real Natify Integration Tests =====

public class PubSubNatifyTests : IDisposable
{
    private const string NatsUrl = "nats://localhost:4222";

    private readonly NatifyServer _natifyServer;
    private readonly NatifyClientFast _natifyClient;
    private readonly IPubSubNatifyClient _client;
    private readonly IPubSub _pubSub;

    public PubSubNatifyTests()
    {
        _natifyServer = new NatifyServer(NatsUrl, "SyncRouter", "SyncGroup", "SyncServer");
        _natifyClient = new NatifyClientFast(NatsUrl, "SyncServer", "ServerGroup", "VN", "SyncRouter");
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

    // ===== Outbound =====

    [Fact]
    public async Task Sync_AddUnit_BroadcastsBatchEnter()
    {
        var signal = new ManualResetEventSlim();
        BatchEnterMsg? received = null;
        _client.OnBatchEnter(msg =>
        {
            received = msg;
            signal.Set();
        });

        _pubSub.AddWatcher(1, V(0, 0), 200);
        var player = new Player();
        await _pubSub.CreateUnitAsync<Player>(10, "hero", V(50, 50), player);

        Assert.True(signal.Wait(5000));
        Assert.NotNull(received);
        Assert.Equal(10, received.UnitId);
        Assert.Equal("hero", received.UnitType);
        Assert.Equal(0, received.Version);
    }

    [Fact]
    public async Task Sync_RemoveUnit_BroadcastsBatchLeave()
    {
        var signal = new ManualResetEventSlim();
        BatchLeaveMsg? received = null;
        _client.OnBatchLeave(msg =>
        {
            received = msg;
            signal.Set();
        });

        _pubSub.AddWatcher(1, V(0, 0), 200);
        var player = new Player();
        var u = await _pubSub.CreateUnitAsync<Player>(20, "mob", V(50, 50), player);

        u.Destroy();
        await _pubSub.FlushAsync();

        Assert.True(signal.Wait(5000));
        Assert.NotNull(received);
        Assert.Equal(20, received.UnitId);
        Assert.Equal("mob", received.UnitType);
    }

    [Fact]
    public async Task Sync_AddWatcher_BroadcastsSyncEnter()
    {
        var player = new Player();
        await _pubSub.CreateUnitAsync<Player>(30, "item", V(50, 50), player);

        var signal = new ManualResetEventSlim();
        SyncEnterMsg? received = null;
        _client.OnSyncEnter(msg =>
        {
            received = msg;
            signal.Set();
        });

        _pubSub.AddWatcher(1, V(0, 0), 200);

        Assert.True(signal.Wait(5000));
        Assert.NotNull(received);
        Assert.Equal(1, received.WatcherId);
        Assert.Single(received.Units);
        Assert.Equal(30, received.Units[0].Id);
        Assert.Equal(0, received.Units[0].Version);
    }

    [Fact]
    public async Task Sync_UnitPositionChange_IntoRange_BroadcastsBatchEnter()
    {
        _pubSub.AddWatcher(1, V(0, 0), 200);
        var player = new Player();
        var u = await _pubSub.CreateUnitAsync<Player>(40, "hero", V(500, 500), player);
        await _pubSub.FlushAsync();

        var signal = new ManualResetEventSlim();
        BatchEnterMsg? received = null;
        _client.OnBatchEnter(msg =>
        {
            received = msg;
            signal.Set();
        });

        u.Position = V(50, 50);
        await _pubSub.FlushAsync();

        Assert.True(signal.Wait(5000));
        Assert.NotNull(received);
        Assert.Equal(40, received.UnitId);
        Assert.Equal(1, received.Version);
    }

    [Fact]
    public async Task Sync_UnitEvent_Broadcasts()
    {
        var signal = new ManualResetEventSlim();
        UnitEventMsg? received = null;
        _client.OnUnitEvent(msg =>
        {
            received = msg;
            signal.Set();
        });

        _pubSub.AddWatcher(1, V(0, 0), 200);
        var player = new Player();
        var u = await _pubSub.CreateUnitAsync<Player>(50, "hero", V(50, 50), player);
        await _pubSub.FlushAsync();

        u.PublishEvent("attack", new byte[] { 99 });
        await _pubSub.FlushAsync();

        Assert.True(signal.Wait(5000));
        Assert.NotNull(received);
        Assert.Equal(50, received.UnitId);
        Assert.Equal("attack", received.EventName);
    }

    [Fact]
    public async Task Sync_UnitData_IncludedInBatchEnter()
    {
        var signal = new ManualResetEventSlim();
        BatchEnterMsg? received = null;
        _client.OnBatchEnter(msg =>
        {
            received = msg;
            signal.Set();
        });

        _pubSub.AddWatcher(1, V(0, 0), 200);
        var player = new Player();
        await _pubSub.CreateUnitAsync<Player>(60, "hero", V(50, 50), player, new byte[] { 7, 8, 9 });

        Assert.True(signal.Wait(5000));
        Assert.NotNull(received);
        Assert.Equal(3, received.Data.Length);
        Assert.Equal(1, received.Version);
    }

    [Fact]
    public async Task Sync_MultipleWatchers_SingleTopicBatchEnter()
    {
        var signal = new ManualResetEventSlim();
        BatchEnterMsg? received = null;
        _client.OnBatchEnter(msg =>
        {
            received = msg;
            signal.Set();
        });

        _pubSub.AddWatcher(1, V(0, 0), 200);
        _pubSub.AddWatcher(2, V(0, 0), 200);
        await _pubSub.FlushAsync();

        var player = new Player();
        await _pubSub.CreateUnitAsync<Player>(70, "hero", V(50, 50), player);

        Assert.True(signal.Wait(5000));
        Assert.NotNull(received);
        Assert.Equal(2, received.WatcherIds.Count);
        Assert.Contains(1L, received.WatcherIds);
        Assert.Contains(2L, received.WatcherIds);
    }

    // ===== Inbound =====

    [Fact]
    public async Task Sync_Inbound_AddWatcher_Called()
    {
        var signal = new ManualResetEventSlim();
        Action<(long, List<IUnit<Player>>)> cb = _ => signal.Set();
        _pubSub.OnUnitEnter(cb);

        var player = new Player();
        await _pubSub.CreateUnitAsync<Player>(80, "mob", V(50, 50), player);

        _client.SendAddWatcher(new AddWatcherCmd
        {
            WatcherId = 99, PosX = 0, PosY = 0, Radius = 200f
        });

        Assert.True(signal.Wait(5000));
    }

    [Fact]
    public async Task Sync_PingUnits_VersionMismatch_SyncEnter()
    {
        var player = new Player();
        var u = await _pubSub.CreateUnitAsync<Player>(8, "mob", V(50, 50), player);
        u.Position = V(60, 60);

        var enterSignal = new ManualResetEventSlim();
        _client.OnSyncEnter(_ => enterSignal.Set());
        _client.SendAddWatcher(new AddWatcherCmd
        {
            WatcherId = 1, PosX = 0, PosY = 0, Radius = 200f
        });
        Assert.True(enterSignal.Wait(5000));

        var signal = new ManualResetEventSlim();
        SyncEnterMsg? received = null;
        _client.OnSyncEnter(msg =>
        {
            received = msg;
            signal.Set();
        });

        var cmd = new PingUnitsCmd { WatcherId = 1 };
        var g = new TypeGroup { Type = "mob" };
        g.UnitIds.Add(8);
        g.Versions.Add(0);
        cmd.Units.Add(g);
        _client.SendPingUnits(cmd);

        Assert.True(signal.Wait(5000));
        Assert.NotNull(received);
        Assert.Equal(1, received.WatcherId);
        Assert.Single(received.Units);
        Assert.Equal(8, received.Units[0].Id);
        Assert.Equal(1, received.Units[0].Version);
    }
}

using Google.Protobuf;
using Natify;
using PubSubLib;
using PubSubLib.Messages;

namespace PubSubLibTest;

public class PubSubNatifyTests
{
    // ===== Proto Roundtrip =====

    [Fact]
    public void Proto_BatchEnterMsg_Roundtrip()
    {
        var msg = new BatchEnterMsg
        {
            UnitId = 42,
            UnitType = "hero",
            PosX = 1.5f,
            PosY = 2.5f,
            Data = ByteString.CopyFrom(new byte[] { 1, 2, 3 })
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
            Data = ByteString.CopyFrom(new byte[] { 0xFF })
        });

        var bytes = msg.ToByteArray();
        var parsed = SyncEnterMsg.Parser.ParseFrom(bytes);

        Assert.Equal(5, parsed.WatcherId);
        Assert.Single(parsed.Units);
        Assert.Equal(10, parsed.Units[0].Id);
        Assert.Equal("npc", parsed.Units[0].Type);
        Assert.Equal(10f, parsed.Units[0].PosX);
        Assert.Equal(1, parsed.Units[0].Data.Length);
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

    // ===== Command roundtrip =====

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
        cmd.Units.Add(new TypeGroup { Type = "hero", UnitIds = { 10, 20 } });
        cmd.Units.Add(new TypeGroup { Type = "mob", UnitIds = { 99 } });

        var parsed = PingUnitsCmd.Parser.ParseFrom(cmd.ToByteArray());

        Assert.Equal(3, parsed.WatcherId);
        Assert.Equal(2, parsed.Units.Count);
        Assert.Equal("hero", parsed.Units[0].Type);
        Assert.Equal(2, parsed.Units[0].UnitIds.Count);
        Assert.Equal(10, parsed.Units[0].UnitIds[0]);
        Assert.Equal(20, parsed.Units[0].UnitIds[1]);
        Assert.Equal("mob", parsed.Units[1].Type);
        Assert.Single(parsed.Units[1].UnitIds);
        Assert.Equal(99, parsed.Units[1].UnitIds[0]);
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

    // ===== PubSubNatifySync Outbound =====

    [Fact]
    public void Sync_AddUnit_BroadcastsBatchEnter()
    {
        var mock = new MockNatifyAdapter();
        var pubSub = CreatePubSubWithNatify(mock);

        pubSub.AddWatcher(1, V(0, 0), 200);
        var u = IUnit<Player>.Create(10, "hero", V(50, 50), new Player());
        pubSub.AddUnit(u);

        Thread.Sleep(200);

        Assert.True(mock.PublishedMessages.Count > 0);
        var (topic, obj) = mock.PublishedMessages[0];
        Assert.Contains("PubSub.Evt", topic);

        var evt = (PubSubEvent)obj;
        Assert.Equal(PubSubEvent.EvtOneofCase.BatchEnter, evt.EvtCase);
        Assert.Equal(10, evt.BatchEnter.UnitId);
        Assert.Equal("hero", evt.BatchEnter.UnitType);
    }

    [Fact]
    public void Sync_RemoveUnit_BroadcastsBatchLeave()
    {
        var mock = new MockNatifyAdapter();
        var pubSub = CreatePubSubWithNatify(mock);

        pubSub.AddWatcher(1, V(0, 0), 200);
        var u = IUnit<Player>.Create(20, "mob", V(50, 50), new Player());
        pubSub.AddUnit(u);
        Thread.Sleep(100);
        mock.PublishedMessages.Clear();

        pubSub.RemoveUnit(u);
        Thread.Sleep(200);

        var (topic, obj) = mock.PublishedMessages[0];
        Assert.Contains("PubSub.Evt", topic);

        var evt = (PubSubEvent)obj;
        Assert.Equal(PubSubEvent.EvtOneofCase.BatchLeave, evt.EvtCase);
        Assert.Equal(20, evt.BatchLeave.UnitId);
    }

    [Fact]
    public void Sync_AddWatcher_BroadcastsSyncEnter()
    {
        var mock = new MockNatifyAdapter();
        var pubSub = CreatePubSubWithNatify(mock);

        var u = IUnit<Player>.Create(30, "item", V(50, 50), new Player());
        pubSub.AddUnit(u);
        Thread.Sleep(100);
        mock.PublishedMessages.Clear();

        pubSub.AddWatcher(1, V(0, 0), 200);
        Thread.Sleep(200);

        Assert.True(mock.PublishedMessages.Count > 0);
        var (topic, obj) = mock.PublishedMessages[0];
        Assert.Contains("PubSub.Evt", topic);

        var evt = (PubSubEvent)obj;
        Assert.Equal(PubSubEvent.EvtOneofCase.SyncEnter, evt.EvtCase);
        Assert.Equal(1, evt.SyncEnter.WatcherId);
        Assert.Single(evt.SyncEnter.Units);
        Assert.Equal(30, evt.SyncEnter.Units[0].Id);
    }

    [Fact]
    public void Sync_UnitPositionChange_IntoRange_BroadcastsBatchEnter()
    {
        var mock = new MockNatifyAdapter();
        var pubSub = CreatePubSubWithNatify(mock);

        pubSub.AddWatcher(1, V(0, 0), 200);
        var u = IUnit<Player>.Create(40, "hero", V(500, 500), new Player());
        pubSub.AddUnit(u);
        Thread.Sleep(100);
        mock.PublishedMessages.Clear();

        u.Position = V(50, 50);
        Thread.Sleep(200);

        var (topic, obj) = mock.PublishedMessages[0];
        Assert.Contains("PubSub.Evt", topic);

        var evt = (PubSubEvent)obj;
        Assert.Equal(PubSubEvent.EvtOneofCase.BatchEnter, evt.EvtCase);
        Assert.Equal(40, evt.BatchEnter.UnitId);
    }

    [Fact]
    public void Sync_UnitEvent_Broadcasts()
    {
        var mock = new MockNatifyAdapter();
        var pubSub = CreatePubSubWithNatify(mock);

        pubSub.AddWatcher(1, V(0, 0), 200);
        var u = IUnit<Player>.Create(50, "hero", V(50, 50), new Player());
        pubSub.AddUnit(u);
        Thread.Sleep(100);
        mock.PublishedMessages.Clear();

        u.PublishEvent("attack", new byte[] { 99 });
        Thread.Sleep(200);

        var (topic, obj) = mock.PublishedMessages[0];
        Assert.Contains("PubSub.Evt", topic);

        var evt = (PubSubEvent)obj;
        Assert.Equal(PubSubEvent.EvtOneofCase.UnitEvent, evt.EvtCase);
        Assert.Equal(50, evt.UnitEvent.UnitId);
        Assert.Equal("attack", evt.UnitEvent.EventName);
    }

    [Fact]
    public void Sync_UnitData_IncludedInBatchEnter()
    {
        var mock = new MockNatifyAdapter();
        var pubSub = CreatePubSubWithNatify(mock);

        pubSub.AddWatcher(1, V(0, 0), 200);
        var u = IUnit<Player>.Create(60, "hero", V(50, 50), new Player());
        u.Data = new byte[] { 7, 8, 9 };
        pubSub.AddUnit(u);
        Thread.Sleep(200);

        var (_, obj) = mock.PublishedMessages[0];
        var evt = (PubSubEvent)obj;
        Assert.Equal(3, evt.BatchEnter.Data.Length);
    }

    [Fact]
    public void Sync_MultipleWatchers_SingleTopicBatchEnter()
    {
        var mock = new MockNatifyAdapter();
        var pubSub = CreatePubSubWithNatify(mock);

        pubSub.AddWatcher(1, V(0, 0), 200);
        pubSub.AddWatcher(2, V(0, 0), 200);
        Thread.Sleep(100);
        mock.PublishedMessages.Clear();

        var u = IUnit<Player>.Create(70, "hero", V(50, 50), new Player());
        pubSub.AddUnit(u);
        Thread.Sleep(200);

        Assert.Single(mock.PublishedMessages);
        var (topic, obj) = mock.PublishedMessages[0];
        Assert.Equal("PubSub.Evt", topic);

        var evt = (PubSubEvent)obj;
        Assert.Equal(PubSubEvent.EvtOneofCase.BatchEnter, evt.EvtCase);
        Assert.Equal(new[] { 1L, 2L }, evt.BatchEnter.WatcherIds);
    }

    // ===== PubSubNatifySync Inbound =====

    [Fact]
    public void Sync_Inbound_AddWatcher_Called()
    {
        var mock = new MockNatifyAdapter();
        var pubSub = CreatePubSubWithNatify(mock);

        var signal = new ManualResetEventSlim();
        Action<(long, List<IUnit<Player>>)> cb = _ => signal.Set();
        pubSub.OnUnitEnter(cb);

        var u = IUnit<Player>.Create(80, "mob", V(50, 50), new Player());
        pubSub.AddUnit(u);

        var cmd = new PubSubCommand
        {
            AddWatcher = new AddWatcherCmd
            {
                WatcherId = 99, PosX = 0, PosY = 0, Radius = 200f
            }
        };
        mock.SimulateReceived<PubSubCommand>("PubSub.Cmd", cmd.ToByteArray());
        Thread.Sleep(200);

        Assert.True(signal.Wait(5000));
    }

    // ===== Helpers =====

    private static Vector2 V(float x, float y) => new Vector2 { x = x, y = y };

    private static IPubSub CreatePubSubWithNatify(MockNatifyAdapter mock)
    {
        var pubSub = IPubSub.Create<Player>(new PubSubConfig { GridSize = 100f });
        var internalPubSub = (PubSubLib.PubSub<Player>)pubSub;
        internalPubSub.AddNatifyInternal(mock);
        return pubSub;
    }
}

// ===== MockNatifyAdapter =====

internal class MockNatifyAdapter : INatifyAdapter
{
    private readonly Dictionary<string, Delegate> _handlers = new();
    public List<(string Topic, object Message)> PublishedMessages { get; } = new();

    public void Publish<T>(string topic, T msg) where T : IMessage
    {
        PublishedMessages.Add((topic, msg));
    }

    public void Subscribe<T>(string topic, Action<Data<T>> handler) where T : IMessage, new()
    {
        _handlers[topic] = handler;
    }

    public void SimulateReceived<T>(string topic, byte[] data) where T : IMessage, new()
    {
        if (_handlers.TryGetValue(topic, out var handler))
        {
            var action = (Action<Data<T>>)handler;
            var msg = new T();
            msg.MergeFrom(data);
            action(new Data<T>(msg, "", "", ""));
        }
    }

    public void Dispose() { }
}

using Google.Protobuf;
using PubSubLib;
using PubSubLib.Client;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLibTest;

[UnitMirrorServer(typeof(RemoveWatcherCmd), UnitType = "remove_watcher")]
public partial class RemoveWatcherUnitServer
{
}

[UnitMirrorClient(typeof(RemoveWatcherCmd), UnitType = "remove_watcher", Target = typeof(RemoveWatcherClientTarget))]
public partial class RemoveWatcherTestClient
{
}

public class RegionTestAll
{
    [Fact]
    public void Generated_UnitMirrorServer_Implements_IRegionUnit()
    {
        var unit = new RemoveWatcherUnitServer();
        Assert.IsAssignableFrom<PubSubLib.IRegionUnit<RemoveWatcherCmd>>(unit);
        Assert.IsAssignableFrom<IRegionUnitInternal>(unit);
    }

    [Fact]
    public void Generated_UnitMirrorServer_Has_UnitType()
    {
        var unit = new RemoveWatcherUnitServer();
        Assert.Equal("remove_watcher", unit.UnitType);
        Assert.Equal("remove_watcher", ((IRegionUnitInternal)unit).GetUnitType());
    }

    [Fact]
    public void Generated_UnitMirrorServer_UnitType_Static()
    {
        Assert.Equal("remove_watcher", RemoveWatcherUnitServer._unitType);
    }

    [Fact]
    public void Generated_UnitMirrorClient_Implements_IRegionUnit_And_IRegionClientUnitInternal()
    {
        var unit = new RemoveWatcherTestClient();
        Assert.IsAssignableFrom<PubSubLib.Client.IRegionUnit<RemoveWatcherClientTarget>>(unit);
        Assert.IsAssignableFrom<IRegionClientUnitInternal>(unit);
    }

    [Fact]
    public void Generated_UnitMirrorClient_GetTarget_ReturnsTypedTarget()
    {
        var unit = new RemoveWatcherTestClient();
        var target = new RemoveWatcherClientTarget();
        ((IRegionClientUnitInternal)unit).SetTarget(target);
        Assert.Same(target, unit.GetTarget());
    }

    [Fact]
    public void Generated_UnitMirrorClient_Has_Id_And_UnitType()
    {
        var unit = new RemoveWatcherTestClient();
        Assert.Equal("remove_watcher", unit.UnitType);
        Assert.Equal(0, unit.Id);
    }

    [Fact]
    public void Generated_UnitMirrorServer_MirrorFields_ReadWrite()
    {
        var unit = new RemoveWatcherUnitServer();
        unit.WatcherId = 123;
        Assert.Equal(123, unit.WatcherId);
    }

    [Fact]
    public void Generated_UnitMirrorServer_Commit_SerializesAndFires()
    {
        using var suppress = MirrorProtoBus.SuppressBackground();

        var unit = new RemoveWatcherUnitServer();

        var iu = new FakeServerUnit(1, "remove_watcher");
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.WatcherId = 77;
        unit.Commit("test_commit");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedEventName);
        Assert.Equal("commit", iu.LastPublishedEventName);

        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        Assert.Equal("test_commit", commit.Commit);
        Assert.True(commit.MirrorData.Length > 0);

        var cmd = RemoveWatcherCmd.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(77, cmd.WatcherId);
    }

    [Fact]
    public void Generated_UnitMirrorServer_SendMessage_Fires()
    {
        using var suppress = MirrorProtoBus.SuppressBackground();

        var unit = new RemoveWatcherUnitServer();

        var iu = new FakeServerUnit(1, "remove_watcher");
        ((IRegionUnitInternal)unit).SetUnit(iu);

        var msg = new RemoveWatcherCmd { WatcherId = 42 };
        unit.SendMessage("test_subject", msg, true);
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedEventName);
        Assert.Equal("message", iu.LastPublishedEventName);

        var rmsg = RegionMessage.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        Assert.Equal("test_subject", rmsg.Subject);
        Assert.True(rmsg.Data.Length > 0);
    }

    [Fact]
    public void Generated_UnitMirrorServer_SetPosition_DelegatesToUnit()
    {
        var unit = new RemoveWatcherUnitServer();

        var iu = new FakeServerUnit(1, "remove_watcher");
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.SetPosition(10, 20);
        Assert.Equal(10, iu.Position.x);
        Assert.Equal(20, iu.Position.y);

        unit.SetPosition(new Vector2 { x = 5, y = 15 });
        Assert.Equal(5, iu.Position.x);
        Assert.Equal(15, iu.Position.y);
    }

    [Fact]
    public void Generated_UnitMirrorServer_Destroy_CallsUnitDestroy()
    {
        var unit = new RemoveWatcherUnitServer();

        var iu = new FakeServerUnit(1, "remove_watcher");
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Destroy();

        Assert.True(iu.IsDestroyed);
    }

    [Fact]
    public void Generated_UnitMirrorServer_Get_ParsesFromData()
    {
        var unit = new RemoveWatcherUnitServer();

        var cmd = new RemoveWatcherCmd { WatcherId = 99 };
        var bytes = cmd.ToByteArray();

        var iu = new FakeServerUnit(1, "remove_watcher") { Data = bytes };
        ((IRegionUnitInternal)unit).SetUnit(iu);

        var result = unit.Get();
        Assert.Equal(99, result.WatcherId);
    }

    [Fact]
    public void Generated_UnitMirrorServer_Get_EmptyData_ReturnsNewProto()
    {
        var unit = new RemoveWatcherUnitServer();

        var iu = new FakeServerUnit(1, "remove_watcher");
        ((IRegionUnitInternal)unit).SetUnit(iu);

        var result = unit.Get();
        Assert.NotNull(result);
        Assert.Equal(0, result.WatcherId);
    }

    [Fact]
    public void Generated_UnitMirrorServer_Commit_SetsDataOnUnit()
    {
        using var suppress = MirrorProtoBus.SuppressBackground();

        var unit = new RemoveWatcherUnitServer();

        var iu = new FakeServerUnit(1, "remove_watcher");
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.WatcherId = 1;
        unit.Commit("update");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.Data);
        Assert.True(iu.Data!.Length > 0);
    }

    [Fact]
    public void Generated_UnitMirrorClient_ApplyUpdate_SyncsMirrorFields()
    {
        var unit = new RemoveWatcherTestClient();

        var cmd = new RemoveWatcherCmd { WatcherId = 55 };
        var bytes = cmd.ToByteArray();

        ((IRegionClientUnitInternal)unit).ApplyUpdate(bytes, "commit_info");

        Assert.Equal(55, unit.WatcherId);
    }

    [Fact]
    public void Generated_UnitMirrorClient_ApplyUpdate_FirstTimeCallsOnStart()
    {
        var unit = new RemoveWatcherTestClient();

        var cmd = new RemoveWatcherCmd { WatcherId = 1 };
        var bytes = cmd.ToByteArray();

        ((IRegionClientUnitInternal)unit).ApplyUpdate(bytes, "");
        Assert.True(unit.IsInitialized);
    }

    [Fact]
    public void Generated_UnitMirrorClient_Init_SetsIdAndPosition()
    {
        var unit = new RemoveWatcherTestClient();

        ((IRegionClientUnitInternal)unit).Init(42, new Vector2 { x = 10, y = 20 });

        Assert.Equal(42, unit.Id);
    }

    [Fact]
    public void Generated_UnitMirrorClient_GetUnitType_Static()
    {
        Assert.Equal("remove_watcher", RemoveWatcherTestClient._unitType);
    }

    [Fact]
    public void Generate_UnitMirrorServer_Id_DelegatesToUnit()
    {
        var unit = new RemoveWatcherUnitServer();

        var iu = new FakeServerUnit(42, "remove_watcher");
        ((IRegionUnitInternal)unit).SetUnit(iu);

        Assert.Equal(42, unit.Id);
    }

    [Fact]
    public void Generate_UnitMirrorServer_Position_DelegatesToUnit()
    {
        var unit = new RemoveWatcherUnitServer();

        var iu = new FakeServerUnit(1, "remove_watcher") { Position = new Vector2 { x = 3, y = 7 } };
        ((IRegionUnitInternal)unit).SetUnit(iu);

        Assert.Equal(3, unit.Position.x);
        Assert.Equal(7, unit.Position.y);

        unit.Position = new Vector2 { x = 9, y = 1 };
        Assert.Equal(9, iu.Position.x);
        Assert.Equal(1, iu.Position.y);
    }

    private sealed class FakeServerUnit : PubSubLib.IUnit
    {
        public long Id { get; }
        public string Type { get; }
        public Vector2 Position { get; set; }
        public bool IsAlive => true;
        public object? Target => null;
        public int Version { get; set; }
        public byte[]? Data { get; set; }

        public string? LastPublishedEventName;
        public object? LastPublishedData;
        public bool IsDestroyed;

        public FakeServerUnit(long id, string type)
        {
            Id = id;
            Type = type;
        }

        public void PublishEvent(string eventName, object? data, bool reliable = true)
        {
            LastPublishedEventName = eventName;
            LastPublishedData = data;
        }

        public void Destroy()
        {
            IsDestroyed = true;
        }
    }
}

public sealed class RemoveWatcherClientTarget : PubSubLib.Client.IAlive
{
    public bool IsAlive { get; set; } = true;
}

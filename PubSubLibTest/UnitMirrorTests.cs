using Google.Protobuf;
using PubSubLib;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLibTest;

[UnitMirrorServer(typeof(RemoveWatcherCmd), UnitType = "remove_watcher", Target = typeof(UMRemoveWatcherTarget))]
public partial class UMRemoveWatcher
{
}

[UnitMirrorServer(typeof(BatchEnterMsg), UnitType = "batch_enter", Target = typeof(UMBatchEnterTarget))]
public partial class UMBatchEnter
{
}

[UnitMirrorServer(typeof(StructTestMsg), UnitType = "struct_test", Target = typeof(UMStructTestTarget))]
public partial class UMStructTest
{
}

[UnitMirrorServer(typeof(Vector3TestMsg), UnitType = "vector3_test", Target = typeof(UMVector3TestTarget))]
public partial class UMVector3Test
{
}

[UnitMirrorServer(typeof(Vector3StructTestMsg), UnitType = "v3struct_test", Target = typeof(UMVector3StructTarget))]
public partial class UMVector3Struct
{
}

[UnitMirrorServer(typeof(Vector3SingleStructTestMsg), UnitType = "v3single_test", Target = typeof(UMVector3SingleStructTarget))]
public partial class UMVector3SingleStruct
{
}

[UnitMirrorServer(typeof(PrimitiveArrayStructTestMsg), UnitType = "primarr_test", Target = typeof(UMPrimitiveArrayTarget))]
public partial class UMPrimitiveArray
{
}

[UnitMirrorServer(typeof(ClassTestMsg), UnitType = "class_test", Target = typeof(UMClassTestTarget))]
public partial class UMClassTest
{
}

[Collection("MirrorProtoBus")]
public class UnitMirrorTests
{
    private static FakeUnit NewUnit(long id = 1, string type = "test")
    {
        var u = new FakeUnit(id, type);
        return u;
    }

    [Fact]
    public void SimpleField_Commit_SendsData()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMRemoveWatcher();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.WatcherId = 42;
        unit.Commit("test");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var cmd = RemoveWatcherCmd.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(42L, cmd.WatcherId);
    }

    [Fact]
    public void RepeatedField_Commit_SendsData()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMBatchEnter();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.WatcherIds.Add(1);
        unit.WatcherIds.Add(2);
        unit.WatcherIds.Add(3);
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = BatchEnterMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1, 2, 3 }, msg.WatcherIds);
    }

    [Fact]
    public void RepeatedField_DirtyFlag_ResetsAfterCommit()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMBatchEnter();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        Assert.False(unit.WatcherIds.IsDirty);
        unit.WatcherIds.Add(1);
        Assert.True(unit.WatcherIds.IsDirty);

        unit.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(unit.WatcherIds.IsDirty);
    }

    [Fact]
    public void StructGroup_Commit_SendsData()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMStructTest();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMStructTest.Player(1, "ninh"));
        unit.Players.Add(new UMStructTest.Player(2, "yen"));
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = StructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1, 2 }, msg.StructXPlayerXId);
        Assert.Equal(new string[] { "ninh", "yen" }, msg.StructXPlayerXName);
    }

    [Fact]
    public void StructGroup_DirtyFlag_ResetsAfterCommit()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMStructTest();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        Assert.False(unit.Players.IsDirty);
        unit.Players.Add(new UMStructTest.Player(1, "ninh"));
        Assert.True(unit.Players.IsDirty);

        unit.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(unit.Players.IsDirty);
    }

    [Fact]
    public void Vector3List_Commit_SendsData()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMVector3Test();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Waypoints.Add(new Vector3 { x = 1, y = 2, z = 3 });
        unit.Waypoints.Add(new Vector3 { x = 4, y = 5, z = 6 });
        unit.Commit("list");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = Vector3TestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, msg.WaypointsVector3S);
    }

    [Fact]
    public void Vector3List_DirtyFlag_ResetsAfterCommit()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMVector3Test();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        Assert.False(unit.Waypoints.IsDirty);
        unit.Waypoints.Add(new Vector3 { x = 1, y = 2, z = 3 });
        Assert.True(unit.Waypoints.IsDirty);

        unit.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(unit.Waypoints.IsDirty);
    }

    [Fact]
    public void Vector3Struct_Commit_SendsValueAndCount()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMVector3Struct();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMVector3Struct.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 }, new Vector3 { x = 4, y = 5, z = 6 } }));
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = Vector3StructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1 }, msg.StructXPlayerXId);
        Assert.Equal(new string[] { "ninh" }, msg.StructXPlayerXName);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, msg.StructXPlayerXPositionVector3SValue);
        Assert.Equal(new int[] { 2 }, msg.StructXPlayerXPositionVector3SCount);
    }

    [Fact]
    public void Vector3Struct_MultiplePlayers_CommitsCorrectFloats()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMVector3Struct();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMVector3Struct.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        unit.Players.Add(new UMVector3Struct.Player(2, "yen",
            new Vector3[] { new Vector3 { x = 4, y = 5, z = 6 }, new Vector3 { x = 7, y = 8, z = 9 } }));
        unit.Commit("multi");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = Vector3StructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1, 2 }, msg.StructXPlayerXId);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, msg.StructXPlayerXPositionVector3SValue);
        Assert.Equal(new int[] { 1, 2 }, msg.StructXPlayerXPositionVector3SCount);
    }

    [Fact]
    public void Vector3Struct_EmptyArray_CommitsCountZero()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMVector3Struct();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMVector3Struct.Player(1, "ninh", new Vector3[0]));
        unit.Commit("empty");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = Vector3StructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new int[] { 0 }, msg.StructXPlayerXPositionVector3SCount);
        Assert.Empty(msg.StructXPlayerXPositionVector3SValue);
    }

    [Fact]
    public void Vector3SingleStruct_Commit_SendsThreeFloats()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMVector3SingleStruct();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMVector3SingleStruct.Player(1, "ninh",
            new Vector3 { x = 1, y = 2, z = 3 }));
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = Vector3SingleStructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1 }, msg.StructXPlayerXId);
        Assert.Equal(new float[] { 1, 2, 3 }, msg.StructXPlayerXPositionVector3);
    }

    [Fact]
    public void Vector3SingleStruct_MultiplePlayers_CommitsCorrectFloats()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMVector3SingleStruct();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMVector3SingleStruct.Player(1, "ninh",
            new Vector3 { x = 1, y = 2, z = 3 }));
        unit.Players.Add(new UMVector3SingleStruct.Player(2, "yen",
            new Vector3 { x = 4, y = 5, z = 6 }));
        unit.Commit("multi");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = Vector3SingleStructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1, 2 }, msg.StructXPlayerXId);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, msg.StructXPlayerXPositionVector3);
    }

    [Fact]
    public void PrimitiveArray_Commit_SendsData()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMPrimitiveArray();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMPrimitiveArray.Player(1, "ninh", new long[] { 10, 20 }, new float[] { 0.5f, 1.0f }));
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = PrimitiveArrayStructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1 }, msg.StructXPlayerXId);
        Assert.Equal(new long[] { 10, 20 }, msg.StructXPlayerXBeanArrayValue);
        Assert.Equal(new int[] { 2 }, msg.StructXPlayerXBeanArrayCount);
        Assert.Equal(new float[] { 0.5f, 1.0f }, msg.StructXPlayerXScoresArrayValue);
        Assert.Equal(new int[] { 2 }, msg.StructXPlayerXScoresArrayCount);
    }

    [Fact]
    public void PrimitiveArray_MultipleItems_Commit()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMPrimitiveArray();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMPrimitiveArray.Player(1, "a", new long[] { 1 }, new float[] { 0.1f }));
        unit.Players.Add(new UMPrimitiveArray.Player(2, "b", new long[] { 2, 3 }, new float[] { 0.2f, 0.3f }));
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = PrimitiveArrayStructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1, 2 }, msg.StructXPlayerXId);
        Assert.Equal(new long[] { 1, 2, 3 }, msg.StructXPlayerXBeanArrayValue);
        Assert.Equal(new int[] { 1, 2 }, msg.StructXPlayerXBeanArrayCount);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, msg.StructXPlayerXScoresArrayValue);
        Assert.Equal(new int[] { 1, 2 }, msg.StructXPlayerXScoresArrayCount);
    }

    [Fact]
    public void PrimitiveArray_EmptyArray_Commit_CountZero()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMPrimitiveArray();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMPrimitiveArray.Player(1, "x", System.Array.Empty<long>(), new float[] { 0.5f }));
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = PrimitiveArrayStructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Empty(msg.StructXPlayerXBeanArrayValue);
        Assert.Equal(new int[] { 0 }, msg.StructXPlayerXBeanArrayCount);
    }

    [Fact]
    public void PrimitiveArray_ChangeElement_Commit_SendsUpdatedValue()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMPrimitiveArray();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMPrimitiveArray.Player(1, "ninh", new long[] { 10, 20 }, new float[] { 0.5f }));
        unit.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(unit.Players.IsDirty);

        unit.Players[0].Bean[0] = 99;
        Assert.True(unit.Players.IsDirty);

        unit.Commit("update");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = PrimitiveArrayStructTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 99, 20 }, msg.StructXPlayerXBeanArrayValue);
    }

    [Fact]
    public void ClassGroup_Commit_SendsData()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMClassTest();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMClassTest.Player(1, "ninh", new Vector3[0]));
        unit.Players.Add(new UMClassTest.Player(2, "yen", new Vector3[0]));
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = ClassTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1, 2 }, msg.ClassXPlayerXId);
        Assert.Equal(new string[] { "ninh", "yen" }, msg.ClassXPlayerXName);
    }

    [Fact]
    public void ClassGroup_ChangeProperty_Commit_SendsUpdatedValue()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMClassTest();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMClassTest.Player(1, "ninh", new Vector3[0]));
        unit.Commit("init");
        MirrorProtoBus.Flush();

        unit.Players[0].Id = 99;
        unit.Players[0].Name = "updated";
        Assert.True(unit.Players.IsDirty);

        unit.Commit("update");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = ClassTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 99 }, msg.ClassXPlayerXId);
        Assert.Equal(new string[] { "updated" }, msg.ClassXPlayerXName);
    }

    [Fact]
    public void ClassGroup_Vector3Array_Commit_SendsData()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMClassTest();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMClassTest.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        unit.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = ClassTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new long[] { 1 }, msg.ClassXPlayerXId);
        Assert.Equal(new float[] { 1, 2, 3 }, msg.ClassXPlayerXPositionVector3SValue);
        Assert.Equal(new int[] { 1 }, msg.ClassXPlayerXPositionVector3SCount);
    }

    [Fact]
    public void ClassGroup_Vector3Array_ChangeElement_Commit_SendsUpdatedValue()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMClassTest();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        unit.Players.Add(new UMClassTest.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        unit.Commit("init");
        MirrorProtoBus.Flush();

        unit.Players[0].Position[0] = new Vector3 { x = 9, y = 8, z = 7 };
        Assert.True(unit.Players.IsDirty);

        unit.Commit("update");
        MirrorProtoBus.Flush();

        Assert.NotNull(iu.LastPublishedData);
        var commit = RegionCommit.Parser.ParseFrom((byte[])iu.LastPublishedData!);
        var msg = ClassTestMsg.Parser.ParseFrom(commit.MirrorData);
        Assert.Equal(new float[] { 9, 8, 7 }, msg.ClassXPlayerXPositionVector3SValue);
    }

    [Fact]
    public void ClassGroup_Remove_DetachesDirtyMarker()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var unit = new UMClassTest();
        var iu = NewUnit();
        ((IRegionUnitInternal)unit).SetUnit(iu);

        var player = new UMClassTest.Player(1, "ninh", new Vector3[0]);
        unit.Players.Add(player);
        unit.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(unit.Players.IsDirty);

        unit.Players.Remove(player);
        unit.Commit("remove");
        MirrorProtoBus.Flush();
        Assert.False(unit.Players.IsDirty);

        player.Name = "after_remove";
        Assert.False(unit.Players.IsDirty);
    }

    private sealed class FakeUnit : PubSubLib.IUnit
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

        public FakeUnit(long id, string type)
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

public sealed class UMRemoveWatcherTarget : PubSubLib.IAlive
{
    public bool IsAlive { get; set; } = true;
}

public sealed class UMBatchEnterTarget : PubSubLib.IAlive
{
    public bool IsAlive { get; set; } = true;
}

public sealed class UMStructTestTarget : PubSubLib.IAlive
{
    public bool IsAlive { get; set; } = true;
}

public sealed class UMVector3TestTarget : PubSubLib.IAlive
{
    public bool IsAlive { get; set; } = true;
}

public sealed class UMVector3StructTarget : PubSubLib.IAlive
{
    public bool IsAlive { get; set; } = true;
}

public sealed class UMVector3SingleStructTarget : PubSubLib.IAlive
{
    public bool IsAlive { get; set; } = true;
}

public sealed class UMPrimitiveArrayTarget : PubSubLib.IAlive
{
    public bool IsAlive { get; set; } = true;
}

public sealed class UMClassTestTarget : PubSubLib.IAlive
{
    public bool IsAlive { get; set; } = true;
}

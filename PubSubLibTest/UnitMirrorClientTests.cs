using Google.Protobuf;
using PubSubLib;
using PubSubLib.Client;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLibTest;

public class UMCAlive : PubSubLib.Client.IAlive
{
    public bool IsAlive => true;
}

[UnitMirrorClient(typeof(RemoveWatcherCmd), Target = typeof(UMCAlive))]
public partial class UMCRemoveWatcher
{
    public string? LastCommit { get; private set; }
    public bool OnStartCalled { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
    partial void OnStart() => OnStartCalled = true;
}

[UnitMirrorClient(typeof(BatchEnterMsg), Target = typeof(UMCAlive))]
public partial class UMCBatchEnter
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[UnitMirrorClient(typeof(StructTestMsg), Target = typeof(UMCAlive))]
public partial class UMCStructTest
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[UnitMirrorClient(typeof(Vector3TestMsg), Target = typeof(UMCAlive))]
public partial class UMCVector3Test
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[UnitMirrorClient(typeof(Vector3StructTestMsg), Target = typeof(UMCAlive))]
public partial class UMCVector3Struct
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[UnitMirrorClient(typeof(Vector3SingleStructTestMsg), Target = typeof(UMCAlive))]
public partial class UMCVector3SingleStruct
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[UnitMirrorClient(typeof(PrimitiveArrayStructTestMsg), Target = typeof(UMCAlive))]
public partial class UMCPrimitiveArray
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[UnitMirrorClient(typeof(ClassTestMsg), Target = typeof(UMCAlive))]
public partial class UMCClassTest
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[UnitMirrorClient(typeof(MirrorSendTestMsg), Target = typeof(UMCAlive))]
public partial class UMCMirrorSend
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[Collection("MirrorProtoBus")]
public class UnitMirrorClientTests
{
    [Fact]
    public void GetMirrorProto_Returns_Same_Instance()
    {
        var client = new UMCRemoveWatcher();
        var p1 = client.GetMirrorProto();
        var p2 = client.GetMirrorProto();
        Assert.Same(p1, p2);
    }

    [Fact]
    public void ApplyUpdate_DeserializesSimpleField()
    {
        var src = new RemoveWatcherCmd { WatcherId = 42 };
        var client = new UMCRemoveWatcher();
        client.ApplyUpdate(src.ToByteArray(), "test_commit");

        Assert.Equal(42L, client.WatcherId);
        Assert.Equal("test_commit", client.LastCommit);
        Assert.True(client.OnStartCalled);
    }

    [Fact]
    public void ApplyUpdate_SecondUpdate_ReplacesValue()
    {
        var client = new UMCRemoveWatcher();
        client.ApplyUpdate(new RemoveWatcherCmd { WatcherId = 1 }.ToByteArray(), "first");
        client.ApplyUpdate(new RemoveWatcherCmd { WatcherId = 99 }.ToByteArray(), "second");

        Assert.Equal(99L, client.WatcherId);
        Assert.Equal("second", client.LastCommit);
    }

    [Fact]
    public void OnStart_Invoked_OnlyOnce()
    {
        var client = new UMCRemoveWatcher();
        Assert.False(client.OnStartCalled);

        client.ApplyUpdate(new RemoveWatcherCmd { WatcherId = 1 }.ToByteArray(), "init");
        Assert.True(client.OnStartCalled);

        client.ApplyUpdate(new RemoveWatcherCmd { WatcherId = 2 }.ToByteArray(), "update");
        Assert.True(client.OnStartCalled);
        Assert.Equal("update", client.LastCommit);
    }

    [Fact]
    public void RepeatedField_ApplyUpdate_Deserializes()
    {
        var src = new BatchEnterMsg { UnitId = 1 };
        src.WatcherIds.Add(10);
        src.WatcherIds.Add(20);
        src.WatcherIds.Add(30);

        var client = new UMCBatchEnter();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(1L, client.UnitId);
        Assert.Equal(new long[] { 10, 20, 30 }, client.WatcherIds);
    }

    [Fact]
    public void RepeatedField_SecondApplyUpdate_ReplacesList()
    {
        var client = new UMCBatchEnter();
        var src1 = new BatchEnterMsg { UnitId = 1 };
        src1.WatcherIds.Add(1);
        client.ApplyUpdate(src1.ToByteArray(), "first");

        var src2 = new BatchEnterMsg { UnitId = 2 };
        src2.WatcherIds.Add(9);
        src2.WatcherIds.Add(8);
        client.ApplyUpdate(src2.ToByteArray(), "second");

        Assert.Equal(2L, client.UnitId);
        Assert.Equal(new long[] { 9, 8 }, client.WatcherIds);
    }

    [Fact]
    public void StructGroup_ApplyUpdate_Deserializes()
    {
        var src = new StructTestMsg { Version = 5 };
        src.StructXPlayerXId.Add(10);
        src.StructXPlayerXName.Add("alpha");
        src.StructXPlayerXId.Add(20);
        src.StructXPlayerXName.Add("beta");

        var client = new UMCStructTest();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(5, client.Version);
        Assert.Equal(2, client.Players.Count);
        Assert.Equal(10L, client.Players[0].Id);
        Assert.Equal("alpha", client.Players[0].Name);
        Assert.Equal(20L, client.Players[1].Id);
        Assert.Equal("beta", client.Players[1].Name);
    }

    [Fact]
    public void StructGroup_SecondApplyUpdate_ReplacesList()
    {
        var client = new UMCStructTest();
        var src1 = new StructTestMsg();
        src1.StructXPlayerXId.Add(1);
        src1.StructXPlayerXName.Add("one");
        client.ApplyUpdate(src1.ToByteArray(), "first");

        var src2 = new StructTestMsg();
        src2.StructXPlayerXId.Add(9);
        src2.StructXPlayerXName.Add("nine");
        src2.StructXPlayerXId.Add(8);
        src2.StructXPlayerXName.Add("eight");
        client.ApplyUpdate(src2.ToByteArray(), "second");

        Assert.Equal(2, client.Players.Count);
        Assert.Equal(9L, client.Players[0].Id);
        Assert.Equal("nine", client.Players[0].Name);
        Assert.Equal(8L, client.Players[1].Id);
        Assert.Equal("eight", client.Players[1].Name);
    }

    [Fact]
    public void Vector3_Single_ApplyUpdate_Deserializes()
    {
        var src = new Vector3TestMsg { Version = 7 };
        src.PositionVector3.Add(1.5f);
        src.PositionVector3.Add(2.5f);
        src.PositionVector3.Add(3.5f);

        var client = new UMCVector3Test();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(7, client.Version);
        Assert.Equal(1.5f, client.Position.x);
        Assert.Equal(2.5f, client.Position.y);
        Assert.Equal(3.5f, client.Position.z);
    }

    [Fact]
    public void Vector3_List_ApplyUpdate_Deserializes()
    {
        var src = new Vector3TestMsg();
        src.WaypointsVector3S.Add(1);
        src.WaypointsVector3S.Add(2);
        src.WaypointsVector3S.Add(3);
        src.WaypointsVector3S.Add(4);
        src.WaypointsVector3S.Add(5);
        src.WaypointsVector3S.Add(6);

        var client = new UMCVector3Test();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(2, client.Waypoints.Count);
        Assert.Equal(1f, client.Waypoints[0].x);
        Assert.Equal(2f, client.Waypoints[0].y);
        Assert.Equal(3f, client.Waypoints[0].z);
        Assert.Equal(4f, client.Waypoints[1].x);
        Assert.Equal(5f, client.Waypoints[1].y);
        Assert.Equal(6f, client.Waypoints[1].z);
    }

    [Fact]
    public void Vector3_List_SecondApplyUpdate_ReplacesList()
    {
        var client = new UMCVector3Test();
        var src1 = new Vector3TestMsg();
        src1.WaypointsVector3S.Add(1);
        src1.WaypointsVector3S.Add(2);
        src1.WaypointsVector3S.Add(3);
        client.ApplyUpdate(src1.ToByteArray(), "first");

        var src2 = new Vector3TestMsg();
        src2.WaypointsVector3S.Add(9);
        src2.WaypointsVector3S.Add(8);
        src2.WaypointsVector3S.Add(7);
        src2.WaypointsVector3S.Add(6);
        src2.WaypointsVector3S.Add(5);
        src2.WaypointsVector3S.Add(4);
        client.ApplyUpdate(src2.ToByteArray(), "second");

        Assert.Equal(2, client.Waypoints.Count);
        Assert.Equal(9f, client.Waypoints[0].x);
        Assert.Equal(8f, client.Waypoints[0].y);
        Assert.Equal(7f, client.Waypoints[0].z);
        Assert.Equal(6f, client.Waypoints[1].x);
        Assert.Equal(5f, client.Waypoints[1].y);
        Assert.Equal(4f, client.Waypoints[1].z);
    }

    [Fact]
    public void Vector3Struct_Array_ApplyUpdate_Deserializes()
    {
        var src = new Vector3StructTestMsg { Version = 5 };
        src.StructXPlayerXId.Add(10);
        src.StructXPlayerXName.Add("alpha");
        src.StructXPlayerXPositionVector3SValue.Add(1);
        src.StructXPlayerXPositionVector3SValue.Add(2);
        src.StructXPlayerXPositionVector3SValue.Add(3);
        src.StructXPlayerXPositionVector3SCount.Add(1);

        var client = new UMCVector3Struct();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(5, client.Version);
        Assert.Single(client.Players);
        Assert.Equal(10L, client.Players[0].Id);
        Assert.Equal("alpha", client.Players[0].Name);
        Assert.Single(client.Players[0].Position);
        Assert.Equal(1f, client.Players[0].Position[0].x);
        Assert.Equal(2f, client.Players[0].Position[0].y);
        Assert.Equal(3f, client.Players[0].Position[0].z);
    }

    [Fact]
    public void Vector3Struct_MultiplePlayers_DifferentCounts()
    {
        var src = new Vector3StructTestMsg();
        src.StructXPlayerXId.Add(1);
        src.StructXPlayerXName.Add("ninh");
        src.StructXPlayerXPositionVector3SCount.Add(1);
        src.StructXPlayerXPositionVector3SValue.Add(1);
        src.StructXPlayerXPositionVector3SValue.Add(2);
        src.StructXPlayerXPositionVector3SValue.Add(3);
        src.StructXPlayerXId.Add(2);
        src.StructXPlayerXName.Add("yen");
        src.StructXPlayerXPositionVector3SCount.Add(2);
        src.StructXPlayerXPositionVector3SValue.Add(4);
        src.StructXPlayerXPositionVector3SValue.Add(5);
        src.StructXPlayerXPositionVector3SValue.Add(6);
        src.StructXPlayerXPositionVector3SValue.Add(7);
        src.StructXPlayerXPositionVector3SValue.Add(8);
        src.StructXPlayerXPositionVector3SValue.Add(9);

        var client = new UMCVector3Struct();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(2, client.Players.Count);
        Assert.Equal(1L, client.Players[0].Id);
        Assert.Single(client.Players[0].Position);
        Assert.Equal(1f, client.Players[0].Position[0].x);
        Assert.Equal(2L, client.Players[1].Id);
        Assert.Equal(2, client.Players[1].Position.Count);
        Assert.Equal(4f, client.Players[1].Position[0].x);
        Assert.Equal(7f, client.Players[1].Position[1].x);
    }

    [Fact]
    public void Vector3Struct_EmptyPosition_Deserializes()
    {
        var src = new Vector3StructTestMsg();
        src.StructXPlayerXId.Add(1);
        src.StructXPlayerXName.Add("ninh");
        src.StructXPlayerXPositionVector3SCount.Add(0);

        var client = new UMCVector3Struct();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Single(client.Players);
        Assert.Equal(1L, client.Players[0].Id);
        Assert.Empty(client.Players[0].Position);
    }

    [Fact]
    public void Vector3Struct_SecondApplyUpdate_ReplacesList()
    {
        var client = new UMCVector3Struct();
        var src1 = new Vector3StructTestMsg();
        src1.StructXPlayerXId.Add(1);
        src1.StructXPlayerXName.Add("one");
        src1.StructXPlayerXPositionVector3SCount.Add(1);
        src1.StructXPlayerXPositionVector3SValue.Add(1);
        src1.StructXPlayerXPositionVector3SValue.Add(2);
        src1.StructXPlayerXPositionVector3SValue.Add(3);
        client.ApplyUpdate(src1.ToByteArray(), "first");

        var src2 = new Vector3StructTestMsg();
        src2.StructXPlayerXId.Add(9);
        src2.StructXPlayerXName.Add("nine");
        src2.StructXPlayerXPositionVector3SCount.Add(2);
        src2.StructXPlayerXPositionVector3SValue.Add(9);
        src2.StructXPlayerXPositionVector3SValue.Add(8);
        src2.StructXPlayerXPositionVector3SValue.Add(7);
        src2.StructXPlayerXPositionVector3SValue.Add(6);
        src2.StructXPlayerXPositionVector3SValue.Add(5);
        src2.StructXPlayerXPositionVector3SValue.Add(4);
        client.ApplyUpdate(src2.ToByteArray(), "second");

        Assert.Single(client.Players);
        Assert.Equal(9L, client.Players[0].Id);
        Assert.Equal(2, client.Players[0].Position.Count);
        Assert.Equal(9f, client.Players[0].Position[0].x);
        Assert.Equal(6f, client.Players[0].Position[1].x);
    }

    [Fact]
    public void Vector3SingleStruct_ApplyUpdate_Deserializes()
    {
        var src = new Vector3SingleStructTestMsg { Version = 5 };
        src.StructXPlayerXId.Add(10);
        src.StructXPlayerXName.Add("alpha");
        src.StructXPlayerXPositionVector3.Add(1);
        src.StructXPlayerXPositionVector3.Add(2);
        src.StructXPlayerXPositionVector3.Add(3);

        var client = new UMCVector3SingleStruct();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(5, client.Version);
        Assert.Single(client.Players);
        Assert.Equal(10L, client.Players[0].Id);
        Assert.Equal("alpha", client.Players[0].Name);
        Assert.Equal(1f, client.Players[0].Position.x);
        Assert.Equal(2f, client.Players[0].Position.y);
        Assert.Equal(3f, client.Players[0].Position.z);
    }

    [Fact]
    public void Vector3SingleStruct_MultiplePlayers_Deserializes()
    {
        var src = new Vector3SingleStructTestMsg();
        src.StructXPlayerXId.Add(1);
        src.StructXPlayerXName.Add("ninh");
        src.StructXPlayerXPositionVector3.Add(1);
        src.StructXPlayerXPositionVector3.Add(2);
        src.StructXPlayerXPositionVector3.Add(3);
        src.StructXPlayerXId.Add(2);
        src.StructXPlayerXName.Add("yen");
        src.StructXPlayerXPositionVector3.Add(4);
        src.StructXPlayerXPositionVector3.Add(5);
        src.StructXPlayerXPositionVector3.Add(6);

        var client = new UMCVector3SingleStruct();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(2, client.Players.Count);
        Assert.Equal(1L, client.Players[0].Id);
        Assert.Equal(1f, client.Players[0].Position.x);
        Assert.Equal(2L, client.Players[1].Id);
        Assert.Equal(4f, client.Players[1].Position.x);
        Assert.Equal(5f, client.Players[1].Position.y);
        Assert.Equal(6f, client.Players[1].Position.z);
    }

    [Fact]
    public void Vector3SingleStruct_MissingFloats_Defaults()
    {
        var src = new Vector3SingleStructTestMsg();
        src.StructXPlayerXId.Add(1);
        src.StructXPlayerXName.Add("ninh");

        var client = new UMCVector3SingleStruct();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Single(client.Players);
        Assert.Equal(1L, client.Players[0].Id);
        Assert.Equal(0f, client.Players[0].Position.x);
        Assert.Equal(0f, client.Players[0].Position.y);
        Assert.Equal(0f, client.Players[0].Position.z);
    }

    [Fact]
    public void PrimitiveArray_ApplyUpdate_Deserializes()
    {
        var proto = new PrimitiveArrayStructTestMsg
        {
            StructXPlayerXId = { 1 },
            StructXPlayerXName = { "ninh" },
            StructXPlayerXBeanArrayValue = { 10, 20 },
            StructXPlayerXBeanArrayCount = { 2 },
            StructXPlayerXScoresArrayValue = { 0.5f, 1.0f },
            StructXPlayerXScoresArrayCount = { 2 }
        };

        var client = new UMCPrimitiveArray();
        client.ApplyUpdate(proto.ToByteArray(), "init");

        Assert.Single(client.Players);
        var p = client.Players[0];
        Assert.Equal(1L, p.Id);
        Assert.Equal("ninh", p.Name);
        Assert.Equal(new long[] { 10, 20 }, p.Bean);
        Assert.Equal(new float[] { 0.5f, 1.0f }, p.Scores);
    }

    [Fact]
    public void PrimitiveArray_MultipleItems_Deserializes()
    {
        var proto = new PrimitiveArrayStructTestMsg
        {
            StructXPlayerXId = { 1, 2 },
            StructXPlayerXName = { "a", "b" },
            StructXPlayerXBeanArrayValue = { 1, 2, 3 },
            StructXPlayerXBeanArrayCount = { 1, 2 },
            StructXPlayerXScoresArrayValue = { 0.1f, 0.2f, 0.3f },
            StructXPlayerXScoresArrayCount = { 1, 2 }
        };

        var client = new UMCPrimitiveArray();
        client.ApplyUpdate(proto.ToByteArray(), "init");

        Assert.Equal(2, client.Players.Count);
        var p0 = client.Players[0];
        Assert.Equal(new long[] { 1 }, p0.Bean);
        Assert.Equal(new float[] { 0.1f }, p0.Scores);
        var p1 = client.Players[1];
        Assert.Equal(new long[] { 2, 3 }, p1.Bean);
        Assert.Equal(new float[] { 0.2f, 0.3f }, p1.Scores);
    }

    [Fact]
    public void PrimitiveArray_EmptyArray_Deserializes()
    {
        var proto = new PrimitiveArrayStructTestMsg
        {
            StructXPlayerXId = { 1 },
            StructXPlayerXName = { "x" },
            StructXPlayerXBeanArrayCount = { 0 },
            StructXPlayerXScoresArrayCount = { 0 }
        };

        var client = new UMCPrimitiveArray();
        client.ApplyUpdate(proto.ToByteArray(), "init");

        Assert.Single(client.Players);
        Assert.Empty(client.Players[0].Bean);
        Assert.Empty(client.Players[0].Scores);
    }

    [Fact]
    public void PrimitiveArray_SecondApplyUpdate_Replaces()
    {
        var client = new UMCPrimitiveArray();
        client.ApplyUpdate(new PrimitiveArrayStructTestMsg
        {
            StructXPlayerXId = { 1 },
            StructXPlayerXName = { "first" },
            StructXPlayerXBeanArrayValue = { 1, 2 },
            StructXPlayerXBeanArrayCount = { 2 },
            StructXPlayerXScoresArrayValue = { 0.1f },
            StructXPlayerXScoresArrayCount = { 1 }
        }.ToByteArray(), "init");

        client.ApplyUpdate(new PrimitiveArrayStructTestMsg
        {
            StructXPlayerXId = { 2 },
            StructXPlayerXName = { "second" },
            StructXPlayerXBeanArrayValue = { 3, 4, 5 },
            StructXPlayerXBeanArrayCount = { 3 },
            StructXPlayerXScoresArrayValue = { 0.2f, 0.3f },
            StructXPlayerXScoresArrayCount = { 2 }
        }.ToByteArray(), "update");

        Assert.Single(client.Players);
        var p = client.Players[0];
        Assert.Equal(2L, p.Id);
        Assert.Equal("second", p.Name);
        Assert.Equal(new long[] { 3, 4, 5 }, p.Bean);
        Assert.Equal(new float[] { 0.2f, 0.3f }, p.Scores);
    }

    [Fact]
    public void ClassGroup_ApplyUpdate_Deserializes()
    {
        var src = new ClassTestMsg { Version = 5 };
        src.ClassXPlayerXId.Add(10);
        src.ClassXPlayerXName.Add("alpha");
        src.ClassXPlayerXId.Add(20);
        src.ClassXPlayerXName.Add("beta");

        var client = new UMCClassTest();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Equal(2, client.Players.Count);
        Assert.Equal(10L, client.Players[0].Id);
        Assert.Equal("alpha", client.Players[0].Name);
        Assert.Equal(20L, client.Players[1].Id);
        Assert.Equal("beta", client.Players[1].Name);
    }

    [Fact]
    public void ClassGroup_SecondApplyUpdate_ReplacesList()
    {
        var client = new UMCClassTest();
        var src1 = new ClassTestMsg();
        src1.ClassXPlayerXId.Add(1);
        src1.ClassXPlayerXName.Add("first");
        client.ApplyUpdate(src1.ToByteArray(), "init");

        var src2 = new ClassTestMsg();
        src2.ClassXPlayerXId.Add(9);
        src2.ClassXPlayerXName.Add("second");
        src2.ClassXPlayerXId.Add(8);
        src2.ClassXPlayerXName.Add("third");
        client.ApplyUpdate(src2.ToByteArray(), "update");

        Assert.Equal(2, client.Players.Count);
        Assert.Equal(9L, client.Players[0].Id);
        Assert.Equal("second", client.Players[0].Name);
        Assert.Equal(8L, client.Players[1].Id);
        Assert.Equal("third", client.Players[1].Name);
    }

    [Fact]
    public void ClassGroup_Vector3Array_Deserializes()
    {
        var src = new ClassTestMsg { Version = 5 };
        src.ClassXPlayerXId.Add(1);
        src.ClassXPlayerXName.Add("ninh");
        src.ClassXPlayerXPositionVector3SValue.Add(1);
        src.ClassXPlayerXPositionVector3SValue.Add(2);
        src.ClassXPlayerXPositionVector3SValue.Add(3);
        src.ClassXPlayerXPositionVector3SCount.Add(1);

        var client = new UMCClassTest();
        client.ApplyUpdate(src.ToByteArray(), "init");

        Assert.Single(client.Players);
        Assert.Equal(1L, client.Players[0].Id);
        Assert.Equal("ninh", client.Players[0].Name);
        Assert.Single(client.Players[0].Position);
        Assert.Equal(1f, client.Players[0].Position[0].x);
        Assert.Equal(2f, client.Players[0].Position[0].y);
        Assert.Equal(3f, client.Players[0].Position[0].z);
    }

    [Fact]
    public void ClassGroup_Vector3Array_SecondApplyUpdate_Replaces()
    {
        var client = new UMCClassTest();
        var src1 = new ClassTestMsg();
        src1.ClassXPlayerXId.Add(1);
        src1.ClassXPlayerXName.Add("first");
        src1.ClassXPlayerXPositionVector3SCount.Add(1);
        src1.ClassXPlayerXPositionVector3SValue.Add(1);
        src1.ClassXPlayerXPositionVector3SValue.Add(2);
        src1.ClassXPlayerXPositionVector3SValue.Add(3);
        client.ApplyUpdate(src1.ToByteArray(), "init");

        var src2 = new ClassTestMsg();
        src2.ClassXPlayerXId.Add(9);
        src2.ClassXPlayerXName.Add("second");
        src2.ClassXPlayerXPositionVector3SCount.Add(2);
        src2.ClassXPlayerXPositionVector3SValue.Add(4);
        src2.ClassXPlayerXPositionVector3SValue.Add(5);
        src2.ClassXPlayerXPositionVector3SValue.Add(6);
        src2.ClassXPlayerXPositionVector3SValue.Add(7);
        src2.ClassXPlayerXPositionVector3SValue.Add(8);
        src2.ClassXPlayerXPositionVector3SValue.Add(9);
        client.ApplyUpdate(src2.ToByteArray(), "update");

        Assert.Single(client.Players);
        Assert.Equal(9L, client.Players[0].Id);
        Assert.Equal(2, client.Players[0].Position.Count);
        Assert.Equal(4f, client.Players[0].Position[0].x);
        Assert.Equal(7f, client.Players[0].Position[1].x);
    }

    [Fact]
    public void OnMessage_ReceivesAndDeserializes()
    {
        var client = new UMCMirrorSend();
        ChatMsg? received = null;
        client.OnMessage<ChatMsg>("chat", msg => received = msg);

        var chat = new ChatMsg { Text = "hello world" };
        ((IRegionClientUnitInternal)client).DispatchMessage("chat", chat.ToByteArray());

        Assert.NotNull(received);
        Assert.Equal("hello world", received!.Text);
    }

    [Fact]
    public void OnMessage_Unsubscribe_StopsReceiving()
    {
        var client = new UMCMirrorSend();
        var received = new List<string>();
        var sub = client.OnMessage<ChatMsg>("chat", msg => received.Add(msg.Text));

        ((IRegionClientUnitInternal)client).DispatchMessage("chat", new ChatMsg { Text = "first" }.ToByteArray());
        Assert.Single(received);

        sub.UnSubscribe();

        ((IRegionClientUnitInternal)client).DispatchMessage("chat", new ChatMsg { Text = "second" }.ToByteArray());
        Assert.Single(received);
        Assert.Equal("first", received[0]);
    }

    [Fact]
    public void DispatchMessage_NoHandler_DoesNotThrow()
    {
        var client = new UMCMirrorSend();
        ((IRegionClientUnitInternal)client).DispatchMessage("chat", new ChatMsg { Text = "hi" }.ToByteArray());
    }

    [Fact]
    public void GetTarget_ReturnsTarget_AfterSetTarget()
    {
        var client = new UMCRemoveWatcher();
        var target = new UMCAlive();
        ((IRegionClientUnitInternal)client).SetTarget(target);
        Assert.Same(target, client.GetTarget());
    }

    [Fact]
    public void Init_SetsIdAndPosition()
    {
        var client = new UMCRemoveWatcher();
        ((IRegionClientUnitInternal)client).Init(42, new Vector2 { x = 1.5f, y = 2.5f });

        Assert.Equal(42L, client.Id);
    }
}

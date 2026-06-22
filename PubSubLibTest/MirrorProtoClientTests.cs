using Google.Protobuf;
using PubSubLib;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLibTest;

[MirrorProtoClient(typeof(RemoveWatcherCmd))]
public partial class RemoveWatcherMirrorClient
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[MirrorProtoClient(typeof(BatchEnterMsg))]
public partial class BatchEnterMirrorClient
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[MirrorProtoClient(typeof(StructTestMsg))]
public partial class StructTestMirrorClient
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[MirrorProtoClient(typeof(Vector3TestMsg))]
public partial class Vector3TestMirrorClient
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[MirrorProtoClient(typeof(Vector3StructTestMsg))]
public partial class Vector3StructTestMirrorClient
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[MirrorProtoClient(typeof(Vector3SingleStructTestMsg))]
public partial class Vector3SingleStructTestMirrorClient
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

[MirrorProtoClient(typeof(MirrorSendTestMsg))]
public partial class MirrorSendTestMirrorClient
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;
}

public class MirrorProtoClientTests
{
    [Fact]
    public void GetMirrorProto_Returns_Same_Instance()
    {
        var mirror = new RemoveWatcherMirrorClient();
        var p1 = mirror.GetMirrorProto();
        var p2 = mirror.GetMirrorProto();
        Assert.Same(p1, p2);
    }

    [Fact]
    public void Getter_ReturnsStoredValue()
    {
        var src = new RemoveWatcherCmd { WatcherId = 99 };
        var mirror = new RemoveWatcherMirrorClient();
        mirror.ApplyUpdate(src.ToByteArray(), "test");
        Assert.Equal(99L, mirror.WatcherId);
    }

    [Fact]
    public void ApplyUpdate_DeserializesAndSyncs()
    {
        var src = new RemoveWatcherCmd { WatcherId = 42 };
        var bytes = src.ToByteArray();

        var mirror = new RemoveWatcherMirrorClient();
        mirror.ApplyUpdate(bytes, "test_commit");

        Assert.Equal(42L, mirror.WatcherId);
    }

    [Fact]
    public void DataName_Defaults_To_ProtoTypeName()
    {
        var mirror = new RemoveWatcherMirrorClient();
        Assert.Equal("RemoveWatcherCmd", mirror.DataName);
    }

    [Fact]
    public void Implements_IPlayerMirrorClient()
    {
        var mirror = new RemoveWatcherMirrorClient() as IPlayerMirrorClient;
        Assert.NotNull(mirror);
        mirror!.PlayerId = 123;
        Assert.Equal(123L, mirror.PlayerId);
    }

    [Fact]
    public void OnCommit_Invoked_After_ApplyUpdate()
    {
        var src = new RemoveWatcherCmd { WatcherId = 42 };
        var bytes = src.ToByteArray();

        var mirror = new RemoveWatcherMirrorClient();
        mirror.ApplyUpdate(bytes, "player_did_action");

        Assert.Equal("player_did_action", mirror.LastCommit);
        Assert.Equal(42L, mirror.WatcherId);
    }

    [Fact]
    public void Repeated_ApplyUpdate_CopiesList()
    {
        var src = new BatchEnterMsg { UnitId = 1 };
        src.WatcherIds.Add(10);
        src.WatcherIds.Add(20);
        src.WatcherIds.Add(30);
        var bytes = src.ToByteArray();

        var mirror = new BatchEnterMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        Assert.Equal(1L, mirror.UnitId);
        var list = ((System.Collections.Generic.IReadOnlyList<long>)mirror.WatcherIds);
        Assert.Equal(new long[] { 10, 20, 30 }, list);
    }

    [Fact]
    public void Repeated_SecondApplyUpdate_ReplacesList()
    {
        var src1 = new BatchEnterMsg();
        src1.WatcherIds.Add(1);
        var bytes1 = src1.ToByteArray();

        var src2 = new BatchEnterMsg();
        src2.WatcherIds.Add(9);
        src2.WatcherIds.Add(8);
        var bytes2 = src2.ToByteArray();

        var mirror = new BatchEnterMirrorClient();
        mirror.ApplyUpdate(bytes1, "first");
        mirror.ApplyUpdate(bytes2, "second");

        var list = ((System.Collections.Generic.IReadOnlyList<long>)mirror.WatcherIds);
        Assert.Equal(new long[] { 9, 8 }, list);
    }

    [Fact]
    public void StructGroup_ApplyUpdate_Deserializes()
    {
        var src = new StructTestMsg { Version = 5 };
        src.StructXPlayerXId.Add(10);
        src.StructXPlayerXName.Add("alpha");
        src.StructXPlayerXId.Add(20);
        src.StructXPlayerXName.Add("beta");
        var bytes = src.ToByteArray();

        var mirror = new StructTestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        Assert.Equal(5, mirror.Version);
        var list = ((System.Collections.Generic.IReadOnlyList<StructTestMirrorClient.Player>)mirror.Players);
        Assert.Equal(2, list.Count);
        Assert.Equal(10L, list[0].Id);
        Assert.Equal("alpha", list[0].Name);
        Assert.Equal(20L, list[1].Id);
        Assert.Equal("beta", list[1].Name);
    }

    [Fact]
    public void StructGroup_SecondApplyUpdate_ReplacesList()
    {
        var src1 = new StructTestMsg();
        src1.StructXPlayerXId.Add(1);
        src1.StructXPlayerXName.Add("one");
        var bytes1 = src1.ToByteArray();

        var src2 = new StructTestMsg();
        src2.StructXPlayerXId.Add(9);
        src2.StructXPlayerXName.Add("nine");
        src2.StructXPlayerXId.Add(8);
        src2.StructXPlayerXName.Add("eight");
        var bytes2 = src2.ToByteArray();

        var mirror = new StructTestMirrorClient();
        mirror.ApplyUpdate(bytes1, "first");
        mirror.ApplyUpdate(bytes2, "second");

        var list = ((System.Collections.Generic.IReadOnlyList<StructTestMirrorClient.Player>)mirror.Players);
        Assert.Equal(2, list.Count);
        Assert.Equal(9L, list[0].Id);
        Assert.Equal("nine", list[0].Name);
        Assert.Equal(8L, list[1].Id);
        Assert.Equal("eight", list[1].Name);
    }

    [Fact]
    public void Vector3_ApplyUpdate_Deserializes()
    {
        var src = new Vector3TestMsg { Version = 7 };
        src.PositionVector3.Add(1.5f);
        src.PositionVector3.Add(2.5f);
        src.PositionVector3.Add(3.5f);
        var bytes = src.ToByteArray();

        var mirror = new Vector3TestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        Assert.Equal(7, mirror.Version);
        Assert.Equal(1.5f, mirror.Position.x);
        Assert.Equal(2.5f, mirror.Position.y);
        Assert.Equal(3.5f, mirror.Position.z);
    }

    [Fact]
    public void Vector3List_ApplyUpdate_Deserializes()
    {
        var src = new Vector3TestMsg();
        src.WaypointsVector3S.Add(1);
        src.WaypointsVector3S.Add(2);
        src.WaypointsVector3S.Add(3);
        src.WaypointsVector3S.Add(4);
        src.WaypointsVector3S.Add(5);
        src.WaypointsVector3S.Add(6);
        var bytes = src.ToByteArray();

        var mirror = new Vector3TestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        var list = ((System.Collections.Generic.IReadOnlyList<Vector3>)mirror.Waypoints);
        Assert.Equal(2, list.Count);
        Assert.Equal(1f, list[0].x);
        Assert.Equal(2f, list[0].y);
        Assert.Equal(3f, list[0].z);
        Assert.Equal(4f, list[1].x);
        Assert.Equal(5f, list[1].y);
        Assert.Equal(6f, list[1].z);
    }

    [Fact]
    public void Vector3List_SecondApplyUpdate_ReplacesList()
    {
        var src1 = new Vector3TestMsg();
        src1.WaypointsVector3S.Add(1);
        src1.WaypointsVector3S.Add(2);
        src1.WaypointsVector3S.Add(3);
        var bytes1 = src1.ToByteArray();

        var src2 = new Vector3TestMsg();
        src2.WaypointsVector3S.Add(9);
        src2.WaypointsVector3S.Add(8);
        src2.WaypointsVector3S.Add(7);
        src2.WaypointsVector3S.Add(6);
        src2.WaypointsVector3S.Add(5);
        src2.WaypointsVector3S.Add(4);
        var bytes2 = src2.ToByteArray();

        var mirror = new Vector3TestMirrorClient();
        mirror.ApplyUpdate(bytes1, "first");
        mirror.ApplyUpdate(bytes2, "second");

        var list = ((System.Collections.Generic.IReadOnlyList<Vector3>)mirror.Waypoints);
        Assert.Equal(2, list.Count);
        Assert.Equal(9f, list[0].x);
        Assert.Equal(8f, list[0].y);
        Assert.Equal(7f, list[0].z);
        Assert.Equal(6f, list[1].x);
        Assert.Equal(5f, list[1].y);
        Assert.Equal(4f, list[1].z);
    }

    [Fact]
    public void Vector3Struct_ApplyUpdate_Deserializes()
    {
        var src = new Vector3StructTestMsg { Version = 5 };
        src.StructXPlayerXId.Add(10);
        src.StructXPlayerXName.Add("alpha");
        src.StructXPlayerXPositionVector3SValue.Add(1); src.StructXPlayerXPositionVector3SValue.Add(2); src.StructXPlayerXPositionVector3SValue.Add(3);
        src.StructXPlayerXPositionVector3SCount.Add(1);
        var bytes = src.ToByteArray();

        var mirror = new Vector3StructTestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        Assert.Equal(5, mirror.Version);
        var list = ((System.Collections.Generic.IReadOnlyList<Vector3StructTestMirrorClient.Player>)mirror.Players);
        Assert.Single(list);
        Assert.Equal(10L, list[0].Id);
        Assert.Equal("alpha", list[0].Name);
        Assert.Single(list[0].Position);
        Assert.Equal(1f, list[0].Position[0].x);
        Assert.Equal(2f, list[0].Position[0].y);
        Assert.Equal(3f, list[0].Position[0].z);
    }

    [Fact]
    public void Vector3Struct_MultiplePlayers_DifferentCounts_Deserializes()
    {
        var src = new Vector3StructTestMsg();
        src.StructXPlayerXId.Add(1);
        src.StructXPlayerXName.Add("ninh");
        src.StructXPlayerXPositionVector3SCount.Add(1);
        src.StructXPlayerXPositionVector3SValue.Add(1); src.StructXPlayerXPositionVector3SValue.Add(2); src.StructXPlayerXPositionVector3SValue.Add(3);
        src.StructXPlayerXId.Add(2);
        src.StructXPlayerXName.Add("yen");
        src.StructXPlayerXPositionVector3SCount.Add(2);
        src.StructXPlayerXPositionVector3SValue.Add(4); src.StructXPlayerXPositionVector3SValue.Add(5); src.StructXPlayerXPositionVector3SValue.Add(6);
        src.StructXPlayerXPositionVector3SValue.Add(7); src.StructXPlayerXPositionVector3SValue.Add(8); src.StructXPlayerXPositionVector3SValue.Add(9);
        var bytes = src.ToByteArray();

        var mirror = new Vector3StructTestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        var list = ((System.Collections.Generic.IReadOnlyList<Vector3StructTestMirrorClient.Player>)mirror.Players);
        Assert.Equal(2, list.Count);
        Assert.Equal(1L, list[0].Id);
        Assert.Single(list[0].Position);
        Assert.Equal(1f, list[0].Position[0].x);
        Assert.Equal(2L, list[1].Id);
        Assert.Equal(2, list[1].Position.Length);
        Assert.Equal(4f, list[1].Position[0].x);
        Assert.Equal(7f, list[1].Position[1].x);
    }

    [Fact]
    public void Vector3Struct_EmptyPosition_Deserializes()
    {
        var src = new Vector3StructTestMsg();
        src.StructXPlayerXId.Add(1);
        src.StructXPlayerXName.Add("ninh");
        src.StructXPlayerXPositionVector3SCount.Add(0);
        var bytes = src.ToByteArray();

        var mirror = new Vector3StructTestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        var list = ((System.Collections.Generic.IReadOnlyList<Vector3StructTestMirrorClient.Player>)mirror.Players);
        Assert.Single(list);
        Assert.Equal(1L, list[0].Id);
        Assert.Empty(list[0].Position);
    }

    [Fact]
    public void Vector3Struct_SecondApplyUpdate_ReplacesList()
    {
        var src1 = new Vector3StructTestMsg();
        src1.StructXPlayerXId.Add(1);
        src1.StructXPlayerXName.Add("one");
        src1.StructXPlayerXPositionVector3SCount.Add(1);
        src1.StructXPlayerXPositionVector3SValue.Add(1); src1.StructXPlayerXPositionVector3SValue.Add(2); src1.StructXPlayerXPositionVector3SValue.Add(3);
        var bytes1 = src1.ToByteArray();

        var src2 = new Vector3StructTestMsg();
        src2.StructXPlayerXId.Add(9);
        src2.StructXPlayerXName.Add("nine");
        src2.StructXPlayerXPositionVector3SCount.Add(2);
        src2.StructXPlayerXPositionVector3SValue.Add(9); src2.StructXPlayerXPositionVector3SValue.Add(8); src2.StructXPlayerXPositionVector3SValue.Add(7);
        src2.StructXPlayerXPositionVector3SValue.Add(6); src2.StructXPlayerXPositionVector3SValue.Add(5); src2.StructXPlayerXPositionVector3SValue.Add(4);
        var bytes2 = src2.ToByteArray();

        var mirror = new Vector3StructTestMirrorClient();
        mirror.ApplyUpdate(bytes1, "first");
        mirror.ApplyUpdate(bytes2, "second");

        var list = ((System.Collections.Generic.IReadOnlyList<Vector3StructTestMirrorClient.Player>)mirror.Players);
        Assert.Single(list);
        Assert.Equal(9L, list[0].Id);
        Assert.Equal(2, list[0].Position.Length);
        Assert.Equal(9f, list[0].Position[0].x);
        Assert.Equal(6f, list[0].Position[1].x);
    }

    [Fact]
    public void Vector3SingleStruct_ApplyUpdate_Deserializes()
    {
        var src = new Vector3SingleStructTestMsg { Version = 5 };
        src.StructXPlayerXId.Add(10);
        src.StructXPlayerXName.Add("alpha");
        src.StructXPlayerXPositionVector3.Add(1); src.StructXPlayerXPositionVector3.Add(2); src.StructXPlayerXPositionVector3.Add(3);
        var bytes = src.ToByteArray();

        var mirror = new Vector3SingleStructTestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        Assert.Equal(5, mirror.Version);
        var list = ((System.Collections.Generic.IReadOnlyList<Vector3SingleStructTestMirrorClient.Player>)mirror.Players);
        Assert.Single(list);
        Assert.Equal(10L, list[0].Id);
        Assert.Equal("alpha", list[0].Name);
        Assert.Equal(1f, list[0].Position.x);
        Assert.Equal(2f, list[0].Position.y);
        Assert.Equal(3f, list[0].Position.z);
    }

    [Fact]
    public void Vector3SingleStruct_MultiplePlayers_Deserializes()
    {
        var src = new Vector3SingleStructTestMsg();
        src.StructXPlayerXId.Add(1);
        src.StructXPlayerXName.Add("ninh");
        src.StructXPlayerXPositionVector3.Add(1); src.StructXPlayerXPositionVector3.Add(2); src.StructXPlayerXPositionVector3.Add(3);
        src.StructXPlayerXId.Add(2);
        src.StructXPlayerXName.Add("yen");
        src.StructXPlayerXPositionVector3.Add(4); src.StructXPlayerXPositionVector3.Add(5); src.StructXPlayerXPositionVector3.Add(6);
        var bytes = src.ToByteArray();

        var mirror = new Vector3SingleStructTestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        var list = ((System.Collections.Generic.IReadOnlyList<Vector3SingleStructTestMirrorClient.Player>)mirror.Players);
        Assert.Equal(2, list.Count);
        Assert.Equal(1L, list[0].Id);
        Assert.Equal(1f, list[0].Position.x);
        Assert.Equal(2L, list[1].Id);
        Assert.Equal(4f, list[1].Position.x);
        Assert.Equal(5f, list[1].Position.y);
        Assert.Equal(6f, list[1].Position.z);
    }

    [Fact]
    public void Vector3SingleStruct_MissingFloats_DeserializesDefault()
    {
        var src = new Vector3SingleStructTestMsg();
        src.StructXPlayerXId.Add(1);
        src.StructXPlayerXName.Add("ninh");
        var bytes = src.ToByteArray();

        var mirror = new Vector3SingleStructTestMirrorClient();
        mirror.ApplyUpdate(bytes, "init");

        var list = ((System.Collections.Generic.IReadOnlyList<Vector3SingleStructTestMirrorClient.Player>)mirror.Players);
        Assert.Single(list);
        Assert.Equal(1L, list[0].Id);
        Assert.Equal(0f, list[0].Position.x);
        Assert.Equal(0f, list[0].Position.y);
        Assert.Equal(0f, list[0].Position.z);
    }

    [Fact]
    public void OnMessage_ReceivesAndDeserializes()
    {
        var mirror = new MirrorSendTestMirrorClient();
        ChatMsg? received = null;
        mirror.OnMessage<ChatMsg>("chat", msg => received = msg);

        var chat = new ChatMsg { Text = "hello world" };
        ((IPlayerMirrorClient)mirror).DispatchMessage("chat", chat.ToByteArray());

        Assert.NotNull(received);
        Assert.Equal("hello world", received!.Text);
    }

    [Fact]
    public void OnMessage_MultipleHandlers_AllNotified()
    {
        var mirror = new MirrorSendTestMirrorClient();
        var received = new System.Collections.Concurrent.ConcurrentBag<string>();
        mirror.OnMessage<ChatMsg>("chat", msg => received.Add("a:" + msg.Text));
        mirror.OnMessage<ChatMsg>("chat", msg => received.Add("b:" + msg.Text));

        var chat = new ChatMsg { Text = "hi" };
        ((IPlayerMirrorClient)mirror).DispatchMessage("chat", chat.ToByteArray());

        Assert.Equal(2, received.Count);
        Assert.Contains("a:hi", received);
        Assert.Contains("b:hi", received);
    }

    [Fact]
    public void OnMessage_Unsubscribe_StopsReceiving()
    {
        var mirror = new MirrorSendTestMirrorClient();
        var received = new List<string>();
        var sub = mirror.OnMessage<ChatMsg>("chat", msg => received.Add(msg.Text));

        var chat = new ChatMsg { Text = "first" };
        ((IPlayerMirrorClient)mirror).DispatchMessage("chat", chat.ToByteArray());
        Assert.Single(received);

        sub.UnSubscribe();

        ((IPlayerMirrorClient)mirror).DispatchMessage("chat", new ChatMsg { Text = "second" }.ToByteArray());
        Assert.Single(received);
        Assert.Equal("first", received[0]);
    }

    [Fact]
    public void DispatchMessage_NoHandler_DoesNotThrow()
    {
        var mirror = new MirrorSendTestMirrorClient();
        ((IPlayerMirrorClient)mirror).DispatchMessage("chat", new ChatMsg { Text = "hi" }.ToByteArray());
    }

    [Fact]
    public void OnMessage_WrongSubject_NotDispatched()
    {
        var mirror = new MirrorSendTestMirrorClient();
        var received = false;
        mirror.OnMessage<ChatMsg>("chat", _ => received = true);

        ((IPlayerMirrorClient)mirror).DispatchMessage("other", new ChatMsg { Text = "hi" }.ToByteArray());

        Assert.False(received);
    }

    [Fact]
    public void SendMessage_InvokesOnSendHandler()
    {
        var mirror = new MirrorSendTestMirrorClient { PlayerId = 42 };
        (string subject, long playerId, byte[] data)? captured = null;
        ((IPlayerMirrorClient)mirror).OnSendMessage((s, pid, b) => captured = (s, pid, b));

        mirror.SendMessage("chat", new ChatMsg { Text = "hello from client" });

        Assert.NotNull(captured);
        Assert.Equal("chat", captured!.Value.subject);
        Assert.Equal(42L, captured.Value.playerId);
        var parsed = ChatMsg.Parser.ParseFrom(captured.Value.data);
        Assert.Equal("hello from client", parsed.Text);
    }

    [Fact]
    public void SendMessage_NoHandler_DoesNotThrow()
    {
        var mirror = new MirrorSendTestMirrorClient();
        mirror.SendMessage("chat", new ChatMsg { Text = "hi" });
    }
}

using Google.Protobuf;
using PubSubLib;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLibTest;

[MirrorProto(typeof(RemoveWatcherCmd))]
public partial class RemoveWatcherMirror
{
}

public class MirrorProtoTests
{
    [Fact]
    public void GetMirrorProto_Returns_Same_Instance()
    {
        var mirror = new RemoveWatcherMirror();
        var p1 = mirror.GetMirrorProto();
        var p2 = mirror.GetMirrorProto();
        Assert.Same(p1, p2);
    }

    [Fact]
    public void Commit_NotifiesOnChange_WithContent()
    {
        var mirror = new RemoveWatcherMirror();
        (byte[] data, string commit)? received = null;
        mirror.OnChange((bytes, commit) => received = (bytes, commit));

        mirror.WatcherId = 42;
        mirror.Commit("test_reason");
        MirrorProtoBus.Flush();

        Assert.NotNull(received);
        Assert.Equal("test_reason", received!.Value.commit);
        Assert.True(received!.Value.data.Length > 0);
    }

    [Fact]
    public void Commit_Notifies_MultipleHandlers()
    {
        var mirror = new RemoveWatcherMirror();
        int count = 0;
        mirror.OnChange((_, _) => count++);
        mirror.OnChange((_, _) => count++);

        mirror.WatcherId = 7;
        mirror.Commit("multi");
        MirrorProtoBus.Flush();

        Assert.Equal(2, count);
    }

    [Fact]
    public void Commit_AlwaysFires_EvenWithoutFieldChange()
    {
        var mirror = new RemoveWatcherMirror();
        bool called = false;
        mirror.OnChange((_, _) => called = true);

        mirror.Commit("no_change");
        MirrorProtoBus.Flush();

        Assert.True(called);
    }

    [Fact]
    public void Getter_ReturnsStoredValue()
    {
        var mirror = new RemoveWatcherMirror { WatcherId = 99 };

        Assert.Equal(99L, mirror.WatcherId);
    }

    [Fact]
    public void Commit_ByteArray_DeserializesCorrectly()
    {
        var mirror = new RemoveWatcherMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.WatcherId = 100;
        mirror.Commit("deser");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = RemoveWatcherCmd.Parser.ParseFrom(data);
        Assert.Equal(100L, parsed.WatcherId);
    }

    [Fact]
    public void DataName_Defaults_To_ProtoTypeName()
    {
        var mirror = new RemoveWatcherMirror();
        Assert.Equal("RemoveWatcherCmd", mirror.DataName);
    }

    [Fact]
    public void Implements_IPlayerData()
    {
        var mirror = new RemoveWatcherMirror() as IPlayerData;
        Assert.NotNull(mirror);
        ((IPlayerDataInternal)mirror!).SetPlayerId(42);
        Assert.Equal(42, mirror.PlayerId);
        var internalMirror = (IPlayerDataInternal)mirror;
        internalMirror.SetOnline(true);
        Assert.True(mirror.IsOnLine);
    }

    [Fact]
    public void DataName_Can_Be_Overridden()
    {
        var mirror = new CustomDataNameMirror();
        Assert.Equal("MyCustomName", mirror.DataName);
    }

    [Fact]
    public void Repeated_AddItems_Commit_SendsData()
    {
        var mirror = new BatchEnterMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.WatcherIds.Add(1);
        mirror.WatcherIds.Add(2);
        mirror.WatcherIds.Add(3);
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = BatchEnterMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1, 2, 3 }, parsed.WatcherIds);
    }

    [Fact]
    public void Repeated_NoChange_SkipsRepeatedCopy()
    {
        var mirror = new BatchEnterMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.WatcherIds.Add(42);
        mirror.Commit("first");
        MirrorProtoBus.Flush();

        mirror.Commit("second");
        MirrorProtoBus.Flush();

        var parsed = BatchEnterMsg.Parser.ParseFrom(data!);
        Assert.Equal(new long[] { 42 }, parsed.WatcherIds);
    }

    [Fact]
    public void Repeated_DirtyFlag_ResetsAfterCommit()
    {
        var mirror = new BatchEnterMirror();

        var list = mirror.WatcherIds;
        Assert.False(list.IsDirty);

        mirror.WatcherIds.Add(1);
        Assert.True(list.IsDirty);

        mirror.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(list.IsDirty);
    }

    [Fact]
    public void StructGroup_AddItem_Commit_SendsData()
    {
        var mirror = new StructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new StructTestMirror.Player(1, "ninh"));
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = StructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1 }, parsed.StructXPlayerXId);
        Assert.Equal(new string[] { "ninh" }, parsed.StructXPlayerXName);
    }

    [Fact]
    public void StructGroup_MultipleItems_Commit()
    {
        var mirror = new StructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new StructTestMirror.Player(1, "ninh"));
        mirror.Players.Add(new StructTestMirror.Player(2, "yen"));
        mirror.Players.Add(new StructTestMirror.Player(3, "bao"));
        mirror.Commit("multi");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = StructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1, 2, 3 }, parsed.StructXPlayerXId);
        Assert.Equal(new string[] { "ninh", "yen", "bao" }, parsed.StructXPlayerXName);
    }

    [Fact]
    public void StructGroup_DirtyFlag_ResetsAfterCommit()
    {
        var mirror = new StructTestMirror();

        var list = mirror.Players;
        Assert.False(list.IsDirty);

        mirror.Players.Add(new StructTestMirror.Player(1, "ninh"));
        Assert.True(list.IsDirty);

        mirror.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(list.IsDirty);
    }

    [Fact]
    public void Vector3Struct_NoChange_SkipsRepeated()
    {
        var mirror = new Vector3StructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new Vector3StructTestMirror.Player(1, "ninh", new Vector3[0]));
        mirror.Commit("first");
        MirrorProtoBus.Flush();

        mirror.Version = 99;
        mirror.Commit("second");
        MirrorProtoBus.Flush();

        var parsed = Vector3StructTestMsg.Parser.ParseFrom(data!);
        Assert.Equal(new long[] { 1 }, parsed.StructXPlayerXId);
        Assert.Equal(99, parsed.Version);
    }

    [Fact]
    public void Vector3SingleStruct_SetPosition_Commit_SendsThreeFloats()
    {
        var mirror = new Vector3SingleStructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new Vector3SingleStructTestMirror.Player(1, "ninh",
            new Vector3 { x = 1, y = 2, z = 3 }));
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = Vector3SingleStructTestMsg.Parser.ParseFrom(data!);
        Assert.Equal(new long[] { 1 }, parsed.StructXPlayerXId);
        Assert.Equal(new string[] { "ninh" }, parsed.StructXPlayerXName);
        Assert.Equal(new float[] { 1, 2, 3 }, parsed.StructXPlayerXPositionVector3);
    }

    [Fact]
    public void Vector3SingleStruct_MultiplePlayers_CommitsCorrectFloats()
    {
        var mirror = new Vector3SingleStructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new Vector3SingleStructTestMirror.Player(1, "ninh",
            new Vector3 { x = 1, y = 2, z = 3 }));
        mirror.Players.Add(new Vector3SingleStructTestMirror.Player(2, "yen",
            new Vector3 { x = 4, y = 5, z = 6 }));
        mirror.Commit("multi");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = Vector3SingleStructTestMsg.Parser.ParseFrom(data!);
        Assert.Equal(new long[] { 1, 2 }, parsed.StructXPlayerXId);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, parsed.StructXPlayerXPositionVector3);
    }

    [Fact]
    public void Vector3SingleStruct_DirtyFlag_ResetsAfterCommit()
    {
        var mirror = new Vector3SingleStructTestMirror();

        var list = mirror.Players;
        Assert.False(list.IsDirty);

        mirror.Players.Add(new Vector3SingleStructTestMirror.Player(1, "ninh",
            new Vector3 { x = 1, y = 2, z = 3 }));
        Assert.True(list.IsDirty);

        mirror.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(list.IsDirty);
    }

    [Fact]
    public void Vector3_Set_Position_Commit_SendsData()
    {
        var mirror = new Vector3TestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Position = new Vector3 { x = 1.5f, y = 2.5f, z = 3.5f };
        mirror.Commit("vec");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = Vector3TestMsg.Parser.ParseFrom(data);
        Assert.Equal(new float[] { 1.5f, 2.5f, 3.5f }, parsed.PositionVector3);
    }

    [Fact]
    public void Vector3List_AddItems_Commit_SendsData()
    {
        var mirror = new Vector3TestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Waypoints.Add(new Vector3 { x = 1, y = 2, z = 3 });
        mirror.Waypoints.Add(new Vector3 { x = 4, y = 5, z = 6 });
        mirror.Commit("list");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = Vector3TestMsg.Parser.ParseFrom(data);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, parsed.WaypointsVector3S);
    }

    [Fact]
    public void Vector3List_DirtyFlag_ResetsAfterCommit()
    {
        var mirror = new Vector3TestMirror();

        var list = mirror.Waypoints;
        Assert.False(list.IsDirty);

        mirror.Waypoints.Add(new Vector3 { x = 1, y = 2, z = 3 });
        Assert.True(list.IsDirty);

        mirror.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(list.IsDirty);
    }

    [Fact]
    public void Vector3List_NoChange_SkipsRepeated()
    {
        var mirror = new Vector3TestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Waypoints.Add(new Vector3 { x = 1, y = 2, z = 3 });
        mirror.Commit("first");
        MirrorProtoBus.Flush();

        mirror.Version = 99;
        mirror.Commit("second");
        MirrorProtoBus.Flush();

        var parsed = Vector3TestMsg.Parser.ParseFrom(data!);
        Assert.Equal(new float[] { 1, 2, 3 }, parsed.WaypointsVector3S);
        Assert.Equal(99, parsed.Version);
    }

    [Fact]
    public void Vector3Struct_SetPosition_Commit_SendsValueAndCount()
    {
        var mirror = new Vector3StructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new Vector3StructTestMirror.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 }, new Vector3 { x = 4, y = 5, z = 6 } }));
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = Vector3StructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1 }, parsed.StructXPlayerXId);
        Assert.Equal(new string[] { "ninh" }, parsed.StructXPlayerXName);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, parsed.StructXPlayerXPositionVector3SValue);
        Assert.Equal(new int[] { 2 }, parsed.StructXPlayerXPositionVector3SCount);
    }

    [Fact]
    public void Vector3Struct_MultiplePlayersWithDifferentCounts()
    {
        var mirror = new Vector3StructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new Vector3StructTestMirror.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        mirror.Players.Add(new Vector3StructTestMirror.Player(2, "yen",
            new Vector3[] { new Vector3 { x = 4, y = 5, z = 6 }, new Vector3 { x = 7, y = 8, z = 9 } }));
        mirror.Players.Add(new Vector3StructTestMirror.Player(3, "bao",
            new Vector3[] { new Vector3 { x = 10, y = 11, z = 12 }, new Vector3 { x = 13, y = 14, z = 15 }, new Vector3 { x = 16, y = 17, z = 18 } }));
        mirror.Commit("multi");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = Vector3StructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1, 2, 3 }, parsed.StructXPlayerXId);
        Assert.Equal(new string[] { "ninh", "yen", "bao" }, parsed.StructXPlayerXName);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 }, parsed.StructXPlayerXPositionVector3SValue);
        Assert.Equal(new int[] { 1, 2, 3 }, parsed.StructXPlayerXPositionVector3SCount);
    }

    [Fact]
    public void Vector3Struct_EmptyPositionArray_CommitsCountZero()
    {
        var mirror = new Vector3StructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new Vector3StructTestMirror.Player(1, "ninh", new Vector3[0]));
        mirror.Commit("empty");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = Vector3StructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1 }, parsed.StructXPlayerXId);
        Assert.Equal(new int[] { 0 }, parsed.StructXPlayerXPositionVector3SCount);
        Assert.Empty(parsed.StructXPlayerXPositionVector3SValue);
    }

    [Fact]
    public void Vector3Struct_DirtyFlag_ResetsAfterCommit()
    {
        var mirror = new Vector3StructTestMirror();

        var list = mirror.Players;
        Assert.False(list.IsDirty);

        mirror.Players.Add(new Vector3StructTestMirror.Player(1, "ninh", new Vector3[0]));
        Assert.True(list.IsDirty);

        mirror.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(list.IsDirty);
    }

    [Fact]
    public void SendMessage_SerializesDataCorrectly()
    {
        var mirror = new MirrorSendTestMirror();
        (string subject, byte[] data)? received = null;
        ((IPlayerData)mirror).OnMessage((s, b) => received = (s, b));

        mirror.Version = 42;
        mirror.Commit("commit");
        MirrorProtoBus.Flush();

        var chat = new ChatMsg { Text = "hello" };
        mirror.SendMessage("chat", chat);
        MirrorProtoBus.Flush();

        Assert.NotNull(received);
        Assert.Equal("chat", received!.Value.subject);
        var parsed = ChatMsg.Parser.ParseFrom(received.Value.data);
        Assert.Equal("hello", parsed.Text);
    }

    [Fact]
    public void SendMessage_MultipleMessages_SerializedInOrder()
    {
        using var _ = MirrorProtoBus.SuppressBackground();
        var mirror = new MirrorSendTestMirror();
        var received = new System.Collections.Generic.List<(string subject, byte[] data)>();
        ((IPlayerData)mirror).OnMessage((s, b) => received.Add((s, b)));

        mirror.SendMessage("first", new ChatMsg { Text = "one" });
        mirror.SendMessage("second", new ChatMsg { Text = "two" });
        MirrorProtoBus.Flush();

        Assert.Equal(2, received.Count);
        Assert.Equal("first", received[0].subject);
        Assert.Equal("one", ChatMsg.Parser.ParseFrom(received[0].data).Text);
        Assert.Equal("second", received[1].subject);
        Assert.Equal("two", ChatMsg.Parser.ParseFrom(received[1].data).Text);
    }

    [Fact]
    public void SendMessage_NoHandler_DoesNotThrow()
    {
        var mirror = new MirrorSendTestMirror();
        mirror.SendMessage("chat", new ChatMsg { Text = "hi" });
        MirrorProtoBus.Flush();
    }

    [Fact]
    public void OnMessage_ReceivesAndDeserializes()
    {
        var mirror = new MirrorSendTestMirror();
        ChatMsg? received = null;
        mirror.OnMessage<ChatMsg>("chat", msg => received = msg);

        var chat = new ChatMsg { Text = "hello world" };
        var actions = ((IPlayerDataInternal)mirror).PrepareMessageDispatch("chat", chat.ToByteArray());

        Assert.Single(actions);
        actions[0]();
        Assert.NotNull(received);
        Assert.Equal("hello world", received!.Text);
    }

    [Fact]
    public void OnMessage_MultipleHandlers_AllNotified()
    {
        var mirror = new MirrorSendTestMirror();
        var received = new System.Collections.Concurrent.ConcurrentBag<string>();
        mirror.OnMessage<ChatMsg>("chat", msg => received.Add("a:" + msg.Text));
        mirror.OnMessage<ChatMsg>("chat", msg => received.Add("b:" + msg.Text));

        var chat = new ChatMsg { Text = "hi" };
        var actions = ((IPlayerDataInternal)mirror).PrepareMessageDispatch("chat", chat.ToByteArray());

        Assert.Equal(2, actions.Count);
        foreach (var a in actions) a();
        Assert.Equal(2, received.Count);
        Assert.Contains("a:hi", received);
        Assert.Contains("b:hi", received);
    }

    [Fact]
    public void OnMessage_Unsubscribe_StopsReceiving()
    {
        var mirror = new MirrorSendTestMirror();
        var received = new List<string>();
        var sub = mirror.OnMessage<ChatMsg>("chat", msg => received.Add(msg.Text));

        var chat = new ChatMsg { Text = "first" };
        var actions = ((IPlayerDataInternal)mirror).PrepareMessageDispatch("chat", chat.ToByteArray());
        Assert.Single(actions);
        actions[0]();

        sub.Dispose();

        actions = ((IPlayerDataInternal)mirror).PrepareMessageDispatch("chat", new ChatMsg { Text = "second" }.ToByteArray());
        Assert.Empty(actions);
        Assert.Single(received);
        Assert.Equal("first", received[0]);
    }

    [Fact]
    public void PrepareMessageDispatch_NoHandler_ReturnsEmpty()
    {
        var mirror = new MirrorSendTestMirror();
        var actions = ((IPlayerDataInternal)mirror).PrepareMessageDispatch("chat", new ChatMsg { Text = "hi" }.ToByteArray());
        Assert.Empty(actions);
    }

    [Fact]
    public void PrepareMessageDispatch_WrongSubject_ReturnsEmpty()
    {
        var mirror = new MirrorSendTestMirror();
        mirror.OnMessage<ChatMsg>("chat", _ => { });

        var actions = ((IPlayerDataInternal)mirror).PrepareMessageDispatch("other", new ChatMsg { Text = "hi" }.ToByteArray());

        Assert.Empty(actions);
    }

    [Fact]
    public void PrimitiveArray_AddItem_Commit_SendsData()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "ninh", new long[] { 10, 20 }, new float[] { 0.5f, 1.0f }));
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = PrimitiveArrayStructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1 }, parsed.StructXPlayerXId);
        Assert.Equal(new string[] { "ninh" }, parsed.StructXPlayerXName);
        Assert.Equal(new long[] { 10, 20 }, parsed.StructXPlayerXBeanArrayValue);
        Assert.Equal(new int[] { 2 }, parsed.StructXPlayerXBeanArrayCount);
        Assert.Equal(new float[] { 0.5f, 1.0f }, parsed.StructXPlayerXScoresArrayValue);
        Assert.Equal(new int[] { 2 }, parsed.StructXPlayerXScoresArrayCount);
    }

    [Fact]
    public void PrimitiveArray_MultipleItems_Commit()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "a", new long[] { 1 }, new float[] { 0.1f }));
        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(2, "b", new long[] { 2, 3 }, new float[] { 0.2f, 0.3f }));
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = PrimitiveArrayStructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1, 2 }, parsed.StructXPlayerXId);
        Assert.Equal(new string[] { "a", "b" }, parsed.StructXPlayerXName);
        Assert.Equal(new long[] { 1, 2, 3 }, parsed.StructXPlayerXBeanArrayValue);
        Assert.Equal(new int[] { 1, 2 }, parsed.StructXPlayerXBeanArrayCount);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, parsed.StructXPlayerXScoresArrayValue);
        Assert.Equal(new int[] { 1, 2 }, parsed.StructXPlayerXScoresArrayCount);
    }

    [Fact]
    public void PrimitiveArray_EmptyArray_Commit_CountZero()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "x", System.Array.Empty<long>(), new float[] { 0.5f }));
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = PrimitiveArrayStructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1 }, parsed.StructXPlayerXId);
        Assert.Empty(parsed.StructXPlayerXBeanArrayValue);
        Assert.Equal(new int[] { 0 }, parsed.StructXPlayerXBeanArrayCount);
    }

    [Fact]
    public void PrimitiveArray_NullArray_Commit_CountZero()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "x", null!, new float[] { 0.5f }));
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = PrimitiveArrayStructTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new int[] { 0 }, parsed.StructXPlayerXBeanArrayCount);
        Assert.Empty(parsed.StructXPlayerXBeanArrayValue);
    }

    [Fact]
    public void PrimitiveArray_DirtyFlag_ResetsAfterCommit()
    {
        var mirror = new PrimitiveArrayStructTestMirror();

        var list = mirror.Players;
        Assert.False(list.IsDirty);

        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "x", new long[] { 1 }, new float[] { 0.1f }));
        Assert.True(list.IsDirty);

        mirror.Commit("add");
        MirrorProtoBus.Flush();
        Assert.False(list.IsDirty);
    }

    [Fact]
    public void PrimitiveArray_ChangeElement_MarksListDirty()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "ninh", [10, 20], [0.5f]));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players[0].Bean[0] = 99;
        Assert.True(mirror.Players.IsDirty);
    }

    [Fact]
    public void PrimitiveArray_ChangeElement_Commit_SendsUpdatedValue()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "ninh", new long[] { 10, 20 }, new float[] { 0.5f }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();

        mirror.Players[0].Bean[0] = 99;

        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);
        mirror.Commit("update");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = PrimitiveArrayStructTestMsg.Parser.ParseFrom(data!);
        Assert.Equal(new long[] { 99, 20 }, parsed.StructXPlayerXBeanArrayValue);
        Assert.Equal(new int[] { 2 }, parsed.StructXPlayerXBeanArrayCount);
    }

    [Fact]
    public void PrimitiveArray_SameValue_DoesNotMarkDirty()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "ninh", new long[] { 10, 20 }, new float[] { 0.5f }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players[0].Bean[0] = 10;
        Assert.False(mirror.Players.IsDirty);
    }

    [Fact]
    public void PrimitiveArray_AddElement_MarksListDirty()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "ninh", new long[] { 10 }, new float[] { 0.5f }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players[0].Bean.Add(99);
        Assert.True(mirror.Players.IsDirty);
    }

    [Fact]
    public void PrimitiveArrayStruct_ChangeArrayElement_MarksListDirty()
    {
        var mirror = new PrimitiveArrayStructTestMirror();
        mirror.Players.Add(new PrimitiveArrayStructTestMirror.Player(1, "ninh", new long[] { 10, 20 }, new float[] { 0.5f }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        var p = mirror.Players[0];
        p.Bean[0] = 99;
        Assert.True(mirror.Players.IsDirty);
        mirror.Players[0] = p;
    }

    [Fact]
    public void ClassGroup_Add_MarksListDirty()
    {
        var mirror = new ClassTestMirror();
        var list = mirror.Players;
        Assert.False(list.IsDirty);

        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh", new Vector3[0]));
        Assert.True(list.IsDirty);
    }

    [Fact]
    public void ClassGroup_ChangeProperty_MarksListDirty()
    {
        var mirror = new ClassTestMirror();
        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh", new Vector3[0]));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players[0].Name = "changed";
        Assert.True(mirror.Players.IsDirty);
    }

    [Fact]
    public void ClassGroup_ChangePropertySameValue_DoesNotMarkDirty()
    {
        var mirror = new ClassTestMirror();
        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh", new Vector3[0]));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players[0].Name = "ninh";
        Assert.False(mirror.Players.IsDirty);
    }

    [Fact]
    public void ClassGroup_AddItem_Commit_SendsData()
    {
        var mirror = new ClassTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh", new Vector3[0]));
        mirror.Commit("add");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = ClassTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1 }, parsed.ClassXPlayerXId);
        Assert.Equal(new string[] { "ninh" }, parsed.ClassXPlayerXName);
    }

    [Fact]
    public void ClassGroup_ChangeProperty_Commit_SendsUpdatedValue()
    {
        var mirror = new ClassTestMirror();
        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh", new Vector3[0]));
        mirror.Commit("init");
        MirrorProtoBus.Flush();

        mirror.Players[0].Id = 99;
        mirror.Players[0].Name = "updated";

        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);
        mirror.Commit("update");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = ClassTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 99 }, parsed.ClassXPlayerXId);
        Assert.Equal(new string[] { "updated" }, parsed.ClassXPlayerXName);
    }

    [Fact]
    public void ClassGroup_DirtyFlag_ResetsAfterCommit()
    {
        var mirror = new ClassTestMirror();
        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh", new Vector3[0]));
        Assert.True(mirror.Players.IsDirty);

        mirror.Commit("test");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);
    }

    [Fact]
    public void ClassGroup_MultipleItems_Commit()
    {
        var mirror = new ClassTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh", new Vector3[0]));
        mirror.Players.Add(new ClassTestMirror.Player(2, "yen", new Vector3[0]));
        mirror.Players.Add(new ClassTestMirror.Player(3, "bao", new Vector3[0]));
        mirror.Commit("multi");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = ClassTestMsg.Parser.ParseFrom(data);
        Assert.Equal(new long[] { 1, 2, 3 }, parsed.ClassXPlayerXId);
        Assert.Equal(new string[] { "ninh", "yen", "bao" }, parsed.ClassXPlayerXName);
    }

    [Fact]
    public void ClassGroup_Remove_DetachesDirtyMarker()
    {
        var mirror = new ClassTestMirror();
        var player = new ClassTestMirror.Player(1, "ninh", new Vector3[0]);
        mirror.Players.Add(player);
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players.Remove(player);
        mirror.Commit("remove");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        player.Name = "after_remove";
        Assert.False(mirror.Players.IsDirty);
    }

    [Fact]
    public void ClassGroup_Clear_DetachesAllDirtyMarkers()
    {
        var mirror = new ClassTestMirror();
        var p1 = new ClassTestMirror.Player(1, "ninh", new Vector3[0]);
        var p2 = new ClassTestMirror.Player(2, "yen", new Vector3[0]);
        mirror.Players.Add(p1);
        mirror.Players.Add(p2);
        mirror.Commit("init");
        MirrorProtoBus.Flush();

        mirror.Players.Clear();
        mirror.Commit("clear");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        p1.Name = "after_clear";
        p2.Name = "after_clear";
        Assert.False(mirror.Players.IsDirty);
    }

    [Fact]
    public void ClassGroup_ChangeVector3Array_MarksListDirty()
    {
        var mirror = new ClassTestMirror();
        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players[0].Position[0] = new Vector3 { x = 9, y = 8, z = 7 };
        Assert.True(mirror.Players.IsDirty);
    }

    [Fact]
    public void ClassGroup_ChangeVector3Array_Commit_SendsUpdatedValue()
    {
        var mirror = new ClassTestMirror();
        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();

        mirror.Players[0].Position[0] = new Vector3 { x = 9, y = 8, z = 7 };

        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);
        mirror.Commit("update");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = ClassTestMsg.Parser.ParseFrom(data!);
        Assert.Equal(new float[] { 9, 8, 7 }, parsed.ClassXPlayerXPositionVector3SValue);
        Assert.Equal(new int[] { 1 }, parsed.ClassXPlayerXPositionVector3SCount);
    }

    [Fact]
    public void ClassGroup_ChangeVector3ArraySameValue_DoesNotMarkDirty()
    {
        var mirror = new ClassTestMirror();
        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players[0].Position[0] = new Vector3 { x = 1, y = 2, z = 3 };
        Assert.False(mirror.Players.IsDirty);
    }

    [Fact]
    public void ClassGroup_Vector3Add_MarksListDirty()
    {
        var mirror = new ClassTestMirror();
        mirror.Players.Add(new ClassTestMirror.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        mirror.Players[0].Position.Add(new Vector3 { x = 4, y = 5, z = 6 });
        Assert.True(mirror.Players.IsDirty);
    }

    [Fact]
    public void Vector3Struct_ChangeArrayElement_MarksListDirty()
    {
        var mirror = new Vector3StructTestMirror();
        mirror.Players.Add(new Vector3StructTestMirror.Player(1, "ninh",
            new Vector3[] { new Vector3 { x = 1, y = 2, z = 3 } }));
        mirror.Commit("init");
        MirrorProtoBus.Flush();
        Assert.False(mirror.Players.IsDirty);

        var p = mirror.Players[0];
        p.Position[0] = new Vector3 { x = 9, y = 8, z = 7 };
        Assert.True(mirror.Players.IsDirty);
        mirror.Players[0] = p;

        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);
        mirror.Commit("update");
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        var parsed = Vector3StructTestMsg.Parser.ParseFrom(data!);
        Assert.Equal(new float[] { 9, 8, 7 }, parsed.StructXPlayerXPositionVector3SValue);
        Assert.Equal(new int[] { 1 }, parsed.StructXPlayerXPositionVector3SCount);
    }

}

[MirrorProto(typeof(RemoveWatcherCmd), DataName = "MyCustomName")]
public partial class CustomDataNameMirror
{
}

[MirrorProto(typeof(BatchEnterMsg))]
public partial class BatchEnterMirror
{
}

[MirrorProto(typeof(StructTestMsg))]
public partial class StructTestMirror
{
}

[MirrorProto(typeof(Vector3TestMsg))]
public partial class Vector3TestMirror
{
}

[MirrorProto(typeof(Vector3StructTestMsg))]
public partial class Vector3StructTestMirror
{
}

[MirrorProto(typeof(Vector3SingleStructTestMsg))]
public partial class Vector3SingleStructTestMirror
{
}

[MirrorProto(typeof(MirrorSendTestMsg))]
public partial class MirrorSendTestMirror
{
}

[MirrorProto(typeof(PrimitiveArrayStructTestMsg))]
public partial class PrimitiveArrayStructTestMirror
{
}

[MirrorProto(typeof(ClassTestMsg))]
public partial class ClassTestMirror
{
}

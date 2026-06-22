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
    public void StructGroup_NoChange_SkipsRepeated()
    {
        var mirror = new StructTestMirror();
        byte[]? data = null;
        mirror.OnChange((bytes, _) => data = bytes);

        mirror.Players.Add(new StructTestMirror.Player(42, "ninh"));
        mirror.Commit("first");
        MirrorProtoBus.Flush();

        mirror.Version = 99;
        mirror.Commit("second");
        MirrorProtoBus.Flush();

        var parsed = StructTestMsg.Parser.ParseFrom(data!);
        Assert.Equal(new long[] { 42 }, parsed.StructXPlayerXId);
        Assert.Equal(99, parsed.Version);
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

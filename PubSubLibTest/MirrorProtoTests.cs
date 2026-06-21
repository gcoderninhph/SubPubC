using PubSubLib;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLibTest;

[MirrorProto(typeof(RemoveWatcherCmd))]
public partial class RemoveWatcherMirror
{
    private long _watcherId;
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
    public void SetProperty_NotifiesOnChange()
    {
        var mirror = new RemoveWatcherMirror();
        byte[]? data = null;
        mirror.OnChange(bytes => data = bytes);

        mirror.WatcherId = 42;
        MirrorProtoBus.Flush();

        Assert.NotNull(data);
        Assert.True(data!.Length > 0);
    }

    [Fact]
    public void OnChange_MultipleHandlers()
    {
        var mirror = new RemoveWatcherMirror();
        int count = 0;
        mirror.OnChange(_ => count++);
        mirror.OnChange(_ => count++);

        mirror.WatcherId = 7;
        MirrorProtoBus.Flush();

        Assert.Equal(2, count);
    }

    [Fact]
    public void OnChange_NotCalled_WhenNoChange()
    {
        var mirror = new RemoveWatcherMirror();
        bool called = false;
        mirror.OnChange(_ => called = true);

        MirrorProtoBus.Flush();

        Assert.False(called);
    }

    [Fact]
    public void Getter_ReturnsStoredValue()
    {
        var mirror = new RemoveWatcherMirror { WatcherId = 99 };

        MirrorProtoBus.Flush();

        Assert.Equal(99L, mirror.WatcherId);
    }

    [Fact]
    public void ByteArray_DeserializesCorrectly()
    {
        var mirror = new RemoveWatcherMirror();
        byte[]? data = null;
        mirror.OnChange(bytes => data = bytes);

        mirror.WatcherId = 100;
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
        mirror!.PlayerId = 42;
        Assert.Equal(42, mirror.PlayerId);
        mirror.IsOnLine = true;
        Assert.True(mirror.IsOnLine);
        MirrorProtoBus.Flush();
    }

    [Fact]
    public void DataName_Can_Be_Overridden()
    {
        var mirror = new CustomDataNameMirror();
        Assert.Equal("MyCustomName", mirror.DataName);
    }
}

[MirrorProto(typeof(RemoveWatcherCmd), DataName = "MyCustomName")]
public partial class CustomDataNameMirror
{
    private long _watcherId;
}

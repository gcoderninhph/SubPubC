using Google.Protobuf;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLibTest;

[MirrorProtoClient(typeof(RemoveWatcherCmd))]
public partial class RemoveWatcherMirrorClient
{
    private long _watcherId;

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
}

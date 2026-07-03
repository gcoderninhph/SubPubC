using Google.Protobuf;
using MyConnection;
using Natify;
using PubSubLib;
using PubSubLib.Client;
using PubSubLib.Messages;
using PubSubLib.Mirror;
using PubSubLib.Router;

namespace PubSubLibTest;

[MirrorProto(typeof(RemoveWatcherCmd), DataName = "rm")]
public partial class TestPlayerData
{
}

[MirrorProtoClient(typeof(RemoveWatcherCmd), DataName = "rm")]
public partial class TestPlayerDataClient
{
    public string? LastCommit { get; private set; }
    partial void OnCommit(string commit) => LastCommit = commit;

    partial void OnStart()
    {
        Console.WriteLine(LastCommit);
    }
}

public class PlayerSpeaksTestAll : IDisposable
{
    private const string NatsUrl = "nats://localhost:4222";

    private NatifyServer _natifyServer;
    private NatifyClientFast _natifyClient;
    private IServer _serverConn;
    private IPlayerSpeaksManager _manager;

    private void CreateRouter()
    {
        _serverConn = IServer.Create(new ServerConfig
        {
            restEndpoint = "/api",
            websocketEndpoint = "/ws",
            restCompressedEnable = true,
            udpPort = 9091,
            tcpPort = 9090,
            jwtSecret = "super-secret-jwt-key-for-testing-42",
            jwtAudience = "test-audience",
            jwtIssuer = "test-issuer",
        });

        _natifyServer = new NatifyServer(NatsUrl, "SyncRouter", "SyncGroup", "SyncServer");
        _serverConn.OnLogin<StringValue>(body =>
        {
            var spl = body.Value.Split('_');
            var user = spl[0];
            var id = spl[1];
            return Task.FromResult<IUser>(new Player(id, user));
        });
        _serverConn.AddModule(IPlayerSpeaksRouterModule.Create(_natifyServer, "VN"));
    }

    private async Task CreateServerAsync()
    {
        _natifyClient = new NatifyClientFast(NatsUrl, "SyncServer", "ServerGroup", "VN", "SyncRouter");
        _manager = IPlayerSpeaksManager.Create(new PlayerSpeakerConfig
        {
            ClientFast = _natifyClient,
            PlayerTimeoutSeconds = 10,
            PlayerCleanupIntervalSeconds = 5
        });
        await Task.Delay(2000);
    }

    private static async Task<PlayerSpeaksClientHandle> CreateClientAsync(string user, long playerId)
    {
        var client = IClient.Create(new ClientConfig
        {
            tcpServer = "127.0.0.1:9090",
            websocketEnpoint = "/ws",
            restEndpoint = "/api",
            restCompressedEnable = true,
            tcpSecurity = false,
            udpServer = "127.0.0.1:9091",
            udpPingIntervalMs = 5000,
            udpPingTimeoutMs = 15000
        });

        var module = IPlayerSpeaksClientModule.Create();
        client.AddModule(module);
        await client.Login(() => new StringValue { Value = $"{user}_{playerId}" });
        await client.ConnectServer();
        return new PlayerSpeaksClientHandle(client, module);
    }

    public void Dispose()
    {
        _serverConn?.DisposeAsync().GetAwaiter().GetResult();
        _manager?.Dispose();
        _natifyServer?.Dispose();
        _natifyClient?.Dispose();
    }

    private sealed record PlayerSpeaksClientHandle(IClient Client, IPlayerSpeaksClientModule Module) : IDisposable
    {
        public IPlayerSpeaksClient ClientData => Module.Get();

        public void Dispose()
        {
            Module.Dispose();
            Client.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task FullStack_ServerChangesData_ClientReceivesUpdate()
    {
        var playerId = 1L;

        CreateRouter();
        await CreateServerAsync();

        var serverData = _manager.CreateData<TestPlayerData>(playerId);
        serverData.WatcherId = 111L;

        using var handle = await CreateClientAsync("test", playerId);

        handle.ClientData.AddData<TestPlayerDataClient>();
        var clientData = handle.ClientData.GetData<TestPlayerDataClient>();
        await Task.Delay(2000);

        Assert.Equal(111L, clientData!.WatcherId);

        await Task.Delay(2000);

        serverData.WatcherId = 999;
        serverData.Commit("update_watcher");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (clientData!.WatcherId == 999)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(999L, clientData!.WatcherId);
    }

    [Fact]
    public async Task FullStack_ClientReceivesCommitMessage()
    {
        var playerId = 3L;

        CreateRouter();
        await CreateServerAsync();

        var serverData = _manager.CreateData<TestPlayerData>(playerId);

        using var handle = await CreateClientAsync("ctest", playerId);

        handle.ClientData.AddData<TestPlayerDataClient>();
        var clientData = handle.ClientData.GetData<TestPlayerDataClient>();

        await Task.Delay(2000);
        Assert.True(serverData.IsOnLine, "serverIsOnLine");

        serverData.WatcherId = 777;
        serverData.Commit("battle_won");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (clientData!.WatcherId == 777)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(777L, clientData!.WatcherId);

        await Task.Delay(500);

        Assert.NotNull(clientData.LastCommit);
        Assert.Equal("battle_won", clientData.LastCommit);
    }

    [Fact]
    public async Task FullStack_PlayerDataIsolated_OtherPlayerNotAffected()
    {
        var player1Id = 10L;
        var player2Id = 20L;

        CreateRouter();
        await CreateServerAsync();

        var serverData1 = _manager.CreateData<TestPlayerData>(player1Id);
        var serverData2 = _manager.CreateData<TestPlayerData>(player2Id);

        // Player 1
        using var handle1 = await CreateClientAsync("p1", player1Id);
        handle1.ClientData.AddData<TestPlayerDataClient>();
        var clientData1 = handle1.ClientData.GetData<TestPlayerDataClient>();

        // Player 2
        using var handle2 = await CreateClientAsync("p2", player2Id);
        handle2.ClientData.AddData<TestPlayerDataClient>();
        var clientData2 = handle2.ClientData.GetData<TestPlayerDataClient>();

        await Task.Delay(2000);

        // Player 1 commits
        serverData1.WatcherId = 111;
        serverData1.Commit("p1_update");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (clientData1!.WatcherId == 111)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(111L, clientData1!.WatcherId);
        Assert.NotEqual(111L, clientData2!.WatcherId);

        // Player 2 commits
        serverData2.WatcherId = 222;
        serverData2.Commit("p2_update");

        deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (clientData2!.WatcherId == 222)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(222L, clientData2!.WatcherId);
        Assert.NotEqual(222L, clientData1!.WatcherId);
    }

    [Fact]
    public async Task ClientOnline_StatusReflectedOnServer()
    {
        var playerId = 50L;

        CreateRouter();
        await CreateServerAsync();

        var serverData = _manager.CreateData<TestPlayerData>(playerId);
        Assert.False(serverData.IsOnLine);

        using var handle = await CreateClientAsync("onlineTest", playerId);
        await Task.Delay(2000);

        Assert.True(serverData.IsOnLine);

        await handle.Client.DisconnectAsync();
        await Task.Delay(2000);

        Assert.False(serverData.IsOnLine);
    }

    [Fact]
    public async Task FullStack_SendMessage_ServerToClient()
    {
        var playerId = 1L;
        var signal = new ManualResetEventSlim();
        ChatMsg? received = null;

        CreateRouter();
        await CreateServerAsync();

        // pass 1: verify mirror client khớp mirror server
        var serverData = _manager.CreateData<MirrorSendTestMirror>(playerId);

        using var handle = await CreateClientAsync("test", playerId);
        handle.ClientData.AddData<MirrorSendTestMirrorClient>();
        var clientData = handle.ClientData.GetData<MirrorSendTestMirrorClient>();

        await Task.Delay(2000);

        Assert.NotNull(clientData);
        Assert.Equal(playerId, clientData!.PlayerId);
        Assert.Equal("MirrorSendTestMsg", clientData.DataName);

        // pass 2: gửi ChatMsg → client nhận đúng nội dung
        clientData.OnMessage<ChatMsg>("chat", msg =>
        {
            received = msg;
            signal.Set();
        });

        serverData.SendMessage("chat", new ChatMsg { Text = "hello from server" });
        MirrorProtoBus.Flush();

        Assert.True(signal.Wait(10000), "Client did not receive SendMessage");
        Assert.NotNull(received);
        Assert.Equal("hello from server", received!.Text);
    }

    [Fact]
    public async Task FullStack_SendMessage_ClientToServer()
    {
        var playerId = 2L;
        var signal = new ManualResetEventSlim();
        ChatMsg? received = null;

        CreateRouter();
        await CreateServerAsync();

        var serverData = _manager.CreateData<MirrorSendTestMirror>(playerId);
        serverData.OnMessage<ChatMsg>("chat", msg =>
        {
            received = msg;
            signal.Set();
        });

        using var handle = await CreateClientAsync("test2", playerId);
        handle.ClientData.AddData<MirrorSendTestMirrorClient>();
        var clientData = handle.ClientData.GetData<MirrorSendTestMirrorClient>();

        await Task.Delay(2000);

        Assert.NotNull(clientData);
        Assert.Equal(playerId, clientData!.PlayerId);

        clientData.SendMessage("chat", new ChatMsg { Text = "hello from client" });
        await Task.Delay(500);
        _manager.Tick();

        Assert.True(signal.Wait(10000), "Server did not receive SendMessage");
        Assert.NotNull(received);
        Assert.Equal("hello from client", received!.Text);
    }

    [Fact]
    public async Task FullStack_OnDefault_AutoCreatesDataOnClientConnect()
    {
        var playerId = 99L;

        CreateRouter();
        await CreateServerAsync();

        _manager.OnDefault<TestPlayerData>(data =>
        {
            data.WatcherId = 42;
            data.Commit("default_init");
            return Task.CompletedTask;
        });

        await Task.Delay(3000);

        using var handle = await CreateClientAsync("defaultTest", playerId);

        handle.ClientData.AddData<TestPlayerDataClient>();
        var clientData = handle.ClientData.GetData<TestPlayerDataClient>();
        Assert.NotNull(clientData);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (clientData!.WatcherId == 42)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(42L, clientData!.WatcherId);
        Assert.Equal("default_init", clientData.LastCommit);
    }

    [Fact]
    public async Task FullStack_RemoveAsync_RejectsOnline_RemovesOffline_FiresOnRemove()
    {
        var onlinePlayerId = 99L;
        var offlinePlayerId = 100L;

        CreateRouter();
        await CreateServerAsync();

        var dataOnline = _manager.CreateData<TestPlayerData>(onlinePlayerId);
        var dataOffline = _manager.CreateData<TestPlayerData>(offlinePlayerId);

        TestPlayerData? removedData = null;
        _manager.OnRemove<TestPlayerData>(async data =>
        {
            removedData = data;
            await Task.CompletedTask;
        });

        using var handle = await CreateClientAsync("onlineUser", onlinePlayerId);
        await Task.Delay(2000);

        var resultOnline = await _manager.RemoveAsync(onlinePlayerId);
        var resultOffline = await _manager.RemoveAsync(offlinePlayerId);
        Assert.False(resultOnline);
        Assert.True(resultOffline);

        Assert.NotNull(removedData);
        Assert.Same(dataOffline, removedData);

        var afterOnline = _manager.GetData<TestPlayerData>(onlinePlayerId);
        var afterOffline = _manager.GetData<TestPlayerData>(offlinePlayerId);
        Assert.NotNull(afterOnline);
        Assert.Null(afterOffline);
    }

    internal class Player(string id, string name) : IUser
    {
        public string Name => name;
        public string Id => id;
    }
}
using MyConnection;
using Natify;
using PubSubLib;
using PubSubLib.Client;
using PubSubLib.Messages;
using PubSubLib.Router;
using Vector2 = PubSubLib.Vector2;


namespace PubSubLibTest;

// ===== Real Natify Integration Tests =====
public class PubSubTestAll : IDisposable
{
    private const string NatsUrl = "nats://localhost:4222";

    private NatifyServer _natifyServer;
    private NatifyClientFast _natifyClient;
    private IClient _clientConn;
    private IServer _serverConn;
    private IPubSub _pubSub;
    private IPubSubClientModule _pubSubClientModule;

    private static readonly Dictionary<long, GameObjectTest> clientUnit = new();
    private static readonly Dictionary<long, MyUnit> serverUnit = new();

    private static Vector2 V(float x, float y) => new Vector2 { x = x, y = y };

    private async Task CreateUnityClient(string user, long id)
    {
        _clientConn = IClient.Create(new ClientConfig
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

        _pubSubClientModule = IPubSubClientModule.Create(new Config
        {
            PingIntervalMs = 300
        });

        _pubSubClientModule.Get().AddProvider(new Provider());

        _clientConn.AddModule(_pubSubClientModule);
        await _clientConn.Login(() => new StringValue { Value = $"{user}_{id}" });
        await _clientConn.ConnectServer();
    }

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
        _serverConn.AddModule(IPubSubRouterModule.Create(_natifyServer, "VN"));
    }

    private async Task CreateUnityServer()
    {
        _natifyClient = new NatifyClientFast(NatsUrl, "SyncServer", "ServerGroup", "VN", "SyncRouter");
        _pubSub = IPubSub.Create(new PubSubConfig { GridSize = 100f });
        _pubSub.AddNatify(_natifyClient);
        Thread.Sleep(2000);
    }

    private async Task CreateUnit(long unitId, Vector2 position)
    {
        serverUnit[unitId] = new MyUnit();
        var a = await _pubSub.CreateUnitAsync(unitId, "T1", position, serverUnit[unitId]);
    }

    private void DestroyUnit(long unitId)
    {
        serverUnit.Remove(unitId);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }


    public void Dispose()
    {
        _pubSub?.Dispose();
        _natifyServer?.Dispose();
        _natifyClient?.Dispose();
    }

    [Fact]
    public async Task FullStack_CreateUnit_ClientReceivesUnit()
    {
        clientUnit.Clear();
        serverUnit.Clear();

        var unitId = 1L;
        var signal = new ManualResetEventSlim();

        CreateRouter();
        await CreateUnityServer();
        await CreateUnityClient("test", unitId);

        var provider = new Provider { CreatedSignal = signal, ExpectedUnitId = unitId };
        _pubSubClientModule.Get().AddProvider(provider);

        await Task.Delay(2000);

        await CreateUnit(unitId, V(50, 50));

        Assert.True(signal.Wait(5000), $"Client did NOT receive unit {unitId}");
        Assert.True(clientUnit.ContainsKey(unitId));
        Assert.NotNull(clientUnit[unitId]);
    }

    internal class Provider : IProvider
    {
        public ManualResetEventSlim? CreatedSignal;
        public long ExpectedUnitId = -1;
        public string UnitType => "T1";

        public GameObjectTest CreateObject(long unitId, int version, byte[] data)
        {
            var ob1 = new GameObjectTest();
            clientUnit[unitId] = ob1;
            if (unitId == ExpectedUnitId)
                CreatedSignal?.Set();
            return ob1;
        }

        public void DestroyObject(long unitId, GameObjectTest obj)
        {
            clientUnit.Remove(unitId);
        }
    }

    internal class MyUnit
    {
    }

    internal class Player(string id, string name) : IUser
    {
        public string Name => name;
        public string Id => id;
    }
}
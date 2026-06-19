using MyConnection;
using Natify;
using PubSubLib;
using PubSubLib.Client;
using PubSubLib.Messages;
using PubSubLib.Router;
using Vector2 = PubSubLib.Vector2;
using ServerAlive = PubSubLib.IAlive;
using ClientAlive = PubSubLib.Client.IAlive;


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

    private static readonly Dictionary<long, AliveStub> clientUnit = new();
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

    private async Task<PubSubLib.IUnit> CreateUnit(long unitId, Vector2 position)
    {
        serverUnit[unitId] = new MyUnit();
        return await _pubSub.CreateUnitAsync(unitId, "T1", position, serverUnit[unitId]);
    }

    private void DestroyUnit(long unitId)
    {
        if (serverUnit.TryGetValue(unitId, out var myUnit))
            myUnit.IsAlive = false;
        serverUnit.Remove(unitId);
    }


    public void Dispose()
    {
        _pubSubClientModule?.Dispose();
        _clientConn?.DisposeAsync().GetAwaiter().GetResult();
        _serverConn?.DisposeAsync().GetAwaiter().GetResult();
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

    [Fact]
    public async Task FullStack_ClientKillsUnit_ServerResyncs()
    {
        clientUnit.Clear();
        serverUnit.Clear();

        var unitId = 1L;
        var createdSignal = new ManualResetEventSlim();
        var resyncSignal = new ManualResetEventSlim();

        CreateRouter();
        await CreateUnityServer();
        await CreateUnityClient("test", unitId);

        var provider = new ResyncProvider { CreatedSignal = createdSignal, ResyncSignal = resyncSignal, ExpectedUnitId = unitId };
        _pubSubClientModule.Get().AddProvider(provider);

        await Task.Delay(2000);
        await CreateUnit(unitId, V(50, 50));
        Assert.True(createdSignal.Wait(5000), "Stage 1 failed: unit not received by client");
        Assert.True(clientUnit.ContainsKey(unitId));

        // Stage 1.5: run ticks so watcher registers "T1" as known type via ping
        using (var cts = new CancellationTokenSource())
        {
            _ = RunTickLoop(cts.Token);
            await Task.Delay(1500);
        }

        // Stage 2: kill unit on client — set IsAlive = false to simulate dead unit
        if (clientUnit.TryGetValue(unitId, out var stub))
            stub.IsAlive = false;
        clientUnit.Remove(unitId);

        // Stage 3: tick → ping empty but watcher remembers "T1" → server re-syncs unit
        using (var cts = new CancellationTokenSource())
        {
            _ = RunTickLoop(cts.Token);
            Assert.True(resyncSignal.Wait(15000), "Unit was NOT re-synced by server");
        }
        Assert.True(clientUnit.ContainsKey(unitId));
    }

    [Fact]
    public async Task FullStack_MoveWatcher_EntersNewUnit_LeavesOldUnit()
    {
        clientUnit.Clear();
        serverUnit.Clear();

        var unit1Id = 1L;
        var unit2Id = 2L;
        var unit1Created = new ManualResetEventSlim();
        var unit2Entered = new ManualResetEventSlim();
        var unit1Left = new ManualResetEventSlim();

        CreateRouter();
        await CreateUnityServer();
        await CreateUnityClient("test", unit1Id);

        var provider = new MoveWatcherProvider
        {
            Unit1Created = unit1Created,
            Unit2Entered = unit2Entered,
            Unit1Left = unit1Left,
            Unit1Id = unit1Id,
            Unit2Id = unit2Id
        };
        _pubSubClientModule.Get().AddProvider(provider);

        await Task.Delay(2000);

        // Stage 1: create unit1 at (50,50) → in watcher's cell "0:0" → BatchEnter
        await CreateUnit(unit1Id, V(50, 50));
        Assert.True(unit1Created.Wait(5000), "Stage 1 failed: unit1 did not enter");
        Assert.True(clientUnit.ContainsKey(unit1Id));

        // Stage 2: create unit2 at (500,500) → outside range, no event yet
        await CreateUnit(unit2Id, V(500, 500));

        // Run ticks so watcher registers "T1" as known type
        using (var cts = new CancellationTokenSource())
        {
            _ = RunTickLoop(cts.Token);
            await Task.Delay(1500);
        }

        // Stage 3: move watcher to unit2's position → SyncEnter unit2 + SyncLeave unit1
        _pubSubClientModule.Get().MoveWatcher(new PubSubLib.Client.Vector2 { x = 500, y = 500 }, 100);

        Assert.True(unit2Entered.Wait(15000), "Stage 3 failed: unit2 did not enter after MoveWatcher");
        Assert.True(unit1Left.Wait(15000), "Stage 3 failed: unit1 did not leave after MoveWatcher");

        Assert.True(clientUnit.ContainsKey(unit2Id));
        Assert.False(clientUnit.ContainsKey(unit1Id));
    }

    [Fact]
    public async Task FullStack_UnitPublishesEvent_TwoClientsReceive()
    {
        clientUnit.Clear();
        serverUnit.Clear();

        var unitId = 10L;
        var client1Created = new ManualResetEventSlim();
        var client2Created = new ManualResetEventSlim();
        var client1Event = new ManualResetEventSlim();
        var client2Event = new ManualResetEventSlim();

        CreateRouter();
        await CreateUnityServer();

        // Client 1 (via helper)
        await CreateUnityClient("player1", 1);
        var provider1 = new EventProvider { CreatedSignal = client1Created, EventSignal = client1Event, ExpectedUnitId = unitId };
        _pubSubClientModule.Get().AddProvider(provider1);

        // Client 2 (inline — same router, different watcherId)
        var client2 = IClient.Create(new ClientConfig
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
        var module2 = IPubSubClientModule.Create(new Config { PingIntervalMs = 300 });
        var provider2 = new EventProvider { CreatedSignal = client2Created, EventSignal = client2Event, ExpectedUnitId = unitId };
        module2.Get().AddProvider(provider2);
        client2.AddModule(module2);
        await client2.Login(() => new StringValue { Value = "player2_2" });
        await client2.ConnectServer();

        try
        {
            await Task.Delay(2000);

            // Stage 1: create unit, both watchers are in cell "0:0" → both receive BatchEnter
            var unit = await CreateUnit(unitId, V(50, 50));
            Assert.True(client1Created.Wait(5000), "Stage 1 failed: client1 did not receive unit");
            Assert.True(client2Created.Wait(5000), "Stage 1 failed: client2 did not receive unit");
            Assert.True(clientUnit.ContainsKey(unitId));

            // Run ticks so both watchers register "T1" as known type
            using (var cts = new CancellationTokenSource())
            {
                _ = RunTickLoop(cts.Token);
                _ = RunTickLoop(cts.Token, module2.Get());
                await Task.Delay(1500);
            }

            // Stage 2: unit publishes event → both clients receive
            unit.PublishEvent("attack", new byte[] { 1, 2, 3 });
            Assert.True(client1Event.Wait(15000), "Stage 2 failed: client1 did not receive event");
            Assert.True(client2Event.Wait(15000), "Stage 2 failed: client2 did not receive event");
        }
        finally
        {
            await client2.DisposeAsync();
        }
    }

    [Fact]
    public async Task FullStack_ServerDestroysUnit_ClientReceivesDestroy()
    {
        clientUnit.Clear();
        serverUnit.Clear();

        var unitId = 1L;
        var createdSignal = new ManualResetEventSlim();
        var destroyedSignal = new ManualResetEventSlim();

        CreateRouter();
        await CreateUnityServer();
        await CreateUnityClient("test", unitId);

        var provider = new DestroyProvider { CreatedSignal = createdSignal, DestroyedSignal = destroyedSignal, ExpectedUnitId = unitId };
        _pubSubClientModule.Get().AddProvider(provider);

        await Task.Delay(2000);
        await CreateUnit(unitId, V(50, 50));
        Assert.True(createdSignal.Wait(5000), "Stage 1 failed: unit not received by client");
        Assert.True(clientUnit.ContainsKey(unitId));

        // Stage 1.5: run ticks so watcher registers "T1" as known type via ping
        using (var cts = new CancellationTokenSource())
        {
            _ = RunTickLoop(cts.Token);
            await Task.Delay(1500);
        }

        // Stage 2: server destroys unit — client unit still alive, ping includes it
        DestroyUnit(unitId);

        // Stage 3: tick → ping has unit 1 → server TryResolveAlive detects dead → BatchLeave → client receives destroy
        using (var cts = new CancellationTokenSource())
        {
            _ = RunTickLoop(cts.Token);
            Assert.True(destroyedSignal.Wait(15000), "Client did NOT receive destroy event");
        }
        Assert.False(clientUnit.ContainsKey(unitId));
    }

    private async Task RunTickLoop(CancellationToken ct, IPubSubClient? client = null)
    {
        var c = client ?? _pubSubClientModule.Get();
        while (!ct.IsCancellationRequested)
        {
            c.Tick();
            try { await Task.Delay(10, ct); } catch { break; }
        }
    }

    internal class Provider : IProvider<AliveStub>
    {
        public ManualResetEventSlim? CreatedSignal;
        public long ExpectedUnitId = -1;
        public string UnitType => "T1";

        public AliveStub CreateObject(long unitId, byte[] data)
        {
            var ob1 = new AliveStub();
            clientUnit[unitId] = ob1;
            if (unitId == ExpectedUnitId)
                CreatedSignal?.Set();
            return ob1;
        }

        public void UpdateObject(long unitId, AliveStub obj, byte[] data)
        {
        }

        public void DestroyObject(long unitId, AliveStub obj)
        {
            clientUnit.Remove(unitId);
        }

        public void OnEvent(long unitId, AliveStub obj, string eventName, byte[] data, EventMeta meta)
        {
        }
    }

    internal class ResyncProvider : IProvider<AliveStub>
    {
        public ManualResetEventSlim? CreatedSignal;
        public ManualResetEventSlim? ResyncSignal;
        public long ExpectedUnitId = -1;
        public string UnitType => "T1";
        private int _expectedCreateCount;

        public AliveStub CreateObject(long unitId, byte[] data)
        {
            var ob = new AliveStub();
            clientUnit[unitId] = ob;
            if (unitId == ExpectedUnitId)
            {
                _expectedCreateCount++;
                if (_expectedCreateCount == 1)
                    CreatedSignal?.Set();
                else
                    ResyncSignal?.Set();
            }
            return ob;
        }

        public void UpdateObject(long unitId, AliveStub obj, byte[] data)
        {
        }

        public void DestroyObject(long unitId, AliveStub obj)
        {
            clientUnit.Remove(unitId);
        }

        public void OnEvent(long unitId, AliveStub obj, string eventName, byte[] data, EventMeta meta)
        {
        }
    }

    internal class DestroyProvider : IProvider<AliveStub>
    {
        public ManualResetEventSlim? CreatedSignal;
        public ManualResetEventSlim? DestroyedSignal;
        public long ExpectedUnitId = -1;
        public string UnitType => "T1";

        public AliveStub CreateObject(long unitId, byte[] data)
        {
            var ob = new AliveStub();
            clientUnit[unitId] = ob;
            if (unitId == ExpectedUnitId)
                CreatedSignal?.Set();
            return ob;
        }

        public void UpdateObject(long unitId, AliveStub obj, byte[] data)
        {
        }

        public void DestroyObject(long unitId, AliveStub obj)
        {
            clientUnit.Remove(unitId);
            if (unitId == ExpectedUnitId)
                DestroyedSignal?.Set();
        }

        public void OnEvent(long unitId, AliveStub obj, string eventName, byte[] data, EventMeta meta)
        {
        }
    }

    internal class MoveWatcherProvider : IProvider<AliveStub>
    {
        public ManualResetEventSlim? Unit1Created;
        public ManualResetEventSlim? Unit2Entered;
        public ManualResetEventSlim? Unit1Left;
        public long Unit1Id = -1;
        public long Unit2Id = -1;
        public string UnitType => "T1";

        public AliveStub CreateObject(long unitId, byte[] data)
        {
            var ob = new AliveStub();
            clientUnit[unitId] = ob;
            if (unitId == Unit1Id)
                Unit1Created?.Set();
            if (unitId == Unit2Id)
                Unit2Entered?.Set();
            return ob;
        }

        public void UpdateObject(long unitId, AliveStub obj, byte[] data)
        {
        }

        public void DestroyObject(long unitId, AliveStub obj)
        {
            clientUnit.Remove(unitId);
            if (unitId == Unit1Id)
                Unit1Left?.Set();
        }

        public void OnEvent(long unitId, AliveStub obj, string eventName, byte[] data, EventMeta meta)
        {
        }
    }

    internal class EventProvider : IProvider<AliveStub>
    {
        public ManualResetEventSlim? CreatedSignal;
        public ManualResetEventSlim? EventSignal;
        public long ExpectedUnitId = -1;
        public string UnitType => "T1";

        public AliveStub CreateObject(long unitId, byte[] data)
        {
            var ob = new AliveStub();
            clientUnit[unitId] = ob;
            if (unitId == ExpectedUnitId)
                CreatedSignal?.Set();
            return ob;
        }

        public void UpdateObject(long unitId, AliveStub obj, byte[] data)
        {
        }

        public void DestroyObject(long unitId, AliveStub obj)
        {
            clientUnit.Remove(unitId);
        }

        public void OnEvent(long unitId, AliveStub obj, string eventName, byte[] data, EventMeta meta)
        {
            if (unitId == ExpectedUnitId)
                EventSignal?.Set();
        }
    }

    internal class MyUnit : ServerAlive
    {
        public bool IsAlive { get; set; } = true;
    }

    internal class AliveStub : ClientAlive
    {
        public bool IsAlive { get; set; } = true;
    }

    internal class Player(string id, string name) : IUser
    {
        public string Name => name;
        public string Id => id;
    }
}
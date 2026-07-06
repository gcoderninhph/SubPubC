using System.Collections.Concurrent;
using System.Linq;
using MyConnection;
using Natify;
using PubSubLib;
using PubSubLib.Client;
using PubSubLib.Messages;
using PubSubLib.Mirror;
using PubSubLib.Router;
using ServerAlive = PubSubLib.IAlive;
using ClientAlive = PubSubLib.Client.IAlive;

namespace PubSubLibTest;

// ===== Real Natify Integration Tests =====

[UnitMirrorClient(typeof(RemoveWatcherCmd), UnitType = "remove_watcher", Target = typeof(RemoveWatcherTarget))]
public partial class RemoveWatcherUnitClient
{
}

[UnitMirrorClient(typeof(RemoveWatcherCmd), UnitType = "remove_watcher", Target = typeof(TrackedTarget))]
public partial class TrackedWatcherUnitClient
{
}

public class RegionTestFullStack : IDisposable
{
    private const string NatsUrl = "nats://localhost:4222";

    private NatifyServer _natifyServer;
    private NatifyClientFast _natifyClient;
    private IClient _clientConn;
    private IServer _serverConn;
    private IRegionModule _regionModule;
    private IRegionClientModule _regionClientModule;

    private static readonly Dictionary<long, RemoveWatcherTarget> _clientTargets = new();
    private static readonly Dictionary<long, ServerTarget> _serverTargets = new();
    private readonly List<ClientContext> _clients = new();

    private static Vector2 V(float x, float y) => new() { x = x, y = y };

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

        _regionClientModule = IRegionClientModule.Create(new Config
        {
            PingIntervalMs = 300
        });

        _clientConn.AddModule(_regionClientModule);
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
            return Task.FromResult<IUser>(new RegionPlayer(id, user));
        });
        _serverConn.AddModule(IRegionRouterModule.Create(_natifyServer, "VN"));
    }

    private void CreateUnityServer()
    {
        _natifyClient = new NatifyClientFast(NatsUrl, "SyncServer", "ServerGroup", "VN", "SyncRouter");
        _regionModule = IRegionModule.Create(new RegionConfig
        {
            GridSize = 100f,
            NatifyClient = _natifyClient
        });
        Thread.Sleep(2000);
    }

    private async Task<RemoveWatcherUnitServer> CreateUnit(long unitId, Vector2 position)
    {
        var target = new ServerTarget();
        _serverTargets[unitId] = target;
        return await _regionModule.CreateUnitAsync<RemoveWatcherUnitServer, ServerTarget>(unitId, position, target);
    }

    private void DestroyUnit(long unitId)
    {
        if (_serverTargets.TryGetValue(unitId, out var target))
            target.IsAlive = false;
        _serverTargets.Remove(unitId);
    }

    private async Task<ClientContext> CreateClient(string user, long id)
    {
        var ctx = new ClientContext(user, id);
        ctx.ClientConn = IClient.Create(new ClientConfig
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
        ctx.RegionClientModule = IRegionClientModule.Create(new Config
        {
            PingIntervalMs = 300
        });
        ctx.ClientConn.AddModule(ctx.RegionClientModule);
        await ctx.ClientConn.Login(() => new StringValue { Value = $"{user}_{id}" });
        await ctx.ClientConn.ConnectServer();
        _clients.Add(ctx);
        return ctx;
    }

    private static void MoveClientWatcher(ClientContext ctx, Vector2 position, float range)
    {
        ctx.RegionClientModule.MoveWatcher(position, range);
    }

    private static async Task RunClientTickLoop(ClientContext ctx, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { ctx.RegionClientModule.Tick(); } catch { }
            try { await Task.Delay(10, ct); } catch { break; }
        }
    }

    public void Dispose()
    {
        _regionClientModule = null!;
        _clientConn?.DisposeAsync().GetAwaiter().GetResult();
        _serverConn?.DisposeAsync().GetAwaiter().GetResult();
        _regionModule?.Dispose();
        _natifyServer?.Dispose();
        _natifyClient?.Dispose();
        foreach (var ctx in _clients)
            ctx.Dispose();
        _clients.Clear();
    }

    [Fact]
    public async Task FullStack_CreateUnit_ClientReceivesUnit()
    {
        _clientTargets.Clear();
        _serverTargets.Clear();

        var unitId = 1L;
        var signal = new ManualResetEventSlim();

        CreateRouter();
        CreateUnityServer();
        await CreateUnityClient("test", unitId);

        _regionClientModule.OnCreateUnit<RemoveWatcherUnitClient, RemoveWatcherTarget>(wrapper =>
        {
            var target = new RemoveWatcherTarget();
            _clientTargets[unitId] = target;
            if (unitId == 1L)
                signal.Set();
            return target;
        });

        await Task.Delay(2000);

        await CreateUnit(unitId, V(50, 50));

        Assert.True(signal.Wait(5000), $"Client did NOT receive unit {unitId}");
        Assert.True(_clientTargets.ContainsKey(unitId));
        Assert.NotNull(_clientTargets[unitId]);
    }

    [Fact]
    public async Task FullStack_Commit_ClientSyncsMirrorFields()
    {
        _clientTargets.Clear();
        _serverTargets.Clear();

        var unitId = 1L;
        var createdSignal = new ManualResetEventSlim();
        var commitSignal = new ManualResetEventSlim();

        CreateRouter();
        CreateUnityServer();
        await CreateUnityClient("test", unitId);

        _regionClientModule.OnCreateUnit<RemoveWatcherUnitClient, RemoveWatcherTarget>(wrapper =>
        {
            var target = new RemoveWatcherTarget
            {
                OnCommitCallback = () => commitSignal.Set()
            };
            _clientTargets[wrapper.Id] = target;
            createdSignal.Set();
            return target;
        });

        await Task.Delay(2000);

        var unit = await CreateUnit(unitId, V(50, 50));
        Assert.True(createdSignal.Wait(5000), "Stage 1 failed: unit not received");

        // Run ticks so watcher registers known type
        using (var cts = new CancellationTokenSource())
        {
            _ = RunTickLoop(cts.Token);
            await Task.Delay(1500);
        }

        unit.WatcherId = 42;
        unit.Commit("test_commit");
        MirrorProtoBus.Flush();

        Assert.True(commitSignal.Wait(10000), "Stage 2 failed: commit not received");
        Assert.Equal(42, _clientTargets[unitId].MirrorUnit!.WatcherId);
    }

    [Fact]
    public async Task FullStack_DestroyUnit_ClientRemovesUnit()
    {
        _clientTargets.Clear();
        _serverTargets.Clear();

        var unitId = 1L;
        var createdSignal = new ManualResetEventSlim();
        var destroyedSignal = new ManualResetEventSlim();

        CreateRouter();
        CreateUnityServer();
        await CreateUnityClient("test", unitId);

        _regionClientModule.OnCreateUnit<RemoveWatcherUnitClient, RemoveWatcherTarget>(wrapper =>
        {
            var target = new RemoveWatcherTarget
            {
                DestroySignal = destroyedSignal
            };
            _clientTargets[unitId] = target;
            createdSignal.Set();
            return target;
        });

        await Task.Delay(2000);

        await CreateUnit(unitId, V(50, 50));
        Assert.True(createdSignal.Wait(5000), "Stage 1 failed");

        using (var cts = new CancellationTokenSource())
        {
            _ = RunTickLoop(cts.Token);
            await Task.Delay(1500);
        }

        _regionModule.DestroyUnit<RemoveWatcherUnitServer, ServerTarget>(unitId);

        Assert.True(destroyedSignal.Wait(10000), "Stage 2 failed: destroy not received");
    }

    [Fact]
    public async Task FullStack_Lifecycle_OnStartOnDestroy()
    {
        _serverTargets.Clear();

        var unitId = 1L;
        var serverStartSignal = new ManualResetEventSlim();
        var serverInitSignal = new ManualResetEventSlim();
        var serverDestroySignal = new ManualResetEventSlim();

        CreateRouter();
        CreateUnityServer();

        var target = new ServerTarget
        {
            StartSignal = serverStartSignal,
            InitSignal = serverInitSignal,
            DestroySignal = serverDestroySignal
        };
        _serverTargets[unitId] = target;

        var unit = await _regionModule.CreateUnitAsync<RemoveWatcherUnitServer, ServerTarget>(unitId, V(50, 50), target);

        Assert.True(serverInitSignal.Wait(5000), "ISetRegionUnit.SetRegionUnit not called");
        Assert.True(serverStartSignal.Wait(5000), "IRegionUnitOnStart.OnUnitStart not called");

        _regionModule.DestroyUnit<RemoveWatcherUnitServer, ServerTarget>(unitId);

        Assert.True(serverDestroySignal.Wait(5000), "IRegionUnitOnDestroy.OnUnitDestroy not called");
    }

    // ===== Multi-unit / multi-client integration tests =====

    [Fact]
    public async Task FullStack_MultipleUnits_SingleClient()
    {
        _serverTargets.Clear();

        const int unitCount = 5;
        var createSignals = new ManualResetEventSlim[unitCount + 1];
        for (int i = 1; i <= unitCount; i++)
            createSignals[i] = new ManualResetEventSlim();

        CreateRouter();
        CreateUnityServer();
        var ctx = await CreateClient("multi", 1);

        ctx.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            ctx.Targets[wrapper.Id] = t;
            if (wrapper.Id > 0 && wrapper.Id <= unitCount)
                createSignals[wrapper.Id].Set();
            return t;
        });

        MoveClientWatcher(ctx, V(0, 0), 1000);
        await Task.Delay(2500);

        using var cts = new CancellationTokenSource();
        _ = RunClientTickLoop(ctx, cts.Token);
        await Task.Delay(1000);

        for (long i = 1; i <= unitCount; i++)
        {
            await CreateUnit(i, V(i * 20, i * 20));
            Assert.True(createSignals[i].Wait(10000), $"Unit {i} not created on client");
        }

        await Task.Delay(500);
        var units = ctx.RegionClientModule.GetUnits<TrackedWatcherUnitClient, TrackedTarget>();
        Assert.Equal(unitCount, units.Count);
        var ids = new HashSet<long>(units.Select(u => u.Id));
        for (long i = 1; i <= unitCount; i++)
            Assert.Contains(i, ids);
    }

    [Fact]
    public async Task FullStack_MultipleClients_UnitCoverage()
    {
        _serverTargets.Clear();

        CreateRouter();
        CreateUnityServer();

        var clientA = await CreateClient("player", 1);
        var clientB = await CreateClient("player", 2);
        var clientC = await CreateClient("player", 3);

        foreach (var ctx in new[] { clientA, clientB, clientC })
        {
            ctx.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
            {
                var t = new TrackedTarget();
                ctx.Targets[wrapper.Id] = t;
                return t;
            });
        }

        MoveClientWatcher(clientA, V(0, 0), 400);
        MoveClientWatcher(clientB, V(500, 0), 400);
        MoveClientWatcher(clientC, V(1000, 0), 400);

        using var cts = new CancellationTokenSource();
        _ = RunClientTickLoop(clientA, cts.Token);
        _ = RunClientTickLoop(clientB, cts.Token);
        _ = RunClientTickLoop(clientC, cts.Token);
        await Task.Delay(1000);

        await CreateUnit(1, V(0, 0));
        await CreateUnit(2, V(300, 0));
        await CreateUnit(3, V(700, 0));
        await CreateUnit(4, V(1200, 0));
        await Task.Delay(2000);

        static HashSet<long> AliveIds(IRegionClientModule mod)
        {
            var units = mod.GetUnits<TrackedWatcherUnitClient, TrackedTarget>();
            return new HashSet<long>(units.Select(u => u.Id));
        }

        var idsA = AliveIds(clientA.RegionClientModule);
        Assert.Equal(2, idsA.Count);
        Assert.True(idsA.Contains(1L));
        Assert.True(idsA.Contains(2L));

        var idsB = AliveIds(clientB.RegionClientModule);
        Assert.Equal(2, idsB.Count);
        Assert.True(idsB.Contains(2L));
        Assert.True(idsB.Contains(3L));

        var idsC = AliveIds(clientC.RegionClientModule);
        Assert.Equal(2, idsC.Count);
        Assert.True(idsC.Contains(3L));
        Assert.True(idsC.Contains(4L));
    }

    [Fact]
    public async Task FullStack_WatcherMove_EnterExit()
    {
        _serverTargets.Clear();

        CreateRouter();
        CreateUnityServer();

        var clientA = await CreateClient("player", 1);
        var clientB = await CreateClient("player", 2);

        var createSigsA = new Dictionary<long, ManualResetEventSlim>();
        var createSigsB = new Dictionary<long, ManualResetEventSlim>();

        clientA.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            clientA.Targets[wrapper.Id] = t;
            if (createSigsA.TryGetValue(wrapper.Id, out var sig))
                sig.Set();
            return t;
        });

        clientB.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            clientB.Targets[wrapper.Id] = t;
            if (createSigsB.TryGetValue(wrapper.Id, out var sig))
                sig.Set();
            return t;
        });

        MoveClientWatcher(clientA, V(0, 0), 200);
        MoveClientWatcher(clientB, V(500, 0), 200);

        using var cts = new CancellationTokenSource();
        _ = RunClientTickLoop(clientA, cts.Token);
        _ = RunClientTickLoop(clientB, cts.Token);
        await Task.Delay(2500);

        // Phase 1: create units at (100,0) for A and (400,0) for B
        createSigsA[1] = new ManualResetEventSlim();
        createSigsB[2] = new ManualResetEventSlim();

        await CreateUnit(1, V(100, 0));
        await CreateUnit(2, V(400, 0));
        await Task.Delay(2500);

        Assert.True(createSigsA[1].Wait(10000), "A did not see unit 1");
        Assert.True(createSigsB[2].Wait(10000), "B did not see unit 2");

        static List<long> Alive(IRegionClientModule m) =>
            m.GetUnits<TrackedWatcherUnitClient, TrackedTarget>().Select(u => u.Id).ToList();

        var aInit = Alive(clientA.RegionClientModule);
        Assert.True(aInit.Contains(1L));
        Assert.True(!aInit.Contains(2L));

        var bInit = Alive(clientB.RegionClientModule);
        Assert.True(bInit.Contains(2L));
        Assert.True(!bInit.Contains(1L));

        // Phase 2: move watcher A from (0,0) to (500,0) range 200
        // Unit 1 (100,0) leaves A; Unit 2 (400,0) enters A
        var aLostUnit1 = new ManualResetEventSlim();
        clientA.Targets[1].DestroySignal = aLostUnit1;
        createSigsA[2] = new ManualResetEventSlim();

        MoveClientWatcher(clientA, V(500, 0), 200);
        await Task.Delay(2500);

        Assert.True(aLostUnit1.Wait(10000), "A did not lose unit 1 after watcher move");
        Assert.True(createSigsA[2].Wait(10000), "A did not gain unit 2 after watcher move");

        var aAfter = Alive(clientA.RegionClientModule);
        Assert.True(!aAfter.Contains(1L), "A should not have unit 1 after move");
        Assert.True(aAfter.Contains(2L), "A should have unit 2 after move");

        var bAfter = Alive(clientB.RegionClientModule);
        Assert.True(bAfter.Contains(2L), "B should still have unit 2");
        Assert.True(!bAfter.Contains(1L), "B should still not have unit 1");
    }

    [Fact]
    public async Task FullStack_UnitMove_EnterExit()
    {
        _serverTargets.Clear();

        CreateRouter();
        CreateUnityServer();

        var ctx = await CreateClient("player", 1);

        ctx.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            ctx.Targets[wrapper.Id] = t;
            return t;
        });

        MoveClientWatcher(ctx, V(250, 0), 100);

        using var cts = new CancellationTokenSource();
        _ = RunClientTickLoop(ctx, cts.Token);
        await Task.Delay(2500);

        static List<long> Alive(IRegionClientModule m) =>
            m.GetUnits<TrackedWatcherUnitClient, TrackedTarget>().Select(u => u.Id).ToList();

        // Create unit at (0,0) — outside watcher range (cells 1-3, x∈[100,400))
        var unit = await CreateUnit(1, V(0, 0));
        await Task.Delay(2500);

        var outOfRange = Alive(ctx.RegionClientModule);
        Assert.True(!outOfRange.Contains(1L), "Unit should be dead outside range");

        // Move unit into range (x=200 → cell 2)
        unit.SetPosition(200, 0);
        await Task.Delay(2500);

        var inRange = Alive(ctx.RegionClientModule);
        Assert.True(inRange.Contains(1L), "Unit should be alive after entering range");

        // Move unit out of range (x=500 → cell 5)
        unit.SetPosition(500, 0);
        await Task.Delay(2500);

        var outAgain = Alive(ctx.RegionClientModule);
        Assert.True(!outAgain.Contains(1L), "Unit should be dead after leaving range");
    }

    [Fact]
    public async Task FullStack_Commit_OnlyCorrectWatchers()
    {
        _serverTargets.Clear();

        CreateRouter();
        CreateUnityServer();

        var clientA = await CreateClient("player", 1);
        var clientB = await CreateClient("player", 2);

        clientA.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            clientA.Targets[wrapper.Id] = t;
            return t;
        });

        clientB.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            clientB.Targets[wrapper.Id] = t;
            return t;
        });

        MoveClientWatcher(clientA, V(0, 0), 200);
        MoveClientWatcher(clientB, V(500, 0), 200);

        using var cts = new CancellationTokenSource();
        _ = RunClientTickLoop(clientA, cts.Token);
        _ = RunClientTickLoop(clientB, cts.Token);
        await Task.Delay(2500);

        static List<long> Alive(IRegionClientModule m) =>
            m.GetUnits<TrackedWatcherUnitClient, TrackedTarget>().Select(u => u.Id).ToList();

        // Create unit at (100,0) → cell 1 → only A covers it
        var unit = await CreateUnit(1, V(100, 0));
        await Task.Delay(2500);

        var aliveA = Alive(clientA.RegionClientModule);
        var aliveB = Alive(clientB.RegionClientModule);
        Assert.True(aliveA.Contains(1L), "A should have unit alive");
        Assert.True(!aliveB.Contains(1L), "B should not have unit alive");

        // Commit: only A should receive
        var commitA = new ManualResetEventSlim();
        clientA.Targets[1].CommitSignal = commitA;
        unit.WatcherId = 99;
        unit.Commit("only_A");
        MirrorProtoBus.Flush();
        Assert.True(commitA.Wait(10000), "A did not receive commit");
        Assert.Equal("only_A", clientA.Targets[1].LastCommit);
        Assert.Equal(99, clientA.Targets[1].MirrorUnit!.WatcherId);
    }

    [Fact]
    public async Task FullStack_UnitMove_CommitFollowsUnit()
    {
        _serverTargets.Clear();

        CreateRouter();
        CreateUnityServer();

        var clientA = await CreateClient("player", 1);
        var clientB = await CreateClient("player", 2);

        clientA.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            clientA.Targets[wrapper.Id] = t;
            return t;
        });

        clientB.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            clientB.Targets[wrapper.Id] = t;
            return t;
        });

        MoveClientWatcher(clientA, V(0, 0), 200);
        MoveClientWatcher(clientB, V(500, 0), 200);

        using var cts = new CancellationTokenSource();
        _ = RunClientTickLoop(clientA, cts.Token);
        _ = RunClientTickLoop(clientB, cts.Token);
        await Task.Delay(2500);

        static List<long> Alive(IRegionClientModule m) =>
            m.GetUnits<TrackedWatcherUnitClient, TrackedTarget>().Select(u => u.Id).ToList();

        // Phase 1: unit at (100,0) → only A sees it
        var unit = await CreateUnit(1, V(100, 0));
        await Task.Delay(2500);

        Assert.True(Alive(clientA.RegionClientModule).Contains(1L), "A should have unit");
        Assert.True(!Alive(clientB.RegionClientModule).Contains(1L), "B should not have unit");

        // Only A receives commit
        var commitA = new ManualResetEventSlim();
        clientA.Targets[1].CommitSignal = commitA;
        unit.Commit("msg_a");
        MirrorProtoBus.Flush();
        Assert.True(commitA.Wait(10000), "A did not receive first commit");
        Assert.Equal("msg_a", clientA.Targets[1].LastCommit);

        // Phase 2: move unit to (400,0) → enters B's range, leaves A's
        unit.SetPosition(400, 0);
        await Task.Delay(2500);

        Assert.True(!Alive(clientA.RegionClientModule).Contains(1L), "A should have lost unit");
        Assert.True(Alive(clientB.RegionClientModule).Contains(1L), "B should have gained unit");

        // Only B receives commit now
        var commitB = new ManualResetEventSlim();
        clientB.Targets[1].CommitSignal = commitB;
        unit.Commit("msg_b");
        MirrorProtoBus.Flush();
        Assert.True(commitB.Wait(10000), "B did not receive commit after unit moved");
        Assert.Equal("msg_b", clientB.Targets[1].LastCommit);
    }

    [Fact]
    public async Task FullStack_WatcherMove_CommitReach()
    {
        _serverTargets.Clear();

        CreateRouter();
        CreateUnityServer();

        var clientA = await CreateClient("player", 1);
        var createSigsA = new Dictionary<long, ManualResetEventSlim>();

        clientA.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
        {
            var t = new TrackedTarget();
            clientA.Targets[wrapper.Id] = t;
            if (createSigsA.TryGetValue(wrapper.Id, out var sig))
                sig.Set();
            return t;
        });

        MoveClientWatcher(clientA, V(0, 0), 200);

        using var cts = new CancellationTokenSource();
        _ = RunClientTickLoop(clientA, cts.Token);
        await Task.Delay(2500);

        static List<long> Alive(IRegionClientModule m) =>
            m.GetUnits<TrackedWatcherUnitClient, TrackedTarget>().Select(u => u.Id).ToList();

        // Create unit at (100,0) → watcher A covers it
        createSigsA[1] = new ManualResetEventSlim();
        var unit = await CreateUnit(1, V(100, 0));
        await Task.Delay(2500);

        Assert.True(createSigsA[1].Wait(10000), "A did not see unit");
        Assert.Contains(1L, Alive(clientA.RegionClientModule));

        // Commit 1: A receives
        var commit1 = new ManualResetEventSlim();
        clientA.Targets[1].CommitSignal = commit1;
        unit.Commit("first");
        MirrorProtoBus.Flush();
        Assert.True(commit1.Wait(10000), "A did not receive first commit");
        Assert.Equal("first", clientA.Targets[1].LastCommit);

        // Move watcher A to (500,0) range 200 → unit at (100,0) leaves A
        MoveClientWatcher(clientA, V(500, 0), 200);
        await Task.Delay(2500);

        Assert.True(!Alive(clientA.RegionClientModule).Contains(1L), "Unit should not be alive after watcher move");

        // Commit 2: A should NOT receive (target is dead)
        unit.Commit("second");
        MirrorProtoBus.Flush();
        await Task.Delay(500);
        Assert.NotEqual("second", clientA.Targets[1].LastCommit);

        // Move watcher A back to (0,0) range 200 → unit re-enters
        createSigsA[1] = new ManualResetEventSlim();
        MoveClientWatcher(clientA, V(0, 0), 200);
        await Task.Delay(2500);

        Assert.True(createSigsA[1].Wait(10000), "A did not regain unit after watcher moved back");
        Assert.Contains(1L, Alive(clientA.RegionClientModule));

        // Commit 3: A receives on new target
        var commit3 = new ManualResetEventSlim();
        clientA.Targets[1].CommitSignal = commit3;
        unit.Commit("third");
        MirrorProtoBus.Flush();
        Assert.True(commit3.Wait(10000), "A did not receive third commit");
        Assert.Equal("third", clientA.Targets[1].LastCommit);
    }

    [Fact]
    public async Task FullStack_MultiUnit_MultiClient_Complex()
    {
        _serverTargets.Clear();

        CreateRouter();
        CreateUnityServer();

        var clients = new[]
        {
            await CreateClient("player", 1),
            await CreateClient("player", 2),
            await CreateClient("player", 3),
        };

        foreach (var cl in clients)
        {
            var ctx = cl;
            ctx.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
            {
                var t = new TrackedTarget();
                ctx.Targets[wrapper.Id] = t;
                return t;
            });
        }

        MoveClientWatcher(clients[0], V(0, 0), 400);
        MoveClientWatcher(clients[1], V(500, 0), 400);
        MoveClientWatcher(clients[2], V(1000, 0), 400);

        using var cts = new CancellationTokenSource();
        foreach (var cl in clients)
            _ = RunClientTickLoop(cl, cts.Token);
        await Task.Delay(2500);

        static List<long> Alive(IRegionClientModule m) =>
            m.GetUnits<TrackedWatcherUnitClient, TrackedTarget>().Select(u => u.Id).ToList();

        // Create 5 units at various positions
        var unit1 = await CreateUnit(1, V(0, 0));
        var unit2 = await CreateUnit(2, V(300, 0));
        var unit3 = await CreateUnit(3, V(500, 0));
        var unit4 = await CreateUnit(4, V(700, 0));
        var unit5 = await CreateUnit(5, V(1200, 0));
        await Task.Delay(3000);

        // Expected: C1 sees 1,2 | C2 sees 2,3,4 | C3 sees 4,5
        var c1Alive = Alive(clients[0].RegionClientModule);
        Assert.True(c1Alive.Contains(1L));
        Assert.True(c1Alive.Contains(2L));
        Assert.True(!c1Alive.Contains(3L));
        Assert.True(!c1Alive.Contains(5L));

        var c2Alive = Alive(clients[1].RegionClientModule);
        Assert.True(c2Alive.Contains(2L));
        Assert.True(c2Alive.Contains(3L));
        Assert.True(c2Alive.Contains(4L));
        Assert.True(!c2Alive.Contains(1L));

        var c3Alive = Alive(clients[2].RegionClientModule);
        Assert.True(c3Alive.Contains(4L));
        Assert.True(c3Alive.Contains(5L));
        Assert.True(!c3Alive.Contains(1L));

        // Commits reach correct watchers: unit 2 at (300,0) → C1 and C2
        var commitC1U2 = new ManualResetEventSlim();
        var commitC2U2 = new ManualResetEventSlim();
        clients[0].Targets[2].CommitSignal = commitC1U2;
        clients[1].Targets[2].CommitSignal = commitC2U2;
        unit2.Commit("u2_overlap");
        MirrorProtoBus.Flush();
        Assert.True(commitC1U2.Wait(10000), "C1 missed commit from unit 2");
        Assert.True(commitC2U2.Wait(10000), "C2 missed commit from unit 2");
        Assert.Equal("u2_overlap", clients[0].Targets[2].LastCommit);
        Assert.Equal("u2_overlap", clients[1].Targets[2].LastCommit);

        // Move unit 4 from (700,0) to (200,0): leaves C3, enters C1
        unit4.SetPosition(200, 0);
        await Task.Delay(2500);

        c1Alive = Alive(clients[0].RegionClientModule);
        c3Alive = Alive(clients[2].RegionClientModule);
        Assert.True(c1Alive.Contains(4L), "C1 should have unit 4 after move");
        Assert.True(!c3Alive.Contains(4L), "C3 should not have unit 4 after move");

        // Commit from unit 4 now reaches C1 and C2 (not C3)
        var commitC1U4 = new ManualResetEventSlim();
        var commitC2U4 = new ManualResetEventSlim();
        clients[0].Targets[4].CommitSignal = commitC1U4;
        clients[1].Targets[4].CommitSignal = commitC2U4;
        unit4.Commit("u4_moved");
        MirrorProtoBus.Flush();
        Assert.True(commitC1U4.Wait(10000), "C1 missed commit from moved unit 4");
        Assert.True(commitC2U4.Wait(10000), "C2 missed commit from moved unit 4");

        // Final state verification
        var final1 = Alive(clients[0].RegionClientModule);
        Assert.True(final1.Contains(1L));
        Assert.True(final1.Contains(2L));
        Assert.True(final1.Contains(4L));

        var final2 = Alive(clients[1].RegionClientModule);
        Assert.True(final2.Contains(2L));
        Assert.True(final2.Contains(3L));
        Assert.True(final2.Contains(4L));

        var final3 = Alive(clients[2].RegionClientModule);
        Assert.True(final3.Contains(5L));
    }

    private async Task RunTickLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { _regionClientModule.Tick(); } catch { }
            try { await Task.Delay(10, ct); } catch { break; }
        }
    }
}

// ===== Target classes =====

public sealed class ServerTarget : ServerAlive, PubSubLib.ISetRegionUnit<RemoveWatcherUnitServer, ServerTarget>, IRegionUnitOnStart, IRegionUnitOnDestroy
    {
        public bool IsAlive { get; set; } = true;
        public RemoveWatcherUnitServer? RegionUnit;

        public ManualResetEventSlim? InitSignal;
        public ManualResetEventSlim? StartSignal;
        public ManualResetEventSlim? DestroySignal;

        public void SetRegionUnit(RemoveWatcherUnitServer region)
        {
            RegionUnit = region;
            InitSignal?.Set();
        }

        public void OnUnitStart()
        {
            StartSignal?.Set();
        }

        public void OnUnitDestroy()
        {
            DestroySignal?.Set();
        }
    }

    public sealed class RemoveWatcherTarget : ClientAlive, PubSubLib.Client.ISetRegionUnit<RemoveWatcherUnitClient, RemoveWatcherTarget>, IRegionOnStart, IRegionOnDestroy, IRegionOnCommit
    {
        public bool IsAlive { get; set; } = true;
        public RemoveWatcherUnitClient? RegionUnit;

        public ManualResetEventSlim? DestroySignal;
        public Action? OnCommitCallback;
        public RemoveWatcherUnitClient? MirrorUnit;

        public void SetRegionUnit(RemoveWatcherUnitClient region)
        {
            RegionUnit = region;
            MirrorUnit = region;
        }

        public void OnStartUnit()
        {
        }

        public void OnDestroyUnit()
        {
            DestroySignal?.Set();
        }

        public void OnCommitUnit(string commit)
        {
            OnCommitCallback?.Invoke();
        }
    }

    internal sealed class ClientContext(string user, long id) : IDisposable
    {
        public string User => user;
        public long Id => id;
        public long WatcherId => id;
        public IClient ClientConn = null!;
        public IRegionClientModule RegionClientModule = null!;
        public readonly ConcurrentDictionary<long, TrackedTarget> Targets = new();

        public void Dispose()
        {
            RegionClientModule = null!;
            try { ClientConn?.DisposeAsync().GetAwaiter().GetResult(); } catch { }
        }
    }

    public sealed class TrackedTarget : ClientAlive, PubSubLib.Client.ISetRegionUnit<TrackedWatcherUnitClient, TrackedTarget>, IRegionOnStart, IRegionOnDestroy, IRegionOnCommit
    {
        public bool IsAlive { get; set; } = true;
        public TrackedWatcherUnitClient? RegionUnit;
        public TrackedWatcherUnitClient? MirrorUnit;

        public ManualResetEventSlim? CreateSignal;
        public ManualResetEventSlim? DestroySignal;
        public ManualResetEventSlim? CommitSignal;
        public string? LastCommit;
        public int CommitCount;

        public void SetRegionUnit(TrackedWatcherUnitClient region)
        {
            RegionUnit = region;
            MirrorUnit = region;
            CreateSignal?.Set();
        }

        public void OnStartUnit() { }

        public void OnDestroyUnit()
        {
            IsAlive = false;
            DestroySignal?.Set();
        }

        public void OnCommitUnit(string commit)
        {
            LastCommit = commit;
            CommitCount++;
            CommitSignal?.Set();
        }
    }

    internal sealed class RegionPlayer(string id, string name) : IUser
    {
        public string Name => name;
        public string Id => id;
    }

using System.Collections.Concurrent;
using MyConnection;
using Natify;
using PubSubLib;
using PubSubLib.Client;
using PubSubLib.Messages;
using PubSubLib.Mirror;
using PubSubLib.Router;
using Xunit.Abstractions;
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

[UnitMirrorServer(typeof(UnitDataMsg), UnitType = "unit_data", Target = typeof(UnitDataServerTarget))]
public partial class UnitDataUnitServer
{
}

[UnitMirrorClient(typeof(UnitDataMsg), UnitType = "unit_data", Target = typeof(UnitDataClientTarget))]
public partial class UnitDataUnitClient
{
}

public class RegionTestFullStack : IAsyncLifetime
{
    private readonly ITestOutputHelper _testOutputHelper;
    private const string NatsUrl = "nats://localhost:4222";

    private INatifyServer _natifyServer = null!;
    private INatifyClient _natifyClient = null!;
    private IClient _clientConn;
    private IServer _serverConn;
    private IRegionModule _regionModule;
    private IRegionClientModule _regionClientModule;

    public RegionTestFullStack(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }


    public Task InitializeAsync() => Task.CompletedTask;

    private static Vector2 V(float x, float y) => new() { x = x, y = y };

    private async Task<(IServer myConnection, INatifyServer natifyServer, IRegionRouterModule regionRouter)>
        CreateRouterAsync()
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

        _natifyServer = await INatifyServer.CreateAsync(NatsUrl, "SyncRouter", "SyncGroup", "SyncServer");
        _serverConn.OnLogin<StringValue>(body =>
        {
            var spl = body.Value.Split('_');
            var user = spl[0];
            var id = spl[1];
            return Task.FromResult<IUser>(new RegionPlayer(id, user));
        });
        var regionRoute = IRegionRouterModule.Create(_natifyServer, "VN");
        _serverConn.AddModule(regionRoute);

        return (_serverConn, _natifyServer, regionRoute);
    }

    private async Task<IRegionModule> CreateUnityServerAsync()
    {
        _natifyClient = await INatifyClient.Create(NatsUrl, "SyncServer", "ServerGroup", "VN", "SyncRouter");
        _regionModule = IRegionModule.Create(new RegionConfig(_natifyClient)
        {
            GridSize = 100f,
            WatcherTimeoutSeconds = 600
        });
        Thread.Sleep(2000);
        return _regionModule;
    }

    private Task<RemoveWatcherUnitServer> CreateUnit(ConcurrentQueue<Action> queue, long unitId, Vector2 position)
    {
        TaskCompletionSource<RemoveWatcherUnitServer> tcs = new TaskCompletionSource<RemoveWatcherUnitServer>();
        queue.Enqueue(() =>
        {
            var target = new ServerTarget();
            tcs.SetResult(_regionModule.CreateUnit<RemoveWatcherUnitServer, ServerTarget>(unitId, position, target));
        });
        return tcs.Task;
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
        ctx.RegionClientModule = IRegionClientModule.Create(new PubSubLib.Client.Config
        {
            PingIntervalMs = 300
        });
        ctx.ClientConn.AddModule(ctx.RegionClientModule);
        await ctx.ClientConn.Login(() => new StringValue { Value = $"{user}_{id}" });
        await ctx.ClientConn.ConnectServer();
        return ctx;
    }

    private static void MoveClientWatcher(ClientContext ctx, Vector2 position, float range)
    {
        ctx.RegionClientModule.MoveWatcher(position, range);
    }

    private static async Task RunTickLoop(ClientContext ctx, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                ctx.RegionClientModule.Tick();
            }
            catch
            {
            }

            try
            {
                await Task.Delay(10, ct);
            }
            catch
            {
                break;
            }
        }
    }

    public async Task DisposeAsync()
    {
        _regionClientModule = null!;
        _clientConn?.DisposeAsync().GetAwaiter().GetResult();
        _serverConn?.DisposeAsync().GetAwaiter().GetResult();
        if (_regionModule != null)
            await _regionModule.DisposeAsync();
        if (_natifyServer != null)
            await _natifyServer.DisposeAsync();
        if (_natifyClient != null)
            await _natifyClient.DisposeAsync();
    }

    [Fact]
    public async Task FullStack_CreateUnit_ClientReceivesUnit()
    {
        var unitId = 1L;
        var signal = new ManualResetEventSlim();

        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        queue.Enqueue(() =>
        {
            clientA.RegionClientModule.OnCreateUnit<RemoveWatcherUnitClient, RemoveWatcherTarget>(wrapper =>
            {
                var target = new RemoveWatcherTarget();
                if (unitId == 1L)
                    signal.Set();
                return target;
            });
        });

        await Task.Delay(1000);

        await CreateUnit(queue, unitId, V(50, 50));

        Assert.True(signal.Wait(5000), $"Client did NOT receive unit {unitId}");

        await process.CancelAsync();
    }

    [Fact]
    public async Task FullStack_Commit_ClientSyncsMirrorFields()
    {
        var unitId = 1L;
        var createdSignal = new ManualResetEventSlim();
        var commitSignal = new ManualResetEventSlim();

        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        queue.Enqueue(() =>
        {
            clientA.RegionClientModule.OnCreateUnit<RemoveWatcherUnitClient, RemoveWatcherTarget>(wrapper =>
            {
                var target = new RemoveWatcherTarget
                {
                    OnCommitCallback = () => commitSignal.Set()
                };
                createdSignal.Set();
                return target;
            });
        });


        await Task.Delay(1000);

        var unit = await CreateUnit(queue, unitId, V(50, 50));

        Assert.True(createdSignal.Wait(5000), "Stage 1 failed: unit not received");

        unit.WatcherId = 42;
        unit.Commit("test_commit");
        MirrorProtoBus.Flush();

        Assert.True(commitSignal.Wait(10000), "Stage 2 failed: commit not received");
        await process.CancelAsync();
    }

    [Fact]
    public async Task FullStack_DestroyUnit_ClientRemovesUnit()
    {
        var unitId = 1L;
        var createdSignal = new ManualResetEventSlim();
        var destroyedSignal = new ManualResetEventSlim();

        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        queue.Enqueue(() =>
        {
            clientA.RegionClientModule.OnCreateUnit<RemoveWatcherUnitClient, RemoveWatcherTarget>(wrapper =>
            {
                var target = new RemoveWatcherTarget
                {
                    DestroySignal = destroyedSignal
                };
                createdSignal.Set();
                _testOutputHelper.WriteLine("Create Done");
                return target;
            });
        });

        await Task.Delay(1000);

        await CreateUnit(queue, unitId, V(50, 50));

        Assert.True(createdSignal.Wait(5000), "Stage 1 failed");

        queue.Enqueue(() => server.DestroyUnit<RemoveWatcherUnitServer, ServerTarget>(unitId));
        await Task.Delay(1000);
        Assert.True(destroyedSignal.Wait(5000), "Stage 2 failed: destroy not received");
        await process.CancelAsync();
    }

    [Fact]
    public async Task FullStack_Lifecycle_OnStartOnDestroy()
    {
        var unitId = 1L;
        var serverStartSignal = new ManualResetEventSlim();
        var serverInitSignal = new ManualResetEventSlim();
        var serverDestroySignal = new ManualResetEventSlim();

        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);


        queue.Enqueue(() =>
        {
            var target = new ServerTarget
            {
                StartSignal = serverStartSignal,
                InitSignal = serverInitSignal,
                DestroySignal = serverDestroySignal
            };

            var unit = _regionModule.CreateUnit<RemoveWatcherUnitServer, ServerTarget>(unitId, V(50, 50),
                target);
        });


        Assert.True(serverInitSignal.Wait(5000), "ISetRegionUnit.SetRegionUnit not called");
        Assert.True(serverStartSignal.Wait(5000), "IRegionUnitOnStart.OnUnitStart not called");

        queue.Enqueue(() =>
        {
            _regionModule.DestroyUnit<RemoveWatcherUnitServer, ServerTarget>(unitId);
            _regionModule.Tick();
        });

        await Task.Delay(1000);

        Assert.True(serverDestroySignal.Wait(5000), "IRegionUnitOnDestroy.OnUnitDestroy not called");
        await process.CancelAsync();
    }

    // ===== Multi-unit / multi-client integration tests =====

    [Fact]
    public async Task FullStack_MultipleUnits_SingleClient()
    {
        const int unitCount = 5;
        var createSignals = new ManualResetEventSlim[unitCount + 1];
        for (int i = 1; i <= unitCount; i++)
            createSignals[i] = new ManualResetEventSlim();

        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        queue.Enqueue(() =>
        {
            clientA.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
            {
                var t = new TrackedTarget();
                clientA.Targets[wrapper.Id] = t;
                if (wrapper.Id > 0 && wrapper.Id <= unitCount)
                    createSignals[wrapper.Id].Set();
                return t;
            });

            MoveClientWatcher(clientA, V(0, 0), 1000);
        });


        await Task.Delay(1000);


        for (long i = 1; i <= unitCount; i++)
        {
            await CreateUnit(queue, i, V(i * 20, i * 20));
            Assert.True(createSignals[i].Wait(10000), $"Unit {i} not created on client");
        }


        await Task.Delay(500);

        IList<TrackedWatcherUnitClient> units = [];

        queue.Enqueue(() =>
        {
            units = clientA.RegionClientModule.GetUnits<TrackedWatcherUnitClient, TrackedTarget>();
        });

        await Task.Delay(1000);

        Assert.Equal(unitCount, units.Count);
        var ids = new HashSet<long>(units.Select(u => u.Id));
        for (long i = 1; i <= unitCount; i++)
            Assert.Contains(i, ids);

        await process.CancelAsync();
    }

    [Fact]
    public async Task FullStack_MultipleClients_UnitCoverage()
    {
        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        queue.Enqueue(() =>
        {
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
        });
        await CreateUnit(queue, 1, V(0, 0));
        await CreateUnit(queue, 2, V(300, 0));
        await CreateUnit(queue, 3, V(700, 0));
        await CreateUnit(queue, 4, V(1200, 0));
        await Task.Delay(1000);

        Task<HashSet<long>> AliveIds(IRegionClientModule mod)
        {
            TaskCompletionSource<HashSet<long>> tcs = new TaskCompletionSource<HashSet<long>>();
            queue.Enqueue(() =>
            {
                var units = mod.GetUnits<TrackedWatcherUnitClient, TrackedTarget>();
                tcs.SetResult(new HashSet<long>(units.Select(u => u.Id)));
            });
            return tcs.Task;
        }

        var idsA = await AliveIds(clientA.RegionClientModule);
        Assert.Equal(2, idsA.Count);
        Assert.True(idsA.Contains(1L));
        Assert.True(idsA.Contains(2L));

        var idsB = await AliveIds(clientB.RegionClientModule);
        Assert.Equal(2, idsB.Count);
        Assert.True(idsB.Contains(2L));
        Assert.True(idsB.Contains(3L));

        var idsC = await AliveIds(clientC.RegionClientModule);
        Assert.Equal(2, idsC.Count);
        Assert.True(idsC.Contains(3L));
        Assert.True(idsC.Contains(4L));

        await process.CancelAsync();
    }

    [Fact]
    public async Task FullStack_WatcherMove_EnterExit()
    {
        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        var createSigsA = new Dictionary<long, ManualResetEventSlim>();
        var createSigsB = new Dictionary<long, ManualResetEventSlim>();

        queue.Enqueue(() =>
        {
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
        });

        await Task.Delay(1000);

        // Phase 1: create units at (100,0) for A and (400,0) for B
        createSigsA[1] = new ManualResetEventSlim();
        createSigsB[2] = new ManualResetEventSlim();

        await CreateUnit(queue, 1, V(100, 0));
        await CreateUnit(queue, 2, V(400, 0));

        await Task.Delay(1000);

        Assert.True(createSigsA[1].Wait(10000), "A did not see unit 1");
        Assert.True(createSigsB[2].Wait(10000), "B did not see unit 2");


        var aInit = await Alive(queue, clientA.RegionClientModule);
        Assert.True(aInit.Contains(1L));
        Assert.True(!aInit.Contains(2L));

        var bInit = await Alive(queue, clientB.RegionClientModule);
        Assert.True(bInit.Contains(2L));
        Assert.True(!bInit.Contains(1L));

        // Phase 2: move watcher A from (0,0) to (500,0) range 200
        // Unit 1 (100,0) leaves A; Unit 2 (400,0) enters A
        var aLostUnit1 = new ManualResetEventSlim();
        clientA.Targets[1].DestroySignal = aLostUnit1;
        createSigsA[2] = new ManualResetEventSlim();
        queue.Enqueue(() => MoveClientWatcher(clientA, V(500, 0), 200));

        await Task.Delay(1000);

        Assert.True(aLostUnit1.Wait(10000), "A did not lose unit 1 after watcher move");
        Assert.True(createSigsA[2].Wait(10000), "A did not gain unit 2 after watcher move");

        var aAfter = await Alive(queue, clientA.RegionClientModule);
        Assert.True(!aAfter.Contains(1L), "A should not have unit 1 after move");
        Assert.True(aAfter.Contains(2L), "A should have unit 2 after move");

        var bAfter = await Alive(queue, clientB.RegionClientModule);
        Assert.True(bAfter.Contains(2L), "B should still have unit 2");
        Assert.True(!bAfter.Contains(1L), "B should still not have unit 1");

        await process.CancelAsync();
    }

    [Fact]
    public async Task FullStack_UnitMove_EnterExit()
    {
        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        queue.Enqueue(() =>
        {
            clientA.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
            {
                var t = new TrackedTarget();
                clientA.Targets[wrapper.Id] = t;
                return t;
            });

            MoveClientWatcher(clientA, V(250, 0), 100);
        });

        await Task.Delay(1000);


        // Create unit at (0,0) — outside watcher range (cells 1-3, x∈[100,400))

        var unit = await CreateUnit(queue, 1, V(0, 0));
        await Task.Delay(1000);

        var outOfRange = await Alive(queue, clientA.RegionClientModule);
        Assert.True(!outOfRange.Contains(1L), "Unit should be dead outside range");

        // Move unit into range (x=200 → cell 2)
        unit.SetPosition(200, 0);
        await Task.Delay(1000);

        var inRange = await Alive(queue, clientA.RegionClientModule);
        Assert.True(inRange.Contains(1L), "Unit should be alive after entering range");

        // Move unit out of range (x=500 → cell 5)
        unit.SetPosition(500, 0);
        await Task.Delay(1000);

        var outAgain = await Alive(queue, clientA.RegionClientModule);
        Assert.True(!outAgain.Contains(1L), "Unit should be dead after leaving range");

        await process.CancelAsync();
    }

    [Fact]
    public async Task FullStack_Commit_OnlyCorrectWatchers()
    {
        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        queue.Enqueue(() =>
        {
            clientA.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
            {
                var t = new TrackedTarget { Id = wrapper.Id };
                clientA.Targets[wrapper.Id] = t;
                return t;
            });

            clientB.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
            {
                var t = new TrackedTarget { Id = wrapper.Id };
                clientB.Targets[wrapper.Id] = t;
                return t;
            });

            MoveClientWatcher(clientA, V(0, 0), 200);
            MoveClientWatcher(clientB, V(500, 0), 200);
        });

        await Task.Delay(1000);

        // Create unit at (100,0) → cell 1 → only A covers it
        var unit = await CreateUnit(queue, 1, V(100, 0));
        await Task.Delay(1000);

        var aliveA = await Alive(queue, clientA.RegionClientModule);
        var aliveB = await Alive(queue, clientB.RegionClientModule);
        Assert.True(aliveA.Contains(1L), "A should have unit alive");
        Assert.True(!aliveB.Contains(1L), "B should not have unit alive");

        // Commit: only A should receive
        var commitA = new ManualResetEventSlim(false);
        clientA.Targets[1].CommitSignal = commitA;

        queue.Enqueue(() =>
        {
            unit.WatcherId = 99;
            unit.Commit("only_A");
        });

        await Task.Delay(1000);

        Assert.True(commitA.Wait(10000), "A did not receive commit");
        Assert.Equal("only_A", clientA.Targets[1].LastCommit);
        Assert.Equal(99, clientA.Targets[1].MirrorUnit!.WatcherId);

        await process.CancelAsync();
    }

    private Task<(IRegionModule server,
            ClientContext clientA,
            ClientContext clientB,
            ClientContext clientC,
            ConcurrentQueue<Action> queue)>
        CreateBase(CancellationTokenSource process)
    {
        TaskCompletionSource<(IRegionModule server,
            ClientContext clientA,
            ClientContext clientB,
            ClientContext clientC,
            ConcurrentQueue<Action> queue)> connection = new();
        var queue = new ConcurrentQueue<Action>();

        IRegionModule? server;
        ClientContext? clientA;
        ClientContext? clientB;
        ClientContext? clientC;

        _ = Task.Run(async () =>
        {
            var router = await CreateRouterAsync();
            server = await CreateUnityServerAsync();
            clientA = await CreateClient("player-1", 1);
            clientB = await CreateClient("player-2", 2);
            clientC = await CreateClient("player-3", 3);

            connection.SetResult((server, clientA, clientB, clientC, queue));

            while (!process.IsCancellationRequested)
            {
                server.Tick();
                clientA.RegionClientModule?.Tick();
                clientB.RegionClientModule?.Tick();
                clientC.RegionClientModule?.Tick();
                while (queue.TryDequeue(out var action))
                {
                    action();
                }

                await Task.Delay(10, process.Token);
            }

            await server.DisposeAsync();
            clientA.Dispose();
            clientB.Dispose();
            clientC.Dispose();
            await router.myConnection.DisposeAsync();
            await router.natifyServer.DisposeAsync();
        }, process.Token);
        return connection.Task;
    }


    [Fact]
    public async Task FullStack_UnitMove_CommitFollowsUnit()
    {
        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        queue.Enqueue(() =>
        {
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
        });

        // Phase 1: unit at (100,0) → only A sees it
        var unit1 = await CreateUnit(queue, 1, V(100, 0));

        await Task.Delay(1000);

        Assert.True((await Alive(queue, clientA.RegionClientModule)).Contains(1L), "A should have unit");
        Assert.True(!(await Alive(queue, clientB.RegionClientModule)).Contains(1L), "B should not have unit");

        // Only A receives commit
        var commitA = new ManualResetEventSlim(false);

        clientA.Targets[1].CommitSignal = commitA;

        queue.Enqueue(() => unit1.Commit("msg_a"));

        await Task.Delay(1000);

        Assert.True(commitA.Wait(10000), "A did not receive first commit");
        Assert.Equal("msg_a", clientA.Targets[1].LastCommit);

        // Phase 2: move unit to (400,0) → enters B's range, leaves A's
        queue.Enqueue(() => unit1.SetPosition(400, 0));

        await Task.Delay(1000);

        Assert.True(!(await Alive(queue, clientA.RegionClientModule)).Contains(1L), "A should have lost unit");
        Assert.True((await Alive(queue, clientB.RegionClientModule)).Contains(1L), "B should have gained unit");

        // Only B receives commit now
        var commitB = new ManualResetEventSlim();
        clientB.Targets[1].CommitSignal = commitB;
        queue.Enqueue(() => unit1.Commit("msg_b"));

        await Task.Delay(1000);

        Assert.True(commitB.Wait(10000), "B did not receive commit after unit moved");
        Assert.Equal("msg_b", clientB.Targets[1].LastCommit);

        await process.CancelAsync();
    }


    [Fact]
    public async Task FullStack_WatcherMove_CommitReach()
    {
        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        var createSigsA = new Dictionary<long, ManualResetEventSlim>();

        queue.Enqueue(() =>
        {
            clientA.RegionClientModule.OnCreateUnit<TrackedWatcherUnitClient, TrackedTarget>(wrapper =>
            {
                var t = new TrackedTarget();
                clientA.Targets[wrapper.Id] = t;
                if (createSigsA.TryGetValue(wrapper.Id, out var sig))
                    sig.Set();
                return t;
            });

            MoveClientWatcher(clientA, V(0, 0), 200);
        });

        await Task.Delay(1000);

        // Create unit at (100,0) → watcher A covers it
        createSigsA[1] = new ManualResetEventSlim();
        var unit = await CreateUnit(queue, 1, V(100, 0));
        await Task.Delay(1000);

        Assert.True(createSigsA[1].Wait(10000), "A did not see unit");
        Assert.Contains(1L, await Alive(queue, clientA.RegionClientModule));

        // Commit 1: A receives
        var commit1 = new ManualResetEventSlim();
        clientA.Targets[1].CommitSignal = commit1;

        queue.Enqueue(() => unit.Commit("first"));

        await Task.Delay(1000);

        Assert.True(commit1.Wait(10000), "A did not receive first commit");
        Assert.Equal("first", clientA.Targets[1].LastCommit);

        // Move watcher A to (500,0) range 200 → unit at (100,0) leaves A

        queue.Enqueue(() => MoveClientWatcher(clientA, V(500, 0), 200));
        await Task.Delay(1000);

        Assert.True(!(await Alive(queue, clientA.RegionClientModule)).Contains(1L),
            "Unit should not be alive after watcher move");

        // Commit 2: A should NOT receive (target is dead)
        queue.Enqueue(() => unit.Commit("second"));
        await Task.Delay(1000);
        Assert.NotEqual("second", clientA.Targets[1].LastCommit);

        // Move watcher A back to (0,0) range 200 → unit re-enters
        createSigsA[1] = new ManualResetEventSlim();
        queue.Enqueue(() => MoveClientWatcher(clientA, V(0, 0), 200));
        await Task.Delay(1000);

        Assert.True(createSigsA[1].Wait(10000), "A did not regain unit after watcher moved back");
        Assert.Contains(1L, await Alive(queue, clientA.RegionClientModule));

        // Commit 3: A receives on new target
        var commit3 = new ManualResetEventSlim();
        clientA.Targets[1].CommitSignal = commit3;
        queue.Enqueue(() => unit.Commit("third"));
        await Task.Delay(1000);
        Assert.True(commit3.Wait(10000), "A did not receive third commit");
        Assert.Equal("third", clientA.Targets[1].LastCommit);
        await process.CancelAsync();
    }

    private static Task<List<long>> Alive(ConcurrentQueue<Action> queue, IRegionClientModule m)
    {
        TaskCompletionSource<List<long>> tcs = new TaskCompletionSource<List<long>>();
        queue.Enqueue(() =>
        {
            tcs.SetResult(m.GetUnits<TrackedWatcherUnitClient, TrackedTarget>().Select(u => u.Id).ToList());
        });
        return tcs.Task;
    }


    [Fact]
    public async Task FullStack_MultiUnit_MultiClient_Complex()
    {
        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        var clients = new[] { clientA, clientB, clientC };

        queue.Enqueue(() =>
        {
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
        });

        await Task.Delay(1000);


        // Create 5 units at various positions
        var unit1 = await CreateUnit(queue, 1, V(0, 0));
        var unit2 = await CreateUnit(queue, 2, V(300, 0));
        var unit3 = await CreateUnit(queue, 3, V(500, 0));
        var unit4 = await CreateUnit(queue, 4, V(700, 0));
        var unit5 = await CreateUnit(queue, 5, V(1200, 0));
        await Task.Delay(3000);

        // Expected: C1 sees 1,2 | C2 sees 2,3,4 | C3 sees 4,5
        var c1Alive = await Alive(queue, clients[0].RegionClientModule);
        Assert.True(c1Alive.Contains(1L));
        Assert.True(c1Alive.Contains(2L));
        Assert.True(!c1Alive.Contains(3L));
        Assert.True(!c1Alive.Contains(5L));

        var c2Alive = await Alive(queue, clients[1].RegionClientModule);
        Assert.True(c2Alive.Contains(2L));
        Assert.True(c2Alive.Contains(3L));
        Assert.True(c2Alive.Contains(4L));
        Assert.True(!c2Alive.Contains(1L));

        var c3Alive = await Alive(queue, clients[2].RegionClientModule);
        Assert.True(c3Alive.Contains(4L));
        Assert.True(c3Alive.Contains(5L));
        Assert.True(!c3Alive.Contains(1L));

        // Commits reach correct watchers: unit 2 at (300,0) → C1 and C2
        var commitC1U2 = new ManualResetEventSlim();
        var commitC2U2 = new ManualResetEventSlim();
        clients[0].Targets[2].CommitSignal = commitC1U2;
        clients[1].Targets[2].CommitSignal = commitC2U2;
        queue.Enqueue(() => unit2.Commit("u2_overlap"));
        await Task.Delay(1000);
        Assert.True(commitC1U2.Wait(10000), "C1 missed commit from unit 2");
        Assert.True(commitC2U2.Wait(10000), "C2 missed commit from unit 2");
        Assert.Equal("u2_overlap", clients[0].Targets[2].LastCommit);
        Assert.Equal("u2_overlap", clients[1].Targets[2].LastCommit);

        // Move unit 4 from (700,0) to (200,0): leaves C3, enters C1
        queue.Enqueue(() => unit4.SetPosition(200, 0));
        await Task.Delay(1000);

        c1Alive = await Alive(queue, clients[0].RegionClientModule);
        c3Alive = await Alive(queue, clients[2].RegionClientModule);
        Assert.True(c1Alive.Contains(4L), "C1 should have unit 4 after move");
        Assert.True(!c3Alive.Contains(4L), "C3 should not have unit 4 after move");

        // Commit from unit 4 now reaches C1 and C2 (not C3)
        var commitC1U4 = new ManualResetEventSlim();
        var commitC2U4 = new ManualResetEventSlim();
        clients[0].Targets[4].CommitSignal = commitC1U4;
        clients[1].Targets[4].CommitSignal = commitC2U4;
        queue.Enqueue(() => unit4.Commit("u4_moved"));
        await Task.Delay(1000);
        Assert.True(commitC1U4.Wait(10000), "C1 missed commit from moved unit 4");
        Assert.True(commitC2U4.Wait(10000), "C2 missed commit from moved unit 4");

        // Final state verification
        var final1 = await Alive(queue, clients[0].RegionClientModule);
        Assert.True(final1.Contains(1L));
        Assert.True(final1.Contains(2L));
        Assert.True(final1.Contains(4L));

        var final2 = await Alive(queue, clients[1].RegionClientModule);
        Assert.True(final2.Contains(2L));
        Assert.True(final2.Contains(3L));
        Assert.True(final2.Contains(4L));

        var final3 = await Alive(queue, clients[2].RegionClientModule);
        Assert.True(final3.Contains(5L));

        await process.CancelAsync();
    }


    [Fact]
    public async Task FullStack_UnitData_Description_ClientReceivesCorrectData_InCallBack()
    {
        var process = new CancellationTokenSource();
        var (server, clientA, clientB, clientC, queue) = await CreateBase(process);

        var createdSignal = new ManualResetEventSlim();
        var commitSignal = new ManualResetEventSlim();
        UnitDataClientTarget? clientTarget = null;

        queue.Enqueue(async () =>
        {
            clientA.RegionClientModule.OnCreateUnit<UnitDataUnitClient, UnitDataClientTarget>(wrapper =>
            {
                clientTarget = new UnitDataClientTarget { CommitSignal = commitSignal };
                createdSignal.Set();
                return clientTarget;
            });

            MoveClientWatcher(clientA, V(50, 50), 200);

            await Task.Delay(1000);

            var serverTarget = new UnitDataServerTarget();
            _regionModule.CreateUnit<UnitDataUnitServer, UnitDataServerTarget>(
                1, V(50, 50), serverTarget, serverUnit =>
                {
                    serverUnit.Name = "Hero";
                    serverUnit.Description = "A brave warrior";
                    serverUnit.Health = 100;
                    serverUnit.Level = 5;
                });
        });

        Assert.True(createdSignal.Wait(5000), "Client did not receive unit enter");

        await Task.Delay(1500);


        MirrorProtoBus.Flush();

        // Assert.True(commitSignal.Wait(5000), "Client did not receive commit");
        Assert.NotNull(clientTarget!.MirrorUnit);
        Assert.Equal("Hero", clientTarget.MirrorUnit!.Name);
        Assert.Equal("A brave warrior", clientTarget.MirrorUnit.Description);
        Assert.Equal(100, clientTarget.MirrorUnit.Health);
        Assert.Equal(5, clientTarget.MirrorUnit.Level);

        await process.CancelAsync();
    }
}

// ===== Target classes =====

public sealed class ServerTarget : ServerAlive, PubSubLib.ISetRegionUnit<RemoveWatcherUnitServer, ServerTarget>,
    IRegionUnitOnStart, IRegionUnitOnDestroy
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

public sealed class RemoveWatcherTarget : ClientAlive,
    PubSubLib.Client.ISetRegionUnit<RemoveWatcherUnitClient, RemoveWatcherTarget>, IRegionOnStart, IRegionOnDestroy,
    IRegionOnCommit
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
        try
        {
            ClientConn?.DisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }
}

public sealed class TrackedTarget : ClientAlive,
    PubSubLib.Client.ISetRegionUnit<TrackedWatcherUnitClient, TrackedTarget>, IRegionOnStart, IRegionOnDestroy,
    IRegionOnCommit
{
    public long Id;
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

    public void OnStartUnit()
    {
    }

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

// ===== UnitData targets =====

public sealed class UnitDataServerTarget : ServerAlive,
    PubSubLib.ISetRegionUnit<UnitDataUnitServer, UnitDataServerTarget>, IRegionUnitOnStart, IRegionUnitOnDestroy
{
    public bool IsAlive { get; set; } = true;
    public UnitDataUnitServer? RegionUnit;

    public void SetRegionUnit(UnitDataUnitServer region) => RegionUnit = region;

    public void OnUnitStart()
    {
    }

    public void OnUnitDestroy() => IsAlive = false;
}

public sealed class UnitDataClientTarget : ClientAlive,
    PubSubLib.Client.ISetRegionUnit<UnitDataUnitClient, UnitDataClientTarget>, IRegionOnCommit
{
    public bool IsAlive { get; set; } = true;
    public UnitDataUnitClient? RegionUnit;
    public UnitDataUnitClient? MirrorUnit;
    public ManualResetEventSlim? CommitSignal;
    public string? LastCommit;

    public void SetRegionUnit(UnitDataUnitClient region)
    {
        RegionUnit = region;
        MirrorUnit = region;
    }

    public void OnStartUnit()
    {
    }

    public void OnDestroyUnit() => IsAlive = false;

    public void OnCommitUnit(string commit)
    {
        LastCommit = commit;
        CommitSignal?.Set();
    }
}
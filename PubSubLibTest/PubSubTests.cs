using PubSubLib;

namespace PubSubLibTest;

class Player : IAlive
{
    public string Name = "";
    public bool IsAlive { get; set; } = true;
}

public class PubSubTests
{
    private static IPubSub CreatePubSub(float gridSize = 100f)
    {
        return IPubSub.Create(new PubSubConfig { GridSize = gridSize });
    }

    private static Vector2 V(float x, float y) => new Vector2 { x = x, y = y };

    [Fact]
    public void Create_And_Dispose()
    {
        var pubSub = CreatePubSub();
        Assert.NotNull(pubSub);
        pubSub.Dispose();
    }

    // ===== BatchEnter / BatchLeave =====

    [Fact]
    public async Task AddUnit_InRange_BatchEnter()
    {
        var signal = new ManualResetEventSlim();
        List<long>? watcherIds = null;
        IUnit? unit = null;

        var pubSub = CreatePubSub();
        try
        {
            Action<(List<long>, IUnit)> cb = tuple =>
            {
                watcherIds = new List<long>(tuple.Item1);
                unit = tuple.Item2;
                signal.Set();
            };
            pubSub.OnUnitEnter(cb);

            pubSub.AddWatcher(1, V(0, 0), 200);
            var player = new Player { Name = "A" };
            var u = await pubSub.CreateUnitAsync<Player>(42, "hero", V(50, 50), player);

            Assert.True(signal.Wait(5000));
            Assert.NotNull(watcherIds);
            Assert.Single(watcherIds);
            Assert.Contains(1L, watcherIds);
            Assert.NotNull(unit);
            Assert.Equal(42, unit.Id);
            Assert.Equal("hero", unit.Type);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task AddUnit_OutOfRange_NoEvent()
    {
        var signal = new ManualResetEventSlim();

        var pubSub = CreatePubSub();
        try
        {
            Action<(List<long>, IUnit)> cb = _ => signal.Set();
            pubSub.OnUnitEnter(cb);

            pubSub.AddWatcher(1, V(0, 0), 10);
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(1, "hero", V(200, 200), player);

            Assert.False(signal.Wait(1000));
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task RemoveUnit_InRange_BatchLeave()
    {
        var signal = new ManualResetEventSlim();
        List<long>? watcherIds = null;
        IUnit? unit = null;

        var pubSub = CreatePubSub();
        try
        {
            Action<(List<long>, IUnit)> cb = tuple =>
            {
                watcherIds = new List<long>(tuple.Item1);
                unit = tuple.Item2;
                signal.Set();
            };
            pubSub.OnUnitLeave(cb);

            pubSub.AddWatcher(1, V(0, 0), 200);
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(99, "npc", V(30, 30), player);
            u.Destroy();
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.NotNull(watcherIds);
            Assert.Contains(1L, watcherIds);
            Assert.NotNull(unit);
            Assert.Equal(99, unit.Id);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== SyncEnter / SyncLeave =====

    [Fact]
    public async Task AddWatcher_ExistingUnit_SyncEnter()
    {
        var signal = new ManualResetEventSlim();
        long watcherId = 0;
        List<IUnit>? units = null;

        var pubSub = CreatePubSub();
        try
        {
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(7, "mob", V(50, 50), player);

            Action<(long, List<IUnit>)> cb = tuple =>
            {
                watcherId = tuple.Item1;
                units = new List<IUnit>(tuple.Item2);
                signal.Set();
            };
            pubSub.OnUnitEnter(cb);

            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.Equal(1, watcherId);
            Assert.NotNull(units);
            Assert.Single(units);
            Assert.Equal(7, units[0].Id);
            Assert.Equal("mob", units[0].Type);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task RemoveWatcher_ExistingUnit_SyncLeave()
    {
        var enterSignal = new ManualResetEventSlim();
        var leaveSignal = new ManualResetEventSlim();
        long enterWatcherId = 0;
        List<IUnit>? enterUnits = null;

        var pubSub = CreatePubSub();
        try
        {
            pubSub.OnUnitEnter(tuple =>
            {
                enterWatcherId = tuple.Item1;
                enterUnits = new List<IUnit>(tuple.Item2);
                enterSignal.Set();
            });

            Action<(long, List<UnitKey>)> leaveCb = _ => leaveSignal.Set();
            pubSub.OnUnitLeave(leaveCb);

            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(5, "item", V(50, 50), player);

            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Assert.True(enterSignal.Wait(5000));
            Assert.Equal(1, enterWatcherId);
            Assert.NotNull(enterUnits);
            Assert.Single(enterUnits);
            Assert.Equal(5, enterUnits[0].Id);

            enterSignal.Reset();
            enterWatcherId = 0;
            enterUnits = null;

            pubSub.RemoveWatcher(1);
            await pubSub.FlushAsync();

            Assert.False(leaveSignal.Wait(1000), "SyncLeave must not fire when watcher is removed");

            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Assert.True(enterSignal.Wait(5000));
            Assert.Equal(1, enterWatcherId);
            Assert.NotNull(enterUnits);
            Assert.Single(enterUnits);
            Assert.Equal(5, enterUnits[0].Id);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== MoveWatcher =====

    [Fact]
    public async Task MoveWatcher_IntoRange_SyncEnter()
    {
        var signal = new ManualResetEventSlim();
        long watcherId = 0;
        List<IUnit>? units = null;

        var pubSub = CreatePubSub();
        try
        {
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(10, "hero", V(50, 50), player);

            pubSub.AddWatcher(1, V(500, 500), 50);
            await pubSub.FlushAsync();

            Action<(long, List<IUnit>)> cb = tuple =>
            {
                watcherId = tuple.Item1;
                units = new List<IUnit>(tuple.Item2);
                signal.Set();
            };
            pubSub.OnUnitEnter(cb);

            pubSub.MoveWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.Equal(1, watcherId);
            Assert.NotNull(units);
            Assert.Single(units);
            Assert.Equal(10, units[0].Id);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task MoveWatcher_OutOfRange_SyncLeave()
    {
        var signal = new ManualResetEventSlim();
        long watcherId = 0;
        List<UnitKey>? unitKeys = null;

        var pubSub = CreatePubSub();
        try
        {
            Action<(long, List<UnitKey>)> cb = tuple =>
            {
                watcherId = tuple.Item1;
                unitKeys = new List<UnitKey>(tuple.Item2);
                signal.Set();
            };
            pubSub.OnUnitLeave(cb);

            pubSub.AddWatcher(1, V(0, 0), 200);
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(20, "mob", V(50, 50), player);

            pubSub.MoveWatcher(1, V(500, 500), 50);
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.Equal(1, watcherId);
            Assert.NotNull(unitKeys);
            Assert.Single(unitKeys);
            Assert.Equal(20, unitKeys[0].Id);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== Position change (Unit moves) =====

    [Fact]
    public async Task UnitChangeCell_OutOfRange_BatchLeave()
    {
        var leaveSignal = new ManualResetEventSlim();
        List<long>? watcherIds = null;

        var pubSub = CreatePubSub();
        try
        {
            pubSub.AddWatcher(1, V(0, 0), 80);
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(1, "hero", V(50, 50), player);

            Action<(List<long>, IUnit)> cb = tuple =>
            {
                watcherIds = new List<long>(tuple.Item1);
                leaveSignal.Set();
            };
            pubSub.OnUnitLeave(cb);

            u.Position = V(200, 200);
            await pubSub.FlushAsync();

            Assert.True(leaveSignal.Wait(5000));
            Assert.NotNull(watcherIds);
            Assert.Contains(1L, watcherIds);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task UnitChangeCell_StillInRange_NoEvent()
    {
        var enterSignal = new ManualResetEventSlim();
        var leaveSignal = new ManualResetEventSlim();

        var pubSub = CreatePubSub();
        try
        {
            pubSub.AddWatcher(1, V(0, 0), 300);
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(1, "hero", V(50, 50), player);

            Action<(List<long>, IUnit)> enterCb = _ => enterSignal.Set();
            Action<(List<long>, IUnit)> leaveCb = _ => leaveSignal.Set();
            pubSub.OnUnitEnter(enterCb);
            pubSub.OnUnitLeave(leaveCb);

            u.Position = V(150, 150);
            await pubSub.FlushAsync();

            Assert.False(enterSignal.Wait(1000));
            Assert.False(leaveSignal.Wait(1000));
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task UnitPosition_IntoRange_BatchEnter()
    {
        var signal = new ManualResetEventSlim();
        List<long>? watcherIds = null;

        var pubSub = CreatePubSub();
        try
        {
            pubSub.AddWatcher(1, V(0, 0), 200);
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(3, "npc", V(500, 500), player);

            Action<(List<long>, IUnit)> cb = tuple =>
            {
                watcherIds = new List<long>(tuple.Item1);
                signal.Set();
            };
            pubSub.OnUnitEnter(cb);

            u.Position = V(50, 50);
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.NotNull(watcherIds);
            Assert.Contains(1L, watcherIds);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== WatcherPingUnits =====

    [Fact]
    public async Task PingUnits_Missing_SyncEnter()
    {
        var signal = new ManualResetEventSlim();
        long watcherId = 0;
        List<IUnit>? units = null;

        var pubSub = CreatePubSub();
        try
        {
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(8, "mob", V(50, 50), player);
            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Action<(long, List<IUnit>)> cb = tuple =>
            {
                watcherId = tuple.Item1;
                units = new List<IUnit>(tuple.Item2);
                signal.Set();
            };
            pubSub.OnUnitEnter(cb);

            pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>> { { "mob", new Dictionary<long, int>() } });
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.Equal(1, watcherId);
            Assert.NotNull(units);
            Assert.Single(units);
            Assert.Equal(8, units[0].Id);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task PingUnits_Extra_SyncLeave()
    {
        var signal = new ManualResetEventSlim();
        long watcherId = 0;
        List<UnitKey>? unitKeys = null;

        var pubSub = CreatePubSub();
        try
        {
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(9, "mob", V(50, 50), player);
            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Action<(long, List<UnitKey>)> cb = tuple =>
            {
                watcherId = tuple.Item1;
                unitKeys = new List<UnitKey>(tuple.Item2);
                signal.Set();
            };
            pubSub.OnUnitLeave(cb);

            var fakeKey = new UnitKey(999, "mob");
            pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>> { { "mob", new Dictionary<long, int> { { fakeKey.Id, 0 } } } });
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.Equal(1, watcherId);
            Assert.NotNull(unitKeys);
            Assert.Single(unitKeys);
            Assert.Equal(999, unitKeys[0].Id);
            Assert.Equal("mob", unitKeys[0].Type);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task UnitVersion_IncrementsOnPositionChange()
    {
        var pubSub = CreatePubSub();
        try
        {
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(1, "hero", V(0, 0), player);
            Assert.Equal(0, u.Version);

            u.Position = V(100, 100);
            Assert.Equal(1, u.Version);

            u.Position = V(200, 200);
            Assert.Equal(2, u.Version);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task UnitVersion_IncrementsOnDataSet()
    {
        var pubSub = CreatePubSub();
        try
        {
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(1, "hero", V(0, 0), player);
            Assert.Equal(0, u.Version);

            u.Data = new byte[] { 1 };
            Assert.Equal(1, u.Version);

            u.Data = new byte[] { 2, 3 };
            Assert.Equal(2, u.Version);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task PingUnits_VersionMismatch_SyncEnter()
    {
        var signal = new ManualResetEventSlim();
        long watcherId = 0;
        List<IUnit>? units = null;

        var pubSub = CreatePubSub();
        try
        {
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(8, "mob", V(50, 50), player);
            Assert.Equal(0, u.Version);

            u.Position = V(60, 60);
            Assert.Equal(1, u.Version);

            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Action<(long, List<IUnit>)> cb = tuple =>
            {
                watcherId = tuple.Item1;
                units = new List<IUnit>(tuple.Item2);
                signal.Set();
            };
            pubSub.OnUnitEnter(cb);

            pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>>
            {
                { "mob", new Dictionary<long, int>
                    {
                        { 8, 0 }
                    }
                }
            });
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.Equal(1, watcherId);
            Assert.NotNull(units);
            Assert.Single(units);
            Assert.Equal(8, units[0].Id);
            Assert.Equal(1, units[0].Version);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== PublishEvent =====

    [Fact]
    public async Task PublishEvent_UnitEvent()
    {
        var signal = new ManualResetEventSlim();
        List<long>? watcherIds = null;
        IUnit? unit = null;
        string? eventName = null;
        object? data = null;

        var pubSub = CreatePubSub();
        try
        {
            Action<(List<long>, IUnit, string, object, bool)> cb = tuple =>
            {
                watcherIds = new List<long>(tuple.Item1);
                unit = tuple.Item2;
                eventName = tuple.Item3;
                data = tuple.Item4;
                signal.Set();
            };
            pubSub.OnUnitEvent(cb);

            pubSub.AddWatcher(1, V(0, 0), 200);
            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(11, "hero", V(30, 30), player);

            var eventData = new { damage = 50 };
            u.PublishEvent("attack", eventData);
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.NotNull(watcherIds);
            Assert.Contains(1L, watcherIds);
            Assert.Equal("attack", eventName);
            Assert.NotNull(data);
        }
        finally { pubSub.Dispose();         }
    }

    [Fact]
    public async Task PublishEvent_TcpAndUdp()
    {
        var signal = new ManualResetEventSlim();
        int eventsReceived = 0;
        (List<long>, IUnit, string, object, bool)? tcpResult = null;
        (List<long>, IUnit, string, object, bool)? udpResult = null;

        var pubSub = CreatePubSub();
        try
        {
            Action<(List<long>, IUnit, string, object, bool)> cb = tuple =>
            {
                if (tuple.Item5)
                    tcpResult = tuple;
                else
                    udpResult = tuple;

                if (Interlocked.Increment(ref eventsReceived) >= 2)
                    signal.Set();
            };
            pubSub.OnUnitEvent(cb);

            pubSub.AddWatcher(1, V(0, 0), 200);

            var player1 = new Player();
            var player2 = new Player();
            var u1 = await pubSub.CreateUnitAsync<Player>(11, "hero", V(30, 30), player1);
            var u2 = await pubSub.CreateUnitAsync<Player>(12, "mob", V(50, 50), player2);

            u1.PublishEvent("tcp_evt", new byte[] { 1 }, reliable: true);
            u2.PublishEvent("udp_evt", new byte[] { 2 }, reliable: false);
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.Equal(2, eventsReceived);

            Assert.NotNull(tcpResult);
            Assert.Equal("tcp_evt", tcpResult.Value.Item3);
            Assert.True(tcpResult.Value.Item5);
            Assert.Equal(11, tcpResult.Value.Item2.Id);

            Assert.NotNull(udpResult);
            Assert.Equal("udp_evt", udpResult.Value.Item3);
            Assert.False(udpResult.Value.Item5);
            Assert.Equal(12, udpResult.Value.Item2.Id);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== Multiple watchers =====

    [Fact]
    public async Task MultipleWatchers_BatchEnter_BatchLeave()
    {
        var enterSignal = new ManualResetEventSlim();
        var leaveSignal = new ManualResetEventSlim();
        List<long>? enterIds = null;
        List<long>? leaveIds = null;

        var pubSub = CreatePubSub();
        try
        {
            Action<(List<long>, IUnit)> enterCb = tuple =>
            {
                enterIds = new List<long>(tuple.Item1);
                enterSignal.Set();
            };
            pubSub.OnUnitEnter(enterCb);

            pubSub.AddWatcher(1, V(0, 0), 200);
            pubSub.AddWatcher(2, V(0, 0), 200);

            var player = new Player();
            var u = await pubSub.CreateUnitAsync<Player>(42, "hero", V(50, 50), player);

            Assert.True(enterSignal.Wait(5000));
            Assert.NotNull(enterIds);
            Assert.Equal(2, enterIds.Count);
            Assert.Contains(1L, enterIds);
            Assert.Contains(2L, enterIds);

            Action<(List<long>, IUnit)> leaveCb = tuple =>
            {
                leaveIds = new List<long>(tuple.Item1);
                leaveSignal.Set();
            };
            pubSub.OnUnitLeave(leaveCb);

            u.Destroy();
            await pubSub.FlushAsync();

            Assert.True(leaveSignal.Wait(5000));
            Assert.NotNull(leaveIds);
            Assert.Equal(2, leaveIds.Count);
            Assert.Contains(1L, leaveIds);
            Assert.Contains(2L, leaveIds);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== Lazy cleanup =====

    private static (UnitKey key, Player target) CreateDoomedUnit(IPubSub pubSub)
    {
        var target = new Player { Name = "doomed" };
        pubSub.CreateUnit<Player>(13, "mob", V(50, 50), target, _ => { });
        return (new UnitKey(13, "mob"), target);
    }

    [Fact]
    public async Task LazyCleanup_DeadUnitRemoved()
    {
        var signal = new ManualResetEventSlim();
        List<UnitKey>? unitKeys = null;

        var pubSub = CreatePubSub();
        try
        {
            var (deadKey, target) = CreateDoomedUnit(pubSub);
            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            target.IsAlive = false;

            Action<(long, List<UnitKey>)> cb = tuple =>
            {
                unitKeys = new List<UnitKey>(tuple.Item2);
                signal.Set();
            };
            pubSub.OnUnitLeave(cb);

            pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>> { { "mob", new Dictionary<long, int> { { deadKey.Id, 0 } } } });
            await pubSub.FlushAsync();

            Assert.True(signal.Wait(5000));
            Assert.NotNull(unitKeys);
            Assert.Single(unitKeys);
            Assert.Equal(13, unitKeys[0].Id);
        }
        finally { pubSub.Dispose(); }
    }

    [Fact]
    public async Task LazyCleanup_EnterThenLeaveAfterGC()
    {
        var enterSignal = new ManualResetEventSlim();
        var leaveSignal = new ManualResetEventSlim();
        IUnit? enteredUnit = null;
        List<UnitKey>? leaveKeys = null;

        var pubSub = CreatePubSub();
        try
        {
            Action<(long, List<IUnit>)> enterCb = tuple =>
            {
                enteredUnit = tuple.Item2.FirstOrDefault();
                enterSignal.Set();
            };
            pubSub.OnUnitEnter(enterCb);

            var (deadKey, target) = CreateDoomedUnit(pubSub);
            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Assert.True(enterSignal.Wait(5000));
            Assert.NotNull(enteredUnit);
            Assert.Equal(13, enteredUnit.Id);

            target.IsAlive = false;

            Action<(long, List<UnitKey>)> leaveCb = tuple =>
            {
                leaveKeys = new List<UnitKey>(tuple.Item2);
                leaveSignal.Set();
            };
            pubSub.OnUnitLeave(leaveCb);

            pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>> { { "mob", new Dictionary<long, int> { { deadKey.Id, 0 } } } });
            await pubSub.FlushAsync();

            Assert.True(leaveSignal.Wait(5000));
            Assert.NotNull(leaveKeys);
            Assert.Single(leaveKeys);
            Assert.Equal(13, leaveKeys[0].Id);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== Worker thread resilience =====

    [Fact]
    public async Task WorkerThread_SurvivesCallbackException()
    {
        var signal = new ManualResetEventSlim();
        List<long>? watcherIds = null;

        var pubSub = CreatePubSub();
        try
        {
            pubSub.AddWatcher(1, V(0, 0), 200);
            await pubSub.FlushAsync();

            Action<(List<long>, IUnit)> boom = _ => throw new InvalidOperationException("boom");
            pubSub.OnUnitEnter(boom);
            var player1 = new Player();
            var u1 = await pubSub.CreateUnitAsync<Player>(1, "hero", V(50, 50), player1);

            Action<(List<long>, IUnit)> cb = tuple =>
            {
                watcherIds = new List<long>(tuple.Item1);
                signal.Set();
            };
            pubSub.OnUnitEnter(cb);
            var player2 = new Player();
            var u2 = await pubSub.CreateUnitAsync<Player>(2, "hero", V(60, 60), player2);

            Assert.True(signal.Wait(5000));
            Assert.NotNull(watcherIds);
            Assert.Contains(1L, watcherIds);
        }
        finally { pubSub.Dispose(); }
    }

    // ===== Watcher Expiration =====

    [Fact]
    public async Task WatcherExpires_OnlyPingedWatcherSurvives()
    {
        var config = new PubSubConfig
        {
            GridSize = 100f,
            WatcherTimeoutSeconds = 1,
            WatcherCleanupIntervalSeconds = 1
        };
        var pubSub = IPubSub.Create(config);
        try
        {
            pubSub.AddWatcher(1, V(0, 0), 200f);
            pubSub.AddWatcher(2, V(0, 0), 200f);
            await pubSub.FlushAsync();

            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>> { { "mob", new Dictionary<long, int>() } });
                    await Task.Delay(300, cts.Token);
                }
            });

            await Task.Delay(3000);
            cts.Cancel();
            await pubSub.FlushAsync();

            var signal = new ManualResetEventSlim();
            List<long>? enteredWatcherIds = null;
            pubSub.OnUnitEnter(tuple =>
            {
                enteredWatcherIds = new List<long>(tuple.Item1);
                signal.Set();
            });

            var player = new Player { Name = "B" };
            await pubSub.CreateUnitAsync<Player>(20, "mob", V(50, 50), player);

            Assert.True(signal.Wait(5000));
            Assert.NotNull(enteredWatcherIds);
            Assert.Single(enteredWatcherIds);
            Assert.Equal(1, enteredWatcherIds[0]);
        }
        finally { pubSub.Dispose(); }
    }
}

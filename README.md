# SubPubC

Spatial Pub/Sub library for game servers written in .NET. Tracks entities (**Units**) on a 2D grid and notifies observers (**Watchers**) when units enter, leave, move, or emit events within their observation radius. Supports standalone local use, networked mode via **Natify** (NATS-based protocol), and **Unity** (IL2CPP).

---

## Table of Contents

- [Concepts](#concepts)
- [Installation](#installation)
- [Quick Start (Local)](#quick-start-local)
- [Quick Start (Networked via Natify)](#quick-start-networked-via-natify)
- [API Reference](#api-reference)
  - [IPubSub](#ipubsub)
  - [IUnit](#iunit)
  - [IPubSubNatifyClient](#ipubsubnatifyclient)
- [Event Model](#event-model)
  - [BatchEnter / BatchLeave](#batchenter--batchleave)
  - [SyncEnter / SyncLeave](#syncenter--syncleave)
  - [UnitEvent](#unitevent)
- [Architecture](#architecture)
  - [Thread Model](#thread-model)
  - [Spatial Grid](#spatial-grid)
  - [Memory Management](#memory-management)
  - [Version Tracking](#version-tracking)
  - [Watcher Expiration](#watcher-expiration)
  - [After-Callbacks (Natify Bridge)](#after-callbacks-natify-bridge)
- [Network Protocol (Natify)](#network-protocol-natify)
  - [Topics](#topics)
  - [Message Types](#message-types)
- [Docker Compose](#docker-compose)
- [Building](#building)
- [Running Tests](#running-tests)

---

## Concepts

| Term | Definition |
|------|-----------|
| **Unit** | A game entity with `Id`, `Type`, `Position` (2D), optional `Data`, and a `WeakReference` to your own object. |
| **Watcher** | An observer at a position with a `Radius`. Receives events for all units whose position falls within its range. |
| **Cell** | Internally, the world is divided into square grid cells (size configurable via `GridSize`) for O(1) spatial queries. You don't interact with cells directly. |
| **BatchEnter** | Fire-and-forget notification: _this unit just entered your range_. |
| **BatchLeave** | Fire-and-forget notification: _this unit just left your range_. |
| **SyncEnter** | Bulk notification: _here are all units currently in your range_. Used for initial sync and ping-based reconciliation. |
| **SyncLeave** | Bulk notification: _these unit keys are no longer in your range_. Companion to SyncEnter. |
| **UnitEvent** | Arbitrary event emitted by a unit (e.g. "attack", "pickup") forwarded to all watchers covering that unit. |

---

## Installation

The library multi-targets `netstandard2.1` and `net9.0`:

| Target | Use case |
|--------|----------|
| `netstandard2.1` | **Unity** (IL2CPP), older .NET runtimes. `System.Threading.Channels.dll` is **bundled** in the NuGet package — no extra dependency to resolve. |
| `net9.0` | **Standalone .NET server**. `System.Threading.Channels` comes from the runtime. |

Add the NuGet package to your `.csproj`:

```xml
<PackageReference Include="PubSubLib" Version="1.0.0" />
```

Or use a project reference:

```xml
<ProjectReference Include="..\PubSubLib\PubSubLib.csproj" />
```

**Dependencies:**
- `System.Threading.Channels` — internal worker queue (bundled for netstandard2.1, built-in for net9.0)
- `Natify` (v1.0.2) — optional, only needed for networked mode
- `Google.Protobuf` (v3.34.1) — for binary message serialization in networked mode

---

## Quick Start (Local)

```csharp
using PubSubLib;

// 1. Create the PubSub instance
var pubSub = IPubSub.Create(new PubSubConfig
{
    GridSize = 100f  // each grid cell is 100x100 units
});

// 2. Register callbacks for events you care about
pubSub.OnUnitEnter(tuple =>
{
    var watcherIds = tuple.Item1; // List<long> — which watchers are notified
    var unit       = tuple.Item2; // IUnit
    Console.WriteLine($"[BatchEnter] Unit {unit.Id} entered range of watchers [{string.Join(",", watcherIds)}]");
});

pubSub.OnUnitLeave(tuple =>
{
    var unitKeys    = tuple.Item2; // List<UnitKey> — which units left
    Console.WriteLine($"[SyncLeave] Watcher {tuple.Item1} lost [{string.Join(",", unitKeys)}]");
});

pubSub.OnUnitEvent(tuple =>
{
    Console.WriteLine($"[UnitEvent] {tuple.Item2.Type}:{tuple.Item2.Id} emitted '{tuple.Item3}' with data {tuple.Item4}");
});

// 3. Place a watcher (observer)
pubSub.AddWatcher(watcherId: 1, position: V(0, 0), radius: 200f);

// 4. Spawn a unit asynchronously
var player = new Player { Name = "Hero" };
var unit = await pubSub.CreateUnitAsync<Player>(
    id: 42, type: "hero", position: V(50, 50), target: player
);

// 5. Move the unit
unit.Position = V(150, 150);
await pubSub.FlushAsync(); // ensure all events are processed

// 6. Emit an event from the unit
unit.PublishEvent("attack", new { damage = 50 });
await pubSub.FlushAsync();

// 7. Destroy the unit
unit.Destroy();
await pubSub.FlushAsync();

// 8. Cleanup
pubSub.Dispose();

// Helper
static Vector2 V(float x, float y) => new Vector2 { x = x, y = y };
```

### `FlushAsync()` — Why?

All public methods enqueue work onto a single background thread. `FlushAsync()` enqueues a no-op and returns a `Task` that completes after all previously enqueued actions have been processed. Use it:
- After a series of operations when you need to assert/test results.
- After `unit.Destroy()` to ensure leave events have fired before disposal.
- Before reading `u.Version` or `u.Data` if you just set them.

You do **not** need to flush after every single call. Flush is primarily for synchronization points in tests or ordered cleanup.

### Creating Units with a Callback (non-async)

```csharp
pubSub.CreateUnit<Player>(id, type, position, target, onCreated: unit =>
{
    // unit is IUnit, fully registered in the grid
    // Note: this callback runs on the worker thread
});
```

---

## Quick Start (Networked via Natify)

SubPubC supports distributing events across the network via [Natify](https://github.com/anomalyco/natify) (NATS-based RPC/pub-sub).

```
┌─────────────┐           NATS           ┌─────────────┐
│   Client    │ ── PubSub.Cmd ────────▶  │   Server    │
│  (Watcher)  │ ◀─ PubSub.Evt ────────   │  (PubSub)   │
└─────────────┘                           └─────────────┘
```

**Server side:**

```csharp
using Natify;
using PubSubLib;

// Setup NATS
var natifyClient = new NatifyClientFast("nats://localhost:4222",
    "PubSubServer", "ServerGroup", "VN", "Router");

// Create PubSub and attach Natify
var pubSub = IPubSub.Create(new PubSubConfig { GridSize = 100f });
pubSub.AddNatify(natifyClient);

// Now any BatchEnter/SyncEnter/UnitEvent emitted by the server
// will be automatically published to NATS topic PubSub.Evt.

// Commands received from clients on topic PubSub.Cmd are
// automatically enqueued to the worker thread.
```

**Client side:**

```csharp
using Natify;
using PubSubLib;
using PubSubLib.Messages;

// Setup Natify (client role)
var natifyServer = new NatifyServer("nats://localhost:4222",
    "Router", "RouterGroup", "PubSubServer");
var client = IPubSubNatifyClient.Create(natifyServer, "VN");

// Subscribe to events from server
client.OnBatchEnter(msg =>
{
    Console.WriteLine($"Unit {msg.UnitId} ({msg.UnitType}) at ({msg.PosX},{msg.PosY}) v{msg.Version}");
    // msg.WatcherIds — which of your watchers are notified
});

client.OnSyncEnter(msg =>
{
    Console.WriteLine($"Sync for watcher {msg.WatcherId}, {msg.Units.Count} units:");
    foreach (var u in msg.Units)
        Console.WriteLine($"  Unit {u.Id} ({u.Type}) at ({u.PosX},{u.PosY}) v{u.Version}");
});

client.OnSyncLeave(msg =>
{
    foreach (var g in msg.Keys)
        Console.WriteLine($"Watcher {msg.WatcherId} lost {g.Type}: [{string.Join(",", g.UnitIds)}]");
});

client.OnUnitEvent(msg =>
{
    Console.WriteLine($"Event '{msg.EventName}' from unit {msg.UnitId}");
});

// Send commands to server
client.SendAddWatcher(new AddWatcherCmd
{
    WatcherId = 100, PosX = 0, PosY = 0, Radius = 200f
});

client.SendPublishEvent(new PublishEventCmd
{
    UnitId = 42, UnitType = "hero", EventName = "attack",
    Data = Google.Protobuf.ByteString.CopyFrom(new byte[] { 99 })
});

// Ping for state reconciliation
var cmd = new PingUnitsCmd { WatcherId = 100 };
var group = new TypeGroup { Type = "hero" };
group.UnitIds.Add(42);
group.Versions.Add(0); // client thinks unit 42 is at version 0
cmd.Units.Add(group);
client.SendPingUnits(cmd);
// Server responds with SyncEnter if version mismatch, SyncLeave for stale keys.

// Cleanup
client.Dispose();
```

---

## API Reference

### IPubSub

```csharp
public interface IPubSub : IDisposable
{
    // ── Factory ──
    static IPubSub Create(PubSubConfig config);

    // ── Unit lifecycle ──
    void CreateUnit<T>(long id, string type, Vector2 position, T target,
        Action<IUnit> onCreated, byte[]? data = null) where T : class;
    Task<IUnit> CreateUnitAsync<T>(long id, string type, Vector2 position,
        T target, byte[]? data = null) where T : class;
    Task FlushAsync();

    // ── Watcher lifecycle ──
    void AddWatcher(long watcherId, Vector2 position, float radius);
    void RemoveWatcher(long watcherId);
    void MoveWatcher(long watcherId, Vector2 position, float radius);

    // ── State reconciliation ──
    void WatcherPingUnits(long watcherId,
        Dictionary<string, Dictionary<long, int>> typeVersions);

    // ── Natify integration ──
    void AddNatify(NatifyClientFast client);
    void AddNatify(NatifyClient client);

    // ── Batch callbacks (instant enter/leave) ──
    void OnUnitEnter(Action<(List<long> notyWatchIds, IUnit units)> cb);
    void OnUnitLeave(Action<(List<long> notyWatchIds, IUnit units)> cb);

    // ── Sync callbacks (initial state / reconciliation) ──
    void OnUnitEnter(Action<(long watcherId, List<IUnit> units)> cb);
    void OnUnitLeave(Action<(long watcherId, List<UnitKey> unitKeys)> cb);

    // ── Event callback ──
    void OnUnitEvent(Action<(List<long> watcherIds, IUnit unit,
        string eventName, object data)> cb);
}
```

#### PubSubConfig

```csharp
public class PubSubConfig
{
    public float GridSize = 100f;                // side length of each square grid cell
    public int WatcherTimeoutSeconds = 5;        // watcher expires if not pinged within this duration
    public int WatcherCleanupIntervalSeconds = 2; // how often to check for expired watchers
}
```

- **GridSize**: Smaller values = finer grid = more cells but fewer units per cell. Larger values = coarser grid = fewer cells but more units per cell. Tune based on your world scale and unit density.
- **WatcherTimeoutSeconds**: If `WatcherPingUnits` is not called for a watcher within this interval, the watcher is automatically removed. Default 5 seconds.
- **WatcherCleanupIntervalSeconds**: Expired watchers are checked and removed at this interval during the idle phase of the worker loop. Default 2 seconds.

### IUnit

```csharp
public interface IUnit
{
    long Id { get; }                        // unique ID
    string Type { get; }                    // unit type (e.g. "hero", "mob", "item")
    Vector2 Position { get; set; }          // 2D position — setting it triggers grid cell update + version bump
    bool IsAlive { get; }                   // false if GC collected the target
    object? Target { get; }                 // resolved target (null if collected)
    int Version { get; }                    // incremented on Position set or Data set
    byte[]? Data { get; set; }              // binary payload — setting it bumps version
    void PublishEvent(string eventName, object? data); // emit event to all covering watchers
    void Destroy();                          // remove unit from grid, fire leave events
}
```

Key behaviors:
- Setting `Position` triggers a cell change check. If the cell changes, `BatchLeave` fires to watchers who lose sight, `BatchEnter` fires to watchers who gain sight.
- Setting `Position` to the same value is a no-op (no version bump, no event).
- `Destroy()` removes the unit from the grid and fires `BatchLeave` to all current watchers.
- `PublishEvent()` fires `UnitEvent` to all watchers covering the unit's current cell.
- `IsAlive` uses `WeakReference.TryGetTarget` — if your target object has been garbage collected, the unit is considered dead and will be lazily cleaned up.

### UnitKey

```csharp
public readonly struct UnitKey : IEquatable<UnitKey>
{
    public long Id { get; }
    public string Type { get; }
    // Equality is by both Id and Type
}
```

`UnitKey` is used in `SyncLeave` callbacks and internally by `WatcherPingUnits` to identify units by their composite key (Id + Type). Units of different types can share the same numeric Id.

---

### IPubSubNatifyClient

```csharp
public interface IPubSubNatifyClient : IDisposable
{
    static IPubSubNatifyClient Create(NatifyServer server, string regionId);

    // ── Send commands to server (PubSub.Cmd) ──
    void SendAddWatcher(AddWatcherCmd cmd);
    void SendRemoveWatcher(RemoveWatcherCmd cmd);
    void SendMoveWatcher(MoveWatcherCmd cmd);
    void SendPingUnits(PingUnitsCmd cmd);
    void SendPublishEvent(PublishEventCmd cmd);

    // ── Receive events from server (PubSub.Evt) ──
    void OnBatchEnter(Action<BatchEnterMsg> callback);
    void OnBatchLeave(Action<BatchLeaveMsg> callback);
    void OnSyncEnter(Action<SyncEnterMsg> callback);
    void OnSyncLeave(Action<SyncLeaveMsg> callback);
    void OnUnitEvent(Action<UnitEventMsg> callback);
}
```

---

## Event Model

SubPubC has five event types split into two categories:

### BatchEnter / BatchLeave

**When:** A unit appears or disappears relative to a watcher's range.

**Triggered by:**
- `CreateUnit`/`CreateUnitAsync` — unit spawns inside a watcher's range
- `unit.Position = ...` — unit moves into/out of a watcher's range
- `unit.Destroy()` — unit is removed from the grid

**Callback signature (Batch):**
```csharp
(List<long> watcherIds, IUnit unit)
```
Multiple watchers can be notified in one call (e.g., if two watchers overlap).

**Callback signature (SyncEnter):**
```csharp
(long watcherId, List<IUnit> units)
```
Used when a watcher is added or moved — lists all units currently in range. Also used by `WatcherPingUnits` for reconciliation.

**Callback signature (SyncLeave):**
```csharp
(long watcherId, List<UnitKey> unitKeys)
```
Companion to SyncEnter — sent when a watcher is moved away or `WatcherPingUnits` reveals stale keys. **Note:** `RemoveWatcher` does NOT fire `SyncLeave` (the watcher is already gone and does not need notification).

### UnitEvent

**When:** A unit calls `PublishEvent()`.

**Triggered by:**
- `unit.PublishEvent("attack", data)` — forwarded to all watchers whose range covers the unit

**Callback signature:**
```csharp
(List<long> watcherIds, IUnit unit, string eventName, object data)
```

### Event Flow Diagram

```
CreateUnit
  │
  ├─ cell X → has watchers [1,2]? → FireBatchEnter(watcherIds=[1,2], unit)
  │
AddWatcher(w=3, pos, radius)
  │
  ├─ cells in range → units in cells → FireSyncEnter(watcherId=3, units=[...])
  │
Unit.Position = newPos
  │
  ├─ oldCell ≠ newCell?
  │   ├─ old watchers \ new watchers → FireBatchLeave(exited, unit)
  │   └─ new watchers \ old watchers → FireBatchEnter(entered, unit)
  │
Unit.Destroy()
  │
  └─ FireBatchLeave(current watchers, unit) → remove from grid
```

---

## Architecture

### Thread Model

```
Your Code (any thread)                    Worker Thread ("PubSubLib.EventChannel")
       │                                          │
       │── pubSub.AddWatcher(...)                 │
       │── pubSub.CreateUnitAsync(...)            │
       │── unit.Position = ...                    │
       │     (enqueues Action)                    │
       │                                          │
       │     ═══ Channel<Action> ═══════════════▶ │
       │                                          │── Execute action
       │                                          │── Call FireBatchEnter/FireSyncEnter/...
       │                                          │── Invoke OnUnitEnter callback
       │                                          │── Invoke AfterBatchEnter (→ Natify publish)
       │                                          │── OnIdleCheck (→ watcher expiration)
       │                                          │
       │◀─ FlushAsync completes ───────────────── │
```

- All public API methods enqueue an `Action` onto an unbounded `Channel<Action>`.
- A single background worker thread (`PubSubLib.EventChannel`) dequeues and executes actions sequentially.
- No locks on `_units`, `_cells`, or `_watchers` dictionaries — only the worker thread mutates them.
- `FlushAsync()` enqueues a `TaskCompletionSource.SetResult()` — awaiting it guarantees all prior actions are processed.
- Callbacks are invoked with try/catch inside `Fire*` helpers — a single broken subscriber cannot crash the worker thread.
- After processing all queued actions, the worker calls `OnIdleCheck` every loop cycle, which runs watcher expiration cleanup at the configured interval.

### Spatial Grid

The world is partitioned into square cells of side length `GridSize` (default 100).

```
Cell key: "{cellX}:{cellY}"
cellX = floor(x / gridSize)
cellY = floor(y / gridSize)
```

- Each cell holds a set of `UnitKey` and a set of `watcherId`.
- `AddUnit`: place unit in the cell at its position, notify all watchers in that cell.
- `AddWatcher`: compute all cells in range (`GetAllGridCellsInRange`), mark watcher in those cells, resolve all units in those cells and fire `SyncEnter`.
- `MoveWatcher`: compute cell diff (added/removed), fire `SyncEnter`/`SyncLeave` accordingly.
- `Unit.Position = ...`: if cell changes, remove from old cell, add to new cell, fire `BatchEnter`/`BatchLeave` for watchers that lost/gained sight.

### Memory Management

- Units hold a `WeakReference` to your target object (e.g., `Player`).
- `IsAlive` returns `false` when the GC collects the target.
- When `TryResolveAlive()` encounters a dead unit during any operation (ping, cell query), it automatically removes the unit from internal dictionaries and fires cleanup events.
- **Best practice:** Call `unit.Destroy()` explicitly when removing entities. The `WeakReference` lazy cleanup is a safety net, not a primary removal mechanism.
- `ListPool<T>` (ConcurrentBag-based) reuses `List<T>` instances for callbacks to reduce allocations.

### Version Tracking

Each unit has a `Version` counter incremented on:
- `Position` change (change cell, value actually differs)
- `Data` setter

`Version` is sent over Natify in `BatchEnterMsg`, `SyncEnterMsg`/`UnitEnterItem`. Clients can use `WatcherPingUnits` with their known version map to detect stale or missing data. The server responds with:
- `SyncEnter` for units whose version changed (or new units)
- `SyncLeave` for keys the client thinks are present but no longer exist

### Watcher Expiration

Watchers must periodically call `WatcherPingUnits` to stay alive. If a watcher does not ping within `WatcherTimeoutSeconds` (default 5), it is automatically removed from the grid.

**How it works:**
- Each watcher has an expiration timestamp stored in a **sorted dictionary** (`ISortedDictionary<long, long>` implemented by `DictionaryScore`, a Redis ZSET-like data structure using skip list + hash map).
- On `AddWatcher`, `WatcherPingUnits`, and `RemoveWatcher`, the expiration timestamp is refreshed/removed.
- During the worker thread's idle phase, `CheckIdle()` compares elapsed time against `WatcherCleanupIntervalSeconds` (default 2). If the interval has passed, `CleanupExpiredWatchers()` removes all watchers with timestamps older than now.
- `RangeByScore(0, nowTicks)` — O(log n + k) — efficiently fetches only expired watchers without scanning all watchers.

**Example — ping keeps watcher alive:**
```csharp
var pubSub = IPubSub.Create(new PubSubConfig
{
    WatcherTimeoutSeconds = 5,
    WatcherCleanupIntervalSeconds = 2
});

pubSub.AddWatcher(1, V(0, 0), 200f);

// Ping every 3 seconds to keep watcher alive
while (running)
{
    pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>>());
    await Task.Delay(3000);
}
// Watcher 1 stays alive. Any watcher that fails to ping for 5s is auto-removed.
```

- `SyncLeave` is NOT fired when a watcher expires (same as `RemoveWatcher` — the watcher is already gone).
- Expired watcher IDs are freed and can be reused immediately.

### After-Callbacks (Natify Bridge)

`EventChannel` has `After*` callbacks (`AfterBatchEnter`, `AfterBatchLeave`, `AfterSyncEnter`, `AfterSyncLeave`, `AfterUnitEvent`) that are set by `PubSubNatifySync`. These fire _after_ the user-registered `On*` callbacks and publish serialized protobuf messages to the NATS topic `PubSub.Evt`. This means:

- User callbacks run first (for game logic).
- Then Natify publishes the event to remote clients.
- If no Natify is attached, After-callbacks are null and skipped.

---

## Network Protocol (Natify)

### Topics

| Topic | Direction | Purpose |
|-------|-----------|---------|
| `PubSub.Cmd` | Client → Server | Commands: AddWatcher, RemoveWatcher, MoveWatcher, PingUnits, PublishEvent |
| `PubSub.Evt` | Server → Client | Events: BatchEnter, BatchLeave, SyncEnter, SyncLeave, UnitEvent |

### Message Types

Defined in `PubSubLib/Messages/PubSubMessages.proto`:

```
PubSubCommand (oneof): AddWatcherCmd | RemoveWatcherCmd | MoveWatcherCmd | PingUnitsCmd | PublishEventCmd
PubSubEvent (oneof):   BatchEnterMsg | BatchLeaveMsg | SyncEnterMsg | SyncLeaveMsg | UnitEventMsg
```

See the proto file for full field definitions.

---

## Docker Compose

A `docker-compose.yml` is provided that starts NATS and the SubPubC console:

```bash
docker-compose up -d
```

Services:
- `nats` — NATS JetStream server on ports 4222 (client), 8222 (monitoring)
- `subpubc` — the SubPubC console app (depends on nats)

---

## Building

```bash
# Restore and build the library
dotnet build PubSubLib/PubSubLib.csproj

# Build tests
dotnet build PubSubLibTest/PubSubLibTest.csproj
```

---

## Running Tests

```bash
dotnet test PubSubLibTest/PubSubLibTest.csproj
```

Test categories:
- **PubSubTests** (34 tests) — unit tests for local PubSub (create/dispose, batch enter/leave, sync enter/leave, move watcher, position change, ping, events, version tracking, lazy cleanup, worker resilience, watcher expiration)
- **PubSubTestAll** (3 tests) — full-stack integration tests (basic create & sync, client kills unit → server re-syncs, server destroys unit → client receives destroy)
- **PubSubNatifyProtoTests** (12 tests) — protobuf roundtrip tests for all message types
- **PubSubNatifyTests** (9 tests) — outbound/inbound Natify sync tests (requires local NATS server)
- **PubSubNatifyIntegrationTests** (3 tests) — end-to-end integration tests (requires local NATS server)
- **NatifyBasicConnectivityTests** — basic NATS connectivity checks
- **NatifyFullRoundtripTests** — full roundtrip tests

Natify tests require a running NATS server on `nats://localhost:4222`. Start with:
```bash
docker run -d --name nats -p 4222:4222 -p 8222:8222 nats:latest -js
```

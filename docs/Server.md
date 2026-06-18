# Server — PubSubLib

Package chứa toàn bộ logic không gian (spatial grid), quản lý Unit/Watcher, và cầu nối NATS (Natify). Đây là **thành phần authoritative** — sở hữu dữ liệu chính thống về vị trí, trạng thái của mọi unit.

## Mục lục

- [Cài đặt](#cài-đặt)
- [Khái niệm](#khái-niệm)
- [Cấu hình](#cấu-hình-pubsubconfig)
- [API Reference](#api-reference)
  - [IPubSub](#ipubsub)
  - [IUnit](#iunit)
  - [UnitKey](#unitkey)
- [2 chế độ sử dụng](#2-chế-độ-sử-dụng)
  - [Standalone (không Natify)](#1-standalone-không-natify)
  - [Networked (có Natify)](#2-networked-có-natify)
- [Luồng xử lý nội bộ](#luồng-xử-lý-nội-bộ)
  - [Thread Model](#thread-model)
  - [Spatial Grid](#spatial-grid)
  - [Event Model](#event-model)
  - [Version Tracking](#version-tracking)
  - [Watcher Expiration](#watcher-expiration)
  - [Memory Management](#memory-management)

---

## Cài đặt

Package multi-target `netstandard2.1` (Unity IL2CPP) và `net9.0` (standalone server):

```xml
<PackageReference Include="PubSubLib" Version="1.0.0" />
```

Hoặc project reference:

```xml
<ProjectReference Include="..\PubSubLib\PubSubLib.csproj" />
```

Dependencies:
- `System.Threading.Channels` — hàng đợi worker thread (bundled cho netstandard2.1)
- `Natify` (v1.0.2) — optional, chỉ cần cho chế độ networked
- `Google.Protobuf` (v3.34.1) — serialize binary message cho networked

---

## Khái niệm

| Khái niệm | Định nghĩa |
|-----------|------------|
| **Unit** | Thực thể game có `Id`, `Type`, `Position` (2D), `Data` (binary), và `WeakReference` tới object của bạn. |
| **Watcher** | Người quan sát tại một vị trí với `Radius`. Nhận event cho mọi unit có vị trí nằm trong bán kính. |
| **Cell** | Lưới không gian được chia thành các ô vuông (kích thước `GridSize`). Truy vấn không gian O(1). |
| **BatchEnter** | Thông báo fire-and-forget: *unit này vừa vào tầm quan sát của bạn*. |
| **BatchLeave** | Thông báo fire-and-forget: *unit này vừa rời tầm quan sát của bạn*. |
| **SyncEnter** | Thông báo bulk: *danh sách toàn bộ unit hiện có trong tầm*. Dùng cho lần đầu sync và ping reconciliation. |
| **SyncLeave** | Thông báo bulk: *những unit key này không còn trong tầm*. |
| **UnitEvent** | Sự kiện tùy ý do unit phát ra (vd: "attack", "pickup") → forward tới mọi watcher đang quan sát unit đó. |

---

## Cấu hình (PubSubConfig)

```csharp
public class PubSubConfig
{
    public float GridSize = 100f;                  // kích thước mỗi cell vuông (đơn vị world)
    public int WatcherTimeoutSeconds = 5;          // watcher hết hạn nếu không ping trong khoảng này
    public int WatcherCleanupIntervalSeconds = 2;  // tần suất kiểm tra watcher hết hạn (mỗi N giây)
}
```

- **GridSize**: Giá trị nhỏ → nhiều cell hơn, ít unit/cell hơn → query nhanh hơn nhưng tốn bộ nhớ hơn. Giá trị lớn → ít cell hơn, nhiều unit/cell hơn. Điều chỉnh theo scale world và mật độ unit.
- **WatcherTimeoutSeconds**: Nếu `WatcherPingUnits` không được gọi cho watcher trong khoảng này, watcher tự động bị xóa. Mặc định 5 giây.
- **WatcherCleanupIntervalSeconds**: Worker thread kiểm tra và xóa watcher hết hạn ở tần suất này trong pha idle. Mặc định 2 giây.

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

    // ── Batch callbacks (enter/leave tức thời) ──
    void OnUnitEnter(Action<(List<long> notyWatchIds, IUnit units)> callBack);
    void OnUnitLeave(Action<(List<long> notyWatchIds, IUnit units)> callBack);

    // ── Sync callbacks (trạng thái ban đầu / reconciliation) ──
    void OnUnitEnter(Action<(long notyWatchId, List<IUnit> units)> callBack);
    void OnUnitLeave(Action<(long notyWatchId, List<UnitKey> unitKeys)> callBack);

    // ── Event callback ──
    void OnUnitEvent(Action<(List<long> notyWatchId, IUnit units, string eventName, object data)> callBack);
}
```

> **Lưu ý về generic**: `CreateUnit<T>` và `CreateUnitAsync<T>` giữ `<T>` ở cấp method để type-safety cho tham số `target`. Các callback và interface `IUnit` không còn generic — `Target` trả về `object?`.

#### Các phương thức chi tiết

| Phương thức | Mô tả |
|-------------|-------|
| `CreateUnit(id, type, pos, target, onCreated, data)` | Tạo unit mới. `onCreated` callback chạy trên worker thread với `IUnit` đã được đăng ký trong grid. |
| `CreateUnitAsync(id, type, pos, target, data)` | Tạo unit async. Trả về `Task<IUnit>` hoàn thành khi unit đã được đăng ký. |
| `FlushAsync()` | Enqueue no-op, trả về `Task` hoàn thành sau khi mọi action trước đó đã được xử lý. Dùng để đồng bộ trong test hoặc cleanup. |
| `AddWatcher(id, pos, radius)` | Thêm watcher vào grid. Tính toán tất cả cell trong bán kính, đăng ký watcher vào các cell đó, fire `SyncEnter` với mọi unit hiện có. |
| `RemoveWatcher(id)` | Xóa watcher. **Không** fire `SyncLeave`. |
| `MoveWatcher(id, pos, radius)` | Di chuyển watcher. Tính diff cell (thêm/xóa), fire `SyncEnter`/`SyncLeave` tương ứng. |
| `WatcherPingUnits(id, typeVersions)` | Ping giữ watcher sống + đồng bộ trạng thái. `typeVersions` map từ `"unitType"` → `{unitId: version}`. Server so sánh version: thiếu/khớp sai → `SyncEnter`, thừa key → `SyncLeave`. `Watcher` lưu `_knownTypes` — các type đã từng ping. Khi ping rỗng vẫn process các type đã biết (unitVersions rỗng → sync toàn bộ unit trong cell). |
| `AddNatify(client)` | Gắn Natify để publish event qua NATS và subscribe command từ client. |

#### Callback Overloads

Hai overload cho `OnUnitEnter` và `OnUnitLeave`:

| Overload | Dạng tuple | Dùng cho |
|----------|------------|----------|
| `OnUnitEnter` | `(List<long> watcherIds, IUnit unit)` | **Batch**: unit vừa vào tầm → thông báo các watcher bị ảnh hưởng |
| `OnUnitEnter` | `(long watcherId, List<IUnit> units)` | **Sync**: thêm watcher mới hoặc re-sync → danh sách toàn bộ unit trong tầm |
| `OnUnitLeave` | `(List<long> watcherIds, IUnit unit)` | **Batch**: unit rời tầm → thông báo các watcher bị ảnh hưởng |
| `OnUnitLeave` | `(long watcherId, List<UnitKey> unitKeys)` | **Sync**: watcher di chuyển hoặc ping phát hiện key cũ → danh sách key cần xóa |

### IUnit

```csharp
public interface IUnit
{
    long Id { get; }              // ID duy nhất
    string Type { get; }          // loại unit ("hero", "mob", "item")
    Vector2 Position { get; set; } // vị trí 2D — set trigger cập nhật cell + tăng version
    bool IsAlive { get; }         // false nếu target bị GC collect
    object? Target { get; }       // target object (null nếu bị collect)
    int Version { get; }          // tăng khi Position hoặc Data thay đổi
    byte[]? Data { get; set; }    // binary payload — set trigger tăng version
    void PublishEvent(string eventName, object? data); // phát event tới mọi watcher đang quan sát
    void Destroy();               // xóa unit khỏi grid, fire leave event
}
```

Các hành vi chính:
- **Set `Position`**: kiểm tra cell thay đổi. Nếu cell đổi → fire `BatchLeave` cho watcher mất tầm nhìn, `BatchEnter` cho watcher có tầm nhìn mới, version++. Nếu cell không đổi → không làm gì.
- **Set `Position` cùng giá trị**: no-op (không tăng version, không event).
- **`Destroy()`**: xóa unit khỏi grid, fire `BatchLeave` tới mọi watcher hiện tại đang quan sát.
- **`PublishEvent()`**: fire `UnitEvent` tới mọi watcher có cell chứa unit.
- **`IsAlive`**: dùng `WeakReference.TryGetTarget` — nếu target object bị GC collect, unit coi là dead và sẽ bị lazy cleanup.

### UnitKey

```csharp
public readonly struct UnitKey : IEquatable<UnitKey>
{
    public long Id { get; }
    public string Type { get; }
    // So sánh bằng cả Id và Type
}
```

Dùng trong `SyncLeave` callback và nội bộ `WatcherPingUnits` để định danh unit theo composite key (Id + Type). Unit khác type có thể dùng chung numeric Id.

---

## 2 chế độ sử dụng

### 1. Standalone (không Natify)

Toàn bộ logic chạy trong 1 process. Phù hợp cho:
- **Local test**: kiểm tra luồng event không cần network
- **Server monolithic**: game server xử lý mọi thứ nội bộ

```csharp
using PubSubLib;

var pubSub = IPubSub.Create(new PubSubConfig { GridSize = 100f });

// Đăng ký callbacks
pubSub.OnUnitEnter(tuple => {
    Console.WriteLine($"[BatchEnter] Watchers {string.Join(",", tuple.Item1)} thấy unit {tuple.Item2.Id}");
});

pubSub.OnUnitLeave(tuple => {
    Console.WriteLine($"[SyncLeave] Watcher {tuple.Item1} mất {tuple.Item2.Count} unit");
});

pubSub.OnUnitEvent(tuple => {
    Console.WriteLine($"[Event] {tuple.Item2.Id} phát '{tuple.Item3}'");
});

// Thêm watcher
pubSub.AddWatcher(1, new Vector2 { x = 0, y = 0 }, 200f);

// Tạo unit
var myObj = new Player();
var unit = await pubSub.CreateUnitAsync<Player>(42, "hero", new Vector2 { x = 50, y = 50 }, myObj);

// Di chuyển
unit.Position = new Vector2 { x = 150, y = 150 };

// Event
unit.PublishEvent("attack", new { damage = 50 });

// Đồng bộ
pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>>
{
    ["hero"] = new() { [42] = unit.Version }
});

// Dọn dẹp
unit.Destroy();
await pubSub.FlushAsync();
pubSub.Dispose();
```

**Luồng sự kiện standalone:**

```
CreateUnit (bất kỳ thread nào)
  │ enqueue Action
  ▼
Worker Thread (PubSubLib.EventChannel)
  ├─ Tạo Unit(id, type, pos, target) với WeakReference
  ├─ _units[key] = unit
  ├─ Cell = GetGridCellByPosition(pos)
  ├─ cell.AddUnit(key)
  ├─ Lấy watcher trong cell → FireBatchEnter(watcherIds, unit)
  │   ├─ User callback: OnUnitEnterBatch(tuple)
  │   └─ (nếu có Natify) After* → serialize → NATS
  │
AddWatcher (bất kỳ thread nào)
  │ enqueue Action
  ▼
Worker Thread
  ├─ GetAllGridCellsInRange(pos, radius) → danh sách cell
  ├─ Đăng ký watcher trong tất cả cell đó
  ├─ Thu thập mọi unit trong các cell
  └─ FireSyncEnter(watcherId, [IUnit...])
      ├─ User callback: OnUnitEnterSync(tuple)
      └─ (nếu có Natify) After* → serialize → NATS
```

### 2. Networked (có Natify)

Khi gắn Natify, mọi event được tự động publish lên NATS topic `PubSub.Evt`, và server subscribe `PubSub.Cmd` để nhận command từ client.

```csharp
using Natify;
using PubSubLib;

// Tạo PubSub
var pubSub = IPubSub.Create(new PubSubConfig { GridSize = 100f });

// Gắn Natify
var natifyClient = new NatifyClientFast(
    "nats://localhost:4222",
    "PubSubServer",   // tên server
    "ServerGroup",    // group
    "VN",             // region
    "Router"          // router name để nhận command
);
pubSub.AddNatify(natifyClient);

// Đăng ký callbacks như bình thường
pubSub.OnUnitEnter(tuple => { /* xử lý game logic */ });
pubSub.OnUnitEvent(tuple => { /* xử lý game logic */ });

// Mọi thứ khác giống standalone — event tự động publish + command tự động nhận
```

**Cách PubSubNatifySync hoạt động:**

```
                      ┌─────────────────────────────────┐
                      │         PubSubNatifySync         │
                      │                                  │
NATS PubSub.Cmd ──────▶── Subscribe<PubSubCommand> ──▶── OnCommand()
                      │   ├─ AddWatcher → pubSub.AddWatcher()
                      │   ├─ RemoveWatcher → pubSub.RemoveWatcher()
                      │   ├─ MoveWatcher → pubSub.MoveWatcher()
                      │   ├─ PingUnits → HandlePingUnits()
                      │   │   └─ Gom TypeGroup → Dictionary<string,Dictionary<long,int>>
                      │   │   └─ pubSub.WatcherPingUnits(watcherId, typeVersions)
                      │   └─ PublishEvent → pubSub.HandleNatifyPublishEvent()
                      │                                  │
                      │  After* callbacks                │
                      │  ├─ AfterBatchEnter → serialize → Publish("PubSub.Evt", evt)
                      │  ├─ AfterBatchLeave → serialize → Publish("PubSub.Evt", evt)
                      │  ├─ AfterSyncEnter → serialize → Publish("PubSub.Evt", evt)
                      │  ├─ AfterSyncLeave → serialize → Publish("PubSub.Evt", evt)
                      │  └─ AfterUnitEvent → serialize → Publish("PubSub.Evt", evt)
                      │                                  │
                      └─────────────────────────────────┘
```

- **Inbound**: Subscribe `PubSub.Cmd` → dispatch theo loại command → gọi phương thức tương ứng trên `pubSub`
- **Outbound**: `EventChannel` có cặp callback `On*` (user) và `After*` (Natify). `After*` serialize thành protobuf và publish lên `PubSub.Evt`
- `HandlePingUnits` gom tất cả `TypeGroup` trong `PingUnitsCmd` thành 1 `Dictionary<string, Dictionary<long, int>>`, gọi `WatcherPingUnits` đúng 1 lần

---

## Luồng xử lý nội bộ

### Thread Model

```
Code của bạn (bất kỳ thread)              Worker Thread ("PubSubLib.EventChannel")
       │                                            │
       │── pubSub.AddWatcher(...)                   │
       │── pubSub.CreateUnitAsync(...)              │
       │── unit.Position = ...                      │
       │     (enqueue Action vào Channel)            │
       │                                            │
       │     ═══ Channel<Action> ═════════════════▶ │
       │                                            │── Đọc action từ channel
       │                                            │── Thực thi action
       │                                            │── Gọi FireBatchEnter / FireSyncEnter / ...
       │                                            │── Gọi user callback (try/catch)
       │                                            │── Gọi After* callback (→ Natify publish)
       │                                            │── OnIdleCheck (→ dọn watcher hết hạn)
       │                                            │
       │◀─ FlushAsync hoàn thành ────────────────── │
```

Chi tiết triển khai:
- `Channel.CreateUnbounded<Action>()` — không lock, thread-safe
- 1 background thread duy nhất tên `"PubSubLib.EventChannel"` — không cần lock trên `_units`, `_cells`, `_watchers`
- `FlushAsync()` enqueue `TaskCompletionSource.SetResult()` — await nó đảm bảo mọi action trước đó đã xử lý xong
- Callback được gọi trong try/catch — 1 subscriber lỗi không crash worker thread
- Idle check chạy mỗi vòng lặp: kiểm tra và xóa watcher hết hạn

Worker loop (`EventChannel.cs:60-78`):

```csharp
private void WorkerLoop()
{
    while (!_cts.IsCancellationRequested)
    {
        // Đợi có action (timeout 1s để không block cancel)
        _reader.WaitToReadAsync(_cts.Token).AsTask().Wait(1000);

        // Xử lý tất cả action đang có
        while (_reader.TryRead(out var action))
        {
            try { action(); } catch { }
        }

        // Idle: kiểm tra watcher hết hạn
        try { _onIdleCheck(); } catch { }
    }
}
```

### Spatial Grid

Thế giới được chia thành các cell vuông cạnh `GridSize` (mặc định 100):

```
Cell key: "{cellX}:{cellY}"
cellX = floor(x / gridSize)
cellY = floor(y / gridSize)
```

Mỗi cell chứa:
- `HashSet<UnitKey>` — các unit trong cell
- `HashSet<long>` — các watcher đang quan sát cell

**Các thao tác không gian:**

| Thao tác | Logic |
|----------|-------|
| `AddUnit(pos)` | Xác định cell → thêm unit vào cell → thông báo watcher trong cell |
| `AddWatcher(pos, radius)` | `GetAllGridCellsInRange()` → đánh dấu watcher trong tất cả cell → thu thập unit → `SyncEnter` |
| `MoveWatcher(pos, radius)` | Tính cell diff (thêm/xóa) → `SyncEnter`/`SyncLeave` |
| `Unit.Position = newPos` | Nếu cell đổi → xóa unit khỏi cell cũ, thêm vào cell mới → `BatchEnter`/`BatchLeave` |

### Event Model

#### BatchEnter / BatchLeave

**Khi nào fire:**
- `CreateUnit` / `CreateUnitAsync` — unit xuất hiện trong tầm watcher
- `unit.Position = ...` — unit di chuyển vào/ra tầm watcher
- `unit.Destroy()` — unit bị xóa khỏi grid

**Thứ tự xử lý:**
1. Xác định watcher bị ảnh hưởng (cell membership)
2. Gọi user callback `OnUnitEnter` / `OnUnitLeave` (batch overload)
3. Gọi `AfterBatchEnter` / `AfterBatchLeave` (→ Natify publish nếu có)

#### SyncEnter / SyncLeave

**Khi nào fire:**
- `AddWatcher` — danh sách toàn bộ unit trong tầm
- `MoveWatcher` — unit mới vào tầm (`SyncEnter`), unit rời tầm (`SyncLeave`)
- `WatcherPingUnits` — reconciliation: version khớp sai → `SyncEnter`, key thừa → `SyncLeave`

**Lưu ý:** `RemoveWatcher` **không** fire `SyncLeave` (watcher đã biến mất, không cần thông báo).

#### UnitEvent

**Khi nào fire:**
- `unit.PublishEvent("eventName", data)` — forward tới mọi watcher trong cell chứa unit

### Version Tracking

Mỗi unit có `Version` (int), tăng khi:
- `Position` thay đổi (giá trị khác, cell có thể đổi hoặc không)
- `Data` setter được gọi

**Cách dùng trong ping reconciliation:**

```
Client ping:  { "hero": { 42: 5 } }   → client nghĩ unit 42 version 5
Server check: unit 42 version = 7     → version khác → SyncEnter với version 7 mới
Server check: unit 42 không tồn tại    → SyncLeave với key [42]
```

`WatcherPingUnits` nhận `Dictionary<string, Dictionary<long, int>>` — map type → (unitId → version). Server:
1. Refresh watcher expiration timestamp
2. Với mỗi type trong `watcher.KnownTypes` (các type đã từng ping):
   - Nếu type có trong ping → lấy `unitVersions` từ ping
   - Nếu type không có trong ping → `unitVersions` rỗng → sync toàn bộ unit trong cell
3. So sánh version: thiếu/khớp sai → `FireSyncEnter`, thừa key → `FireSyncLeave`

### Watcher Expiration

Watcher phải ping định kỳ để duy trì sự sống. Nếu không ping trong `WatcherTimeoutSeconds` (mặc định 5), watcher tự động bị xóa.

**Cơ chế:**
- `DictionaryScore<long, long>` — cấu trúc skip list + hash map (giống Redis ZSET), lưu `watcherId → expirationTimestamp`
- `AddWatcher` / `WatcherPingUnits` → refresh timestamp
- `RemoveWatcher` → xóa timestamp
- Worker idle check (`CheckIdle`): mỗi `WatcherCleanupIntervalSeconds` (mặc định 2), gọi `RangeByScore(0, nowTicks)` — O(log n + k) — lấy tất cả watcher hết hạn

```csharp
// Giữ watcher sống: ping mỗi 3 giây
while (running)
{
    pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>>());
    await Task.Delay(3000);
}
// Watcher 1 sống. Watcher nào không ping trong 5s → tự động bị xóa.
```

- `SyncLeave` **không** fire khi watcher hết hạn (giống `RemoveWatcher`)
- WatcherId bị xóa có thể tái sử dụng ngay

### Memory Management

- Unit giữ `WeakReference` tới target object (vd: `Player`, `Monster`)
- `IsAlive` trả về `false` khi GC collect target
- `TryResolveAlive()` gặp unit dead trong bất kỳ thao tác nào (ping, cell query) → tự động xóa unit khỏi dictionary nội bộ + fire cleanup event
- **Best practice:** Gọi `unit.Destroy()` tường minh khi xóa entity. `WeakReference` lazy cleanup là safety net, không phải cơ chế xóa chính.
- `ListPool<T>` (ConcurrentBag-based) tái sử dụng `List<T>` instance cho callback để giảm allocation.

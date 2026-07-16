# Client — PubSubLib.Client

Package chạy trong game client (Unity / .NET). Tự động ping định kỳ để đồng bộ trạng thái với PubSub server, nhận event để tạo/hủy GameObject. Đồng bộ dữ liệu player (mirror) qua NATS. Là một `IClientModule` cắm vào MyConnection client.

## Mục lục

- [Vai trò](#vai-trò)
- [Cài đặt](#cài-đặt)
- [Kiến trúc](#kiến-trúc)
- [API Reference](#api-reference)
  - [IPubSubClient](#ipubsubclient)
  - [IUnit / IUnit\<T\>](#iunit--iunitt)
  - [IPubSubClientModule](#ipubsubclientmodule)
  - [IProvider / IProvider\<T\>](#iprovider--iprovidert)
  - [ProviderAbstract\<T\>](#providerabstractt)
  - [EventMeta](#eventmeta)
  - [Config](#config)
  - [IPlayerMirrorClient](#iplayermirrorclient)
  - [IPlayerSpeaksClient](#iplayerspeaksclient)
  - [IPlayerSpeaksClientModule](#iplayerspeaksclientmodule)
- [Cách dùng](#cách-dùng)
- [Luồng xử lý nội bộ](#luồng-xử-lý-nội-bộ)
  - [Tick / Ping cycle](#1-tick--ping-cycle)
  - [Nhận event từ server](#2-nhận-event-từ-server)
  - [MoveWatcher](#3-movewatcher)
  - [Unit tracking với IAlive](#4-unit-tracking-với-ialive)
  - [Player data sync](#5-player-data-sync)

---

## Vai trò

```
Game Client (PubSubLib.Client) ◄── TCP ──► Router ◄── NATS ──► PubSub Server
```

Client làm 3 nhiệm vụ:

| Nhiệm vụ | Cơ chế |
|----------|--------|
| **Đồng bộ trạng thái** | `Tick()` định kỳ build `PingUnitsCmd` với version map → gửi UDP `PubSub.Cmd` |
| **Nhận event** | Subscribe TCP `PubSub.Evt` → `HandleBatchEnter`/`HandleSyncEnter`/`HandleBatchLeave`/`HandleSyncLeave` → gọi `IProvider` tạo/hủy GameObject |
| **Đồng bộ dữ liệu player** | `IPlayerSpeaksClientModule` subscribe TCP `PlayerSpeaks.Msg` → mirror dữ liệu từ server xuống client qua `IPlayerMirrorClient` |

Client **không** tự quản lý vị trí unit — mọi thay đổi vị trí do server authoritative quyết định và gửi về qua event.

---

## Cài đặt

```xml
<PackageReference Include="PubSubLib.Client" Version="2.0.1" />
```

Package target `netstandard2.1` — tương thích Unity IL2CPP.

Dependencies:
- `PubSubLib.Contracts` — protobuf message types
- `Google.Protobuf` (3.34.1) — deserialize message
- `MyConnection` (1.0.5) — giao tiếp TCP/UDP với Router

---

## Kiến trúc

```
                         PubSubClientModule                  PubSubClient
┌──────────────┐        ┌──────────────────────┐        ┌─────────────────────┐
│ MyConnection │  TCP   │ IClientModule        │        │ IPubSubClient       │
│   IClient    │◄──────►│                      │───────►│                     │
│              │        │ SetIClient(client)   │        │ Tick()              │
│              │        │   → SubscribeTcp     │        │ MoveWatcher()       │
│              │        │     "PubSub.Evt"     │        │ AddProvider()       │
│              │        │                      │        │                     │
│              │  UDP   │                      │        │ _units: Dictionary  │
│              │◄───────│                      │        │   (Id,Type) → Unit  │
│              │        │                      │        │ _providers: Dict    │
│              │        │                      │        │   "type" → IProvider│
└──────────────┘        └──────────────────────┘        └─────────────────────┘
                                   │
                                   ▼
                            IProvider (do bạn implement)
                            ├─ HeroProvider  → CreateObject / DestroyObject
                            └─ MobProvider   → CreateObject / DestroyObject
```

---

## API Reference

### IPubSubClient

```csharp
public interface IPubSubClient : IDisposable
{
    void Tick();
    void MoveWatcher(Vector2 position, float radius);
    IPubSubClient AddProvider(IProvider provider);
    IReadOnlyList<IUnit> GetAllUnits();
    IReadOnlyList<IUnit> GetAllUnitsByType(string unitType);
    IUnit? GetUnit(long unitId, string unitType);
}
```

### IUnit / IUnit\<T\>

```csharp
public interface IUnit
{
    string UnitType { get; }
    long Id { get; }
    int Version { get; }
    bool IsAlive { get; }
    object? Target { get; }
}

public interface IUnit<T> : IUnit where T : class, IAlive
{
    new T? Target { get; }
}
```

| Member | Mô tả |
|--------|-------|
| `UnitType` | Loại unit ("hero", "mob") |
| `Id` | ID duy nhất |
| `Version` | Tăng khi server cập nhật unit |
| `IsAlive` | `true` nếu target còn sống |
| `Target` | Object gốc. `IUnit` trả về `object?`, `IUnit<T>` trả về `T?` (typed) |

| Phương thức | Mô tả |
|-------------|-------|
| `Tick()` | Gọi mỗi frame. Kiểm tra thời gian ping → nếu đến hạn, build `PingUnitsCmd` và gửi UDP. Đồng thời dọn unit dead (GC collected). |
| `MoveWatcher(pos, radius)` | Gửi `MoveWatcherCmd` lên server. Deduplicated — chỉ gửi khi vị trí hoặc bán kính thực sự thay đổi. |
| `AddProvider(provider)` | Đăng ký factory cho 1 `UnitType`. Provider được gọi khi client nhận `BatchEnter`/`SyncEnter` để tạo GameObject, và `BatchLeave`/`SyncLeave` để hủy. |
| `GetAllUnits()` | Trả về toàn bộ unit client đang track. |
| `GetAllUnitsByType(type)` | Trả về các unit cùng loại (vd: tất cả `"hero"`). |
| `GetUnit(id, type)` | Tìm 1 unit cụ thể theo id và type, trả về `null` nếu không có. |
| `Dispose()` | Dọn dẹp: hủy tất cả unit qua provider, clear dictionaries. |

### IPubSubClientModule

```csharp
public interface IPubSubClientModule : IClientModule, IDisposable
{
    static IPubSubClientModule Create(Config config);
    IPubSubClient Get();
}
```

Module cắm vào MyConnection client:

| Phương thức | Mô tả |
|-------------|-------|
| `Create(config)` | Tạo module với cấu hình ping |
| `Get()` | Lấy `IPubSubClient` để gọi `Tick()`, `MoveWatcher()`, `AddProvider()` |
| `Dispose()` | Hủy đăng ký TCP/UDP subscriber, dispose inner `PubSubClient` |

### IProvider

```csharp
public interface IProvider
{
    string UnitType { get; }

    IAlive CreateObject(long unitId, byte[] data);
    void UpdateObject(long unitId, IAlive obj, byte[] data);
    void DestroyObject(long unitId, IAlive obj);
    void OnEvent(long unitId, IAlive obj, string eventName, byte[] data, EventMeta meta);
}

public interface IProvider<T> : IProvider where T : class, IAlive
{
    new T CreateObject(long unitId, byte[] data);
    new void UpdateObject(long unitId, T obj, byte[] data);
    new void DestroyObject(long unitId, T obj);
    new void OnEvent(long unitId, T obj, string eventName, byte[] data, EventMeta meta);
}
```

Factory pattern cho từng loại unit. Bạn implement `IProvider<T>` (generic) là đủ — `IProvider` (non-generic) có default interface methods tự động delegate.

| Method | Khi nào gọi | Mô tả |
|--------|------------|-------|
| `CreateObject(unitId, data)` | Nhận `BatchEnter` hoặc `SyncEnter` lần đầu | Tạo object (vd: `Hero`), áp dụng data, trả về instance |
| `UpdateObject(unitId, obj, data)` | Nhận `SyncEnter` khi unit đã tồn tại | Cập nhật object hiện có (re-sync, tái sử dụng thay vì tạo mới) |
| `DestroyObject(unitId, obj)` | Nhận `BatchLeave` hoặc `SyncLeave` | Hủy object, cleanup resource |
| `OnEvent(unitId, obj, eventName, data, meta)` | Nhận `UnitEventMsg` | Xử lý event từ unit. `meta.Transport` cho biết event đến qua TCP hay UDP |
| `UnitType` | — | String type khớp với `unit.Type` trên server (vd: `"hero"`, `"mob"`) |

> Dùng `object` thay vì `GameObject` — provider trong Unity cast sang `GameObject`, provider test dùng `object` trực tiếp. Không phụ thuộc Unity Engine.

### ProviderAbstract\<T\>

```csharp
public abstract class ProviderAbstract<T> : IProvider<T> where T : class, IAlive
{
    protected ProviderAbstract(IPubSubClient client);

    public IReadOnlyList<IUnit<T>> GetAllUnits();
    public IUnit<T>? GetUnit(long unitId);

    public abstract string UnitType { get; }
    public abstract T CreateObject(long unitId, byte[] data);
    public abstract void UpdateObject(long unitId, T obj, byte[] data);
    public abstract void DestroyObject(long unitId, T obj);
    public abstract void OnEvent(long unitId, T obj, string eventName, byte[] data, EventMeta meta);
}
```

Abstract class convenience — kế thừa `ProviderAbstract<T>` thay vì implement `IProvider<T>` thủ công để có sẵn 2 helper lookup unit:

| Method | Mô tả |
|--------|-------|
| `GetAllUnits()` | Trả về tất cả `IUnit<T>` thuộc `UnitType` của provider này. `IUnit<T>.Target` trả về `T?` — không cần cast. |
| `GetUnit(unitId)` | Tìm unit theo id, trả về `null` nếu không có. |

Constructor nhận `IPubSubClient` để truy vấn nội bộ. Provider dùng:

```csharp
public class HeroProvider : ProviderAbstract<Hero>
{
    public HeroProvider(IPubSubClient client) : base(client) { }

    public override string UnitType => "hero";

    public override Hero CreateObject(long unitId, byte[] data) { ... }
    public override void UpdateObject(long unitId, Hero obj, byte[] data) { }
    public override void DestroyObject(long unitId, Hero obj) { ... }
    public override void OnEvent(long unitId, Hero obj, string eventName, byte[] data, EventMeta meta) { }

    void HealAll()
    {
        foreach (var u in GetAllUnits())  // IUnit<Hero>
        {
            var hero = u.Target;           // Hero? — typed!
            if (hero != null) hero.Hp = 100;
        }
    }
}

// Setup:
var client = module.Get();
client.AddProvider(new HeroProvider(client));
```

### EventMeta

```csharp
public enum EventTransport { Tcp, Udp }

public readonly struct EventMeta
{
    public EventTransport Transport { get; }
    public EventMeta(EventTransport transport);
}
```

Dùng trong `IProvider.OnEvent()` để biết event đến qua kênh nào:
- `EventTransport.Tcp` — reliable, đảm bảo delivery
- `EventTransport.Udp` — best-effort, có thể mất gói

> Dùng `object` cho mọi tham số — không phụ thuộc Unity Engine. Provider trong Unity cast sang `GameObject`, provider test dùng `object` trực tiếp.

### Config

```csharp
public class Config
{
    public int PingIntervalMs { get; set; } = 1000;
}
```

| Param | Default | Mô tả |
|-------|---------|-------|
| `PingIntervalMs` | 1000 | Tần suất ping (ms). `Tick()` chỉ gửi ping khi thời gian từ lần ping trước ≥ giá trị này. |

### IPlayerMirrorClient

Contract cho dữ liệu player được mirror từ server xuống client. Mỗi type implement `IPlayerMirrorClient` tương ứng với 1 `IPlayerData` trên server — client tự động nhận cập nhật khi server thay đổi.

```csharp
namespace PubSubLib.Mirror;

public interface IPlayerMirrorClient
{
    long PlayerId { get; set; }                         // ID của player sở hữu dữ liệu
    string DataName { get; }                            // Tên duy nhất khớp với IPlayerData.DataName

    void ApplyUpdate(byte[] data, string commit);       // Nhận cập nhật dữ liệu từ server
    void DispatchMessage(string subject, byte[] data);  // Nhận message từ server
    void OnSendMessage(Action<string, long, byte[]> handler); // Đăng ký handler gửi message lên server
}
```

| Member | Mô tả |
|--------|-------|
| `PlayerId` | ID của player. Được set tự động bởi module khi nhận dữ liệu từ server. |
| `DataName` | String key khớp với `IPlayerData.DataName` trên server — dùng để route dữ liệu đúng type. |
| `ApplyUpdate(data, commit)` | Được gọi khi server `Commit()` dữ liệu — client nhận `(byte[] data, string commit)` và áp dụng thay đổi. |
| `DispatchMessage(subject, data)` | Được gọi khi server gửi message đến player (qua `IPlayerData.OnMessage`). |
| `OnSendMessage(handler)` | Đăng ký handler `(string subject, long playerId, byte[] data)` để gửi message từ client lên server. |

**Ví dụ implement:**

```csharp
[MirrorProtoClient(typeof(PlayerProfileMsg))]
public class PlayerProfileClient : IPlayerMirrorClient
{
    public long PlayerId { get; set; }
    public string DataName => "PlayerProfile";

    public int Level { get; set; }
    public int Gold { get; set; }

    public void ApplyUpdate(byte[] data, string commit)
    {
        // Deserialize data và áp dụng
        var msg = PlayerProfileMsg.Parser.ParseFrom(data);
        Level = msg.Level;
        Gold = msg.Gold;
    }

    public void DispatchMessage(string subject, byte[] data)
    {
        // Xử lý message từ server (vd: "Welcome", "Notification")
    }

    public void OnSendMessage(Action<string, long, byte[]> handler)
    {
        // Handler sẽ được gọi khi client muốn gửi message lên server
    }
}
```

### IPlayerSpeaksClient

Quản lý các `IPlayerMirrorClient` instance trên client side. Tự động subscribe/unsubscribe từ server khi `AddData<T>()`/`RemoveData<T>()`.

```csharp
public interface IPlayerSpeaksClient : IDisposable
{
    void AddData<T>() where T : class, IPlayerMirrorClient, new();
    T? GetData<T>() where T : class, IPlayerMirrorClient;
    void RemoveData<T>() where T : class, IPlayerMirrorClient;
}
```

| Phương thức | Mô tả |
|-------------|-------|
| `AddData<T>()` | Đăng ký type `T` để nhận mirror từ server. Module tự động tạo instance, subscribe TCP topic, và gọi `ApplyUpdate`/`DispatchMessage` khi có dữ liệu. |
| `GetData<T>()` | Lấy instance mirror hiện tại. Trả về `null` nếu chưa được `AddData` hoặc chưa nhận dữ liệu từ server. |
| `RemoveData<T>()` | Hủy đăng ký type `T` — ngừng nhận mirror, xóa instance. |
| `Dispose()` | Dọn dẹp tất cả mirror, unsubscribe TCP. |

### IPlayerSpeaksClientModule

Module cắm vào MyConnection client để quản lý kết nối player speaks.

```csharp
public interface IPlayerSpeaksClientModule : IClientModule, IDisposable
{
    static IPlayerSpeaksClientModule Create(int pingIntervalMs = 2000);
    IPlayerSpeaksClient Get();
    void Tick();
}
```

| Phương thức | Mô tả |
|-------------|-------|
| `Create(pingIntervalMs)` | Static factory — tạo module mới với tần suất ping (ms). Mặc định 2000ms. |
| `Get()` | Lấy `IPlayerSpeaksClient` để gọi `AddData<T>()`, `GetData<T>()`, `RemoveData<T>()` |
| `Tick()` | Gọi định kỳ (mỗi frame). Gửi `PlayerPingMsg` qua TCP khi đến hạn ping. |
| `Dispose()` | Hủy đăng ký TCP subscriber, dispose inner `IPlayerSpeaksClient` |

**Ví dụ sử dụng:**

```csharp
// Tạo module với ping 500ms
var playerSpeaksModule = IPlayerSpeaksClientModule.Create(500);
var playerSpeaks = playerSpeaksModule.Get();

// Đăng ký nhận mirror dữ liệu PlayerProfile từ server
playerSpeaks.AddData<PlayerProfileClient>();

// Sau khi connect và nhận dữ liệu từ server:
var profile = playerSpeaks.GetData<PlayerProfileClient>();
if (profile != null)
{
    Console.WriteLine($"Level: {profile.Level}, Gold: {profile.Gold}");
}

// Khi không cần nữa:
playerSpeaks.RemoveData<PlayerProfileClient>();

// Gắn module vào client
client.AddModule(playerSpeaksModule);

// Game loop — gọi Tick() mỗi frame:
// playerSpeaksModule.Tick();
```

---

## Cách dùng

### Setup cơ bản

```csharp
using MyConnection;
using PubSubLib.Client;

// 1. Tạo MyConnection client (kết nối đến Router)
var client = IClient.Create(new ClientConfig
{
    tcpServer = "127.0.0.1:9090",
    udpServer = "127.0.0.1:9091",
    udpPingIntervalMs = 5000,
    udpPingTimeoutMs = 15000
});

// 2. Tạo PubSub client module
var pubSubModule = IPubSubClientModule.Create(new Config
{
    PingIntervalMs = 1000  // ping mỗi 1 giây
});

// 3. Đăng ký Provider cho từng loại unit
pubSubModule.Get()
    .AddProvider(new HeroProvider())
    .AddProvider(new MobProvider())
    .AddProvider(new ItemProvider());

// 4. Gắn module vào client
client.AddModule(pubSubModule);

// 5. Login (UserId = watcherId)
await client.Login(() => new LoginBody { UserId = "player_42" });
await client.ConnectServer();

// === Game Loop ===
// Mỗi frame:
//   pubSubModule.Get().Tick();                          // ping định kỳ
//   pubSubModule.Get().MoveWatcher(playerPos, 200f);     // cập nhật vị trí watcher
```

### Implement IProvider

```csharp
public class Hero : IAlive
{
    public bool IsAlive { get; set; } = true;
    // ... your fields
}

public class HeroProvider : IProvider<Hero>
{
    public string UnitType => "hero";

    public Hero CreateObject(long unitId, byte[] data)
    {
        var hero = new Hero();
        Console.WriteLine($"[Client] Tạo hero {unitId}, data={data?.Length ?? 0} bytes");
        return hero;
    }

    public void UpdateObject(long unitId, Hero obj, byte[] data)
    {
        // Cập nhật dữ liệu trên Hero đã có
    }

    public void DestroyObject(long unitId, Hero obj)
    {
        obj.IsAlive = false;
        Console.WriteLine($"[Client] Hủy hero {unitId}");
    }

    public void OnEvent(long unitId, Hero obj, string eventName, byte[] data, EventMeta meta)
    {
        if (meta.Transport == EventTransport.Udp)
        {
            // Xử lý event UDP (best-effort, có thể bỏ qua nếu FPS thấp)
        }
        else
        {
            // Xử lý event TCP (reliable, luôn được gọi)
        }
    }
}
```

### Game loop đầy đủ

```csharp
// Unity MonoBehaviour
public class GameManager : MonoBehaviour
{
    private IPubSubClient _pubSubClient;

    void Start()
    {
        // ... setup client, login ...
        var module = IPubSubClientModule.Create(new Config { PingIntervalMs = 500 });
        _pubSubClient = module.Get();
        _pubSubClient.AddProvider(new HeroProvider());
        client.AddModule(module);
    }

    void Update()
    {
        // Gọi Tick() mỗi frame — nội bộ tự kiểm tra thời gian ping
        _pubSubClient.Tick();

        // Gọi MoveWatcher() mỗi frame — nội bộ tự deduplicate
        _pubSubClient.MoveWatcher(transform.position, sightRadius);
    }
}
```

---

## Luồng xử lý nội bộ

### 1. Tick / Ping cycle

```
Update() mỗi frame
  │
  ▼ Tick()
  ├─ Kiểm tra _client != null?
  ├─ Kiểm tra elapsed >= _pingIntervalMs?
  │   └─ Không → return (skip ping lần này)
  │
  ├─ CleanDeadUnits()
  │   ├─ Duyệt _units, kiểm tra IsAlive
  │   ├─ Unit dead (IAlive.IsAlive == false) → DestroyObject + xóa khỏi _units
  │   └─ Unit alive → tiếp tục
  │
  ├─ Build PingUnitsCmd
  │   ├─ Gom tất cả unit theo type → TypeGroup
  │   │   hero: [id=42 v=7, id=43 v=3]
  │   │   mob:  [id=100 v=1]
  │   └─ cmd.Units.Add(groups)
  │
  └─ _client.SendOnUdp("PubSub.Cmd", PubSubCommand { PingUnits = cmd })
      │
      ▼ UDP ──► Router ──► NATS ──► Server // WatcherPingUnits()
```

**CleanDeadUnits** (`PubSubClient.cs:162-180`):
- Mỗi lần `Tick()`, trước khi build ping, client duyệt toàn bộ `_units`
- Nếu `unit.IsAlive == false` (target set `IAlive.IsAlive = false`): gọi `DestroyObject` trên provider, xóa khỏi `_units`
- Đảm bảo ping chỉ chứa unit thực sự còn sống

**Deduplication:** `Tick()` tự kiểm tra `_lastPingTimestamp` — chỉ gửi ping khi đủ `PingIntervalMs`. Bạn có thể gọi `Tick()` mỗi frame mà không lo spam.

### 2. Nhận event từ server

Client subscribe cả TCP và UDP topic `PubSub.Evt` để nhận event từ server, dispatch theo loại event:

```
TCP/UDP "PubSub.Evt" ──► PubSubClientModule.OnEvent()
                        │
                        ▼ switch evt.EvtCase
                    ┌──────────────────────────────────────────────┐
                    │ BatchEnterMsg                                │
                    │   → HandleBatchEnter(msg)                    │
                    │   → provider.CreateObject(unitId, ver, data) │
                    │   → _units[(id, type)] = new Unit(...)       │
                    │                                              │
                    │ SyncEnterMsg                                 │
                    │   → foreach item in msg.Units                │
                    │   → HandleSyncEnter(msg)                     │
                    │   → provider.CreateObject(...)               │
                    │   → _units[(id, type)] = new Unit(...)       │
                    │                                              │
                    │ BatchLeaveMsg                                │
                    │   → HandleBatchLeave(msg)                    │
                    │   → provider.DestroyObject(unitId, target)   │
                    │   → _units.Remove((id, type))                │
                    │                                              │
                    │ SyncLeaveMsg                                 │
                    │   → foreach group in msg.Keys                │
                    │   → foreach unitId in group.UnitIds          │
                    │   → HandleSyncLeave(msg)                     │
                    │   → provider.DestroyObject(...)              │
                    │   → _units.Remove((id, type))                │
                    │                                              │
                    │ UnitEventMsg                                 │
                    │   → HandleUnitEvent(msg)                     │
                    │   → provider.OnEvent(id, go, name, data, meta)│
                    └──────────────────────────────────────────────┘
```

**Luồng điển hình:**

```
1. Server tạo unit → BatchEnter → Client nhận → CreateObject → GameObject xuất hiện
2. Client Tick() ping → gửi version map → Server so sánh
3. Server thấy version cũ → SyncEnter với version mới → Client cập nhật
4. Server thấy unit đã mất → SyncLeave → Client DestroyObject → GameObject biến mất
```

### 3. MoveWatcher

```
MoveWatcher(position, radius)
  │
  ├─ Lưu _position = position, _radius = radius
  ├─ Kiểm tra dedup: vị trí và bán kính có khác lần gửi trước?
  │   └─ Không → return (không gửi lại)
  │
  ├─ _lastSentPos = position, _lastSentRadius = radius
  └─ _client.SendOnUdp("PubSub.Cmd", PubSubCommand { MoveWatcher = cmd })
      │
      ▼ UDP ──► Router ──► NATS ──► Server // pubSub.MoveWatcher()
```

**Deduplication:** So sánh `(x, y, radius)` hiện tại với lần gửi trước. Chỉ gửi khi thực sự thay đổi — tránh spam network khi nhân vật đứng yên.

### 4. Unit tracking với IAlive

```
Client _units: Dictionary<(long Id, string Type), Unit>

Unit (client-side):
  ├─ Id, Version, Type
  └─ IAlive _target → game object (Hero, Mob, ...)

Vòng đời:
  1. Nhận BatchEnter/SyncEnter → CreateObject → Unit(obj) với IAlive
  2. Tick() → CleanDeadUnits → kiểm tra Unit.IsAlive (delegate qua IAlive.IsAlive)
     ├─ Alive (target.IsAlive == true) → giữ lại trong ping
     └─ Dead (target.IsAlive == false) → DestroyObject + xóa khỏi _units
  3. Nhận BatchLeave/SyncLeave → DestroyObject + xóa khỏi _units
```

**Cơ chế lifecycle:**
- Unit giữ strong reference tới target object (IAlive)
- Target phải set `IsAlive = false` khi không còn dùng (vd: `DestroyObject` gọi `obj.IsAlive = false`)
- Lần `Tick()` tiếp theo, `CleanDeadUnits` phát hiện và dọn dẹp
- Server sẽ nhận ping thiếu unit → `SyncLeave` (nếu unit thực sự không còn trên server) hoặc `SyncEnter` (re-sync nếu unit vẫn còn)

### 5. Player data sync

```
Server IPlayerData.Commit("update")
  │
  ▼ NATS PlayerSpeaks.Msg ──────► Router
                                    │
                                    ▼ Forward TCP "PlayerSpeaks.Msg" ──► Client
                                                                          │
                                                                          ▼ PlayerSpeaksClientModule.OnMessage()
                                                                          ├─ Parse MirrorMessageEvent
                                                                          ├─ Tìm IPlayerMirrorClient theo DataName
                                                                          └─ client.ApplyUpdate(data, commit)

Server gửi message qua IPlayerData.OnMessage
  │
  ▼ NATS PlayerSpeaks.Msg ──────► Router
                                    │
                                    ▼ Forward TCP "PlayerSpeaks.Msg" ──► Client
                                                                          │
                                                                          ▼ PlayerSpeaksClientModule
                                                                          └─ client.DispatchMessage(subject, data)

Client gửi message lên server:
  │
  ▼ IPlayerMirrorClient.OnSendMessage(handler)
  └─ handler(subject, playerId, data)
      │
      ▼ TCP "PlayerSpeaks.Msg" ──► Router
                                    │
                                    ▼ NATS PlayerSpeaks.Msg ──► Server
                                                                  │
                                                                  ▼ PlayerSpeaksManager
                                                                  └─ IPlayerData.OnMessage handler
```

**Luồng hoàn chỉnh:**
1. Server tạo `IPlayerData` qua `manager.CreateData<T>(playerId)` + `data.DoneInit()`
2. Client đăng ký mirror type qua `playerSpeaks.AddData<T>()`
3. Client kết nối → Router gửi `PlayerPingMsg` lên NATS → Server `ProcessPing` → tạo defaults + set online
4. Client định kỳ gửi `PlayerPingMsg` qua TCP → Router forward NATS → Server refresh timer
5. Server `Commit("init")` → dữ liệu serialize → NATS → Router → TCP → Client `ApplyUpdate(data, "init")`
6. Khi dữ liệu thay đổi, server `Commit("update")` → client nhận `ApplyUpdate` với commit message
7. Khi player disconnect → Router gửi `PlayerOnlineStatusMsg(IsOnline=false)` → Server set `IsOnLine = false` + xóa timer
8. Sau `PlayerTimeoutSeconds`, ITimedCollection expiration → `DisconnectPlayer` xóa dữ liệu

---

## Ví dụ hoàn chỉnh

Full setup Client + Router + Server: xem [PubSubTestAll.cs](../PubSubLibTest/PubSubTestAll.cs) — test `FullStack_ClientKillsUnit_ServerResyncs` mô phỏng client mất unit → server re-sync.

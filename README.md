# SubPubC

Thư viện **Spatial Pub/Sub** cho game server viết bằng .NET. Theo dõi thực thể (**Unit**) trên lưới 2D và thông báo cho người quan sát (**Watcher**) khi unit vào/ra/di chuyển/phát sự kiện trong bán kính quan sát.

## Kiến trúc 3 thành phần

```
┌──────────────────┐                    ┌──────────────────┐        NATS         ┌──────────────────┐
│   Game Client    │  TCP (MyConnection) │     Router       │ ◄── PubSub.Cmd ── │   PubSub Server  │
│                  │ ◄─────────────────► │                  │ ─── PubSub.Evt ──► │                  │
│ PubSubLib.Client │                    │ PubSubLib.Router │                    │    PubSubLib     │
└──────────────────┘                    └──────────────────┘                    └──────────────────┘
```

| Thành phần | Package | Vai trò |
|------------|---------|---------|
| **Server** | `PubSubLib` | Sở hữu lưới không gian chính thống (authoritative). Tạo/di chuyển/hủy unit, quản lý watcher. |
| **Router** | `PubSubLib.Router` | Cầu nối giữa game client và PubSub server. Map connection → watcherId, forward command lên NATS, demux event xuống từng client. |
| **Client** | `PubSubLib.Client` | Chạy trong Unity/game process. Ping định kỳ để đồng bộ trạng thái, nhận event từ server để tạo/hủy GameObject. |

---

## Quick Start

### 1. Dùng standalone (không module, không Natify)

Dùng `PubSubLib` trực tiếp trong cùng một process. Phù hợp cho local test hoặc server monolithic.

```csharp
using PubSubLib;

// Tạo PubSub instance
var pubSub = IPubSub.Create(new PubSubConfig
{
    GridSize = 100f  // mỗi cell lưới 100x100 đơn vị
});

// Đăng ký callback — batch enter (unit vừa vào tầm quan sát)
pubSub.OnUnitEnter(tuple =>
{
    var watcherIds = tuple.Item1; // List<long> — watcher nào được thông báo
    var unit       = tuple.Item2; // IUnit
    Console.WriteLine($"[BatchEnter] Unit {unit.Id} vào tầm của watcher [{string.Join(",", watcherIds)}]");
});

// Sync enter — danh sách toàn bộ unit trong tầm khi thêm watcher hoặc re-sync
pubSub.OnUnitEnter(tuple =>
{
    foreach (var u in tuple.Item2) // List<IUnit>
        Console.WriteLine($"[SyncEnter] Watcher {tuple.Item1} thấy unit {u.Id}");
});

// Sync leave — unit không còn trong tầm
pubSub.OnUnitLeave(tuple =>
{
    foreach (var k in tuple.Item2) // List<UnitKey>
        Console.WriteLine($"[SyncLeave] Watcher {tuple.Item1} mất unit [{k.Id}]");
});

// Unit event
pubSub.OnUnitEvent(tuple =>
{
    Console.WriteLine($"[Event] {tuple.Item2.Type}:{tuple.Item2.Id} phát '{tuple.Item3}'");
});

// Thêm watcher
pubSub.AddWatcher(watcherId: 1, position: V(0, 0), radius: 200f);

// Tạo unit async
var target = new MyGameObject();
var unit = await pubSub.CreateUnitAsync<MyGameObject>(
    id: 42, type: "hero", position: V(50, 50), target: target
);

// Di chuyển unit
unit.Position = V(150, 150);
await pubSub.FlushAsync(); // đợi worker xử lý xong

// Phát sự kiện
unit.PublishEvent("attack", new { damage = 50 });
await pubSub.FlushAsync();

// Hủy unit
unit.Destroy();
await pubSub.FlushAsync();

// Ping giữ watcher sống + đồng bộ trạng thái
pubSub.WatcherPingUnits(1, new Dictionary<string, Dictionary<long, int>>());

// Dọn dẹp
pubSub.Dispose();

static Vector2 V(float x, float y) => new Vector2 { x = x, y = y };
```

### 2. Dùng full stack (Client + Router + Server + Natify)

Dùng cả 3 package để phân tán qua mạng. Cần NATS server chạy ở `nats://localhost:4222`.

#### Server

```csharp
using Natify;
using PubSubLib;

// Kết nối NATS
var natifyClient = new NatifyClientFast("nats://localhost:4222",
    "PubSubServer", "ServerGroup", "VN", "Router");

// Tạo PubSub + gắn Natify
var pubSub = IPubSub.Create(new PubSubConfig { GridSize = 100f });
pubSub.AddNatify(natifyClient);

// Từ đây mọi event (BatchEnter, SyncEnter, UnitEvent...) tự động publish lên NATS PubSub.Evt
// Mọi command từ client gửi lên PubSub.Cmd được tự động xử lý
```

#### Router

```csharp
using MyConnection;
using Natify;
using PubSubLib.Router;

// Tạo MyConnection server (TCP cho client, NATS cho server)
var server = IServer.Create(new ServerConfig
{
    tcpPort = 9090,
    udpPort = 9091,
    jwtSecret = "your-secret-key",
    jwtAudience = "game-audience",
    jwtIssuer = "game-issuer"
});

// Xác thực user
server.OnLogin<LoginBody>(body =>
{
    return Task.FromResult<IUser>(new GameUser(body.UserId));
});

// NATS bridge
var natifyServer = new NatifyServer("nats://localhost:4222",
    "Router", "RouterGroup", "PubSubServer");

// Gắn router module — map connection ↔ watcherId
server.AddModule(IPubSubRouterModule.Create(natifyServer, "VN"));
```

**Luồng Router:**
- Khi client connect → Router gửi `AddWatcher` lên Server qua NATS (watcherId = `connection.User.Id`)
- Khi client disconnect → Router gửi `RemoveWatcher`
- Khi client gửi `PubSub.Cmd` (MoveWatcher, PingUnits, PublishEvent) qua UDP → Router gán `watcherId`, forward lên NATS
- Khi nhận `PubSub.Evt` từ NATS → Router demux đến đúng client TCP dựa trên `watcherId`

#### Client (Unity / Game Client)

```csharp
using MyConnection;
using PubSubLib.Client;

// Kết nối đến Router
var client = IClient.Create(new ClientConfig
{
    tcpServer = "127.0.0.1:9090",
    udpServer = "127.0.0.1:9091",
    udpPingIntervalMs = 5000,
    udpPingTimeoutMs = 15000
});

// Tạo PubSub client module
var pubSubModule = IPubSubClientModule.Create(new Config
{
    PingIntervalMs = 1000  // ping mỗi 1 giây
});

// Đăng ký Provider — factory tạo/hủy GameObject cho từng UnitType
pubSubModule.Get()
    .AddProvider(new HeroProvider())
    .AddProvider(new MobProvider());

client.AddModule(pubSubModule);

// Login (UserId sẽ làm watcherId)
await client.Login(() => new LoginBody { UserId = "player_1" });
await client.ConnectServer();

// === Game loop ===
// Mỗi frame, gọi Tick() để client ping định kỳ:
//   pubSubModule.Get().Tick();
//
// Khi nhân vật di chuyển, gọi MoveWatcher():
//   pubSubModule.Get().MoveWatcher(newPosition, radius);

// Provider mẫu
public class HeroProvider : IProvider
{
    public string UnitType => "hero";

    public GameObjectTest CreateObject(long unitId, int version, byte[] data)
    {
        // Tạo GameObject từ prefab, áp dụng data...
        var go = new GameObjectTest();
        Console.WriteLine($"[Client] Tạo hero {unitId} v{version}");
        return go;
    }

    public void DestroyObject(long unitId, GameObjectTest obj)
    {
        // Hủy GameObject
        Console.WriteLine($"[Client] Hủy hero {unitId}");
    }
}
```

**Luồng Client:**
1. `Tick()` định kỳ build `PingUnitsCmd` chứa tất cả unit client đang track + version → gửi UDP `PubSub.Cmd`
2. Server so sánh version → trả về `SyncEnter` (unit mới/thay đổi) hoặc `SyncLeave` (unit đã mất)
3. Client nhận `PubSub.Evt` qua TCP → `HandleBatchEnter/HandleSyncEnter/HandleBatchLeave/HandleSyncLeave`
4. Provider `CreateObject/DestroyObject` được gọi để tạo/hủy GameObject tương ứng
5. Unit track bằng `WeakReference` — nếu GameObject bị GC collect, unit tự xóa khỏi danh sách ping

---

## Chi tiết từng thành phần

| File | Nội dung |
|------|----------|
| [docs/Server.md](docs/Server.md) | PubSubLib chi tiết: API, cấu hình, luồng xử lý nội bộ, standalone vs Natify |
| [docs/Router.md](docs/Router.md) | PubSubLib.Router chi tiết: bridge client-server, map connection, demux event |
| [docs/Client.md](docs/Client.md) | PubSubLib.Client chi tiết: ping cycle, IProvider, Unit tracking, WeakReference |

---

## Cấu trúc dự án

```
SubPubC.sln
├── PubSubLib/               # Core library (multi-target: netstandard2.1, net9.0)
│   ├── IPubSub.cs            # Public interface
│   ├── PubSub.cs             # Core implementation
│   ├── IUnit.cs              # Unit interface
│   ├── Unit.cs               # Unit implementation (WeakReference)
│   ├── Watcher.cs            # Watcher (vị trí, bán kính, known types)
│   ├── EventChannel.cs       # Worker thread (Channel<Action>)
│   ├── Cell.cs               # Grid cell
│   ├── PubSubNatifySync.cs   # NATS bridge (inbound/outbound)
│   └── Messages/             # Protobuf definitions
│
├── PubSubLib.Client/         # Game client module (netstandard2.1, Unity IL2CPP)
│   ├── IPubSubClient.cs       # Client interface (Tick, MoveWatcher, AddProvider)
│   ├── PubSubClient.cs        # Client implementation
│   ├── IPubSubClientModule.cs # Module interface (IClientModule)
│   ├── PubSubClientModule.cs  # Bridges MyConnection events
│   ├── IProvider.cs           # Factory tạo/hủy GameObject
│   └── Config.cs              # PingIntervalMs
│
├── PubSubLib.Router/         # Router module (netstandard2.1)
│   ├── IPubSubNatifyClient.cs  # Natify client interface
│   ├── PubSubNatifyClient.cs   # NATS communication
│   ├── IPubSubRouterModule.cs  # Router module interface (IServerModule)
│   └── PubSubRouterModule.cs   # Maps connections ↔ watchers
│
├── PubSubLib.Contracts/      # Shared protobuf messages
├── PubSubLibTest/            # Test project (xUnit)
└── SubPubCTest/              # ASP.NET Core test app
```

## Build & Test

```bash
# Build
dotnet build PubSubLib/PubSubLib.csproj
dotnet build PubSubLibTest/PubSubLibTest.csproj

# Test (cần NATS server cho test Natify)
dotnet test PubSubLibTest/PubSubLibTest.csproj
```

## Docker

```bash
docker-compose up -d   # NATS + SubPubC console
```

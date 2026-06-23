# Router — PubSubLib.Router

Package cầu nối giữa game client (kết nối TCP qua MyConnection) và PubSub server (qua NATS). Router là một `IServerModule` cắm vào MyConnection server.

## Mục lục

- [Vai trò](#vai-trò)
- [Cài đặt](#cài-đặt)
- [Kiến trúc](#kiến-trúc)
- [API Reference](#api-reference)
  - [IPubSubRouterModule](#ipubsubroutermodule)
  - [IPubSubNatifyClient](#ipubsubnatifyclient)
  - [IPlayerSpeaksRouterModule](#iplayerspeaksroutermodule)
  - [IPlayerSpeaksNatifyClient](#iplayerspeaksnatifyclient)
- [Cách dùng](#cách-dùng)
- [Luồng xử lý nội bộ](#luồng-xử-lý-nội-bộ)
  - [Client connect](#1-client-connect)
  - [Client disconnect](#2-client-disconnect)
  - [Client gửi command](#3-client-gửi-command)
  - [Server gửi event](#4-server-gửi-event)

---

## Vai trò

```
Game Client ◄── TCP (MyConnection) ──► Router ◄── NATS ──► PubSub Server
```

Router làm 3 nhiệm vụ chính:

| Hướng | Nhiệm vụ |
|-------|----------|
| **Client → Server** | Nhận `PubSub.Cmd` và `PlayerSpeaks.Msg` từ client qua TCP/UDP → gán `watcherId`/`playerId` từ connection → forward lên NATS |
| **Server → Client** | Nhận `PubSub.Evt` và `PlayerSpeaks.Msg` từ NATS → demux (phân phối) đến đúng client TCP dựa trên `watcherId`/`playerId` trong message |
| **Player Speak** | Theo dõi online/offline status của player → publish `PlayerOnlineStatusMsg` qua NATS khi client connect/disconnect |

Router **không** chứa logic game. Nó chỉ là cầu nối — map connection ↔ watcherId và forward dữ liệu.

---

## Cài đặt

```xml
<PackageReference Include="PubSubLib.Router" Version="1.4.1" />
```

Hoặc project reference:

```xml
<ProjectReference Include="..\PubSubLib.Router\PubSubLib.Router.csproj" />
```

Dependencies:
- `PubSubLib` — dùng các kiểu proto message
- `Natify` — giao tiếp NATS với PubSub server
- `MyConnection` — giao tiếp TCP/UDP với game client
- `System.Threading.Channels`

---

## Kiến trúc

```
                          PubSubRouterModule
┌──────────────┐         ┌──────────────────────────────────────┐       ┌──────────────┐
│ Game Client  │  TCP    │  IServerModule                       │ NATS  │ PubSub Server│
│              │◄──────►│                                      │◄─────►│              │
│ watcherId=1  │         │  _connections: {watcherId → conn}    │       │              │
│              │         │  _watcherIds:  {conn → watcherId}    │       │              │
└──────────────┘         │                                      │       └──────────────┘
                          │  ┌──────────────────────┐           │
                          │  │  IPubSubNatifyClient  │           │
                          │  │  ├─ SendAddWatcher()  │── NATS ──► PubSub.Cmd
                          │  │  ├─ SendRemoveWatcher()│         │
                          │  │  ├─ SendMoveWatcher() │          │
                          │  │  ├─ SendPingUnits()   │          │
                          │  │  └─ SendPublishEvent()│          │
                          │  │                      │           │
                          │  │  OnBatchEnter() ◄───── NATS ──── PubSub.Evt
                          │  │  OnBatchLeave()      │           │
                          │  │  OnSyncEnter()       │           │
                          │  │  OnSyncLeave()       │           │
                          │  │  OnUnitEvent()       │           │
                          │  └──────────────────────┘           │
                          └──────────────────────────────────────┘
```

---

## API Reference

### IPubSubRouterModule

```csharp
public interface IPubSubRouterModule : IServerModule
{
    static IPubSubRouterModule Create(NatifyServer server, string regionId);
}
```

Kế thừa `IServerModule` từ MyConnection — tự động tích hợp vào vòng đời server.

| Tham số | Mô tả |
|---------|-------|
| `server` | `NatifyServer` — kết nối NATS |
| `regionId` | Region identifier (vd: `"VN"`, `"US"`) — prefix cho NATS subject |

### IPubSubNatifyClient

Interface nội bộ dùng để giao tiếp NATS:

```csharp
public interface IPubSubNatifyClient : IDisposable
{
    static IPubSubNatifyClient Create(NatifyServer server, string regionId);

    // Gửi command → PubSub.Cmd
    void SendAddWatcher(AddWatcherCmd cmd);
    void SendRemoveWatcher(RemoveWatcherCmd cmd);
    void SendMoveWatcher(MoveWatcherCmd cmd);
    void SendPingUnits(PingUnitsCmd cmd);
    void SendPublishEvent(PublishEventCmd cmd);

    // Nhận event ← PubSub.Evt
    void OnBatchEnter(Action<BatchEnterMsg> callback);
    void OnBatchLeave(Action<BatchLeaveMsg> callback);
    void OnSyncEnter(Action<SyncEnterMsg> callback);
    void OnSyncLeave(Action<SyncLeaveMsg> callback);
    void OnUnitEvent(Action<UnitEventMsg> callback);
}
```

---

## Cách dùng

```csharp
using MyConnection;
using Natify;
using PubSubLib.Router;

// 1. Tạo MyConnection server (TCP cho client)
var server = IServer.Create(new ServerConfig
{
    tcpPort = 9090,
    udpPort = 9091,
    jwtSecret = "your-secret-key-here-32chars-min",
    jwtAudience = "game-audience",
    jwtIssuer = "game-issuer",
    restEndpoint = "/api",
    websocketEndpoint = "/ws"
});

// 2. Xác thực user — trả về IUser với Id = watcherId
server.OnLogin<LoginBody>(body =>
{
    // body.Value chứa dữ liệu login từ client
    // Id của IUser sẽ được dùng làm watcherId
    return Task.FromResult<IUser>(new GameUser(body.UserId));
});

// 3. Tạo NATS bridge
var natifyServer = new NatifyServer(
    "nats://localhost:4222",  // NATS URL
    "Router",                  // tên router
    "RouterGroup",             // group
    "PubSubServer"             // tên server để gửi command đến
);

// 4. Gắn router module
server.AddModule(IPubSubRouterModule.Create(natifyServer, "VN"));
```

**Yêu cầu:**
- NATS server chạy ở `nats://localhost:4222`
- PubSub server đã đăng ký với tên `"PubSubServer"` trong NATS

---

## Luồng xử lý nội bộ

### 1. Client connect

```
Client ── TCP connect ──► MyConnection Server
                              │
                              ▼ server.OnConnect callback
                          PubSubRouterModule.SetServer()
                              │
                              ├─ Parse watcherId = long.Parse(conn.User.Id)
                              ├─ _connections[watcherId] = conn
                              ├─ _watcherIds[conn] = watcherId
                              └─ _natifyClient.SendAddWatcher(new AddWatcherCmd
                                  {
                                      WatcherId = watcherId,
                                      PosX = 0, PosY = 0, Radius = 0
                                  })
                                  │
                                  ▼ NATS PubSub.Cmd ──────► PubSub Server
                                                              pubSub.AddWatcher(watcherId, ...)
```

- `watcherId` được lấy từ `conn.User.Id` (kết quả của `OnLogin` callback)
- Router gửi `AddWatcher` với vị trí/bán kính mặc định (0,0,0) — client sẽ gửi `MoveWatcher` để cập nhật sau
- Router đăng ký `SubscribeUdp<PubSubCommand>("PubSub.Cmd", ...)` để nhận command từ client

### 2. Client disconnect

```
Client ── TCP disconnect ──► MyConnection Server
                                │
                                ▼ server.OnDisconnect callback
                            PubSubRouterModule
                                │
                                ├─ _watcherIds.TryRemove(conn, out watcherId)
                                ├─ _connections.TryRemove(watcherId, out _)
                                └─ _natifyClient.SendRemoveWatcher(new RemoveWatcherCmd
                                    {
                                        WatcherId = watcherId
                                    })
                                    │
                                    ▼ NATS PubSub.Cmd ──────► PubSub Server
                                                                pubSub.RemoveWatcher(watcherId)
```

### 3. Client gửi command

Client gửi `PubSubCommand` qua UDP topic `PubSub.Cmd`. Router xử lý:

```
Client ── UDP "PubSub.Cmd" ──► server.SubscribeUdp → OnCommand(conn, cmd)
                                    │
                                    ▼
                                Kiểm tra conn.Connected?
                                Lấy watcherId từ _watcherIds
                                    │
                                    ▼ switch cmd.CmdCase
                                ┌───────────────────────────────┐
                                │ MoveWatcherCmd                │
                                │   → gán cmd.MoveWatcher.WatcherId = watcherId
                                │   → _natifyClient.SendMoveWatcher(cmd)
                                │                               │
                                │ PingUnitsCmd                  │
                                │   → gán cmd.PingUnits.WatcherId = watcherId
                                │   → _natifyClient.SendPingUnits(cmd)
                                │                               │
                                │ PublishEventCmd               │
                                │   → _natifyClient.SendPublishEvent(cmd)
                                │   (watcherId không cần thiết) │
                                └───────────────────────────────┘
                                    │
                                    ▼ NATS PubSub.Cmd ──────► PubSub Server
```

**Quan trọng:** Router **ghi đè** `WatcherId` trong `MoveWatcherCmd` và `PingUnitsCmd` bằng watcherId từ connection. Client không cần (và không nên) tự gửi watcherId — router đảm bảo tính xác thực.

### 4. Server gửi event

Server publish event lên NATS `PubSub.Evt`. Router nhận và demux:

```
NATS PubSub.Evt ──────► IPubSubNatifyClient callbacks
                            │
                            ▼ switch msg type
                        ┌───────────────────────────────────────────────────┐
                        │ BatchEnterMsg / BatchLeaveMsg                   │
                        │   → foreach watcherId in msg.WatcherIds        │
                        │       → if _connections.TryGetValue(watcherId) │
                        │       → if conn.Connected                      │
                        │       → _server.SendOnTcp("PubSub.Evt", conn, evt)│
                        │                                                │
                        │ UnitEventMsg                                   │
                        │   → foreach watcherId in msg.WatcherIds        │
                        │       → if _connections.TryGetValue(watcherId) │
                        │       → if conn.Connected                      │
                        │       → if msg.UseUdp:                         │
                        │           _server.SendOnUdp("PubSub.Evt", conn, evt)│
                        │         else:                                  │
                        │           _server.SendOnTcp("PubSub.Evt", conn, evt)│
                        │                                                │
                        │ SyncEnterMsg / SyncLeaveMsg                    │
                        │   → watcherId = msg.WatcherId (đơn lẻ)         │
                        │   → if _connections.TryGetValue(watcherId)     │
                        │   → if conn.Connected                          │
                        │   → _server.SendOnTcp("PubSub.Evt", conn, evt) │
                        └───────────────────────────────────────────────────┘
                            │
                            ▼ TCP "PubSub.Evt" ──────► Client
```

**Demux logic:**
- `BatchEnter`/`BatchLeave`: message chứa `repeated watcher_ids` — duyệt từng watcherId, tìm connection, gửi qua **TCP**
- `UnitEvent`: message chứa `repeated watcher_ids`. Nếu `msg.UseUdp == true` → gửi qua **UDP** (best-effort), ngược lại gửi qua **TCP** (reliable)
- `SyncEnter`/`SyncLeave`: message chứa 1 `watcherId` duy nhất — gửi trực tiếp qua **TCP**

---

### IPlayerSpeaksRouterModule

Module quản lý player speak — theo dõi kết nối client và đồng bộ trạng thái online/offline với server qua NATS.

```csharp
public interface IPlayerSpeaksRouterModule : IServerModule
{
    static IPlayerSpeaksRouterModule Create(NatifyServer server, string regionId);
}
```

Kế thừa `IServerModule` từ MyConnection — tự động tích hợp vào vòng đời server.

| Tham số | Mô tả |
|---------|-------|
| `server` | `NatifyServer` — kết nối NATS |
| `regionId` | Region identifier (vd: `"VN"`) — prefix cho NATS subject |

**Luồng hoạt động:**

```
Client ── TCP connect ──► MyConnection Server
                            │
                            ▼ OnConnect callback
                        PlayerSpeaksRouterModule
                            │
                            ├─ Parse playerId = long.Parse(conn.User.Id)
                            ├─ _connections[playerId] = conn
                            └─ _natifyClient.SendOnlineStatus(new PlayerOnlineStatusMsg
                                {
                                    PlayerId = playerId,
                                    IsOnline = true
                                })
                                │
                                ▼ NATS PlayerSpeaks.Msg ──► PubSub Server
                                                              │
                                                              ▼ PlayerSpeaksManager
                                                              SetOnline(playerId, true)
                                                              → Nếu có dữ liệu, Commit("init")

Client ── TCP disconnect ──►
                            ▼ OnDisconnect callback
                        PlayerSpeaksRouterModule
                            │
                            ├─ _connections.TryRemove(playerId, out _)
                            └─ _natifyClient.SendOnlineStatus(new PlayerOnlineStatusMsg
                                {
                                    PlayerId = playerId,
                                    IsOnline = false
                                })
                                │
                                ▼ NATS PlayerSpeaks.Msg ──► PubSub Server
                                                              │
                                                              ▼ PlayerSpeaksManager
                                                              SetOnline(playerId, false)
                                                              → CleanupLoop bắt đầu đếm timeout
```

### IPlayerSpeaksNatifyClient

Interface nội bộ dùng để giao tiếp NATS cho player speaks:

```csharp
public interface IPlayerSpeaksNatifyClient : IDisposable
{
    static IPlayerSpeaksNatifyClient Create(NatifyServer server, string regionId);

    void SendOnlineStatus(PlayerOnlineStatusMsg msg);               // Gửi trạng thái online/offline

    void OnPlayerSpeaks(Action<PlayerSpeaksEvent> callback);        // Nhận event từ server → forward đến client
    void OnMirrorMessage(Action<MirrorMessageEvent> callback);     // Nhận mirror message từ client → forward đến server
    void SendClientMsg(ClientMirrorMessage msg);                    // Gửi message từ client lên server
}
```

| Phương thức | Mô tả |
|-------------|-------|
| `SendOnlineStatus(msg)` | Publish `PlayerOnlineStatusMsg` lên NATS khi client connect/disconnect |
| `OnPlayerSpeaks(callback)` | Subscribe NATS event từ server (data update, commit) → forward TCP đến client |
| `OnMirrorMessage(callback)` | Subscribe NATS mirror message từ client → forward đến server |
| `SendClientMsg(msg)` | Gửi `ClientMirrorMessage` từ client lên NATS để server xử lý |

---

## Cấu trúc dữ liệu nội bộ

```csharp
// Map watcherId → connection (để demux event từ server)
private readonly ConcurrentDictionary<long, IConnection> _connections = new();

// Map connection → watcherId (để tra cứu watcherId khi nhận command từ client)
private readonly ConcurrentDictionary<IConnection, long> _watcherIds = new();
```

Cả 2 dictionary đều là `ConcurrentDictionary` — thread-safe, phù hợp với môi trường multi-thread của MyConnection server.

---

## Ví dụ hoàn chỉnh

Full setup Router + Server + Client: xem [PubSubTestAll.cs](../PubSubLibTest/PubSubTestAll.cs) — 3 integration test mô phỏng toàn bộ luồng thực tế.

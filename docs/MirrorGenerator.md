# Mirror Generator — PubSubLib.Mirror.Generator

Roslyn Incremental Source Generator tự động sinh code mirror proto từ class C# sang protobuf và ngược lại. Dùng cho cả **Server** (sở hữu dữ liệu, auto-commit) và **Client** (nhận mirror, read-only).

## Mục lục

- [Cài đặt](#cài-đặt)
- [Tổng quan](#tổng-quan)
- [Custom Attributes](#custom-attributes)
  - [[MirrorProto]](#mirrorproto)
  - [[MirrorProtoClient]](#mirrorprotoclient)
- [Kiểu dữ liệu hỗ trợ](#kiểu-dữ-liệu-hỗ-trợ)
  - [Scalar (đơn giản)](#1-scalar-đơn-giản)
  - [Repeated (danh sách)](#2-repeated-danh-sách)
  - [Vector3 (đơn)](#3-vector3-đơn)
  - [Vector3 (danh sách)](#4-vector3-danh-sách)
  - [Struct Group (SoA)](#5-struct-group-struct-of-arrays)
  - [Struct + Vector3 con](#6-struct--vector3-con)
- [Quy luật đặt tên proto](#quy-luật-đặt-tên-proto)
- [Quy tắc chung](#quy-tắc-chung)
- [So sánh Server vs Client](#so-sánh-server-vs-client)
- [Ví dụ hoàn chỉnh](#ví-dụ-hoàn-chỉnh)

---

## Cài đặt

```xml
<PackageReference Include="PubSubLib.Mirror.Generator" Version="1.19.2" />
<PackageReference Include="PubSubLib.Contracts" Version="1.5.1" />
```

Generator hoạt động ở compile-time — không cần runtime dependency ngoài `PubSubLib.Contracts` (chứa attribute definitions). Target `netstandard2.0`, tương thích Unity IL2CPP.

---

## Tổng quan

```
Bạn viết                              Generator sinh ra
─────────                             ──────────────────
[partial class]                       [partial class bổ sung]
  + [MirrorProto(typeof(Msg))]   →    Implement IPlayerData + IPlayerDataInternal
                                       Field → Property mapping
                                       Dirty tracking
                                       Auto-commit qua MirrorProtoBus

  + [MirrorProtoClient(typeof(Msg))] → Implement IPlayerMirrorClient
                                        Field → Read-only property
                                        SyncFromProto()
                                        ApplyUpdate(byte[], string)
```

Generator quét các property của protobuf message type, bỏ qua `PlayerId`, `IsOnLine`, `DataName` và enum oneof case, rồi sinh code tương ứng.

---

## Custom Attributes

### [MirrorProto]

Dùng cho **Server** — class sở hữu dữ liệu, có quyền ghi và auto-commit.

```csharp
namespace PubSubLib.Mirror;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MirrorProtoAttribute : Attribute
{
    public Type ProtoType { get; }      // Bắt buộc: Type của protobuf message (IMessage)
    public string? DataName { get; set; } // Tùy chọn: override DataName (mặc định = protoType.Name)
}
```

**Ràng buộc:**
- Chỉ gắn lên **class** (không struct, không enum)
- Class phải khai báo **partial**
- ProtoType phải là concrete protobuf message (kế thừa `IMessage`)

**Code sinh ra:**
- Implement `IPlayerData` + `IPlayerDataInternal`
- Property get/set cho scalar, `MirrorRepeatedList<T>` cho repeated
- `Commit(string)` — serialize thay đổi, gửi qua NATS
- `OnChange` / `OnMessage` / `SendMessage` callbacks
- `PlayerId`, `IsOnLine`, `DataName` tự động

### [MirrorProtoClient]

Dùng cho **Client** — nhận mirror từ server, read-only.

```csharp
namespace PubSubLib.Mirror;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MirrorProtoClientAttribute : Attribute
{
    public Type ProtoType { get; }  // Bắt buộc: cùng protobuf type như server dùng
}
```

**Ràng buộc:**
- Chỉ gắn lên **class** (không struct, không enum)
- Class phải khai báo **partial**
- ProtoType phải khớp với server

**Code sinh ra:**
- Implement `IPlayerMirrorClient`
- Property get-only cho mọi field
- `List<T>` backing cho repeated, `IReadOnlyList<T>` expose
- `ApplyUpdate(byte[], string)` — parse proto, sync dữ liệu
- `SyncFromProto()` — ánh xạ proto → C# fields
- `OnCommit(string)` — partial method (bạn có thể override)

---

## Kiểu dữ liệu hỗ trợ

### 1. Scalar (đơn giản)

```protobuf
message PlayerProfileMsg {
    int64 player_id = 1;   // → long PlayerId (RESERVED - bị bỏ qua)
    int32 level = 2;       // → int Level
    float health = 3;      // → float Health
    bool is_vip = 4;       // → bool IsVip
    string name = 5;       // → string Name
    bytes data = 6;        // → byte[] Data
}
```

```csharp
// Server
[MirrorProto(typeof(PlayerProfileMsg), DataName = "PlayerProfile")]
public partial class PlayerProfileData { }

// Client
[MirrorProtoClient(typeof(PlayerProfileMsg))]
public partial class PlayerProfileClient { }
```

**Server:** Property có get/set. Commit gửi toàn bộ scalar (không dirty tracking cho value type).

**Client:** Property get-only. `ApplyUpdate()` gán giá trị từ proto.

| Proto type | C# type |
|-----------|---------|
| `int64` | `long` |
| `int32` | `int` |
| `uint32` | `uint` |
| `float` | `float` |
| `double` | `double` |
| `bool` | `bool` |
| `string` | `string` |
| `bytes` | `byte[]` |

### 2. Repeated (danh sách)

```protobuf
message InventoryMsg {
    repeated string items = 1;       // → MirrorRepeatedList<string> Items
    repeated int32 quantities = 2;   // → MirrorRepeatedList<int> Quantities
}
```

```csharp
// Server: MirrorRepeatedList<T> — có IsDirty tracking
var data = manager.CreateData<InventoryData>(playerId);
data.Items.Add("Sword");    // IsDirty = true
data.Items.Add("Shield");
data.Commit("loot");         // serialize Items[], clear dirty

// Client: IReadOnlyList<T> — read-only
var client = playerSpeaks.GetData<InventoryClient>();
foreach (var item in client.Items) { ... }
```

**Server:**
- Backing field là `MirrorRepeatedList<T>` với `IsDirty` flag
- Khi commit: nếu `IsDirty` → serialize toàn bộ list, nếu không dirty → bỏ qua (tiết kiệm bandwidth)
- `ClearDirty()` reset flag sau commit

**Client:**
- Backing là `List<T>`, expose qua `IReadOnlyList<T>`
- `ApplyUpdate()` clear list rồi `AddRange` từ proto

### 3. Vector3 (đơn)

```protobuf
message PositionMsg {
    repeated float position_vector3 = 1;  // → Vector3 Position
}
```

Quy ước: property name kết thúc bằng `Vector3`, type là `repeated float` — generator hiểu đây là 3 float (x, y, z).

```csharp
// Server
data.Position = new Vector3 { x = 1, y = 2, z = 3 };
// Commit → serialize 3 float: [1, 2, 3]

// Client
Vector3 pos = client.Position;  // get-only
```

### 4. Vector3 (danh sách)

```protobuf
message PathMsg {
    repeated float waypoints_vector3_s = 1;  // → MirrorRepeatedList<Vector3> Waypoints
}
```

Quy ước: property name kết thúc bằng `Vector3S`, type là `repeated float`. Generator chia flat array thành từng bộ 3 float.

```csharp
// Server
data.Waypoints.Add(new Vector3 { x = 0, y = 0, z = 0 });
data.Waypoints.Add(new Vector3 { x = 1, y = 2, z = 3 });
// Commit → serialize [0,0,0, 1,2,3] (6 float)

// Client
foreach (var wp in client.Waypoints) { ... }  // IReadOnlyList<Vector3>
```

### 5. Struct Group (Struct of Arrays)

Pattern SoA (Struct of Arrays) — tối ưu cho nhiều instance cùng loại. Trigger khi có **ít nhất 2 repeated field** có tên theo pattern `PrefixXStructNameXMemberName`.

```protobuf
message TeamMsg {
    repeated int64 struct_x_player_x_id = 1;      // → Players[].Id
    repeated string struct_x_player_x_name = 2;    // → Players[].Name
    repeated int32 struct_x_player_x_level = 3;    // → Players[].Level
}
```

**Quy ước tách tên:**
- Tách PascalCase thành các từ
- `Struct` → prefix (bị bỏ qua)
- `Player` → tên struct (từ index 1 đến index n-1)
- `Id`, `Name`, `Level` → tên member (từ cuối cùng)
- Tối thiểu 2 field chung struct name để kích hoạt

Generator sinh ra:
```csharp
// Nested struct (tự động)
public readonly struct Player
{
    public long Id { get; }
    public string Name { get; }
    public int Level { get; }
    // Constructor(long id, string name, int level)
}

// Property truy cập
public MirrorRepeatedList<Player> Players { get; }  // plural: StructName + "s"
```

```csharp
// Server
data.Players.Add(new Player(1, "Alice", 10));
data.Players.Add(new Player(2, "Bob", 15));
data.Commit("team_update");

// Client
foreach (var p in client.Players)  // IReadOnlyList<Player>
{
    Console.WriteLine($"{p.Name} level {p.Level}");
}
```

**Cách commit struct group:**
1. Clear tất cả proto repeated field (`struct_x_player_x_id`, `struct_x_player_x_name`, `struct_x_player_x_level`)
2. Duyệt từng `Player` trong `Players`
3. Add `p.Id` → `struct_x_player_x_id`
4. Add `p.Name` → `struct_x_player_x_name`
5. Add `p.Level` → `struct_x_player_x_level`

### 6. Struct + Vector3 con

Vector3 có thể nằm trong struct group. 2 dạng:

#### a. Vector3 đơn trong struct

```protobuf
message UnitMsg {
    repeated int64 struct_x_unit_x_id = 1;
    repeated float struct_x_unit_x_position_vector3 = 2;  // → Unit.Position (Vector3)
}
```

`position_vector3`: tên member kết thúc bằng `Vector3`, type `repeated float`.

Struct sinh ra:
```csharp
public readonly struct Unit
{
    public long Id { get; }
    public Vector3 Position { get; }
}
```

Commit: mỗi struct element → 3 float vào proto repeated field.

#### b. Vector3 array trong struct

```protobuf
message PathUnitMsg {
    repeated int64 struct_x_unit_x_id = 1;
    repeated float struct_x_unit_x_waypoints_vector3_s_value = 2;   // giá trị flat
    repeated int32 struct_x_unit_x_waypoints_vector3_s_count = 3;   // số lượng Vector3 mỗi element
}
```

**Quy ước:** Mỗi cặp `{prefix}Vector3SValue` + `{prefix}Vector3SCount` trong cùng struct group tạo ra list `Vector3` trong struct.

- `*Vector3SValue` (`repeated float`) — tất cả x,y,z của mọi element nối tiếp
- `*Vector3SCount` (`repeated int32`) — số lượng Vector3 trong mỗi element

Struct sinh ra:
```csharp
public readonly struct Unit
{
    public long Id { get; }
    public IReadOnlyList<Vector3> Waypoints { get; }  // List<Vector3> trên server
}
```

Commit:
- Clear `*Vector3SValue` và `*Vector3SCount`
- Duyệt từng Unit: add `waypoints.Count` vào `*Vector3SCount`, add x,y,z từng waypoint vào `*Vector3SValue`

---

## Quy luật đặt tên proto

### DataName

| Attribute | DataName mặc định | Override |
|-----------|-------------------|----------|
| `[MirrorProto]` | `protoType.Name` (vd: `"RemoveWatcherCmd"`) | Qua `DataName = "..."` |
| `[MirrorProtoClient]` | `protoType.Name` (không override được) | — |

`DataName` dùng để:
- Key phân biệt các loại dữ liệu trong `IPlayerSpeaksManager`
- Route mirror message từ server → client đúng type
- Phải **giống nhau** giữa server và client để mirror hoạt động

### Tên property → tên field

| Proto property | C# backing field | C# property |
|---------------|------------------|-------------|
| `WatcherId` | `_watcherId` | `WatcherId` |
| `Position` | `_position` | `Position` |
| `IsOnline` | `_isOnline` | `IsOnline` |

Quy tắc: first char lowercase + prefix `_`.

### Tên bị reserved (bỏ qua)

Generator luôn bỏ qua các proto property sau:
- `PlayerId` — tự động quản lý bởi `IPlayerData`
- `IsOnLine` — tự động quản lý bởi `IPlayerSpeaksManager`
- `DataName` — tự động từ attribute/proto type name
- Property kết thúc bằng `"Case"` có type là `enum` (protobuf oneof case)

### Struct group naming

```
Proto:      Struct_X_Player_X_Id
            ─────── ────── ──
            prefix  struct  member
```

- **Prefix:** Từ đầu tiên (thường là `Struct`), bị bỏ qua
- **Struct name:** Các từ ở giữa (vd: `Player`, `InventoryItem`)
- **Member name:** Từ cuối cùng (vd: `Id`, `Name`, `Position`)
- Tách bằng chữ in hoa (PascalCase)

### Vector3 naming

| Pattern | Ý nghĩa | Ví dụ |
|---------|---------|-------|
| `{name}Vector3` | Vector3 đơn | `PositionVector3` → `Vector3 Position` |
| `{name}Vector3S` | List Vector3 | `WaypointsVector3S` → `List<Vector3> Waypoints` |
| `{name}Vector3SValue` | Array Vector3 trong struct | `PathVector3SValue` + `PathVector3SCount` |
| `{name}Vector3SCount` | Đếm số Vector3/element trong struct | Đi cùng `*Vector3SValue` |

---

## Quy tắc chung

### 1. Class phải là partial
```csharp
// ĐÚNG
[MirrorProto(typeof(FooMsg))]
public partial class FooData { }

// SAI — thiếu partial
[MirrorProto(typeof(FooMsg))]
public class FooData { }  // Generator không chạy
```

### 2. ProtoType phải là concrete protobuf message
```csharp
// ĐÚNG — PlayerProfileMsg là class sinh từ .proto
[MirrorProto(typeof(PlayerProfileMsg))]

// SAI — IMessage là interface
[MirrorProto(typeof(IMessage))]
```

### 3. Struct group cần ít nhất 2 field
```protobuf
// SAI — chỉ 1 field, không đủ để tạo struct group
repeated int64 struct_x_foo_x_id = 1;

// ĐÚNG — 2+ field chung struct name
repeated int64 struct_x_foo_x_id = 1;
repeated string struct_x_foo_x_name = 2;
```

### 4. Dirty tracking (server only)
- **Scalar value types** (int, float, bool, ...): **không** dirty tracking — luôn serialize khi commit
- **Repeated fields** (`MirrorRepeatedList<T>`): có `IsDirty` — chỉ serialize khi có thay đổi
- **Vector3**: property setter luôn set dirty cho repeated backing
- **Struct groups**: backing là `MirrorRepeatedList<T>` — dirty khi thêm/xóa

### 5. Thread safety
- `MirrorProtoBus` dùng `Channel<Action>` — tất cả serialize/commit chạy tuần tự
- `Commit()` enqueue action, không block thread gọi
- Callback `OnChange` chạy trên background thread

### 6. Message routing
- Server `Commit("init")` → NATS `PlayerSpeaks.Msg` → Router → TCP → Client `ApplyUpdate(data, "init")`
- Server `SendMessage<T>(subject, data)` → NATS → Router → TCP → Client `DispatchMessage(subject, data)`
- Client `SendMessage<T>(subject, data)` → TCP → Router → NATS → Server `OnMessage`

---

## So sánh Server vs Client

| Khía cạnh | Server (`[MirrorProto]`) | Client (`[MirrorProtoClient]`) |
|-----------|--------------------------|-------------------------------|
| Interface | `IPlayerData` + `IPlayerDataInternal` | `IPlayerMirrorClient` |
| DataName | Có thể override | Luôn là protoType.Name |
| Scalar | get/set | get-only |
| Repeated | `MirrorRepeatedList<T>` (có IsDirty) | `IReadOnlyList<T>` (List<T> backing) |
| Vector3 | get/set | get-only |
| Struct group | `MirrorRepeatedList<Struct>` | `IReadOnlyList<Struct>` |
| Commit | `Commit(string)` — serialize + gửi NATS | Không có (chỉ nhận) |
| Sync | Không cần (là source of truth) | `ApplyUpdate(byte[], string)` |
| SendMessage | `SendMessage<T>(subject, data)` | `SendMessage<T>(subject, data)` |
| OnMessage | `OnMessage<T>(subject, callback)` | `OnMessage<T>(subject, callback)` |

---

## Ví dụ hoàn chỉnh

### Proto definition

```protobuf
// PubSubLib.Contracts / Messages / PlayerMessages.proto
syntax = "proto3";

message PlayerProfileMsg {
    int64 player_id = 1;                    // RESERVED - bị bỏ qua
    int32 level = 2;
    int32 gold = 3;
    string nickname = 4;
    repeated string achievements = 5;       // repeated field
    repeated float home_position_vector3 = 6; // Vector3 đơn

    // Struct group: InventoryItem
    repeated int32 struct_x_inventory_item_x_id = 10;
    repeated int32 struct_x_inventory_item_x_count = 11;

    // Struct group: Friend (có Vector3 đơn)
    repeated int64 struct_x_friend_x_player_id = 20;
    repeated string struct_x_friend_x_name = 21;
    repeated float struct_x_friend_x_position_vector3 = 22;
}
```

### Server side

```csharp
using PubSubLib;
using PubSubLib.Messages;
using PubSubLib.Mirror;

[MirrorProto(typeof(PlayerProfileMsg), DataName = "PlayerProfile")]
public partial class PlayerProfileData { }

// Sử dụng
var manager = IPlayerSpeaksManager.Create(config);
manager.OnDefault<PlayerProfileData>(async profile =>
{
    profile.Level = 1;
    profile.Gold = 100;
    profile.Nickname = "NewPlayer";
    profile.Achievements.Add("first_login");
    profile.HomePosition = new Vector3 { x = 0, y = 0, z = 0 };

    // Struct group
    profile.InventoryItems.Add(new InventoryItem(1001, 5));  // id=1001, count=5
    profile.Friends.Add(new Friend(42, "Alice", new Vector3 { x = 1, y = 2, z = 3 }));

    await Task.CompletedTask;
});

// Commit gửi dữ liệu đến client
var profile = manager.CreateData<PlayerProfileData>(playerId);
profile.Level = 10;
profile.Achievements.Add("level_10");
profile.Commit("level_up");  // serialize + NATS → Router → Client
```

### Client side

```csharp
using PubSubLib.Mirror;

[MirrorProtoClient(typeof(PlayerProfileMsg))]
public partial class PlayerProfileClient
{
    // Optional: override OnCommit để nhận thông báo khi có update
    partial void OnCommit(string commit)
    {
        Console.WriteLine($"[Client] Received commit: {commit}");
    }
}

// Sử dụng
var playerSpeaks = IPlayerSpeaksClientModule.Create().Get();
playerSpeaks.AddData<PlayerProfileClient>();

// Sau khi kết nối và nhận mirror:
var profile = playerSpeaks.GetData<PlayerProfileClient>();
if (profile != null)
{
    Console.WriteLine($"Level: {profile.Level}, Gold: {profile.Gold}");
    foreach (var item in profile.InventoryItems)
        Console.WriteLine($"  Item {item.Id} x{item.Count}");
    foreach (var friend in profile.Friends)
        Console.WriteLine($"  Friend: {friend.Name} at ({friend.Position.x}, {friend.Position.y})");
}

// Gửi message lên server
profile.SendMessage("buy_item", new BuyItemMsg { ItemId = 1001 }.ToByteArray());

// Nhận message từ server (đã được đăng ký tự động qua OnMessage)
```

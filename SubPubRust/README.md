# SubPubRust — Spatial Pub/Sub Service

Dịch vụ Pub/Sub không gian (spatial) hiệu năng cao, viết bằng Rust. Sử dụng NATS làm message broker để theo dõi vị trí Unit và Watcher trên lưới 2D, tự động thông báo khi Unit vào/ra vùng quan sát của Watcher.

Đây là bản Rust tương thích 100% API với [SubPubC](../SubPubC) (bản C#).

## Kiến trúc

```
┌──────────┐       NATS        ┌─────────────┐
│  Game    │ ◄──────────────► │ SubPubRust  │
│  Server  │  Unit.Enter/Move  │             │
│          │  Watcher.Enter    │  Grid Cell  │
│          │  ◄────────────── │  Tracking   │
│          │  Watcher.{id}.   │             │
│          │  Unit.Enter/Exit  │             │
└──────────┘                   └─────────────┘
```

**Modules:**

| File | Vai trò |
|---|---|
| `main.rs` | Entry point, NATS subscriptions, message parsing |
| `grid.rs` | `Vec2`, `CellId`, tính toán lưới không gian |
| `cell.rs` | Quản lý unit/watcher trong từng ô lưới |
| `unit.rs` | Xử lý enter/move/exit/event/payload của Unit |
| `watcher.rs` | Xử lý enter/move/exit của Watcher + publish NATS |

## NATS Topics

### Subscribe (nhận từ game server)

| Chủ đề | Ý nghĩa | Payload ASCII |
|---|---|---|
| `Unit.Enter` | Unit xuất hiện | `unitId,x,y` |
| `Unit.Move` | Unit di chuyển | `unitId,x,y` |
| `Unit.Event` | Unit gửi event | `unitId,event_name` |
| `Unit.Exit` | Unit rời bản đồ | `unitId` |
| `Unit.{unit_id}.Payload.{payload_subject}` | Unit gửi payload | `byte[]` |
| `Watcher.Enter` | Watcher bắt đầu quan sát | `watcherId,x,y,range` |
| `Watcher.Move` | Watcher di chuyển vùng quan sát | `watcherId,x,y,range` |
| `Watcher.Exit` | Watcher dừng quan sát | `watcherId` |
| `Watcher.{watcherId}.PingUnit` | ping toàn bộ unit | `unitId1,unitId2,...` |

Giá trị `x`, `y`, `range` là số thực (`float`). Các giá trị cách nhau dấu phẩy, không có khoảng trắng.

### Publish (trả về cho game server)

| Chủ đề | Ý nghĩa | Payload |
|---|---|---|
| `Watcher.{watcherId}.Unit.Enter` | Unit vào vùng quan sát | `id1,id2,...` |
| `Watcher.{watcherId}.Unit.Exit` | Unit rời vùng quan sát | `id1,id2,...` |
| `Watcher.{watcherId}.Unit.Event.{event_name}` | Unit event trong vùng | `unitId` |
| `Watcher.{watcherId}.Unit.{unitId}.Payload.{payload_subject}` | Payload từ Unit | `byte[]` |

## Cấu hình

| Biến môi trường | Mặc định | Mô tả |
|---|---|---|
| `NATS_URL` | `nats://localhost:4222` | Địa chỉ NATS server |
| `GRID_SIZE` | `100` | Kích thước mỗi ô lưới (đơn vị game) |

## Chạy

### Development

```bash
cd SubPubRust
cargo run
```

### Release

```bash
cargo build --release
NATS_URL=nats://localhost:4222 ./target/release/sub-pub-rust
```

### Docker

```bash
docker compose up -d
```

Hoặc build thủ công:

```bash
docker build -t sub-pub-rust .
docker run -e NATS_URL=nats://host:4222 sub-pub-rust
```

## Tối ưu so với bản C#

| Điểm | C# (SubPubC) | Rust (SubPubRust) |
|---|---|---|
| Cell ID | `string "x:y"` — allocate mỗi lần | `(i32, i32)` tuple — zero allocation |
| Collections | Custom `DictionaryShard` / `HashSetShard` | `DashMap` / `DashSet` — production-grade |
| Locking | `ReaderWriterLockSlim` (OS kernel) | `parking_lot` — user-space spinlock |
| Grid size | `static float` | `AtomicU32` — lock-free |
| Event system | C# delegate `Action<>` | Gọi trực tiếp + `tokio::spawn` |
| NATS publish | Sync blocking | Async buffered — non-blocking |
| Memory | GC managed | Zero-cost, no GC pauses |
| Binary | Runtime + nhiều MB | LTO + strip → binary nhỏ gọn |

## Hiệu năng ước tính (2-core 2.2GHz)

| Scenario | TPS |
|---|---|
| Unit.Move cùng cell | 300K – 500K |
| Unit.Move cross-cell, ~5 watchers | 80K – 150K |
| Mixed operations (game thực tế) | 50K – 100K |

## License

Internal use.

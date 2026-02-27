mod cell;
mod grid;
mod unit;
mod watcher;

use std::sync::OnceLock;

use bytes::Bytes;
use futures::StreamExt;
use tracing::{error, info};

// ── Global NATS client ───────────────────────────────────────────────
static NATS_CLIENT: OnceLock<async_nats::Client> = OnceLock::new();

/// Fire-and-forget NATS publish.  `Client::publish` only writes to an
/// internal buffer so the spawned task is essentially free.
pub(crate) fn nats_publish(subject: String, payload: Bytes) {
    if let Some(client) = NATS_CLIENT.get() {
        let client = client.clone();
        tokio::spawn(async move {
            if let Err(e) = client.publish(subject, payload).await {
                error!("NATS publish error: {}", e);
            }
        });
    }
}

// ── Entry point ──────────────────────────────────────────────────────

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::from_default_env()
                .add_directive(tracing::Level::INFO.into()),
        )
        .init();

    let nats_url =
        std::env::var("NATS_URL").unwrap_or_else(|_| "nats://localhost:4222".into());
    let grid_size_val: f32 = std::env::var("GRID_SIZE")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(100.0);

    grid::set_grid_size(grid_size_val);

    // ── Banner ───────────────────────────────────────────────────────
    println!(
        r#"

  /$$$$$$            /$$            /$$ /$$$$$$$            /$$              /$$$$$$ 
 /$$__  $$          | $$           /$$/| $$__  $$          | $$             /$$__  $$
| $$  \__/ /$$   /$$| $$$$$$$     /$$/ | $$  \ $$ /$$   /$$| $$$$$$$       | $$  \__/
|  $$$$$$ | $$  | $$| $$__  $$   /$$/  | $$$$$$$/| $$  | $$| $$__  $$      | $$      
 \____  $$| $$  | $$| $$  \ $$  /$$/   | $$____/ | $$  | $$| $$  \ $$      | $$      
 /$$  \ $$| $$  | $$| $$  | $$ /$$/    | $$      | $$  | $$| $$  | $$      | $$    $$
|  $$$$$$/|  $$$$$$/| $$$$$$$//$$/     | $$      |  $$$$$$/| $$$$$$$/      |  $$$$$$/
 \______/  \______/ |_______/|__/      |__/       \______/ |_______/        \______/ v.1.1

Sub/Pub Rust — Spatial Pub/Sub Service (grid_size={grid_size_val})


### Subscribe (nhận từ game server)

| Chủ đề | Ý nghĩa | Payload ASCII |
|---|---|---|
| `Unit.Enter` | Unit xuất hiện | `unitId,x,y` |
| `Unit.Move` | Unit di chuyển | `unitId,x,y` |
| `Unit.Event` | Unit gửi event | `unitId,event_name` |
| `Unit.Exit` | Unit rời bản đồ | `unitId` |
| `Unit.<unit_id>.Payload.<payload_subject>` | Unit gửi payload | `byte[]` |
| `Unit.Ping` | ping các unit (Expired : 10s) | `unitId1,unitId2,...` |
| `Watcher.Enter` | Watcher bắt đầu quan sát | `watcherId,x,y,range` |
| `Watcher.Move` | Watcher di chuyển vùng quan sát | `watcherId,x,y,range` |
| `Watcher.Exit` | Watcher dừng quan sát | `watcherId` |
| `Watcher.<watcherId>.PingUnit` | ping toàn bộ unit | `unitId1,unitId2,...` |

Giá trị `x`, `y`, `range` là số thực (`float`). Các giá trị cách nhau dấu phẩy, không có khoảng trắng.

### Publish (trả về cho game server)

| Chủ đề | Ý nghĩa | Payload |
|---|---|---|
| `Watcher.<watcherId>.Unit.Enter` | Unit vào vùng quan sát | `id1,id2,...` |
| `Watcher.<watcherId>.Unit.Exit` | Unit rời vùng quan sát | `id1,id2,...` |
| `Watcher.<watcherId>.Unit.Event.<event_name>` | Unit event trong vùng | `unitId` |
| `Watcher.<watcherId>.Unit.<unitId>.Payload.<payload_subject>` | Payload từ Unit | `byte[]` |
| `Provider.Unit.Enter` | Thông tin các unit cần cung cấp Unit.Enter | `unitId1,unitId2,...` |
"#
    );

    info!("Starting SubPubRust service...");
    info!("Connecting to NATS server at {}", nats_url);

    let client = async_nats::connect(&nats_url).await?;
    NATS_CLIENT
        .set(client.clone())
        .expect("NATS already initialized");

    // ── Subscriptions ────────────────────────────────────────────────
    let sub_unit_enter = client.subscribe("Unit.Enter").await?;
    let sub_unit_move = client.subscribe("Unit.Move").await?;
    let sub_unit_event = client.subscribe("Unit.Event").await?;
    let sub_unit_exit = client.subscribe("Unit.Exit").await?;
    let sub_unit_payload = client.subscribe("Unit.*.Payload.*").await?;
    let sub_unit_ping = client.subscribe("Unit.Ping").await?;
    let sub_watcher_enter = client.subscribe("Watcher.Enter").await?;
    let sub_watcher_move = client.subscribe("Watcher.Move").await?;
    let sub_watcher_exit = client.subscribe("Watcher.Exit").await?;
    let sub_watcher_ping = client.subscribe("Watcher.*.PingUnit").await?;

    info!("Subscribed to all topics. Waiting for messages...");

    // ── Spawn handlers ───────────────────────────────────────────────
    tokio::spawn(handle_unit_enter(sub_unit_enter));
    tokio::spawn(handle_unit_move(sub_unit_move));
    tokio::spawn(handle_unit_event(sub_unit_event));
    tokio::spawn(handle_unit_exit(sub_unit_exit));
    tokio::spawn(handle_unit_payload(sub_unit_payload));
    tokio::spawn(handle_unit_ping(sub_unit_ping));
    tokio::spawn(unit_expiry_checker());
    tokio::spawn(handle_watcher_enter(sub_watcher_enter));
    tokio::spawn(handle_watcher_move(sub_watcher_move));
    tokio::spawn(handle_watcher_exit(sub_watcher_exit));
    tokio::spawn(handle_watcher_ping(sub_watcher_ping));

    // Wait until Ctrl-C
    tokio::signal::ctrl_c().await?;
    info!("Shutting down...");
    Ok(())
}

// ── Message handlers ─────────────────────────────────────────────────

async fn handle_unit_enter(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        match parse_unit_message(&msg.payload) {
            Ok((unit_id, pos)) => unit::enter(&unit_id, pos),
            Err(e) => error!("Error processing Unit.Enter: {}", e),
        }
    }
}

async fn handle_unit_move(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        match parse_unit_message(&msg.payload) {
            Ok((unit_id, pos)) => unit::mov(&unit_id, pos),
            Err(e) => error!("Error processing Unit.Move: {}", e),
        }
    }
}

async fn handle_unit_event(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        match std::str::from_utf8(&msg.payload) {
            Ok(s) => {
                if let Some((unit_id, event_name)) = s.split_once(',') {
                    unit::event(unit_id, event_name);
                } else {
                    error!("Invalid Unit.Event format (expected 'unitId,event')");
                }
            }
            Err(e) => error!("Error processing Unit.Event: {}", e),
        }
    }
}

async fn handle_unit_exit(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        match std::str::from_utf8(&msg.payload) {
            Ok(unit_id) => unit::exit(unit_id),
            Err(e) => error!("Error processing Unit.Exit: {}", e),
        }
    }
}

async fn handle_unit_payload(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        // Subject format: Unit.{unitId}.Payload.{payloadSubject}
        let parts: Vec<&str> = msg.subject.split('.').collect();
        if parts.len() == 4 {
            let unit_id = parts[1];
            let payload_subject = parts[3];
            unit::payload(unit_id, msg.payload.clone(), payload_subject);
        } else {
            error!("Invalid Unit.*.Payload.* topic: {}", msg.subject);
        }
    }
}

async fn handle_unit_ping(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        match std::str::from_utf8(&msg.payload) {
            Ok(s) => {
                if s.is_empty() {
                    continue;
                }
                let unit_ids: Vec<&str> = s.split(',').collect();
                let missing = unit::ping(&unit_ids);
                if !missing.is_empty() {
                    // Ask providers to send Unit.Enter for these units
                    let payload = missing.join(",");
                    info!("Provider.Unit.Enter requested for: {}", payload);
                    nats_publish(
                        "Provider.Unit.Enter".to_string(),
                        Bytes::from(payload),
                    );
                }
            }
            Err(e) => error!("Error processing Unit.Ping: {}", e),
        }
    }
}

/// Background task: every 5 seconds, sweep expired units.
async fn unit_expiry_checker() {
    let mut interval = tokio::time::interval(std::time::Duration::from_secs(5));
    loop {
        interval.tick().await;
        unit::check_expired();
    }
}

async fn handle_watcher_enter(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        match parse_watcher_message(&msg.payload) {
            Ok((watcher_id, pos, range)) => {
                info!(
                    "Watcher.Enter: {} at ({}, {}) range={}",
                    watcher_id, pos.x, pos.y, range
                );
                watcher::enter(&watcher_id, pos, range);
            }
            Err(e) => error!("Error processing Watcher.Enter: {}", e),
        }
    }
}

async fn handle_watcher_move(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        match parse_watcher_message(&msg.payload) {
            Ok((watcher_id, pos, range)) => watcher::mov(&watcher_id, pos, range),
            Err(e) => error!("Error processing Watcher.Move: {}", e),
        }
    }
}

async fn handle_watcher_exit(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        match std::str::from_utf8(&msg.payload) {
            Ok(watcher_id) => {
                info!("Watcher.Exit: {}", watcher_id);
                watcher::exit(watcher_id);
            }
            Err(e) => error!("Error processing Watcher.Exit: {}", e),
        }
    }
}

async fn handle_watcher_ping(mut sub: async_nats::Subscriber) {
    while let Some(msg) = sub.next().await {
        // Subject format: Watcher.{watcherId}.PingUnit
        let parts: Vec<&str> = msg.subject.split('.').collect();
        if parts.len() != 3 {
            error!("Invalid Watcher.*.PingUnit topic: {}", msg.subject);
            continue;
        }
        let watcher_id = parts[1];
        match std::str::from_utf8(&msg.payload) {
            Ok(s) => {
                let unit_ids: Vec<&str> = if s.is_empty() {
                    Vec::new()
                } else {
                    s.split(',').collect()
                };
                watcher::ping_unit(watcher_id, &unit_ids);
            }
            Err(e) => error!("Error processing Watcher.PingUnit: {}", e),
        }
    }
}

// ── Parsing ──────────────────────────────────────────────────────────

fn parse_unit_message(data: &[u8]) -> anyhow::Result<(String, grid::Vec2)> {
    let msg = std::str::from_utf8(data)?;
    let parts: Vec<&str> = msg.splitn(3, ',').collect();
    anyhow::ensure!(
        parts.len() == 3,
        "Invalid Unit message: expected 'unitId,x,y'"
    );
    Ok((
        parts[0].to_string(),
        grid::Vec2 {
            x: parts[1].parse()?,
            y: parts[2].parse()?,
        },
    ))
}

fn parse_watcher_message(data: &[u8]) -> anyhow::Result<(String, grid::Vec2, f32)> {
    let msg = std::str::from_utf8(data)?;
    let parts: Vec<&str> = msg.splitn(4, ',').collect();
    anyhow::ensure!(
        parts.len() == 4,
        "Invalid Watcher message: expected 'watcherId,x,y,range'"
    );
    Ok((
        parts[0].to_string(),
        grid::Vec2 {
            x: parts[1].parse()?,
            y: parts[2].parse()?,
        },
        parts[3].parse()?,
    ))
}

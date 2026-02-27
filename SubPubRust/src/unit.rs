use std::sync::{Arc, LazyLock};
use std::time::Instant;

use bytes::Bytes;
use dashmap::DashMap;
use parking_lot::Mutex;
use tracing::info;

use crate::cell;
use crate::grid::{self, CellId, Vec2};
use crate::watcher;

// ── Global unit storage ──────────────────────────────────────────────
static UNITS: LazyLock<DashMap<String, Arc<Unit>>> = LazyLock::new(DashMap::new);

/// Expiry duration for unit ping (10 seconds).
const PING_EXPIRY: std::time::Duration = std::time::Duration::from_secs(10);

struct Unit {
    /// Current cell; `None` means the unit has no position yet.
    current_cell_id: Mutex<Option<CellId>>,
    /// Last time this unit was pinged. Updated on enter() and ping().
    last_ping: Mutex<Instant>,
}

impl Unit {
    fn new() -> Self {
        Self {
            current_cell_id: Mutex::new(None),
            last_ping: Mutex::new(Instant::now()),
        }
    }
}

// ── Helpers ──────────────────────────────────────────────────────────

fn get(unit_id: &str) -> Option<Arc<Unit>> {
    UNITS.get(unit_id).map(|r| r.value().clone())
}

fn get_or_create(unit_id: &str) -> Arc<Unit> {
    UNITS
        .entry(unit_id.to_string())
        .or_insert_with(|| Arc::new(Unit::new()))
        .value()
        .clone()
}

// ── Public API ───────────────────────────────────────────────────────

/// Unit appears at `position`. If it already existed, it is removed first.
pub fn enter(unit_id: &str, position: Vec2) {
    // Remove old state if any
    exit(unit_id);

    let unit = get_or_create(unit_id);
    let cell_id = grid::get_cell(position);
    *unit.current_cell_id.lock() = Some(cell_id);
    *unit.last_ping.lock() = Instant::now();
    cell::publish_unit_enter(unit_id, cell_id);
}

/// Unit moves to `new_position`. If it crosses a cell boundary, watchers
/// are notified of enter/exit.
/// **Ignored** if the unit has not called `enter()` first.
pub fn mov(unit_id: &str, new_position: Vec2) {
    let unit = match get(unit_id) {
        Some(u) => u,
        None => return, // not entered yet — ignore
    };

    // Read current cell (fast copy, then drop the lock)
    let current = *unit.current_cell_id.lock();

    match current {
        Some(current_cell_id) => {
            let new_cell_id = grid::get_cell(new_position);
            if current_cell_id != new_cell_id {
                cell::publish_move(unit_id, current_cell_id, new_cell_id);
                *unit.current_cell_id.lock() = Some(new_cell_id);
            }
        }
        None => {
            // Unit exists but has no cell yet (shouldn't happen after enter),
            // ignore to avoid accidental publish_unit_enter from Move.
        }
    }
}

/// Unit leaves the map entirely.
pub fn exit(unit_id: &str) {
    let unit = match get(unit_id) {
        Some(u) => u,
        None => return,
    };

    let cell_id = unit.current_cell_id.lock().take();

    if let Some(cid) = cell_id {
        let watchers = cell::get_watchers(cid);
        if !watchers.is_empty() {
            let uid = unit_id.to_string();
            for watcher_id in &watchers {
                watcher::publish_unit_exit(watcher_id, &[uid.clone()]);
            }
        }
        cell::remove_unit(unit_id, cid);
    }

    UNITS.remove(unit_id);
}

/// Unit fires a named event visible to watchers in its current cell.
pub fn event(unit_id: &str, event_name: &str) {
    let unit = match get(unit_id) {
        Some(u) => u,
        None => return,
    };

    let cell_id = *unit.current_cell_id.lock();

    if let Some(cid) = cell_id {
        let watchers = cell::get_watchers(cid);
        for watcher_id in &watchers {
            watcher::publish_unit_event(watcher_id, unit_id, event_name);
        }
    }
}

/// Handle `Unit.Ping` — refresh last-ping for known units,
/// return list of unknown unit IDs that need `Provider.Unit.Enter`.
pub fn ping(unit_ids: &[&str]) -> Vec<String> {
    let mut missing = Vec::new();
    for &uid in unit_ids {
        match get(uid) {
            Some(unit) => {
                *unit.last_ping.lock() = Instant::now();
            }
            None => {
                missing.push(uid.to_string());
            }
        }
    }
    missing
}

/// Check all units for ping expiry. Units that haven't been pinged
/// within `PING_EXPIRY` are removed via `exit()`.
pub fn check_expired() {
    let now = Instant::now();
    let expired: Vec<String> = UNITS
        .iter()
        .filter(|entry| now.duration_since(*entry.value().last_ping.lock()) > PING_EXPIRY)
        .map(|entry| entry.key().clone())
        .collect();

    for uid in &expired {
        info!("Unit {} expired (no ping for {:?}), removing", uid, PING_EXPIRY);
        exit(uid);
    }
}

/// Unit sends a binary payload (forwarded to watchers in its cell).
pub fn payload(unit_id: &str, data: Bytes, payload_subject: &str) {
    let unit = match get(unit_id) {
        Some(u) => u,
        None => return,
    };

    let cell_id = *unit.current_cell_id.lock();

    if let Some(cid) = cell_id {
        let watchers = cell::get_watchers(cid);
        for watcher_id in &watchers {
            watcher::publish_unit_payload(watcher_id, unit_id, data.clone(), payload_subject);
        }
    }
}

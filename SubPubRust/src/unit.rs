use std::sync::{Arc, LazyLock};

use bytes::Bytes;
use dashmap::DashMap;
use parking_lot::Mutex;

use crate::cell;
use crate::grid::{self, CellId, Vec2};
use crate::watcher;

// ── Global unit storage ──────────────────────────────────────────────
static UNITS: LazyLock<DashMap<String, Arc<Unit>>> = LazyLock::new(DashMap::new);

struct Unit {
    /// Current cell; `None` means the unit has no position yet.
    /// Mutex is used instead of RwLock because writes are the common case
    /// and the critical section is tiny (just a copy of an Option<(i32,i32)>).
    current_cell_id: Mutex<Option<CellId>>,
}

impl Unit {
    fn new() -> Self {
        Self {
            current_cell_id: Mutex::new(None),
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
    cell::publish_unit_enter(unit_id, cell_id);
}

/// Unit moves to `new_position`. If it crosses a cell boundary, watchers
/// are notified of enter/exit.
pub fn mov(unit_id: &str, new_position: Vec2) {
    let unit = get_or_create(unit_id);

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
            let cell_id = grid::get_cell(new_position);
            cell::publish_unit_enter(unit_id, cell_id);
            *unit.current_cell_id.lock() = Some(cell_id);
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

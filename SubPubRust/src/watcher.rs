use std::collections::HashSet;
use std::sync::{Arc, LazyLock};

use bytes::Bytes;
use dashmap::DashMap;
use parking_lot::RwLock;

use crate::cell;
use crate::grid::{self, CellId, Vec2};

// ── Global watcher storage ───────────────────────────────────────────
static WATCHERS: LazyLock<DashMap<String, Arc<Watcher>>> = LazyLock::new(DashMap::new);

struct Watcher {
    /// The set of grid cells this watcher currently observes.
    /// RwLock is used so the Move diff (read old → compute new → write)
    /// can snapshot cheaply while still supporting concurrent reads.
    cells: RwLock<HashSet<CellId>>,
}

impl Watcher {
    fn new() -> Self {
        Self {
            cells: RwLock::new(HashSet::new()),
        }
    }

    fn cells_snapshot(&self) -> HashSet<CellId> {
        self.cells.read().clone()
    }

    fn add_cells(&self, ids: &[CellId]) {
        let mut lock = self.cells.write();
        for &c in ids {
            lock.insert(c);
        }
    }

    fn remove_cells(&self, ids: &[CellId]) {
        let mut lock = self.cells.write();
        for c in ids {
            lock.remove(c);
        }
    }
}

// ── Helpers ──────────────────────────────────────────────────────────

fn get(watcher_id: &str) -> Option<Arc<Watcher>> {
    WATCHERS.get(watcher_id).map(|r| r.value().clone())
}

fn get_or_create(watcher_id: &str) -> Arc<Watcher> {
    WATCHERS
        .entry(watcher_id.to_string())
        .or_insert_with(|| Arc::new(Watcher::new()))
        .value()
        .clone()
}

// ── Public API ───────────────────────────────────────────────────────

/// Watcher starts observing around `position` with `range`.
pub fn enter(watcher_id: &str, position: Vec2, range: f32) {
    let watcher = get_or_create(watcher_id);

    let grid_cells = grid::get_cells_in_range(position, range);
    watcher.add_cells(&grid_cells);

    publish_unit_enter_by_cells(watcher_id, &grid_cells);
    cell::add_watcher_to_cells(watcher_id, &grid_cells);
}

/// Watcher moves its observation to `new_position` / `range`.
pub fn mov(watcher_id: &str, new_position: Vec2, range: f32) {
    let watcher = get_or_create(watcher_id);

    let old_cells = watcher.cells_snapshot();
    let new_cells: HashSet<CellId> = grid::get_cells_in_range(new_position, range)
        .into_iter()
        .collect();

    let cells_to_add: Vec<CellId> = new_cells.difference(&old_cells).copied().collect();
    let cells_to_remove: Vec<CellId> = old_cells.difference(&new_cells).copied().collect();

    publish_unit_enter_by_cells(watcher_id, &cells_to_add);
    publish_unit_exit_by_cells(watcher_id, &cells_to_remove);

    watcher.add_cells(&cells_to_add);
    watcher.remove_cells(&cells_to_remove);

    cell::add_watcher_to_cells(watcher_id, &cells_to_add);
    cell::remove_watcher_from_cells(watcher_id, &cells_to_remove);
}

/// Watcher stops observing.
pub fn exit(watcher_id: &str) {
    let watcher = match get(watcher_id) {
        Some(w) => w,
        None => return,
    };

    let cell_ids: Vec<CellId> = watcher.cells_snapshot().into_iter().collect();
    cell::remove_watcher_from_cells(watcher_id, &cell_ids);
    WATCHERS.remove(watcher_id);

    publish_unit_exit_by_cells(watcher_id, &cell_ids);
}

/// Ping: compare client-side unit list against server-side truth.
/// Missing units → Watcher.{id}.Unit.Enter, extra units → Watcher.{id}.Unit.Exit.
pub fn ping_unit(watcher_id: &str, client_unit_ids: &[&str]) {
    let watcher = match get(watcher_id) {
        Some(w) => w,
        None => return,
    };

    let cell_ids: Vec<CellId> = watcher.cells_snapshot().into_iter().collect();
    let server_units: HashSet<String> = cell::get_all_units_by_cells(&cell_ids)
        .into_iter()
        .collect();
    let client_units: HashSet<&str> = client_unit_ids.iter().copied().collect();

    // Units on server but not on client → need to enter
    let missing: Vec<String> = server_units
        .iter()
        .filter(|u| !client_units.contains(u.as_str()))
        .cloned()
        .collect();

    // Units on client but not on server → need to exit
    let extra: Vec<String> = client_units
        .iter()
        .filter(|u| !server_units.contains(**u))
        .map(|u| u.to_string())
        .collect();

    if !missing.is_empty() {
        publish_unit_enter(watcher_id, &missing);
    }
    if !extra.is_empty() {
        publish_unit_exit(watcher_id, &extra);
    }
}

// ── NATS publishing ──────────────────────────────────────────────────

fn publish_unit_enter_by_cells(watcher_id: &str, cell_ids: &[CellId]) {
    if cell_ids.is_empty() {
        return;
    }
    let units = cell::get_all_units_by_cells(cell_ids);
    if units.is_empty() {
        return;
    }
    publish_unit_enter(watcher_id, &units);
}

fn publish_unit_exit_by_cells(watcher_id: &str, cell_ids: &[CellId]) {
    if cell_ids.is_empty() {
        return;
    }
    let units = cell::get_all_units_by_cells(cell_ids);
    if units.is_empty() {
        return;
    }
    publish_unit_exit(watcher_id, &units);
}

/// Publish `Watcher.{watcherId}.Unit.Enter` with comma-joined unit IDs.
pub fn publish_unit_enter(watcher_id: &str, unit_ids: &[String]) {
    let subject = format!("Watcher.{}.Unit.Enter", watcher_id);
    let payload = unit_ids.join(",");
    crate::nats_publish(subject, Bytes::from(payload));
}

/// Publish `Watcher.{watcherId}.Unit.Exit` with comma-joined unit IDs.
pub fn publish_unit_exit(watcher_id: &str, unit_ids: &[String]) {
    let subject = format!("Watcher.{}.Unit.Exit", watcher_id);
    let payload = unit_ids.join(",");
    crate::nats_publish(subject, Bytes::from(payload));
}

/// Publish `Watcher.{watcherId}.Unit.Event.{event_name}` with the unit ID.
pub fn publish_unit_event(watcher_id: &str, unit_id: &str, event_name: &str) {
    let subject = format!("Watcher.{}.Unit.Event.{}", watcher_id, event_name);
    crate::nats_publish(subject, Bytes::from(unit_id.to_string()));
}

/// Publish `Watcher.{watcherId}.Unit.{unitId}.Payload.{subject}` with raw bytes.
pub fn publish_unit_payload(
    watcher_id: &str,
    unit_id: &str,
    payload: Bytes,
    payload_subject: &str,
) {
    let subject = format!(
        "Watcher.{}.Unit.{}.Payload.{}",
        watcher_id, unit_id, payload_subject
    );
    crate::nats_publish(subject, payload);
}

use std::collections::HashSet;
use std::sync::{Arc, LazyLock};

use dashmap::DashMap;
use dashmap::DashSet;

use crate::grid::CellId;
use crate::watcher;

// ── Global cell storage ──────────────────────────────────────────────
// DashMap is already sharded internally (replaces the C# DictionaryShard).
// Values are Arc<Cell> so we can release the DashMap shard lock immediately
// and still operate on the cell concurrently.
static CELLS: LazyLock<DashMap<CellId, Arc<Cell>>> = LazyLock::new(DashMap::new);

/// A spatial cell that tracks which units and watchers are present.
/// Inner DashSets provide lock-free concurrent read/write without
/// needing to hold the outer DashMap lock.
pub struct Cell {
    watchers: DashSet<String>,
    units: DashSet<String>,
}

impl Cell {
    fn new() -> Self {
        Self {
            watchers: DashSet::new(),
            units: DashSet::new(),
        }
    }
}

// ── Helpers ──────────────────────────────────────────────────────────

fn get_or_create(cell_id: CellId) -> Arc<Cell> {
    CELLS
        .entry(cell_id)
        .or_insert_with(|| Arc::new(Cell::new()))
        .value()
        .clone()
}

fn get(cell_id: CellId) -> Option<Arc<Cell>> {
    CELLS.get(&cell_id).map(|r| r.value().clone())
}

// ── Public API ───────────────────────────────────────────────────────

/// Notify all watchers in `cell_id` that `unit_id` entered, then add unit.
pub fn publish_unit_enter(unit_id: &str, cell_id: CellId) {
    let cell = get_or_create(cell_id);

    // Collect watchers snapshot first, then publish
    let watchers: Vec<String> = cell.watchers.iter().map(|r| r.key().clone()).collect();
    if !watchers.is_empty() {
        let uid = unit_id.to_string();
        for watcher_id in &watchers {
            watcher::publish_unit_enter(watcher_id, &[uid.clone()]);
        }
    }

    cell.units.insert(unit_id.to_string());
}

/// Handle a unit moving between cells — diff watchers and notify.
pub fn publish_move(unit_id: &str, old_cell_id: CellId, new_cell_id: CellId) {
    let old_cell = get_or_create(old_cell_id);
    let new_cell = get_or_create(new_cell_id);

    let old_watchers: HashSet<String> =
        old_cell.watchers.iter().map(|r| r.key().clone()).collect();
    let new_watchers: HashSet<String> =
        new_cell.watchers.iter().map(|r| r.key().clone()).collect();

    let uid = unit_id.to_string();

    // Watchers that no longer see this unit
    for watcher_id in old_watchers.difference(&new_watchers) {
        watcher::publish_unit_exit(watcher_id, &[uid.clone()]);
    }

    // Watchers that now see this unit
    for watcher_id in new_watchers.difference(&old_watchers) {
        watcher::publish_unit_enter(watcher_id, &[uid.clone()]);
    }

    old_cell.units.remove(unit_id);
    new_cell.units.insert(uid);
}

/// Collect all unique unit IDs across the given cells.
pub fn get_all_units_by_cells(cell_ids: &[CellId]) -> Vec<String> {
    let mut units = HashSet::new();
    for &cid in cell_ids {
        if let Some(cell) = get(cid) {
            for r in cell.units.iter() {
                units.insert(r.key().clone());
            }
        }
    }
    units.into_iter().collect()
}

/// Register a watcher in every listed cell.
pub fn add_watcher_to_cells(watcher_id: &str, cell_ids: &[CellId]) {
    let wid = watcher_id.to_string();
    for &cid in cell_ids {
        let cell = get_or_create(cid);
        cell.watchers.insert(wid.clone());
    }
}

/// Remove a watcher from every listed cell.
pub fn remove_watcher_from_cells(watcher_id: &str, cell_ids: &[CellId]) {
    for &cid in cell_ids {
        if let Some(cell) = get(cid) {
            cell.watchers.remove(watcher_id);
        }
    }
}

/// Return all watcher IDs in a given cell.
pub fn get_watchers(cell_id: CellId) -> Vec<String> {
    match get(cell_id) {
        Some(cell) => cell.watchers.iter().map(|r| r.key().clone()).collect(),
        None => Vec::new(),
    }
}

/// Remove a unit from a cell's unit set.
pub fn remove_unit(unit_id: &str, cell_id: CellId) {
    if let Some(cell) = get(cell_id) {
        cell.units.remove(unit_id);
    }
}

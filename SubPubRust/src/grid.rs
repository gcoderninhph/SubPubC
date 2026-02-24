use std::sync::atomic::{AtomicU32, Ordering};

/// 2D position vector.
#[derive(Debug, Clone, Copy)]
pub struct Vec2 {
    pub x: f32,
    pub y: f32,
}

/// Grid cell identifier as (col, row) tuple — avoids String allocation
/// and is faster to hash/compare than the C# "x:y" format.
pub type CellId = (i32, i32);

/// Grid size stored as atomic u32 (bit-pattern of f32) for lock-free access.
/// Default: 100.0
static GRID_SIZE_BITS: AtomicU32 = AtomicU32::new(0x42C8_0000); // 100.0f32

pub fn set_grid_size(size: f32) {
    GRID_SIZE_BITS.store(size.to_bits(), Ordering::Relaxed);
}

#[inline]
pub fn grid_size() -> f32 {
    f32::from_bits(GRID_SIZE_BITS.load(Ordering::Relaxed))
}

/// Map a world-space position to a grid cell.
#[inline]
pub fn get_cell(pos: Vec2) -> CellId {
    let gs = grid_size();
    ((pos.x / gs).floor() as i32, (pos.y / gs).floor() as i32)
}

/// Return every grid cell whose bounding square overlaps the circle (pos, range).
pub fn get_cells_in_range(pos: Vec2, range: f32) -> Vec<CellId> {
    let gs = grid_size();
    let min_x = ((pos.x - range) / gs).floor() as i32;
    let max_x = ((pos.x + range) / gs).floor() as i32;
    let min_y = ((pos.y - range) / gs).floor() as i32;
    let max_y = ((pos.y + range) / gs).floor() as i32;

    let cap = ((max_x - min_x + 1) * (max_y - min_y + 1)) as usize;
    let mut cells = Vec::with_capacity(cap);
    for x in min_x..=max_x {
        for y in min_y..=max_y {
            cells.push((x, y));
        }
    }
    cells
}

CREATE TABLE IF NOT EXISTS window_layouts (
    id         TEXT PRIMARY KEY,
    name       TEXT NOT NULL,
    name_key   TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_window_layouts_name
    ON window_layouts(name_key);

CREATE TABLE IF NOT EXISTS window_layout_slots (
    id                    TEXT PRIMARY KEY,
    layout_id             TEXT NOT NULL,
    slot_order            INTEGER NOT NULL,
    collection_id         TEXT NOT NULL,
    captured_window_title TEXT,
    monitor_device_name   TEXT NOT NULL,
    monitor_is_primary    INTEGER NOT NULL,
    captured_work_x       INTEGER NOT NULL,
    captured_work_y       INTEGER NOT NULL,
    captured_work_width   INTEGER NOT NULL,
    captured_work_height  INTEGER NOT NULL,
    captured_x            INTEGER NOT NULL,
    captured_y            INTEGER NOT NULL,
    captured_width        INTEGER NOT NULL,
    captured_height       INTEGER NOT NULL,
    normalized_x          REAL NOT NULL,
    normalized_y          REAL NOT NULL,
    normalized_width      REAL NOT NULL,
    normalized_height     REAL NOT NULL,
    window_state          TEXT NOT NULL,
    z_order               INTEGER NOT NULL,
    desktop_policy        TEXT NOT NULL DEFAULT 'current',
    UNIQUE (layout_id, slot_order),
    UNIQUE (layout_id, collection_id)
);

CREATE INDEX IF NOT EXISTS ix_window_layout_slots_layout
    ON window_layout_slots(layout_id);

CREATE INDEX IF NOT EXISTS ix_window_layout_slots_collection
    ON window_layout_slots(collection_id);

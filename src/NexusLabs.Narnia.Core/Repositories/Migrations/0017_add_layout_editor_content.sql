CREATE TABLE IF NOT EXISTS window_layout_monitors (
    id                      TEXT PRIMARY KEY,
    layout_id               TEXT NOT NULL,
    monitor_order           INTEGER NOT NULL,
    monitor_device_name     TEXT NOT NULL,
    monitor_is_primary      INTEGER NOT NULL,
    captured_monitor_x      INTEGER NOT NULL,
    captured_monitor_y      INTEGER NOT NULL,
    captured_monitor_width  INTEGER NOT NULL,
    captured_monitor_height INTEGER NOT NULL,
    captured_work_x         INTEGER NOT NULL,
    captured_work_y         INTEGER NOT NULL,
    captured_work_width     INTEGER NOT NULL,
    captured_work_height    INTEGER NOT NULL,
    UNIQUE (layout_id, monitor_device_name)
);

INSERT OR IGNORE INTO window_layout_monitors (
    id, layout_id, monitor_order, monitor_device_name, monitor_is_primary,
    captured_monitor_x, captured_monitor_y, captured_monitor_width, captured_monitor_height,
    captured_work_x, captured_work_y, captured_work_width, captured_work_height)
-- Migration 0016 stored only the work area. Use it as the conservative full-monitor
-- approximation for existing Layouts; newly created definitions persist both rectangles.
SELECT
    layout_id || ':' || monitor_device_name,
    layout_id,
    MIN(slot_order),
    monitor_device_name,
    MAX(monitor_is_primary),
    captured_work_x,
    captured_work_y,
    captured_work_width,
    captured_work_height,
    captured_work_x,
    captured_work_y,
    captured_work_width,
    captured_work_height
FROM window_layout_slots
GROUP BY
    layout_id,
    monitor_device_name,
    captured_work_x,
    captured_work_y,
    captured_work_width,
    captured_work_height;

CREATE INDEX IF NOT EXISTS ix_window_layout_monitors_layout
    ON window_layout_monitors(layout_id);

CREATE TABLE window_layout_slots_new (
    id                    TEXT PRIMARY KEY,
    layout_id             TEXT NOT NULL,
    slot_order            INTEGER NOT NULL,
    content_kind          TEXT NOT NULL,
    collection_id         TEXT,
    session_id            TEXT,
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
    CHECK (
        (content_kind = 'collection' AND collection_id IS NOT NULL AND session_id IS NULL) OR
        (content_kind = 'session' AND session_id IS NOT NULL AND collection_id IS NULL)
    ),
    UNIQUE (layout_id, slot_order)
);

INSERT INTO window_layout_slots_new (
    id, layout_id, slot_order, content_kind, collection_id, session_id,
    captured_window_title, monitor_device_name, monitor_is_primary,
    captured_work_x, captured_work_y, captured_work_width, captured_work_height,
    captured_x, captured_y, captured_width, captured_height,
    normalized_x, normalized_y, normalized_width, normalized_height,
    window_state, z_order, desktop_policy)
SELECT
    id, layout_id, slot_order, 'collection', collection_id, NULL,
    captured_window_title, monitor_device_name, monitor_is_primary,
    captured_work_x, captured_work_y, captured_work_width, captured_work_height,
    captured_x, captured_y, captured_width, captured_height,
    normalized_x, normalized_y, normalized_width, normalized_height,
    window_state, z_order, desktop_policy
FROM window_layout_slots;

DROP TABLE window_layout_slots;
ALTER TABLE window_layout_slots_new RENAME TO window_layout_slots;

CREATE INDEX ix_window_layout_slots_layout
    ON window_layout_slots(layout_id);

CREATE UNIQUE INDEX ux_window_layout_slots_collection
    ON window_layout_slots(layout_id, collection_id)
    WHERE collection_id IS NOT NULL;

CREATE UNIQUE INDEX ux_window_layout_slots_session
    ON window_layout_slots(layout_id, session_id)
    WHERE session_id IS NOT NULL;

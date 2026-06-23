CREATE TABLE IF NOT EXISTS terminal_windows (
    id               TEXT PRIMARY KEY,
    name             TEXT,
    pinned           INTEGER NOT NULL DEFAULT 0,
    source           TEXT NOT NULL,
    status           TEXT NOT NULL DEFAULT 'closed',
    terminal_pid     INTEGER,
    composition_key  TEXT NOT NULL,
    occurrence_count INTEGER NOT NULL DEFAULT 1,
    first_seen_at    TEXT NOT NULL,
    last_seen_at     TEXT NOT NULL,
    closed_at        TEXT
);

CREATE TABLE IF NOT EXISTS terminal_window_tabs (
    window_id  TEXT NOT NULL,
    session_id TEXT NOT NULL,
    tab_order  INTEGER NOT NULL DEFAULT 0,
    directory  TEXT,
    PRIMARY KEY (window_id, session_id)
);

CREATE INDEX IF NOT EXISTS ix_terminal_window_tabs_window ON terminal_window_tabs(window_id);
CREATE INDEX IF NOT EXISTS ix_terminal_windows_composition ON terminal_windows(composition_key);
CREATE INDEX IF NOT EXISTS ix_terminal_windows_status ON terminal_windows(status);

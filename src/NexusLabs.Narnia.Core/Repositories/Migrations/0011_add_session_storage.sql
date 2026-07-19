CREATE TABLE IF NOT EXISTS session_storage_current (
    session_id               TEXT PRIMARY KEY,
    scan_id                  TEXT NOT NULL,
    scanned_at               TEXT NOT NULL,
    previous_scanned_at      TEXT,
    total_bytes              INTEGER NOT NULL,
    previous_total_bytes     INTEGER,
    file_count               INTEGER NOT NULL,
    last_write_at            TEXT,
    events_bytes             INTEGER NOT NULL,
    session_database_bytes   INTEGER NOT NULL,
    checkpoints_bytes        INTEGER NOT NULL,
    rewind_bytes             INTEGER NOT NULL,
    artifacts_bytes          INTEGER NOT NULL,
    other_bytes              INTEGER NOT NULL,
    largest_file_bytes       INTEGER NOT NULL,
    largest_file_path        TEXT,
    is_complete              INTEGER NOT NULL,
    error                    TEXT,
    is_user_named            INTEGER NOT NULL,
    contains_git_repository  INTEGER NOT NULL,
    contains_linked_worktree INTEGER NOT NULL,
    contains_reparse_point   INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_session_storage_current_total_bytes
    ON session_storage_current(total_bytes DESC);

CREATE TABLE IF NOT EXISTS session_storage_daily (
    snapshot_date          TEXT PRIMARY KEY,
    scanned_at             TEXT NOT NULL,
    session_count          INTEGER NOT NULL,
    total_bytes            INTEGER NOT NULL,
    events_bytes           INTEGER NOT NULL,
    session_database_bytes INTEGER NOT NULL,
    checkpoints_bytes      INTEGER NOT NULL,
    rewind_bytes           INTEGER NOT NULL,
    artifacts_bytes        INTEGER NOT NULL,
    other_bytes            INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS session_storage_scan (
    id             INTEGER PRIMARY KEY CHECK (id = 1),
    status         TEXT NOT NULL,
    started_at     TEXT NOT NULL,
    completed_at   TEXT NOT NULL,
    session_count  INTEGER NOT NULL,
    complete_count INTEGER NOT NULL,
    error          TEXT
);

CREATE TABLE IF NOT EXISTS session_cleanup_audit (
    id              TEXT PRIMARY KEY,
    session_id      TEXT NOT NULL,
    requested_at    TEXT NOT NULL,
    completed_at    TEXT NOT NULL,
    estimated_bytes INTEGER NOT NULL,
    result          TEXT NOT NULL,
    error           TEXT
);

CREATE INDEX IF NOT EXISTS ix_session_cleanup_audit_session
    ON session_cleanup_audit(session_id, completed_at DESC);

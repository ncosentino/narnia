CREATE TABLE IF NOT EXISTS session_migrations (
    id                         TEXT PRIMARY KEY,
    source_session_id          TEXT NOT NULL,
    replacement_session_id     TEXT NOT NULL UNIQUE,
    status                     TEXT NOT NULL,
    recovery_packet_path       TEXT NOT NULL,
    recovery_packet_bytes      INTEGER NOT NULL DEFAULT 0,
    recovery_packet_truncated  INTEGER NOT NULL DEFAULT 0,
    error                      TEXT,
    created_at                 TEXT NOT NULL,
    updated_at                 TEXT NOT NULL,
    completed_at               TEXT
);

CREATE INDEX IF NOT EXISTS ix_session_migrations_source
    ON session_migrations(source_session_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_session_migrations_status
    ON session_migrations(status);

CREATE TABLE session_migrations_rebuilt (
    id                         TEXT PRIMARY KEY,
    source_session_id          TEXT NOT NULL,
    replacement_session_id     TEXT NOT NULL,
    status                     TEXT NOT NULL,
    recovery_packet_path       TEXT NOT NULL,
    recovery_packet_bytes      INTEGER NOT NULL DEFAULT 0,
    recovery_packet_truncated  INTEGER NOT NULL DEFAULT 0,
    error                      TEXT,
    created_at                 TEXT NOT NULL,
    updated_at                 TEXT NOT NULL,
    completed_at               TEXT,
    archived_events_path       TEXT,
    archived_events_sha256     TEXT,
    baseline_turn_count        INTEGER NOT NULL DEFAULT 0,
    baseline_updated_at        TEXT
);

INSERT INTO session_migrations_rebuilt (
    id,
    source_session_id,
    replacement_session_id,
    status,
    recovery_packet_path,
    recovery_packet_bytes,
    recovery_packet_truncated,
    error,
    created_at,
    updated_at,
    completed_at,
    archived_events_path,
    archived_events_sha256,
    baseline_turn_count,
    baseline_updated_at)
SELECT
    id,
    source_session_id,
    replacement_session_id,
    status,
    recovery_packet_path,
    recovery_packet_bytes,
    recovery_packet_truncated,
    error,
    created_at,
    updated_at,
    completed_at,
    archived_events_path,
    archived_events_sha256,
    baseline_turn_count,
    baseline_updated_at
FROM session_migrations;

DROP TABLE session_migrations;
ALTER TABLE session_migrations_rebuilt RENAME TO session_migrations;

CREATE INDEX ix_session_migrations_source
    ON session_migrations(source_session_id, created_at DESC);

CREATE INDEX ix_session_migrations_replacement
    ON session_migrations(replacement_session_id, created_at DESC);

CREATE UNIQUE INDEX ux_session_migrations_external_replacement
    ON session_migrations(replacement_session_id)
    WHERE source_session_id <> replacement_session_id;

CREATE INDEX ix_session_migrations_status
    ON session_migrations(status);

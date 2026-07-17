CREATE TABLE IF NOT EXISTS work_collections (
    id         TEXT PRIMARY KEY,
    name       TEXT NOT NULL,
    name_key   TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_work_collections_name
    ON work_collections(name_key);

CREATE TABLE IF NOT EXISTS work_collection_sessions (
    collection_id TEXT NOT NULL,
    session_id    TEXT NOT NULL,
    added_at      TEXT NOT NULL,
    PRIMARY KEY (collection_id, session_id)
);

CREATE INDEX IF NOT EXISTS ix_work_collection_sessions_collection
    ON work_collection_sessions(collection_id);

CREATE INDEX IF NOT EXISTS ix_work_collection_sessions_session
    ON work_collection_sessions(session_id);

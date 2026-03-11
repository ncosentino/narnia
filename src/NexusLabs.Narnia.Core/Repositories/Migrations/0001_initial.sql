CREATE TABLE IF NOT EXISTS session_overrides (
    session_id   TEXT PRIMARY KEY,
    display_name TEXT,
    repository   TEXT,
    branch       TEXT,
    notes        TEXT,
    created_at   TEXT NOT NULL,
    updated_at   TEXT NOT NULL
);

ALTER TABLE session_overrides ADD COLUMN local_path TEXT;

CREATE TABLE IF NOT EXISTS narnia_settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

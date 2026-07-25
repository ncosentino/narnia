ALTER TABLE session_migrations ADD COLUMN archived_events_path TEXT;
ALTER TABLE session_migrations ADD COLUMN archived_events_sha256 TEXT;
ALTER TABLE session_migrations ADD COLUMN baseline_turn_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE session_migrations ADD COLUMN baseline_updated_at TEXT;

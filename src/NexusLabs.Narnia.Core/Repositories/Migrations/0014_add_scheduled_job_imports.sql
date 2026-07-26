CREATE TABLE IF NOT EXISTS scheduled_job_imports (
    job_id                  TEXT PRIMARY KEY,
    package_id              TEXT NOT NULL,
    portable_job_id         TEXT NOT NULL,
    definition_fingerprint  TEXT NOT NULL,
    source_job_id           TEXT,
    imported_at             TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_scheduled_job_imports_origin
    ON scheduled_job_imports(package_id, portable_job_id);

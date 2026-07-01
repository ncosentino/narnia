CREATE TABLE IF NOT EXISTS scheduled_jobs (
    id           TEXT PRIMARY KEY,
    name         TEXT NOT NULL,
    description  TEXT,
    cwd          TEXT,
    cadence      TEXT,
    args         TEXT,
    script_path  TEXT,
    log_dir      TEXT,
    allow_flags  TEXT,
    task_folder  TEXT NOT NULL,
    task_name    TEXT NOT NULL,
    notes        TEXT,
    created_at   TEXT NOT NULL,
    updated_at   TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS scheduled_job_skills (
    job_id       TEXT NOT NULL,
    skill        TEXT NOT NULL,
    resolution   TEXT,
    skill_order  INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (job_id, skill)
);

CREATE INDEX IF NOT EXISTS ix_scheduled_job_skills_job ON scheduled_job_skills(job_id);
CREATE INDEX IF NOT EXISTS ix_scheduled_jobs_task ON scheduled_jobs(task_folder, task_name);

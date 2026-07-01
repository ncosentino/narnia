ALTER TABLE scheduled_jobs ADD COLUMN prompt        TEXT;
ALTER TABLE scheduled_jobs ADD COLUMN cadence_kind  TEXT;
ALTER TABLE scheduled_jobs ADD COLUMN cadence_time  TEXT;
ALTER TABLE scheduled_jobs ADD COLUMN cadence_days  TEXT;
ALTER TABLE scheduled_jobs ADD COLUMN copilot_args  TEXT;

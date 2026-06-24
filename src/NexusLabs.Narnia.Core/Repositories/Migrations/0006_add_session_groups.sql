CREATE TABLE IF NOT EXISTS session_groups (
    id         TEXT PRIMARY KEY,
    name       TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS session_group_members (
    group_id     TEXT NOT NULL,
    session_id   TEXT NOT NULL,
    member_order INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (group_id, session_id)
);

CREATE INDEX IF NOT EXISTS ix_session_group_members_group ON session_group_members(group_id);

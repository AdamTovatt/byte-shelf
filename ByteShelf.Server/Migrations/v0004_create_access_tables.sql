-- Who may use a shelf besides admins: users granted access to it, and keys programs present. Neither
-- cascades: a shelf or user is removed only after the code has removed (and logged) its grants and
-- keys, so a foreign key failure means that step was skipped.

-- One row per user granted access to a shelf. The id is what the change log names the row by.
CREATE TABLE shelf_grant (
    id TEXT PRIMARY KEY,
    shelf_id TEXT NOT NULL REFERENCES shelf(id),
    user_id TEXT NOT NULL REFERENCES user_account(id),
    permission TEXT NOT NULL CHECK (permission IN ('read', 'read_write')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_shelf_grant_shelf_user ON shelf_grant (shelf_id, user_id);
CREATE INDEX idx_shelf_grant_user ON shelf_grant (user_id);

-- A key works on exactly one shelf. Only the SHA-256 of its secret is stored: the secret has 256
-- random bits, so a fast hash is enough and a request is authenticated with one indexed lookup. The
-- name's length limit lives in ShelfKeys.MaxNameLength alone, as a file name's lives in ShelfPath, so
-- the rule has one home.
CREATE TABLE shelf_key (
    id TEXT PRIMARY KEY,
    shelf_id TEXT NOT NULL REFERENCES shelf(id),
    name TEXT NOT NULL CHECK (name <> ''),
    permission TEXT NOT NULL CHECK (permission IN ('read', 'add_only', 'read_write')),
    secret_sha256 TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_shelf_key_secret ON shelf_key (secret_sha256);
CREATE INDEX idx_shelf_key_shelf ON shelf_key (shelf_id);

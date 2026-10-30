-- Shelves, their directories and files, uploads in progress, and the change log replicas follow.
-- Contents live under objects/ named by SHA-256; a file row points at its object by hash, and an
-- object is removed only once no row (trash included) points at it. Nothing here cascades: rows
-- are removed by the purge, deepest first, so no delete reaches further than the code meant.

CREATE TABLE shelf (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    quota_bytes INTEGER CHECK (quota_bytes IS NULL OR quota_bytes >= 0),   -- NULL: no limit
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_shelf_name ON shelf (name);

-- Every shelf has exactly one root: the row with no parent and an empty name. A deletion marks a
-- directory and everything under it with one deletion_id, which is what a restore brings back.
CREATE TABLE directory (
    id TEXT PRIMARY KEY,
    shelf_id TEXT NOT NULL REFERENCES shelf(id),
    parent_id TEXT REFERENCES directory(id),
    name TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    deleted_at TEXT,
    deletion_id TEXT,
    CONSTRAINT directory_root_unnamed CHECK ((parent_id IS NULL) = (name = '')),
    CONSTRAINT directory_deletion_consistent CHECK ((deleted_at IS NULL) = (deletion_id IS NULL)),
    CONSTRAINT directory_root_never_deleted CHECK (parent_id IS NOT NULL OR deleted_at IS NULL)
);

CREATE UNIQUE INDEX idx_directory_root ON directory (shelf_id) WHERE parent_id IS NULL;
CREATE UNIQUE INDEX idx_directory_live_name ON directory (parent_id, name) WHERE deleted_at IS NULL;
CREATE INDEX idx_directory_deletion_id ON directory (deletion_id) WHERE deletion_id IS NOT NULL;

CREATE TABLE file (
    id TEXT PRIMARY KEY,
    shelf_id TEXT NOT NULL REFERENCES shelf(id),
    directory_id TEXT NOT NULL REFERENCES directory(id),
    name TEXT NOT NULL CHECK (name <> ''),
    content_type TEXT NOT NULL,
    size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
    sha256 TEXT NOT NULL CHECK (length(sha256) = 64),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    deleted_at TEXT,
    deletion_id TEXT,
    CONSTRAINT file_deletion_consistent CHECK ((deleted_at IS NULL) = (deletion_id IS NULL))
);

CREATE UNIQUE INDEX idx_file_live_name ON file (directory_id, name) WHERE deleted_at IS NULL;
CREATE INDEX idx_file_shelf_id ON file (shelf_id);
CREATE INDEX idx_file_sha256 ON file (sha256);
CREATE INDEX idx_file_deletion_id ON file (deletion_id) WHERE deletion_id IS NOT NULL;

-- An upload holds its declared size against the shelf's quota from the moment it starts until it
-- finishes or is removed. Its bytes are in staging/<id>.
CREATE TABLE upload (
    id TEXT PRIMARY KEY,
    shelf_id TEXT NOT NULL REFERENCES shelf(id),
    path TEXT NOT NULL,
    content_type TEXT NOT NULL,
    size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
    received_bytes INTEGER NOT NULL CHECK (received_bytes >= 0 AND received_bytes <= size_bytes),
    overwrite INTEGER NOT NULL,
    is_resumable INTEGER NOT NULL,           -- 0: a single PUT, which cannot outlive its request
    created_at TEXT NOT NULL,
    last_activity_at TEXT NOT NULL
);

CREATE INDEX idx_upload_shelf_id ON upload (shelf_id);

-- One row per change, in the order the changes committed: the entity's type and id, what was done,
-- and the entity's whole state afterwards (NULL once it is gone), so applying a row twice is harmless.
CREATE TABLE change_log (
    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    action TEXT NOT NULL,
    payload TEXT,
    created_at TEXT NOT NULL
);

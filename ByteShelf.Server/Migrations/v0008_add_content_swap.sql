-- An upload either stores a new file at a path, or swaps an existing file's contents in place (a
-- client encrypting or decrypting it). A swap names the file, which keeps its id, and the SHA-256 its
-- contents must still have when the upload finishes, so a change made since is never overwritten; it
-- has no path, overwrite or description. The table is rebuilt, since SQLite cannot drop NOT NULL, so
-- the check can hold each kind to its own columns. Uploads in progress are kept.
CREATE TABLE upload_new (
    id TEXT PRIMARY KEY,
    shelf_id TEXT NOT NULL REFERENCES shelf(id),
    started_by TEXT NOT NULL,
    path TEXT,
    content_type TEXT NOT NULL,
    size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
    received_bytes INTEGER NOT NULL CHECK (received_bytes >= 0 AND received_bytes <= size_bytes),
    overwrite INTEGER,
    description TEXT CHECK (description IS NULL OR length(description) BETWEEN 1 AND 2000),
    encrypted INTEGER NOT NULL DEFAULT 0 CHECK (encrypted IN (0, 1)),
    is_resumable INTEGER NOT NULL,           -- 0: a single PUT, which cannot outlive its request
    swap_file_id TEXT,
    swap_sha256 TEXT,
    created_at TEXT NOT NULL,
    last_activity_at TEXT NOT NULL,
    CHECK (CASE WHEN swap_file_id IS NULL
        THEN path IS NOT NULL AND overwrite IS NOT NULL AND swap_sha256 IS NULL
        ELSE path IS NULL AND overwrite IS NULL AND description IS NULL AND swap_sha256 IS NOT NULL
    END)
);

INSERT INTO upload_new (id, shelf_id, started_by, path, content_type, size_bytes, received_bytes, overwrite, description, encrypted, is_resumable, created_at, last_activity_at)
SELECT id, shelf_id, started_by, path, content_type, size_bytes, received_bytes, overwrite, description, encrypted, is_resumable, created_at, last_activity_at
FROM upload;

DROP TABLE upload;

ALTER TABLE upload_new RENAME TO upload;

CREATE INDEX idx_upload_shelf_id ON upload (shelf_id);

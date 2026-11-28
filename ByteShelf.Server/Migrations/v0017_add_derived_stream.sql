-- derived_size (v0016) takes the kind stream too: the contents remade for streaming. SQLite cannot change a CHECK, so the
-- table is made anew with what it held.
CREATE TABLE derived_size_new (
    sha256 TEXT NOT NULL CHECK (length(sha256) = 64),
    kind TEXT NOT NULL CHECK (kind IN ('thumbnail', 'preview', 'full', 'agent', 'stream')),
    size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
    PRIMARY KEY (sha256, kind)
) WITHOUT ROWID;

INSERT INTO derived_size_new (sha256, kind, size_bytes)
SELECT sha256, kind, size_bytes
FROM derived_size;

DROP TABLE derived_size;

ALTER TABLE derived_size_new RENAME TO derived_size;

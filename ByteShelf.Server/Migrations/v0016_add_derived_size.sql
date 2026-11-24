-- How many bytes each derived file on disk takes (DerivedStore), by the SHA-256 of the contents it was made from and its
-- kind, so a shelf's usage counts them for each of its file rows that holds the contents. Written in the step that keeps or
-- removes the file, and brought in line with the folder at startup and daily by derived.maintenance. A server's own, as
-- the folder is: never logged, replicated or backed up, so a replica counts what it made.
CREATE TABLE derived_size (
    sha256 TEXT NOT NULL CHECK (length(sha256) = 64),
    kind TEXT NOT NULL CHECK (kind IN ('thumbnail', 'preview', 'full', 'agent')),
    size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
    PRIMARY KEY (sha256, kind)
) WITHOUT ROWID;

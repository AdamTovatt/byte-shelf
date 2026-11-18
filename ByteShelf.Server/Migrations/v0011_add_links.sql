-- Every download (share) link and upload link this server has made, until it runs out or is revoked:
-- a link's token names its row by id, so deleting the row ends the link. Each server keeps its own, as
-- each signs its links with its own secret: a replica's links are made and used on the replica. The
-- rows are not in the change log, so replication never carries them.
--
-- Unlike the logged tables, every reference here cascades: nothing logs a link's removal, so the
-- database removes a link with its shelf, its file (when purged), or the user or key that made it,
-- on a primary and on a replica (whose applier removes those rows) alike.
CREATE TABLE shelf_link (
    id TEXT PRIMARY KEY,
    shelf_id TEXT NOT NULL REFERENCES shelf(id) ON DELETE CASCADE,
    kind TEXT NOT NULL CHECK (kind IN ('download', 'upload')),
    -- A download link's file.
    file_id TEXT REFERENCES file(id) ON DELETE CASCADE,
    -- An upload link's place, whether it replaces a file there, and the description it stores.
    path TEXT,
    overwrite INTEGER,
    description TEXT,
    created_by_user_id TEXT REFERENCES user_account(id) ON DELETE CASCADE,
    created_by_key_id TEXT REFERENCES shelf_key(id) ON DELETE CASCADE,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    CONSTRAINT shelf_link_one_maker CHECK ((created_by_user_id IS NULL) <> (created_by_key_id IS NULL)),
    CONSTRAINT shelf_link_kind_target CHECK (
        (kind = 'download' AND file_id IS NOT NULL AND path IS NULL AND overwrite IS NULL AND description IS NULL)
        OR (kind = 'upload' AND file_id IS NULL AND path IS NOT NULL AND overwrite IN (0, 1)))
);

CREATE INDEX idx_shelf_link_shelf ON shelf_link (shelf_id, created_at);
CREATE INDEX idx_shelf_link_expires_at ON shelf_link (expires_at);

-- For the cascades, which look a removed row's links up by these.
CREATE INDEX idx_shelf_link_file ON shelf_link (file_id) WHERE file_id IS NOT NULL;
CREATE INDEX idx_shelf_link_user ON shelf_link (created_by_user_id) WHERE created_by_user_id IS NOT NULL;
CREATE INDEX idx_shelf_link_key ON shelf_link (created_by_key_id) WHERE created_by_key_id IS NOT NULL;

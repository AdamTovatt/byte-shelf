-- What plugins keep about files: one JSON object a plugin and what it is about (ByteShelfCommon.PluginDocument,
-- whose rules hold wherever one is made or read, on a replica too; the checks on document are the database's own share of
-- them, all but how deep it nests).
-- A plugin that is no longer installed keeps its rows. Both tables are in the change log, whose entity id is a Guid made
-- from the plugin and what the row is about (PluginMetadataId), so no two rows anywhere are about the same.

-- About contents: what a plugin's reader found in them, for every plain file that holds them. Written only by
-- metadata.reading on a primary, as object_metadata is, with a null document for contents it found nothing in or
-- failed on, which are not read again by that version. Removed with the last plain file that holds the contents.
CREATE TABLE content_plugin_metadata (
    id TEXT PRIMARY KEY,
    plugin_id TEXT NOT NULL CHECK (plugin_id <> ''),
    sha256 TEXT NOT NULL CHECK (length(sha256) = 64),
    reader_version INTEGER NOT NULL,
    read_at TEXT NOT NULL,
    document TEXT CHECK (document IS NULL OR (json_valid(document) AND json_type(document) = 'object' AND length(CAST(document AS BLOB)) <= 65536)),
    CONSTRAINT content_plugin_metadata_one_row UNIQUE (plugin_id, sha256)
);

CREATE INDEX idx_content_plugin_metadata_sha256 ON content_plugin_metadata (sha256);

-- The version of the server's own reader that read the row: what another version read is read again, as a plugin's is.
-- 1 read every row from before versions were kept (ObjectMetadata.ReaderVersion).
ALTER TABLE object_metadata ADD COLUMN reader_version INTEGER NOT NULL DEFAULT 1;

-- About a file, whatever its contents. A null document was removed: its row stays, so a version is never given twice. It goes with the file's row, and nothing logs that: a replica keeps the document of
-- a file purged on its primary for as long as it keeps the file in its own trash.
CREATE TABLE file_plugin_metadata (
    id TEXT PRIMARY KEY,
    plugin_id TEXT NOT NULL CHECK (plugin_id <> ''),
    file_id TEXT NOT NULL REFERENCES file(id) ON DELETE CASCADE,
    version INTEGER NOT NULL CHECK (version > 0),
    document TEXT CHECK (document IS NULL OR (json_valid(document) AND json_type(document) = 'object' AND length(CAST(document AS BLOB)) <= 65536)),
    CONSTRAINT file_plugin_metadata_one_row UNIQUE (plugin_id, file_id)
);

-- For the cascade, which looks a removed file's rows up by it.
CREATE INDEX idx_file_plugin_metadata_file ON file_plugin_metadata (file_id);

-- The values of the fields an installed plugin declares in its documents, for search and listings, one row a field that has
-- one: taken from a document whenever it is written or applied from a primary (PluginFieldValueRepository), never logged or
-- replicated, and gone with it. Which fields are declared is a server's own, so a replica takes its own.
CREATE TABLE plugin_field_value (
    plugin_id TEXT NOT NULL,
    field TEXT NOT NULL,
    sha256 TEXT,                   -- of a document about contents, else null
    file_id TEXT,                  -- of a document about a file, else null
    text_fold TEXT,                -- a text, as search compares it
    number REAL,
    instant TEXT,
    flag INTEGER CHECK (flag IN (0, 1)),
    searched INTEGER NOT NULL CHECK (searched IN (0, 1)), -- whether a search's free text matches text_fold
    listed TEXT,                   -- the value as listings carry it, a ByteShelfCommon.PluginFieldValue; null when they do not
    CONSTRAINT plugin_field_value_one_document CHECK ((sha256 IS NULL) <> (file_id IS NULL)),
    CONSTRAINT plugin_field_value_one_value CHECK ((text_fold IS NOT NULL) + (number IS NOT NULL) + (instant IS NOT NULL) + (flag IS NOT NULL) = 1),
    FOREIGN KEY (plugin_id, sha256) REFERENCES content_plugin_metadata (plugin_id, sha256) ON DELETE CASCADE,
    FOREIGN KEY (plugin_id, file_id) REFERENCES file_plugin_metadata (plugin_id, file_id) ON DELETE CASCADE
);

-- For the cascades, and for looking a file's values up by its id and its contents.
CREATE UNIQUE INDEX idx_plugin_field_value_contents ON plugin_field_value (sha256, plugin_id, field);
CREATE UNIQUE INDEX idx_plugin_field_value_file ON plugin_field_value (file_id, plugin_id, field);

-- What the values above were taken by: the fields each plugin declared, and the version of the server's own rules. A plugin
-- whose values would be taken by something else at startup, or that is gone, has them taken again, or dropped (PluginFieldIndex).
CREATE TABLE plugin_values_taken (
    plugin_id TEXT PRIMARY KEY,
    taken_by TEXT NOT NULL
);

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

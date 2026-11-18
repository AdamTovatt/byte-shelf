-- What the server read from a file's contents: one row an object, keyed by its SHA-256 as the
-- object store is, so identical contents are read once. A row with nothing found still says the
-- contents were read. Written only by the job that reads them (metadata.reading) on a primary, and on
-- a replica only from the change log, whose entity id is a Guid: id is the first 128 bits of the
-- SHA-256 (ObjectMetadata.IdFor), so one contents never has two rows anywhere.
--
-- A file shows the row of its contents unless it is encrypted, whose contents the server never
-- reads. The *_fold columns hold FileSearch.Fold of the text beside them, which search matches as it
-- does a name. length() counts code points, as MetadataReader.MaxTextLength does; a replica writes what
-- its primary sends, so only these checks hold the limit there.
CREATE TABLE object_metadata (
    id TEXT PRIMARY KEY,
    sha256 TEXT NOT NULL UNIQUE CHECK (length(sha256) = 64),
    read_at TEXT NOT NULL,
    taken_at TEXT,
    latitude REAL,
    longitude REAL,
    duration_seconds REAL CHECK (duration_seconds IS NULL OR duration_seconds >= 0),
    page_count INTEGER CHECK (page_count IS NULL OR page_count >= 0),
    title TEXT CHECK (title IS NULL OR length(title) BETWEEN 1 AND 1000),
    title_fold TEXT,
    camera TEXT CHECK (camera IS NULL OR length(camera) BETWEEN 1 AND 1000),
    camera_fold TEXT,
    CONSTRAINT object_metadata_position_whole CHECK ((latitude IS NULL) = (longitude IS NULL)),
    CONSTRAINT object_metadata_title_folded CHECK ((title IS NULL) = (title_fold IS NULL)),
    CONSTRAINT object_metadata_camera_folded CHECK ((camera IS NULL) = (camera_fold IS NULL))
);

-- A file's description: free text, matched by search. NULL is no description, so an empty one never
-- exists. length() counts characters, as FileDescription.MaxLength does. A resumable upload carries
-- the description it will store until it finishes.
ALTER TABLE file ADD COLUMN description TEXT CHECK (description IS NULL OR length(description) BETWEEN 1 AND 2000);

ALTER TABLE upload ADD COLUMN description TEXT CHECK (description IS NULL OR length(description) BETWEEN 1 AND 2000);

-- What search compares, written by FileRepository from the row on every write, by the same Common
-- rules the in-memory client applies: the name and description folded by FileSearch.Fold (SQLite's
-- lower() folds ASCII only), and the content type without its parameters by ContentTypes.MediaTypeOf.
-- No release ever held a file before this script, so the defaults only stand in for the rows of a
-- development database, which search misses until they are written again.
ALTER TABLE file ADD COLUMN name_fold TEXT NOT NULL DEFAULT '';

ALTER TABLE file ADD COLUMN description_fold TEXT;

ALTER TABLE file ADD COLUMN media_type TEXT NOT NULL DEFAULT '';

-- A key holds grants the way a user does: one row per shelf it works on, with what it may do there.
-- A key always has at least one: the code deletes a key with its last grant. Like the other logged
-- tables nothing cascades, so a key or shelf is removed only after the code has removed (and logged)
-- its grants.
CREATE TABLE shelf_key_grant (
    id TEXT PRIMARY KEY,
    key_id TEXT NOT NULL REFERENCES shelf_key(id),
    shelf_id TEXT NOT NULL REFERENCES shelf(id),
    permission TEXT NOT NULL CHECK (permission IN ('read', 'add_only', 'read_write')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_shelf_key_grant_key_shelf ON shelf_key_grant (key_id, shelf_id);
CREATE INDEX idx_shelf_key_grant_shelf ON shelf_key_grant (shelf_id);

-- Every key becomes one grant on its shelf. The grant takes the key's id, which is unique among grants
-- since each key has one, so a primary and its replicas, each running this on its own database, make
-- the same rows and replication finds nothing to change.
INSERT INTO shelf_key_grant (id, key_id, shelf_id, permission, created_at, updated_at)
SELECT id, id, shelf_id, permission, created_at, created_at
FROM shelf_key;

-- Each grant is logged, as ShelfKeyGrantRepository logs one, so a whole copy read from the log holds it.
-- The payload is the ShelfKeyGrant model as ChangePayload writes it: camelCase, the permission as the
-- enum's camelCase name. The entry is dated now, when the row is written: a key's age would make a
-- replica that has not applied it yet look behind by that long. The one instant not from TimeProvider,
-- written in StoredInstant's form (SQLite gives milliseconds; the rest of the ticks are zero).
INSERT INTO change_log (entity_type, entity_id, action, payload, created_at)
SELECT
    'shelf_key_grant',
    id,
    'added',
    json_object(
        'id', id,
        'keyId', id,
        'shelfId', shelf_id,
        'permission', CASE permission WHEN 'add_only' THEN 'addOnly' WHEN 'read_write' THEN 'readWrite' ELSE 'read' END,
        'createdAt', created_at,
        'updatedAt', created_at),
    strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000Z'
FROM shelf_key
ORDER BY created_at, id;

-- Earlier key states lose the shelf and permission they carried, so replaying the log, or a whole copy
-- taken before the upgrade and still held, meets one shape: ShelfKey refuses a payload with either.
UPDATE change_log
SET payload = json_remove(payload, '$.shelfId', '$.permission')
WHERE entity_type = 'shelf_key' AND payload IS NOT NULL;

UPDATE replication_copy
SET payload = json_remove(payload, '$.shelfId', '$.permission')
WHERE entity_type = 'shelf_key';

-- Dropped in place rather than by rebuilding the table: dropping shelf_key would delete the links its
-- keys made (shelf_link cascades), and SQLite drops a column only once no index names it.
DROP INDEX idx_shelf_key_shelf;
ALTER TABLE shelf_key DROP COLUMN shelf_id;
ALTER TABLE shelf_key DROP COLUMN permission;

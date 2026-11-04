-- One-way replication: a primary pairs with replicas, which follow its change log and fetch its
-- objects. A server is a replica while it has a replication_source row.

-- The log is compacted rather than cut: an entry is dropped once it is old and a newer one for the
-- same entity exists, and a removal once it is old. So the newest entry of every entity that still
-- exists stays, and a whole copy is read from the log itself. A follower replaying the log across a
-- dropped entry could miss a removal or meet a row before the parent it names, so the highest
-- sequence dropped is kept: a follower behind it takes a copy.
CREATE INDEX idx_change_log_entity ON change_log (entity_type, entity_id, sequence);

CREATE TABLE change_log_state (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    pruned_through INTEGER NOT NULL
);

INSERT INTO change_log_state (singleton, pruned_through) VALUES (1, 0);

-- Who this server is, made the first time it is asked: a pairing code names the instance that made
-- it, so a server refuses its own code. A copied root keeps it, being the same instance's data.
CREATE TABLE server_instance (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    id TEXT NOT NULL
);

-- On a primary: a pairing code, kept as the hash of its secret until it is used or runs out.
CREATE TABLE replica_invite (
    id TEXT PRIMARY KEY,
    secret_sha256 TEXT NOT NULL,
    expires_at TEXT NOT NULL
);

CREATE UNIQUE INDEX idx_replica_invite_secret ON replica_invite (secret_sha256);

-- On a primary: each paired replica, the hash of the credential it presents, and how far it has
-- applied the log as it last said.
CREATE TABLE replica (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    credential_sha256 TEXT NOT NULL,
    paired_at TEXT NOT NULL,
    last_seen_at TEXT,
    applied_sequence INTEGER NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX idx_replica_credential ON replica (credential_sha256);

-- On a replica: the one primary it follows. The credential is kept as sent: this database already
-- holds everything the primary would answer with it. applied_fingerprint identifies the last entry
-- applied, so a primary restored from a backup, whose log holds other entries under those numbers,
-- is noticed; null after a copy until the next entry.
CREATE TABLE replication_source (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    primary_url TEXT NOT NULL,
    primary_instance_id TEXT NOT NULL,
    credential TEXT NOT NULL,
    paired_at TEXT NOT NULL,
    applied_sequence INTEGER NOT NULL DEFAULT 0,
    applied_fingerprint TEXT,
    needs_copy INTEGER NOT NULL DEFAULT 1,      -- 1: take a whole copy before following the log
    last_synced_at TEXT,
    last_error TEXT,
    last_error_at TEXT
);

-- On a replica taking a whole copy: the primary's entities as read so far, held until all of them
-- (and their objects) are here and they are applied in one transaction.
CREATE TABLE replication_copy (
    sequence INTEGER PRIMARY KEY,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    action TEXT NOT NULL,
    payload TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX idx_replication_copy_entity ON replication_copy (entity_type, entity_id);

-- The replication problems this server has emailed its admins about and not yet seen clear, so a
-- restart neither sends the start of one again nor loses the email saying it ended. One row a
-- subject: a replica, on a primary ('replica:<id>'), or the primary, on a replica ('primary'). Each
-- server keeps its own; it is not in the change log.
CREATE TABLE replication_alert (
    subject TEXT PRIMARY KEY,
    problem TEXT NOT NULL,
    since TEXT NOT NULL,
    last_sent_at TEXT NOT NULL
);

-- On a primary: whether the replica's last request was for a page of a whole copy, so it is not
-- counted behind for the changes the copy brings it.
ALTER TABLE replica ADD COLUMN copying INTEGER NOT NULL DEFAULT 0;

-- On a replica: when the steps that have failed since the last one that worked began failing; null
-- while the last step worked.
ALTER TABLE replication_source ADD COLUMN failing_since TEXT;

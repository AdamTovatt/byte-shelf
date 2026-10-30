-- Shared background-job run history. Every tracked job records a row per pass: when it started
-- and finished, whether it succeeded, a short human-readable summary, and any error.

CREATE TABLE job_run (
    id TEXT PRIMARY KEY,
    job_name TEXT NOT NULL,            -- stable identifier, see JobNames
    started_at TEXT NOT NULL,
    completed_at TEXT,                 -- NULL while running / if interrupted
    success INTEGER,                   -- NULL until the run finishes
    result_summary TEXT,               -- short JSON outcome (counts etc.)
    error_message TEXT,                -- failure reason when success = 0
    -- 1. un-finalised iff both completed_at and success are NULL;
    -- 2. a successful run never carries an error_message;
    -- 3. a failed run always carries a non-empty one.
    CONSTRAINT job_run_status_consistent CHECK (
        ((completed_at IS NULL) = (success IS NULL))
        AND (success IS NOT 1 OR error_message IS NULL)
        AND (success IS NOT 0 OR (error_message IS NOT NULL AND length(error_message) > 0))
    ),
    -- A completed run must not finish before it started (StoredInstant text orders as instants do).
    CONSTRAINT job_run_completion_after_start CHECK (completed_at IS NULL OR completed_at >= started_at)
);

-- Latest-run-per-job lookups order by started_at DESC.
CREATE INDEX idx_job_run_name_started_at ON job_run (job_name, started_at DESC);

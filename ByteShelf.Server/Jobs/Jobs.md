# Jobs

Background work that runs on a schedule.

## Writing a job

Derive from `PeriodicJobWorker<TSummary>`: give it a name from `JobNames` (`domain.job`), an
interval, and the work for one pass. It runs at startup and then the interval after each pass ends,
each pass in a scope of its own. A pass's summary is logged at Debug; a failed pass is logged as an
error, and the next one runs on schedule. The jobs are `storage.maintenance` (hourly),
`users.expired_token_cleanup` (daily) and `change_log.compaction` (daily).

Nothing records runs in the database: the log is the record. A job that a person needs to see the
history of would add a table together with something that reads and prunes it.

# Jobs

Run history for background work, so the `job_run` table tells what ran, when, and how it ended.

## Writing a job

Derive from `PeriodicJobWorker<TSummary>`: give it a name from `JobNames` (`domain.job`), an
interval, and the work for one pass. It runs at startup and then after every interval, each pass in
a scope of its own and recorded through `IJobRunRecorder`; a failed pass is recorded and logged, and
the next one runs on schedule. The jobs are `storage.maintenance` (hourly) and
`users.expired_token_cleanup` (daily).

## Recording

`IJobRunRecorder.RunAsync` inserts a start row, runs the work, and completes the row from the
outcome. A thrown exception completes it as a failure with the message and is rethrown; a
cancellation by the shutdown token completes it with the neutral
`JobRunSentinels.ShutdownErrorMessage` instead.

`JobRunStartupSweeper` closes rows a killed process left open, at boot and before any job records.

The table's CHECK constraints encode the status shape (see the job_run migration), so any other
writer stays honest too.

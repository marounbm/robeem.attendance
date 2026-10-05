# Attendance P10 — scheduled sync, resilience and reconciliation: notes

## What Prompt 10 added

- **Scheduler decision (CLAUDE.md §2.8):** no Hangfire. One `ScheduledJobsHostedService` in `robeem.Api` runs the `IScheduledJob`s in `src/robeem.Api/Attendance/Jobs`. It replaces the P5 stand-in `AttendanceNormalizationHostedService`, which is deleted; its work is now `NormalizationJob`. The reasons, and what the choice gives up (single Api instance, no replay of missed runs, in-memory breaker state), are recorded in §2.8.
- **Jobs:** `NormalizationJob` (every `Attendance:Normalization:PollIntervalSeconds`), `IntegrationSyncJob` (ticks every `Attendance:Sync:TickSeconds`; each integration runs when its own `PollingScheduleCron` is due) and `ReconciliationJob` (cron `Attendance:Sync:ReconciliationCron`, default 02:00). Each walks `ITenantRegistry.Tenants` inside its own `ITenantScopeFactory.CreateScope`; a failing tenant or integration never stops the others.
- **Application services:** `IIntegrationSyncService` / `IntegrationSyncService` (`SyncAsync`, `ReconcileAsync`, `ListPollingIntegrationsAsync`), `IntegrationResilience` (a Polly pipeline and breaker per tenant + integration), `CronSchedule` (Cronos), `RawBatchWriter` (the storage step now shared with manual upload, extracted from `IngestionService`) and `AttendanceSyncOptions`.
- **Adapter contract:** `AttendanceIntegrationConfig.SyncOverlap` carries the overlap (default 5 minutes). The cursor is the vendor's own and opaque, so the **adapter** subtracts the overlap in its own terms. `GetTransactionChangesAsync` throwing `NotSupportedException` means "no incremental sync": the integration is skipped, not counted as a failure (the generic CSV adapter does this).
- **No migration.** `AttendanceIntegration` already had `SyncCursor`, `LastSuccessfulSyncAt` and `HealthStatus` (P1).

## Rules later prompts must keep

- **The cursor moves only after the raw rows are committed.** Batch, rows, cursor and `LastSuccessfulSyncAt` are staged in that order; the cursor is saved in a second `SaveChanges`, after the batch's. If that save fails, the next sync reads the same window again and duplicates absorb it.
- **Retry and the breaker wrap the provider call only.** A storage failure says nothing about the vendor, so it records a failed batch but does not count towards the breaker.
- **The breaker is the outer layer**: one failed sync, after all its retries, is one sample. `BreakerFailureThreshold` counts syncs, not attempts. A success between failures keeps it closed.
- **Health:** a failed sync sets `Degraded`, `Unhealthy` once the breaker has opened (and it stays so until a sync succeeds); a success sets `Healthy`. An open breaker is `CircuitOpen`, with no provider call and no new failed batch.
- **Every failure is a `Failed` `AttendanceProcessingBatch`** with the reason in `ErrorSummary` (truncated to 1000 characters). P13 can build its health KPIs from these and from `HealthStatus`.
- **Reconciliation never touches the cursor or `LastSuccessfulSyncAt`.** It pulls `ReconciliationWindowHours` (default 48) back from now, only for integrations that have synced before, and relies on duplicate detection (§1.4) for the overlap.

## Things to know

- **The study was not available**, so the defaults (5 minutes of overlap, 48 hours of reconciliation, three failures to open, a five-minute break) are the prompt's, with the retry count (3) and delay (2 s, doubling, jittered) my choice. All are configuration in `Attendance:Sync`.
- **Polly limits:** a breaker needs at least 2 samples and a 0.5 s break; `IntegrationResilience` clamps to those.
- **No distributed lock.** Run one Api instance (§2.8).
- **Cron is evaluated in server local time**, with five fields, or six with seconds first. A bad cron on an integration is logged once and the integration is not synced; a bad one on the reconciliation job stops that job at startup, with an error in the log.
- A failed sync is not retried until the next cron time (the job remembers its last attempt); after a restart, the last success is the reference.

## Tests

`IntegrationSyncServiceTests` and `CronScheduleTests` (22 cases). The three the prompt asks for:

- a successful sync advances the cursor (`SuccessfulSyncAdvancesTheCursor`, with the overlap and stored cursor passed to the adapter);
- a failing adapter leaves the cursor unchanged, retries, and flips `HealthStatus` (`FailedSync…`, `FailuresInARow…` for `Unhealthy` and the short circuit);
- a retried sync that re-delivers overlapping punches lands as `DuplicateConfirmed`, and the day's record counts 9 hours once, not 18 (`RetriedSyncRedeliveringOverlap…`).

The rest covers: a storage failure leaves the cursor alone, `NotSupportedException` skips, a paused integration is skipped, the polling list order, reconciliation's window and untouched cursor, and the cron helper.
